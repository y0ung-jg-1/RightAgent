using RightAgent.Core;
using Xunit;

namespace RightAgent.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void DefaultsEnableOnlyDetectedCommands()
    {
        var settings = SettingsDefaults.Create(command => command is "claude" or "kimi" or "cursor-agent");

        Assert.True(settings.Agents.Single(agent => agent.Id == "claude-code").Enabled);
        Assert.False(settings.Agents.Single(agent => agent.Id == "codex").Enabled);
        var kimi = settings.Agents.Single(agent => agent.Id == "kimi");
        Assert.True(kimi.Enabled);
        Assert.Equal("Kimi", kimi.Name);
        Assert.Equal("kimi", kimi.Action.Value);
        Assert.True(settings.Agents.Single(agent => agent.Id == "cursor-agent").Enabled);
        Assert.Equal("claude-code", settings.DirectAgentId);
        Assert.Equal(SettingsContract.AutomaticTerminalShell, settings.TerminalShell);
    }

    [Fact]
    public void CursorAgentUsesExpectedBuiltInDefinition()
    {
        var cursor = SettingsDefaults.Create(_ => false).Agents.Single(agent => agent.Id == "cursor-agent");

        Assert.Equal("Cursor Agent", cursor.Name);
        Assert.Equal("builtin:cursor", cursor.IconPath);
        Assert.Equal(SettingsContract.TerminalCommand, cursor.Action.Type);
        Assert.Equal("cursor-agent", cursor.Action.Value);
        Assert.Equal(5, cursor.Sort);
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
    [InlineData("grouped", "grouped")]
    [InlineData("direct", "direct")]
    [InlineData("multiDirect", "multiDirect")]
    [InlineData("unknown", "grouped")]
    public void NormalizeAcceptsKnownMenuModes(string input, string expected)
    {
        var settings = new RightAgentSettings { MenuMode = input };

        Assert.Equal(expected, SettingsValidator.Normalize(settings).MenuMode);
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
            settings.TerminalShell = SettingsContract.CommandPromptTerminalShell;
            settings.Agents.Add(Agent("测试 Agent", "测试-agent", true, 4, "echo 路径 & 空格"));

            await store.SaveAsync(settings);
            var reloaded = await store.LoadAsync();

            Assert.Equal(SettingsContract.CommandPromptTerminalShell, reloaded.TerminalShell);
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
            Directory.CreateDirectory(root);
            for (var slot = 0; slot < SettingsContract.MaxMultiDirectAgents; ++slot)
            {
                File.WriteAllBytes(Path.Combine(root, CommandSlotPlanner.CommandPackageFileName(slot)), [1]);
            }
            Assert.True(CommandSlotPlanner.CacheIsComplete(root));
            File.Delete(Path.Combine(root, "07.msix"));
            Assert.False(CommandSlotPlanner.CacheIsComplete(root));
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
    public void MenuEnabledDefaultsToTrueAndSurvivesNormalize()
    {
        Assert.True(SettingsDefaults.Create(_ => false).MenuEnabled);
        Assert.True(SettingsValidator.Normalize(new RightAgentSettings()).MenuEnabled);

        var disabled = new RightAgentSettings { MenuEnabled = false };
        Assert.False(SettingsValidator.Normalize(disabled).MenuEnabled);
    }

    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("pwsh", "pwsh")]
    [InlineData("windowsPowerShell", "windowsPowerShell")]
    [InlineData("cmd", "cmd")]
    [InlineData("unknown", "auto")]
    public void NormalizeAcceptsKnownTerminalShells(string input, string expected)
    {
        var settings = new RightAgentSettings { TerminalShell = input };

        Assert.Equal(expected, SettingsValidator.Normalize(settings).TerminalShell);
    }

    [Fact]
    public async Task StoreDefaultsMissingTerminalShellToAutomatic()
    {
        var root = Path.Combine(Path.GetTempPath(), "RightAgent.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "settings.json"),
                """{"schemaVersion":1,"agents":[]}""");

            var settings = await new SettingsStore(root).LoadAsync();

            Assert.Equal(SettingsContract.AutomaticTerminalShell, settings.TerminalShell);
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
    public void WindowsTerminalLocatorChecksPathAndWindowsAppsAlias()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("C:\\Tools", "wt.exe")
        };

        Assert.True(WindowsTerminalLocator.IsAvailable(
            "C:\\Other;\"C:\\Tools\"",
            "C:\\Users\\Test\\AppData\\Local",
            existing.Contains));

        existing.Clear();
        existing.Add(Path.Combine("C:\\Users\\Test\\AppData\\Local", "Microsoft", "WindowsApps", "wt.exe"));

        Assert.True(WindowsTerminalLocator.IsAvailable(
            "C:\\Other",
            "C:\\Users\\Test\\AppData\\Local",
            existing.Contains));
    }

    [Fact]
    public void WindowsTerminalLocatorReportsMissingTerminal()
    {
        Assert.False(WindowsTerminalLocator.IsAvailable("C:\\Missing", "C:\\MissingLocal", _ => false));
    }

    [Fact]
    public void WindowsTerminalProfileCatalogReadsVisibleProfilesAndDefault()
    {
        var catalog = WindowsTerminalProfileCatalog.Parse(
            """
            {
              // Startup
              "defaultProfile": "{574e775e-4f2a-5b96-ac1e-a2962a402336}",
              "profiles": {
                "list": [
                  { "guid": "{61c54bbd-c2c6-5271-96e7-009a87ff44bf}", "name": "Windows PowerShell", "hidden": false },
                  { "guid": "{574e775e-4f2a-5b96-ac1e-a2962a402336}", "name": "PowerShell", "hidden": false },
                  { "guid": "{7f0c4180-34d6-53ed-a60e-a10bbf11a91e}", "name": "VS 2019", "hidden": true }
                ]
              }
            }
            """);

        Assert.Equal("{574e775e-4f2a-5b96-ac1e-a2962a402336}", catalog.DefaultProfileId);
        Assert.Equal("PowerShell", catalog.DefaultProfileName);
        Assert.Equal(["Windows PowerShell", "PowerShell"], catalog.VisibleProfiles.Select(profile => profile.Name));
        Assert.Equal("{574e775e-4f2a-5b96-ac1e-a2962a402336}", catalog.NormalizeSelection("PowerShell"));
        Assert.Equal("{61c54bbd-c2c6-5271-96e7-009a87ff44bf}", catalog.NormalizeSelection("61c54bbd-c2c6-5271-96e7-009a87ff44bf"));
        Assert.Null(catalog.NormalizeSelection(" "));
        Assert.Equal("Custom", catalog.NormalizeSelection("Custom"));
    }

    [Fact]
    public void WindowsTerminalProfileCatalogSupportsLegacyProfileArray()
    {
        var catalog = WindowsTerminalProfileCatalog.Parse(
            """
            { "defaultProfile": "cmd", "profiles": [ { "name": "cmd" }, { "guid": "{0caa0dad-35be-5f56-a8ff-afceeeaa6101}", "name": "Command Prompt" } ] }
            """);

        Assert.Equal("cmd", catalog.DefaultProfileName);
        Assert.Equal(2, catalog.Profiles.Count);
    }

    [Fact]
    public void WindowsTerminalProfileCatalogLoadUsesFirstExistingSettingsFile()
    {
        var localAppData = "C:\\Users\\Test\\AppData\\Local";
        var preview = Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json");
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { preview };
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [preview] = """{ "defaultProfile": "Preview PowerShell", "profiles": [ { "name": "Preview PowerShell" } ] }"""
        };

        var catalog = WindowsTerminalProfileCatalog.Load(localAppData, existing.Contains, path => files[path]);

        Assert.Equal("Preview PowerShell", catalog.DefaultProfileName);
        Assert.Contains(
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            WindowsTerminalProfileCatalog.SettingsPaths(localAppData));
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
