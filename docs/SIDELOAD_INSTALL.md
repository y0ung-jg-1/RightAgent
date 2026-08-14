# RightAgent 侧载安装说明

**简体中文** | [English](SIDELOAD_INSTALL.en.md)

RightAgent 从 v1.0.2 起通过 GitHub Release 提供单文件 x64 安装器。公开 `Setup.exe` 是本机安装（`%ProgramFiles%\RightAgent`）。安装器界面跟随 Windows 显示语言。安装器内含 16 个用于独立根菜单命令的隐藏 MSIX 和项目公共证书；设置写在 `%LOCALAPPDATA%\RightAgent`。用户仍只需下载并运行一个 EXE，开始菜单中也只显示一个 RightAgent。RightAgent 当前不通过 Microsoft Store 发布。

## 安装步骤

1. 只从 RightAgent 官方 GitHub Release 下载 `RightAgent-版本-x64-Setup.exe` 和同名 `.sha256` 文件，并核对安装器的 SHA-256。
2. 双击安装器并开始安装。本机版会请求管理员批准。安装器会验证全部内嵌 MSIX 与公共证书；界面为中文或英文，取决于 Windows 显示语言。
3. 首次安装会把公共 RightAgent 证书导入“本地计算机\受信任的人”，不会导入“受信任的根证书颁发机构”，然后只注册当前菜单需要的命令包。取消用户账户控制提示后不会安装 RightAgent。可在“应用和功能”中卸载。
4. 安装完成后，右键文件夹或文件夹空白处即可看到 RightAgent。安装器会刷新资源管理器以匹配当前菜单；若菜单仍未更新，请关闭全部资源管理器窗口或注销一次。
5. 在“应用和功能”中卸载会删除设置应用、已注册的命令包、`%LOCALAPPDATA%\RightAgent` 中的设置和缓存，以及“本地计算机\受信任的人”里的项目证书，并刷新资源管理器。同一版本上再跑一次安装器，或大版本升级，会保留 `settings.json`。

## 安全说明

信任自签名证书意味着：由对应发布私钥签名的软件包可以作为 RightAgent 安装。安装器不包含发布私钥。请只使用官方 Release 中的安装器，并在安装前核对 SHA-256。

由于当前使用自签名证书，干净电脑首次运行下载的安装器时，Microsoft Defender SmartScreen 或 Windows 安全提示仍可能将项目标记为未知发布者。本机 Setup 会请求管理员批准。取得受公共信任的代码签名证书后，才能消除安装器的未知发布者提示。
