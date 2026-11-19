# Integration audit — 19 November 2026

## Scope and non-negotiable limits

This project is a GTA V Legacy Story Mode mod manager and transformation profile. It is not a GTA VI engine port. It must combine existing third-party mods through declared manifests and reversible file operations; it must not recreate Rockstar proprietary code, assets, maps, missions, or the GTA VI campaign.

The user's target hardware is an RTX 4050 6 GB laptop GPU at 1080p. This is a test target, not a performance guarantee. No FPS claim is valid until measured on the actual machine with a recorded game build, driver, graphics settings, mod profile, VRAM use, and frame-time capture.

## New research candidates (not install-approved)

| Area | Candidate | Evidence | Current gate |
|---|---|---|---|
| HUD / minimap / character wheel | [GTA VI-Inspired HUD single-player](https://github.com/Geckomacabre/GTA-VI-Inspired-HUD) | Upstream README describes a single-player HUD and 3D minimap, character wheel with Jason/Lucia slots, and additional systems. Jason/Lucia models are separate downloads; Lucia may be paid. | Researching. Exact Legacy build, package contents, nested licences, loaders, hash, install/rollback, and RTX 4050 test outstanding. |
| Aquatic population | [Sea Life V](https://www.gta5-mods.com/misc/sea-life-v) | Source describes higher ocean populations of sharks, fish, killer whales, stingrays and dolphins. Original release is from 2017. | Researching. Legacy-era file; current-build compatibility, population metadata conflicts, and real spawn behaviour unverified. |
| Terrestrial vegetation | [Vanilla Vegetation Overhaul](https://www.gta5-mods.com/misc/realistic-vegetation-new-gen-tree-models-gta-5-legacy) | Listing includes a 5.0 beta and marks the project deprecated/discontinued. | Researching only. Do not install until archive, LODs, coverage, licences, build compatibility and VRAM use are audited. |
| GTA VI-style standalone HUD | [GTA VI Styled Minimap & HUD (Legacy)](https://www.gta5-mods.com/scripts/gta-vi-styled-minimap-legacy) | Separate Legacy listing describes minimap and HUD redesign. | Researching; mutually exclusive with the full HUD overhaul until overlap is tested. |

No candidate above is marked Tested or approved for one-click deployment. Source-page existence is not compatibility evidence.

## UI surface coverage

- HUD/minimap: candidate found; not validated in-game.
- Character switcher: upstream HUD README describes Jason/Lucia slots; character models are not included in the HUD package.
- Title/boot, full pause menu, full map replacement and complete settings replacement: no verified compatible Legacy stack approved yet.
- GTA VI story: exact GTA VI campaign content is not a feasible lawful GTA V mod-pack deliverable. The product can catalogue separately authored fan-made missions/story expansions only, with clear attribution and no claim they are GTA VI's story.

## Wildlife, water and vegetation

- Sea Life V is a legacy candidate for increasing aquatic population counts, not a complete species expansion.
- Aquatic Animals Blips 2.0 is a locator/overlay, not an animal-spawn or species pack. The source page lists Script Hook V and ScriptHookVDotNet; community reports describe script/runtime issues and an animal invulnerability behaviour. It is not selected for the default profile.
- Vanilla GTA V species and flora inventories have not yet been extracted from the exact installed game build. Therefore a claim of a complete vanilla inventory, or a complete list of additional species, would be unverified.
- No verified underwater vegetation package has been approved. Water shaders, water-level changes, aquatic population metadata and underwater plant assets are separate technical scopes and must not be conflated.

## Required integration work before release

1. Add an OpenIV package inspector that rejects path traversal, symlinks/reparse points, duplicate destinations, unsupported archive types, and unbounded extraction.
2. Add explicit package manifests and exact SHA-256 hashes for user-imported archives. No automatic install from a webpage URL.
3. Add profile dependency/conflict resolution, including mutually exclusive HUD, graphics, population and vegetation groups.
4. Implement per-adapter staged deployment, backup, journal, recovery and rollback for OpenIV OIV/RPF data and ScriptHookVDotNet scripts. The current generic copy engine alone does not prove these formats are handled safely.
5. Build a game-build detector and reject unknown or unsupported builds.
6. Add a local mod-import workflow with source URL, author, licence, archive hash, package version, required framework versions, file inventory, and user consent.
7. Test clean install, first launch, missions, character switching, map/waypoints, settings persistence, cutscenes, normal exit, rollback, interrupted apply and repeated apply/rollback.
8. Benchmark the selected profile on the RTX 4050 6 GB / 1080p target. Default to one visual overhaul, conservative textures, no experimental ray tracing, and optional density systems.
9. Run the complete acceptance suite on an actual GTA V Legacy installation. GitHub CI cannot substitute for in-game validation.

## Release status

This repository is not production-ready yet. The Windows workflow now publishes a self-contained win-x64 build artifact after build and tests pass. A published CI artifact is not the same as a signed installer or an in-game-validated release.
