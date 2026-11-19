namespace GtaTransformationHub.Services;

public sealed record DeploymentFile(
    string PackageId,
    string SourcePath,
    string DestinationRelativePath,
    string ExpectedSha256,
    string PackageVersion = "");
