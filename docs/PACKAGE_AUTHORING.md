# GTA Transformation Hub package format

The Hub currently imports local ZIP files. It does not automatically download archives from mod-hosting pages, authenticate the author, run an installer, or sandbox files after GTA V starts.

## ZIP layout

Every archive must contain a root-level gta-hub-manifest.json file. Every other file in the ZIP must be listed exactly once in the manifest. Example:

~~~text
sample-package.zip
├── gta-hub-manifest.json
└── package/
    └── mods/
        └── sample.dat
~~~

## Manifest schema version 1

~~~json
{
  "schemaVersion": 1,
  "id": "sample-package",
  "name": "Sample Package",
  "author": "Package Author",
  "sourceUri": "https://author.example.com/sample-package",
  "licence": "Describe the applicable author licence and redistribution permission",
  "version": "1.0.0",
  "gameTarget": "GTA V Legacy",
  "minimumGameBuild": "1.0.1000.0",
  "maximumGameBuild": "1.0.9999.0",
  "dependencies": [],
  "conflicts": [],
  "exclusiveGroup": "optional-mutually-exclusive-group",
  "files": [
    {
      "sourcePath": "package/mods/sample.dat",
      "destinationPath": "mods/sample.dat",
      "sha256": "REPLACE_WITH_64_HEX_CHARACTERS",
      "sizeBytes": 123
    }
  ]
}
~~~

exclusiveGroup is optional. Use the same value for packages that must never be installed together. dependencies and conflicts contain package IDs, not display names. sizeBytes is the exact uncompressed file size. sha256 is the SHA-256 digest of the exact file bytes.

Example PowerShell commands to calculate a file size and digest:

~~~powershell
(Get-Item .\package\mods\sample.dat).Length
(Get-FileHash .\package\mods\sample.dat -Algorithm SHA256).Hash
~~~

Replace the example values with the real values before creating the ZIP.

## Import and installation constraints

- Only GTA V Legacy manifests are accepted.
- Package and build versions must be numeric.
- sourceUri must use HTTPS.
- The archive is limited to 2 GB compressed, 4 GB expanded, 10,000 entries and 512 MB per file.
- Absolute paths, traversal segments, Windows device names, symbolic links, reparse points, duplicate paths and undeclared archive files are rejected.
- The manifest itself is not deployed.
- Any destination ending in .exe is blocked. This does not make DLL, ASI or script files safe.
- The Hub copies only the declared files. It does not run .exe, .bat, PowerShell, OpenIV or other package installers.
- Each deployed file is backed up and written through a transaction journal. Removal is allowed only when a prior successful Hub deployment record exists and the on-disk hash still matches.

## Trust and security model

The SHA-256 values in this format are self-declared by the package. They detect accidental corruption between manifest creation and import, but they do not authenticate the author: an attacker who can replace both the manifest and payload can generate matching hashes.

The Hub currently has no trusted publisher-signing infrastructure or signed catalogue. Users must obtain archives from the author's genuine source and inspect the package contents and licence before acknowledging installation. Mod files may execute inside GTA V when the game loads them; the Hub does not sandbox that runtime code.

Do not use this format to distribute copyrighted assets unless the author licence expressly permits redistribution. Keep author/source attribution and permission terms accurate.
