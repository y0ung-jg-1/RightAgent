# Releasing RightAgent

RightAgent uses GitHub Actions to build and test every change. A version tag
starts a separate release job that imports the project-owned signing key from
the GitHub `release` environment, signs and timestamps the MSIX, builds and
signs a single-file administrator-gated Setup EXE, verifies its checksum, and
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

4. The release workflow installs the exact Inno Setup 6.7.3 compiler from its
   pinned upstream URL after checking its SHA-256. For equivalent local release
   builds, install the same version with Windows Package Manager:

       winget install --exact --id JRSoftware.InnoSetup --version 6.7.3 --scope user

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
       .\scripts\Sign-Package.ps1 -Configuration Release -PackageIdentity Release

   Then create the same verified payload and signed Setup executable used by
   GitHub Actions:

       . .\scripts\PackageHelpers.ps1
       $package = Get-RightAgentPackagePath -RepoRoot $PWD -Configuration Release -PackageIdentity Release
       .\scripts\New-ReleaseBundle.ps1 -PackagePath $package -CertificatePath .\.local\signing\RightAgent.cer
       .\scripts\New-SetupExecutable.ps1

3. Commit and push the reviewed source to `main`, then wait for CI to pass.
4. Create and push the exact matching tag, for example `v1.0.0` for package
   version `1.0.0.0`.
5. Approve the `release` environment job if protection rules require it.
6. Inspect the draft Release and Actions log. Confirm its title is exactly the
   version tag (for example `v1.0.2`) without the product name, the release body
   keeps the Chinese and English notes in separate sections, and it contains
   exactly one `RightAgent-version-x64-Setup.exe` and its `.sha256`. Verify the
   Setup signature, timestamp, certificate thumbprint, and SHA-256.
7. Run Setup on a clean Windows 11 x64 standard-user account. Confirm UAC is
   required before the wizard, the public certificate is added only to Local
   Machine\Trusted People, and the MSIX is installed for the user who started
   Setup rather than the administrator account used for UAC approval.
8. Publish the draft only after that clean-machine acceptance passes.

The workflow deliberately creates a draft. Pushing a tag does not make the
release public by itself.
