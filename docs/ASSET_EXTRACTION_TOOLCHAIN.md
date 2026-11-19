# External GTA V asset inspection toolchain

## Purpose

The Transformation Hub uses an optional external parser to begin building an evidence-based inventory of a user's own GTA V Legacy installation. This is the first step toward exact-build ecological verification; it is not itself a complete flora/fauna or water-system baseline.

## Pinned tool

- Project: [VIRUXE/rage-cli](https://github.com/VIRUXE/rage-cli)
- Pinned release: [v0.28.1](https://github.com/VIRUXE/rage-cli/releases/tag/v0.28.1)
- Windows x64 asset: [rage-windows-x86_64.exe](https://github.com/VIRUXE/rage-cli/releases/download/v0.28.1/rage-windows-x86_64.exe)
- Pinned release asset SHA-256: `b7a9aa1d6196632544889cdc97481db001936dadc8e046784eadeb38acd3b1ac`
- Upstream licence: Unlicense (public domain dedication)

The Hub does not bundle, install, update or download this executable. The user downloads it from the upstream release and chooses the file in the Hub. Before the Hub runs it, the toolchain checks both the exact SHA-256 and the program's `rage 0.28.1` version output. A different build or renamed file is not accepted by this pin.

A new upstream version must not be silently accepted. Updating the pin requires review of the new release, its source/tag, supported formats, licence and published Windows asset digest, followed by tests against the target GTA V Legacy build.

## What the current integration does

The Hub launches the verified executable with `UseShellExecute=false` and a structured argument list, not through a command shell. It disables rage-cli's background update check, supplies the selected GTA5.exe explicitly, bounds captured process output, imposes a 45-minute scan timeout and kills the process tree when the user cancels.

The scan command is equivalent to:

```text
rage --no-update-check --exe <GTA5.exe> catalog --db <Hub catalogue.sqlite> build --game <GTA V folder> --full --strict --report <catalog-report.json>
```

The catalog is stored under the current user's Local AppData folder:

```text
%LOCALAPPDATA%\GtaTransformationHub\rage-cli\catalogue\
```

The generated SQLite database and JSON scan report are hashed and their paths are shown in the Hub. Catalogues are stored in a subfolder keyed by the SHA-256 of the selected GTA5.exe, preventing a scan from one executable build from silently reusing another build's database. No GTA V game file is modified by this scan.

## Ecology metadata discovery

The Hub also has an optional bounded metadata-discovery pass. It runs rage-cli's nested-RPF `search` command once per supported metadata container extension: `.ymt`, `.meta`, `.ytyp`, `.ymap`, `.ymf`, `.pso` and `.xml`. This avoids repeatedly walking every RPF archive for filename keywords that would return the same metadata files. Candidates are deduplicated across patterns and later sorted so names containing population/model-set, animal/fish, vegetation/flora/fauna and water terms are dumped first.

Each pattern is capped at 10,000 hits. Searches can run for up to 15 minutes per pattern, and captured stdout/stderr are each bounded to 16 MiB. A hit-cap, truncated output, unreadable archive, or parse/fingerprint error marks the discovery report incomplete. The JSON report records the pattern, nested archive path, resource name/type/size and JOAAT hashes, the selected GTA5.exe SHA-256, the rage-cli version and SHA-256, and SHA-256 fingerprints for each referenced source RPF. Repeated hits from overlapping patterns are grouped.

A search that reaches a hit cap, has truncated output, cannot parse JSON, or reports unreadable/skipped archives is marked incomplete. The report is stored under the same executable-fingerprint-specific catalogue directory. The search does not extract or modify game files.

**This is candidate discovery only.** It does not parse the metadata payloads, prove that the pattern list found every species or habitat, or establish a complete population/water baseline. Generic `.ymap` and `.ymt` searches may reach the cap. A completed pattern scan must not be labelled a complete ecological inventory.

## Selective metadata payload dumps

After a discovery report has been created, the Hub can dump supported metadata candidates from that report to JSON in indexed batches of up to 50 candidates. The offset is zero-based. Candidates are processed in order and the batch stops at the first failed candidate; the next offset remains at that candidate until a verified JSON payload is saved. Each batch creates a separate index report recording the total eligible candidate count, starting offset, next offset, batch-completion state and each payload's global candidate index. Use the next offset shown in the Hub to resume. The analysis action accepts multiple batch index files from the same discovery report, merges payloads by global candidate index, accepts retry overlap only when the source archive, internal path and JSON hashes agree, and reports any missing candidate indexes. Conflicting payload evidence is rejected. Each batch and analysis pass rechecks the selected GTA5.exe fingerprint, the pinned rage-cli version/digest, the shared discovery report and source RPF/JSON hashes.

For a nested resource, the Hub follows the exact RPF chain recorded by the search result. It extracts only the next nested `.rpf` layer into a temporary staging folder, opens that extracted archive, and repeats until it reaches the resource. It then calls rage-cli `resource dump --archive ... --json --output ...` for the leaf resource. It does **not** use recursive extraction, which would read every leaf in the archive.

Supported candidate extensions are `.ymt`, `.meta`, `.ytyp`, `.ymap`, `.ymf`, `.pso` and `.xml` when the search result identifies the entry as a resource. Candidate ordering gives priority to names containing population/model-set, animal/fish, vegetation/flora/fauna and water terms. Unsupported or unparseable resources are reported as errors rather than silently ignored.

Safety limits: 50 candidate dumps per batch, 512 MB of nested RPF extraction per candidate, 128 MB per JSON dump and 512 MB aggregate JSON output per batch. Candidate batches can be repeated until the candidate list is exhausted; output/index reports remain fingerprint-bound to the same GTA5.exe and discovery report. The index report includes the outer source RPF hash, each extracted nested-RPF hash, dumped JSON path/hash/size and parse root type. Temporary extracted RPF layers are removed after each candidate.

Outputs are stored under:

```text
%LOCALAPPDATA%\GtaTransformationHub\rage-cli\metadata-dumps\<full GTA5.exe SHA-256>\
```

Each JSON dump is also given a bounded structural summary in the index: object/array/scalar counts, maximum nesting depth and property-name frequency. Property-name collection is capped at 4,096 unique names, and individual names longer than 256 characters are shortened with an explicit truncation marker. These limits are recorded in the summary. The structural analyser does not treat field names or string values as proof of species identity, habitat, spawn behaviour or water simulation semantics.

The JSON dumps are inspection artifacts for building and validating the semantic inventory parser. They are **not** a verified species inventory and do not unlock ecology-sensitive packages.

## Exact-build game name harvesting

The Hub can run rage-cli's `names harvest --output` command against the selected GTA5.exe and local RPF archives. The resulting JOAAT name list is stored in a folder keyed by the complete GTA5.exe SHA-256. A sidecar manifest records the selected game build, executable hash, pinned rage-cli version/digest, names-list digest and entry count. The Hub revalidates that manifest and re-hashes the names list before passing it to later `resource dump --names` invocations.

This step helps convert more hashed model, archetype, enum and metadata member identifiers into readable names. It does not identify species by itself. A clean names harvest is not proof that all ecological resources were found, and a list with parser warnings remains warning-marked.

## Heuristic metadata candidate analysis

After dumping metadata payloads, the Hub's multi-select analysis action accepts one or more batch index files produced from the same discovery report. This lets a large candidate set be analyzed together after the 50-resource dump batches have been completed. Before reading them, it checks the exact GTA5.exe build/fingerprint, the pinned rage-cli version/digest, the shared discovery report hash, each source RPF hash, each JSON payload hash and the declared payload sizes. It rejects duplicate index selections, conflicting retry evidence, mismatched discovery reports, incomplete provenance, reparse points and paths outside the Hub's build-specific local directories.

The analyzer walks the bounded JSON trees and emits a separate, provenance-linked candidate-review report. It tags values where structure types, field names or JSON paths resemble population/model-set, terrestrial or aquatic animal/plant, habitat or water-related definitions. Model-name, model-hash, archetype, JOAAT and name-hash fields in an ecology/model context also receive a separate `ModelIdentifier` review tag while retaining their animal/plant/population context tags. More than one review tag may be retained for the same field/value. The report records each global candidate index, JSON path, field, resource, original archive path and source/dump hashes.

These matches are **heuristics**, not semantic truth. Generic field names, unresolved hashes, unsupported metadata classes, strings that happen to contain words such as `animal` or `water`, and data not covered by the bounded discovery/dump passes can all create false positives or omissions. The analyzer records whether every global resource index is represented by the selected batch indexes, whether discovery patterns were capped/errored, whether the 10,000-record output cap was reached, and whether any JSON traversal hit its one-million-node per-payload limit. Any such incompleteness is surfaced in the report's `InputIndexWasComplete` flag and warnings. A complete candidate walk still does not validate the semantics of the field mappings. The candidate report never becomes a normalized species baseline automatically. `CompletenessVerified` and `PackageEligibilityUnlocked` remain false, regardless of the number of candidates found.

## What this scan covers — and what it does not

Upstream rage-cli's `catalog build` indexes supported drawables, fragments, drawable dictionaries, dictionary entries, texture dictionaries and textures in game load order. It can inspect nested RPF archives and read retail archive keys using the user's own GTA5.exe.

The current Hub integration does **not** yet turn the drawable/texture catalogue or the bounded set of metadata JSON dumps into a complete ecological inventory. Candidate discovery is limited to the listed metadata extensions and a 10,000-hit cap per extension; the payload dumper processes at most 50 selected resources per batch. Repeated batches cover the candidate array captured in one discovery report, but a capped extension still requires a separate scan strategy/run to enumerate beyond its cap. Resources outside those patterns, beyond hit caps, unsupported by rage-cli or not successfully dumped remain unexamined. The pipeline does not yet enumerate and semantically verify every terrestrial animal, fish, marine animal, terrestrial plant, tree, aquatic plant, spawn/population record, habitat, animation behaviour or water-simulation definition. The output does not establish that a package preserves those identities.

In particular, a successful asset catalogue build is not permission to install any animal, vegetation, water or population-data package. The ecological preflight remains fail-closed.

## Remaining extractor work

1. The build/executable/tool/archive/payload provenance chain is implemented for the current bounded workflow.
2. Replace heuristic field tagging with versioned, schema-aware parsers for population, model-set, species, vegetation and water metadata structures.
3. Expand discovery and extraction beyond current search/dump limits, including all supported nested RPFs, metadata resource types and DLC/load-order overrides; report every skipped or unsupported archive.
4. Enumerate every vanilla land and aquatic species, plant identity, model identifier, behaviour reference, habitat association and population entry.
5. Build a separate exact-build baseline for water bodies, simulation/interaction definitions, weather response and visual water rendering parameters.
6. Bind normalized snapshots to verified source archives and an exact game build, then feed them to `EcologyInventoryDiffService`.
7. Add in-game regression captures and controlled package tests before enabling any ecology-sensitive package.

Until all of those steps exist, ecology-sensitive packages remain blocked.

## Trust and privacy boundaries

- The external executable must match the pinned upstream asset digest before execution.
- The Hub does not use shell command strings, does not launch arbitrary package scripts and does not install the external tool.
- The tool receives the selected local game path so it can read the files and derive retail archive keys from that installation.
- The Hub does not upload game files, derived keys, catalogue databases or scan reports.
- Only run the official release binary obtained from the linked upstream release page.

## Normalized ecology inventory comparison

The Windows Hub now accepts two normalized semantic inventory snapshots, validates both against the currently selected GTA5.exe build and SHA-256, runs the semantic preservation comparator, hashes the input files and stores a local report. See [Normalized ecology inventory snapshot schema](ECOLOGY_INVENTORY_SNAPSHOT_SCHEMA.md) for the exact JSON contract and workflow.

This is a comparison and evidence-recording tool, not a new extractor. The current bounded rage-cli discovery/dump stage does not automatically produce complete semantic snapshots. Every report explicitly leaves `completenessVerified=false` and `packageEligibilityUnlocked=false`; a clean comparison of supplied rows does not unblock animals, plants, population or water packages.
