using RightAgent.Core;
using Xunit;

namespace RightAgent.Core.Tests;

public sealed class OccupancyTests
{
    [Fact]
    public void CommandSlotPlannerMatchesVisibleRootCommands()
    {
        var disabled = new RightAgentSettings
        {
            MenuEnabled = false,
            MenuMode = SettingsContract.MultiDirectMenu,
            Agents = [Agent("One", "one", true, 0), Agent("Two", "two", true, 1)]
        };
        Assert.Equal(0, CommandSlotPlanner.RequiredSlotCount(disabled));

        var empty = new RightAgentSettings
        {
            MenuMode = SettingsContract.GroupedMenu,
            Agents = [Agent("Off", "off", false, 0)]
        };
        Assert.Equal(0, CommandSlotPlanner.RequiredSlotCount(empty));

        var grouped = new RightAgentSettings
        {
            MenuMode = SettingsContract.GroupedMenu,
            Agents = [Agent("One", "one", true, 0), Agent("Two", "two", true, 1)]
        };
        Assert.Equal(1, CommandSlotPlanner.RequiredSlotCount(grouped));
        Assert.Equal(1, CommandSlotPlanner.RequiredSlotCount(new RightAgentSettings
        {
            MenuMode = SettingsContract.DirectMenu,
            DirectAgentId = "two",
            Agents = grouped.Agents
        }));

        var multiDirect = new RightAgentSettings
        {
            MenuMode = SettingsContract.MultiDirectMenu,
            Agents =
            [
                Agent("One", "one", true, 0),
                Agent("Off", "off", false, 1),
                Agent("Two", "two", true, 2),
                Agent("Three", "three", true, 3)
            ]
        };
        Assert.Equal(3, CommandSlotPlanner.RequiredSlotCount(multiDirect));
        var tooMany = new RightAgentSettings
        {
            MenuMode = SettingsContract.MultiDirectMenu,
            Agents = Enumerable.Range(0, 17).Select(index => Agent($"A{index}", $"a{index}", true, index)).ToList()
        };
        Assert.Equal(16, CommandSlotPlanner.RequiredSlotCount(tooMany));
        Assert.Equal("00.msix", CommandSlotPlanner.CommandPackageFileName(0));
        Assert.Equal("15.msix", CommandSlotPlanner.CommandPackageFileName(15));
    }

    [Fact]
    public void CommandPackageCacheRequiresEverySlotFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "RightAgent.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(CommandSlotPlanner.CacheIsComplete(root));
            Assert.False(CommandSlotPlanner.CachedPackageExists(root, 0));
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, CommandSlotPlanner.CommandPackageFileName(0)), [1]);
            Assert.True(CommandSlotPlanner.CachedPackageExists(root, 0));
            Assert.False(CommandSlotPlanner.CachedPackageExists(root, 1));
            Assert.False(CommandSlotPlanner.CacheIsComplete(root));
            for (var slot = 1; slot < SettingsContract.MaxMultiDirectAgents; ++slot)
            {
                File.WriteAllBytes(Path.Combine(root, CommandSlotPlanner.CommandPackageFileName(slot)), [1]);
            }
            Assert.True(CommandSlotPlanner.CacheIsComplete(root));
            File.Delete(Path.Combine(root, "07.msix"));
            Assert.False(CommandSlotPlanner.CacheIsComplete(root));
            Assert.False(CommandSlotPlanner.CachedPackageExists(root, 7));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void OccupancyPlanAddsMissingCachedSlotsAndRemovesExtras()
    {
        var installed = new Dictionary<int, string>
        {
            [0] = "RightAgent.Command00_1.0.0.0_x64__pub",
            [2] = "RightAgent.Command02_1.0.0.0_x64__pub"
        };
        var plan = CommandSlotPlanner.Plan(2, installed, slot => slot == 1);

        Assert.Equal([1], plan.SlotsToAdd);
        Assert.Equal(["RightAgent.Command02_1.0.0.0_x64__pub"], plan.PackagesToRemove);
        Assert.False(plan.CacheMissingRequiredSlots);
    }

    [Fact]
    public void OccupancyPlanReportsMissingCacheInsteadOfInventingAdds()
    {
        var plan = CommandSlotPlanner.Plan(2, new Dictionary<int, string>(), _ => false);

        Assert.Empty(plan.SlotsToAdd);
        Assert.Empty(plan.PackagesToRemove);
        Assert.True(plan.CacheMissingRequiredSlots);
    }

    [Fact]
    public void OccupancyPlanIsUnchangedWhenRequiredSlotsAreInstalled()
    {
        var plan = CommandSlotPlanner.Plan(
            1,
            new Dictionary<int, string> { [0] = "RightAgent.Command00_1.0.0.0_x64__pub" },
            _ => true);

        Assert.Empty(plan.SlotsToAdd);
        Assert.Empty(plan.PackagesToRemove);
        Assert.False(plan.CacheMissingRequiredSlots);
    }

    private static AgentDefinition Agent(
        string name,
        string id,
        bool enabled,
        int sort,
        string value = "codex") => new()
    {
        Id = id,
        Name = name,
        Enabled = enabled,
        Sort = sort,
        IconPath = "builtin:rightagent",
        Action = new AgentAction { Type = SettingsContract.TerminalCommand, Value = value }
    };
}
