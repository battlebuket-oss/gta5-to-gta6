using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class PackageDeploymentCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubDeployTests", Guid.NewGuid().ToString("N"));
    private readonly string _packagesRoot;
    private readonly string _gameRoot;
    private readonly string _transactionRoot;
    private readonly string _stagingRoot;

    public PackageDeploymentCoordinatorTests()
    {
        _packagesRoot = Path.Combine(_root, "packages");
        _gameRoot = Path.Combine(_root, "game");
        _transactionRoot = Path.Combine(_root, "transactions");
        _stagingRoot = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_gameRoot);
        File.WriteAllText(Path.Combine(_gameRoot, "GTA5.exe"), "test executable marker");
    }

    [Fact]
    public async Task ValidateAndApply_DeploysOnlyManifestDeclaredFile()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("safe test mod data");
        var manifest = CreateManifest(content);
        var archivePath = CreateArchive(manifest, ("mods/sample.dat", content));
        var imported = await new PackageArchiveService(_packagesRoot).ImportAsync(archivePath);
        var installation = new GameInstallation(
            _gameRoot, Path.Combine(_gameRoot, "GTA5.exe"), "1.0.3889.0", "1.0.3889.0", "1.0.3889.0");
        var deployer = new TransactionalDeploymentService(dataRoot: _transactionRoot);
        var coordinator = new PackageDeploymentCoordinator(deployer, _stagingRoot);

        var report = await coordinator.ValidateAsync(installation, new[] { imported });
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(1, report.FilesToDeploy);

        var result = await coordinator.ApplyAsync(installation, new[] { imported });

        Assert.Equal(1, result.FilesApplied);
        Assert.Equal(
            "safe test mod data",
            await File.ReadAllTextAsync(Path.Combine(_gameRoot, "mods", "sample.dat")));
    }

    [Fact]
    public async Task Validate_RejectsBuildMismatch()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("safe test mod data");
        var imported = await new PackageArchiveService(_packagesRoot).ImportAsync(
            CreateArchive(CreateManifest(content), ("mods/sample.dat", content)));
        var installation = new GameInstallation(
            _gameRoot, Path.Combine(_gameRoot, "GTA5.exe"), "1.0.1.0", "1.0.1.0", "1.0.1.0");
        var coordinator = new PackageDeploymentCoordinator(
            new TransactionalDeploymentService(dataRoot: _transactionRoot), _stagingRoot);

        var report = await coordinator.ValidateAsync(installation, new[] { imported });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("not compatible"));
    }

    [Fact]
    public async Task Validate_RejectsOivEvenWhenManifestClaimsCompatibilityVerified()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("oiv imported loose file");
        var manifest = CreateManifest(content) with
        {
            PackageFormat = "OIV-2.2",
            CompatibilityVerified = true
        };
        var archivePath = CreateArchive(manifest, ("mods/sample.dat", content));
        var imported = await new PackageArchiveService(_packagesRoot).ImportAsync(archivePath);
        var installation = new GameInstallation(
            _gameRoot, Path.Combine(_gameRoot, "GTA5.exe"), "1.0.3889.0", "1.0.3889.0", "1.0.3889.0");
        var coordinator = new PackageDeploymentCoordinator(
            new TransactionalDeploymentService(dataRoot: _transactionRoot), _stagingRoot);

        var report = await coordinator.ValidateAsync(installation, new[] { imported });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("OIV deployment remains disabled"));
    }

    [Fact]
    public async Task Validate_RequiresExactGta5ExecutableFingerprintForUiPackage()
    {
        var contentBytes = System.Text.Encoding.UTF8.GetBytes("ui package data");
        using var rsa = RSA.Create(2048);
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(rsa.ExportSubjectPublicKeyInfoPem());
        var trustedStore = new TrustedPublisherStore(Path.Combine(_root, "trusted-publishers.json"));
        await trustedStore.AddAsync("UI Test Publisher", rsa.ExportSubjectPublicKeyInfoPem(), fingerprint);
        var signatureService = new PackageSignatureService(trustedStore);
        var actualHash = await GameExecutableFingerprintService.ComputeSha256Async(
            Path.Combine(_gameRoot, "GTA5.exe"));

        var importedWrongHash = await CreateSignedUiPackageAsync(
            contentBytes, rsa, fingerprint, "1.0.3889.0", new string('B', 64));
        var installation = new GameInstallation(
            _gameRoot, Path.Combine(_gameRoot, "GTA5.exe"), "1.0.3889.0", "1.0.3889.0", "1.0.3889.0");
        var coordinator = new PackageDeploymentCoordinator(
            new TransactionalDeploymentService(dataRoot: _transactionRoot),
            _stagingRoot,
            signatureService);

        var report = await coordinator.ValidateAsync(installation, new[] { importedWrongHash });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("different exact GTA5.exe build or SHA-256 fingerprint", StringComparison.Ordinal));
        Assert.NotEqual(new string('B', 64), actualHash);
    }

    [Fact]
    public async Task Validate_RequiresExactGta5BuildForUiPackage()
    {
        var contentBytes = System.Text.Encoding.UTF8.GetBytes("ui package data");
        using var rsa = RSA.Create(2048);
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(rsa.ExportSubjectPublicKeyInfoPem());
        var trustedStore = new TrustedPublisherStore(Path.Combine(_root, "trusted-publishers.json"));
        await trustedStore.AddAsync("UI Test Publisher", rsa.ExportSubjectPublicKeyInfoPem(), fingerprint);
        var signatureService = new PackageSignatureService(trustedStore);
        var actualHash = await GameExecutableFingerprintService.ComputeSha256Async(
            Path.Combine(_gameRoot, "GTA5.exe"));

        var importedWrongBuild = await CreateSignedUiPackageAsync(
            contentBytes, rsa, fingerprint, "1.0.3888.0", actualHash);
        var installation = new GameInstallation(
            _gameRoot, Path.Combine(_gameRoot, "GTA5.exe"), "1.0.3889.0", "1.0.3889.0", "1.0.3889.0");
        var coordinator = new PackageDeploymentCoordinator(
            new TransactionalDeploymentService(dataRoot: _transactionRoot),
            _stagingRoot,
            signatureService);

        var report = await coordinator.ValidateAsync(installation, new[] { importedWrongBuild });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("different exact GTA5.exe build or SHA-256 fingerprint", StringComparison.Ordinal));
    }

    private async Task<ImportedPackage> CreateSignedUiPackageAsync(
        byte[] contentBytes,
        RSA privateKey,
        string publisherFingerprint,
        string verifiedGameBuild,
        string verifiedExecutableSha256)
    {
        var manifest = CreateManifest(contentBytes) with
        {
            ImpactAreas = new[] { "interface" },
            CompatibilityVerified = true,
            SignatureVersion = 3,
            Ui = new UiPackageDescriptor(
                new[] { "hud" },
                "GameScript",
                new[] { "KeyboardMouse", "Controller" },
                new[] { "1920x1080" },
                new[] { "KeyboardNavigation", "ControllerNavigation", "VisibleFocus" },
                true,
                true,
                true,
                true,
                true,
                "https://example.com/ui-compatibility",
                "https://example.com/ui-rollback",
                verifiedGameBuild,
                verifiedExecutableSha256)
        };
        var toSign = manifest with { SigningKeyId = publisherFingerprint, Signature = null };
        var signature = privateKey.SignData(
            PackageSignatureService.CreateSigningPayload(toSign),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var signed = toSign with { Signature = Convert.ToBase64String(signature) };
        var archivePath = CreateArchive(signed, ("mods/sample.dat", contentBytes));
        return await new PackageArchiveService(_packagesRoot).ImportAsync(archivePath);
    }

    private static PackageManifest CreateManifest(byte[] content) =>
        new(
            1,
            "test-package",
            "Test Package",
            "Test Author",
            "https://example.com/package",
            "Test-only fixture licence",
            "1.0.0",
            "GTA V Legacy",
            "1.0.1000.0",
            "1.0.9999.0",
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[]
            {
                new PackageManifestFile(
                    "mods/sample.dat",
                    "mods/sample.dat",
                    Convert.ToHexString(SHA256.HashData(content)),
                    content.LongLength)
            });

    private string CreateArchive(PackageManifest manifest, params (string Name, byte[] Content)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry(PackageArchiveService.ManifestFileName);
        using (var writer = new StreamWriter(manifestEntry.Open()))
            writer.Write(JsonSerializer.Serialize(manifest));

        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name);
            using var entryStream = entry.Open();
            entryStream.Write(item.Content);
        }

        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
