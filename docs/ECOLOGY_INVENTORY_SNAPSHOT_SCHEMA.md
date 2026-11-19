# Normalized ecology inventory snapshot schema

The Transformation Hub can compare two **normalized semantic inventory snapshots** for the same GTA V Legacy executable. This JSON schema is the input contract for that comparison workflow. It is not the raw output of `rage-cli catalog`, `rage-cli search`, or `rage-cli resource dump`.

## Important limitation

The Hub currently does not include a validated parser that converts every rage-cli metadata dump into these normalized species, habitat, population and water records. A snapshot must be produced by a separately reviewed semantic adapter or a controlled manual curation process. Do not infer species identities from filenames, model catalogue rows, property-name frequencies, or screenshots.

The comparison tool always records:
- `completenessVerified = false`
- `packageEligibilityUnlocked = false`

Even if the semantic diff is clean, the app does not promote, approve or unblock ecology-sensitive packages. The bounded rage-cli discovery/dump workflow currently searches selected patterns and dumps at most 50 candidate resources; it is not a complete exact-build inventory.

## Required top-level fields

```json
{
  "schemaVersion": 1,
  "gameBuild": "1.0.3889.0",
  "gameExecutableSha256": "64_HEX_CHARACTERS_FROM_THE_SELECTED_GTA5_EXE",
  "extractorId": "reviewed-semantic-adapter-id",
  "extractorVersion": "1.0.0",
  "sourceArchiveSha256": [
    "64_HEX_CHARACTERS_PER_SOURCE_RPF"
  ],
  "species": [],
  "habitats": [],
  "populationEntries": [],
  "waterBodies": []
}
```

The selected game build must be the exact numeric build shown by the Hub. The executable digest must be computed from the exact `GTA5.exe` selected in the app. Every source archive hash must identify a GTA V archive read by the adapter. Baseline and candidate snapshots must use the same extractor ID/version and the same set of original source-archive hashes; a comparison across adapters or a changed original archive set is rejected. The adapter ID/version and source hashes are provenance hints, not a claim that all source content was enumerated.

## Species record

Each species record contains:
- `id`: stable semantic identity, not just a display name.
- `domain`: `TerrestrialAnimal`, `AquaticAnimal`, `TerrestrialPlant`, or `AquaticPlant`.
- `modelIdentifiers`: all model identifiers used by that species.
- `habitatIds`: exact habitat references.
- `behaviourProfileId`: stable behaviour identity.
- `behaviourDefinitionSha256`: digest of the normalized behaviour definition.

## Habitat record

Each habitat contains:
- `id`
- `habitatType`
- `waterBodyId` (empty for non-aquatic habitats)
- `definitionSha256`

## Population record

Each population/distribution entry contains:
- `id`
- `speciesId`
- `habitatId`
- `definitionSha256`

Every population row must refer to an existing species and habitat, and the habitat must also be declared by that species.

## Water-body record

Each water body contains:
- `id`
- `waterType`
- `simulationProfileId`
- `interactionProfileId`
- `renderingProfileId`
- `simulationDefinitionSha256`
- `interactionDefinitionSha256`
- `renderingDefinitionSha256`

The semantic diff permits rendering-profile changes so a rendering upgrade can be evaluated separately. It does not permit replacing the original water simulation or interaction identity.

## Supported app workflow

1. Select and validate a GTA V Legacy installation in the Hub.
2. Produce a vanilla baseline snapshot and a candidate snapshot using the same reviewed adapter and exact game build.
3. Use **Compare normalized ecology inventories** in the Ecological Fidelity section.
4. Select the baseline and candidate JSON files. The Hub checks both snapshot game builds and GTA5.exe SHA-256 values against the currently selected installation.
5. Review the saved local comparison report, added species list and all semantic errors.

The report is saved under:

```text
%LOCALAPPDATA%\\GtaTransformationHub\\ecology-evidence\\
```

The report records both input file hashes and the exact executable fingerprint. It reports whether the supplied semantic records preserve the listed vanilla identities, but explicitly leaves completeness and package eligibility false.

## Release gate still outstanding

Before an ecology-sensitive package can ever be considered for approval, the project still needs:
- a validated exact-build GTA V Legacy archive parser and semantic adapter,
- exhaustive accounting of all relevant nested RPF resources with no hit caps or unreadable/unsupported entries,
- a trusted vanilla baseline covering every land/aquatic species, plant, habitat, population entry, behaviour and water system,
- package-bound candidate inventories and reproducible evidence provenance,
- in-game regression and rollback testing against a disposable installation.

Until those conditions exist, ecology-sensitive deployment remains fail-closed.
