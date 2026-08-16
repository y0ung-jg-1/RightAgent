using RightAgent.App.ViewModels;
using RightAgent.Core;
using Xunit;

namespace RightAgent.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task FlushWithInvalidFieldKeepsDiskSettingsAndSkipsOccupancy()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SettingsStore(root);
            await store.SaveAsync(SeedSettings());
            var recorder = new RecordingSync();
            var viewModel = CreateViewModel(root, recorder);
            await viewModel.LoadAsync();

            Assert.Single(recorder.Calls);

            // Blank command on the only enabled agent: the field is invalid,
            // the persist must be skipped, and occupancy must keep matching
            // the still-valid settings on disk.
            viewModel.Agents[0].ActionValue = "   ";
            Assert.True(viewModel.HasValidationErrors);

            await viewModel.FlushAutoSaveAsync();

            Assert.Single(recorder.Calls);
            var onDisk = await store.LoadAsync();
            Assert.Equal("cmd-a", onDisk.Agents[0].Action.Value);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task FlushWithValidEditSyncsThePersistedSnapshot()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SettingsStore(root);
            await store.SaveAsync(SeedSettings());
            var recorder = new RecordingSync();
            var viewModel = CreateViewModel(root, recorder);
            await viewModel.LoadAsync();

            viewModel.Agents[0].Name = "Renamed";
            await viewModel.FlushAutoSaveAsync();

            Assert.Equal(2, recorder.Calls.Count);
            Assert.Equal("Renamed", recorder.Calls[1].Agents[0].Name);
            var onDisk = await store.LoadAsync();
            Assert.Equal("Renamed", onDisk.Agents[0].Name);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task DirectTargetRepairDuringPersistDoesNotRescheduleSave()
    {
        var root = CreateTempRoot();
        try
        {
            var (store, viewModel, recorder) = await LoadRepairHostAsync(root);

            viewModel.Agents[0].Enabled = false;
            viewModel.Agents.Single(agent => agent.Id == "b").ActionValue = "   ";
            Assert.False(viewModel.HasValidationErrors);

            await viewModel.FlushAutoSaveAsync();

            Assert.Equal(2, recorder.Calls.Count);
            Assert.Null(recorder.Calls[1].DirectAgentId);
            Assert.False(recorder.Calls[1].Agents.Single(agent => agent.Id == "b").Enabled);
            Assert.Null(viewModel.DirectAgentId);
            var onDisk = await store.LoadAsync();
            Assert.Null(onDisk.DirectAgentId);

            await Task.Delay(700);
            Assert.Equal(2, recorder.Calls.Count);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task PersistSideRepairIsPostedThroughTheCapturedSynchronizationContext()
    {
        var root = CreateTempRoot();
        var previous = SynchronizationContext.Current;
        var context = new CapturingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var (_, viewModel, recorder) = await LoadRepairHostAsync(root);

            viewModel.Agents[0].Enabled = false;
            viewModel.Agents.Single(agent => agent.Id == "b").ActionValue = "   ";

            await viewModel.FlushAutoSaveAsync();

            Assert.True(context.PostCount > 0);
            Assert.Null(viewModel.DirectAgentId);
            Assert.Equal(2, recorder.Calls.Count);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task InvalidEditCancelsPendingDebounceAndDoesNotWrite()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SettingsStore(root);
            await store.SaveAsync(SeedSettings());
            var recorder = new RecordingSync();
            var viewModel = CreateViewModel(root, recorder);
            await viewModel.LoadAsync();

            viewModel.Agents[0].Name = "Edited";
            viewModel.Agents[0].ActionValue = "   ";
            Assert.True(viewModel.HasValidationErrors);

            await Task.Delay(700);

            Assert.Single(recorder.Calls);
            var onDisk = await store.LoadAsync();
            Assert.Equal("A", onDisk.Agents[0].Name);
            Assert.Equal("cmd-a", onDisk.Agents[0].Action.Value);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task OccupancyUsesTheLatestPersistedSnapshotWhenRunsOverlap()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SettingsStore(root);
            await store.SaveAsync(SeedSettings());
            var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var recorder = new RecordingSync
            {
                FirstEntered = firstEntered,
                BlockFirst = releaseFirst.Task
            };
            var viewModel = CreateViewModel(root, recorder);
            var load = viewModel.LoadAsync();
            await firstEntered.Task;

            viewModel.Agents[0].Name = "Later";
            var flush = viewModel.FlushAutoSaveAsync();
            releaseFirst.SetResult();
            await load;
            await flush;
            await recorder.WaitUntilLastAsync(settings => settings.Agents[0].Name == "Later");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static MainViewModel CreateViewModel(string root, RecordingSync recorder) =>
        new(root, recorder.Invoke, () => WindowsTerminalProfileCatalog.Empty);

    private static async Task<(SettingsStore Store, MainViewModel ViewModel, RecordingSync Recorder)> LoadRepairHostAsync(
        string root)
    {
        var settings = SeedSettings(menuEnabled: false);
        settings.Agents.Add(new AgentDefinition
        {
            Id = "b",
            Name = "B",
            Enabled = true,
            Sort = 1,
            IconPath = "builtin:rightagent",
            Action = new AgentAction { Type = SettingsContract.TerminalCommand, Value = "cmd-b" }
        });
        var store = new SettingsStore(root);
        await store.SaveAsync(settings);
        var recorder = new RecordingSync();
        var viewModel = CreateViewModel(root, recorder);
        await viewModel.LoadAsync();
        Assert.Single(recorder.Calls);
        return (store, viewModel, recorder);
    }

    private static RightAgentSettings SeedSettings(bool menuEnabled = true) => new()
    {
        MenuEnabled = menuEnabled,
        MenuMode = SettingsContract.GroupedMenu,
        DirectAgentId = "a",
        Agents =
        [
            new AgentDefinition
            {
                Id = "a",
                Name = "A",
                Enabled = true,
                Sort = 0,
                IconPath = "builtin:rightagent",
                Action = new AgentAction { Type = SettingsContract.TerminalCommand, Value = "cmd-a" }
            }
        ]
    };

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RightAgent.App.Tests", Guid.NewGuid().ToString("N"));
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

    private sealed class RecordingSync
    {
        private int started;

        public List<RightAgentSettings> Calls { get; } = [];

        public TaskCompletionSource? FirstEntered { get; set; }

        public Task? BlockFirst { get; set; }

        public async Task<CommandPackageSyncResult> Invoke(
            RightAgentSettings settings,
            string localStateDirectory,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                FirstEntered?.TrySetResult();
                if (BlockFirst is not null)
                {
                    await BlockFirst.ConfigureAwait(false);
                }
            }

            lock (Calls)
            {
                Calls.Add(settings);
            }

            return CommandPackageSyncResult.Unchanged;
        }

        public async Task WaitUntilLastAsync(Func<RightAgentSettings, bool> match)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                RightAgentSettings? last = null;
                lock (Calls)
                {
                    if (Calls.Count > 0)
                    {
                        last = Calls[^1];
                    }
                }

                if (last is not null && match(last))
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("Occupancy did not observe the expected snapshot.");
        }
    }

    private sealed class CapturingSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            d(state);
        }
    }
}
