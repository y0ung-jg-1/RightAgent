# RightAgent sideload installation

[Chinese](SIDELOAD_INSTALL.md) | **English**

Starting with v1.0.2, RightAgent is distributed through GitHub Releases as a single-file x64 Setup executable. The current installer contains one signed main MSIX, 16 hidden MSIX packages that provide independently attributed root commands, the x64 VCLibs dependencies, and the project-owned public certificate. Users still download and run one EXE, and only one RightAgent entry is visible in Start. RightAgent is not currently published through Microsoft Store.

## Installation steps

1. Download `RightAgent-version-x64-Setup.exe` and its matching `.sha256` file only from the official RightAgent GitHub Release, then verify the Setup executable's SHA-256.
2. Double-click Setup and start the installation. Setup first validates every embedded MSIX and the public certificate while retaining the identity of the Windows user who started it. During deployment, the progress bar displays the combined percentage reported by Windows for the complete package set.
3. On the first installation, Windows requests administrator approval if the certificate is not trusted yet. The elevated helper only imports the public RightAgent certificate into Local Computer\Trusted People, never Trusted Root Certification Authorities. After the helper exits, the original user process installs all packages for the current user. Cancelling User Account Control prevents RightAgent installation. Upgrades do not request elevation again while the certificate remains trusted.
4. After installation, right-click a folder or folder background. If File Explorer still caches an old menu, close all File Explorer windows or sign out once.

## Security information

Trusting a self-signed certificate allows packages signed by its corresponding release key to install as RightAgent. The installer never contains the private key. Use only the official GitHub Release and verify the SHA-256 before installation.

Because the project currently uses a self-signed certificate, Microsoft Defender SmartScreen or another Windows security prompt can still identify the downloaded installer as an unknown publisher on a clean PC. The first-install administrator request is launched by the Windows PowerShell helper. A publicly trusted code-signing certificate is required to remove the installer's unknown-publisher warning.
