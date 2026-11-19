# GTA V Legacy gameplay, compatibility, performance and rollback test protocol

**Execution status: NOT RUN against a real GTA V installation.** This document defines the evidence required for a release. Automated unit tests and archive comparisons do not substitute for in-game execution.

## 1. Test environment record

For every run, record:

- GTA V edition: Legacy only for the current release target.
- Exact GTA5.exe product/file version and SHA-256.
- Game installation source and launcher: Steam or Rockstar Games Launcher.
- Every relevant RPF/archive SHA-256 before and after the test.
- Windows edition/build, GPU model/driver, CPU, physical RAM, VRAM and storage.
- Display resolution/refresh rate, graphics settings, VSync/frame cap and background applications.
- Hub version/commit, package IDs/versions, dependencies, load order and package manifest hashes.
- Save profile identifier and whether the save is a disposable test save.

Use a disposable clone of the game directory and a separate copy of Story Mode saves. Do not use the user's only installation as the test target.

## 2. Vanilla baseline

Before installing any package:

1. Hash GTA5.exe, all relevant RPF archives, launcher-owned files and the selected save files.
2. Start GTA V through the installed launcher, not only by direct executable invocation.
3. Complete the gameplay scenarios below on the unmodified installation.
4. Capture frame-time, memory, temperature and stability measurements.
5. Export the asset catalogue and exact-build ecology inventory where the extractor supports the resource.
6. Store the baseline report under the exact GTA5.exe fingerprint.

The baseline must be repeatable. Reboot or clear the test state between runs where required and retain raw logs, not only averages.

## 3. Gameplay and compatibility scenarios

Run each scenario on vanilla first, then repeat for each approved package individually and each declared compatible package combination.

| Scenario | Required observation |
|---|---|
| Cold launch and launcher hand-off | Launcher opens the correct edition; no missing DLL, script or loader dependency |
| Story Mode load and save | Existing campaign loads; disposable save can be saved, exited and reloaded |
| Character switching | All native characters switch; animations, camera and mission state remain valid |
| Dense city driving | Traffic, pedestrians, vehicles, collisions and streaming remain stable |
| Countryside and wilderness | Land vegetation, wildlife, ambient populations and world streaming remain present |
| Weather and day/night | Lighting, weather transitions, reflections and time-of-day changes behave correctly |
| Water surface and shoreline | Water-body identity, surface continuity, shoreline interaction, waves and weather response remain functional |
| Underwater traversal | Underwater visibility, lighting, depth response, aquatic vegetation and aquatic species remain present |
| Animal and population behaviour | Original species, population entries, spawn groups and habitat relationships remain valid |
| Police response and combat | Wanted levels, dispatch, pursuit, combat, weapon wheel and mission prompts remain functional |
| Mission and cutscene transitions | Mission start/finish, checkpoints, cutscenes, subtitles and character control remain functional |
| Pause, map and settings | Native actions, waypoint, zoom/pan, settings persistence and back/cancel work |
| Controller and keyboard/mouse | Focus, navigation, input mapping and exit behaviour remain correct |
| Exit and relaunch | No locked process state, orphaned helper, incomplete transaction or persistent file handle |

Record every crash, visual defect, missing asset, broken native action and reproducible sequence. A screenshot alone is not a compatibility pass.

## 4. Vanilla identity and ecology acceptance

For the exact GTA V executable and source archive fingerprints:

- Every vanilla terrestrial and aquatic species identifier must remain present.
- Every vanilla plant, vegetation, underwater plant and aquatic vegetation identifier must remain present.
- Original model identities, population entries, spawn groups, habitat links and behaviour references must remain unchanged unless an explicitly reviewed compatibility mapping proves equivalence.
- New species and plants must use distinct identifiers and append-only population/habitat entries.
- No package may overwrite a vanilla model or resource by reusing its identity.
- Water-body definitions and interaction/simulation references must remain present. A rendering upgrade must not silently replace water simulation identity.
- Any missing, unreadable, capped, ambiguous or unparsed source resource makes the relevant inventory incomplete and blocks the package.

Compare structured semantic inventories, not only file counts or archive hashes. Keep source archive hashes, extractor version/digest, resource paths, parsed identifiers and comparator output with the test record. Ecology-sensitive packages remain blocked until package-bound evidence proves these conditions.

## 5. Performance methodology

Measure the vanilla baseline and every approved package set under identical conditions.

- Run each repeatable benchmark route at least three times after a warm-up pass.
- Capture frame-time distribution (P50/P95/P99), average FPS and 1% low FPS.
- Capture peak system RAM, process working set, VRAM use, CPU/GPU utilisation and temperatures.
- Include a minimum 60-minute soak for a candidate release profile, covering driving, streaming, weather and water scenarios.
- Record stutter, asset pop-in, texture loss, audio dropouts, input latency and crashes.
- Compare each profile against the same-build vanilla baseline. Explain material regressions; do not hide outliers or claim a fixed FPS target without hardware-specific evidence.

A profile is not performance-approved merely because the Hub recommends it. The current hardware recommendation is guidance only and does not change GTA V settings.

## 6. Deployment, interruption and rollback

On a disposable game copy:

1. Record hashes for GTA5.exe, RPF archives, launcher files, saves and all target paths.
2. Apply one package and confirm every deployed file matches its manifest.
3. Confirm the deployment journal records ownership, original backups and resulting hashes.
4. Remove the package and verify only files owned by that deployment are removed.
5. Roll back and compare every protected/original file against the pre-test hash.
6. Repeat with profile activation, stale-package removal and profile rollback.
7. Simulate process termination or machine interruption at each transaction stage supported by the journal; relaunch the Hub and verify recovery is deterministic.
8. Repeat rollback after a package file has been externally changed. The Hub must refuse to overwrite user-modified content and explain the conflict.
9. Confirm the original executable, RPF archives, launcher files and save files remain unchanged.
10. Repeat apply/rollback twice to verify idempotency and no orphaned backup/journal state.

A rollback pass requires exact hash equality for protected files and a successful game launch after restoration.

## 7. Release evidence

Each supported game build and approved package combination must have a signed-off record containing:

- Test environment and exact executable/archive fingerprints.
- Test case results with pass/fail/blocked status.
- Before/after hashes and semantic ecology comparison report.
- Raw performance captures and summary statistics.
- Crash logs, screenshots/video and reproducible defect references.
- Install, recovery, removal and rollback results.
- Reviewer, date and Hub commit.

Until evidence exists, mark the build or package **Not tested**. Do not infer compatibility from a successful compile, unit test, archive import or catalogue entry.
