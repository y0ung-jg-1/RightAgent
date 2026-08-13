# Settings schema v1

The public local contract is UTF-8 JSON named `settings.json`:

```json
{
  "schemaVersion": 1,
  "menuEnabled": true,
  "language": "system",
  "menuMode": "grouped",
  "directAgentId": "claude-code",
  "terminalShell": "auto",
  "terminalProfile": null,
  "agents": [
    {
      "id": "claude-code",
      "name": "Claude Code",
      "enabled": true,
      "sort": 0,
      "iconPath": "builtin:claude",
      "action": {
        "type": "terminalCommand",
        "value": "claude"
      }
    },
    {
      "id": "kimi-web",
      "name": "Kimi Web",
      "enabled": true,
      "sort": 1,
      "iconPath": "builtin:kimi",
      "action": {
        "type": "url",
        "value": "https://www.kimi.com"
      }
    }
  ]
}
```

## Field rules

- `schemaVersion`: integer `1`. Writers always emit the current version.
- `menuEnabled`: optional boolean, default `true`. When `false`, the Explorer command is hidden everywhere. Added later within schema version 1: readers ignore unknown fields, and files written before this field existed behave as `true`.
- `language`: `system`, `zh-CN`, or `en-US`; unknown values normalize to `system`, with non-Chinese systems falling back to English.
- `menuMode`: `grouped`, `direct`, or `multiDirect`; unknown values normalize to `grouped`. `multiDirect` exposes each enabled agent as a separate root command in configured order and supports up to 16 enabled agents.
- `directAgentId`: ID of an enabled agent used only by `direct` mode. If absent or disabled, the first enabled agent becomes the fallback.
- `terminalShell`: `auto`, `pwsh`, `windowsPowerShell`, or `cmd`. Missing or unknown values normalize to `auto`, which prefers PowerShell 7 (`pwsh.exe`) and falls back to Windows PowerShell 5.1. CMD is used only when explicitly selected.
- `terminalProfile`: optional Windows Terminal profile name. `null` or whitespace uses the Terminal default profile. This controls the Terminal profile independently from `terminalShell`; the selected shell executable is passed explicitly so the configured command can be executed reliably.
- `agents`: ordered by `sort`; the settings writer rewrites sort values to contiguous zero-based integers.
- `id`: stable, case-insensitive ID containing lower-case letters, digits, `.`, `_`, or `-`. The UI generates unique IDs and does not change them when names change.
- `name`: user-visible menu title.
- `enabled`: disabled or invalid actions do not appear in Explorer.
- `iconPath`: `builtin:rightagent`, `builtin:claude`, `builtin:codex`, `builtin:kimi`, `builtin:grok`, `builtin:opencode`, or a safe `local:` relative path copied under LocalState.
- `action.type`: `terminalCommand` or `url`.
- `action.value`: command text interpreted by the configured `terminalShell` for terminal actions, or an absolute `http`/`https` URL for web actions.

The settings app writes to a temporary sibling file, flushes it, and atomically replaces `settings.json`. If deserialization fails, it makes a timestamped `settings.corrupt-*.json` copy before restoring detected defaults. The Explorer DLL never repairs or writes configuration.
