# RightAgent sideload installation

[Chinese](SIDELOAD_INSTALL.md) | **English**

Starting with v1.0.2, RightAgent is distributed through GitHub Releases as a single-file x64 Setup executable. The default `Setup.exe` is per-machine (`%ProgramFiles%\RightAgent`); `UserSetup.exe` is per-user. The Setup UI follows the Windows display language. Both SKUs embed 16 hidden MSIX packages that provide independently attributed root commands and include the project-owned public certificate. Settings live at `%LOCALAPPDATA%\RightAgent`. Users still download and run one EXE, and only one RightAgent entry is visible in Start. RightAgent is not currently published through Microsoft Store.

## Installation steps

1. Download `RightAgent-version-x64-Setup.exe` and its matching `.sha256` file only from the official RightAgent GitHub Release, then verify the Setup executable's SHA-256. Use `UserSetup.exe` for a current-user install.
2. Double-click Setup and start the installation. The per-machine SKU requests administrator approval. Setup validates every embedded MSIX and the public certificate. The wizard is Chinese or English according to the Windows display language.
3. First install imports the public RightAgent certificate into Local Computer\Trusted People, never Trusted Root Certification Authorities, then registers only the command packages required by the current menu. Cancelling User Account Control prevents RightAgent installation. Uninstall it from Apps & features.
4. After installation, right-click a folder or folder background. Setup refreshes File Explorer to match the current menu. If the menu is still stale, close all File Explorer windows or sign out once.
5. Uninstall from Apps & features removes the settings app, registered command packages, settings and cache under `%LOCALAPPDATA%\RightAgent`, and the project certificate from Local Computer\Trusted People, then refreshes Explorer. Running the same Setup again, or a major upgrade, keeps `settings.json`.

## Security information

Trusting a self-signed certificate allows packages signed by its corresponding release key to install as RightAgent. The installer never contains the private key. Use only the official GitHub Release and verify the SHA-256 before installation.

Because the project currently uses a self-signed certificate, Microsoft Defender SmartScreen or another Windows security prompt can still identify the downloaded installer as an unknown publisher on a clean PC. The first-install administrator request is launched by the Windows PowerShell helper. A publicly trusted code-signing certificate is required to remove the installer's unknown-publisher warning.
