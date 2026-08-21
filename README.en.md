# RightAgent

[Chinese](README.md) | **English**

<img src="docs/screenshots/rightagent-logo.png" width="96" alt="RightAgent logo">

**AI coding agents in your Windows 11 right-click menu.**

Current version: v1.3.1 · [MIT License](LICENSE)

```text
Open with RightAgent  >
    Claude Code
    Codex
    Kimi
    Grok
    opencode
    Cursor Agent
```

Right-click a folder or a folder background and open that directory with your preferred AI coding agent. The menu mode, agent list, commands, icons, interface language, and Windows Terminal profile are all managed in one WinUI 3 settings app.

RightAgent has no tray process, background service, telemetry, or automatic updater. Closing the settings window exits the application completely.

![RightAgent settings](docs/screenshots/after-brands.png)

## Features

- **Three menu modes**: a grouped submenu, one direct agent, or every enabled agent placed directly at the context-menu root.
- **Six built-in agents**: Claude Code, Codex, Kimi, Grok, opencode, and Cursor Agent, ready out of the box.
- **Fully customizable**: add, rename, reorder, and toggle agents freely; actions can be terminal commands or `http(s)` URLs, and local images are normalized to ICO.
- **Uses your Windows Terminal profile**: tabs open with the profile's own shell, icon, and colors.
- **Bilingual UI + live preview**: follow the system language or pick one explicitly; the preview pane shows the resulting menu immediately, and a master switch disables the whole menu at once.

| Direct mode | English interface |
| --- | --- |
| ![Direct mode](docs/screenshots/after-direct.png) | ![English interface](docs/screenshots/after-english.png) |

## Installation

Requires Windows 11 x64 (build 22000 or newer) and Windows Terminal (`wt.exe`).

Download `RightAgent-1.3.1-x64-Setup.exe` from the [official GitHub Release](https://github.com/y0ung-jg-1/RightAgent/releases/latest), verify it against the matching `.sha256` file, and double-click Setup. Installation requests administrator approval and trusts the project public certificate in Local Computer\Trusted People; settings live in `%LOCALAPPDATA%\RightAgent`. See the [sideload installation guide](docs/SIDELOAD_INSTALL.en.md) for complete steps and security details.

> The current version registers only the modern Windows 11 context menu. Classic “Show more options” commands are planned for a later phase.

## Build from source

Requires Visual Studio 2026 Community (WinUI app development, C++ desktop development, C++ WinUI tools, MSIX/WAP tools, Windows 11 SDK 26100+) and the .NET 10 SDK; run `.\scripts\Validate-Environment.ps1` after setup. Build, sign, and install a development build in one step:

```powershell
.\scripts\Install-DevBuild.ps1
```

Run `.\scripts\Run-SettingsApp.ps1` to debug only the settings UI without registering the context menu, and `.\scripts\Test.ps1` for the test suite. For details, see the [architecture](docs/ARCHITECTURE.md), [settings schema](docs/SETTINGS_SCHEMA.md), and [release guide](docs/RELEASING.md) docs.

## License

RightAgent is open source under the [MIT License](LICENSE). Built-in agent icons use glyphs from [@lobehub/icons](https://github.com/lobehub/lobe-icons) (MIT), packaged locally and never fetched at runtime. See the [third-party notices](THIRD_PARTY_NOTICES.md) for full copyright notices; third-party trademarks remain the property of their respective owners.
