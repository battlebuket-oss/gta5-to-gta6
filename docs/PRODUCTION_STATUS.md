# Production readiness status

Last reviewed: 2026-11-19

This document distinguishes implemented development-branch functions from release requirements that have not yet been verified. The product is not yet a production release.

## Implemented in the current development branch

| Area | Status |
|---|---|
| Windows WPF application foundation | Implemented |
| Single-instance guard | Implemented |
| GTA V Legacy executable selection | Implemented with file identity and numeric version checks; real-edition identification still needs testing |
| Process lock | Implemented; fails closed on process-inspection errors and checks Legacy/Enhanced process names |
| Local ZIP package import | Implemented |
| Direct HTTPS package acquisition | Implemented for user-supplied direct URLs; bounded streaming, redirect validation and DNS validation at socket connection |
| OIV 2.2 import | Implemented for direct loose-file add operations only; RPF/patch/script operations rejected and compatibility-unverified packages blocked |
| Ecology preservation and semantic comparison | Fail-closed terrestrial/aquatic classifier and parser-independent inventory comparison engine implemented; ALL ecology-sensitive packages remain blocked until trusted exact-build asset extractors, provenance and package-bound evidence are integrated |
| External asset inspection toolchain | Optional user-selected rage-cli v0.28.1 Windows x64 integration; verifies pinned SHA/version, runs strict model/texture cataloguing, bounded nested-RPF metadata discovery and selective JSON dumps for up to 50 resources. Records GTA5.exe, source/nested RPF and JSON hashes; removes staging after each candidate. Adds bounded JSON structure summaries (depth, node, property-name and scalar counts) without claiming semantic interpretation. Not a complete ecology baseline |
| Shared mutation lock and protected destinations | Implemented for deployment, removal, rollback and recovery, including a lock-already-held recovery path; executable, RPF and save/profile paths blocked |
| Story Mode save backup | Implemented as read-only ZIP archive backup of a user-selected Profiles folder; verification handles are closed before final archive rename |
| Hardware/performance guidance | Implemented as conservative recommendation only; no game settings are modified |
| ZIP path traversal, link, duplicate, undeclared-entry and size checks | Implemented |
| Bounded ZIP decompression | Implemented for manifest and payload extraction |
| Manifest-declared SHA-256 and size verification | Implemented |
| Local imported-package inventory | Implemented |
| Local profile persistence | Implemented, including exact package-version pins and backward-compatible legacy profile loading |
| Profile package selection | Implemented; ambiguous legacy profiles require manual version selection |
| Profile activation | Implemented against active deployment journals; stale package removal and missing package deployment are covered by tests |
| Transaction journal, backups and rollback | Implemented, including multi-step profile rollback and ownership-only removal records for already-missing files |
| Managed package removal | Implemented with active deployment ownership and hash checks |
| Startup recovery for interrupted transactions | Implemented |
| Explicit third-party execution-risk acknowledgement | Implemented in the UI |
| RSA publisher signatures | Implemented for package manifests using RSA-2048+, SHA-256 and PKCS#1 v1.5 |
| Local trusted-publisher key store | Implemented with independent fingerprint matching and an add-key dialog |
| Signature-aware package preflight | Implemented; unsigned and unknown-publisher packages warn, invalid trusted-key signatures reject |
| Windows x64 CI build, test and publish workflow | Configured; one earlier full run passed build, tests and self-contained publish; latest head has not yet been verified |
| Per-user Windows x64 installer definition | Implemented with Inno Setup; installer compilation and clean-machine installation still require a successful Windows workflow run |
| Real-game gameplay/performance/rollback test protocol | Test plan and evidence requirements documented; no real GTA V gameplay or performance run has been executed from this environment |
| Vanilla ecology identity validation | Preservation rules and semantic comparator exist; complete exact-build inventory and real-game package validation remain blocked |
| Release packaging workflow | Implemented: self-contained publish, test gate, installer compilation, SHA-256 checksums and tag-based GitHub release |
| Authenticode signing automation | Implemented conditionally for application EXE and installer; certificate secrets and a real signed release have not yet been configured or verified |

| In-game UI package contract | Implemented: UI surfaces, implementation type, input/resolution/accessibility declarations, native-action preservation and HTTPS evidence references are validated; overlapping selected surfaces are rejected |
| UI package publisher signatures | Implemented with signature version 3; v1/v2 canonical payloads remain backward-compatible; deployment requires a trusted publisher key and compatibility attestation |
| Hub accessibility preferences | Implemented locally: high contrast, 100–150% text scale and reduced-motion preference persist across app restarts; these settings do not modify GTA V |
| Story Mode test evidence recorder | Implemented: required mission lifecycle / input / recovery / rollback scenario matrix saved against package archive SHA-256 and exact GTA5.exe SHA-256; never auto-approves a package |
| GTA5.exe evidence fingerprint | Implemented SHA-256 calculation and validation for exact-build test records |

## Not yet production-ready

| Area | Remaining work |
|---|---|
| Trusted publisher management | Local add/revoke UI and independent fingerprint confirmation are implemented; signed central catalogue and signed application-update channel remain outstanding |
| Automatic package downloads | Direct user-supplied HTTPS URL downloads implemented; no HTML scraping, host API crawling or signed catalogue feed |
| Mod-host-specific installers | Restricted OIV 2.2 loose-file add import implemented; no RPF mutation, XML/GFX, ASI-specific or other host-specific deployment adapters |
| Tested package catalogue | Current mod candidates remain research-only; no third-party mod is approved for one-click installation. All ecology-sensitive packages are blocked; exact-build extraction, inventory provenance and package-preflight integration remain outstanding |
| Exact ecological extraction | Implement semantic interpretation of the dumped resources, recursive coverage of all relevant archive metadata beyond the current 50-resource limit, exact-build population/species/habitat/behaviour/water definition inventories, and package-bound snapshots wired to preflight |
| Build compatibility | Numeric executable version detection exists, but supported builds have not been exhaustively tested against real mod releases |
| Game launch integration | Direct GTA5.exe launch can require Steam or Rockstar Games Launcher and needs real-world verification |
| Performance and stability | No repeatable in-game benchmark matrix or extended gameplay soak tests |
| Game-side interface transformation | Not implemented; current interface is the Windows management application. Actual title screen, pause menu, map, settings and HUD replacements still need verified GTA V implementation packages and real-game tests |

| Actual in-game UI surface replacements | Not implemented. The signed UI descriptor and surface-conflict gate are management safeguards only; no title, pause, map, settings, HUD, phone, radial or loading-screen replacement is currently verified |
| Story Mode mission implementation | Not implemented by the Hub. It can validate and manage community-authored story packages and record tester evidence; it cannot author or execute GTA VI's proprietary campaign |
| Game-side accessibility mods | Research only; not installed or verified |
| Windows release signing | No code-signing certificate or signed installer configured |
| Upgrade/uninstall migration | No migration of older manual mod installations or complete application updater |
| Real-world rollback testing | Automated filesystem tests exist; tests on cloned real GTA V Legacy installations remain required |

## Mandatory release gates

The detailed test matrix and evidence record are defined in [Gameplay, compatibility, performance and rollback test plan](GAMEPLAY_COMPATIBILITY_PERFORMANCE_TEST_PLAN.md). Test results must be recorded per exact game executable/archive fingerprint.

1. The latest Windows CI run must complete with a successful application build, all tests passing and a self-contained publish artifact produced.
2. Review all compiler warnings and test failures; do not suppress failures to obtain a green build.
3. Test install, rollback, interrupted-deployment recovery, package removal, profile reconciliation and removal rollback against disposable copies of each supported GTA V Legacy build.
4. Verify that user saves, launcher files and the original game executable remain untouched.
5. Test actual launch through supported Steam/Rockstar installation paths.
6. Test each approved package individually and in documented combinations on a hardware matrix; record frame-time, VRAM/RAM use and stability.
7. Review third-party licences and obtain required permissions. Do not redistribute binaries without explicit permission.
8. Verify publisher key fingerprints through independent channels and establish signed catalogue/update metadata before enabling a curated one-click catalogue.
9. Complete accessibility review for the Windows app and validate any game-side accessibility package separately.
10. Sign the Windows executable/installer and publish release notes, hashes, supported builds and rollback instructions.

A successful CI run is necessary but does not prove the platform is production-ready. The optional rage-cli integration is an asset-catalogue first step only; it does not establish a complete ecological baseline and does not enable ecology-sensitive packages. Real game compatibility, licence review, publisher-key governance, signing and release testing are still mandatory.
