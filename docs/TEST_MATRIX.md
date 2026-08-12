# Acceptance test matrix

## Automated gates

- Managed tests: defaults detect commands, Windows Terminal detection covers PATH/WindowsApps/missing cases, terminal-shell values normalize safely, IDs/order/direct target are repaired, URL schemes are restricted, and Unicode settings save/load atomically.
- Native tests: Windows argument round-trip covers spaces, Chinese, parentheses, single quotes, ampersands, quotes, and trailing backslashes.
- Native COM smoke test: loads `RightAgent.Shell.dll` without registration, creates its class factory, checks the grouped title/flag, and enumerates agent subcommands.
- Release build: x64 Launcher and Shell compile with warnings as errors; WAP produces one unsigned package before signing.

## Manual Windows 11 acceptance

1. Install a signed package and open RightAgent once. With Windows Terminal installed, confirm no dependency prompt appears. With `wt.exe` unavailable, confirm a localized prompt offers **Open Microsoft Store** and **Later**; Later keeps the settings app usable and the prompt returns after reopening.
2. Right-click a folder background and confirm the command is in the modern menu, not only under `Show more options`.
3. Right-click one selected folder and confirm the same behavior.
4. Confirm no command appears for files, multiple selected folders, This PC, Libraries, ZIP virtual folders, or non-file-system locations.
5. Test grouped and direct mode; toggle, rename, reorder, add, delete, and change the direct agent.
6. Verify Chinese, English, and system language, including menu title and launcher errors.
7. With command shell set to **Automatic**, launch an agent and run `$PSVersionTable.PSVersion`; confirm PowerShell 7 is used when `pwsh.exe` is installed, otherwise Windows PowerShell 5.1 is used.
8. Select PowerShell 7, Windows PowerShell 5.1, and CMD explicitly; launch an agent in each and confirm the selected shell opens. For CMD, verify the working directory with `cd`; for PowerShell, use `Get-Location`.
9. Launch `claude`, `codex`, and `kimi web`; confirm each new Terminal window uses the exact selected folder.
10. Repeat with a folder named `中文 space & (test) 'quote'`.
11. Configure an `https` web action and confirm the default browser opens. Confirm `file:`, `javascript:`, and malformed URLs cannot be saved as enabled actions.
12. Temporarily configure a missing simple command and confirm the launcher error offers **Open settings** without crashing Explorer.
13. Test light, dark, and high-contrast themes; 100%, 150%, and 200% display scaling; keyboard-only navigation; Narrator labels; and text scaling.
14. Close the settings app and complete a menu launch. Confirm Task Manager has no remaining RightAgent process after the launcher returns.
15. Install `0.1.0.0`, install a higher version over it, then uninstall. Confirm the modern context menu registration follows package lifetime.

If Explorer retains a cached command after install/upgrade, close all Explorer windows or sign out once. Do not add a background watcher merely to refresh the menu.
