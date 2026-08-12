# Releasing RightAgent

RightAgent uses GitHub Actions to build and test every change. A version tag
starts a separate release job that imports the project-owned signing key from
the GitHub `release` environment, signs and timestamps the MSIX, verifies the
bundle, and creates a draft GitHub Release.

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

## Release checklist

1. Update `Version` in both package manifests. The build rejects any drift
   between the manifests outside their intentional Name and Publisher fields.
2. Run the full local gate:

       .\scripts\Build.ps1 -Configuration Release -PackageIdentity Release
       .\scripts\Sign-Package.ps1 -Configuration Release -PackageIdentity Release

3. Commit and push the reviewed source to `main`, then wait for CI to pass.
4. Create and push the exact matching tag, for example `v1.0.0` for package
   version `1.0.0.0`.
5. Approve the `release` environment job if protection rules require it.
6. Inspect the draft Release, its Actions log, ZIP contents, certificate
   thumbprint, and SHA-256. Install the ZIP on a clean Windows 11 x64 machine.
7. Publish the draft only after that clean-machine acceptance passes.

The workflow deliberately creates a draft. Pushing a tag does not make the
release public by itself.
