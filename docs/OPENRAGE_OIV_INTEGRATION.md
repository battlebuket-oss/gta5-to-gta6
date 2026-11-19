# OpenRAGE OIV integration

## Purpose and boundary

The Hub can use a separately installed, checksum-pinned OpenRAGE v0.4.0 Windows x64 command-line tool to inspect and install user-supplied prebuilt OpenIV OIV packages. This is an optional adapter for existing community packages. It is not a GTA VI engine conversion and does not create new game assets.

OpenRAGE is downloaded at runtime from its upstream release. Its binaries and third-party mod packages are not bundled into this repository. The Hub's own copy-only deployment service remains separate from OpenRAGE's OIV operations.

## Toolchain acquisition and verification

The application pins:

- Version: `0.4.0`
- Upstream release: [Two-Play/OpenRAGE v0.4.0](https://github.com/Two-Play/openrage/releases/tag/v0.4.0)
- Windows x64 archive: `openrage-v0.4.0-windows-x86_64.zip`
- SHA-256: `32F149015A8F807FEEDBF5C2A2EC34FBFDC115F00F1CCEF74A3DFEC7F0FD2C25`

The download path uses the Hub's HTTPS acquisition service, which rejects non-public address resolution and unpinned archive contents. The toolchain service independently checks the archive digest, extracts into a staging directory with path/size/type checks, finds exactly one CLI executable, verifies the executable against the copy in the pinned archive, and runs the version command. Existing toolchain folders are replaced through a staging/previous-folder move with recovery on failure.

The Hub does not verify a code-signing certificate for OpenRAGE. The pinned archive digest is the integrity trust anchor used by this integration.

## Game and package preflight

Before an OIV preview or operation, the Hub:

1. Revalidates the selected `GTA5.exe` through the GTA V Legacy installation service.
2. Requires the selected installation to contain the OpenIV `mods` folder and `OpenIV.asi`.
3. Validates the selected OIV path and checks the toolchain installation and CLI version.
4. Ensures the game-key setup required by OpenRAGE is available.
5. Recomputes the selected OIV SHA-256 before and after the dry-run, and rejects a package that changes during inspection.

The user first imports an OIV into the Hub's local research library. The importer retains the original OIV, extracts a bounded inspection copy, records provenance, computes archive and per-file hashes, and parses `assembly.xml` without executing it. Importing an archive never modifies GTA V.

## Preview and install flow

The dashboard offers an explicit **Select for OpenRAGE preview** action for each retained OIV. The user then runs the preview command. The Hub displays OpenRAGE's output, blockers, warnings, and detected game-folder file changes.

Install is enabled only for a current preview with no blockers. Immediately before installation, the Hub:

- verifies the retained source OIV and imported inventory again;
- checks that the package path and SHA-256 still match the preview;
- reruns the OpenRAGE dry-run;
- rejects changed package contents or any newly reported blockers;
- asks for explicit confirmation to install;
- asks for a separate confirmation if the OIV proposes files outside the OpenIV mods folder.

The actual file operations are carried out by the external OpenRAGE process. The Hub does not interpret an exit code as proof that the mod works. After installation, the user must launch Story Mode, test the feature, verify missions and saves, and inspect the tool's uninstall/undo state.

## Installed-package and rollback controls

The Hub exposes these explicit operations against the selected GTA V Legacy installation:

- **List installed OIVs** — reads the OpenRAGE package list.
- **Uninstall group** — requires the exact package-group name reported by the list and explicit confirmation.
- **Undo latest change** — invokes OpenRAGE's own latest-change undo after explicit confirmation.

These are OpenRAGE-maintained records and operations. They are not a proof that every file is intact, that no other mod overwrote a file, or that gameplay has returned to vanilla. Back up the game installation and Story Mode saves before package changes.

## Locking and failure behaviour

OpenRAGE operations are blocked if GTA V is running, process state cannot be verified, startup transaction recovery has not completed, or another OpenRAGE operation is active. The UI disables other configuration/import controls while an OpenRAGE operation is in progress.

A failed preview does not install anything. A failed external install command may have made partial changes before returning an error; inspect OpenRAGE's own records and use its uninstall/undo workflow. Do not assume the Hub's independent copy-deployment journal can roll back OpenRAGE operations.

## Explicit limitations

- This integration installs user-supplied OIV packages. It does not establish licence permission, authorship, malicious-code safety, or compatibility with every GTA V Legacy build.
- OIV manifest parsing and a clean OpenRAGE dry-run are necessary preconditions, not gameplay compatibility evidence.
- Not all OpenIV features are guaranteed to be supported by OpenRAGE. OpenRAGE blockers are authoritative for this adapter: the Hub does not bypass them.
- Root-level ASI, DLL, or other game-folder payloads require additional consent and separate review. They can affect the base game and loader chain.
- OpenRAGE installation is not yet reconciled with the saved Mod Control Centre ON/OFF catalogue profile. Catalogue toggles persist desired selections only; they do not invoke this installer.
- The external install path has not been acceptance-tested against the user's real GTA V installation, Story Mode saves, mod combinations, or RTX 4050 laptop.
- The original campaign, Rockstar-proprietary GTA VI missions/dialogue/cutscenes/assets, or exact GTA VI game systems are not reproduced by this OIV adapter.


## GTA VI-Inspired HUD Legacy package: loader and root-plugin gate

The catalogue entry `gta-vi-inspired-hud-single-player` remains `Researching`; a clean OIV preview is not package approval.

For this specific package only, the Hub performs a documented prerequisite presence check before enabling installation. The check looks for:

- `ScriptHookV.dll` and `dinput8.dll`
- `ScriptHookVDotNet-Loader.asi`, `ScriptHookVDotNet.dll`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll`, and `ScriptHookVDotNet.ini`
- `OpenIV.asi` and the `mods/` folder

File presence does not establish runtime compatibility. The HUD's author requires Script Hook V .NET 3.7.0 Nightly 193 or newer and warns that the HUD is supported in English; the Hub cannot authoritatively infer that SHVDN nightly requirement from Windows file version metadata. Immediately before installation, the UI asks the user to confirm that GTA V is configured to English and that the exact Legacy game patch is supported by the installed Script Hook V, with the required SHVDN nightly.

After the OIV is installed into the `mods` folder, the author documents two files that must be beside `GTA5.exe`:

- `ViMinimap.asi`
- `ViPreload.asi`

The Hub's **Deploy VI HUD root plugins** action accepts only those two hard-coded filenames and only reads their source from the selected installation's `mods` folder. It hashes both files, asks before copying them to the game root, backs up different existing root files, and commits through the Hub's journalled deployment engine. It stores the transaction ID in LocalAppData so the user can use **Roll back VI HUD root plugins** after restarting the Hub. Rollback preflights both files and refuses to overwrite either one if it has changed since deployment.

This adapter does not move any other OIV file into the root, does not install generic ASI/DLL packages, and does not make the HUD Tested. The user must still validate Story Mode launch, menus, character switching, missions, saves and rollback on the exact Legacy game build.


## Pinned GTA VI-Inspired HUD Legacy 2.8.2 acquisition

The dashboard can download the author's exact v2.8.2 Legacy release asset:

- Asset: `GTA-VI-Inspired-HUD-2.8.2-Legacy.oiv`
- Upstream release: [v2.8.2](https://github.com/Geckomacabre/GTA-VI-Inspired-HUD/releases/tag/v2.8.2)
- Published: 13 November 2026
- Size: 117,140,655 bytes
- SHA-256: `72B8FE300BD99978756DEF3711DD5AFE4BD5A3B65BD4AEE9E4C8FDF97F250835`

The downloader uses the normal HTTPS acquisition pipeline, checks the pinned digest and exact byte count, then passes the package to the archive quarantine importer. The original OIV is retained and its manifest and extracted file inventory are hashed. An exact matching import already present in the library is reused only after its local inventory is verified. The downloaded temporary copy is removed after import.

This operation downloads and imports only; it does not install the OIV, copy root plugins, mark the package Tested, or promise compatibility. The release's 117 MB size and pinned hash are from the upstream GitHub release metadata.
