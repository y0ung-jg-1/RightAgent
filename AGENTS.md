# Agent notes

Read `docs/ARCHITECTURE.md` and `docs/SETTINGS_SCHEMA.md` before changing launch or settings behavior.

## Settings UI

- Setting rows use Community Toolkit `SettingsCard` / `SettingsExpander`. Do not rebuild them with hand-rolled `Border` cards or by editing Expander template parts.
- **Do not add a second line of small description text** on cards or expanders. Do not set `SettingsCard.Description` or `SettingsExpander.Description` for help copy, command previews, or “what this setting does.”
- Help belongs on a tooltip or `AutomationProperties.HelpText`. Validation errors appear only when a field is actually invalid, next to that field (or in the summary InfoBar).
- The live context-menu preview stays a custom surface; there is no stock WinUI control for it.
