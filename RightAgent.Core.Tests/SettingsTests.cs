using RightAgent.Core;
using Xunit;

namespace RightAgent.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void DefaultsEnableOnlyDetectedCommands()
    {
        var settings = SettingsDefaults.Create(command => command is "claude" or "kimi");

        Assert.True(settings.Agents.Single(agent => agent.Id == "claude-code").Enabled);
        Assert.False(settings.Agents.Single(agent => agent.Id == "codex").Enabled);
        Assert.True(settings.Agents.Single(agent => agent.Id == "kimi-web").Enabled);
        Assert.Equal("claude-code", settings.DirectAgentId);
    }

    [Fact]
    public void NormalizeRepairsIdsSortAndDirectTarget()
    {
        var settings = new RightAgentSettings
        {
            MenuMode = SettingsContract.DirectMenu,
            DirectAgentId = "missing",
            Agents =
            [
                Agent("Same Name", "same", true, 20),
                Agent("Same Name", "same", true, 10),
                Agent("Broken", "broken", true, 30, value: "")
            ]
        };

        var result = SettingsValidator.Normalize(settings);

        Assert.Equal([0, 1, 2], result.Agents.Select(agent => agent.Sort));
        Assert.Equal(3, result.Agents.Select(agent => agent.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(result.Agents[0].Id, result.DirectAgentId);
        Assert.False(result.Agents[2].Enabled);
    }

    [Theory]
    [InlineData("https://www.kimi.com", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("javascript:alert(1)", false)]
    public void UrlValidationAllowsOnlyHttpAndHttps(string value, bool expected)
    {
        Assert.Equal(expected, SettingsValidator.IsActionValid(SettingsContract.Url, value));
    }

    [Fact]
    public async Task StoreWritesAndReadsUnicodeAtomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "RightAgent.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(root);
            var settings = SettingsDefaults.Create(_ => false);
            settings.Agents.Add(Agent("测试 Agent", "测试-agent", true, 4, "echo 路径 & 空格"));

            await store.SaveAsync(settings);
            var reloaded = await store.LoadAsync();

            Assert.Contains(reloaded.Agents, agent => agent.Name == "测试 Agent" && agent.Action.Value == "echo 路径 & 空格");
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
