# RTX 4050 mod-stack plan — GTA V Legacy

**Profile status: research / user-test staging. No package in this plan is marked Tested or preselected for automatic deployment.**

## Hardware target

This plan targets 1920×1080 and an RTX 4050 Laptop GPU with 6 GB VRAM. Laptop RTX 4050 power limits, CPU, cooling and memory configuration vary, so this is a conservative texture/asset plan—not an FPS promise.

The current profile intentionally excludes 8K texture replacements and experimental ray tracing by default. Do not combine multiple full weather/lighting overhauls, minimap replacements, water texture replacements, grass model replacements, or population-table replacements until their actual archive targets are diffed.

## Recommended stack shape

Use one candidate per exclusive slot. Add a package only after verifying the exact archive, Legacy folder, author licence, prerequisites and supported GTA V build.

| Slot | Preferred test candidate | Alternatives | Rule |
|---|---|---|---|
| GTA VI-inspired HUD | [GTA VI HUD — Legacy 1.3 FIXED](https://www.gta5-mods.com/uk/scripts/gta-vi-hud-mod-893860ef-7b03-4c23-abd9-92b224291705) | [GTA VI Styled Minimap & HUD](https://www.gta5-mods.com/scripts/gta-vi-styled-minimap-legacy), [GTA VI MiniMap 3D Buildings](https://www.gta5-mods.com/scripts/gtavi-minimap-3d-buildings-legacy-enhanced) | Choose one complete HUD/minimap stack. Do not combine multiple radar/weapon-wheel replacements. GTA VI HUD requires Script Hook V, ScriptHookVDotNet 3 and LemonUI SHVDN3 per its author listing. |
| Weather / lighting | [VisualV Legacy 1.0.630](https://www.gta5-mods.com/scripts/visualv) | [NaturalVision Evolved](https://www.gta5-mods.com/misc/naturalvision-evolved), [Realistic Weather Effects Unleashed](https://www.gta5-mods.com/misc/realistic-effects-unleashed-beta-oiv) | Pick one foundation. Compare OIV/RPF targets first. Do not layer full weather/timecycle replacements by default. |
| Water appearance | [Better Blue Water](https://www.gta5-mods.com/misc/better-blue-water) | [Upscaled Water Overhaul Legacy 8K](https://www.nexusmods.com/gta5/mods/1085) | Choose one. Better Blue Water is a small colour/texture change, not a water-simulation upgrade. The 8K package is a VRAM risk on 6 GB and is not the default recommendation. |
| Grass / vegetation | [Grass V performance-friendly Legacy variant](https://www.gta5-mods.com/misc/grass-v-improved-models-textures-density-beta-oiv) | None approved | Use only the performance-friendly variant. It improves existing grass; it does not add new plant species. Author advises installing after other grass model replacements, but archive-path conflicts still need review. |
| New wildlife | [Wild Crocodiles add-on](https://www.gta5-mods.com/scripts/wild-crocodiles-add-on-animal-net) | [Quadrupeds Animals add-on](https://www.gta5-mods.com/misc/add-on-quadrupeds-animals) | Test one DLC/script pack at a time on a cloned Legacy install. Check gameconfig, heap, packfile limits, script dependencies, spawn performance and existing species preservation. Do not start with both. |
| Aquatic population | [Sea Life V](https://www.gta5-mods.com/misc/sea-life-v) | None approved | Old population-file modification. Do not install until the exact Popgroups.ymt is compared with the selected game's vanilla file and every vanilla row is preserved. |
| Jason / Lucia visual preview | [MP Jason & Lucia V Freemode Heads](https://www.gta5-mods.com/player/mp-jason-lucia-v) | None approved | Cosmetic freemode models only. This does not add GTA VI protagonists to Story Mode or reproduce GTA VI missions. |

## Suggested test order

1. Make a clean copy of GTA V Legacy and back up Story Mode saves.
2. Record the exact Legacy build and GTA5.exe SHA-256 in the Hub.
3. Install/verify OpenRAGE from the Hub and verify that OpenIV's mods folder is available.
4. Test one HUD package by itself. Confirm its Script Hook V, ScriptHookVDotNet and LemonUI versions against the exact game build.
5. Test one graphics/weather package by itself. Capture the same scenes at 1080p before/after.
6. Test one water package by itself. Inspect ocean, river, lake, coast, shore foam and underwater visibility.
7. Test Grass V only after comparing model/texture archive paths against the chosen graphics package.
8. Test one wildlife add-on on a separate clean clone. Check original animal species and spawn-table entries, DLC limits, missing models, crashes and streaming.
9. Test combinations only after each individual package passes. Record package hashes, load order, frame-time, RAM/VRAM, normal exit, rollback and a long gameplay soak.

## What the Hub can and cannot install today

The Hub now offers an optional OpenRAGE v0.4.0 integration for prebuilt OpenIV .oiv packages. It downloads the official OpenRAGE Windows release on demand, verifies its pinned upstream archive SHA-256 and executable, previews OIV changes, shows blockers/warnings, requires confirmation, and uses OpenRAGE's own journal for OIV uninstall/undo. Its journal is separate from the Hub's ZIP package deployment journal.

This is not a universal installer for every mod archive:
- A mod supplied only as a ZIP/RAR, standalone .asi/.dll, loose script folder or manual OpenIV instructions is not automatically converted to OIV.
- An OIV operation that OpenRAGE reports as blocked is not forced through. The Hub does not add unsupported PSO editing or bypass OpenRAGE safety checks.
- Loose files that OpenRAGE would write directly to the GTA V game folder require a separate explicit acknowledgement. This can include executable ASI/DLL plugins.
- The OIV preview is an operation plan, not proof of exact-build gameplay compatibility, file conflict freedom, licence permission or rollback on every third-party package.
- OpenRAGE OIV installs are tracked by OpenRAGE, not by the Hub's ZIP transaction journal. Use OpenRAGE's installed-package list/uninstall/undo controls for those packages.

The research catalogue remains separate from the installer's OIV picker. The installer accepts user-selected OIV files, but a catalogue entry marked Researching is not certified merely because OpenRAGE can parse it. The current profile stays empty until exact-build and game-side testing is complete.

## Preservation rules

- Never install a replacement that removes/replaces an original GTA V species or its model identity. The Blue Whales/Humpback Replacer entry is explicitly Blocked.
- Treat any Popgroups.ymt, popcycle, pedmodelsets, animal/plant model or water simulation edit as an ecology-sensitive change.
- New animals and plants must be unique append-only additions with their own models, behaviours, habitats and spawn rows.
- A water texture change is not a water-system improvement. Preserve the original water-body simulation and interaction profiles.
- No underwater plant species package has yet been approved in this catalogue. Underwater flora is still a gap.

## Release status

This is a curated staging plan, not a validated final mod stack. At the time of writing, all candidate mods are Researching, no third-party mod is Tested, no package is preselected, and no real-game combination test has been performed. The user's in-game testing and captured evidence are required before promotion.
