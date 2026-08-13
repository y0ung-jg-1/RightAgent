# Acceptance test matrix

## Automated gates

- Managed tests: defaults detect commands, including Cursor Agent's exact `cursor-agent` command; Windows Terminal detection covers PATH/WindowsApps/missing cases, menu-mode and terminal-shell values normalize safely, IDs/order/direct target are repaired, URL schemes are restricted, and Unicode settings save/load atomically.
- Native tests: Windows argument round-trip covers spaces, Chinese, parentheses, single quotes, ampersands, quotes, and trailing backslashes; built-in Cursor defaults, icon resolution, the unpackaged settings fallback, and explicit settings-path overrides are verified.
- Native COM smoke test: loads `RightAgent.Shell.dll` without registration, checks grouped and single-direct behavior, then verifies 16 registered class slots, ordered multi-direct root titles/actions, hidden unused slots, and main-package family mapping. Manifest validation requires one independently identified hidden command package per slot so Explorer cannot collapse multi-direct commands into one app-attributed flyout.
- Release build: x64 Launcher and Shell compile with warnings as errors; WAP produces one unsigned settings package and the command packer produces 16 unsigned hidden command packages before signing.
- Release installer: the pinned Inno Setup compiler hash is verified; the final Setup EXE and all 17 embedded MSIX packages use the expected certificate and RFC 3161 timestamps; the external SHA-256 covers the exact EXE. The tag workflow runs the final Setup on its clean hosted runner and rejects installer exceptions, elevated install mode, incomplete package-deployment progress, a missing or wrong package version, command packages exposed in Start, or an incorrect certificate store.

## Manual Windows 11 acceptance

1. On a clean standard-user account, double-click Setup and confirm the wizard remains under the initiating user. Start installation, supply administrator approval for the certificate helper, then confirm the indeterminate animation changes to a real 0–100% indicator while all 17 internal MSIX packages deploy and RightAgent is installed for the initiating user rather than the administrator account. Confirm the certificate exists only in Local Machine\Trusted People, not Trusted Root; cancelling UAC must leave the package set uninstalled.
2. While installation is active, start the same Setup again and confirm the second instance is rejected. After installation, run the same Setup version again and confirm it succeeds without another UAC prompt or resetting `LocalState/settings.json`.
3. Open RightAgent once. With Windows Terminal installed, confirm no dependency prompt appears. With `wt.exe` unavailable, confirm a localized prompt offers **Open Microsoft Store** and **Later**; Later keeps the settings app usable and the prompt returns after reopening.
4. Resize the settings window from its default size down to the enforced minimum at 100%, 150%, and 200% display scaling. Confirm the main content never moves beyond either window edge, the preview disappears below the narrow-layout breakpoint, all setting cards remain usable, and vertical scrolling still reaches the final field.
5. Right-click a folder background and confirm the command is in the modern menu, not only under `Show more options`.
6. Right-click one selected folder and confirm the same behavior.
7. Confirm no command appears for files, multiple selected folders, This PC, Libraries, ZIP virtual folders, or non-file-system locations.
8. Test grouped, single-direct, and multi-direct modes; toggle, rename, reorder, add, delete, and change the direct agent. Confirm a saved setting is read from the main package `LocalState` file by every COM surrogate. In multi-direct mode, confirm every enabled agent appears independently at the menu root in configured order, no `RightAgent` wrapper or flyout remains, disabled agents stay hidden, and more than 16 enabled agents cannot be saved.
9. Verify Chinese, English, and system language, including menu title and launcher errors.
10. With command shell set to **Automatic**, launch an agent and run `$PSVersionTable.PSVersion`; confirm PowerShell 7 is used when `pwsh.exe` is installed, otherwise Windows PowerShell 5.1 is used.
11. Select PowerShell 7, Windows PowerShell 5.1, and CMD explicitly; launch an agent in each and confirm the selected shell opens. For CMD, verify the working directory with `cd`; for PowerShell, use `Get-Location`.
12. Launch `claude`, `codex`, `kimi web`, and `cursor-agent`; confirm each new Terminal window uses the exact selected folder.
13. Repeat with a folder named `中文 space & (test) 'quote'`.
14. Configure an `https` web action and confirm the default browser opens. Confirm `file:`, `javascript:`, and malformed URLs cannot be saved as enabled actions.
15. Temporarily configure a missing simple command and confirm the launcher error offers **Open settings** without crashing Explorer.
16. Test light, dark, and high-contrast themes; keyboard-only navigation; Narrator labels; and text scaling.
17. Close the settings app and complete a menu launch. Confirm Task Manager has no remaining RightAgent process after the launcher returns.
18. Install `0.1.0.0`, install a higher version over it, then remove the main package. Confirm any independently installed command packages become inert immediately and no modern context-menu command remains visible.

If Explorer retains a cached command after install/upgrade, close all Explorer windows or sign out once. Do not add a background watcher merely to refresh the menu.
