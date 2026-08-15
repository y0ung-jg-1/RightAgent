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
            var viewModel = new MainViewModel(root, recorder.Invoke);
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
            var viewModel = new MainViewModel(root, recorder.Invoke);
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
            var viewModel = new MainViewModel(root, recorder.Invoke);
            await viewModel.LoadAsync();
            Assert.Single(recorder.Calls);

            // With the menu off, disabling A moves the UI direct target to B
            // and blanking B's command raises no validation error; only
            // Normalize disables B, so the persist has to repair the target.
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

            // A persist-side re-schedule would debounce a third save here.
            await Task.Delay(700);
            Assert.Equal(2, recorder.Calls.Count);
        }
        finally
        {
            DeleteTempRoot(root);
        }
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
        public List<RightAgentSettings> Calls { get; } = [];

        public Task<CommandPackageSyncResult> Invoke(
            RightAgentSettings settings,
            string localStateDirectory,
            CancellationToken cancellationToken)
        {
            Calls.Add(settings);
            return Task.FromResult(CommandPackageSyncResult.Unchanged);
        }
    }
}
