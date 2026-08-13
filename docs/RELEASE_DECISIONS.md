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
- Initial v1.0.2 release format (superseded on 2026-08-13): publish a single
  administrator-gated x64 Setup EXE plus its SHA-256 file. The installer embeds
  the signed MSIX, x64 dependencies, public certificate, license, and notices.
- Installer toolchain: use the official Inno Setup 6.7.3 compiler downloaded
  from its pinned upstream release and accepted only after an exact SHA-256
  match. Sign and timestamp both the embedded MSIX and the final Setup EXE.
- Repository visibility: publish the RightAgent source repository and its GitHub
  Releases publicly. The project owner explicitly authorized the private-to-public
  change and the GitHub release-environment configuration on 2026-08-12.

## Decision revised 2026-08-13

- Installer privilege boundary: keep the single-file x64 Setup EXE, but run the
  bootstrapper as the initiating Windows user. On first installation it requests
  elevation only for the helper that imports the verified public certificate into
  Local Machine\Trusted People; the original user process performs MSIX deployment.
  Setup and script-level mutexes reject concurrent installation attempts. This
  replaces the v1.0.2 `ExecAsOriginalUser` handoff after that mechanism failed in
  a real installation while the AppX deployment continued in the background.
- Installer progress: keep the indeterminate animation while Setup validates the
  payload or waits for first-install certificate approval. Once MSIX deployment
  begins, stream the percentage reported by Windows `DeploymentProgress` into a
  determinate 0–100% progress bar; do not estimate elapsed time.
- Multi-direct package attribution: beginning with v1.1.1, keep one public Setup
  EXE but embed one visible settings MSIX and 16 independently identified hidden
  command MSIX packages. Windows 11 groups verbs attributed to one package even
  when they use separate application identities, so independent package
  identities are required for genuine root-level commands. Every package is
  signed, version-aligned, installed for the initiating user, and verified by
  CI; only the settings app is visible in Start.

## Publication gate

- The tag workflow must complete successfully. Its internal payload and public
  Setup EXE must pass package, signature, timestamp, and checksum verification
  before the draft is published.
- Perform one clean-machine sideload acceptance check before publishing the
  first public Release.
