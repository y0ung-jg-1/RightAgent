# Settings schema v1

The public local contract is UTF-8 JSON named `settings.json`:

```json
{
  "schemaVersion": 1,
  "menuEnabled": true,
  "language": "system",
  "menuMode": "grouped",
  "directAgentId": "claude-code",
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
- `menuMode`: `grouped` or `direct`; unknown values normalize to `grouped`.
- `directAgentId`: ID of an enabled agent. If absent or disabled, the first enabled agent becomes the fallback.
- `terminalProfile`: optional Windows Terminal profile name. `null` or whitespace uses the Terminal default.
- `agents`: ordered by `sort`; the settings writer rewrites sort values to contiguous zero-based integers.
- `id`: stable, case-insensitive ID containing lower-case letters, digits, `.`, `_`, or `-`. The UI generates unique IDs and does not change them when names change.
- `name`: user-visible menu title.
- `enabled`: disabled or invalid actions do not appear in Explorer.
- `iconPath`: `builtin:rightagent`, `builtin:claude`, `builtin:codex`, `builtin:kimi`, `builtin:grok`, `builtin:opencode`, or a safe `local:` relative path copied under LocalState.
- `action.type`: `terminalCommand` or `url`.
- `action.value`: PowerShell command text for terminal actions, or an absolute `http`/`https` URL for web actions.

The settings app writes to a temporary sibling file, flushes it, and atomically replaces `settings.json`. If deserialization fails, it makes a timestamped `settings.corrupt-*.json` copy before restoring detected defaults. The Explorer DLL never repairs or writes configuration.
