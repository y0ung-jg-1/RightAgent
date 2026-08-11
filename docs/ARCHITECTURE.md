# Architecture

```mermaid
flowchart LR
    E["Explorer.exe / COM surrogate"] -->|"IExplorerCommand"| S["RightAgent.Shell.dll"]
    S -->|"--agent ID --cwd PATH"| L["RightAgent.Launcher.exe"]
    L -->|"terminalCommand"| W["Windows Terminal + PowerShell"]
    L -->|"url"| B["Default browser"]
    A["RightAgent.App.exe"] -->|"atomic write"| J["LocalState/settings.json"]
    S -->|"read only"| J
    L -->|"read only"| J
```

## Process boundaries

- `RightAgent.Shell.dll` runs in the packaged COM surrogate. It reads a small local JSON file, computes titles/icons/state, resolves one local folder, and starts the launcher. It never opens a network connection, runs an agent command, displays application UI, or writes settings.
- `RightAgent.Launcher.exe` is a GUI-subsystem executable. It validates the request and configuration, starts Windows Terminal or the default browser, reports actionable local errors, then exits.
- `RightAgent.App.exe` is the only settings writer. It is opened from Start or the `rightagent://settings` protocol and exits when its window closes.

There is no service, tray app, startup task, scheduled task, watcher, or resident broker.

## Explorer contract

The MSIX manifest registers one CLSID for `Directory` and `Directory\Background` through `windows.fileExplorerContextMenus` and registers the DLL as a `windows.comServer` surrogate class.

The root command returns `ECF_HASSUBCOMMANDS` in grouped mode and enumerates enabled agents in configured order. In direct mode it returns `ECF_DEFAULT` and invokes the selected enabled agent. With no enabled agent, or when the target is not one local file-system folder, the state is hidden.

For a selected folder, the path comes from `IShellItemArray` with `SIGDN_FILESYSPATH`. For a folder background, the handler resolves the current `IFolderView` through `IObjectWithSite`. The launcher path is resolved next to the loaded DLL; no registry lookup or current-directory assumption is used.

## Launch contract

The stable process boundary is:

```text
RightAgent.Launcher.exe --agent <agent-id> --cwd <absolute-directory>
```

Each argument is encoded with the Windows `CommandLineToArgvW` quoting rules. The working directory is never interpolated into the user command. Terminal actions are passed as separate arguments with this semantic shape:

```text
wt.exe -w new new-tab [-p <profile>] -d <directory> powershell.exe -NoLogo -NoExit -Command <configured-command>
```

Only the user-authored command is evaluated by PowerShell. RightAgent runs without elevation and inherits the current user's environment.

## Data and assets

Packaged components share `%LOCALAPPDATA%\Packages\<package-family>\LocalState\settings.json`. Unpackaged developer runs fall back to `%LOCALAPPDATA%\RightAgent\settings.json`. `RIGHTAGENT_SETTINGS_PATH` may override the location for automated tests only.

Built-in icon references use `builtin:<key>` and resolve to package-local ICO/SVG files. Custom files are copied into `LocalState\Icons` and stored as `local:Icons/<file>`. Native validation rejects absolute, parent-relative, or network icon paths.
