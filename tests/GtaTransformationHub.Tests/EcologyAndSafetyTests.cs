using System.IO.Compression;
using System.Text;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class EcologyAndSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubEcologyTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EcologyPackageWithoutPreservationContract_IsRejected()
    {
        var manifest = CreateManifest(
            new[] { new PackageManifestFile("payload/a.dat", "update/x64/dlcpacks/wildlife/wildlife.dat", new string('A', 64), 1) },
            impactAreas: new[] { "wildlife" });
        var errors = EcologyCompatibilityService.Validate(manifest);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, error => error.Contains("preservation declaration"));
    }

    [Fact]
    public void EcologyPackageThatChangesSpeciesIdentity_IsRejected()
    {
        var manifest = CreateManifest(
            new[] { new PackageManifestFile("payload/a.dat", "update/x64/dlcpacks/wildlife/wildlife.dat", new string('A', 64), 1) },
            impactAreas: new[] { "wildlife" },
            ecology: new EcologyPreservationContract("Approved", "VisualOnly", true, true, true, true,
                false, true, false, false, new string('A', 64), "https://example.com/ecology-review"));
        var errors = EcologyCompatibilityService.Validate(manifest);
        Assert.Contains(errors, error => error.Contains("replaces/removes vanilla species"));
        Assert.Contains(errors, error => error.Contains("semantic verification"));
    }

    [Fact]
    public void EcologyManifestClaimsAlone_CannotApproveWithoutSemanticVerifier()
    {
        var manifest = CreateManifest(
            new[] { new PackageManifestFile("payload/a.dat", "update/x64/dlcpacks/vegetation/trees.dat", new string('A', 64), 1) },
            impactAreas: new[] { "vegetation" },
            ecology: new EcologyPreservationContract("Approved", "VisualOnly", true, true, true, true,
                false, false, false, false, new string('A', 64), "https://example.com/ecology-review"));
        Assert.Contains(EcologyCompatibilityService.Validate(manifest), error => error.Contains("semantic verification"));
    }

    [Theory]
    [InlineData("GTA5.exe")]
    [InlineData("update.rpf")]
    [InlineData("update/update.rpf/meta/popgroups.ymt")]
    [InlineData("Profiles/SGTA50000")]
    [InlineData("mods/Launcher.exe")]
    public void ProtectedGamePaths_AreRejected(string destination) =>
        Assert.Throws<InvalidDataException>(() => ProtectedGamePathPolicy.ValidateDestination(destination));

    [Fact]
    public async Task OivImporter_ImportsDirectLooseFileAdds_AndMarksCompatibilityUnverified()
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, "safe.oiv");
        using (var file = new FileStream(archivePath, FileMode.CreateNew))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var assembly = zip.CreateEntry("assembly.xml");
            await using (var stream = assembly.Open())
            {
                var xml = """
                    <package version="2.2" target="Five" id="12345678-1234-1234-1234-123456789abc">
                      <metadata>
                        <name>Loose file visual mod</name>
                        <author><displayName>Test Author</displayName><web>https://example.com</web></author>
                        <version><major>1</major><minor>0</minor></version>
                        <licence>Test fixture only</licence>
                      </metadata>
                      <content><add source="sample.dat">mods/sample.dat</add></content>
                    </package>
                    """;
                await stream.WriteAsync(Encoding.UTF8.GetBytes(xml));
            }
            var payload = zip.CreateEntry("content/sample.dat");
            await using var payloadStream = payload.Open();
            await payloadStream.WriteAsync(Encoding.UTF8.GetBytes("visual data"));
        }

        var imported = await new OivPackageImportService(Path.Combine(_root, "packages")).ImportAsync(archivePath);
        Assert.Single(imported.Manifest.Files);
        Assert.Equal("mods/sample.dat", imported.Manifest.Files[0].DestinationPath);
        Assert.Equal("OIV-2.2", imported.Manifest.PackageFormat);
        Assert.False(imported.Manifest.CompatibilityVerified);
    }

    [Fact]
    public async Task OivImporter_RejectsArchivePatchOperationsWithoutPartialImport()
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, "unsafe.oiv");
        using (var file = new FileStream(archivePath, FileMode.CreateNew))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var assembly = zip.CreateEntry("assembly.xml");
            await using var stream = assembly.Open();
            var xml = """
                <package version="2.2" target="Five" id="12345678-1234-1234-1234-123456789abc">
                  <metadata>
                    <name>Unsupported archive patch</name>
                    <author><displayName>Test Author</displayName></author>
                    <version><major>1</major><minor>0</minor></version>
                    <licence>Test fixture only</licence>
                  </metadata>
                  <content><archive path="update.rpf"><add source="a.dat">x.dat</add></archive></content>
                </package>
                """;
            await stream.WriteAsync(Encoding.UTF8.GetBytes(xml));
        }
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new OivPackageImportService(Path.Combine(_root, "packages")).ImportAsync(archivePath));
        Assert.False(Directory.Exists(Path.Combine(_root, "packages")));
    }

    private static PackageManifest CreateManifest(
        IReadOnlyList<PackageManifestFile> files,
        IReadOnlyList<string>? impactAreas = null,
        EcologyPreservationContract? ecology = null) =>
        new(1, "ecology-test", "Ecology Test", "Test Author", "https://example.com/package",
            "Test-only fixture licence", "1.0.0", "GTA V Legacy", "1.0.1000.0", "1.0.9999.0",
            Array.Empty<string>(), Array.Empty<string>(), files,
            ImpactAreas: impactAreas, Ecology: ecology);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
