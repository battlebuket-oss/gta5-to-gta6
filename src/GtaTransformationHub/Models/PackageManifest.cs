namespace GtaTransformationHub.Models;

/// <summary>
/// A user-imported, declarative package manifest. It describes data files only;
/// package installers and executable hooks are never executed by the Hub.
/// </summary>
public sealed record PackageManifest(
    int SchemaVersion,
    string Id,
    string Name,
    string Author,
    string SourceUri,
    string Licence,
    string Version,
    string GameTarget,
    string MinimumGameBuild,
    string MaximumGameBuild,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<PackageManifestFile> Files,
    string? ExclusiveGroup = null,
    string? SigningKeyId = null,
    string? Signature = null,
    IReadOnlyList<string>? ImpactAreas = null,
    bool CompatibilityVerified = false,
    string? PackageFormat = "HubZip",
    EcologyPreservationContract? Ecology = null);

public sealed record EcologyPreservationContract(
    string ReviewStatus,
    string ImpactType,
    bool PreservesVanillaAnimalSpecies,
    bool PreservesVanillaPlantSpecies,
    bool PreservesVanillaVegetationSpecies,
    bool PreservesVanillaModelIdentifiers,
    bool AllowsNewSpecies,
    bool AllowsSpeciesReplacement,
    bool AllowsSpeciesRemoval,
    bool ChangesSpeciesSpawnTables,
    string VanillaIdentityInventorySha256,
    string EvidenceUri);

public sealed record PackageManifestFile(
    string SourcePath,
    string DestinationPath,
    string Sha256,
    long SizeBytes);

public sealed record ImportedPackage(
    PackageManifest Manifest,
    string PackageRoot,
    string ArchiveSha256,
    DateTimeOffset ImportedAt,
    string SignatureStatus = "Not verified");
