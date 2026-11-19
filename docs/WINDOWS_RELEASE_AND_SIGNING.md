# Windows installer, signing and release procedure

## What the pipeline produces

The `.github/workflows/windows-release.yml` workflow builds the .NET 8 WPF application for Windows x64, runs the test suite, publishes a self-contained application directory, compiles a per-user Inno Setup installer and generates SHA-256 checksums.

Artifacts contain:
- `GtaTransformationHub.exe` and its self-contained runtime files.
- `GtaTransformationHub-Setup-<version>.exe`.
- `SHA256SUMS.txt`.

The installer is per-user and does not request administrator elevation. It installs under the current user's Local AppData Programs directory and creates Start Menu shortcuts. A desktop shortcut is optional.

## Development build

Run the workflow manually from the Actions tab and provide a semantic version such as `0.1.0-preview.1`. Without signing secrets, the workflow produces an **unsigned development artifact**. Windows SmartScreen may warn, and this artifact must not be described as a signed production release.

## Configure code signing

Obtain a legitimate Windows code-signing certificate from a trusted certificate authority. The certificate must be valid for code signing and include its private key. Do not commit the certificate, PFX, password, or private key to the repository.

Add these GitHub Actions repository secrets under Settings → Secrets and variables → Actions:

- `WINDOWS_SIGNING_PFX_BASE64`: Base64 encoding of the PFX file.
- `WINDOWS_SIGNING_PFX_PASSWORD`: PFX password.

A local PowerShell conversion example (run on the certificate owner's machine) is:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes(".\release-signing.pfx")) |
  Set-Clipboard
```

Paste the clipboard value directly into the GitHub secret field. Do not send it in chat, issues, pull requests, logs or source files.

When configured, the workflow signs the application executable and installer with SHA-256 and an RFC 3161 timestamp, then verifies the installer signature. A tag-triggered public release fails closed if the signing secrets are absent. Manual workflow runs without a certificate are development-only and remain unsigned.

The certificate subject and expiry must be reviewed before release. The signing identity must match the publisher identity shown in the installer. Renewals and certificate rotation require an explicit release-process update.

## Create a release

1. Confirm the target commit is on the reviewed release branch.
2. Confirm the latest Windows build and all automated tests pass.
3. Configure and validate the signing secrets.
4. Create and push a version tag, for example `v0.1.0`.
5. Wait for the tag-triggered workflow to complete.
6. Verify the GitHub Release contains the signed installer and checksum file.
7. Download the installer on a clean Windows x64 machine, verify its Authenticode signature and SHA-256, install, launch, upgrade and uninstall it.
8. Retain the workflow run, installer, checksum, test evidence and release notes.

## Release gates not automated by this pipeline

A successful build and signed installer do not establish GTA V compatibility. Before calling a release production-ready, the team must still complete:

- Real GTA V Legacy launch tests through supported Steam and Rockstar Games Launcher configurations.
- Disposable-copy installation, interrupted-transaction recovery, rollback and uninstall tests on each declared game build.
- Vanilla executable, RPF, launcher and save preservation checks.
- Tested mod compatibility combinations and documented dependency/conflict matrix.
- Repeatable in-game frame-time, FPS, RAM, VRAM and stability measurements on documented hardware.
- Full terrestrial and aquatic ecology inventory validation before enabling any ecology-sensitive package.
- UI, map, pause, settings and HUD native-action tests, including controller and keyboard navigation.
- Accessibility, clean-machine installation, upgrade/downgrade and uninstall tests.
- Third-party asset licence and redistribution review.

Do not use the user's only GTA V installation as the test target. Use a disposable copy and independent save backups. Do not redistribute third-party mod binaries unless their licences explicitly permit it.
