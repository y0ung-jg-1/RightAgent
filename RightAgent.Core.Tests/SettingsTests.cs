using System.Text.Json;
using System.Text.Json.Nodes;
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
    [InlineData("MULTIDIRECT", "multiDirect")]
    [InlineData("Direct", "direct")]
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
    [InlineData("http:/missing-slash", false)]
    [InlineData("https://two words.example", false)]
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

            Assert.DoesNotContain("terminalShell", await File.ReadAllTextAsync(store.SettingsPath));
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
    public void MenuEnabledDefaultsToTrueAndSurvivesNormalize()
    {
        Assert.True(SettingsDefaults.Create(_ => false).MenuEnabled);
        Assert.True(SettingsValidator.Normalize(new RightAgentSettings()).MenuEnabled);

        var disabled = new RightAgentSettings { MenuEnabled = false };
        Assert.False(SettingsValidator.Normalize(disabled).MenuEnabled);
    }

    [Fact]
    public async Task StoreIgnoresLegacyTerminalShellAndKeepsMenuEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "RightAgent.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "settings.json"),
                """{"schemaVersion":1,"terminalShell":"cmd","agents":[]}""");

            var store = new SettingsStore(root);
            var settings = await store.LoadAsync();
            await store.SaveAsync(settings);

            Assert.True(settings.MenuEnabled);
            Assert.DoesNotContain("terminalShell", await File.ReadAllTextAsync(store.SettingsPath));
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
    public async Task StoreDoesNotResetLockedSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "RightAgent.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(root);
            var settings = SettingsDefaults.Create(_ => false);
            settings.Agents[0].Name = "Keep Me";
            await store.SaveAsync(settings);

            await using var locked = new FileStream(store.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await Assert.ThrowsAnyAsync<IOException>(() => store.LoadAsync());
            Assert.Empty(Directory.GetFiles(root, "settings.corrupt-*.json"));
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
    public void NormalizeRejectsAbsoluteLocalIcons()
    {
        var settings = new RightAgentSettings
        {
            Agents = [new AgentDefinition
            {
                Id = "web",
                Name = "Web",
                Enabled = true,
                IconPath = "local:C:/Windows/foo.ico",
                Action = new AgentAction { Type = SettingsContract.TerminalCommand, Value = "echo" }
            }]
        };

        Assert.Equal("builtin:rightagent", SettingsValidator.Normalize(settings).Agents[0].IconPath);
    }

    [Theory]
    [InlineData("builtin:claude", "builtin:claude")]
    [InlineData("builtin:CLAUDE", "builtin:claude")]
    [InlineData("builtin:nope", "builtin:rightagent")]
    public void NormalizeIconAllowlistsBuiltInKeys(string input, string expected)
    {
        Assert.Equal(expected, SettingsValidator.NormalizeIconPath(input));
    }

    [Fact]
    public void ShellImageReadsSvgThroughWindowsThumbnail()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "icon.svg");
        Assert.True(File.Exists(path));
        Assert.True(ShellImage.TryGetBgra32(path, 64, out var pixels, out var width, out var height));
        Assert.True(width > 0);
        Assert.True(height > 0);
        Assert.Equal(width * height * 4, pixels.Length);
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

    [Fact]
    public void InstallRecordRoundTripsAndDetectsMissingApp()
    {
        var root = Path.Combine(Path.GetTempPath(), "RightAgent.Tests", Guid.NewGuid().ToString("N"));
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
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NormalizeMatchesSharedGoldenScenarios()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "normalize-agents.json");
        await using var stream = File.OpenRead(path);
        var root = await JsonNode.ParseAsync(stream)
            ?? throw new InvalidOperationException("The shared golden file is not valid JSON.");
        var scenarios = root["scenarios"] as JsonArray
            ?? throw new InvalidOperationException("The shared golden file has no scenarios array.");
        Assert.NotEmpty(scenarios);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        for (var index = 0; index < scenarios.Count; index++)
        {
            var scenario = (JsonObject)scenarios[index]!;
            var input = scenario["input"]!.Deserialize<RightAgentSettings>(options);
            var actual = SettingsValidator.Normalize(input);
            var actualNode = JsonSerializer.SerializeToNode(actual, options);

            Assert.True(
                JsonNode.DeepEquals(actualNode, scenario["expected"]),
                $"Golden scenario #{index} ({scenario["name"]}) mismatch:{Environment.NewLine}"
                    + $"actual:   {actualNode!.ToJsonString()}{Environment.NewLine}"
                    + $"expected: {scenario["expected"]!.ToJsonString()}");
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
}
