# RightAgent sideload installation

RightAgent v1 is distributed through GitHub Releases as an x64 MSIX signed by
the project-owned self-signed certificate included in the release bundle.
RightAgent is not published through Microsoft Store.

## 安装

1. 只从 RightAgent 官方 GitHub Release 下载 RightAgent-版本-x64.zip 和
   同名 .sha256，核对 ZIP 的 SHA-256。
2. 完整解压 ZIP；不要只在压缩包预览中运行脚本。
3. 检查 Install-RightAgent.ps1 后，在该目录打开 PowerShell：

       Set-ExecutionPolicy -Scope Process Bypass
       .\Install-RightAgent.ps1

4. 首次安装会请求管理员批准，把随包的公共 RightAgent.cer 导入
   本地计算机\受信任的人。脚本不会导入“受信任的根证书颁发机构”，也不包含
   发布私钥。
5. 安装后，右键文件夹或文件夹空白处即可看到 RightAgent。若资源管理器仍缓存
   旧菜单，请关闭全部资源管理器窗口或注销一次。

信任自签名证书意味着：持有对应发布私钥的软件包可作为 RightAgent 安装。
请只使用官方 Release 中的 ZIP，并在安装前核对 SHA-256。

## Install (English)

1. Download RightAgent-version-x64.zip and its .sha256 file only from the
   official RightAgent GitHub Release, then verify the ZIP SHA-256.
2. Extract the whole ZIP. Do not run the installer from inside the archive
   preview.
3. Review Install-RightAgent.ps1, open PowerShell in that directory, and run:

       Set-ExecutionPolicy -Scope Process Bypass
       .\Install-RightAgent.ps1

4. The first install requests administrator approval to add the bundled public
   RightAgent.cer to Local Computer\Trusted People. It is not added to Trusted
   Root Certification Authorities, and the release never contains the private
   key.
5. Right-click a folder or folder background. If Explorer still caches an old
   menu, close all Explorer windows or sign out once.

Trusting a self-signed certificate allows packages signed by its corresponding
release key to install as RightAgent. Use only the official GitHub Release and
verify its SHA-256 before installation.
