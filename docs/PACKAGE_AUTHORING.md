# GTA Transformation Hub package format

The Hub currently imports local ZIP files. It does not automatically download archives from mod-hosting pages, run third-party installers, or sandbox mod code after GTA V starts.

## ZIP layout

Every archive must contain a root-level gta-hub-manifest.json file. Every other file in the ZIP must be listed exactly once in the manifest.

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
  "signingKeyId": null,
  "signature": null,
  "signatureVersion": 2,
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

exclusiveGroup is optional. Packages that must never be installed together should share the same value. dependencies and conflicts contain package IDs, not display names. sizeBytes is the exact uncompressed file size. sha256 is the SHA-256 digest of the exact file bytes.

Example PowerShell commands to calculate a file size and digest:

~~~powershell
(Get-Item .\package\mods\sample.dat).Length
(Get-FileHash .\package\mods\sample.dat -Algorithm SHA256).Hash
~~~

Replace the example values with the real values before creating the ZIP.

## Optional publisher signatures

signingKeyId and signature are optional but must either both be present or both be absent. The supported signature is RSA with at least a 2048-bit key, SHA-256 and PKCS#1 v1.5 padding.

signingKeyId is the uppercase hexadecimal SHA-256 fingerprint of the RSA SubjectPublicKeyInfo public key. signature is the Base64-encoded RSA signature over the canonical manifest payload.

The payload is produced by PackageSignatureService.CreateSigningPayload(manifest). It includes the manifest identity, author/source/licence, version and supported game-build range, sorted dependencies/conflicts, exclusive group, signing-key fingerprint, and files sorted by source and destination path. Each file's destination, source, uppercase SHA-256 digest and exact size are signed. The signature property itself is excluded.

Illustrative publisher-side C# signing steps:

~~~csharp
// Keep this private key offline. Never place it in a mod ZIP.
using var rsa = RSA.Create();
rsa.ImportFromPem(File.ReadAllText("publisher-private-key.pem"));

var unsigned = manifest with
{
    SigningKeyId = PackageSignatureService.ComputePublisherFingerprint(
        File.ReadAllText("publisher-public-key.pem")),
    Signature = null
};

var payload = PackageSignatureService.CreateSigningPayload(unsigned);
var signature = rsa.SignData(
    payload,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);

var signedManifest = unsigned with
{
    Signature = Convert.ToBase64String(signature)
};
~~~

The public key and its fingerprint must be distributed to users through a separate, authentic publisher channel. A publisher must never ask users to trust a key based only on a fingerprint embedded in the same ZIP.

In the Hub, use the Trust publisher key control and enter the publisher name, public-key PEM, and fingerprint verified independently from the official source. The Hub recalculates the fingerprint and rejects a mismatch. Trusted keys are stored locally. A valid signature from a trusted key is labelled TrustedPublisher; a signature with an unknown key is labelled UnknownPublisher; an unsigned package is labelled Unsigned; a bad signature from a trusted key is rejected.

The signature authenticates the manifest and declared file hashes. The importer separately hashes the actual extracted file bytes and checks their sizes. A valid signature does not prove that the publisher's code is safe, does not sandbox ASI/DLL/script code, and does not replace licence review. Unsigned packages remain importable but produce a preflight warning. The app currently supports adding trusted publisher keys; a dedicated key-revocation management screen remains outstanding.

## Import and installation constraints

- Only GTA V Legacy manifests are accepted.
- Package and game-build versions must be numeric.
- sourceUri must use HTTPS.
- The archive is limited to 2 GB compressed, 4 GB expanded, 10,000 entries and 512 MB per file.
- Manifest and payload extraction are bounded by declared expanded sizes.
- Absolute paths, traversal segments, Windows device names, symbolic links, reparse points, duplicate paths and undeclared archive files are rejected.
- The manifest itself is not deployed.
- Any destination ending in .exe is blocked. This does not make DLL, ASI or script files safe.
- The Hub copies only the declared files. It does not run .exe, .bat, PowerShell, OpenIV or other package installers.
- Each deployed file is backed up and written through a transaction journal. Removal is allowed only when a prior successful Hub deployment record exists and the on-disk hash still matches.
- Profiles pin both package ID and exact version. Profile activation removes stale Hub-managed packages, preserves intact selected packages, and installs missing desired packages.

## Trust and security model

The Hub's SHA-256 checks detect corruption but do not authenticate an author on their own. Publisher signatures can authenticate a package manifest only when the publisher public key was independently verified and added to the local trust store.

The Hub does not sandbox third-party code loaded by GTA V. Users must obtain archives from genuine author sources and inspect package contents and licence terms. Do not use this format to distribute copyrighted assets unless the author licence expressly permits redistribution.


## Signature versions

- **Version 1** preserves the original canonical payload for legacy manifests that contain only the original package identity, dependencies, conflicts, exclusive group, publisher key fingerprint, and file manifest.
- **Version 2** additionally signs impact areas, compatibility-verification state, package format, ecological preservation metadata, and the complete Story Mode descriptor. New publishers should use version 2.
- A version-1 signature is rejected when version-2-only metadata is present. This prevents a signed legacy manifest from having compatibility, ecology, or Story Mode claims altered without invalidating its signature.
- The signature version is declared as `signatureVersion` in `gta-hub-manifest.json`. It defaults to 1 when omitted for backward compatibility. Use `2` for new signed packages.

## Story Mode package metadata

Story packages must include `impactAreas: ["story"]` and a `storyContent` object. The descriptor contains:

```json
{
  "signatureVersion": 2,
  "impactAreas": ["story"],
  "storyContent": {
    "contentType": "MissionPack",
    "missionIds": ["mission-01", "mission-02"],
    "protagonistRequirements": ["Any"],
    "startLocations": ["downtown-bank"],
    "progressionNamespace": "community-story-progression",
    "saveCompatibility": "SeparateNamespace",
    "requiresSaveBackup": true,
    "preservesOriginalCampaign": true,
    "voiceLanguages": [],
    "subtitleLanguages": ["en-US"],
    "testEvidenceUri": "https://author.example.com/test-evidence"
  }
}
```

Supported `contentType` values are `Campaign`, `MissionPack`, `SideMission`, `CharacterArc`, `ActivityPack`, and `AmbientEvent`. Supported `saveCompatibility` values are `SeparateNamespace`, `NoPersistentData`, and `UsesVanillaSave`. A package that uses the vanilla save must set `requiresSaveBackup` to true. Every story package must declare that it preserves the original GTA V campaign.

These fields are a metadata and safety contract. They do not create missions, guarantee a save-isolated runtime implementation, or prove gameplay compatibility. A story package remains subject to source, licence, exact-build, dependency, hash, install/rollback and real-game validation.
