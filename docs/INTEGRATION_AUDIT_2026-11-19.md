# Integration audit — 19 November 2026

## Scope and non-negotiable limits

This project is a GTA V Legacy Story Mode mod manager and transformation profile. It is not a GTA VI engine port. It must combine existing third-party mods through declared manifests and reversible file operations; it must not recreate Rockstar proprietary code, assets, maps, missions, or the GTA VI campaign.

The user's target hardware is an RTX 4050 6 GB laptop GPU at 1080p. This is a test target, not a performance guarantee. No FPS claim is valid until measured on the actual machine with a recorded game build, driver, graphics settings, mod profile, VRAM use, and frame-time capture.

## New research candidates (not install-approved)

| Area | Candidate | Evidence | Current gate |
|---|---|---|---|
| HUD / minimap / character wheel | [GTA VI-Inspired HUD single-player](https://github.com/Geckomacabre/GTA-VI-Inspired-HUD) | Upstream release and Legacy guide identify version 2.8.2 and a Legacy-only OIV. Documents a GTA VI-styled HUD, 3D minimap, character switcher, VI pause-menu art, optional VI start/loading screens, settings overlay, heists and dive-shop gameplay. Requires Script Hook V, Script Hook V .NET 3.7.0 Nightly 193+, OpenIV/ASI Loader and English game language. Jason/Lucia models are separate; Lucia is paid creator work. The upstream package licence states personal use and lists mixed third-party licences. | Researching. No Hub acquisition/approval, exact target-build validation, licence clearance for any redistribution, archive hash pinning, root ASI deployment, RPF/rollback adapter, or RTX 4050 test. Use author-hosted package only; never bundle the OIV or character assets. |
| Aquatic population | [Sea Life V](https://www.gta5-mods.com/misc/sea-life-v) | Source describes higher ocean populations of sharks, fish, killer whales, stingrays and dolphins. Original release is from 2017. | Researching. Legacy-era file; current-build compatibility, population metadata conflicts, and real spawn behaviour unverified. |
| Terrestrial vegetation | [Vanilla Vegetation Overhaul](https://www.gta5-mods.com/misc/realistic-vegetation-new-gen-tree-models-gta-5-legacy) | Listing includes a 5.0 beta and marks the project deprecated/discontinued. | Researching only. Do not install until archive, LODs, coverage, licences, build compatibility and VRAM use are audited. |
| Pause/settings API framework | [Pause Menu Library 1.0.1](https://www.gta5-mods.com/tools/pause-menu-library) | Framework lets mods add native-rendered pause-menu pages, tabs and settings. It explicitly does nothing on its own and requires a separate mod using its API. | Researching. Author licence forbids redistributing its `.asi`; link-only user acquisition, exact Legacy build and a dependent UI mod must be reviewed. This is not a complete menu replacement. |
| GTA VI-style standalone HUD | [GTA VI Styled Minimap & HUD (Legacy)](https://www.gta5-mods.com/scripts/gta-vi-styled-minimap-legacy) | Current source page says the file is no longer available. | Blocked for acquisition unless the author republishes it; do not select it for a build.

No candidate above is marked Tested or approved for one-click deployment. Source-page existence is not compatibility evidence.

## UI surface coverage

- HUD/minimap: the 2.8.2 Legacy OIV is a concrete candidate with a 3D minimap, GTA VI-inspired HUD, wanted box/lights, and optional loading/start screens. It is not installed or validated in this Hub. Upstream requires the English game language and specific script loaders.
- Pause/menu: the OIV description says its `vihud` and `vihudfonts` packs include VI pause-menu artwork and fonts, plus a small VI pause-menu feature set. It does not document a complete replacement of every pause-menu workflow or every native settings page. Its HUD OPTIONS overlay is a configurable mod overlay, not a universal replacement for GTA V settings.
- Map: the package has a custom 3D minimap and supports map pictures from compatible world-map mods; do not describe it as a full world-map replacement.
- Character switcher: its wheel includes Jason/Lucia slots, but neither character model ships in the HUD. Both require separate user-sourced model packs; licence and compatibility must be checked independently.
- Loading/title: its Legacy package description lists a VI landing/start screen and loading screens; the separate optional loading-screen OIV/uninstaller must be tested separately if used.
- Pause Menu Library 1.0.1 can provide API pages for separate mods, but on its own changes nothing and cannot be redistributed under its current licence.
- GTA VI story: exact GTA VI campaign content is not a feasible lawful GTA V mod-pack deliverable. The product can catalogue separately authored fan-made missions/story expansions only, with clear attribution and no claim they are GTA VI's story.

## Wildlife, water and vegetation

- Sea Life V is a legacy candidate for increasing aquatic population counts, not a complete species expansion.
- Aquatic Animals Blips 2.0 is a locator/overlay, not an animal-spawn or species pack. The source page lists Script Hook V and ScriptHookVDotNet; community reports describe script/runtime issues and an animal invulnerability behaviour. It is not selected for the default profile.
- Vanilla GTA V species and flora inventories have not yet been extracted from the exact installed game build. Therefore a claim of a complete vanilla inventory, or a complete list of additional species, would be unverified.
- No verified underwater vegetation package has been approved. Water shaders, water-level changes, aquatic population metadata and underwater plant assets are separate technical scopes and must not be conflated.

## Implemented on the development branch

- Corrected the catalogue metadata so external tools such as Script Hook V, ScriptHookVDotNet and OpenIV are recorded as external requirements rather than package dependency IDs.
- Strengthened catalogue validation for missing/self-referencing package dependencies and profile references to unknown package IDs.
- Added a WPF archive-import dialog connected to a quarantine-only ZIP/OIV import service.
- The importer hashes the archive and every extracted file, enforces configurable entry/file/total-size and compression-ratio limits, rejects unsafe paths, Windows reserved names, duplicates/case collisions, path collisions, symbolic links, reparse points and special file types, and writes a provenance manifest with status `Researching`.
- Added read-only OpenIV OIV manifest inspection for format versions 2.0–2.2: safe XML parsing, GUID/target metadata checks, nested archive-operation inventory, source/destination path validation, and warnings for unrecognised commands or unresolved payload references.
- The import UI now displays OIV operation counts and manual-review flags. Import does not modify GTA V files, execute installers, approve packages, or enable profiles.

The inspector is a manifest reader, not an OpenIV-compatible installer. It does not parse/edit RPF contents, run XML/text patches, move root-level ASI dependencies, or guarantee the mod works. It does not validate the imported mod's code, licence, game-build support or behaviour.

## Required integration work before release

1. Extend OIV inspection to inspect RPF payload contents and add a package-specific OpenIV deployment adapter. The current inspector reads the assembly manifest only; the HUD also has root-level ASI prerequisites/files to handle separately from the OIV package.
2. Add package approval after human verification of licence/permission, exact source, archive and per-file hashes, supported game build, file destinations, required loaders, conflicts and restore procedure.
3. Complete profile persistence, dependency/conflict resolution and adapter-aware load ordering; basic catalogue reference and exclusive-group validation exists, but no installable packages are approved.
4. Implement per-adapter staged deployment, backup, journal, recovery and rollback for OpenIV OIV/RPF data and ScriptHookVDotNet scripts. The current generic copy engine alone does not prove these formats are handled safely.
5. Build a game-build detector and reject unknown or unsupported builds.
6. Extend the local mod-import workflow with nested package manifests, adapter-specific file destinations, exact supported build ranges, loader version checks and an approval review. The current ZIP/OIV quarantine import records provenance, archive hash, package version and file inventory.
7. Test clean install, first launch, missions, character switching, map/waypoints, settings persistence, cutscenes, normal exit, rollback, interrupted apply and repeated apply/rollback.
8. Benchmark the selected profile on the RTX 4050 6 GB / 1080p target. Default to one visual overhaul, conservative textures, no experimental ray tracing, and optional density systems.
9. Run the complete acceptance suite on an actual GTA V Legacy installation, including the HUD in English, character wheel with separately sourced models, pause-map navigation, mission reloads, save reloads, root ASI placement, OIV rollback, and profile conflicts. GitHub CI cannot substitute for in-game validation.

## Release status

This repository is not production-ready yet. Windows CI run 44464157015 completed successfully on commit 02d9af878d383eddf5e28e6904d588cdc19c39b3: build, automated tests, self-contained win-x64 publish and artifact upload all passed. A CI artifact is not a signed installer or an in-game-validated release.
