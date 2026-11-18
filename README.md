# GTA V Transformation Hub

A Windows desktop mod manager for GTA V Legacy Story Mode. The project aims to curate a reversible, GTA VI-inspired single-player experience using compatible community modifications. It is not a GTA VI engine conversion and does not claim feature parity with GTA VI.

## Implemented application foundation

The current development branch includes:

- A .NET 8 WPF desktop application with a single-instance guard.
- Manual GTA5.exe selection, executable metadata identity checks and numeric build detection.
- Fail-closed process monitoring for GTA V Legacy and Enhanced process names.
- Local ZIP package import with a root-level gta-hub-manifest.json.
- Direct user-supplied HTTPS ZIP/OIV downloads with redirect validation, DNS checks at socket-connect time, bounded streaming and SHA-256 calculation.
- Restricted OpenIV OIV 2.2 import for direct loose-file add operations only; RPF edits, patch operations and other commands are rejected.
- Ecology preservation contracts and fail-closed preflight for animal, plant, vegetation, model, texture, map, animation and metadata assets.
- Shared mutation serialization across deployment, removal and rollback, plus protected paths for executables, RPF archives and save/profile folders.
- Story Mode save-folder archive backups and conservative hardware/performance guidance.
- Archive path, duplicate, link, undeclared-file and expansion-size checks.
- SHA-256 and file-size verification for every manifest-declared file.
- A local package library and persistent user profiles with exact package-version pins.
- Desired-profile reconciliation using version-aware deployment journals: stale packages are removed, intact active packages are preserved, and missing/repair-needed packages are deployed.
- Package preflight for build compatibility, dependencies, conflicts, exclusive groups, destination collisions, file hashes and publisher signatures.
- Local RSA publisher-key trust store with fingerprint verification, trust/revocation UI and signature-aware preflight.
- Copy-only staged deployment with per-file backups and transaction journals.
- Recovery of interrupted transactions, guarded rollback and package removal based on active deployment records and matching file hashes.
- Explicit third-party trust acknowledgement and confirmation before applying packages.
- Windows CI configured to build, test and publish a Windows x64 artifact.

The current CI checks must complete successfully before this branch is eligible for merge or release. A successful CI run is necessary but does not prove compatibility with a real GTA V installation.

## Using the development build

1. Build and run the WPF application on Windows with the .NET 8 SDK.
2. Select the GTA V Legacy GTA5.exe. The app checks file metadata and a numeric product/file version; it does not yet verify the executable's Authenticode signature.
3. Obtain a package ZIP from the genuine author/source page and inspect its licence and contents.
4. Import the ZIP. The archive must follow the format in [Package authoring](docs/PACKAGE_AUTHORING.md).
5. Select the package, run preflight, review the source and licence, and acknowledge the third-party execution risk.
6. Apply the package. The Hub copies only declared files and records a transaction.
7. Use Remove selected to remove files only when the Hub can match an active deployment record and the package hash.
8. Use Rollback latest to reverse the most recent deployment or removal in the current application session.

Direct executable launch may require Steam or Rockstar Games Launcher. The launch path still requires real-world verification across supported installation types.

## Important security limitations

- SHA-256 values declared by a package detect corruption but do not authenticate an author by themselves. Optional RSA-SHA256 package signatures are verified only when the public key has been independently verified and trusted locally.
- Unsigned and unknown-publisher packages remain importable but warn during preflight; an invalid signature from a trusted key is rejected. There is not yet a signed central catalogue or signed application-update channel.
- The Hub downloads only when the user supplies a direct HTTPS ZIP/OIV URL. It does not scrape HTML pages, bypass host-controlled download flows or crawl mod-host APIs.
- The Hub does not sandbox DLL, ASI or script files when the game loads them. Only install files from sources you trust.
- The Hub does not execute third-party installers, modify a running game, inject into a process, bypass DRM/anti-cheat or support GTA Online.
- Current catalogue entries are research-only. No third-party mod is approved for one-click installation. Ecology-sensitive packages require an approved preservation contract; a complete species inventory and automated semantic asset diff are still outstanding.
- OIV support is intentionally limited to direct loose-file add operations. RPF editing, XML/GFX, ASI-specific and other host-specific adapters are not implemented. OIV packages remain blocked until exact-build compatibility is independently verified.

Do not use the development build on your only game installation. Test against a disposable copy and back up your game files and saves first.

## Production readiness

See [Production readiness status](docs/PRODUCTION_STATUS.md) for implemented functions, outstanding engineering work and mandatory release gates.

Outstanding work includes a signed central catalogue/update channel, full OIV/RPF and game-specific adapters, a verified GTA V Legacy species/model baseline with automated semantic diffing, a tested mod compatibility matrix, the actual in-game UI transformation, game-side accessibility, repeatable performance tests, full launcher testing, Windows code signing and real-installation rollback validation.

## Ecological fidelity requirement

Preserve the same GTA V animal, plant and vegetation species and model identities. Improve rendering, textures, lighting, geometry and animation without replacing species, removing the original roster or changing species spawn tables. Ecology-sensitive assets are blocked unless their package includes an approved preservation contract backed by exact-build review evidence. See [Ecology preservation policy](docs/ECOLOGY_PRESERVATION_POLICY.md).

## Product experience priorities

Story Mode is the primary experience: preserve the original campaign and saves, then add compatible mission packs, character arcs, side activities and world events where the mod ecosystem supports them. Accessibility is a first-class research and implementation track, including subtitle readability, screen-reader support, control flexibility, readable UI and comfort settings.

The target is the closest feasible overall GTA VI-inspired experience across visuals, world systems, traffic, police, vehicles, combat, story, activities, audio, accessibility, stability and performance. GTA V's engine and available community content impose hard limits; the app will show honest coverage rather than claim exact GTA VI parity.

## Build and test

~~~powershell
dotnet restore tests/GtaTransformationHub.Tests/GtaTransformationHub.Tests.csproj
dotnet build src/GtaTransformationHub/GtaTransformationHub.csproj --configuration Release
dotnet test tests/GtaTransformationHub.Tests/GtaTransformationHub.Tests.csproj --configuration Release
dotnet publish src/GtaTransformationHub/GtaTransformationHub.csproj --configuration Release --runtime win-x64 --self-contained true
~~~

## Scope and policy

- [Transformation scope](docs/TRANSFORMATION_SCOPE.md)
- [Delivery roadmap](docs/DELIVERY_ROADMAP.md)
- [Mod catalogue policy](docs/MOD_CATALOGUE_POLICY.md)
- [Package authoring](docs/PACKAGE_AUTHORING.md)
- [Production readiness status](docs/PRODUCTION_STATUS.md)
- [Initial mod research shortlist](docs/MOD_RESEARCH_SHORTLIST.md)
- [GTA VI-inspired experience coverage matrix](docs/GTA6_EXPERIENCE_COVERAGE_MATRIX.md)

## Initial target

- GTA V Legacy on Windows
- .NET 8 / WPF
- Local-first; no account or cloud service required
- Story Mode only
