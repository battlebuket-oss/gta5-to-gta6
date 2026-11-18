namespace GtaTransformationHub.Services;

/// <summary>
/// A single file supplied by a locally installed, independently verified mod package.
/// Destination paths are always relative to the selected GTA V installation.
/// </summary>
public sealed record DeploymentFile(
    string PackageId,
    string SourcePath,
    string DestinationRelativePath,
    string ExpectedSha256);
