using RightAgent.Core;
using Xunit;

namespace RightAgent.Core.Tests;

public sealed class WindowsTerminalTests
{
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
}
