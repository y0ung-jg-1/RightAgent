# Acceptance test matrix

## Automated gates

- Managed tests: defaults detect commands, normalization repairs IDs/order/direct target, URL schemes are restricted, and Unicode settings save/load atomically.
- Native tests: Windows argument round-trip covers spaces, Chinese, parentheses, single quotes, ampersands, quotes, and trailing backslashes.
- Native COM smoke test: loads `RightAgent.Shell.dll` without registration, creates its class factory, checks the grouped title/flag, and enumerates agent subcommands.
- Release build: x64 Launcher and Shell compile with warnings as errors; WAP produces one unsigned package before signing.

## Manual Windows 11 acceptance

1. Install a signed package and open RightAgent once.
2. Right-click a folder background and confirm the command is in the modern menu, not only under `Show more options`.
3. Right-click one selected folder and confirm the same behavior.
4. Confirm no command appears for files, multiple selected folders, This PC, Libraries, ZIP virtual folders, or non-file-system locations.
5. Test grouped and direct mode; toggle, rename, reorder, add, delete, and change the direct agent.
6. Verify Chinese, English, and system language, including menu title and launcher errors.
7. Launch `claude`, `codex`, and `kimi web`; in each new Terminal window run `Get-Location` and confirm the exact folder.
8. Repeat with a folder named `中文 space & (test) 'quote'`.
9. Configure an `https` web action and confirm the default browser opens. Confirm `file:`, `javascript:`, and malformed URLs cannot be saved as enabled actions.
10. Temporarily configure a missing simple command and confirm the launcher error offers **Open settings** without crashing Explorer.
11. Test light, dark, and high-contrast themes; 100%, 150%, and 200% display scaling; keyboard-only navigation; Narrator labels; and text scaling.
12. Close the settings app and complete a menu launch. Confirm Task Manager has no remaining RightAgent process after the launcher returns.
13. Install `0.1.0.0`, install a higher version over it, then uninstall. Confirm the modern context menu registration follows package lifetime.

If Explorer retains a cached command after install/upgrade, close all Explorer windows or sign out once. Do not add a background watcher merely to refresh the menu.
