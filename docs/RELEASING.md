# Releasing RightAgent

RightAgent uses GitHub Actions to build and test every change. A version tag
starts a separate release job that imports the project-owned signing key from
the GitHub `release` environment, signs and timestamps the 16 command
packages, publishes the unpackaged settings app, builds and
signs a single-file per-machine Setup EXE, verifies its checksum, and
creates a draft GitHub Release containing only the EXE and `.sha256` file.

## One-time setup

1. Create the release certificate:

       .\scripts\New-ReleaseCertificate.ps1

2. Make a durable encrypted backup outside the repository and store its
   password separately in a password manager:

       .\scripts\Export-ReleaseCertificateBackup.ps1 -OutputDirectory E:\RightAgent-key-backup

   The automatic `.local\signing\RightAgent.pfx.password.dpapi` recovery file
   works only for the same Windows user profile. It is not a durable disaster
   recovery backup.

3. Use a public repository, or a GitHub plan that supports environments for
   private repositories. Create the `release` environment, add a required
   reviewer when the account setup permits it, and upload the two encrypted
   secrets:

       .\scripts\Set-GitHubSigningSecrets.ps1

   The secret names are `RIGHTAGENT_SIGNING_PFX_BASE64` and
   `RIGHTAGENT_SIGNING_PFX_PASSWORD`. Never use them in the ordinary CI
   workflow.

4. The release workflow restores WiX Toolset 7.0.0 through the installer
   `.wixproj` files (`AcceptEula` is `wix7`). Local release builds need the
   same .NET 10 SDK used by the rest of the repo; `New-SetupExecutable.ps1`
   restores and compiles the per-machine Setup.exe and per-user UserSetup.exe
   Burn bundles. Signing also needs the WiX CLI 7.0.0 (`dotnet tool install
   --global wix --version 7.0.0`).

## Release checklist

1. Update `Version` in both package manifests, `assemblyIdentity` in
   `RightAgent.App/app.manifest`, and the numeric and string versions in
   `RightAgent.Launcher/Launcher.rc` and `RightAgent.Shell/Shell.rc`. The build
   rejects drift between the package manifests outside their intentional Name
   and Publisher fields. Copy `docs/releases/TEMPLATE.md` to
   `docs/releases/vX.Y.Z.md`, replace every placeholder, and write one complete
   Chinese section followed by one complete English section. The tag workflow
   rejects a release without that exact file.
2. Run the full local gate:

       .\scripts\Build.ps1 -Configuration Release -PackageIdentity Release
       .\scripts\Sign-PackageSet.ps1 -Configuration Release -PackageIdentity Release

   Then create the same signed Setup executable used by GitHub Actions:

       .\scripts\New-SetupExecutable.ps1 -CertificatePath .\.local\signing\RightAgent.cer

3. Commit and push the reviewed source to `main`, then wait for CI to pass.
4. Create and push the exact matching tag, for example `v1.0.0` for package
   version `1.0.0.0`.
5. Approve the `release` environment job if protection rules require it.
6. Inspect the draft Release and Actions log. Confirm its title is exactly the
   version tag (for example `v1.0.2`) without the product name, the release body
   keeps the Chinese and English notes in separate sections, and it contains
   exactly one `RightAgent-version-x64-Setup.exe` and its `.sha256`. Verify the
   Setup signature, timestamp, certificate thumbprint, and SHA-256. The release
   job also runs the final Setup silently on its clean hosted runner, rejects any
   installer exception, verifies the unpackaged settings app,
   command slot zero at the expected version, unused command slots left
   unregistered, and all 16 command MSIX files cached, confirms only the
   settings app appears
   in Start, and verifies the certificate-store boundary before uploading assets.
7. Run Setup on a clean Windows 11 x64 account. Confirm the per-machine Setup
   requests administrator approval, adds the certificate only to Local
   Machine\Trusted People, and installs the unpackaged settings app under
   `%ProgramFiles%\RightAgent`. Start a
   second Setup while installation is active and confirm the package-install
   mutex rejects a concurrent command-package install. Run
   Setup again after the certificate is trusted and confirm
   `%LOCALAPPDATA%\RightAgent\settings.json` is preserved. Confirm that
   grouped mode has one RightAgent flyout, while multi-direct mode exposes each
   enabled agent independently at the menu root without a RightAgent wrapper.
8. Publish the draft only after that clean-machine acceptance passes.

The workflow deliberately creates a draft. Pushing a tag does not make the
release public by itself.
