namespace RightAgent.Core;

public static class SettingsDefaults
{
    public static RightAgentSettings Create(Func<string, bool>? commandExists = null)
    {
        commandExists ??= CommandLocator.Exists;

        var agents = new List<AgentDefinition>
        {
            BuiltIn("claude-code", "Claude Code", "builtin:claude", "claude", 0, commandExists("claude")),
            BuiltIn("codex", "Codex", "builtin:codex", "codex", 1, commandExists("codex")),
            BuiltIn("kimi", "Kimi", "builtin:kimi", "kimi", 2, commandExists("kimi")),
            BuiltIn("grok", "Grok", "builtin:grok", "grok", 3, commandExists("grok")),
            BuiltIn("opencode", "opencode", "builtin:opencode", "opencode", 4, commandExists("opencode")),
            BuiltIn("cursor-agent", "Cursor Agent", "builtin:cursor", "cursor-agent", 5, commandExists("cursor-agent"))
        };

        return new RightAgentSettings
        {
            SchemaVersion = SettingsContract.CurrentSchemaVersion,
            Language = SettingsContract.SystemLanguage,
            MenuMode = SettingsContract.GroupedMenu,
            DirectAgentId = agents.FirstOrDefault(agent => agent.Enabled)?.Id,
            Agents = agents
        };
    }

    private static AgentDefinition BuiltIn(
        string id,
        string name,
        string iconPath,
        string command,
        int sort,
        bool enabled) => new()
        {
            Id = id,
            Name = name,
            Enabled = enabled,
            Sort = sort,
            IconPath = iconPath,
            Action = new AgentAction
            {
                Type = SettingsContract.TerminalCommand,
                Value = command
            }
        };
}
