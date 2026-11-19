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

### Candidate evidence (all still Researching unless explicitly Blocked)

| Domain | Candidate | What the source actually documents | Current gate |
|---|---|---|---|
| Aquatic population | [Sea Life V](https://www.gta5-mods.com/misc/sea-life-v) | A 2017 `Popgroups.ymt` replacement intended to raise ocean populations of sharks, fish, killer whales, stingrays and dolphins. | Researching. Current Legacy compatibility, exact species/model references, population balance and file conflicts are unknown. It changes spawn populations; it is not itself a new species model pack. |
| Add-on quadruped models | [Quadrupeds Animals Add-On 2.2.1](https://www.gta5-mods.com/misc/add-on-quadrupeds-animals) | The author lists bear, two horses, pony, donkey, bobcat, leopard, two lions, wolf and skunk. The author recommends HeapAdjuster and a compatible gameconfig. | Researching. 2021 package; exact current Legacy support, all model permissions, ped limits, resource impact and rollback need verification. The author explicitly says this pack alone does not naturally spawn or make animals rideable; those behaviours were in the separate Driftwood Mod. Treat this as manual-spawn model content only. |
| Marine mammal replacement | [Blue Whales Replacer — Legacy Beta 1.1](https://www.gta5-mods.com/misc/miitinsmods-blue-whales-replacer-miitins) | Creator's 9 August 2026 Legacy beta replaces the existing humpback whale mesh/appearance and 4K texture. Legacy uses raw DDS files to replace textures in `a_c_humpback.ytd`. | Researching. A replacement of an existing species, not a new whale spawn species. Creator initially noted they could not test Legacy; later comments discuss correcting the raw DDS/YTD workflow. No re-upload or redistribution permitted. Validate mesh/YTD compatibility, underwater behaviour, rollback and 6GB VRAM before use. |
| Animal locator | [Animal Blips 3.1](https://www.nexusmods.com/gta5/mods/1439) | Updated 6 October 2026. Adds map blips for animals and optional animal health/armour/invincibility configuration; requires Script Hook V and the Legacy Script Hook V .NET Nightly runtime. | Researching. This is a locator/configuration utility, not a species spawner. The author disallows re-upload and requires permission for modifications/assets, so the Hub must link to the author source only. |
| Scripted animal spawns | [WildLife Mod 2.2](https://www.gta5-mods.com/scripts/wildlife-mod) | Describes animal skins and spawning lions, sharks, hawks and monkeys, animal companions/riding, plus broad zombie, vampire, robot and crowd/chaos modes. | **Blocked for this Legacy build target.** The listing's own description says it is for GTA V Enhanced, although the page is filed under Legacy. It is not a tested ambient ecosystem or a complete fauna pack. Reconsider only if the author confirms Legacy support and the exact script requirements are validated. |
| Water visuals | [Upscaled Water Overhaul (8K) 3.01](https://www.nexusmods.com/gta5/mods/1085) | Legacy listing describes ripple/wave/foam/reflection changes and upscaled top/bottom water and underwater-fog textures; several textures reach 4K/8K. | Researching. Not a physics simulation or new underwater flora. 8K texture footprint is a risk for the 6GB VRAM target. Author disallows re-upload, modifications and asset use without permission; never bundle it. The author's FPS claim is not a benchmark result for our hardware. |
| Water configuration | [Indian Ocean (Clear Ocean Water) 1.3](https://www.gta5-mods.com/misc/indian-ocean-water-mod) | A 2017 configuration option for ocean clarity and wave size. | Researching. Exact edited files, current Legacy compatibility and interaction with other water configuration mods must be inspected. It adds no animals, plants or water simulation. |
| Underwater/vegetation textures | [HD Vegetation Upscale](https://www.nexusmods.com/gta5/mods/1095) | Describes existing vegetation textures upscaled to 2560×1440, including underwater vegetation. | Researching. Texture upscaling only; no new plant species or underwater plant meshes are documented. Asset provenance/permissions and VRAM/streaming impact are unclear. |
| Grass models/density | [Grass V 1.5 beta](https://www.gta5-mods.com/misc/grass-v-improved-models-textures-density-beta-oiv-foxy_oxy) | Replaces/recolours grass models and increases density, scaling and draw distance; the author lists a performance-friendly option. | Researching. Still beta; the author warns that install order relative to other grass/vegetation replacements matters. It adds no new botanical species. Verify the lighter option and benchmark. |
| Broad tree/hedge overhaul | [Vanilla Vegetation Overhaul 5.0 beta](https://www.gta5-mods.com/misc/realistic-vegetation-new-gen-tree-models-gta-5-legacy) | Adds/replaces selected tree and hedge models/textures. The author marked the project discontinued in January 2026. | Researching only; not a long-term default dependency. Its beta/discontinued status and exact archive must be reviewed before use. |

### What is and is not solved

- The catalogue now distinguishes **species/population**, **animal locator**, **scripted animal spawn**, **water texture/configuration**, and **vegetation texture/model** packages. These are separate systems and should not be represented as one complete ecosystem mod.
- The strongest verified aquatic population lead is Sea Life V, but it is old and only changes population metadata. It still needs model references and spawn validation against the selected Legacy build.
- Animal Blips 3.1 helps locate animals already in the game; it does not add species.
- Quadrupeds Animals Add-On supplies manually spawnable add-on models, but the author explicitly says the add-on alone does not create natural spawning or rideability. It is not a complete ecosystem solution.
- Blue Whales Legacy Beta 1.1 replaces the existing humpback whale rather than adding an independent spawn species. It therefore changes model appearance/size but does not increase species count.
- WildLife Mod documents some extra animal skins and scripted animal spawning, but the same page markets the package as Enhanced. It is blocked for this Legacy-only deployment until the author clarifies support.
- Upscaled Water Overhaul changes water textures, waves/foam/reflections and underwater fog appearance. It does not establish new fluid physics or an underwater plant ecosystem.
- HD Vegetation Upscale explicitly includes underwater vegetation textures, but does not document new underwater plant models/species. At present there is **no verified underwater plant-species pack** in the research catalogue.
- Vanilla GTA V species and flora inventories have not yet been extracted from the exact installed game build. An exhaustive baseline/additions list cannot be claimed until the relevant population metadata, model archives and texture/model dictionaries are inspected.
- For the RTX 4050 6GB / 1080p target, avoid stacking multiple population, grass-density, water-texture and vegetation overhauls. Start with one package per resource domain, use lighter texture variants where available, and measure frame-time, VRAM, streaming hitches and spawn rates before approval.

## Implemented on the development branch

- Corrected the catalogue metadata so external tools such as Script Hook V, ScriptHookVDotNet and OpenIV are recorded as external requirements rather than package dependency IDs.
- Strengthened catalogue validation for missing/self-referencing package dependencies and profile references to unknown package IDs.
- Added a WPF archive-import dialog connected to a quarantine-only ZIP/OIV import service.
- The importer hashes the archive and every extracted file, enforces configurable entry/file/total-size and compression-ratio limits, rejects unsafe paths, Windows reserved names, duplicates/case collisions, path collisions, symbolic links, reparse points and special file types, and writes a provenance manifest with status `Researching`.
- Added read-only OpenIV OIV manifest inspection for format versions 2.0–2.2: safe XML parsing, GUID/target metadata checks, nested archive-operation inventory, source/destination path validation, and warnings for unrecognised commands or unresolved payload references.
- The import UI now displays OIV operation counts and manual-review flags. Import does not modify GTA V files, execute installers, approve packages, or enable profiles.
- Added a read-only CodeWalker install-history view. It reads the external installer's `OIV_CW_Uninstall_Data/*/install.log` records, applies directory/file size and reparse-point checks, and displays package name/version/edition/date/file-operation count. It does not run CodeWalker's `--list` command, modify the game, or assert that any package remains active or functional.

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

## RTX 4050 performance benchmark workflow

- Added a PresentMon CSV analyzer and dashboard import. It filters to `GTA5.exe`, parses common PresentMon v1/v2 frame-time columns, counts dropped frames when present, and reports mean/median/P95/P99 frame time, average FPS, estimated 1% low and optional CPU/GPU Busy metrics.
- The benchmark form records GTA V file version, resolution, GPU, driver, mod profile and scenario. Reports store a SHA-256 of the source CSV and are saved locally as JSON under `%LOCALAPPDATA%\\GtaTransformationHub\\Benchmarks`.
- The 1% low metric is an estimate calculated as 1000 / P99 frame time. The analysis does not infer VRAM usage, temperatures, utilisation percentage, clocks, power or thermal throttling. Hardware-monitor logs are still needed for those measurements.
- `docs/PERFORMANCE_BENCHMARKING.md` describes a repeatable 90-second capture workflow and three-run baseline/mod comparison. PresentMon is a user-obtained external tool; its binary is not bundled.
- **No RTX 4050 in-game benchmark has been run or measured by CI.** The target laptop's actual game build, graphics settings, driver, power/thermal mode, frame-time and VRAM data must be captured before performance claims or a recommended graphics profile can be approved.

## External OIV review handoff

The Hub now supports a controlled manual handoff to the external CodeWalker OIV Package Installer. Before opening the GUI it re-hashes and re-parses the retained OIV, compares the result with the manifest previously shown to the user, verifies the selected installer SHA-256, confirms that the OIV targets GTA V and is not explicitly Enhanced, and blocks unsupported commands or unresolved payload references. It uses CodeWalker's `--set-game` and `--get-game` CLI operations to set the active folder to the detected GTA V Legacy installation and verify the returned path. The GUI is then opened with the retained OIV preloaded for review; the user must choose whether to click Install. The Hub does not execute OIV operations, edit RPF files, confirm install success, or mark the package as approved. CodeWalker is not bundled and its publisher signature is not verified by the Hub.

## Release status

This repository is not production-ready yet. Windows CI run [44469816496](https://github.com/battlebuket-oss/gta5-to-gta6/actions/runs/44469816496) completed successfully on commit `3388d3d22b246cfa5bf15141b46e0b795bb81411`: restore, application build, automated tests, self-contained Windows x64 publish and artifact upload all passed. The [66.3 MB Windows x64 CI artifact](https://github.com/battlebuket-oss/gta5-to-gta6/actions/runs/44469816496/artifacts/14504161451) expires on 3 December 2026. It is a build artifact, not a signed installer or an in-game-validated release. Actual GTA V Legacy install/rollback, story/UI/gameplay testing and the target RTX 4050 gameplay benchmark remain outstanding.
