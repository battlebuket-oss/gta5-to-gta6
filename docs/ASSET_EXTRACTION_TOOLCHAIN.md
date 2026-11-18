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

## What this scan covers — and what it does not

Upstream rage-cli's `catalog build` indexes supported drawables, fragments, drawable dictionaries, dictionary entries, texture dictionaries and textures in game load order. It can inspect nested RPF archives and read retail archive keys using the user's own GTA5.exe.

The current Hub integration does **not** yet turn that catalogue into a complete ecological inventory. It does not yet enumerate and semantically verify every terrestrial animal, fish, marine animal, terrestrial plant, tree, aquatic plant, spawn/population record, habitat, animation behaviour or water-simulation definition. The current catalogue also does not establish that a package preserves those identities.

In particular, a successful asset catalogue build is not permission to install any animal, vegetation, water or population-data package. The ecological preflight remains fail-closed.

## Remaining extractor work

1. Add a provenance record for the selected GTA V build, GTA5.exe digest, all source archive hashes and the exact parser version.
2. Export and parse the relevant population and model-set metadata, including the supported `.ymt`, `.meta`, `.ytyp` and `.ymap` resources.
3. Enumerate every vanilla land and aquatic species, its model identifiers, behaviour references, habitat associations and population entries.
4. Build a separate baseline for water bodies, simulation/interaction definitions and visual water rendering parameters.
5. Bind generated snapshots to verified source archives and an exact game build, then feed them to `EcologyInventoryDiffService`.
6. Add in-game regression captures and controlled package tests before enabling any ecology-sensitive package.

Until all of those steps exist, ecology-sensitive packages remain blocked.

## Trust and privacy boundaries

- The external executable must match the pinned upstream asset digest before execution.
- The Hub does not use shell command strings, does not launch arbitrary package scripts and does not install the external tool.
- The tool receives the selected local game path so it can read the files and derive retail archive keys from that installation.
- The Hub does not upload game files, derived keys, catalogue databases or scan reports.
- Only run the official release binary obtained from the linked upstream release page.
