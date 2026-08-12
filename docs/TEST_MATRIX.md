# Acceptance test matrix

## Automated gates

- Managed tests: defaults detect commands, Windows Terminal detection covers PATH/WindowsApps/missing cases, terminal-shell values normalize safely, IDs/order/direct target are repaired, URL schemes are restricted, and Unicode settings save/load atomically.
- Native tests: Windows argument round-trip covers spaces, Chinese, parentheses, single quotes, ampersands, quotes, and trailing backslashes.
- Native COM smoke test: loads `RightAgent.Shell.dll` without registration, creates its class factory, checks the grouped title/flag, and enumerates agent subcommands.
- Release build: x64 Launcher and Shell compile with warnings as errors; WAP produces one unsigned package before signing.
- Release installer: the pinned Inno Setup compiler hash is verified; the final Setup EXE and embedded MSIX use the expected certificate and RFC 3161 timestamps; the external SHA-256 covers the exact EXE.

## Manual Windows 11 acceptance

1. On a clean standard-user account, double-click Setup and confirm UAC appears before the wizard. Supply administrator approval, then confirm RightAgent is installed for the initiating user rather than the administrator account. Confirm the certificate exists only in Local Machine\Trusted People, not Trusted Root; cancelling UAC must install nothing.
2. Run the same Setup version again and confirm it succeeds without resetting `LocalState/settings.json`.
3. Open RightAgent once. With Windows Terminal installed, confirm no dependency prompt appears. With `wt.exe` unavailable, confirm a localized prompt offers **Open Microsoft Store** and **Later**; Later keeps the settings app usable and the prompt returns after reopening.
4. Resize the settings window from its default size down to the enforced minimum at 100%, 150%, and 200% display scaling. Confirm the main content never moves beyond either window edge, the preview disappears below the narrow-layout breakpoint, all setting cards remain usable, and vertical scrolling still reaches the final field.
5. Right-click a folder background and confirm the command is in the modern menu, not only under `Show more options`.
6. Right-click one selected folder and confirm the same behavior.
7. Confirm no command appears for files, multiple selected folders, This PC, Libraries, ZIP virtual folders, or non-file-system locations.
8. Test grouped and direct mode; toggle, rename, reorder, add, delete, and change the direct agent.
9. Verify Chinese, English, and system language, including menu title and launcher errors.
10. With command shell set to **Automatic**, launch an agent and run `$PSVersionTable.PSVersion`; confirm PowerShell 7 is used when `pwsh.exe` is installed, otherwise Windows PowerShell 5.1 is used.
11. Select PowerShell 7, Windows PowerShell 5.1, and CMD explicitly; launch an agent in each and confirm the selected shell opens. For CMD, verify the working directory with `cd`; for PowerShell, use `Get-Location`.
12. Launch `claude`, `codex`, and `kimi web`; confirm each new Terminal window uses the exact selected folder.
13. Repeat with a folder named `中文 space & (test) 'quote'`.
14. Configure an `https` web action and confirm the default browser opens. Confirm `file:`, `javascript:`, and malformed URLs cannot be saved as enabled actions.
15. Temporarily configure a missing simple command and confirm the launcher error offers **Open settings** without crashing Explorer.
16. Test light, dark, and high-contrast themes; keyboard-only navigation; Narrator labels; and text scaling.
17. Close the settings app and complete a menu launch. Confirm Task Manager has no remaining RightAgent process after the launcher returns.
18. Install `0.1.0.0`, install a higher version over it, then uninstall. Confirm the modern context menu registration follows package lifetime.

If Explorer retains a cached command after install/upgrade, close all Explorer windows or sign out once. Do not add a background watcher merely to refresh the menu.
