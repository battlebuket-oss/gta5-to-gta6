using System.IO;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class OivInstallerLauncherServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubOivLauncherTests", Guid.NewGuid().ToString("N"));

    public OivInstallerLauncherServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void InspectInstallerExecutable_RecordsHashButDoesNotClaimSignatureVerification()
    {
        var installer = Path.Combine(_root, "CodeWalker.OIVInstaller.exe");
        File.WriteAllBytes(installer, new byte[] { 1, 2, 3, 4 });

        var info = new OivInstallerLauncherService().InspectInstallerExecutable(installer);

        Assert.Equal(installer, info.ExecutablePath);
        Assert.Equal(64, info.Sha256.Length);
        Assert.Contains("not verified", info.VerificationNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InspectInstallerExecutable_RejectsAnUnrelatedExecutableName()
    {
        var installer = Path.Combine(_root, "OpenIV.exe");
        File.WriteAllText(installer, "not the supported installer");

        Assert.Throws<InvalidDataException>(() =>
            new OivInstallerLauncherService().InspectInstallerExecutable(installer));
    }

    [Fact]
    public void CreateStartInfo_PassesPackageAsAnArgumentWithoutShellStringConcatenation()
    {
        var installer = Path.Combine(_root, "OIV Engine", "CodeWalker.OIVInstaller.exe");
        var package = Path.Combine(_root, "My Mods", "GTA VI HUD.oiv");

        var info = OivInstallerLauncherService.CreateStartInfo(installer, package);

        Assert.Equal(installer, info.FileName);
        Assert.Equal(Path.GetDirectoryName(installer), info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
        Assert.Single(info.ArgumentList);
        Assert.Equal(package, info.ArgumentList[0]);
    }

    [Fact]
    public void ValidatePackageForLegacyTarget_AcceptsExplicitLegacyPackage()
    {
        var package = CreatePackage();
        var inspection = Inspection(gameVersion: "Legacy");

        OivInstallerLauncherService.ValidatePackageForLegacyTarget(package, ComputeSha256(package), inspection);

        Assert.True(File.Exists(package));
    }

    [Fact]
    public void ValidatePackageForLegacyTarget_RejectsArchiveModifiedAfterInspection()
    {
        var package = CreatePackage();
        var inspectedHash = ComputeSha256(package);
        File.WriteAllBytes(package, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x99 });

        Assert.Throws<InvalidDataException>(() =>
            OivInstallerLauncherService.ValidatePackageForLegacyTarget(
                package, inspectedHash, Inspection(gameVersion: "Legacy")));
    }

    [Fact]
    public void ValidatePackageForLegacyTarget_RejectsMalformedExpectedDigest()
    {
        var package = CreatePackage();

        Assert.Throws<InvalidDataException>(() =>
            OivInstallerLauncherService.ValidatePackageForLegacyTarget(
                package, "not-a-hash", Inspection(gameVersion: "Legacy")));
    }

    [Fact]
    public void ValidatePackageForLegacyTarget_BlocksEnhancedPackage()
    {
        var package = CreatePackage();

        Assert.Throws<InvalidDataException>(() =>
            OivInstallerLauncherService.ValidatePackageForLegacyTarget(
                package, ComputeSha256(package), Inspection(gameVersion: "Enhanced"), allowUnspecifiedLegacyTarget: true));
    }

    [Fact]
    public void ValidatePackageForLegacyTarget_RequiresConfirmationWhenVersionIsUnspecified()
    {
        var package = CreatePackage();
        var inspection = Inspection(gameVersion: "Unspecified");

        Assert.Throws<InvalidDataException>(() =>
            OivInstallerLauncherService.ValidatePackageForLegacyTarget(package, ComputeSha256(package), inspection));

        OivInstallerLauncherService.ValidatePackageForLegacyTarget(
            package, ComputeSha256(package), inspection, allowUnspecifiedLegacyTarget: true);
    }

    [Fact]
    public void ValidatePackageForLegacyTarget_BlocksUnsupportedCommandsEvenWhenUserConfirms()
    {
        var package = CreatePackage();
        var inspection = Inspection(
            gameVersion: "Unspecified",
            unsupportedCommands: ["unknown-command"]);

        Assert.Throws<InvalidDataException>(() =>
            OivInstallerLauncherService.ValidatePackageForLegacyTarget(
                package, ComputeSha256(package), inspection, allowUnspecifiedLegacyTarget: true));
    }

    private string CreatePackage()
    {
        var package = Path.Combine(_root, "test-mod.oiv");
        File.WriteAllBytes(package, new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        return package;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private static OivManifestInspectionResult Inspection(
        string gameVersion,
        IReadOnlyList<string>? unsupportedCommands = null,
        IReadOnlyList<string>? payloadSourceIssues = null) =>
        new(
            FormatVersion: "2.2",
            PackageId: "{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}",
            Target: "Five",
            Name: "Test package",
            Author: "Test author",
            ModVersion: "1.0",
            VersionTag: gameVersion,
            GameVersionRequirement: gameVersion,
            Licence: "Personal use",
            IsGtaVTarget: true,
            Operations: Array.Empty<OivOperationSummary>(),
            UnsupportedCommands: unsupportedCommands ?? Array.Empty<string>(),
            PayloadSourceIssues: payloadSourceIssues ?? Array.Empty<string>());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
