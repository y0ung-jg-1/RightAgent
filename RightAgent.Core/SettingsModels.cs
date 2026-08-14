using System.Text.Json.Serialization;

namespace RightAgent.Core;

public static class SettingsContract
{
    public const int CurrentSchemaVersion = 1;
    public const string SystemLanguage = "system";
    public const string ChineseLanguage = "zh-CN";
    public const string EnglishLanguage = "en-US";
    public const string GroupedMenu = "grouped";
    public const string DirectMenu = "direct";
    public const string MultiDirectMenu = "multiDirect";
    public const int MaxMultiDirectAgents = 16;
    public const string TerminalCommand = "terminalCommand";
    public const string Url = "url";
    public const string ReleasePackageName = "RightAgent";
    public const string ReleasePublisher = "CN=RightAgent";
    public const string DevelopmentPackageName = "RightAgent.Dev";
    public const string DevelopmentPublisher = "CN=RightAgent Dev";
    public const string BuiltInIconPrefix = "builtin:";
    public static readonly string[] BuiltInIconKeys =
    [
        "rightagent",
        "claude",
        "codex",
        "kimi",
        "grok",
        "opencode",
        "cursor"
    ];

    public static bool IsBuiltInIconKey(string? key) =>
        key is not null && BuiltInIconKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static string BuiltInIconPath(string key) => BuiltInIconPrefix + key.Trim().ToLowerInvariant();
}

public sealed class RightAgentSettings
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = SettingsContract.CurrentSchemaVersion;

    [JsonPropertyName("menuEnabled")]
    public bool MenuEnabled { get; set; } = true;

    [JsonPropertyName("language")]
    public string Language { get; set; } = SettingsContract.SystemLanguage;

    [JsonPropertyName("menuMode")]
    public string MenuMode { get; set; } = SettingsContract.GroupedMenu;

    [JsonPropertyName("directAgentId")]
    public string? DirectAgentId { get; set; }

    [JsonPropertyName("terminalProfile")]
    public string? TerminalProfile { get; set; }

    [JsonPropertyName("agents")]
    public List<AgentDefinition> Agents { get; set; } = [];
}

public sealed class AgentDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("sort")]
    public int Sort { get; set; }

    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; } = "builtin:rightagent";

    [JsonPropertyName("action")]
    public AgentAction Action { get; set; } = new();
}

public sealed class AgentAction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = SettingsContract.TerminalCommand;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}
