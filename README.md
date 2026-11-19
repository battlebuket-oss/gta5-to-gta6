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

The application does not inject code into a running GTA V process, modify files while the game is active, bypass DRM/anti-cheat, or support GTA Online. The Hub's own file deployment remains copy-only and journalled. An optional, checksum-pinned OpenRAGE toolchain can execute its own OIV installer commands only after a clean dry-run, exact archive-hash recheck, and explicit user confirmation; it is not a blanket approval of the mod.

## Current engineering status

- WPF .NET 8 Windows application shell and initial game-path selection.
- Windows CI: the last verified green run [44475361459](https://github.com/battlebuket-oss/gta5-to-gta6/actions/runs/44475361459) passed restore, WPF build, automated tests, self-contained Windows x64 publish and artifact upload on commit `0cde33359088d675076a88b726593cf96f9ea7d9`. Later UI-selection and workflow-runtime changes are under CI verification. A green CI build is not an in-game compatibility result or signed production release.
- Package ID validation and typed package/profile foundations.
- Hash-verified deployment plan validation.
- Transaction journal, per-file backups, staged copy, rollback, and protection against overwriting files changed after installation.
- Fail-closed process monitor.
- Research shortlist for potential Legacy mods; **research candidates are not yet approved or enabled packages**.
- Quarantine-only ZIP/OIV import with HTTPS provenance, archive SHA-256, per-file SHA-256 inventory, size and compression-ratio limits, duplicate/case-collision rejection, path traversal checks, and symlink/reparse-point rejection.
- Read-only OpenIV OIV `assembly.xml` inspection (format 2.0–2.2): metadata, nested archive operations, target/source paths, unresolved payload references, and unknown commands are summarized without executing or applying the package.
- Optional OpenRAGE OIV toolchain: downloads the pinned upstream Windows x64 v0.4.0 archive from its author-hosted GitHub release, verifies the whole-archive SHA-256 and the installed CLI against the archive, then performs an OIV dry-run. Installation requires the exact retained OIV hash to match, no parser/installer blockers, explicit install confirmation, and a second confirmation for game-folder files. The Hub also exposes OpenRAGE list, uninstall-by-exact-package-group, and undo-latest operations. OpenRAGE is not bundled and no mod binaries are redistributed. The separate CodeWalker manual review handoff remains available; it is not the OpenRAGE installer path.
- WPF import dialog connected to the quarantine importer. Imported packages stay in the local research library with status `Researching`; no GTA V game files are changed by import.
- Catalogue validation rejects unresolved or self-referencing package dependencies and profiles that reference missing package IDs. A new Mod Control Centre persists ON/OFF selections locally, auto-selects declared dependencies, rejects conflicts/exclusive groups, and removes dependent selections when a dependency is switched off. These are saved loadout selections only: they do not install the selected catalogue packages.
- PresentMon CSV analysis for GTA5.exe captures, with v1/v2 frame-time column support, mean/median/P95/P99 frame times, average FPS, estimated 1% low, dropped-frame count and optional CPU/GPU busy metrics; benchmark metadata and JSON reports are saved locally.
- On-demand local package verification recomputes every extracted file's SHA-256 and checks for missing, modified or untracked files. The source archive is retained and hashed in the package library; this is local integrity checking, not a malware or provenance guarantee.
- Read-only CodeWalker OIV install-history audit reads the third-party install logs under `OIV_CW_Uninstall_Data` and reports package metadata and recorded file-operation counts. It does not alter the game, inspect the current state of every installed file, or claim a mod is active or working.

The application is **not production-ready**. The saved catalogue loadout is not yet connected to a single atomic profile-activation workflow. OIV installation is available only through the optional OpenRAGE adapter and remains subject to package-by-package review; the read-only ZIP/OIV library importer does not itself install packages. Remaining release blockers include: automated OIV install-state reconciliation with the local profile; supported-build and licence approvals for each mod; root-level ASI/script-loader conflict handling; real-game rollback and gameplay acceptance tests; complete in-game home/pause/map/settings/HUD conversion; story/character content (the proprietary GTA VI campaign cannot be reproduced); tested land/water flora and fauna stacks; RTX 4050 1080p profiling; code signing, installer/update delivery, and clean-machine release testing. No real GTA V gameplay or RTX 4050 benchmark has been executed by this CI workflow. Do not treat this build as an approved mod pack or launch-ready game conversion.

## Mod catalogue and redistribution

The catalogue metadata and curated profiles ship with the application. Third-party binaries are bundled only when their licence explicitly permits redistribution. Otherwise, packages must be obtained from the author's official source or imported by the user, then validated. No mod is marked installable merely because it is popular or has a download page.

See:
- [Transformation scope](docs/TRANSFORMATION_SCOPE.md)
- [Delivery roadmap](docs/DELIVERY_ROADMAP.md)
- [Mod catalogue policy](docs/MOD_CATALOGUE_POLICY.md)
- [Safe archive import](docs/SAFE_ARCHIVE_IMPORT.md)
- [OpenRAGE OIV integration](docs/OPENRAGE_OIV_INTEGRATION.md)
- [Initial mod research shortlist](docs/MOD_RESEARCH_SHORTLIST.md)
- [GTA VI-inspired experience coverage matrix](docs/GTA6_EXPERIENCE_COVERAGE_MATRIX.md)
- [Performance benchmarking](docs/PERFORMANCE_BENCHMARKING.md)

## Product experience priorities

Story Mode is the primary experience: preserve the original campaign and saves, then add compatible mission packs, character arcs, side activities and world events where the mod ecosystem supports them. Accessibility is a first-class track, including subtitle readability, screen-reader support research, control flexibility, readable UI and comfort settings. The catalogue currently includes research-only candidates such as GTA V New Game+, Lowriders DLC in Single Player, Subtitles Enhanced (Legacy) and GTA Access. None is approved for one-click installation yet.

The target is the closest feasible overall GTA VI-inspired experience across visuals, world systems, traffic, police, vehicles, combat, story, activities, audio, accessibility, stability and performance. GTA V's engine and available community content impose hard limits; the app will show honest coverage rather than claim exact GTA VI parity.

## Initial target

- GTA V Legacy on Windows
- .NET 8 / WPF
- Local-first; no account or cloud service required
- Story Mode only
