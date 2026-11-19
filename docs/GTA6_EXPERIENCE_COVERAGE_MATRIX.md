# GTA VI-inspired experience coverage matrix

**Purpose:** Set the ambition and acceptance criteria for the GTA V Transformation Hub. The target is the broadest stable, lawful, single-player transformation that the GTA V engine and available mods can support. This is not an engine conversion and cannot reproduce every GTA VI feature or proprietary asset.

## Coverage principles

- Build toward observable player-facing outcomes, not marketing claims or rumours about undocumented internals.
- Prefer complete, compatible systems over a large pile of conflicting mods.
- Every feature is classified as native GTA V configuration, verified mod candidate, custom hub capability, experimental, unavailable, or blocked.
- Never mark a feature "supported" until the exact game build, package version, dependencies, performance, save impact and rollback have been tested.
- Story Mode is the primary product experience. No GTA Online deployment or runtime modification.
- Accessibility is a first-class acceptance track, not a cosmetic extra.

## Experience matrix

| Domain | Target experience | Hub implementation approach | Acceptance / limitation |
|---|---|---|---|
| Visual fidelity | Modern lighting, weather, time-of-day, reflections, materials, vegetation, draw distance and environmental detail | Curated graphics profile; one compatible overhaul at a time; texture/VRAM tiers | Measure frame-time and VRAM at 1080p; no promise of GTA VI rendering or exact assets |
| World and map | Dense, believable urban/rural spaces, interiors, roads, landmarks and varied districts | Map/interior/prop packages, map conflict detection and optional region profiles | No unauthorized GTA VI map/assets; verify map boundaries, collisions, navmesh and mission compatibility |
| Ambient life | More varied pedestrian types, routines, reactions, conversations and contextual scenarios | Population and scenario mods; density presets; scripted systems only where actually supported | Test population load, CPU cost, spawn behaviour and mission interference |
| Traffic | Varied vehicle classes, believable density, lane behaviour, incidents and emergency traffic | Vehicle packs, traffic behaviour mods and density controls | Check vehicle metadata conflicts, frame-time, spawn balance and emergency dispatch |
| Police and law | More reactive witness, dispatch, search, pursuit, escalation and wanted behaviour | One compatible police/dispatch stack, with dependencies pinned | Test pursuit logic, wanted levels, mission scripts and save stability; no claim of Rockstar internal AI |
| Vehicles | Broad vehicle variety, interiors, sounds, handling, suspension, braking, traction and damage | Curated add-on/replacement vehicles and handling packages | Avoid duplicate model IDs; test collisions, handling, audio and memory usage |
| Player movement | Responsive locomotion, contextual animations, transitions, swimming, climbing and interaction | Animation and movement packages with strict compatibility gates | Test all protagonists, weapons, vehicles, cutscenes and mission states |
| Combat | Weapon feel, recoil, hit reactions, cover, enemy tactics, damage and encounter variety | Weapon/animation/AI packages, selectable combat profiles | No changes while game is running; test mission scripting and difficulty balance |
| Story campaign | Preserve GTA V's original campaign while adding high-quality story arcs, missions and character content | Separate story packs, per-pack profiles, load-order rules and save backups | Do not overwrite original story; label fan-made content clearly; test start-to-finish and save compatibility |
| Side activities | Jobs, races, challenges, businesses, encounters, hobbies and repeatable events | Activity/mission packs with clear prerequisites and map/menu discoverability | Distinguish new playable content from cosmetic changes; verify mission completion and persistence |
| Economy and services | More useful shops, garages, properties, services and progression loops | Scripted economy/activity mods where available | Do not promise systems unsupported by GTA V scripting; preserve existing saves |
| Interiors and interaction | Enterable buildings, usable objects, contextual prompts and more meaningful locations | Interior and interaction packages, conflict-aware map deployment | Test collision, navigation, doors, mission triggers and streaming stability |
| Wildlife and environment | More varied animals, ambient ecology, weather-linked atmosphere and environmental events | Wildlife/ambient packages and population presets | Verify spawn performance and animal behaviour; avoid stacking overlapping population mods |
| Audio | Ambient soundscapes, vehicle audio, weapon audio, weather and location-specific ambience | Audio packs with explicit replacement manifests | Check volume balance, missing banks, licensing and language coverage |
| UI and navigation | Clear HUD, map readability, useful menus, scalable interface and reduced clutter | Profile-level HUD/UI packages and hub-side catalogue UX | Keep a known-good reset path; don't make the game unlaunchable through UI conflicts |
| Accessibility | Readable subtitles, speaker identification where available, caption options, UI scale, color/contrast options, control remapping, camera comfort, reduced motion, audio balance and difficulty/aim assistance where supported | First prefer native GTA V settings; then verified accessibility mods; add hub-level explanations and accessible controls | Test each feature independently; document unsupported options; never claim a feature exists merely because a toggle is displayed |
| Accessibility of the Hub | Keyboard navigation, screen-reader labels, high contrast, scalable text, reduced animation, clear focus states, error summaries and no color-only status | WPF accessibility properties, UI automation peers, keyboard-first design and accessibility tests | Verify with Windows Narrator, keyboard-only operation, 100–200% text scaling and high-contrast mode |
| Stability and recovery | Reliable launch, clean exit, crash recovery and reversible mod states | Transaction journal, backups, preflight, process lock and recovery wizard | Test interrupted apply/rollback, forced termination, disk-full and permission errors |
| Performance | Stable frame pacing and predictable memory use | Hardware detection, mutually exclusive profiles and benchmark metadata | Record actual test hardware and settings; no invented FPS guarantee |
| Privacy and security | Local-first configuration, trustworthy packages and transparent updates | Signed catalogue metadata, HTTPS sources, SHA-256, safe archive extraction, least privilege | Never execute arbitrary package installers by default; never silently elevate |

## Priority map conversion track — Leonida / Vice City

**Priority: P0 research and integration blocker. Status: not implemented.**

The target is the closest playable GTA V Legacy world replacement that can be sourced and lawfully used. The desired geography is the complete Leonida concept, not merely a Vice City-themed HUD or a small Miami-style map add-on.

Target regions to track independently:
- Vice City urban core and surrounding beaches/districts.
- Leonida Keys and the island/causeway chain.
- Grassrivers wetlands and waterways.
- Port Gellhorn and its surrounding rural/coastal area.
- Ambrosia industrial/central region.
- Mount Kalaga National Park and northern terrain.
- The road, bridge, rail, airport, port and water connections between these regions.

### Evidence and candidate status (checked 19 November 2026)

- **Sunshine Dream / Leonida Keys-to-Vice City map work:** a creator video and Nexus article show a GTA VI-inspired map expansion for GTA V Enhanced Story Mode, with a Leonida Keys connection to a Vice City map and later 3D-model/texture improvements. This is a lead, not a verified GTA V Legacy package. Do not enable on Legacy until the author publishes explicit Legacy support and the exact archive, dependencies, licence, map boundaries, collisions, navmesh, traffic paths and rollback have been tested. [Nexus author article](https://www.nexusmods.com/gta5enhanced/articles/74) · [creator demonstration](https://www.youtube.com/watch?v=4IJHA4lEIM4)
- **Project Vice (Dark Space / The Ambitioneers):** previously demonstrated a playable fan-made Leonida map in GTA V, but its distribution was withdrawn following a copyright claim. Do not fetch unofficial mirrors, re-uploads or leaked Rockstar assets. It is not an installable package. [Project showcase](https://www.youtube.com/watch?v=FulpnBLnivc) · [reported takedown](https://gtahouse.net/en/games/gta-6/news/gta-6-map-mod-gta-5-taken-down-take-two/)
- **Vice Cry / Vice City Overhaul:** historical Vice City map conversions, not the GTA VI Leonida map. Current availability is reported as withdrawn/no download on the original listings. They must not be represented as a Leonida replacement or sourced from untrusted mirrors.
- **Leonida Atlas / community mapping datasets:** useful as an external research reference for region names and approximate landmark geography, not a GTA V world asset or a plug-in map conversion. Its own project labels the map positions as community approximations. [Open-source atlas](https://github.com/navanem/gta6-leonida-atlas)

### Integration rules

1. Do not install an Enhanced-only map package into the current Legacy target. Maintain separate edition compatibility declarations.
2. Do not treat a 2D map image, browser atlas, radar texture, minimap, or menu-map replacement as a playable 3D world conversion.
3. A playable map package must declare every archive/RPF operation, world-space bounds, IPL/YMAP/YTYP/YDR/YBN/YND and related dependencies where applicable, LODs, collision, navmesh/path nodes, water, vegetation, traffic paths, portals/interior links, streaming budgets, and uninstall/restore operations.
4. Detect and report overlaps with the base Los Santos world, map DLC, existing map mods, interiors, roads, bridges, traffic nodes and mission locations before activation.
5. Provide independent toggles for map package components only where they can be safely separated. Components with shared dependencies must be grouped into one atomic profile and switched together.
6. Require a clean disposable GTA V Legacy installation for tests: boot, map boundaries, road traversal, vehicle and pedestrian navigation, water/boats, aircraft, interiors, mission starts, cutscenes, save/reload, repeated apply/rollback and performance.
7. If a complete compatible Leonida map cannot be sourced, keep the native GTA V world and offer only clearly labelled, independently installable regional/visual enhancements. Never imply that this equals the GTA VI map.

### Acceptance gate

The map track is not complete until the user can enter the replacement world in Story Mode, traverse its connected regions without falling through geometry or hitting invisible boundaries, see working traffic and pedestrian navigation, use its interiors and water systems, complete representative GTA V missions, save/reload, and restore the original world. No such end-to-end Legacy acceptance test has been run.

## Story Mode product requirements

Story Mode must be treated as the primary use case:

1. Preserve a clean baseline of the original GTA V Story Mode and all existing saves.
2. Provide separate campaign, story-expansion and side-activity profiles.
3. Classify each story package as a new campaign, mission pack, side mission, character expansion, activity pack, or ambient event.
4. Display protagonist requirements, mission start locations, prerequisites, estimated playtime only when author-supplied or measured, voice/language coverage, completion state and known save risks.
5. Detect conflicts between mission packs, script frameworks, map edits and shared files.
6. Back up relevant saves before enabling or removing story packages; never silently edit or delete user saves.
7. Test mission start, checkpoints, failure/retry, completion, save/reload and uninstall behaviour.
8. Keep the original GTA V campaign playable. A story pack must not be represented as an official Rockstar expansion.
9. Prioritise coherent, high-quality content over quantity. If compatible content is unavailable, state that gap instead of inventing a package.

## Accessibility requirements

Accessibility has two separate scopes: the game experience and the Transformation Hub application.

Game-side priorities:
- Subtitle size, contrast, background and readability, if the game or a verified mod exposes them.
- Speaker labels and sound captions only where technically supported.
- Color-vision-friendly HUD/map alternatives and contrast presets.
- UI/HUD scaling and reduced clutter.
- Remappable keyboard/controller inputs and clear control prompts.
- Camera shake, motion blur, field-of-view and motion comfort controls where available.
- Audio level controls and dialogue/music/effects balance where available.
- Aim assistance, difficulty and combat accessibility only where supported by the game or a verified mod.
- Clear per-feature notes for unsupported or untested options.

Hub-side priorities:
- Full keyboard navigation and visible focus.
- Narrator-compatible names, roles, descriptions and status announcements.
- High-contrast and scalable text.
- Reduced motion and no information conveyed by color alone.
- Plain-language validation errors and recovery instructions.
- Accessible installer, update and rollback flows.

## Delivery gates

**Gate A — Catalogue truth:** every entry has source, author, licence, exact version, exact supported game build, hash, dependencies, conflicts, install/restore plan and status.

**Gate B — Story safety:** story packages pass clean-save and disposable-save testing, and cannot overwrite original campaign data without an explicit backup and recovery plan.

**Gate C — Accessibility:** the hub passes keyboard-only, Narrator, high-contrast and text-scaling checks. Game-side accessibility claims are individually tested.

**Gate D — Production:** clean-machine install, upgrade, rollback, interrupted transaction, game-running lock, uninstall, diagnostics and security tests pass before release.

## Explicit limitations

GTA V cannot be turned into GTA VI by installing mods. The hub cannot manufacture Rockstar's proprietary engine, assets, motion capture, writing, voice performances, mission scripting or undocumented systems. Some domains will remain partial or unavailable. The product must expose a transparent coverage status rather than claim "every GTA VI feature" is implemented.


## Initial concrete Story Mode and accessibility candidates (18 November 2026)

These entries are research leads, not approved one-click installs:

- **GTA V New Game+ — Story Mode Expansion:** a Legacy/PC Story Mode package with five modules covering stamina, banking, store inventory, pickups and vehicle ownership. Its source notes unfinished fishing and possible conflicts with other inventory, weapon, garage, fuel and stamina mods. It is an expansion of the existing campaign experience, not a complete replacement campaign or conventional completed-save New Game+ import. [Creator listing](https://www.nexusmods.com/gta5/mods/2610) · [GTA5-Mods mirror/listing](https://www.gta5-mods.com/scripts/gta-v-new-game-the-ultimate-story-mode-expansion)
- **Lowriders DLC in Single Player:** ports the Lowriders DLC missions and cutscenes into Story Mode and restores map icons. The author lists Script Hook V and ScriptHookVDotNet Nightly for Legacy and recommends Legacy for the best modding experience. [Author listing](https://www.gta5-mods.com/scripts/lowriders-dlc-in-single-player)
- **Big Bank Heists:** adds six bank locations and configurable NPC crew roles. It requires Script Hook V, ScriptHookVDotNet Nightly and Enable MP Maps. The author explicitly prohibits redistribution; community reports describe interior/vault conflicts, so it stays research-only until tested. [Author listing](https://www.gta5-mods.com/scripts/big-bank-heists)
- **The Golden Train Heist:** adds a Merryweather train ambush/heist. It requires Script Hook V, ScriptHookVDotNet and Enable MP Maps; the older release needs current-build testing, and the author explicitly prohibits redistribution. [Author listing](https://www.gta5-mods.com/scripts/the-golden-train-heist)
- **Subtitles Enhanced — Legacy:** provides medium/large/very-large subtitle variants, colour options, improved wrapping and positioning. The author prohibits re-uploading the package and requires permission for modifications, so the Hub must link to the author's page rather than bundle it unless permissions change. [Author listing](https://www.nexusmods.com/gta5/mods/2239)
- **GTA Access:** an NVDA-integrated screen-reader project for GTA V Legacy with spoken menus, phone, shops, subtitles, mission instructions, object scanning and navigation assistance. The author's page labels version 1.0.128 a tester build and specifies Legacy 1.0.3889 or later, NVDA, Windows OCR and Script Hook V. It must remain an explicitly opted-in accessibility tool; its installer must never be silently run by the Hub. [Author documentation](https://madnessinnsmouth.com/gta-access/)

These candidates demonstrate that the project can target more than graphics: playable side content, persistent gameplay systems, subtitle readability and screen-reader accessibility are all part of the catalogue strategy. Their presence does not mean they have passed the Hub's own install, save, compatibility or rollback tests.

## In-game UI/UX transformation (expanded)

The UI/UX track includes the title/Story Mode entry, pause/home navigation, map, settings, HUD, phone, weapon/radial menus, loading/transition screens and accessibility overlays. It is distinct from the desktop Transformation Hub dashboard. Each surface requires a verified GTA V implementation path, preservation of native actions, keyboard/controller support, accessibility evidence, build compatibility, performance measurement and rollback. See [UI/UX transformation specification](UI_UX_TRANSFORMATION_SPEC.md).
