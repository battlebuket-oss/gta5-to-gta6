# Production readiness status

Last reviewed: 2026-11-18

This document distinguishes implemented development-branch functions from release requirements that have not yet been verified. The product is not yet a production release.

## Implemented in the current development branch

| Area | Status |
|---|---|
| Windows WPF application foundation | Implemented |
| Single-instance guard | Implemented |
| GTA V Legacy executable selection | Implemented with file identity and numeric version checks; real-edition identification still needs testing |
| Process lock | Implemented; fails closed on process-inspection errors and checks Legacy/Enhanced process names |
| Local ZIP package import | Implemented |
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
| Windows x64 CI build, test and publish workflow | Configured; the latest checks must pass before release |

## Not yet production-ready

| Area | Remaining work |
|---|---|
| Trusted publisher management | Add and revoke trusted RSA keys in the UI; signed central catalogue and update channel remain outstanding |
| Automatic package downloads | Not implemented; users download archives from author pages and import local ZIPs |
| Mod-host-specific installers | No OpenIV OIV/XML/GFX, RPF mutation, or other host-specific installation adapters |
| Tested package catalogue | Current mod candidates remain research-only; no third-party mod is approved for one-click installation |
| Build compatibility | Numeric executable version detection exists, but supported builds have not been exhaustively tested against real mod releases |
| Game launch integration | Direct GTA5.exe launch can require Steam or Rockstar Games Launcher and needs real-world verification |
| Performance and stability | No repeatable in-game benchmark matrix or extended gameplay soak tests |
| Game-side interface transformation | Not implemented; current interface is the Windows management application. Actual title screen, pause menu, map, settings and HUD replacements still need verified GTA V implementation packages and real-game tests |
| Game-side accessibility mods | Research only; not installed or verified |
| Windows release signing | No code-signing certificate or signed installer configured |
| Upgrade/uninstall migration | No migration of older manual mod installations or complete application updater |
| Real-world rollback testing | Automated filesystem tests exist; tests on cloned real GTA V Legacy installations remain required |

## Mandatory release gates

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

A successful CI run is necessary but does not prove the platform is production-ready. Real game compatibility, licence review, publisher-key governance, signing and release testing are still mandatory.
