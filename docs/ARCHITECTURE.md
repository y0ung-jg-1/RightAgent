# Architecture

```mermaid
flowchart LR
    E["Explorer.exe / COM surrogate"] -->|"IExplorerCommand"| S["RightAgent.Shell.dll"]
    S -->|"--agent ID --cwd PATH"| L["RightAgent.Launcher.exe"]
    L -->|"terminalCommand"| W["Windows Terminal profile"]
    L -->|"url"| B["Default browser"]
    A["RightAgent.App.exe"] -->|"atomic write"| J["LocalState/settings.json"]
    S -->|"read only"| J
    L -->|"read only"| J
```

## Process boundaries

- `RightAgent.Shell.dll` runs in the packaged COM surrogate. It reads a small local JSON file, computes titles/icons/state, resolves one local folder, and starts the launcher. It never opens a network connection, runs an agent command, displays application UI, or writes settings.
- `RightAgent.Launcher.exe` is a GUI-subsystem executable. It validates the request and configuration, starts Windows Terminal or the default browser, reports actionable local errors, then exits.
- The settings app checks for `wt.exe` after loading. When Windows Terminal is unavailable, it presents a localized installation prompt whose primary action opens the official Microsoft Store product page; choosing Later keeps settings available and the prompt returns on the next app launch.
- `RightAgent.App.exe` is the only settings writer. It is opened from Start or the `rightagent://settings` protocol and exits when its window closes.

There is no service, tray app, startup task, scheduled task, watcher, or resident broker.

The GitHub Release `Setup.exe` is a distribution-only bootstrapper, not a
RightAgent runtime process. It embeds one signed settings MSIX, 16 signed hidden
command MSIX packages, x64 dependencies, and the public release certificate.
This split is internal: users still download and run one Setup executable.
Setup remains under the Windows user who started it, validates every package,
and requests elevation only when a first-install helper must trust the public
certificate in Local Machine\Trusted People. The helper exits before the
original user process deploys the main package, registers the command slots
required by the current settings, and caches all 16 command MSIX files. Fixed
Setup and package-installation mutexes reject concurrent installation
attempts. During deployment, the PowerShell host forwards Windows
`DeploymentProgress` percentages for the packages it registers to Setup, which
replaces its initial indeterminate animation with a determinate progress bar.

## Explorer contract

RightAgent builds 16 independently identified command packages. Each package registers one ordered CLSID slot for both `Directory` and `Directory\Background` through `windows.fileExplorerContextMenus`. Separate package identities are required because Windows 11 groups multiple commands attributed to one package into an app flyout; separate `Application` elements inside one package are not sufficient. Every command application uses `AppListEntry="none"`, so only the primary settings application is visible in Start. All classes are served from identical copies of the same Shell DLL through packaged `windows.comServer` surrogates. Slot zero also owns the grouped submenu and single-direct modes.

Setup still embeds all 16 command MSIX files and copies them into the main package `LocalState/CommandPackages` cache, but it only registers the slots required by the current settings: slot zero on a clean machine, none when the menu is off or no agent is enabled, slot zero for grouped and single-direct, and one slot per enabled agent in multi-direct mode. The settings app keeps that occupancy in sync after later mode or agent changes, then restarts Explorer so Windows 11 drops stale "Loading..." placeholders. Unused slots must stay unregistered rather than merely hidden, because Explorer paints a placeholder for every registered packaged verb before it calls `GetState`. Hidden slots still return `ECS_HIDDEN` and refuse a title if Explorer instantiates them.

Slot zero returns `ECF_HASSUBCOMMANDS` in grouped mode and enumerates enabled agents in configured order. In single-direct mode it returns `ECF_DEFAULT` and invokes the selected enabled agent. In multi-direct mode every installed slot returns `ECF_DEFAULT` and resolves the enabled agent at the same configured index. This produces genuine independent root commands; `EnumSubCommands` is not used to simulate flattening. Multi-direct mode is limited to the 16 command slots. With no enabled agent, or when the target is not one local file-system folder, the relevant state is hidden.

For a selected folder, the path comes from `IShellItemArray` with `SIGDN_FILESYSPATH`. For a folder background, the handler resolves the current `IFolderView` through `IObjectWithSite`. The launcher path is resolved next to the loaded DLL; no registry lookup or current-directory assumption is used.

## Launch contract

The stable process boundary is:

```text
RightAgent.Launcher.exe --agent <agent-id> --cwd <absolute-directory>
```

Each argument is encoded with the Windows `CommandLineToArgvW` quoting rules. The working directory is never interpolated into the user command. Terminal actions are passed as separate arguments with this semantic shape:

```text
wt.exe -w new new-tab -p <profile> -d <directory> --appendCommandLine -- <profile-shell-suffix>
```

The selected Windows Terminal profile is the shell. RightAgent does not pick `pwsh`/`cmd` separately and does not replace the profile command line. `--appendCommandLine` keeps that profile's executable (PowerShell, CMD, Git Bash, VsDevCmd, WSL) and only appends the agent command. PowerShell families get `-NoLogo -NoExit -EncodedCommand`; a bare `cmd.exe` profile gets `/D /K`; a profile that already runs `cmd /k` (including Developer Command Prompt) gets `&& <command>`; Bash gets `-c "<command>; exec bash -i -l"`. PowerShell command text is Base64-encoded as UTF-16LE so Windows Terminal cannot reinterpret semicolons inside the user command. The settings app lists visible profiles from the user's Terminal `settings.json` and stores either `null` for “use Terminal default” or the selected profile GUID. When `terminalProfile` is empty, the launcher still passes `-p` with Terminal's `defaultProfile`.

Only the user-authored command is evaluated by the selected shell. RightAgent runs without elevation and inherits the current user's environment.

## Data and assets

The settings app writes its own `ApplicationData.Current.LocalFolder`. Native packaged components map a command package family such as `RightAgent.Command00_<publisher-id>` back to the main `RightAgent_<publisher-id>` family and read `%LOCALAPPDATA%\Packages\<main-package-family>\LocalState\settings.json`. This keeps the settings app, COM surrogate, and launcher on one file despite their independent package identities. A surviving command package also verifies that the main package is still registered and stays inert after the main app is removed. Unpackaged developer runs fall back to `%LOCALAPPDATA%\RightAgent\settings.json`. `RIGHTAGENT_SETTINGS_PATH` may override the location for automated tests only.

Built-in icon references use `builtin:<key>` and resolve to package-local ICO/SVG files. Custom files are copied into `LocalState\Icons` and stored as `local:Icons/<file>`. Native validation rejects absolute, parent-relative, or network icon paths.
