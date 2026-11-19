# Safe local mod archive import

## User-visible workflow

1. The user selects a local ZIP or OIV archive.
2. The user enters a package ID, display name, author, version, official HTTPS source URL, and licence/permission status.
3. The Hub hashes the selected archive and extracts it to the local research library under the current user's LocalAppData directory.
4. The Hub writes a JSON manifest containing the supplied provenance, archive SHA-256, import timestamp, research-only status, and SHA-256 plus byte size for each extracted file.

Import is **not installation**. It does not modify the GTA V installation, execute an installer, load DLLs, edit RPF archives, approve a mod, or enable a package in a profile.

## Archive safety checks

- ZIP and OIV containers only. OIV is a ZIP container, and `.oiv` imports must contain exactly one root-level `assembly.xml`.
- Maximum 2,500 entries, 256 MiB per entry, 1 GiB total uncompressed bytes, and a configurable compression-ratio ceiling.
- Reject rooted, traversal, empty, overlong, reserved Windows, invalid, duplicate, and case-colliding paths.
- Reject symlinks, reparse points, special Unix file types, malformed directory entries, and file/directory path collisions.
- Extract into a newly created staging directory under the import root, then move the complete directory into its deterministic package location.
- Hash the same open archive stream that is parsed, and hash every extracted file.
- For OIV, parse `assembly.xml` with DTD processing prohibited and external XML resolution disabled. Validate the 2.x package envelope (2.0–2.2), GUID, metadata, required colour settings, and `content/` folder.
- Enumerate root file operations (`add`, `replace`, `delete`), nested `archive` and defragmentation operations, XML and PSO XPath patches, and text-file patch commands (`add`, `insert`, `replace`, `delete`). Check required attributes and accepted enum values for each operation.
- Validate source/destination paths and check whether declared payload sources exist under the OIV `content/` directory. Unknown commands, non-RPF7 archive types, and unresolved sources are flagged for manual review.
- Reference used for the read-only parser: [OpenIV Package Format 2.2 specification](https://github.com/OpenIV-Team/OpenIV-PackageFormat/blob/master/specification/versions/2.2.md) and [format overview](https://github.com/OpenIV-Team/OpenIV-PackageFormat). A clean structural report only means that the known manifest operations and payload paths could be parsed; licence, game-build, dependency, runtime and rollback compatibility must still be reviewed by a person.
- On failure, remove the staging directory when possible. If cleanup itself fails, preserve the original error and leave the staging folder for manual inspection.

## External OIV review handoff

For a user-imported OIV, the Hub can optionally open the retained source package in the separate CodeWalker OIV Package Installer. This is a **manual review handoff**, not a Hub deployment adapter.

1. The user obtains the [CodeWalker OIV Package Installer listing](https://www.gta5-mods.com/tools/oiv-package-installer) and selects `CodeWalker.OIVInstaller.exe` in the Hub. The listing currently shows version 2.1.1 (128 MB, dated 1 August 2026); GTA V Legacy requires `OpenIV.asi`. The Hub records the selected executable's file version and SHA-256. The publisher signature is not verified by the Hub; the user must decide whether to trust the third-party download.
2. The Hub verifies the imported package library inventory, checks the retained `.oiv` source hash, re-parses `assembly.xml`, and confirms the manifest still matches the inspection shown to the user.
3. Before opening the GUI, the Hub asks CodeWalker to set its default game folder to the selected GTA V Legacy installation using `--set-game`, then reads it back using `--get-game`. If the returned folder does not match, the handoff stops.
4. CodeWalker opens the OIV for review. The user must inspect the package and choose whether to click Install. The Hub does not execute OIV operations, click Install, alter RPF files itself, or assert that installation succeeded.
5. The package remains `Researching` in the Hub. A successful handoff or a successful third-party installer message does not approve the package, verify in-game behaviour, prove rollback, or enable a profile.

The external installer is not bundled with the Hub. The OIV inspector supports manifest formats 2.0–2.2 and reports the operations it can parse; it does not independently inspect the contents of every RPF archive or establish that each operation is safe for the user's game build.

## Read-only CodeWalker install-history audit

The dashboard can read the backup/install records written by CodeWalker's OIV installer under:

`<GTA V folder>/OIV_CW_Uninstall_Data/<package>/install.log`

It reads package name, description, version, Legacy/Enhanced marker, install timestamp, and the number of recorded file operations. It does not invoke CodeWalker's `--list` command, execute an installer, modify game files, or change the package's `Researching` status.

The audit applies size and directory-count limits, skips reparse-point directories/files, and skips malformed or oversized logs. The resulting list is **installer history only**. It does not prove that a package's files are still present, that no other mod overwrote them, that the package is currently active, that rollback works, or that gameplay is stable. Packages installed without CodeWalker's backup/log feature may not appear.

## Remaining work before installation

- Parse/inspect RPF payload contents and establish a package-specific OpenIV deployment adapter. The current OIV inspector reads operation metadata only; it does not edit, unpack, or write RPF archives and does not execute OIV commands.
- Add a signed or otherwise trusted package-manifest approval step after compatibility, licence, author attribution, archive hash, file destinations and conflicts have been reviewed.
- Add package-specific staging and rollback adapters for RPF/OpenIV and script-loader packages.
- Verify supported GTA V Legacy build ranges and all required loader versions.
- Test clean install, interrupted install, rollback, game launch, missions and save lifecycle on a disposable GTA V installation.

The imported file inventory is evidence for review, not evidence that its code or assets are safe, compatible, licensed, or functional. In particular, the OIV inspector does not execute `<add>`, `<replace>`, `<delete>`, nested `<archive>`, `<xml>` or `<text>` operations, and it does not prove that OpenIV can safely apply them to a real game installation.
