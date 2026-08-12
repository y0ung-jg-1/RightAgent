# Release decisions

This file records project-owner decisions that affect the RightAgent v1 public
release. It is an engineering release record, not legal advice.

## Decisions recorded 2026-08-12

- Dependency baseline: retain Microsoft Windows App SDK 2.3.1. The project owner
  accepts the documented licensing ambiguity between the stable top-level
  package and its Microsoft.WindowsAppSDK.WinUI 2.3.0 dependency.
- Distribution channel: publish a sideloadable package through GitHub Releases;
  Microsoft Store publication is out of scope for v1.
- Project license: release RightAgent under the MIT License in `LICENSE`.
- Agent icons: retain the current Lobe Icons-derived assets for v1. Their MIT
  copyright notice is preserved in `THIRD_PARTY_NOTICES.md`; third-party
  trademark ownership and the no-endorsement statement remain explicit.
- Sideload identity: use package name RightAgent, publisher CN=RightAgent, and
  a dedicated project-owned self-signed code-signing certificate.
- Signature longevity: apply a SHA-256 RFC 3161 timestamp through DigiCert's
  public timestamp service to every release package.
- Build service: use GitHub-hosted windows-2025 runners. Pull requests and
  ordinary pushes build unsigned packages; only the release
  environment may access the PFX and password used by the tag release job.
- Release format: publish a single administrator-gated x64 Setup EXE plus its
  SHA-256 file. The installer embeds the signed MSIX, x64 dependencies, public
  certificate, license, and notices. Its elevated phase trusts only the public
  certificate; per-user MSIX deployment runs as the user who started Setup.
- Installer toolchain: use the official Inno Setup 6.7.3 compiler downloaded
  from its pinned upstream release and accepted only after an exact SHA-256
  match. Sign and timestamp both the embedded MSIX and the final Setup EXE.
- Repository visibility: publish the RightAgent source repository and its GitHub
  Releases publicly. The project owner explicitly authorized the private-to-public
  change and the GitHub release-environment configuration on 2026-08-12.

## Publication gate

- The tag workflow must complete successfully. Its internal payload and public
  Setup EXE must pass package, signature, timestamp, and checksum verification
  before the draft is published.
- Perform one clean-machine sideload acceptance check before publishing the
  first public Release.
