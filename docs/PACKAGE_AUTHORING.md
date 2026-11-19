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
- **Version 2** additionally signs impact areas, compatibility-verification state, package format, ecological preservation metadata, and the complete Story Mode descriptor.
- **Version 3** preserves the v2 fields and additionally signs the complete `ui` descriptor, including tested surfaces, implementation type, keyboard/controller support, resolution targets, accessibility declarations, native-action/mission/cutscene/subtitle/campaign preservation, HTTPS compatibility/rollback evidence references, the exact tested GTA V Legacy build, and the exact GTA5.exe SHA-256.
- A v1 signature is rejected when v2/v3 metadata is present. A v2 signature is rejected when UI metadata is present. This prevents an older signed manifest from gaining unauthenticated compatibility, ecology, Story Mode or UI claims.
- The signature version is declared as `signatureVersion` in `gta-hub-manifest.json`. It defaults to 1 when omitted for backward compatibility. Use `2` for new signed story packages and `3` for any in-game UI replacement package.


## In-game UI replacement package metadata

An in-game title, frontend, pause, map, settings, HUD, phone, radial, loading or accessibility replacement must declare `impactAreas: ["interface"]` and a `ui` descriptor. A companion desktop overlay or a mock-up is not an in-game replacement and is not an accepted implementation type.

Example descriptor (replace every illustrative value with the exact properties and evidence for the package):

```json
{
  "signatureVersion": 3,
  "impactAreas": ["interface"],
  "compatibilityVerified": true,
  "ui": {
    "surfaces": ["hud", "radial"],
    "implementationType": "GameScript",
    "inputMethods": ["KeyboardMouse", "Controller"],
    "resolutionTargets": ["1920x1080", "2560x1440"],
    "accessibilityFeatures": [
      "KeyboardNavigation",
      "ControllerNavigation",
      "VisibleFocus",
      "HighContrast",
      "SubtitleSafeArea"
    ],
    "nativeActionsPreserved": true,
    "missionTransitionsPreserved": true,
    "cutscenesPreserved": true,
    "subtitlesPreserved": true,
    "originalCampaignPreserved": true,
    "compatibilityEvidenceUri": "https://author.example.com/exact-build-ui-test",
    "rollbackEvidenceUri": "https://author.example.com/exact-build-ui-rollback",
    "verifiedGameBuild": "1.0.3889.0",
    "verifiedGta5ExeSha256": "REPLACE_WITH_64_HEX_CHARACTERS"
  }
}
```

The supported surface values are `title`, `frontend`, `pause`, `map`, `settings`, `hud`, `phone`, `radial`, `loading`, and `accessibility`. Implementation types are `Scaleform`, `GameScript`, and `AssetReplacement`. Input methods must include both `KeyboardMouse` and `Controller`. Resolution and accessibility capability values must match the allow-list enforced by the Hub.

The Hub validates all declarations at import and deployment preflight. It rejects missing evidence URLs, non-numeric game builds, malformed executable fingerprints, unpreserved native actions or mission transitions, and multiple selected packages that replace the same UI surface. The package must set `compatibilityVerified: true`, and its publisher signature must be version 3 from a locally trusted key.

The fingerprint is the SHA-256 of the exact `GTA5.exe` used for testing, not the ZIP hash or a version-range value. Compute it on the tested machine:

```powershell
(Get-FileHash -LiteralPath "C:\\Games\\Grand Theft Auto V\\GTA5.exe" -Algorithm SHA256).Hash.ToLowerInvariant()
(Get-Item -LiteralPath "C:\\Games\\Grand Theft Auto V\\GTA5.exe").VersionInfo.ProductVersion
```

Use the numeric Legacy build reported by the Hub as `verifiedGameBuild`. Do not claim `compatibilityVerified: true` until the package has completed the documented game-side checks. The Hub independently hashes the selected GTA5.exe at deployment preflight and requires both the build and digest to match these signed fields exactly.

To sign UI metadata, create the complete `PackageManifest` first, set `signatureVersion = 3`, include the `ui` descriptor, set `signingKeyId` to the public-key fingerprint and leave `signature` null. Then call `PackageSignatureService.CreateSigningPayload(manifest)`, sign the returned bytes with RSA-SHA256/PKCS#1 v1.5, and Base64-encode the result as `signature`. The private key must remain offline. Version 3 signs the exact build and executable digest along with all UI claims; changing any of those fields requires a new signature.

Passing these checks proves only that the signed declarations match the locally trusted publisher key and the selected executable fingerprint. It does not prove the linked evidence is truthful, or replace a real-game visual, input, mission, accessibility, performance and rollback test. No UI package in the current research catalogue is approved solely by supplying this descriptor.

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
