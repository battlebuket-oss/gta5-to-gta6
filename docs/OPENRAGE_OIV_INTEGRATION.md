# OpenRAGE OIV integration

## Upstream tool

The Hub has an optional integration with [OpenRAGE](https://github.com/Two-Play/openrage), an open-source GTA archive and modding tool released under **GPL-3.0-only**. The integration is external: the Hub does not bundle OpenRAGE or any third-party GTA mod binaries in its Windows installer.

Pinned upstream release:
- Version: 0.4.0
- Release: https://github.com/Two-Play/openrage/releases/tag/v0.4.0
- Windows x64 archive: https://github.com/Two-Play/openrage/releases/download/v0.4.0/openrage-v0.4.0-windows-x86_64.zip
- SHA-256: 32F149015A8F807FEEDBF5C2A2EC34FBFDC115F00F1CCEF74A3DFEC7F0FD2C25

When the user selects Download and verify OpenRAGE, the Hub downloads the official archive through its existing HTTPS acquisition service, checks the pinned SHA-256, safely extracts the archive, checks that exactly one regular openrage.exe exists, runs openrage --version, and compares the installed executable with the copy in the original verified archive. The complete upstream archive and extracted payload—including any licence/notice files present upstream—are retained under the current user's Local AppData. The tool is re-verified before OIV operations.

## Supported OIV workflow

The Hub uses upstream CLI commands without shell interpolation:
- openrage --game <GTA V Legacy folder> keys
- openrage --game <GTA V Legacy folder> oiv info <package.oiv>
- openrage --game <GTA V Legacy folder> oiv install <package.oiv> --yes
- openrage --game <GTA V Legacy folder> oiv list
- openrage --game <GTA V Legacy folder> oiv uninstall <exact package group> --yes
- openrage --game <GTA V Legacy folder> undo

The Hub first requires an existing OpenIV mods folder and OpenIV.asi in the selected game root. If these are missing, install OpenIV and initialize its mods folder/ASI through OpenIV ASI Manager; the Hub will not fake or silently install that loader. It then derives archive keys from the selected local GTA V executable using OpenRAGE's key command. The OIV preview is shown before installation. The user must separately confirm that the selected package targets the exact GTA V Legacy build, the author licence and dependencies have been reviewed, and the test copy and Story Mode saves are backed up. OpenRAGE reports each planned action, warning and blocker. If it reports any blocker, the Hub refuses installation. The Hub hashes the OIV before/after preview, repeats the preview immediately before installation, and verifies the package hash again while holding a read-only file handle.

OpenRAGE installs OIV changes into the OpenIV mods folder and records them in its own journal. Its uninstall and undo functions reverse OpenRAGE journal entries. This is deliberately separate from the Hub's transactional ZIP-package install/remove/rollback journal. Do not use the Hub's ZIP Rollback button to undo an OpenRAGE OIV.

## Direct game-folder files

Some OIV packages contain loose files that OpenRAGE would put in the game folder rather than the OpenIV mods folder. Such preview lines require a separate acknowledgement in the Hub. They can include ASI or DLL plugins that will execute when GTA V loads. Only accept files from an author/source you trust. The Hub does not sandbox mod scripts and does not bypass a preview blocker.

## Safety and limitations

- Only the official v0.4.0 Windows x64 release archive is accepted by the current pin.
- OIV package files are user-supplied and are not redistributed by the project.
- The Hub does not scrape mod-host download pages or automatically fetch protected mod files. Download the package from its official author page and select the local .oiv file.
- OpenRAGE's OIV support is limited by the upstream command parser. A blocked PSO operation, unsupported archive operation or invalid path remains blocked; the Hub does not bypass it.
- OIV preview does not prove the package's exact GTA V build compatibility, licence compliance, performance, absence of conflicts with other installed mods or real-game stability.
- The integration does not automatically detect whether an OIV contains GTA VI assets that are missing, incompatible, or licensed only for another title.
- OIV-installed groups are managed through OpenRAGE's own list/uninstall/undo operations. They are not automatically reconciled by Hub profile activation.
- A user should test against a disposable GTA V Legacy copy and back up saves before installing research-stage OIVs.

## Source and licence notices

OpenRAGE source and its GPL-3.0-only licence are available at:
- https://github.com/Two-Play/openrage
- https://github.com/Two-Play/openrage/blob/v0.4.0/LICENSE
- https://github.com/Two-Play/openrage/blob/v0.4.0/THIRD_PARTY_LICENSES.md

This project does not claim authorship of OpenRAGE or of any mod package.
