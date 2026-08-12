# RightAgent sideload installation

RightAgent is distributed through GitHub Releases as a single x64 Setup EXE.
The installer contains the signed MSIX, its x64 VCLibs dependencies, and the
project-owned public certificate. RightAgent is not published through Microsoft
Store.

## 安装

1. 只从 RightAgent 官方 GitHub Release 下载
   `RightAgent-版本-x64-Setup.exe` 和同名 `.sha256`，核对 EXE 的 SHA-256。
2. 双击安装器。安装向导出现前，Windows 会请求管理员批准；取消 UAC 就不会
   安装任何内容。
3. 管理员阶段验证内嵌 MSIX 的签名，只把公共 RightAgent 证书导入
   `本地计算机\受信任的人`，不会导入“受信任的根证书颁发机构”。随后安装器
   切回最初发起安装的 Windows 用户，为该用户安装 MSIX。
4. 安装后，右键文件夹或文件夹空白处即可看到 RightAgent。若资源管理器仍缓存
   旧菜单，请关闭全部资源管理器窗口或注销一次。

信任自签名证书意味着：持有对应发布私钥的软件包可作为 RightAgent 安装。
安装器不包含发布私钥。请只使用官方 Release 中的 EXE，并在安装前核对
SHA-256。由于当前使用自签名证书，干净电脑第一次显示 UAC 时仍可能标注
“未知发布者”；取得受信任代码签名后才会消除此提示。

## Install (English)

1. Download `RightAgent-version-x64-Setup.exe` and its `.sha256` file only from
   the official RightAgent GitHub Release, then verify the EXE SHA-256.
2. Double-click Setup. Windows requests administrator approval before showing
   the installer; cancelling UAC installs nothing.
3. The elevated phase verifies the embedded MSIX and imports only the public
   RightAgent certificate into Local Computer\Trusted People, never Trusted Root
   Certification Authorities. Setup then switches back to the Windows user who
   started it and installs the MSIX for that user.
4. Right-click a folder or folder background. If Explorer still caches an old
   menu, close all Explorer windows or sign out once.

Trusting a self-signed certificate allows packages signed by its corresponding
release key to install as RightAgent. The installer never contains the private
key. Use only the official GitHub Release and verify its SHA-256. On a clean PC,
the first UAC prompt can still say **Unknown publisher** while the project uses a
self-signed certificate; a publicly trusted code-signing certificate is required
to remove that warning.
