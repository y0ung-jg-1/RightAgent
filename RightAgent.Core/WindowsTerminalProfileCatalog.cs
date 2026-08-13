using System.Text.Json;

namespace RightAgent.Core;

public sealed record WindowsTerminalProfile(string Id, string Name, bool Hidden);

public sealed class WindowsTerminalProfileCatalog
{
    public static WindowsTerminalProfileCatalog Empty { get; } = new(null, []);

    private WindowsTerminalProfileCatalog(string? defaultProfileId, IReadOnlyList<WindowsTerminalProfile> profiles)
    {
        DefaultProfileId = defaultProfileId;
        Profiles = profiles;
    }

    public string? DefaultProfileId { get; }

    public IReadOnlyList<WindowsTerminalProfile> Profiles { get; }

    public string? DefaultProfileName => Find(DefaultProfileId)?.Name;

    public IEnumerable<WindowsTerminalProfile> VisibleProfiles =>
        Profiles.Where(profile => !profile.Hidden);

    public WindowsTerminalProfile? Find(string? idOrName)
    {
        var value = NormalizeToken(idOrName);
        if (value is null)
        {
            return null;
        }

        return Profiles.FirstOrDefault(profile => IdsEqual(profile.Id, value))
               ?? Profiles.FirstOrDefault(profile =>
                   profile.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string? NormalizeSelection(string? configured)
    {
        var value = NormalizeToken(configured);
        return value is null ? null : Find(value)?.Id ?? value;
    }

    public static IReadOnlyList<string> SettingsPaths(string? localAppData)
    {
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return [];
        }

        return
        [
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminalCanary_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json")
        ];
    }

    public static WindowsTerminalProfileCatalog Load(
        string? localAppData = null,
        Func<string, bool>? fileExists = null,
        Func<string, string>? readAllText = null)
    {
        localAppData ??= Environment.GetEnvironmentVariable("LOCALAPPDATA");
        fileExists ??= File.Exists;
        readAllText ??= File.ReadAllText;

        foreach (var path in SettingsPaths(localAppData))
        {
            try
            {
                if (!fileExists(path))
                {
                    continue;
                }

                return Parse(readAllText(path));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Skip unreadable Terminal settings and keep looking.
            }
        }

        return Empty;
    }

    public static WindowsTerminalProfileCatalog Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var root = document.RootElement;
            string? defaultProfileId = null;
            if (root.TryGetProperty("defaultProfile", out var defaultProfile)
                && defaultProfile.ValueKind == JsonValueKind.String)
            {
                defaultProfileId = NormalizeToken(defaultProfile.GetString());
            }

            var profiles = new List<WindowsTerminalProfile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("profiles", out var profilesElement))
            {
                foreach (var element in EnumerateProfileElements(profilesElement))
                {
                    if (TryReadProfile(element, out var profile) && seen.Add(CanonicalId(profile.Id)))
                    {
                        profiles.Add(profile);
                    }
                }
            }

            return new WindowsTerminalProfileCatalog(defaultProfileId, profiles);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static IEnumerable<JsonElement> EnumerateProfileElements(JsonElement profiles)
    {
        if (profiles.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in profiles.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (profiles.ValueKind == JsonValueKind.Object
            && profiles.TryGetProperty("list", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static bool TryReadProfile(JsonElement element, out WindowsTerminalProfile profile)
    {
        profile = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var guid = ReadString(element, "guid");
        var name = ReadString(element, "name");
        var id = NormalizeToken(guid) ?? NormalizeToken(name);
        if (id is null)
        {
            return false;
        }

        var hidden = element.TryGetProperty("hidden", out var hiddenProperty)
                     && hiddenProperty.ValueKind == JsonValueKind.True;
        profile = new WindowsTerminalProfile(id, string.IsNullOrWhiteSpace(name) ? id : name.Trim(), hidden);
        return true;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IdsEqual(string left, string right) =>
        CanonicalId(left).Equals(CanonicalId(right), StringComparison.OrdinalIgnoreCase);

    private static string CanonicalId(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[^1] == '}'
            ? trimmed[1..^1]
            : trimmed;
    }
}
