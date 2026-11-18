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

The transactional service is not yet connected to the dashboard's launch flow. Do not treat the current UI as a finished mod manager or use it to install mods. Automated tests, interrupted-transaction recovery, catalogue approval, UI integration, and a signed Windows release remain required.

## Mod catalogue and redistribution

The catalogue metadata and curated profiles ship with the application. Third-party binaries are bundled only when their licence explicitly permits redistribution. Otherwise, packages must be obtained from the author's official source or imported by the user, then validated. No mod is marked installable merely because it is popular or has a download page.

See:
- [Transformation scope](docs/TRANSFORMATION_SCOPE.md)
- [Delivery roadmap](docs/DELIVERY_ROADMAP.md)
- [Mod catalogue policy](docs/MOD_CATALOGUE_POLICY.md)
- [Initial mod research shortlist](docs/MOD_RESEARCH_SHORTLIST.md)

## Initial target

- GTA V Legacy on Windows
- .NET 8 / WPF
- Local-first; no account or cloud service required
- Story Mode only
