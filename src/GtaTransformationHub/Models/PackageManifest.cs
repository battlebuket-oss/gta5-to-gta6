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
    EcologyPreservationContract? Ecology = null,
    StoryContentDescriptor? StoryContent = null,
    int SignatureVersion = 1,
    UiPackageDescriptor? Ui = null);

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

/// <summary>
/// Story Mode content metadata. SaveCompatibility is one of:
/// SeparateNamespace, NoPersistentData, or UsesVanillaSave.
/// </summary>
public sealed record StoryContentDescriptor(
    string ContentType,
    IReadOnlyList<string> MissionIds,
    IReadOnlyList<string> ProtagonistRequirements,
    IReadOnlyList<string> StartLocations,
    string ProgressionNamespace,
    string SaveCompatibility,
    bool RequiresSaveBackup,
    bool PreservesOriginalCampaign,
    IReadOnlyList<string> VoiceLanguages,
    IReadOnlyList<string> SubtitleLanguages,
    string? TestEvidenceUri = null);

/// <summary>
/// Explicit contract for an in-game UI replacement. This describes a real game-side
/// implementation; it is not satisfied by a desktop companion window or mock-up.
/// Signature version 3 authenticates this metadata.
/// </summary>
public sealed record UiPackageDescriptor(
    IReadOnlyList<string> Surfaces,
    string ImplementationType,
    IReadOnlyList<string> InputMethods,
    IReadOnlyList<string> ResolutionTargets,
    IReadOnlyList<string> AccessibilityFeatures,
    bool NativeActionsPreserved,
    bool MissionTransitionsPreserved,
    bool CutscenesPreserved,
    bool SubtitlesPreserved,
    bool OriginalCampaignPreserved,
    string CompatibilityEvidenceUri,
    string RollbackEvidenceUri,
    string VerifiedGameBuild,
    string VerifiedGta5ExeSha256);

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
