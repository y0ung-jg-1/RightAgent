using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace RightAgent.Core;

public sealed class WindowsCommandPackageDeployment : ICommandPackageDeployment
{
    public static WindowsCommandPackageDeployment Shared { get; } = new();

    private static readonly TimeSpan PackageOperationTimeout = TimeSpan.FromSeconds(30);
    private const uint ShellAssociationChanged = 0x08000000;
    private const uint ShellNotifyIdList = 0x0000;

    private readonly string mutexName;

    public WindowsCommandPackageDeployment(string mutexName = CommandPackageSynchronizer.InstallationMutexName)
    {
        this.mutexName = mutexName;
    }

    public bool TryGetCurrentPackageIdentity(out string mainPackageName, out string publisher)
    {
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

    public Dictionary<int, string> FindInstalledCommandSlots(string mainPackageName, string publisher)
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
                // A failing slot query counts as "not installed"; the plan then
                // simply re-adds that slot on the next pass.
            }
        }

        return installed;
    }

    public IDisposable? TryAcquireInstallationMutex(TimeSpan timeout)
    {
        var mutex = new Mutex(false, mutexName);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died; this wait still owns the mutex.
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return null;
        }

        return new MutexReleaser(mutex);
    }

    public void AddPackage(string packagePath, CancellationToken cancellationToken)
    {
        var packageManager = new PackageManager();
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

    public void RemovePackage(string packageFullName, CancellationToken cancellationToken)
    {
        var packageManager = new PackageManager();
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

    public void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(ShellAssociationChanged, ShellNotifyIdList, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    private sealed class MutexReleaser(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
