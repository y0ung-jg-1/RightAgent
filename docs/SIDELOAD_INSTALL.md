# RightAgent 侧载安装说明

**简体中文** | [English](SIDELOAD_INSTALL.en.md)

RightAgent 从 v1.0.2 起通过 GitHub Release 提供单文件 x64 安装器。安装器内含已签名的 MSIX、x64 VCLibs 依赖和项目公共证书。RightAgent 当前不通过 Microsoft Store 发布。

## 安装步骤

1. 只从 RightAgent 官方 GitHub Release 下载 `RightAgent-版本-x64-Setup.exe` 和同名 `.sha256` 文件，并核对安装器的 SHA-256。
2. 双击安装器。安装向导出现前，Windows 会请求管理员批准；取消用户账户控制提示后，不会安装任何内容。
3. 管理员阶段会验证内嵌 MSIX 的签名，只把公共 RightAgent 证书导入“本地计算机\受信任的人”，不会导入“受信任的根证书颁发机构”。随后，安装器会切回最初发起安装的 Windows 用户，为该用户安装 MSIX。
4. 安装完成后，右键文件夹或文件夹空白处即可看到 RightAgent。若资源管理器仍缓存旧菜单，请关闭全部资源管理器窗口或注销一次。

## 安全说明

信任自签名证书意味着：由对应发布私钥签名的软件包可以作为 RightAgent 安装。安装器不包含发布私钥。请只使用官方 Release 中的安装器，并在安装前核对 SHA-256。

由于当前使用自签名证书，干净电脑第一次显示用户账户控制提示时仍可能标注“未知发布者”。取得受公共信任的代码签名证书后，才能消除此提示。
