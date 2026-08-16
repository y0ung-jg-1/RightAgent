namespace RightAgent.Core;

public enum CommandPackageSyncResult
{
    Unchanged,
    Refreshed,
    Skipped
}

/// <summary>
/// The OS surface the occupancy synchronizer runs on. The default
/// implementation talks to PackageManager; tests substitute a fake.
/// </summary>
public interface ICommandPackageDeployment
{
    bool TryGetCurrentPackageIdentity(out string mainPackageName, out string publisher);

    Dictionary<int, string> FindInstalledCommandSlots(string mainPackageName, string publisher);

    /// <summary>Returns a handle that releases the mutex on dispose, or null when acquisition timed out.</summary>
    IDisposable? TryAcquireInstallationMutex(TimeSpan timeout);

    void AddPackage(string packagePath, CancellationToken cancellationToken);

    void RemovePackage(string packageFullName, CancellationToken cancellationToken);

    void NotifyShellAssociationsChanged();
}

public sealed class CommandPackageSynchronizer
{
    public const string InstallationMutexName = @"Global\RightAgent.Setup";
    private static readonly TimeSpan InstallationMutexTimeout = TimeSpan.FromSeconds(15);

    private static readonly CommandPackageSynchronizer Shared = new(WindowsCommandPackageDeployment.Shared);
    private readonly ICommandPackageDeployment deployment;

    public CommandPackageSynchronizer(ICommandPackageDeployment deployment)
    {
        this.deployment = deployment;
    }

    public static Task<CommandPackageSyncResult> SynchronizeAsync(
        RightAgentSettings settings,
        string localStateDirectory,
        CancellationToken cancellationToken = default)
    {
        return Shared.RunAsync(settings, localStateDirectory, cancellationToken);
    }

    public Task<CommandPackageSyncResult> RunAsync(
        RightAgentSettings settings,
        string localStateDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Named Mutex ownership is thread-affine. Keep WaitOne/ReleaseMutex and the
        // package work on one worker so an await cannot hop to another thread.
        return Task.Run(() => Synchronize(settings, localStateDirectory, cancellationToken), cancellationToken);
    }

    private CommandPackageSyncResult Synchronize(
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
        var stampPath = Path.Combine(localStateDirectory, "command-slots.refreshed");

        // List/plan/apply must share one mutex so a later snapshot cannot write
        // a stamp from a stale list while an earlier add/remove still runs.
        var mutexHandle = deployment.TryAcquireInstallationMutex(InstallationMutexTimeout);
        if (mutexHandle is null)
        {
            return CommandPackageSyncResult.Skipped;
        }

        var applied = false;
        using (mutexHandle)
        {
            var installed = deployment.FindInstalledCommandSlots(mainPackageName, publisher);
            var plan = CommandSlotPlanner.Plan(
                requiredSlots,
                installed,
                slot => CommandSlotPlanner.CachedPackageExists(cacheDirectory, slot));
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

            foreach (var slot in plan.SlotsToAdd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packagePath = Path.Combine(cacheDirectory, CommandSlotPlanner.CommandPackageFileName(slot));
                deployment.AddPackage(packagePath, cancellationToken);
            }

            foreach (var fullName in plan.PackagesToRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();
                deployment.RemovePackage(fullName, cancellationToken);
            }

            applied = true;
            var installedAfter = deployment.FindInstalledCommandSlots(mainPackageName, publisher);
            var requiredInstalled = Enumerable.Range(0, requiredSlots).Count(slot => installedAfter.ContainsKey(slot));
            if (requiredInstalled >= requiredSlots)
            {
                File.WriteAllText(stampPath, requiredSlots.ToString());
            }
        }

        if (applied)
        {
            deployment.NotifyShellAssociationsChanged();
        }

        return CommandPackageSyncResult.Refreshed;
    }

    private bool TryGetCommandPackageIdentity(string localStateDirectory, out string mainPackageName, out string publisher)
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

        return deployment.TryGetCurrentPackageIdentity(out mainPackageName, out publisher);
    }
}
