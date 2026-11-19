# GTA VI Transformation Hub

A Windows desktop mod manager for a GTA V Legacy single-player installation. The product goal is a curated, reversible transformation that brings selected visual, world, vehicle, NPC, police, and gameplay systems closer to the feel of a modern open-world game. It is not a GTA VI engine conversion and does not claim feature parity with GTA VI.

## Core safety lifecycle

1. Detect and validate the GTA V installation.
2. Select locally installed, individually verified mod packages.
3. Validate package hashes, supported game build, dependencies, conflicts, and destination paths.
4. Stage package files and apply them through a journalled transaction with backups.
5. Launch GTA V Story Mode only after deployment has completed.
6. While GTA5.exe is running or process state is uncertain, block every configuration and deployment operation.
7. Roll back a deployment using its transaction journal, while protecting files changed by the user after installation.

The application does not inject code into a running GTA V process, modify files while the game is active, bypass DRM/anti-cheat, or support GTA Online. The deployment engine is copy-only: it does not execute third-party installers or delete arbitrary game files.

## Current engineering status

- WPF .NET 8 Windows application shell and initial game-path selection.
- Windows CI workflow; the initial shell build passed. Newer builds are being checked as the deployment engine is added.
- Package ID validation and typed package/profile foundations.
- Hash-verified deployment plan validation.
- Transaction journal, per-file backups, staged copy, rollback, and protection against overwriting files changed after installation.
- Fail-closed process monitor.
- Research shortlist for potential Legacy mods; **research candidates are not yet approved or enabled packages**.
- Quarantine-only ZIP/OIV import with HTTPS provenance, archive SHA-256, per-file SHA-256 inventory, size and compression-ratio limits, duplicate/case-collision rejection, path traversal checks, and symlink/reparse-point rejection.
- Read-only OpenIV OIV `assembly.xml` inspection (format 2.0–2.2): metadata, nested archive operations, target/source paths, unresolved payload references, and unknown commands are summarized without executing or applying the package.
- WPF import dialog connected to the quarantine importer. Imported packages stay in the local research library with status `Researching`; no GTA V game files are changed by import.
- Catalogue validation now rejects unresolved or self-referencing package dependencies and profiles that reference missing package IDs.

The deployment service is still not connected to a production-approved profile/apply/rollback workflow. The new archive importer is inspection-only and deliberately does not install anything. The application is **not production-ready**: RPF payload inspection and deployment adapters, root-level ASI/script-loader package handling, supported-build detection, full profile persistence/resolution, menu/HUD integration approval, in-game validation, RTX 4050 benchmarks, signed installer/update delivery, and clean-machine release testing remain outstanding.

## Mod catalogue and redistribution

The catalogue metadata and curated profiles ship with the application. Third-party binaries are bundled only when their licence explicitly permits redistribution. Otherwise, packages must be obtained from the author's official source or imported by the user, then validated. No mod is marked installable merely because it is popular or has a download page.

See:
- [Transformation scope](docs/TRANSFORMATION_SCOPE.md)
- [Delivery roadmap](docs/DELIVERY_ROADMAP.md)
- [Mod catalogue policy](docs/MOD_CATALOGUE_POLICY.md)
- [Safe archive import](docs/SAFE_ARCHIVE_IMPORT.md)
- [Initial mod research shortlist](docs/MOD_RESEARCH_SHORTLIST.md)
- [GTA VI-inspired experience coverage matrix](docs/GTA6_EXPERIENCE_COVERAGE_MATRIX.md)

## Product experience priorities

Story Mode is the primary experience: preserve the original campaign and saves, then add compatible mission packs, character arcs, side activities and world events where the mod ecosystem supports them. Accessibility is a first-class track, including subtitle readability, screen-reader support research, control flexibility, readable UI and comfort settings. The catalogue currently includes research-only candidates such as GTA V New Game+, Lowriders DLC in Single Player, Subtitles Enhanced (Legacy) and GTA Access. None is approved for one-click installation yet.

The target is the closest feasible overall GTA VI-inspired experience across visuals, world systems, traffic, police, vehicles, combat, story, activities, audio, accessibility, stability and performance. GTA V's engine and available community content impose hard limits; the app will show honest coverage rather than claim exact GTA VI parity.

## Initial target

- GTA V Legacy on Windows
- .NET 8 / WPF
- Local-first; no account or cloud service required
- Story Mode only
