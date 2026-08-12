# RightAgent sideload installation

[Chinese](SIDELOAD_INSTALL.md) | **English**

Starting with v1.0.2, RightAgent is distributed through GitHub Releases as a single-file x64 Setup executable. The installer contains the signed MSIX, its x64 VCLibs dependencies, and the project-owned public certificate. RightAgent is not currently published through Microsoft Store.

## Installation steps

1. Download `RightAgent-version-x64-Setup.exe` and its matching `.sha256` file only from the official RightAgent GitHub Release, then verify the Setup executable's SHA-256.
2. Double-click Setup. Windows requests administrator approval before showing the installer. Cancelling the User Account Control prompt installs nothing.
3. The elevated phase verifies the embedded MSIX and imports only the public RightAgent certificate into Local Computer\Trusted People, never Trusted Root Certification Authorities. Setup then switches back to the Windows user who started it and installs the MSIX for that user.
4. After installation, right-click a folder or folder background. If File Explorer still caches an old menu, close all File Explorer windows or sign out once.

## Security information

Trusting a self-signed certificate allows packages signed by its corresponding release key to install as RightAgent. The installer never contains the private key. Use only the official GitHub Release and verify the SHA-256 before installation.

On a clean PC, the first User Account Control prompt can still say “Unknown publisher” while the project uses a self-signed certificate. A publicly trusted code-signing certificate is required to remove that warning.
