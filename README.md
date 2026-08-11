# RightAgent

RightAgent adds a native Windows 11 context-menu command for opening coding agents from a folder. It supports both:

```text
Open with RightAgent  >
    Claude Code
    Codex
    Kimi Web
```

and a direct command such as `Open with Claude Code`. The mode, enabled agents, order, commands, URLs, icons, language, and Windows Terminal profile are configured in the WinUI 3 settings app.

RightAgent has no tray process, service, telemetry, automatic updater, or resident background process.

## Requirements

- Windows 11 build 22000 or newer, x64.
- Visual Studio 2026 Community with WinUI application development, Desktop development with C++, C++ WinUI tools, MSIX/WAP tools, and Windows 11 SDK 10.0.26100 or newer.
- .NET 10 SDK.
- Windows Terminal (`wt.exe`).

Validate the machine after installing the prerequisites:

```powershell
.\scripts\Validate-Environment.ps1
```

The repository does not require Node.js, Electron, Tauri, Rust, a database, or a background service.

## Build and test

```powershell
.\scripts\Test.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
```

The build produces an unsigned x64 MSIX/AppX under `artifacts\package\Release`. Native Core, Launcher, Shell DLL, COM-surface tests, and managed settings tests are all part of the solution.

## Sign and install an internal build

Create a per-user development certificate whose subject matches the package manifest:

```powershell
.\scripts\New-DevCertificate.ps1
```

Then sign and install:

```powershell
.\scripts\Sign-Package.ps1 -Configuration Release
.\scripts\Install-DevPackage.ps1 -Configuration Release
```

The PFX and CER are written beneath the gitignored `.local\signing` directory. Never commit or share the PFX. `Install-DevPackage.ps1` trusts the CER only for the current user and installs the package only when the user explicitly runs it.

## Behavior

- Right-clicking a folder background uses that folder.
- Right-clicking one selected file-system folder uses the selected folder.
- Files, multiple selections, virtual folders, and non-file-system locations are hidden/disabled.
- Terminal actions open a new Windows Terminal window and keep PowerShell open after the agent exits.
- URL actions permit only `http` and `https` URLs.
- A simple missing executable is detected before Terminal opens; errors offer a button to open RightAgent settings.
- Settings are atomically written to the package LocalState `settings.json`. A damaged file is backed up and replaced when the settings app next opens.

The initial agent icons are local neutral placeholders, not vendor logos. See [brand asset policy](docs/BRAND_ASSETS.md).

## Repository map

- `RightAgent.App`: C# WinUI 3 settings application.
- `RightAgent.Core`: managed schema, defaults, validation, command detection, and atomic persistence.
- `RightAgent.Shell`: native `IExplorerCommand` COM DLL for the Windows 11 menu.
- `RightAgent.Launcher`: short-lived native process that opens Terminal or a URL.
- `RightAgent.Native.Core`: shared native settings, icon, quoting, and process helpers.
- `RightAgent.Package`: WAP/MSIX identity and Explorer registration.

Implementation details are in [architecture](docs/ARCHITECTURE.md), the exact data contract is in [settings schema](docs/SETTINGS_SCHEMA.md), and manual acceptance coverage is in [test matrix](docs/TEST_MATRIX.md).

## Platform references

- [Integrate a packaged app with File Explorer](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)
- [IExplorerCommand](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorercommand)
- [Windows Terminal command-line arguments](https://learn.microsoft.com/en-us/windows/terminal/command-line-arguments)
- [Set up the Windows App SDK development environment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/set-up-your-development-environment)

Phase two will add classic `Show more options` verbs while reusing the same settings and launcher. It is intentionally not registered in this first phase.
