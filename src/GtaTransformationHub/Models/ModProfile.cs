namespace GtaTransformationHub.Models;

public sealed record ModProfile(
    string Id,
    string Name,
    IReadOnlySet<string> EnabledPackageIds,
    DateTimeOffset UpdatedAt);
