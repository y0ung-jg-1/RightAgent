# RightAgent

**简体中文** | [English](README.en.md)

<img src="docs/screenshots/rightagent-logo.png" width="96" alt="RightAgent 图标">

**把 AI 编程助手装进 Windows 11 的右键菜单。**

当前版本：v1.3.1 · [MIT 许可证](LICENSE)

```text
使用 RightAgent 打开  >
    Claude Code
    Codex
    Kimi
    Grok
    opencode
    Cursor Agent
```

在文件夹上或文件夹空白处点击右键，即可用喜欢的 AI 编程助手打开当前目录。菜单模式、助手列表、命令、图标、界面语言和 Windows Terminal 配置文件，都在 WinUI 3 设置应用里统一管理。

RightAgent 没有托盘进程、后台服务、遥测和自动更新；关闭设置窗口后，程序完全退出。

![RightAgent 设置界面](docs/screenshots/after-brands.png)

## 功能特性

- **三种菜单模式**：分组子菜单、单个 Agent 直达，或把全部已启用助手平铺进右键菜单根目录。
- **六个内置助手**：Claude Code、Codex、Kimi、Grok、opencode 和 Cursor Agent，开箱即用。
- **完全可定制**：自由添加、重命名、排序和启停助手；动作支持终端命令或 `http(s)` 网址，本地图片会自动规范化为 ICO。
- **沿用 Windows Terminal 配置文件**：打开标签页时使用该配置文件自己的 Shell、图标和配色。
- **中英双语 + 实时预览**：界面语言可跟随系统；右侧即时展示菜单最终效果，一键总开关随时停用整个菜单。

| 直达模式 | 英文界面 |
| --- | --- |
| ![直达模式](docs/screenshots/after-direct.png) | ![英文界面](docs/screenshots/after-english.png) |

## 安装

需要 Windows 11 x64（22000 或更新）和 Windows Terminal（`wt.exe`）。

从[官方 GitHub Release](https://github.com/y0ung-jg-1/RightAgent/releases/latest)下载 `RightAgent-1.3.1-x64-Setup.exe`，用同名 `.sha256` 文件核对校验值后双击安装。安装需要管理员批准，会把项目公共证书导入“本地计算机\受信任的人”；设置保存在 `%LOCALAPPDATA%\RightAgent`。完整步骤与安全说明见[侧载安装说明](docs/SIDELOAD_INSTALL.md)。

> 当前版本只注册 Windows 11 新右键菜单；经典“显示更多选项”菜单计划在下一阶段加入。

## 从源码开发

需要 Visual Studio 2026 Community（WinUI 应用开发、C++ 桌面开发、C++ WinUI 工具、MSIX/WAP 工具、Windows 11 SDK 26100+）和 .NET 10 SDK，先运行 `.\scripts\Validate-Environment.ps1` 检查环境。一键构建、签名并安装开发版：

```powershell
.\scripts\Install-DevBuild.ps1
```

只调试设置界面、不注册右键菜单时运行 `.\scripts\Run-SettingsApp.ps1`，运行测试用 `.\scripts\Test.ps1`。实现细节见[架构文档](docs/ARCHITECTURE.md)、[设置结构说明](docs/SETTINGS_SCHEMA.md)与[发版指南](docs/RELEASING.md)。

## 许可证

RightAgent 以 [MIT 许可证](LICENSE)开源。内置助手图标使用 [@lobehub/icons](https://github.com/lobehub/lobe-icons)（MIT）字形，随应用本地打包，运行时不联网获取；完整版权声明见[第三方声明](THIRD_PARTY_NOTICES.md)，第三方商标归各自权利人所有。
