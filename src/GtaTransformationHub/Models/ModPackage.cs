namespace GtaTransformationHub.Models;

public enum CatalogueStatus
{
    Planned,
    Researching,
    Verified,
    Tested,
    Deprecated,
    Blocked
}

public sealed record ModPackage(
    string Id,
    string Name,
    string Category,
    string Author,
    Uri SourceUri,
    string Licence,
    string Version,
    string MinimumGameBuild,
    string MaximumGameBuild,
    string? Sha256,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Conflicts,
    CatalogueStatus Status,
    string InstallAdapter,
    string RestoreStrategy,
    string PerformanceTier,
    string Notes,
    IReadOnlyList<string>? ExternalRequirements = null,
    string? ExclusiveGroup = null);
