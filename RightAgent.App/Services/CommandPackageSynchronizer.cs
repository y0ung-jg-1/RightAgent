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
    internal const string InstallationMutexName = @"Global\RightAgent.Setup";
    private static readonly TimeSpan PackageOperationTimeout = TimeSpan.FromSeconds(30);
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
        if (!TryGetCommandPackageIdentity(localStateDirectory, out var mainPackageName, out var publisher))
        {
            return CommandPackageSyncResult.Skipped;
        }

        var cacheDirectory = Path.Combine(localStateDirectory, CommandSlotPlanner.CommandPackageCacheDirectoryName);
        var requiredSlots = CommandSlotPlanner.RequiredSlotCount(settings);
        var installed = ListInstalledCommandSlots(mainPackageName, publisher);
        var plan = CommandSlotPlanner.Plan(
            requiredSlots,
            installed,
            slot => CommandSlotPlanner.CachedPackageExists(cacheDirectory, slot));
        var stampPath = Path.Combine(localStateDirectory, "command-slots.refreshed");
        var stampMatches = File.Exists(stampPath)
            && string.Equals(File.ReadAllText(stampPath).Trim(), requiredSlots.ToString(), StringComparison.Ordinal);
        if (plan.SlotsToAdd.Count == 0 && plan.PackagesToRemove.Count == 0)
        {
            if (plan.CacheMissingRequiredSlots)
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
            foreach (var slot in plan.SlotsToAdd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packagePath = Path.Combine(cacheDirectory, CommandSlotPlanner.CommandPackageFileName(slot));
                AddPackage(packageManager, packagePath, cancellationToken);
            }

            foreach (var fullName in plan.PackagesToRemove)
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

    private static bool TryGetCommandPackageIdentity(string localStateDirectory, out string mainPackageName, out string publisher)
    {
        var record = InstallRecord.TryLoad(localStateDirectory);
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
        // Query the 16 known command identities. FindPackagesForUser("") walks
        // every package on the machine and has hung or torn down this process.
        var installed = new Dictionary<int, string>();
        var packageManager = new PackageManager();
        for (var slot = 0; slot < SettingsContract.MaxMultiDirectAgents; ++slot)
        {
            var name = mainPackageName + ".Command" + slot.ToString("D2");
            try
            {
                foreach (var package in packageManager.FindPackagesForUser(string.Empty, name, publisher))
                {
                    installed[slot] = package.Id.FullName;
                    break;
                }
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException or ArgumentException)
            {
            }
        }

        return installed;
    }

    private static void AddPackage(PackageManager packageManager, string packagePath, CancellationToken cancellationToken)
    {
        var options = new AddPackageOptions
        {
            ForceUpdateFromAnyVersion = true
        };
        var result = WaitForDeployment(
            packageManager.AddPackageByUriAsync(new Uri(Path.GetFullPath(packagePath)), options),
            cancellationToken);
        if (!result.IsRegistered || result.ExtendedErrorCode is not null)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.ErrorText)
                ? $"Could not register '{packagePath}'."
                : result.ErrorText);
        }
    }

    private static void RemovePackage(PackageManager packageManager, string packageFullName, CancellationToken cancellationToken)
    {
        var result = WaitForDeployment(packageManager.RemovePackageAsync(packageFullName), cancellationToken);
        if (result.ExtendedErrorCode is not null)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.ErrorText)
                ? $"Could not remove '{packageFullName}'."
                : result.ErrorText);
        }
    }

    private static DeploymentResult WaitForDeployment(
        Windows.Foundation.IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation,
        CancellationToken cancellationToken)
    {
        var task = operation.AsTask(cancellationToken);
        if (!task.Wait(PackageOperationTimeout))
        {
            operation.Cancel();
            throw new TimeoutException("A command-package deployment operation timed out.");
        }

        return task.GetAwaiter().GetResult();
    }

    private static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(ShellAssociationChanged, ShellNotifyIdList, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
