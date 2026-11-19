# GTA VI-inspired in-game UI/UX transformation specification

**Status:** Engineering specification — not an implemented feature claim  
**Target:** GTA V Legacy on Windows, Story Mode only  
**Design goal:** Rework the player's actual GTA V interaction experience toward the visual hierarchy, clarity and interaction quality associated with GTA VI, while preserving game functionality and offering a safe fallback.

## Product decision

UI/UX is now a first-class transformation track alongside world, gameplay, Story Mode and accessibility. The scope covers the in-game front end and HUD, not merely the Transformation Hub desktop dashboard.

A faithful visual recreation is a target, not a promise of exact GTA VI parity. GTA V and GTA VI do not share a drop-in UI package. Every screen must be implemented through a compatible GTA V modding surface or a separately labelled companion interface. The project will not use leaked/proprietary GTA VI code or assets.

## Screen inventory

| Screen/system | Desired result | Technical route to investigate | Required behaviour |
|---|---|---|---|
| Boot/title and Story Mode entry | Modern cinematic entry, clean continue/new-game choices and profile identity | GTA V frontend/Scaleform and supported menu-mod hooks; companion launcher only for pre-game functions | Continue must resolve to the correct GTA V save; new game must retain native confirmation; no save deletion |
| Pause/home navigation | Modern panel hierarchy, readable typography, consistent iconography and responsive transitions | Scaleform/frontend replacement or script-driven menu where supported | All native pause actions remain reachable; keyboard/controller focus and back behaviour are correct |
| Map | Cleaner full-screen map treatment, readable legend, category filters, waypoint and route clarity | Native map/Scaleform extension or compatible map UI mod | Preserve coordinates, blips, waypoint setting, zoom, pan, mission markers and controller navigation |
| Settings | Reorganised categories and discoverable options | Native settings/front-end hooks where available; otherwise explain native settings rather than fake controls | Every displayed option must change a real setting and persist correctly |
| HUD | Consistent health/armour, minimap, weapon, vehicle, mission and status presentation | HUD/Scaleform overlays with per-element toggles | No overlap with subtitles, mission prompts, radar, cutscenes or accessibility indicators |
| Phone and interaction menus | Modern card/list layouts and clearer action feedback | Script/API-supported phone and interaction-menu extensions | Preserve calling, contacts, messages, services and mission interactions supported by the game |
| Weapon wheel and radial actions | Clearer grouping, selection state and input feedback | Compatible Scaleform/HUD replacement | Selection latency and all weapon/action mappings remain unchanged |
| Loading and transition screens | Cohesive visual language and legible progress | Supported frontend assets only | No false progress indicator; preserve loading and error states |
| Accessibility overlays | Subtitle readability, focus visibility, contrast, scaling and reduced-motion presentation | Native settings first, verified mods second, hub-managed profile configuration third | Each control is individually verified; unsupported features are explicitly unavailable |

## UX design system

- Establish a coherent visual system: restrained dark surfaces, high-contrast text, deliberate accent use, consistent icon family, spacing scale, typography hierarchy and clear focus/selected/disabled states.
- Design for both controller and keyboard/mouse. Every interactive screen needs deterministic focus order, visible focus, back/cancel semantics and no mouse-only actions.
- Use responsive layouts for 16:9 1080p as the baseline, with safe areas for ultrawide and common display scaling. Avoid text embedded in textures where localisation or scaling would become impossible.
- Keep animation purposeful and brief. Provide reduced-motion behaviour where the rendering path supports it.
- Keep subtitles, mission-critical prompts, minimap and accessibility output above decorative layers. Never let a cosmetic redesign obscure gameplay information.
- Maintain consistent interaction patterns across pause, map, settings, phone, HUD and radial menus.
- Do not imitate GTA VI branding or imply official Rockstar authorship. The product is a fan-made GTA V transformation.

## Architecture and integration

1. **UI capability registry:** each screen is registered with its implementation method, supported GTA V build range, dependencies, conflicts, assets, input methods, accessibility support, rollback method and test status.
2. **One active UI stack:** mutually exclusive replacements for the same Scaleform/frontend/HUD surface must be detected and resolved before deployment. Do not stack competing total-conversion UI packs.
3. **Layered packages:** separate frontend, map, HUD, phone, radial menu and accessibility packages so users can enable only validated components. A coherent curated profile may select a compatible set.
4. **Native-first settings:** read and preserve native configuration. A custom control is exposed only if it has a real adapter and a tested read/write/persistence path.
5. **Safe deployment:** UI packages use the same signed catalogue, author/licence checks, SHA-256 verification, archive inspection, staged deployment, backups, journal and rollback as all other packages.
6. **Game-session lock:** no UI asset, script, profile or configuration changes while GTA5.exe is running. The game must exit before a new UI stack can be applied.
7. **Fallback:** if an in-game screen cannot safely be replaced, keep the native screen and provide a companion Hub feature or documented configuration guide. Do not present an external overlay as an in-game replacement.

## Data model additions required

A UI package needs explicit metadata beyond a generic mod entry:

- `uiSurface`: frontend, title, pause, map, settings, hud, phone, radial, loading, accessibility
- `implementationType`: scaleform, game-script, asset-replacement, companion-only
- `supportedGameBuilds` and exact framework/loader versions
- `inputSupport`: keyboardMouse, controller
- `resolutionSupport` and safe-area behaviour
- `accessibilitySupport`: screen-reader, focus, contrast, scaling, reduced-motion, captions
- `replaces`, `conflicts`, `loadOrder`, `files`, `hashes`
- `nativeActionsPreserved` test evidence
- `rollbackPlan`, `licence`, author/source attribution and validation status

## Acceptance tests

### Functional
- Every menu action maps to a real game action; no dead buttons or decorative fake settings.
- Continue/new game, map pan/zoom/waypoint, settings persistence, phone actions, weapon selection and pause/resume work on keyboard/mouse and controller where claimed.
- Mission prompts, subtitles, cutscenes, interiors and gameplay transitions remain readable and functional.
- Save files remain unchanged by UI-only package installation/removal.

### Accessibility
- Complete keyboard-only navigation with visible focus and predictable escape/back behaviour.
- Windows/game screen-reader compatibility is documented per screen; do not claim game screen-reader support based only on Hub Narrator support.
- High contrast, scalable text and color-independent status cues are tested.
- Reduced motion disables or reduces nonessential UI transitions where supported.
- Subtitles and mission-critical prompts remain legible at the supported display resolutions.

### Reliability and performance
- Test clean install, update, interrupted apply, rollback, uninstall and recovery.
- Test each supported GTA V build and each supported UI-stack combination.
- Benchmark UI frame-time and memory overhead on the documented 1080p/6 GB VRAM baseline.
- Verify no persistent input capture, stuck focus, broken pause state or crash on load/mission transition.

## Delivery order

1. Inventory existing GTA V frontend/Scaleform/HUD extension points and available mod candidates; verify licences and current build compatibility.
2. Build a screen-by-screen candidate matrix and select one compatible implementation per surface.
3. Implement catalogue metadata and UI-stack conflict resolution.
4. Implement one low-risk surface first (HUD or pause-menu component) with a reversible package and automated validation.
5. Expand to map, settings, phone, radial, title/entry and loading screens only after each surface passes acceptance.
6. Deliver a cohesive UI profile and accessibility validation before calling the track production-ready.


## Implemented package contract (application-side)

The package model now has an optional `ui` descriptor. It declares the replaced surfaces (title, frontend, pause, map, settings, HUD, phone, radial, loading or accessibility), implementation type (Scaleform, game script or asset replacement), supported input methods, tested resolutions, accessibility capabilities, native-action preservation, mission/cutscene/subtitle/original-campaign preservation, compatibility evidence URL and rollback evidence URL.

The Hub validates these declarations and rejects UI impact packages that omit the descriptor, use unsupported surfaces or implementation types, claim companion-only overlays as in-game replacements, omit keyboard/controller support, omit evidence references, or fail to preserve native gameplay functions. Preflight rejects two selected packages that replace the same surface. UI packages additionally require `CompatibilityVerified=true`, a locally trusted publisher signature and signature version 3. Signature v3 authenticates the entire UI descriptor. Version 1 and 2 canonical payload formats remain unchanged for existing packages.

These checks validate package declarations and trust provenance; they do not prove that the linked evidence is accurate. The current catalogue candidates remain Researching, and no actual title, pause, map, settings, HUD, phone or radial replacement has been approved or deployed. Real GTA V screen integration remains gated on archive inspection and game-side tests.

## Release gate

The UI/UX track is production-ready only when all shipped screens have verified compatibility, complete native-action coverage, input/accessibility evidence, performance measurements, clean rollback and no unresolved critical defects. A mock-up, screenshot or dashboard panel does not satisfy this gate.


## Community implementation research (18 November 2026)

The research catalogue now tracks current candidates for portions of the intended UI:

- **HUD and weapon wheel:** [GTA VI HUD](https://www.gta5-mods.com/uk/scripts/gta-vi-hud-mod-893860ef-7b03-4c23-abd9-92b224291705) has a Legacy-specific archive and advertises status bars, radial weapon selection, vehicle cards, world markers and other gameplay indicators. Its ScriptHookVDotNet/LemonUI stack and current Legacy build range remain to be verified.
- **Minimap and HUD:** [GTA VI Styled Minimap & HUD (Legacy)](https://www.gta5-mods.com/scripts/gta-vi-styled-minimap-legacy) is an OIV plus separate scripts package. The Hub cannot yet execute OIV/RPF operations.
- **Map rendering:** [GTA VI MiniMap (3D Buildings)](https://www.gta5-mods.com/scripts/gtavi-minimap-3d-buildings-legacy-enhanced) is a beta minimap candidate with 3D buildings and custom waypoints. Do not combine it with other minimap replacements without compatibility tests.
- **Pause-menu extension:** [Pause Menu Library](https://www.gta5-mods.com/fi/tools/pause-menu-library) exposes an API for mod-specific native-style pause-menu pages and settings controls. It does not redesign the native home or pause screens by itself.
- **Map layout and radar controls:** [MinimapRadarEditor VI_DayNightOverlay](https://www.nexusmods.com/gta5/mods/2213) can reposition/resize the radar and big map, but it depends on a compatible HUD stack and has restrictive distribution terms.
- **Loading screen:** [GTA 6 Style Loading Screen, Music & Icon](https://www.gta5-mods.com/misc/gta-6-style-loading-screen-music-icon) is an OpenIV-based loading-art/audio candidate with separate permissions for included music.

All of the above remain Researching. The catalogue must not offer a one-click Install action until the actual archive is inspected, its author licence is recorded, an exact supported game build is confirmed, file conflicts and required frameworks are pinned, and a disposable-install/rollback test passes.

**Current gap:** research has not identified a verified package that replaces the entire title/home screen, pause navigation, full map UI and all native settings as one coherent GTA VI-like interface. The project will implement or curate each surface independently, preserving native functionality and a fallback path.
