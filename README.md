# GTA VI Transformation Hub

A Windows desktop mod manager for a GTA V Legacy single-player installation. The application is designed around a strict **configure-before-launch** lifecycle.

## Core rule: launch locks the configuration

1. Detect and validate the GTA V installation.
2. Select compatible, locally installed mod packages and enable/disable them.
3. Validate dependencies, conflicts, game edition, and required files.
4. Apply the selected profile as one deployment transaction, with a backup/rollback record.
5. Launch GTA V Story Mode.
6. While GTA5.exe is running, the manager enters read-only/locked mode: no mod toggles, installs, removals, profile changes, or file deployment. Changes become available only after the game process exits.

The app does not inject code into GTA V, modify a running process, bypass DRM/anti-cheat, or support GTA Online. It does not bundle third-party copyrighted mods. Users must obtain mods from their authors and follow each mod's licence and installation instructions.

## Initial target

- GTA V Legacy on Windows
- .NET 8 / WPF
- Local-first; no account or cloud service required
- Single-player only

## Planned modules

- Game discovery and edition validation
- Mod package catalogue and metadata manifest
- Dependency/conflict validation
- Profile management
- Staged deployment, backup, rollback, and recovery
- Process monitor and configuration lock
- Performance presets and diagnostics

## Repository status

This repository starts with the application foundation and safety model. Mod-specific installers must be added only after each package's exact file layout, compatibility, licence, and uninstall behaviour have been verified.
