# Transformation Scope: Maximum GTA VI-Inspired GTA V

## Product objective

Deliver the closest technically and legally feasible GTA VI-inspired single-player experience on GTA V Legacy, managed through one Windows desktop application. This is a mod orchestration and compatibility product, not a conversion of GTA V's engine into Rockstar's RAGE build for GTA VI.

"Closest" is measured by the combined player experience: world presentation, systemic reactivity, vehicle feel, character animation, law enforcement, ambient life, missions, and stability—not by claiming feature parity.

## Feature tracks

### P0 — Safe foundation
- GTA V Legacy installation detection and build fingerprinting.
- Mod package manifests with author, source URL, licence, version, supported game build, install method, file hashes, dependencies, conflicts, and uninstall/restore strategy.
- Profiles and per-mod toggles while the game is closed.
- Preflight validation and staged deployment.
- Atomic apply where possible, file backups, rollback, crash recovery.
- Detect GTA5.exe; freeze all configuration and filesystem operations while the game is running.
- Unlock only after the game process exits and deployment state is reconciled.
- Story Mode only; never deploy to GTA Online.

### P1 — Visual and world presentation
- Lighting, weather, timecycle, post-processing and environment improvements.
- Texture and vegetation improvements sized for 6 GB VRAM.
- Higher-quality roads, buildings, interiors and props.
- **Highest-priority world objective: a connected Leonida/Vice City playable map replacement** (Vice City, Keys, Grassrivers, Port Gellhorn, Ambrosia and Mount Kalaga), using existing prebuilt community map packages only where lawful and explicitly compatible with GTA V Legacy. A HUD/minimap texture or browser atlas does not satisfy this requirement.
- Map packages must include and validate world geometry, collision, LODs, streaming, road/traffic path nodes, pedestrian navigation, water, vegetation, interiors, region links and mission compatibility.
- Keep Enhanced-only map candidates disabled on Legacy. If no complete compatible Legacy package exists, report the map gap clearly; never label a visual theme or partial region as a complete Leonida conversion.
- Performance profiles for 1080p and RTX 4050-class hardware.

### P2 — Vehicles and physics feel
- High-quality add-on vehicles and lore-friendly replacements.
- Handling, suspension, braking, traction, damage and camera tuning.
- Vehicle interiors, sounds, traffic variety and density.
- Conflict-aware handling.meta, vehicles.meta, carcols and related data-file deployment.

### P3 — NPCs, crowds and police
- Contextual reactions, ambient scenarios, pedestrian density and variety.
- Traffic behaviour, emergency response, pursuit and wanted-system improvements.
- Witness, search, dispatch and escalation systems where mods support them.
- Ambient interaction and animation improvements.
- These remain authored/scripted game systems unless a specific mod genuinely adds another capability.

### P4 — Story Mode and campaign expansion (primary product track)
- Preserve the complete original GTA V campaign and user saves.
- Curate new single-player campaigns, mission packs, protagonist-specific arcs, side missions, stranger encounters, jobs, races, hobbies, businesses and repeatable world events.
- Classify every content pack by type, protagonist, prerequisites, start location, language/voice coverage, completion behaviour and save compatibility.
- Provide separate Original Campaign, Story Expansion and Activities profiles, with conflict/load-order checks.
- Back up relevant save data before any story-package operation; never silently modify or delete saves.
- Test mission start, checkpoints, failure/retry, completion, save/reload and uninstall.
- Prefer coherent, high-quality story arcs over raw mod count. Clearly label fan-made content; never present it as official Rockstar content.
- The hub can manage and integrate authored content; it cannot create professional voiced/cinematic missions merely by enabling a toggle.

### P5 — Accessibility and comfort (first-class product track)
Game-side features to research and verify:
- Subtitle readability, size/contrast/background and speaker identification where supported.
- Color-vision-friendly HUD/map presets, contrast, interface scaling and reduced clutter.
- Control remapping and clear keyboard/controller prompts.
- Camera shake, motion blur and other motion-comfort controls where supported.
- Dialogue/music/effects balance, audio clarity and sound captions where technically possible.
- Aim assistance and difficulty options where supported.
- Explicitly label unsupported or untested options; never display a fake toggle.

Hub-side requirements:
- Keyboard-first navigation and visible focus.
- Windows Narrator-compatible accessible names, roles and state announcements.
- High-contrast mode, scalable text, reduced motion and no color-only status communication.
- Plain-language validation errors and accessible recovery flows.
- Accessible installer, update and rollback experiences.

### P6 — Optional experimental AI companion systems
- Optional local AI-assisted dialogue or ambient NPC experiments only if a compatible GTA V mod/plugin exists and can be safely integrated.
- Keep inference outside the game process where possible, with explicit opt-in, local model/API configuration, privacy controls, and performance budgets.
- Never represent an external chatbot overlay as native GTA VI NPC intelligence.
- AI must not bypass game security, inject into GTA Online, or modify the game while it is running.

## Accuracy note about GTA VI

Do not describe GTA VI NPCs as confirmed ChatGPT-like generative agents. Public reporting through November 2026 says Take-Two CEO Strauss Zelnick stated generative AI has "zero part" in what Rockstar is building for GTA VI. Rockstar's public descriptions focus on authored behaviour, writing, animation, and reactive interaction. The precise internal AI architecture has not been published. The project should target observable gameplay outcomes, not unverified rumours.

## Non-goals and constraints

- No GTA VI source code, proprietary assets, leaked content, or unauthorized extraction.
- No claim of exact GTA VI parity or conversion.
- No bundled third-party mods without redistribution permission.
- No mod deployment or toggling while GTA5.exe is active.
- No GTA Online support.
- No destructive direct overwrites without backup and verified restore path.
- Mods requiring incompatible game builds, unsupported loaders, or unsafe installers are blocked rather than force-installed.

## Acceptance criteria

1. The user can choose a profile and toggle supported mods only while GTA V is closed.
2. Preflight identifies missing dependencies and conflicts before touching the game directory.
3. Apply records every file copied, replaced, or generated and preserves a recoverable original.
4. Launch is disabled until deployment validation succeeds.
5. When GTA5.exe appears, all mod controls and deployment actions become read-only/locked.
6. If the game crashes or the manager closes, next startup reconciles the session and offers safe recovery.
7. After GTA5.exe exits, the manager unlocks and permits the next profile change.
8. Every catalogue entry links to its original author/source and states compatibility and licence details.


## Comprehensive experience target

The project now uses [the GTA VI-inspired experience coverage matrix](GTA6_EXPERIENCE_COVERAGE_MATRIX.md) as the feature acceptance reference. It covers visual fidelity, world/map/interiors, ambient life, traffic, police, vehicles, movement, combat, Story Mode, side activities, economy, wildlife, audio, UI, accessibility, stability, performance, privacy and security.

The goal is the closest feasible overall player experience, not a misleading claim that every GTA VI system can be reproduced inside GTA V. Each feature must be labelled as native, verified mod, hub feature, experimental, unavailable or blocked.

## P7 — In-game UI/UX transformation (first-class track)

- Rework the actual GTA V frontend/title entry, pause navigation, map, settings, HUD, phone, weapon/radial menus and loading/transition screens toward a cohesive modern GTA VI-inspired experience.
- Implement only through verified GTA V frontend/Scaleform, game-script or asset-replacement extension points; a companion interface is not represented as an in-game replacement.
- Preserve all native actions, keyboard/controller navigation, mission prompts, subtitles, save behaviour and accessibility layers.
- Use one compatible UI stack per surface, detect replacement conflicts, and deploy/rollback through the same transactional engine.
- Expose only settings with a tested real read/write path; never display fake controls.
- Validate each screen across supported game builds, resolutions, input devices, accessibility settings, missions and cutscenes.

The detailed screen inventory, architecture, UX rules and acceptance tests are defined in [the UI/UX transformation specification](UI_UX_TRANSFORMATION_SPEC.md).
