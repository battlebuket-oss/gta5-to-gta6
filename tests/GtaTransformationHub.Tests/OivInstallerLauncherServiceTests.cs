using System.IO.Compression;
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
    public void CreateStartInfo_PreloadsTheRetainedPackageAsAPathSafeArgument()
    {
        var installer = Path.Combine(_root, "OIV Engine", "CodeWalker.OIVInstaller.exe");
        var package = Path.Combine(_root, "My Mods", "GTA VI HUD.oiv");
        Directory.CreateDirectory(Path.GetDirectoryName(installer)!);
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        File.WriteAllText(installer, "placeholder executable");
        File.WriteAllText(package, "placeholder OIV");

        var info = OivInstallerLauncherService.CreateStartInfo(installer, package);

        Assert.Equal(installer, info.FileName);
        Assert.Equal(Path.GetDirectoryName(installer), info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
        Assert.Single(info.ArgumentList);
        Assert.Equal(Path.GetFullPath(package), info.ArgumentList[0]);
    }

    [Fact]
    public void CreateSetGameStartInfo_PassesGameFolderAsOneSafeArgument()
    {
        var installer = Path.Combine(_root, "OIV Engine", "CodeWalker.OIVInstaller.exe");
        var gameFolder = Path.Combine(_root, "Game Folder", "GTA V Legacy");
        Directory.CreateDirectory(Path.GetDirectoryName(installer)!);
        Directory.CreateDirectory(gameFolder);

        var info = OivInstallerLauncherService.CreateSetGameStartInfo(installer, gameFolder);

        Assert.Equal(installer, info.FileName);
        Assert.Equal("--set-game", info.ArgumentList[0]);
        Assert.Equal(Path.GetFullPath(gameFolder), info.ArgumentList[1]);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
    }

    [Fact]
    public void CreateGetGameStartInfo_UsesTheInstallerReadBackCommand()
    {
        var installer = Path.Combine(_root, "OIV Engine", "CodeWalker.OIVInstaller.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(installer)!);

        var info = OivInstallerLauncherService.CreateGetGameStartInfo(installer);

        Assert.Equal(installer, info.FileName);
        Assert.Single(info.ArgumentList);
        Assert.Equal("--get-game", info.ArgumentList[0]);
    }

    [Theory]
    [InlineData("Default game folder: C:\\Games\\GTA V\\", "C:\\Games\\GTA V", true)]
    [InlineData("Default game folder: C:\\Games\\Other", "C:\\Games\\GTA V", false)]
    [InlineData("No default game folder set.", "C:\\Games\\GTA V", false)]
    [InlineData("Default game folder: /tmp/gta", "C:\\Games\\GTA V", false)]
    public void OutputConfirmsDefaultGameFolder_RequiresExactReadBack(
        string output,
        string expectedGameFolder,
        bool expected)
    {
        Assert.Equal(
            expected,
            OivInstallerLauncherService.OutputConfirmsDefaultGameFolder(output, expectedGameFolder));
    }

    [Fact]
    public void InspectRetainedPackage_RechecksTheOriginalArchiveDigestAndManifest()
    {
        var package = CreateValidOivPackage();
        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package)));

        var inspection = OivInstallerLauncherService.InspectRetainedPackage(package, expectedHash);

        Assert.True(inspection.IsGtaVTarget);
        Assert.Equal("Test HUD", inspection.Name);
        Assert.Equal("1.0", inspection.ModVersion);
        Assert.Equal("Unspecified", inspection.GameVersionRequirement);
        Assert.True(inspection.RequiresManualReview);
    }

    [Fact]
    public void InspectRetainedPackage_RejectsArchiveChangedAfterImport()
    {
        var package = CreateValidOivPackage();
        var originalHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package)));
        File.AppendAllText(package, "tampered");

        Assert.Throws<InvalidDataException>(() =>
            OivInstallerLauncherService.InspectRetainedPackage(package, originalHash));
    }

    [Fact]
    public void InspectRetainedPackage_RejectsInvalidExpectedDigest()
    {
        var package = CreateValidOivPackage();

        Assert.Throws<InvalidDataException>(() =>
            OivInstallerLauncherService.InspectRetainedPackage(package, "not-a-sha256"));
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

    private string CreateValidOivPackage()
    {
        var package = Path.Combine(_root, "retained-valid.oiv");
        using var file = File.Create(package);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        archive.CreateEntry("content/");
        using var manifestStream = archive.CreateEntry("assembly.xml").Open();
        using var writer = new StreamWriter(manifestStream);
        writer.Write("""
            <?xml version="1.0" encoding="utf-8"?>
            <package version="2.2" id="{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}" target="Five">
              <metadata>
                <name>Test HUD</name>
                <version><major>1</major><minor>0</minor></version>
                <author><displayName>Test author</displayName></author>
                <description><![CDATA[Test fixture.]]></description>
                <licence>Personal use</licence>
              </metadata>
              <colors>
                <headerBackground useBlackTextColor="False">$FF000000</headerBackground>
                <iconBackground>$FFFFFFFF</iconBackground>
              </colors>
              <content></content>
            </package>
            """);
        return package;
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
