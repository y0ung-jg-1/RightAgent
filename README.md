# RightAgent

<img src="docs/screenshots/rightagent-logo.png" width="96" alt="RightAgent logo">

**把 AI 编程 Agent 装进 Windows 11 的右键菜单。**

当前版本：v1.0.1 · [MIT License](LICENSE) · [English](#english) below

```text
使用 RightAgent 打开  >
    Claude Code
    Codex
    Kimi Web
    Grok
    opencode
```

在文件夹空白处（或选中一个文件夹）点右键，就能用你喜欢的 AI agent 打开当前目录。支持分组菜单「使用 RightAgent 打开」，也支持「使用 Claude Code 打开」这样的直达命令。模式、启用的 Agent、顺序、命令、URL、图标、语言、命令 Shell、Windows Terminal 配置文件，全部在 WinUI 3 设置应用里配置。

RightAgent 没有托盘进程、后台服务、遥测或自动更新——窗口关闭即完全退出。

![RightAgent 设置界面](docs/screenshots/after-brands.png)

## 功能特性

- **两种菜单模式**：分组子菜单，或单个 Agent 直达命令。
- **内置五个 Agent**:Claude Code、Codex、Kimi Web、Grok、opencode，开箱即用。
- **完全可定制**：添加、重命名、启停、排序任意 Agent；动作支持终端命令（terminalCommand）或 http/https URL。
- **自定义图标**：本地 PNG/JPG/BMP/ICO 自动规范化为 ICO。
- **双语界面**：跟随系统、简体中文或 English，一键切换。
- **Shell 可选**：自动优先 PowerShell 7、回退 Windows PowerShell 5.1，也可明确选择 PowerShell 7、Windows PowerShell 5.1 或 CMD。
- **总开关**：一键停用/启用整个右键菜单。
- **实时预览**：右侧即时展示菜单最终效果；设置原子写入，损坏自动备份重建。

| 直达模式 | English UI |
| --- | --- |
| ![直达模式](docs/screenshots/after-direct.png) | ![English UI](docs/screenshots/after-english.png) |

## 运行环境

- Windows 11 build 22000 或更新，x64。
- Windows Terminal(`wt.exe`)。

## 安装（GitHub Release 侧载）

从官方 GitHub Release 下载 `RightAgent-版本-x64-Setup.exe` 与同名
`.sha256`，核对 SHA-256 后双击安装。安装器启动时会请求管理员批准；管理员
阶段只验证并导入随包公共证书到“本地计算机\受信任的人”，随后切回发起安装的
Windows 用户完成 MSIX 安装。发布包不包含私钥。完整步骤与安全说明见
[侧载安装说明](docs/SIDELOAD_INSTALL.md)。

### 从源码开发

创建与包清单匹配的 per-user 开发证书，然后构建、签名、安装：

```powershell
.\scripts\New-DevCertificate.ps1
.\scripts\Build.ps1 -Configuration Release
.\scripts\Sign-Package.ps1 -Configuration Release
.\scripts\Install-DevPackage.ps1 -Configuration Release
```

也可以一键完成构建、签名和安装。正常升降级会保留包的 LocalState；如果已经安装相同版本，脚本默认拒绝覆盖，请先提升清单版本。只有明确接受清空当前开发包设置时，才传入 `-ResetInstalledPackage`：

```powershell
.\scripts\Install-DevBuild.ps1
# 明确重置同版本开发安装（会清空该包设置）：
.\scripts\Install-DevBuild.ps1 -ResetInstalledPackage
```

证书文件写入已被 gitignore 的 `.local\signing` 目录，请勿提交或分享 PFX。首次安装时 `Install-DevPackage.ps1` 会请求管理员批准，仅将公共 CER 导入「本地计算机\受信任的人」（Windows MSIX 部署要求），不会加入「受信任的根证书颁发机构」。不再需要时请移除该开发证书。

只想调试设置界面、不碰资源管理器菜单：

```powershell
.\scripts\Run-SettingsApp.ps1
```

## 行为说明

- 右键文件夹空白处使用该文件夹；右键选中的单个文件夹使用被选文件夹。
- 文件、多选、虚拟文件夹、非文件系统位置会自动隐藏/禁用菜单。
- 终端动作会打开新的 Windows Terminal 窗口，并使用所选命令 Shell；自动模式优先 PowerShell 7，找不到时回退 Windows PowerShell 5.1。Agent 退出后窗口保持打开。
- 设置应用启动时会检测 `wt.exe`；未检测到 Windows Terminal 时，会提示用户并提供 Microsoft Store 安装入口。
- URL 动作仅允许 `http` 和 `https`。
- 可执行文件缺失时在打开终端前检测，错误中提供打开 RightAgent 设置的按钮。
- 设置原子写入包 LocalState 的 `settings.json`。

内置 Agent 图标使用 [@lobehub/icons](https://github.com/lobehub/lobe-icons)（MIT）字形，全部本地打包，运行时不联网取图。版权声明见[第三方声明](THIRD_PARTY_NOTICES.md)，商标与公开发版要求见[品牌资产策略](docs/BRAND_ASSETS.md)。

## 构建与测试

```powershell
.\scripts\Test.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
```

构建产物为 `artifacts\package\Release` 下的 x64 MSIX。解决方案包含原生 Core、Launcher、Shell DLL、COM 表面测试和托管设置测试。

开发环境：Visual Studio 2026 Community(WinUI 应用开发、C++ 桌面开发、C++ WinUI 工具、MSIX/WAP 工具、Windows 11 SDK 10.0.26100+)与 .NET 10 SDK。安装完先跑 `.\scripts\Validate-Environment.ps1` 体检。本仓库不需要 Node.js、Electron、Tauri、Rust、数据库或后台服务。

GitHub Actions 的 CI 在 windows-2025 托管 runner 上运行完整测试并构建不带签名的
Release 身份 MSIX；标签发布工作流再从专用的 release environment 读取
签名 Secrets，签名 MSIX，使用固定版本的 Inno Setup 生成并签名单文件
`Setup.exe`，随后生成 SHA-256 并创建草稿 GitHub Release。公开 Release 只包含
安装器和对应校验文件；内部 MSIX、依赖与证书由安装器携带。
发布私钥不会进入普通 push/PR 构建。工作流见
[CI](.github/workflows/ci.yml) 与 [Release](.github/workflows/release.yml)。

## 仓库结构

- `RightAgent.App`:C# WinUI 3 设置应用。
- `RightAgent.Core`：托管 schema、默认值、校验、命令探测与原子持久化。
- `RightAgent.Shell`：原生 `IExplorerCommand` COM DLL,Windows 11 菜单。
- `RightAgent.Launcher`：短命原生进程，负责打开终端或 URL。
- `RightAgent.Native.Core`：共享的原生设置、图标、引号转义与进程辅助。
- `RightAgent.Package`:WAP/MSIX 标识与资源管理器注册。
- `installer`：管理员启动、原用户安装的单文件 Setup EXE 定义。

实现细节见[架构文档](docs/ARCHITECTURE.md)，数据契约见[设置 schema](docs/SETTINGS_SCHEMA.md)，人工验收覆盖见[测试矩阵](docs/TEST_MATRIX.md)，发布操作见[发版指南](docs/RELEASING.md)，v1 发布取舍见[发布决策记录](docs/RELEASE_DECISIONS.md)。

## 许可证

RightAgent 以 [MIT License](LICENSE) 开源。随包第三方组件与图标的版权声明见[第三方声明](THIRD_PARTY_NOTICES.md)；第三方商标仍归各自权利人所有。

> 注：当前版本只在 Windows 11 新右键菜单注册；经典「显示更多选项」菜单的动词计划在下一阶段复用同一套设置与启动器加入。

---

<a id="english"></a>

# RightAgent (English)

<img src="docs/screenshots/rightagent-logo.png" width="96" alt="RightAgent logo">

**AI coding agents in your Windows 11 right-click menu.** Current version: v1.0.1.

Right-click a folder background (or a single selected folder) and open it with your favorite AI agent — either through a grouped `Open with RightAgent` submenu or a direct command such as `Open with Claude Code`. Mode, enabled agents, order, commands, URLs, icons, language, command shell, and Windows Terminal profile are all configured in the WinUI 3 settings app.

RightAgent has no tray process, service, telemetry, automatic updater, or resident background process — closing the window exits completely.

![RightAgent settings](docs/screenshots/after-brands.png)

## Features

- **Two menu modes**: grouped submenu or a single direct agent command.
- **Five built-in agents**: Claude Code, Codex, Kimi Web, Grok, and opencode.
- **Fully customizable**: add, rename, enable/disable, and reorder arbitrary agents; actions are either a terminal command or an http/https URL.
- **Custom icons**: local PNG/JPG/BMP/ICO, normalized to ICO.
- **Bilingual UI**: follow system, 简体中文， or English.
- **Selectable shell**: automatically prefer PowerShell 7 and fall back to Windows PowerShell 5.1, or explicitly choose PowerShell 7, Windows PowerShell 5.1, or CMD.
- **Master switch**: turn the whole context menu on or off.
- **Live preview** and atomic settings writes with backup-and-replace on corruption.

## Requirements

- Windows 11 build 22000 or newer, x64.
- Windows Terminal (`wt.exe`).

## Install (GitHub Release sideload)

Download `RightAgent-version-x64-Setup.exe` and its matching `.sha256` from the
official GitHub Release, verify the SHA-256, then double-click the installer.
Setup requests administrator approval at startup. Its elevated phase only
validates and imports the bundled public certificate into Local Computer\Trusted
People; MSIX installation then runs as the Windows user who started Setup. The
private key is never distributed. See the
[sideload installation guide](docs/SIDELOAD_INSTALL.md).

### Build and install from source

```powershell
.\scripts\New-DevCertificate.ps1
.\scripts\Build.ps1 -Configuration Release
.\scripts\Sign-Package.ps1 -Configuration Release
.\scripts\Install-DevPackage.ps1 -Configuration Release
```

Or build, sign, and install in one step. Normal upgrades and downgrades preserve package LocalState. A same-version replacement is rejected by default; increment the manifest version, or explicitly opt into erasing that development package's settings:

```powershell
.\scripts\Install-DevBuild.ps1
# Explicitly reset a same-version development install:
.\scripts\Install-DevBuild.ps1 -ResetInstalledPackage
```

Certificates are written beneath the gitignored `.local\signing` directory — never commit or share the PFX. To hack on the settings UI only, run `.\scripts\Run-SettingsApp.ps1`.

## Behavior

- Right-clicking a folder background uses that folder; right-clicking one selected folder uses the selection.
- Files, multiple selections, virtual folders, and non-file-system locations are hidden/disabled.
- Terminal actions open a new Windows Terminal window with the selected command shell. Automatic mode prefers PowerShell 7 and falls back to Windows PowerShell 5.1; the window remains open after the agent exits.
- On settings-app startup, RightAgent checks for `wt.exe`; if Windows Terminal is unavailable, it offers an official Microsoft Store installation link.
- URL actions permit only `http` and `https`.
- A missing executable is detected before Terminal opens; errors offer a button to open RightAgent settings.
- Settings are atomically written to the package LocalState `settings.json`.

Built-in agent icons use glyphs from [@lobehub/icons](https://github.com/lobehub/lobe-icons) (MIT), packaged locally — nothing is fetched at runtime. See the [third-party notices](THIRD_PARTY_NOTICES.md) and [brand asset policy](docs/BRAND_ASSETS.md).

## Build and test

```powershell
.\scripts\Test.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
```

Toolchain: Visual Studio 2026 Community (WinUI, C++ desktop, C++ WinUI tools, MSIX/WAP, Windows 11 SDK 10.0.26100+) and .NET 10 SDK. Run `.\scripts\Validate-Environment.ps1` after setup. No Node.js, Electron, Tauri, Rust, database, or background service required.

GitHub Actions CI runs the full test suite and builds an unsigned release-
identity MSIX on the windows-2025 hosted runner. The tag workflow then reads
the dedicated signing secrets only from the release environment,
signs the MSIX, uses a pinned Inno Setup compiler to build and sign a single-file
`Setup.exe`, creates its SHA-256 file, and opens a draft GitHub Release. Public
Releases contain only the installer and checksum; the installer carries the
MSIX, dependencies, and public certificate. The signing key is never exposed to
ordinary push or pull-request builds. See [CI](.github/workflows/ci.yml) and
[Release](.github/workflows/release.yml).

## Repository map

- `RightAgent.App`: C# WinUI 3 settings application.
- `RightAgent.Core`: managed schema, defaults, validation, command detection, and atomic persistence.
- `RightAgent.Shell`: native `IExplorerCommand` COM DLL for the Windows 11 menu.
- `RightAgent.Launcher`: short-lived native process that opens Terminal or a URL.
- `RightAgent.Native.Core`: shared native settings, icon, quoting, and process helpers.
- `RightAgent.Package`: WAP/MSIX identity and Explorer registration.
- `installer`: single-file Setup definition with elevated trust and original-user deployment.

Details: [architecture](docs/ARCHITECTURE.md) · [settings schema](docs/SETTINGS_SCHEMA.md) · [test matrix](docs/TEST_MATRIX.md) · [release guide](docs/RELEASING.md) · [v1 release decisions](docs/RELEASE_DECISIONS.md).

## License

RightAgent is open source under the [MIT License](LICENSE). See the [third-party notices](THIRD_PARTY_NOTICES.md) for bundled components and icon copyright notices; third-party trademarks remain the property of their respective owners.

## Platform references

- [Integrate a packaged app with File Explorer](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)
- [IExplorerCommand](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorercommand)
- [Windows Terminal command-line arguments](https://learn.microsoft.com/en-us/windows/terminal/command-line-arguments)
- [Set up the Windows App SDK development environment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/set-up-your-development-environment)

> Note: v1.0.1 registers only the modern Windows 11 menu. Classic "Show more options" verbs are planned for a later phase, reusing the same settings and launcher.
