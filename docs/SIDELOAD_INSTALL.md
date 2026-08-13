# RightAgent 侧载安装说明

**简体中文** | [English](SIDELOAD_INSTALL.en.md)

RightAgent 从 v1.0.2 起通过 GitHub Release 提供单文件 x64 安装器。当前安装器内含 1 个已签名的主程序 MSIX、16 个用于独立根菜单命令的隐藏 MSIX、x64 VCLibs 依赖和项目公共证书；用户仍只需下载并运行一个 EXE，开始菜单中也只显示一个 RightAgent。RightAgent 当前不通过 Microsoft Store 发布。

## 安装步骤

1. 只从 RightAgent 官方 GitHub Release 下载 `RightAgent-版本-x64-Setup.exe` 和同名 `.sha256` 文件，并核对安装器的 SHA-256。
2. 双击安装器并开始安装。安装器会先验证全部内嵌 MSIX 与公共证书，并始终保留最初发起安装的 Windows 用户身份；进入部署阶段后，进度条会显示整个包集合的真实安装百分比。
3. 首次安装若证书尚未受信任，Windows 会请求一次管理员批准。管理员辅助进程只把公共 RightAgent 证书导入“本地计算机\受信任的人”，不会导入“受信任的根证书颁发机构”；辅助进程退出后，原用户进程继续为当前用户安装全部包。取消用户账户控制提示后不会安装 RightAgent。证书已经受信任的升级不会重复请求管理员权限。
4. 安装完成后，右键文件夹或文件夹空白处即可看到 RightAgent。安装器会刷新资源管理器以匹配当前菜单；若菜单仍未更新，请关闭全部资源管理器窗口或注销一次。

## 安全说明

信任自签名证书意味着：由对应发布私钥签名的软件包可以作为 RightAgent 安装。安装器不包含发布私钥。请只使用官方 Release 中的安装器，并在安装前核对 SHA-256。

由于当前使用自签名证书，干净电脑首次运行下载的安装器时，Microsoft Defender SmartScreen 或 Windows 安全提示仍可能将项目标记为未知发布者；首次信任证书时的管理员批准由 Windows PowerShell 辅助进程发起。取得受公共信任的代码签名证书后，才能消除安装器的未知发布者提示。
