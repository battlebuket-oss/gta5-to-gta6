# Safe local mod archive import

## User-visible workflow

1. The user selects a local ZIP or OIV archive.
2. The user enters a package ID, display name, author, version, official HTTPS source URL, and licence/permission status.
3. The Hub hashes the selected archive and extracts it to the local research library under the current user's LocalAppData directory.
4. The Hub writes a JSON manifest containing the supplied provenance, archive SHA-256, import timestamp, research-only status, and SHA-256 plus byte size for each extracted file.

Import is **not installation**. It does not modify the GTA V installation, execute an installer, load DLLs, edit RPF archives, approve a mod, or enable a package in a profile.

## Archive safety checks

- ZIP and OIV containers only; OIV is treated as a ZIP container for inspection.
- Maximum 2,500 entries, 256 MiB per entry, 1 GiB total uncompressed bytes, and a configurable compression-ratio ceiling.
- Reject rooted, traversal, empty, overlong, reserved Windows, invalid, duplicate, and case-colliding paths.
- Reject symlinks, reparse points, special Unix file types, malformed directory entries, and file/directory path collisions.
- Extract into a newly created staging directory under the import root, then move the complete directory into its deterministic package location.
- Hash the same open archive stream that is parsed, and hash every extracted file.
- On failure, remove the staging directory when possible. If cleanup itself fails, preserve the original error and leave the staging folder for manual inspection.

## Remaining work before installation

- Inspect and parse OIV manifests and RPF payloads with a verified, supported adapter; do not copy an OIV container or nested RPF directly into the game folder.
- Add a signed or otherwise trusted package-manifest approval step after compatibility, licence, author attribution, archive hash, file destinations and conflicts have been reviewed.
- Add package-specific staging and rollback adapters for RPF/OpenIV and script-loader packages.
- Verify supported GTA V Legacy build ranges and all required loader versions.
- Test clean install, interrupted install, rollback, game launch, missions and save lifecycle on a disposable GTA V installation.

The imported file inventory is evidence for review, not evidence that its code or assets are safe, compatible, licensed, or functional.
