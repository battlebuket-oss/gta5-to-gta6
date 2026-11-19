# GTA V Transformation Hub

A Windows desktop mod manager for GTA V Legacy Story Mode. The project aims to curate a reversible, GTA VI-inspired single-player experience using compatible community modifications. It is not a GTA VI engine conversion and does not claim feature parity with GTA VI.

## Implemented application foundation

The current development branch includes:

- A .NET 8 WPF desktop application with a single-instance guard.
- Manual GTA5.exe selection, executable metadata identity checks and numeric build detection.
- Fail-closed process monitoring for GTA V Legacy and Enhanced process names.
- Local ZIP package import with a root-level gta-hub-manifest.json.
- Direct user-supplied HTTPS ZIP/OIV downloads with redirect validation, DNS checks at socket-connect time, bounded streaming and SHA-256 calculation.
- Optional external OpenRAGE v0.4.0 integration for user-supplied OpenIV OIV packages: official release archive SHA-256 and executable/version verification, GTA V Legacy key setup, OIV dry-run preview, upstream blocker enforcement, exact OIV hash binding across preview/install, direct game-folder-file consent, and OpenRAGE list/uninstall/undo. OpenRAGE retains its own separate journal and GPL-3.0-only licence; it is not bundled into the Hub.
- Ecology preservation contracts, aquatic/underwater asset classification and fail-closed preflight for terrestrial and aquatic animals, plants, water, models, textures, maps, animations and metadata.
- Parser-independent ecological inventory comparison for vanilla species/model identity, habitats, population entries and water-body simulation/interaction identity; it permits distinct append-only new species and water rendering-profile upgrades.
- Optional GTA V asset inspection using a user-selected rage-cli v0.28.1 Windows x64 binary. The Hub checks the exact release SHA-256 and version, then runs a strict read-only model/texture catalogue scan without bundling or downloading the third-party executable.
- Bounded nested-RPF metadata discovery for population/model-set, animal/fish, vegetation/flora/fauna and water-related metadata candidates; reports exact executable/source-archive fingerprints and marks capped or unreadable searches incomplete.
- Selective nested-RPF metadata extraction and rage-cli JSON dumps for up to 50 supported candidate resources per batch, with resumable global candidate offsets, per-layer archive hashes, JSON validation and strict per-file/aggregate size limits. Multiple batches can be merged for heuristic candidate analysis. Temporary extraction is deleted after each candidate.
- Aquatic-path classification for fish, marine life, seagrass, kelp, coral/reef assets, underwater assets, water bodies and shoreline vegetation.
- Shared mutation serialization across deployment, removal and rollback, plus protected paths for executables, RPF archives and save/profile folders.
- Story Mode save-folder archive backups and conservative hardware/performance guidance.

- A Story Mode evidence recorder with required mission lifecycle, input, recovery and rollback scenarios, pinned to the imported package archive SHA-256 and exact GTA5.exe SHA-256.
- An in-game UI package contract for explicit screen surfaces, implementation type, input methods, resolution support, accessibility declarations, native-action preservation and compatibility/rollback evidence. Preflight rejects competing packages for the same UI surface.
- Signature version 3 for UI replacement metadata; legacy signature v1/v2 payloads remain unchanged. UI packages require a locally trusted publisher key and compatibility attestation.
- Persistent Hub accessibility preferences: high-contrast mode, 100–150% text scaling and a reduced-motion preference. These settings do not alter GTA V.
- The selected GTA V Legacy installation is remembered locally and revalidated against its exact executable identity, numeric build and SHA-256 at startup.

- A compatibility/performance evidence recorder for the selected package stack, exact GTA5.exe hash and package archive hashes. It captures the 19-scenario gameplay matrix, repeatable benchmark metrics and 60-minute soak results; it never auto-approves compatibility.
- A Story Mode-specific mission and save evidence recorder for story packages, independently pinned to package and executable hashes.
- A normalized ecology inventory comparison workflow validates user-supplied baseline/candidate snapshots against the selected GTA5.exe and records semantic diffs locally; it explicitly does not certify completeness or unlock ecology-sensitive packages.
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
3. Obtain a mod from its genuine author/source page and inspect its licence, exact Legacy edition, supported GTA V build and dependencies.
4. For a Hub-manifest ZIP, import it and follow [Package authoring](docs/PACKAGE_AUTHORING.md). For a prebuilt OpenIV OIV, use the separate OpenRAGE OIV panel, download/verify the upstream tool, select the local OIV, read the complete preview and install only after the explicit confirmations.
5. For Hub ZIP packages, select the package, run preflight, review source/licence and acknowledge third-party execution risk.
6. Apply the Hub ZIP package. The Hub copies only declared files and records its own transaction. OIV files are instead installed and journaled by OpenRAGE.
7. Use Remove selected/Rollback latest only for Hub-managed ZIP transactions. Use the OpenRAGE package list/uninstall/undo controls for OpenRAGE OIV packages.

Direct executable launch may require Steam or Rockstar Games Launcher. The launch path still requires real-world verification across supported installation types.

## Important security limitations

- SHA-256 values declared by a package detect corruption but do not authenticate an author by themselves. Optional RSA-SHA256 package signatures are verified only when the public key has been independently verified and trusted locally.
- Unsigned and unknown-publisher packages remain importable but warn during preflight; an invalid signature from a trusted key is rejected. There is not yet a signed central catalogue or signed application-update channel.
- The Hub downloads only when the user supplies a direct HTTPS ZIP/OIV URL. It does not scrape HTML pages, bypass host-controlled download flows or crawl mod-host APIs.
- The Hub does not sandbox DLL, ASI or script files when the game loads them. Only install files from sources you trust.
- The Hub does not run arbitrary third-party mod setup executables or scripts. The optional OpenRAGE CLI is a pinned external OIV file-operation tool; its operation plan is previewed and upstream blockers are enforced. The Hub does not modify a running game, inject into a process, bypass DRM/anti-cheat or support GTA Online.
- Current mod catalogue entries are research-only. No third-party mod is marked Tested or preselected in the RTX 4050 profile. The OIV picker can install a user-supplied OIV after preview and explicit consent, but it does not turn a Researching catalogue entry into an approved compatible package. All ecology- and water-sensitive packages remain blocked. The optional rage-cli extraction/heuristic candidate pipeline does not create a complete species, population, habitat or water-system inventory. Full exact-build archive coverage, semantic interpretation, trusted terrestrial/aquatic inventories, water-system baselines and package-preflight evidence integration remain outstanding.
- OpenRAGE support applies to OIV packages and only to the operations accepted by the pinned upstream tool. An OIV preview blocker is not bypassed. Arbitrary ZIP/RAR script bundles, standalone ASI/DLL packages, unsupported PSO operations and manual folder layouts are not automatically installed by OpenRAGE. Loose files that would be placed directly in the GTA V game folder require a separate acknowledgement. OIV preview is not a real-game compatibility test.

Do not use the development build on your only game installation. Test against a disposable copy and back up your game files and saves first.

## Real-game validation protocol

The required gameplay, vanilla-identity, compatibility, performance and rollback test protocol is documented in [Gameplay and release test plan](docs/GAMEPLAY_COMPATIBILITY_PERFORMANCE_TEST_PLAN.md). It has **not** been executed against a real GTA V installation; all real-game results remain Not tested until evidence is recorded.

## Windows installer and release pipeline

A Windows x64 self-contained publish and per-user Inno Setup installer are defined in `.github/workflows/windows-release.yml` and `installer/GtaTransformationHub.iss`. Manual workflow runs can produce unsigned development artifacts. Public tag releases require a configured Windows code-signing certificate; see [Windows release and signing procedure](docs/WINDOWS_RELEASE_AND_SIGNING.md). A generated installer is not evidence of real-game compatibility.

## Production readiness

See [Production readiness status](docs/PRODUCTION_STATUS.md) for implemented functions, outstanding engineering work and mandatory release gates.

Outstanding work includes a signed central catalogue/update channel, adapters for non-OIV ZIP/RAR/ASI/DLL/manual layouts and unsupported archive operations, validated GTA V Legacy terrestrial/aquatic asset extractors, trusted species and water-system baselines, package-preflight evidence integration for the semantic inventory comparator, a tested mod compatibility matrix, the actual in-game UI transformation, game-side accessibility, repeatable performance tests, full launcher testing, Windows code signing and real-installation rollback validation.

## RTX 4050 mod-stack plan

See [RTX 4050 mod-stack plan](docs/RTX4050_MOD_STACK.md) for the recommended one-per-slot HUD, weather, water, grass and wildlife alternatives. The profile is a research/test plan for 1080p and 6 GB VRAM, not a validated performance guarantee; 8K water textures and experimental ray tracing are excluded from the default direction.

The OIV workflow is documented in [OpenRAGE OIV integration](docs/OPENRAGE_OIV_INTEGRATION.md). OpenRAGE manages its own OIV journal, separate from Hub ZIP transactions.

## Flora, fauna and water requirements

Preserve every original GTA V terrestrial and aquatic animal species, plant and vegetation species, model identity and original population entry. New species may be added only as unique, append-only additions; they must never replace, remove or overwrite vanilla species or identities. Preserve the existing water-system identity and behaviour while improving surface realism, waves, reflections, refraction, foam, underwater lighting, caustics, depth response and weather interaction. Add underwater vegetation and aquatic animals as well as land flora and fauna, subject to exact-build verification.

All ecology-sensitive packages remain blocked until validated exact-build extraction and trusted package-bound inventory evidence exist; the semantic inventory comparator is implemented, but a package manifest or model/texture catalogue cannot self-approve this requirement. See [Ecology preservation policy](docs/ECOLOGY_PRESERVATION_POLICY.md), [Flora, fauna and water implementation specification](docs/FLORA_FAUNA_AND_WATER_SPEC.md) and [Asset extraction toolchain](docs/ASSET_EXTRACTION_TOOLCHAIN.md).

## Product experience priorities

Story Mode is the primary experience: preserve the original campaign and saves, then add compatible mission packs, character arcs, side activities and world events where the mod ecosystem supports them. Accessibility is a first-class research and implementation track, including subtitle readability, screen-reader support, control flexibility, readable UI and comfort settings.

The target is the closest feasible overall GTA VI-inspired experience across visuals, water, world systems, traffic, police, vehicles, combat, story, activities, audio, accessibility, stability and performance. GTA V's engine and available community content impose hard limits; the app will show honest coverage rather than claim exact GTA VI parity.

## Build and test

~~~powershell
dotnet restore tests/GtaTransformationHub.Tests/GtaTransformationHub.Tests.csproj
dotnet build src/GtaTransformationHub/GtaTransformationHub.csproj --configuration Release
dotnet test tests/GtaTransformationHub.Tests/GtaTransformationHub.Tests.csproj --configuration Release
dotnet publish src/GtaTransformationHub/GtaTransformationHub.csproj --configuration Release --runtime win-x64 --self-contained true
~~~

## Scope and policy

- [Mod catalogue validation and approval gates](docs/MOD_CATALOGUE_VALIDATION.md)
- [Story Mode content pipeline](docs/STORY_MODE_CONTENT_PIPELINE.md)

- [Transformation scope](docs/TRANSFORMATION_SCOPE.md)
- [Delivery roadmap](docs/DELIVERY_ROADMAP.md)
- [Mod catalogue policy](docs/MOD_CATALOGUE_POLICY.md)
- [Package authoring](docs/PACKAGE_AUTHORING.md)
- [Production readiness status](docs/PRODUCTION_STATUS.md)
- [Flora, fauna and water implementation specification](docs/FLORA_FAUNA_AND_WATER_SPEC.md)
- [Ecological inventory comparison engine](src/GtaTransformationHub/Services/EcologyInventoryDiffService.cs)
- [Asset extraction toolchain](docs/ASSET_EXTRACTION_TOOLCHAIN.md)
- [Initial mod research shortlist](docs/MOD_RESEARCH_SHORTLIST.md)
- [GTA VI-inspired experience coverage matrix](docs/GTA6_EXPERIENCE_COVERAGE_MATRIX.md)

## Initial target

- GTA V Legacy on Windows
- .NET 8 / WPF
- Local-first; no account or cloud service required
- Story Mode only
