# Agent notes

Read `docs/ARCHITECTURE.md` and `docs/SETTINGS_SCHEMA.md` before changing launch or settings behavior.

## Settings UI

- Setting rows use Community Toolkit `SettingsCard` / `SettingsExpander`. Do not rebuild them with hand-rolled `Border` cards or by editing Expander template parts.
- Group related cards with a `SettingsSectionHeaderTextBlockStyle` heading (Windows Settings spacing `1,30,0,6`). Do not fake hierarchy by indenting a block of cards.
- **Do not add a second line of small description text** on cards or expanders. Do not set `SettingsCard.Description` or `SettingsExpander.Description` for help copy, command previews, or “what this setting does.”
- Help belongs on a tooltip or `AutomationProperties.HelpText`. Validation errors appear only when a field is actually invalid, next to that field (or in the summary InfoBar).
- The live context-menu preview stays a custom surface; there is no stock WinUI control for it.
- Settings live on `NavigationView` pages (`Pages/MenuPage`, `Pages/GeneralPage`). Agent switches and editor rows live on the menu page: the full list in grouped/multi-direct, only the selected agent in single-direct. Do not add a separate Agents nav item.
- User-visible strings live in `Strings/<lang>/Resources.resw`. `Localization` reads them through MRT Core. Do not put a second in-code string table back in `Localization.cs`.
- Window minimum size is `OverlappedPresenter.PreferredMinimumWidth` / `PreferredMinimumHeight`. Do not add an `AppWindow.Changed` resize loop. ComboBoxes bind `SelectedValue` TwoWay; do not add named-selector sync helpers.
- Settings persist automatically after edits. Do not add a Save button or a success banner for a normal write. Invalid fields stay on screen and are not written; Explorer occupancy sync still runs only after a valid persist.
- The shipped settings app is unpackaged. Settings and the command-package cache live under `%LOCALAPPDATA%\RightAgent`. Do not send production settings back through `ApplicationData.Current.LocalFolder` or a settings MSIX.
