# Normalized ecology inventory snapshot schema

The Transformation Hub can compare two **normalized semantic inventory snapshots** for the same GTA V Legacy executable. This JSON schema is the input contract for that comparison workflow. It is not the raw output of `rage-cli catalog`, `rage-cli search`, or `rage-cli resource dump`.

## Important limitation

The Hub includes a provenance-checked heuristic candidate extractor for dumped metadata JSON, but it does not include a validated semantic parser that converts every resource into species, habitat, population and water records. A snapshot must still be produced by a separately reviewed semantic adapter or a controlled manual curation process. Do not treat a candidate field/path match as a verified species identity, and do not infer species identities from filenames, model catalogue rows, property-name frequencies, or screenshots.

The comparison tool always records:
- `completenessVerified = false`
- `packageEligibilityUnlocked = false`

Even if the semantic diff is clean, the app does not promote, approve or unblock ecology-sensitive packages. The rage-cli discovery pass searches seven supported metadata extensions, with a 10,000-hit cap per extension. The payload dumper handles up to 50 candidates per batch with a zero-based resume cursor. The candidate-analysis action can merge multiple batch index files from the same exact discovery report, checks global candidate-index coverage, and reports heuristic candidate rows with source RPF / JSON hashes. These stages still do not constitute a complete exact-build semantic inventory.

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
2. Build the model/texture catalogue, harvest the exact-build name dictionary, and run metadata discovery.
3. Dump each 50-resource metadata batch. Use the next zero-based candidate offset reported by the Hub until every candidate in the discovery report has a successful JSON payload.
4. Use **Analyze ecological metadata candidates** and select every batch index produced for the same discovery report. The Hub validates the shared game/tool/discovery provenance, merges retry batches by global candidate index and flags missing ranges.
5. Review the heuristic candidate report. It is useful for manual parser development, but it is not a semantic inventory and does not approve species, habitats or water definitions.
6. Produce vanilla and candidate normalized snapshots using the same separately reviewed semantic adapter and exact game build.
7. Use **Compare normalized ecology inventories**, select both snapshot JSON files, and review the saved comparison report, added species list and all semantic errors.

The report is saved under:

```text
%LOCALAPPDATA%\\GtaTransformationHub\\ecology-evidence\\
```

The report records both input file hashes and the exact executable fingerprint. It reports whether the supplied semantic records preserve the listed vanilla identities, but explicitly leaves completeness and package eligibility false.

## Release gate still outstanding

Before an ecology-sensitive package can ever be considered for approval, the project still needs:
- a validated exact-build GTA V Legacy archive parser and semantic adapter that converts the heuristic/raw candidates into normalized species, habitat, population, behaviour and water-system identities,
- exhaustive accounting of all relevant nested RPF resources with no hit caps or unreadable/unsupported entries,
- a trusted vanilla baseline covering every land/aquatic species, plant, habitat, population entry, behaviour and water system,
- package-bound candidate inventories and reproducible evidence provenance,
- in-game regression and rollback testing against a disposable installation.

Until those conditions exist, ecology-sensitive deployment remains fail-closed.
