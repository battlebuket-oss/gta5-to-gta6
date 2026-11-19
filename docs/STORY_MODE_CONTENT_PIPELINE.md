# Story Mode content pipeline

**Scope:** GTA V Legacy, Windows, offline Story Mode. The original GTA V campaign and user saves remain first-class and must never be silently replaced.

## Campaign-content boundary

The Hub may manage legally distributed, author-approved GTA V mission packs and original fan-made campaigns. It must not reproduce or redistribute Rockstar's proprietary GTA VI script, mission sequences, dialogue, voice performances, characters-as-written, cutscenes, maps or other protected assets. A request for the exact GTA VI story is therefore not an installable feature. The supported alternative is an original, clearly labelled fan-made campaign using independently written characters, missions and dialogue, plus compatible community story packs.

## Content types

- `campaign`: a separately selectable sequence of missions with its own progression namespace.
- `mission-pack`: a collection of independent missions.
- `side-mission`: optional missions that do not replace the base campaign.
- `character-arc`: missions and activities scoped to a protagonist or original character.
- `activity-pack`: repeatable jobs, races, challenges or hobbies.
- `ambient-event`: world encounters that do not claim to be authored campaign missions.

## Required metadata before a story package can be approved

| Field | Purpose |
|---|---|
| contentId, contentType, title, author | Stable identity and attribution |
| sourceUri, licence, redistributionPermission | Provenance and legal distribution |
| gameEdition, minBuild, maxBuild | Exact target compatibility |
| frameworks and exact versions | Script/runtime prerequisites |
| missionIds and progressionNamespace | Prevent mission/state collisions |
| protagonistRequirements | Character availability and mission gating |
| startLocations and mapDependencies | Discoverability and world dependencies |
| saveDataPaths and persistenceModel | Isolate or preserve progression |
| packageDependencies and conflicts | Deterministic load and conflict checks |
| file manifest, hashes and sizes | Integrity and reproducible deployment |
| installPlan and rollbackPlan | Reversible deployment |
| voiceLanguages, subtitleLanguages | Honest language support |
| testEvidenceReference | External gameplay evidence, supplied by the tester |

Unknown fields are not guessed. Missing mandatory fields keep a pack in Researching/Blocked state.

## Runtime and save isolation

1. Keep the vanilla campaign profile immutable and selectable.
2. Store fan-made campaign metadata and progression separately from original GTA V save files wherever the supported scripting API permits.
3. Back up the user's Profiles directory before enabling a story pack that may write persistent state.
4. Never merge, migrate or delete save data automatically.
5. Apply changes only when GTA V is fully closed and process state is certain.
6. Use the existing transaction journal, ownership records and hash-guarded rollback.
7. On uninstall, remove only files still matching the Hub's recorded deployed hashes; preserve modified or user-owned files for manual review.

## Mission lifecycle acceptance contract

A tester must record, for every supported game build and package version: mission discovery, start, checkpoint, fail/retry, completion, reload, persistence, protagonist switching, cutscene transitions, map markers, controller/keyboard navigation, conflict combinations, crash recovery, uninstall and rollback. The Hub must store evidence as a user-provided test record; it must not infer a pass from installation success.

## Current research entries

Existing research leads include Lowriders DLC in Single Player, Big Bank Heists, The Golden Train Heist and GTA V New Game+. They remain unapproved until their exact archives, permissions, framework versions, build ranges, save behaviour and compatibility are independently established. The Hub must link to the genuine author source and must not redistribute packages where permission is absent.


## Manifest and signature contract

The Story Mode manager uses the optional `storyContent` descriptor in a package manifest. Its validation contract is implemented in `StoryContentValidationService` and runs both during ZIP import and again during deployment preflight. The descriptor is bound into publisher signatures using signature version 2.

A complete descriptor declares `contentType`, ordered `missionIds`, `protagonistRequirements`, `startLocations`, a unique `progressionNamespace`, `saveCompatibility`, `requiresSaveBackup`, `preservesOriginalCampaign`, `voiceLanguages`, `subtitleLanguages`, and an optional HTTPS `testEvidenceUri`.

Signature version 1 is retained only for legacy manifests without newer compatibility, ecology, or Story Mode metadata. A version-1 signed package that is later augmented with such metadata is rejected as an invalid signature. New content publishers should sign version-2 manifests.

The Hub validates metadata and manages package files; it does not execute opaque installers, create gameplay missions, or guarantee that an external plugin actually isolates its progression data. The tester must supply mission lifecycle evidence for every supported build and package combination.
