using System.Runtime.InteropServices;
using RightAgent.Core;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace RightAgent.App.Services;

internal enum CommandPackageSyncResult
{
    Unchanged,
    Refreshed,
    Skipped
}

internal static class CommandPackageSynchronizer
{
    private const string InstallationMutexName = @"Local\RightAgent.PackageInstallation";
    private const uint ShellAssociationChanged = 0x08000000;
    private const uint ShellNotifyIdList = 0x0000;

    public static Task<CommandPackageSyncResult> SynchronizeAsync(
        RightAgentSettings settings,
        string localStateDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Named Mutex ownership is thread-affine. Keep WaitOne/ReleaseMutex and the
        // package work on one worker so an await cannot hop to another thread.
        return Task.Run(() => Synchronize(settings, localStateDirectory, cancellationToken), cancellationToken);
    }

    private static CommandPackageSyncResult Synchronize(
        RightAgentSettings settings,
        string localStateDirectory,
        CancellationToken cancellationToken)
    {
        if (!TryGetCommandPackageIdentity(out var mainPackageName, out var publisher))
        {
            return CommandPackageSyncResult.Skipped;
        }

        var cacheDirectory = Path.Combine(localStateDirectory, CommandSlotPlanner.CommandPackageCacheDirectoryName);
        var requiredSlots = CommandSlotPlanner.RequiredSlotCount(settings);
        var installed = ListInstalledCommandSlots(mainPackageName, publisher);
        var toAdd = new List<int>();
        var toRemove = new List<string>();
        for (var slot = 0; slot < SettingsContract.MaxMultiDirectAgents; ++slot)
        {
            var isInstalled = installed.TryGetValue(slot, out var fullName);
            if (slot < requiredSlots && !isInstalled)
            {
                if (CommandSlotPlanner.CachedPackageExists(cacheDirectory, slot))
                {
                    toAdd.Add(slot);
                }
            }
            else if (slot >= requiredSlots && isInstalled && !string.IsNullOrWhiteSpace(fullName))
            {
                toRemove.Add(fullName);
            }
        }

        var missingAdds = Enumerable.Range(0, requiredSlots).Any(slot =>
            !installed.ContainsKey(slot) && !CommandSlotPlanner.CachedPackageExists(cacheDirectory, slot));
        var stampPath = Path.Combine(localStateDirectory, "command-slots.refreshed");
        var stampMatches = File.Exists(stampPath)
            && string.Equals(File.ReadAllText(stampPath).Trim(), requiredSlots.ToString(), StringComparison.Ordinal);
        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            if (missingAdds)
            {
                return CommandPackageSyncResult.Skipped;
            }

            if (!stampMatches)
            {
                File.WriteAllText(stampPath, requiredSlots.ToString());
            }

            return CommandPackageSyncResult.Unchanged;
        }

        using var mutex = new Mutex(false, InstallationMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                return CommandPackageSyncResult.Skipped;
            }

            var packageManager = new PackageManager();
            foreach (var slot in toAdd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packagePath = Path.Combine(cacheDirectory, CommandSlotPlanner.CommandPackageFileName(slot));
                AddPackage(packageManager, packagePath, cancellationToken);
            }

            foreach (var fullName in toRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemovePackage(packageManager, fullName, cancellationToken);
            }
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }

        NotifyShellAssociationsChanged();
        var installedAfter = ListInstalledCommandSlots(mainPackageName, publisher);
        var requiredInstalled = Enumerable.Range(0, requiredSlots).Count(slot => installedAfter.ContainsKey(slot));
        if (requiredInstalled >= requiredSlots)
        {
            File.WriteAllText(stampPath, requiredSlots.ToString());
        }
        return CommandPackageSyncResult.Refreshed;
    }

    private static bool TryGetCommandPackageIdentity(out string mainPackageName, out string publisher)
    {
        var record = InstallRecord.TryLoad();
        if (record is not null &&
            !string.IsNullOrWhiteSpace(record.PackageName) &&
            !string.IsNullOrWhiteSpace(record.Publisher))
        {
            mainPackageName = record.PackageName;
            publisher = record.Publisher;
            return true;
        }

        try
        {
            var id = Package.Current.Id;
            mainPackageName = id.Name;
            publisher = id.Publisher;
            return !string.IsNullOrWhiteSpace(mainPackageName) && !string.IsNullOrWhiteSpace(publisher);
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            mainPackageName = string.Empty;
            publisher = string.Empty;
            return false;
        }
    }

    private static Dictionary<int, string> ListInstalledCommandSlots(string mainPackageName, string publisher)
    {
        var installed = new Dictionary<int, string>();
        var prefix = mainPackageName + ".Command";
        foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
        {
            if (!string.Equals(package.Id.Publisher, publisher, StringComparison.Ordinal))
            {
                continue;
            }

            if (!package.Id.Name.StartsWith(prefix, StringComparison.Ordinal)
                || package.Id.Name.Length != prefix.Length + 2)
            {
                continue;
            }

            var slotText = package.Id.Name[prefix.Length..];
            if (!int.TryParse(slotText, out var slot) || slot < 0 || slot >= SettingsContract.MaxMultiDirectAgents)
            {
                continue;
            }

            installed[slot] = package.Id.FullName;
        }

        return installed;
    }

    private static void AddPackage(PackageManager packageManager, string packagePath, CancellationToken cancellationToken)
    {
        var options = new AddPackageOptions
        {
            ForceAppShutdown = true,
            ForceUpdateFromAnyVersion = true
        };
        var result = packageManager.AddPackageByUriAsync(new Uri(packagePath), options)
            .AsTask(cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (!result.IsRegistered || result.ExtendedErrorCode is not null)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.ErrorText)
                ? $"Could not register '{packagePath}'."
                : result.ErrorText);
        }
    }

    private static void RemovePackage(PackageManager packageManager, string packageFullName, CancellationToken cancellationToken)
    {
        var result = packageManager.RemovePackageAsync(packageFullName)
            .AsTask(cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (result.ExtendedErrorCode is not null)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.ErrorText)
                ? $"Could not remove '{packageFullName}'."
                : result.ErrorText);
        }
    }

    private static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(ShellAssociationChanged, ShellNotifyIdList, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
