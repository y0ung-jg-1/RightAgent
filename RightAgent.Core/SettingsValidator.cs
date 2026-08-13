using System.Text.RegularExpressions;

namespace RightAgent.Core;

public static partial class SettingsValidator
{
    public static RightAgentSettings Normalize(RightAgentSettings? input)
    {
        input ??= SettingsDefaults.Create(_ => false);
        input.SchemaVersion = SettingsContract.CurrentSchemaVersion;
        input.Language = input.Language is SettingsContract.SystemLanguage or SettingsContract.ChineseLanguage or SettingsContract.EnglishLanguage
            ? input.Language
            : SettingsContract.SystemLanguage;
        input.MenuMode = input.MenuMode is SettingsContract.DirectMenu or SettingsContract.MultiDirectMenu
            ? input.MenuMode
            : SettingsContract.GroupedMenu;
        input.TerminalShell = input.TerminalShell is SettingsContract.PowerShell7TerminalShell
            or SettingsContract.WindowsPowerShellTerminalShell
            or SettingsContract.CommandPromptTerminalShell
            ? input.TerminalShell
            : SettingsContract.AutomaticTerminalShell;
        input.TerminalProfile = CleanOptional(input.TerminalProfile);
        input.Agents ??= [];

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<AgentDefinition>();
        foreach (var agent in input.Agents.OrderBy(agent => agent.Sort))
        {
            if (agent is null)
            {
                continue;
            }

            var id = NormalizeId(agent.Id, agent.Name, usedIds);
            var name = string.IsNullOrWhiteSpace(agent.Name) ? id : agent.Name.Trim();
            var actionType = agent.Action?.Type == SettingsContract.Url
                ? SettingsContract.Url
                : SettingsContract.TerminalCommand;
            var actionValue = agent.Action?.Value?.Trim() ?? string.Empty;
            var iconPath = NormalizeIcon(agent.IconPath);

            normalized.Add(new AgentDefinition
            {
                Id = id,
                Name = name,
                Enabled = agent.Enabled && IsActionValid(actionType, actionValue),
                Sort = normalized.Count,
                IconPath = iconPath,
                Action = new AgentAction { Type = actionType, Value = actionValue }
            });
        }

        input.Agents = normalized;
        var enabledIds = normalized.Where(agent => agent.Enabled).Select(agent => agent.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (input.DirectAgentId is null || !enabledIds.Contains(input.DirectAgentId))
        {
            input.DirectAgentId = normalized.FirstOrDefault(agent => agent.Enabled)?.Id;
        }

        return input;
    }

    public static bool IsActionValid(string type, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (type != SettingsContract.Url)
        {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                   || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeId(string? id, string? name, ISet<string> usedIds)
    {
        var seed = string.IsNullOrWhiteSpace(id) ? name ?? "agent" : id;
        var normalized = InvalidIdCharacters().Replace(seed.Trim().ToLowerInvariant(), "-").Trim('-');
        if (normalized.Length == 0)
        {
            normalized = "agent";
        }

        var candidate = normalized;
        var suffix = 2;
        while (!usedIds.Add(candidate))
        {
            candidate = $"{normalized}-{suffix++}";
        }

        return candidate;
    }

    private static string NormalizeIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return "builtin:rightagent";
        }

        var value = iconPath.Trim();
        if (value.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (value.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            var relative = value["local:".Length..].Replace('\\', '/').TrimStart('/');
            if (!relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
            {
                return "local:" + relative;
            }
        }

        return "builtin:rightagent";
    }

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("[^a-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidIdCharacters();
}
