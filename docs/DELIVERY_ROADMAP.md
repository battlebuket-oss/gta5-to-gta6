# Production Delivery Roadmap

The product is delivered in phases, but the acceptance target is a production-grade Windows application. A phase is not considered complete merely because its UI exists.

## Product decisions
- First supported game: GTA V Legacy, Windows, Story Mode only.
- The app ships with its own catalogue metadata, categories, curated profiles, compatibility rules, and package acquisition workflow.
- Third-party mod binaries are not redistributed unless the author explicitly permits redistribution. Where redistribution is not permitted, the app provides the verified source and an integrated download/import flow.
- Users can configure mods only while GTA V is closed. Apply is a pre-launch operation. Once GTA5.exe starts, all configuration and deployment controls are locked until it exits.

## Phase 0 — Product and compatibility research
Deliverables:
- Define supported GTA V Legacy builds and supported mod loaders.
- Verify each proposed mod's source, author, licence, current version, install method, dependencies, conflicts, performance cost, and uninstall method.
- Establish a compatibility matrix and a repeatable clean-game test environment.
Exit gate: no catalogue item is marked supported without evidence and a tested install/restore procedure.

## Phase 1 — Core desktop foundation
Deliverables:
- Windows desktop shell, game discovery, edition/build fingerprinting.
- Local catalogue and profile persistence.
- Package manifest model and source attribution.
- Game process monitor and strict configure-before-launch lock.
- Structured logging, diagnostics, settings, crash-safe state file.
Exit gate: reliable install/start/exit detection and persistent profiles across restarts.

## Phase 2 — Mod package and deployment engine
Deliverables:
- Safe download/import, hash verification, archive inspection and path traversal protection.
- Dependency/conflict resolver and preflight report.
- Staging area, deployment journal, backups, rollback and recovery.
- Per-mod install adapters; no arbitrary executable installers by default.
- Idempotent apply and uninstall.
Exit gate: automated tests prove failed installs restore the previous game state.

## Phase 3 — Curated transformation catalogue
Categories:
- Graphics, weather, lighting and environment.
- Map, roads, buildings and interiors.
- Vehicles, handling, damage and sound.
- Pedestrians, traffic, police and world reactivity.
- Animation, movement, weapons and interaction.
- Story expansions, missions and side activities.
- Optional experimental AI-assisted features.
Exit gate: each shipped entry passes compatibility, licensing, security and performance review.

## Phase 4 — Performance and experience
Deliverables:
- Hardware detection and conservative presets for 6 GB VRAM / 1080p systems.
- Memory and frame-time guidance; avoid promising a fixed FPS.
- Profiles such as Balanced, Visual, Performance and Story.
- Conflict explanations and actionable repair guidance.
Exit gate: benchmark on documented hardware and publish known limitations.

## Phase 5 — Production hardening
Deliverables:
- Automated unit, integration and end-to-end tests.
- Signed installer and update channel.
- Secure update verification, least-privilege operation and no silent elevation.
- Backup retention controls, recovery wizard and uninstall cleanup.
- Accessibility, localization-ready UI, telemetry opt-in only, privacy documentation.
- Support diagnostics and reproducible bug reports.
Exit gate: release checklist, clean-machine install tests, upgrade/downgrade tests, rollback tests, security review and documented support policy.

## Release policy
No release is called production-ready until all applicable exit gates pass. The current repository is an early engineering foundation, not a finished product. Work proceeds in phases toward the production release.
