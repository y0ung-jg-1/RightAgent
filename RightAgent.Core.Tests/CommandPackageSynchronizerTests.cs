using RightAgent.Core;
using Xunit;

namespace RightAgent.Core.Tests;

public sealed class CommandPackageSynchronizerTests
{
    private const string Slot2FullName = "RightAgent.Command02_1.0.0.0_x64__pub";

    [Fact]
    public async Task SkipsWhenNoIdentityResolves()
    {
        var root = CreateTempRoot();
        try
        {
            var deployment = new FakeDeployment { HasCurrentIdentity = false };
            var synchronizer = new CommandPackageSynchronizer(deployment);

            var result = await synchronizer.RunAsync(TwoAgentMultiDirect(), root);

            Assert.Equal(CommandPackageSyncResult.Skipped, result);
            Assert.Equal(0, deployment.ListCalls);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task WritesStampAndReturnsUnchangedWhenOccupancyMatches()
    {
        var root = CreateTempRoot();
        try
        {
            WriteInstallRecord(root);
            WriteCachedPackages(root, 0, 1);
            var deployment = new FakeDeployment().Snapshot(new Dictionary<int, string>
            {
                [0] = "RightAgent.Command00_1.0.0.0_x64__pub",
                [1] = "RightAgent.Command01_1.0.0.0_x64__pub"
            });
            var synchronizer = new CommandPackageSynchronizer(deployment);

            var result = await synchronizer.RunAsync(TwoAgentMultiDirect(), root);

            Assert.Equal(CommandPackageSyncResult.Unchanged, result);
            Assert.Equal("2", File.ReadAllText(Path.Combine(root, "command-slots.refreshed")).Trim());
            Assert.Empty(deployment.AddedPackages);
            Assert.Empty(deployment.RemovedPackages);
            Assert.Equal(0, deployment.NotifyCalls);
            Assert.Equal(1, deployment.AcquireCalls);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SkipsWithoutStampWhenRequiredCacheIsMissing()
    {
        var root = CreateTempRoot();
        try
        {
            WriteInstallRecord(root);
            var deployment = new FakeDeployment().Snapshot([]);
            var synchronizer = new CommandPackageSynchronizer(deployment);

            var result = await synchronizer.RunAsync(TwoAgentMultiDirect(), root);

            Assert.Equal(CommandPackageSyncResult.Skipped, result);
            Assert.False(File.Exists(Path.Combine(root, "command-slots.refreshed")));
            Assert.Equal(1, deployment.AcquireCalls);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task AddsMissingRemovesExtraAndWritesStamp()
    {
        var root = CreateTempRoot();
        try
        {
            WriteInstallRecord(root);
            WriteCachedPackages(root, 0, 1, 2);
            var deployment = new FakeDeployment()
                .Snapshot(new Dictionary<int, string>
                {
                    [0] = "RightAgent.Command00_1.0.0.0_x64__pub",
                    [2] = Slot2FullName
                })
                .Snapshot(new Dictionary<int, string>
                {
                    [0] = "RightAgent.Command00_1.0.0.0_x64__pub",
                    [1] = "RightAgent.Command01_1.0.0.0_x64__pub"
                });
            var synchronizer = new CommandPackageSynchronizer(deployment);

            var result = await synchronizer.RunAsync(TwoAgentMultiDirect(), root);

            Assert.Equal(CommandPackageSyncResult.Refreshed, result);
            var expected = Path.Combine(root, "CommandPackages", "01.msix");
            Assert.Equal([expected], deployment.AddedPackages);
            Assert.Equal([Slot2FullName], deployment.RemovedPackages);
            Assert.Equal(1, deployment.NotifyCalls);
            Assert.Equal("2", File.ReadAllText(Path.Combine(root, "command-slots.refreshed")).Trim());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task DoesNotWriteStampWhenSlotsAreStillMissingAfterSync()
    {
        var root = CreateTempRoot();
        try
        {
            WriteInstallRecord(root);
            WriteCachedPackages(root, 0, 1, 2);
            var deployment = new FakeDeployment()
                .Snapshot(new Dictionary<int, string> { [2] = Slot2FullName })
                .Snapshot([]);
            var synchronizer = new CommandPackageSynchronizer(deployment);

            var result = await synchronizer.RunAsync(TwoAgentMultiDirect(), root);

            Assert.Equal(CommandPackageSyncResult.Refreshed, result);
            Assert.False(File.Exists(Path.Combine(root, "command-slots.refreshed")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task PropagatesAddFailureAndReleasesMutex()
    {
        var root = CreateTempRoot();
        try
        {
            WriteInstallRecord(root);
            WriteCachedPackages(root, 0, 1);
            var deployment = new FakeDeployment
            {
                AddPackageError = new InvalidOperationException("deployment refused")
            }.Snapshot(new Dictionary<int, string> { [0] = "RightAgent.Command00_1.0.0.0_x64__pub" });
            var synchronizer = new CommandPackageSynchronizer(deployment);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => synchronizer.RunAsync(TwoAgentMultiDirect(), root));

            Assert.NotNull(deployment.MutexHandle);
            Assert.True(((FakeDeployment.FakeHandle)deployment.MutexHandle!).Disposed);
            Assert.Equal(0, deployment.NotifyCalls);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SkipsWhenMutexTimesOut()
    {
        var root = CreateTempRoot();
        try
        {
            WriteInstallRecord(root);
            WriteCachedPackages(root, 0, 1);
            var deployment = new FakeDeployment
            {
                MutexHandle = null
            }.Snapshot(new Dictionary<int, string> { [0] = "RightAgent.Command00_1.0.0.0_x64__pub" });
            var synchronizer = new CommandPackageSynchronizer(deployment);

            var result = await synchronizer.RunAsync(TwoAgentMultiDirect(), root);

            Assert.Equal(CommandPackageSyncResult.Skipped, result);
            Assert.Empty(deployment.AddedPackages);
            Assert.Equal(0, deployment.NotifyCalls);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task OverlappingRunsLeaveInstalledSlotsMatchingTheStamp()
    {
        var root = CreateTempRoot();
        try
        {
            WriteInstallRecord(root);
            WriteCachedPackages(root, 0, 1);
            var deployment = new LiveDeployment
            {
                AddDelay = TimeSpan.FromMilliseconds(150)
            };
            var synchronizer = new CommandPackageSynchronizer(deployment);

            var twoSlots = synchronizer.RunAsync(TwoAgentMultiDirect(), root);
            await Task.Delay(40);
            var oneSlot = synchronizer.RunAsync(OneAgentGrouped(), root);
            await Task.WhenAll(twoSlots, oneSlot);

            var installed = deployment.Snapshot();
            Assert.Contains(installed.Count, (int[])[1, 2]);
            Assert.Equal(Enumerable.Range(0, installed.Count), installed.Keys.OrderBy(slot => slot));
            Assert.Equal(
                installed.Count.ToString(),
                File.ReadAllText(Path.Combine(root, "command-slots.refreshed")).Trim());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void AbandonedMutexIsStillAcquired()
    {
        var name = @"Local\RightAgent.Tests.Abandoned." + Guid.NewGuid().ToString("N");
        var holder = new Thread(() =>
        {
            var mutex = new Mutex(false, name);
            mutex.WaitOne();
            // The thread exits while holding the mutex; it becomes abandoned.
        });
        holder.Start();
        holder.Join();

        var deployment = new WindowsCommandPackageDeployment(name);
        using var handle = deployment.TryAcquireInstallationMutex(TimeSpan.FromSeconds(5));

        Assert.NotNull(handle);
    }

    [Fact]
    public void InstallRecordRoundTripsAndDetectsMissingApp()
    {
        var root = CreateTempRoot();
        try
        {
            var record = new InstallRecord
            {
                PackageName = SettingsContract.ReleasePackageName,
                Publisher = SettingsContract.ReleasePublisher,
                AppPath = Path.Combine(root, "missing", "RightAgent.App.exe"),
                Version = "1.1.4.0"
            };
            record.Save(root);

            var loaded = InstallRecord.TryLoad(root);
            Assert.NotNull(loaded);
            Assert.Equal(SettingsContract.ReleasePackageName, loaded!.PackageName);
            Assert.Equal(SettingsContract.ReleasePublisher, loaded.Publisher);
            Assert.Equal(record.AppPath, loaded.AppPath);
            Assert.Equal("1.1.4.0", loaded.Version);
            Assert.False(loaded.AppExists);
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static RightAgentSettings OneAgentGrouped() => new()
    {
        MenuMode = SettingsContract.GroupedMenu,
        MenuEnabled = true,
        DirectAgentId = "a",
        Agents =
        [
            new AgentDefinition
            {
                Id = "a",
                Name = "A",
                Enabled = true,
                Sort = 0,
                Action = new AgentAction { Type = SettingsContract.TerminalCommand, Value = "cmd-a" }
            }
        ]
    };

    private static RightAgentSettings TwoAgentMultiDirect() => new()
    {
        MenuMode = SettingsContract.MultiDirectMenu,
        MenuEnabled = true,
        DirectAgentId = "a",
        Agents =
        [
            new AgentDefinition
            {
                Id = "a",
                Name = "A",
                Enabled = true,
                Sort = 0,
                Action = new AgentAction { Type = SettingsContract.TerminalCommand, Value = "cmd-a" }
            },
            new AgentDefinition
            {
                Id = "b",
                Name = "B",
                Enabled = true,
                Sort = 1,
                Action = new AgentAction { Type = SettingsContract.TerminalCommand, Value = "cmd-b" }
            }
        ]
    };

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RightAgent.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteInstallRecord(string root)
    {
        new InstallRecord
        {
            PackageName = SettingsContract.ReleasePackageName,
            Publisher = SettingsContract.ReleasePublisher,
            AppPath = Path.Combine(root, "RightAgent.App.exe"),
            Version = "1.0.0.0"
        }.Save(root);
    }

    private static void WriteCachedPackages(string root, params int[] slots)
    {
        var cache = Path.Combine(root, CommandSlotPlanner.CommandPackageCacheDirectoryName);
        Directory.CreateDirectory(cache);
        foreach (var slot in slots)
        {
            File.WriteAllText(Path.Combine(cache, CommandSlotPlanner.CommandPackageFileName(slot)), "package");
        }
    }

    private sealed class FakeDeployment : ICommandPackageDeployment
    {
        private readonly Queue<Dictionary<int, string>> installedSnapshots = new();

        public bool HasCurrentIdentity { get; set; } = true;

        public IDisposable? MutexHandle { get; set; } = new FakeHandle();

        public Exception? AddPackageError { get; set; }

        public int AcquireCalls { get; private set; }

        public int ListCalls { get; private set; }

        public int NotifyCalls { get; private set; }

        public List<string> AddedPackages { get; } = [];

        public List<string> RemovedPackages { get; } = [];

        public FakeDeployment Snapshot(Dictionary<int, string> installed)
        {
            installedSnapshots.Enqueue(installed);
            return this;
        }

        public bool TryGetCurrentPackageIdentity(out string mainPackageName, out string publisher)
        {
            mainPackageName = SettingsContract.ReleasePackageName;
            publisher = SettingsContract.ReleasePublisher;
            return HasCurrentIdentity;
        }

        public Dictionary<int, string> FindInstalledCommandSlots(string mainPackageName, string publisher)
        {
            ListCalls++;
            return installedSnapshots.Count > 0 ? installedSnapshots.Dequeue() : [];
        }

        public IDisposable? TryAcquireInstallationMutex(TimeSpan timeout)
        {
            AcquireCalls++;
            return MutexHandle;
        }

        public void AddPackage(string packagePath, CancellationToken cancellationToken)
        {
            if (AddPackageError is not null)
            {
                throw AddPackageError;
            }
            AddedPackages.Add(packagePath);
        }

        public void RemovePackage(string packageFullName, CancellationToken cancellationToken)
        {
            RemovedPackages.Add(packageFullName);
        }

        public void NotifyShellAssociationsChanged()
        {
            NotifyCalls++;
        }

        public sealed class FakeHandle : IDisposable
        {
            public bool Disposed;

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }

    private sealed class LiveDeployment : ICommandPackageDeployment
    {
        private readonly object installationGate = new();
        private readonly object installedLock = new();
        private readonly Dictionary<int, string> installed = new();

        public TimeSpan AddDelay { get; set; }

        public Dictionary<int, string> Snapshot()
        {
            lock (installedLock)
            {
                return new Dictionary<int, string>(installed);
            }
        }

        public bool TryGetCurrentPackageIdentity(out string mainPackageName, out string publisher)
        {
            mainPackageName = SettingsContract.ReleasePackageName;
            publisher = SettingsContract.ReleasePublisher;
            return true;
        }

        public Dictionary<int, string> FindInstalledCommandSlots(string mainPackageName, string publisher)
        {
            return Snapshot();
        }

        public IDisposable? TryAcquireInstallationMutex(TimeSpan timeout)
        {
            if (!Monitor.TryEnter(installationGate, timeout))
            {
                return null;
            }

            return new GateReleaser(installationGate);
        }

        public void AddPackage(string packagePath, CancellationToken cancellationToken)
        {
            if (AddDelay > TimeSpan.Zero)
            {
                Thread.Sleep(AddDelay);
            }

            var slot = int.Parse(
                Path.GetFileNameWithoutExtension(packagePath),
                System.Globalization.CultureInfo.InvariantCulture);
            lock (installedLock)
            {
                installed[slot] = "RightAgent.Command" + slot.ToString("D2") + "_1.0.0.0_x64__pub";
            }
        }

        public void RemovePackage(string packageFullName, CancellationToken cancellationToken)
        {
            lock (installedLock)
            {
                foreach (var entry in installed)
                {
                    if (entry.Value != packageFullName)
                    {
                        continue;
                    }

                    installed.Remove(entry.Key);
                    return;
                }
            }
        }

        public void NotifyShellAssociationsChanged()
        {
        }

        private sealed class GateReleaser(object gate) : IDisposable
        {
            public void Dispose()
            {
                Monitor.Exit(gate);
            }
        }
    }
}
