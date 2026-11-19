# Initial mod research shortlist — GTA V Legacy

**Status: research only. None of these packages is approved for automatic installation yet.** A source page, popularity, or a successful download is not proof that a package is compatible with the user's exact game build. The application must not enable a package until its exact archive, manifest, licence, dependencies, conflicts, hashes, install steps, rollback behaviour, and clean-install test have been recorded.

## Candidate packages

| Area | Candidate | Current evidence | Decision |
|---|---|---|---|
| Visuals / weather | NaturalVision Evolved (Legacy) | The creator's single-player guide targets GTA V Legacy and lists OpenIV and Script Hook V as prerequisites. The creator announced a Legacy update in November 2026 with latest-build support and optional experimental ray-traced lighting. | **Research first.** Use the creator's official distribution only. Keep experimental RT disabled in the default 1080p profile; evaluate the performance add-on and VRAM use on a 6 GB GPU. |
| Visuals / weather | VisualV (Legacy) | A current 2026 comparison reports a separate Legacy release and an OpenIV package, but this needs confirmation against the author's own release page and exact archive. | **Alternative, not combined with NVE** until overlap and conflicts are audited. |
| NPC reactions / physics | Better Euphoria | GTA5-Mods lists version 1.3, dated 29 March 2026, and describes it as an approved continuation with an OIV installer. | **Research first.** Confirm exact supported Legacy build, author permissions, required base files, and rollback coverage. |
| Police / pursuit | BetterChaseRemade | The published page describes Story Mode-only behaviour but explicitly lists Legacy build 1.0.3788.0 and ScriptHookVDotNet 3 Nightly dependencies. | **Blocked pending a newer supported build** matching the target installation. Do not install on a newer game build by assumption. |
| Population / traffic | World Of Variety Plus 2026 | The Nexus page describes restored content and expanded world events. Its permissions explicitly prohibit re-uploading the author's assets elsewhere. | **External-source-only candidate.** No bundled binaries; inspect base-mod requirements and compatibility before approval. |
| Population / traffic | Los Angeles Traffic | The Nexus page shows an update in October 2026, but its documentation still references a tested game version 1.0.3586 and additional DLC-enabling scripts. | **Blocked pending build confirmation and dependency review.** |

## Candidate profile direction

- **Balanced 1080p:** one visual overhaul at most; conservative texture and post-processing choices; no experimental ray tracing by default; only tested gameplay plugins.
- **World realism:** population, traffic, and NPC packages only after overlap analysis. Avoid installing multiple packages that replace the same population or dispatch data.
- **Police and pursuits:** one coherent dispatch/pursuit stack, with every framework and plugin version pinned together.
- **Experimental:** isolated opt-in profile, never part of the default profile.

## Mandatory approval gate

A candidate becomes installable only after all of the following are complete:

1. The official author/source and licence are recorded; redistribution is explicitly permitted before any binary is bundled.
2. The exact archive is scanned, unpacked safely, inventoried, and assigned SHA-256 hashes.
3. The target edition and exact GTA V build are explicitly supported.
4. Required loaders/frameworks are version-pinned; conflicts and load order are documented.
5. Installation is expressed as declarative file operations; arbitrary executable installers and opaque scripts are not run by the hub.
6. A clean GTA V Legacy test installation passes apply, launch, normal exit, rollback, interrupted-transaction recovery, and repeated apply/rollback tests.
7. Performance is measured at 1920×1080 and the package receives a tested performance tier.

## Prebuilt mod stack and hardware profile (19 November 2026)

The current RTX 4050 Balanced 1080p profile is a research plan, not an active install profile. It preselects no package because none of the third-party mods has passed exact-build and real-game QA. The target is 1080p with a 6 GB laptop GPU; avoid experimental ray tracing and 8K textures in the default test plan.

The recommended alternative groups are:
- One HUD/minimap implementation: GTA VI HUD, GTA VI Styled Minimap & HUD, or GTA VI MiniMap 3D Buildings. Do not combine multiple minimap/weapon-wheel replacements.
- One weather/lighting foundation: VisualV Legacy, NaturalVision Evolved Legacy, or Realistic Weather Effects Unleashed Legacy.
- One water texture package: Better Blue Water or Upscaled Water Overhaul Legacy 8K. The 8K option is not recommended as the default for 6 GB VRAM.
- Grass V performance-friendly variant only after the chosen graphics package's grass assets are compared.
- One wildlife/DLC pack per disposable test installation initially. Crocodiles and Quadrupeds require careful gameconfig/heap/packfile review; Sea Life V changes population metadata and stays blocked until vanilla entries are compared.
- No underwater flora package is approved in the current shortlist.

The complete test order, conflicts and hardware caveats are recorded in [RTX 4050 mod-stack plan](RTX4050_MOD_STACK.md). Do not assume the recommended order is a proven load order; archive collision and in-game tests decide that.

## OpenRAGE integration boundary

OpenRAGE is an external GPL-3.0-only program retrieved from its upstream release on the user's own action. The Hub retains the upstream release archive and verifies its SHA-256 and executable hash/version. Mod archives remain user-supplied and subject to the original author’s licence. The OIV picker is an install mechanism, not a catalogue approval mechanism: a research-stage package can be previewed, but the user must still independently verify its Legacy edition, game build, prerequisites and permissions. The OIV install/uninstall journal belongs to OpenRAGE, not the Hub profile/ZIP transaction system.

## Sources checked on 18 November 2026

- [Razed Mods — NaturalVision Evolved single-player guide](https://content.razedmods.com/readme/sp)
- [Razed Mods — November 2026 NVE update](https://www.patreon.com/razedmods/posts/naturalvision-175019911)
- [Better Euphoria 1.3](https://www.gta5-mods.com/misc/realistic-euphoria-physics-mod-based-on-next-gen-euphoria-by-iammrmikeman)
- [BetterChaseRemade](https://www.gta5-mods.com/uk/scripts/betterchaseremade)
- [World Of Variety Plus 2026](https://www.nexusmods.com/gta5/mods/1171)
- [Los Angeles Traffic](https://www.nexusmods.com/gta5/mods/1505)

Secondary comparisons were used only to identify candidates, not to approve packages. The hub's own validation process remains authoritative.


## GTA VI-inspired UI, HUD, map and pause-menu candidates (18 November 2026)

These are catalogue research entries, not approved one-click installs. The Windows Hub now has an optional external OpenRAGE v0.4.0 OIV preview/install/uninstall/undo backend for user-supplied .oiv files. It checks the upstream OpenRAGE release digest, previews the operation list, blocks upstream-reported unsupported operations and records changes in OpenRAGE's own journal. This does not approve a mod, validate exact-build compatibility or install every non-OIV ZIP/script/ASI package. See [OpenRAGE OIV integration](OPENRAGE_OIV_INTEGRATION.md) and [RTX 4050 mod-stack plan](RTX4050_MOD_STACK.md).

| Surface | Candidate | Current source evidence | Integration decision |
|---|---|---|---|
| HUD / weapon wheel | [GTA VI HUD - Legacy / Enhanced](https://www.gta5-mods.com/uk/scripts/gta-vi-hud-mod-893860ef-7b03-4c23-abd9-92b224291705) | Author listing shows version 1.3 FIXED and separate Legacy/Enhanced folders. It advertises status bars, weapon wheel, vehicle card, world markers, cash rings, weapons on back, stamina, vehicle storage, bounty and fuel. Requirements listed: Script Hook V, ScriptHookVDotNet 3 and LemonUI SHVDN3. | Research as the strongest broad HUD candidate. Use only the Legacy archive. Verify exact game build, all scripts, dependencies, licence, save impact, cutscenes and load-order conflicts. |
| Minimap / HUD | [GTA VI Styled Minimap & HUD (Legacy)](https://www.gta5-mods.com/scripts/gta-vi-styled-minimap-legacy) | Author listing shows version 2.0. Describes a GTA VI-inspired minimap, HUD changes and context-sensitive health/special-ability bars. Installation uses an OIV package plus separately copied script files. | Research as an alternative full HUD stack. The OpenRAGE OIV path can preview/install an author-supplied OIV component, but separate script files, dependencies and exact-build compatibility still need review; no binaries may be bundled without permission. |
| Minimap | [GTA VI MiniMap (3D Buildings)](https://www.gta5-mods.com/scripts/gtavi-minimap-3d-buildings-legacy-enhanced) | Version 1.0 is labelled beta. The author describes custom waypoints, coloured sea/sand and 3D buildings; package includes MiniMap.asi plus scripts-folder DLL/font assets. | Optional minimap candidate only. Verify exact dependencies, fullscreen issue and Legacy build before approval. Must not be combined with another minimap replacement without a conflict test. |
| Pause-menu extension API | [Pause Menu Library](https://www.gta5-mods.com/fi/tools/pause-menu-library) | Version 1.0.1 provides a mod-author API for native-style pause-menu pages, tabs, toggles, sliders, option lists, buttons and key-binding pages. Author states Legacy and Enhanced support and Script Hook V requirement. The library does not change the pause menu by itself. | Potential foundation for a custom in-game GTA VI-inspired settings page. Restrictive personal/non-commercial licence; link to author, do not redistribute or modify its ASI. |
| Minimap editor | [MinimapRadarEditor VI_DayNightOverlay](https://www.nexusmods.com/gta5/mods/2213) | Page lists Legacy v1.73 as tested, live radar/map repositioning/resizing/zoom and day/night tint. Requires Script Hook V, ScriptHookVDotNet, LemonUI SHVDN3 and compatible HUD/minimap mods. Permissions prohibit re-upload and modification. | Optional companion only. Do not bundle. Verify compatibility with the selected base minimap and actual executable build. |
| General HUD | [Jaxon UI Overhaul](https://www.gta5-mods.com/gl/scripts/ui-overhaul-v1) | Version 1.0 describes a circular minimap, health/armour rings, speedometer and weapon/ammo display; author reports 1920×1080 16:9 Legacy testing. Install uses an OIV package and Script Hook V/ScriptHookVDotNet 3 dependencies. | Alternative, not stacked with other HUD/minimap packages. The OIV component can be previewed with OpenRAGE, but its ASI/scripts and current Legacy compatibility still need separate verification. |
| Loading screen | [GTA 6 Style Loading Screen, Music & Icon](https://www.gta5-mods.com/misc/gta-6-style-loading-screen-music-icon) | Version 1.0 includes GTA VI-inspired loading artwork, intro music and desktop icon; author says install via OpenIV and prohibits redistribution without permission. | Research only. Verify music rights and OIV asset paths before any managed installation. |

### UI/UX gaps that remain

- These sources primarily cover HUD, minimap, loading artwork and mod-specific settings pages. They do not establish a complete replacement for the GTA V title/home screen, full pause navigation, full map interface or every native settings screen.
- A native-style pause-menu API is not a finished GTA VI settings redesign. It is only an extension point for compatible mods.
- OpenIV OIV archives may contain nested RPF operations, XML/text edits and deletion commands. The Hub delegates supported OIV operations to the external OpenRAGE v0.4.0 tool after a dry-run preview; if OpenRAGE reports a blocker, the Hub refuses installation. Unsupported operations remain blocked. OIV changes use the OpenRAGE journal, which is separate from the Hub's ZIP transaction journal.
- No candidate is considered compatible until its exact files, build range, dependencies, licence, conflicts, rollback and performance are verified on a disposable GTA V Legacy installation.
