using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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

            StopCommandSurrogates(cancellationToken);
            foreach (var slot in toAdd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packagePath = Path.Combine(cacheDirectory, CommandSlotPlanner.CommandPackageFileName(slot));
                AddPackage(packagePath, cancellationToken);
            }

            foreach (var fullName in toRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemovePackage(fullName, cancellationToken);
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
        var namePattern = new Regex(
            "^" + Regex.Escape(mainPackageName) + @"\.Command(0[0-9]|1[0-5])$",
            RegexOptions.CultureInvariant);
        foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
        {
            if (!string.Equals(package.Id.Publisher, publisher, StringComparison.Ordinal))
            {
                continue;
            }

            var match = namePattern.Match(package.Id.Name);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var slot))
            {
                continue;
            }

            installed[slot] = package.Id.FullName;
        }

        return installed;
    }

    private static void AddPackage(string packagePath, CancellationToken cancellationToken)
    {
        RunPowerShell(
            $"Add-AppxPackage -Path '{EscapePowerShellLiteral(packagePath)}' -ForceApplicationShutdown -ForceUpdateFromAnyVersion",
            cancellationToken);
    }

    private static void RemovePackage(string packageFullName, CancellationToken cancellationToken)
    {
        RunPowerShell(
            $"Remove-AppxPackage -Package '{EscapePowerShellLiteral(packageFullName)}'",
            cancellationToken);
    }

    private static void StopCommandSurrogates(CancellationToken cancellationToken)
    {
        var classIds = string.Join(
            ",",
            Enumerable.Range(0, SettingsContract.MaxMultiDirectAgents)
                .Select(slot => $"'F7E08D{0x6D + slot:X2}-676E-4D4B-950A-5B4451E19E3C'"));
        var command =
            "$classIds = @(" + classIds + "); " +
            "$pattern = ($classIds | ForEach-Object { [Regex]::Escape($_) }) -join '|'; " +
            "Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.Name -ieq 'dllhost.exe' -and $_.CommandLine -match $pattern } | " +
            "ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }";
        try
        {
            RunPowerShell(command, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Explorer may still hold a surrogate; package add/remove can continue.
        }
    }

    private static void RunPowerShell(string command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Windows PowerShell.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            while (!process.WaitForExit(200))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode == 0)
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"Windows PowerShell exited with code {process.ExitCode}."
            : detail.Trim());
    }

    private static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(ShellAssociationChanged, ShellNotifyIdList, IntPtr.Zero, IntPtr.Zero);
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
