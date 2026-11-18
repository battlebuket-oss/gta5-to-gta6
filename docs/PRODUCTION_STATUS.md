# Production readiness status

Last reviewed: 2026-11-18

This document distinguishes implemented application functions from release requirements that have not yet been verified.

## Implemented in the current development branch

| Area | Status |
|---|---|
| Windows WPF application foundation | Implemented |
| Single-instance guard | Implemented |
| GTA V Legacy executable selection | Implemented with file identity and numeric version checks |
| Process lock | Implemented; fails closed on process-inspection errors and checks Legacy/Enhanced process names |
| Local ZIP package import | Implemented |
| ZIP path traversal, link, duplicate, undeclared-entry and size checks | Implemented |
| Manifest-declared SHA-256 and size verification | Implemented |
| Local imported-package inventory | Implemented |
| Local profile persistence | Implemented |
| Package build, dependency, conflict, exclusive-group and destination preflight | Implemented |
| Copy-only staged deployment | Implemented through the transaction service |
| Transaction journal, backups and rollback | Implemented |
| Managed package removal | Implemented with active deployment ownership and hash checks |
| Startup recovery for interrupted transactions | Implemented |
| Explicit third-party trust acknowledgement | Implemented in the UI |
| Windows x64 CI build, test and publish workflow | Configured; current checks must pass before release |

## Not yet production-ready

| Area | Remaining work |
|---|---|
| Publisher authenticity | No trusted publisher signatures or signed catalogue |
| Automatic package downloads | Not implemented; users download archives from author pages and import local ZIPs |
| Mod-host-specific installers | No OpenIV OIV/XML/GFX, ASI or other format-specific installation adapters |
| Tested package catalogue | Current catalogue entries remain research-only; no third-party package is approved for one-click install |
| Build compatibility | Numeric executable version detection exists, but supported builds have not been exhaustively tested against real mod releases |
| Profile activation | Version-aware desired-state reconciliation implemented using deployment journals; profiles still store package IDs rather than pinned versions and require the matching imported archives |
| Game launch integration | Direct GTA5.exe launch can require Steam or Rockstar Games Launcher and needs real-world verification |
| Performance and stability | No repeatable in-game benchmark matrix or extended gameplay soak tests |
| Game-side interface transformation | Not implemented; current interface is the Windows management application |
| Game-side accessibility mods | Research only; not installed or verified |
| Windows release signing | No code-signing certificate or signed installer configured |
| Upgrade/uninstall migration | No migration of older manual mod installations or complete application updater |
| Real-world rollback testing | Automated filesystem tests exist; tests on cloned real GTA V Legacy installations remain required |

## Mandatory release gates

1. The latest Windows CI run must complete with a successful application build, all tests passing and a publish artifact produced.
2. Review all compiler warnings and test failures; do not suppress failures to obtain a green build.
3. Test install, rollback, interrupted-deployment recovery, package removal and removal rollback against a disposable copy of each supported GTA V Legacy build.
4. Verify that user saves, launcher files and the original game executable remain untouched.
5. Test actual launch through supported Steam/Rockstar installation paths.
6. Test the packages individually and in documented combinations on the target hardware matrix; record frame-time, VRAM/RAM use and stability.
7. Review third-party licences and obtain required permissions. Do not redistribute binaries without explicit permission.
8. Establish publisher signing and a trusted catalogue/update-signing process before enabling a curated one-click catalogue.
9. Complete accessibility review for the Windows app and validate any game-side accessibility package separately.
10. Sign the Windows executable/installer and publish release notes, hashes, supported builds and rollback instructions.

A successful CI run is necessary but does not prove the platform is production-ready. Real game compatibility, licence review, signing and release testing are still mandatory.
