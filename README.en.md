# RightAgent

[Chinese](README.md) | **English**

<img src="docs/screenshots/rightagent-logo.png" width="96" alt="RightAgent logo">

**AI coding agents in your Windows 11 right-click menu.**

Current version: v1.3.0 · [MIT License](LICENSE)

```text
Open with RightAgent  >
    Claude Code
    Codex
    Kimi
    Grok
    opencode
    Cursor Agent
```

Right-click a folder background or a single selected folder and open that directory with your preferred AI coding agent. RightAgent supports a grouped submenu, one direct agent, or multiple direct commands that place every enabled agent at the context-menu root. The WinUI 3 settings app manages the menu mode, enabled agents, order, commands, URLs, icons, interface language, and Windows Terminal profile.

RightAgent has no tray process, background service, telemetry, or automatic updater. Closing the settings window exits the application completely.

![RightAgent settings](docs/screenshots/after-brands.png)

## Features

- **Three menu modes**: a grouped submenu, one direct agent, or multiple direct commands ordered exactly like the enabled agent list.
- **Six built-in agents**: Claude Code, Codex, Kimi, Grok, opencode, and Cursor Agent.
- **Fully customizable**: add, rename, enable, disable, and reorder arbitrary agents; actions can be terminal commands or `http` and `https` URLs.
- **Custom icons**: local PNG, JPG, BMP, and ICO files are normalized to ICO.
- **Bilingual interface**: follow the system language or explicitly select Simplified Chinese or English.
- **Uses the Windows Terminal profile as the shell**: the chosen profile supplies its own shell, icon, and colors. There is no separate command-interpreter setting.
- **Master switch**: turn the complete context menu on or off.
- **Live preview**: see the resulting menu immediately; settings use atomic writes and recover from corrupted files through backup and replacement.

| Direct mode | English interface |
| --- | --- |
| ![Direct mode](docs/screenshots/after-direct.png) | ![English interface](docs/screenshots/after-english.png) |

## Requirements

- Windows 11 x64, build 22000 or newer.
- Windows Terminal (`wt.exe`).

## Installation

Download `RightAgent-1.3.0-x64-Setup.exe` and its matching `.sha256` file from the [official GitHub Release](https://github.com/y0ung-jg-1/RightAgent/releases/latest). Verify the SHA-256 and double-click Setup. The public installer is per-machine: it requests administrator approval and places the settings app in `%ProgramFiles%\RightAgent`. The Setup UI follows the Windows display language (Chinese or English). First install trusts the project public certificate in Local Computer\Trusted People, writes settings to `%LOCALAPPDATA%\RightAgent`, and registers only the command packages required by the current menu. To prevent Windows 11 from grouping multiple direct commands, Setup internally carries 16 hidden command packages; users still download one EXE and see only one RightAgent entry in Start. The private key is never distributed. See the [sideload installation guide](docs/SIDELOAD_INSTALL.en.md) for complete steps and security details.

### Build and install from source

Create a per-user development certificate matching the package manifest, then build, sign, and install:

```powershell
.\scripts\New-DevCertificate.ps1
.\scripts\Build.ps1 -Configuration Release
.\scripts\Sign-PackageSet.ps1 -Configuration Release
.\scripts\Install-DevPackage.ps1 -Configuration Release
```

You can also build, sign, and install in one step. Settings stay in `%LOCALAPPDATA%\RightAgent`.

```powershell
.\scripts\Install-DevBuild.ps1
```

Certificates are written beneath the ignored `.local\signing` directory. Never commit or share the PFX. On first installation, `Install-DevPackage.ps1` requests administrator approval and imports only the public CER into Local Computer\Trusted People, never Trusted Root Certification Authorities. Remove the development certificate when it is no longer needed.

To debug only the settings interface without registering the File Explorer menu, run:

```powershell
.\scripts\Run-SettingsApp.ps1
```

## Behavior

- Right-clicking a folder background uses that folder; right-clicking one selected folder uses the selection.
- Files, multiple selections, virtual folders, and non-file-system locations are hidden or disabled.
- Terminal actions open a new Windows Terminal window using the selected profile's own shell. The Terminal window remains open after the agent exits.
- On startup, the settings app checks for `wt.exe`. If Windows Terminal is unavailable, it explains the requirement and offers the official Microsoft Store installation entry.
- URL actions permit only `http` and `https`.
- Missing executables are detected before Terminal opens; the error dialog offers a button to open RightAgent settings.
- Settings are atomically written to `%LOCALAPPDATA%\RightAgent\settings.json`.

Built-in agent icons use glyphs from [@lobehub/icons](https://github.com/lobehub/lobe-icons) under the MIT License. They are packaged locally and are never fetched at runtime. See the [third-party notices](THIRD_PARTY_NOTICES.md) and [brand asset policy](docs/BRAND_ASSETS.md).

## Build and test

```powershell
.\scripts\Test.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
```

The main x64 MSIX is written beneath `artifacts\package\Release`, with 16 hidden command MSIX packages in its `Commands` subdirectory. The solution contains the native core, launcher, File Explorer extension, COM surface tests, and managed settings tests.

The toolchain requires Visual Studio 2026 Community with WinUI app development, C++ desktop development, C++ WinUI tools, MSIX/WAP tools, and Windows 11 SDK 10.0.26100 or newer, plus .NET 10 SDK. Run `.\scripts\Validate-Environment.ps1` after setup. The repository does not require Node.js, Electron, Tauri, Rust, a database, or a background service.

GitHub Actions continuous integration runs the full test suite and builds an unsigned release-identity package set on the `windows-2025` hosted runner. The tag workflow reads signing secrets only from the dedicated release environment, signs the 16 command MSIX packages, uses WiX 7 to build and sign the per-machine `Setup.exe`, creates its SHA-256 file, and opens a draft GitHub Release. Public Releases contain only the installer and checksum; Setup carries the internal packages, dependencies, and public certificate. The signing key is never exposed to ordinary push or pull-request builds. See the [continuous integration workflow](.github/workflows/ci.yml) and [release workflow](.github/workflows/release.yml).

## Repository map

- `RightAgent.App`: C# WinUI 3 settings application.
- `RightAgent.Core`: managed settings schema, defaults, validation, command detection, and atomic persistence.
- `RightAgent.Shell`: native `IExplorerCommand` COM component for the modern Windows 11 menu.
- `RightAgent.Launcher`: short-lived native process that opens Terminal or a URL.
- `RightAgent.Native.Core`: shared native settings, icon, quoting, and process helpers.
- `RightAgent.Package`: WAP identity used for command-package version and assets, not the settings app users install.
- `RightAgent.CommandPackage`: shared manifest template for the 16 hidden File Explorer command packages.
- `installer`: WiX 7 Burn definition for the public per-machine `Setup.exe`.

Details: [architecture](docs/ARCHITECTURE.md) · [settings schema](docs/SETTINGS_SCHEMA.md) · [test matrix](docs/TEST_MATRIX.md) · [release guide](docs/RELEASING.md) · [v1 release decisions](docs/RELEASE_DECISIONS.md).

## License

RightAgent is open source under the [MIT License](LICENSE). See the [third-party notices](THIRD_PARTY_NOTICES.md) for bundled components and icon copyright notices. Third-party trademarks remain the property of their respective owners.

> The current version registers only the modern Windows 11 context menu. Classic “Show more options” commands are planned for a later phase and will reuse the same settings and launcher.
