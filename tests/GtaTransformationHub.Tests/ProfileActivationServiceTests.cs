using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class ProfileActivationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubProfileTests", Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;
    private readonly string _packagesRoot;
    private readonly string _transactionsRoot;
    private readonly string _stagingRoot;

    public ProfileActivationServiceTests()
    {
        _gameRoot = Path.Combine(_root, "game");
        _packagesRoot = Path.Combine(_root, "packages");
        _transactionsRoot = Path.Combine(_root, "transactions");
        _stagingRoot = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_gameRoot);
        File.WriteAllText(Path.Combine(_gameRoot, "GTA5.exe"), "test executable marker");
    }

    [Fact]
    public async Task ActivateProfile_RemovesStalePackageAndInstallsDesiredPackage()
    {
        var packageA = await ImportPackageAsync("package-a", "Package A", "mods/a.dat", "alpha");
        var packageB = await ImportPackageAsync("package-b", "Package B", "mods/b.dat", "bravo");
        var library = new[] { packageA, packageB };
        var (activation, _) = CreateServices();

        await activation.ActivateAsync(CreateInstallation(), library, new[] { packageA });
        Assert.True(File.Exists(Path.Combine(_gameRoot, "mods", "a.dat")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, "mods", "b.dat")));

        var result = await activation.ActivateAsync(CreateInstallation(), library, new[] { packageB });

        Assert.Equal(1, result.FilesRemoved);
        Assert.Equal(1, result.FilesApplied);
        Assert.Equal(2, result.TransactionIds.Count);
        Assert.False(File.Exists(Path.Combine(_gameRoot, "mods", "a.dat")));
        Assert.Equal("bravo", await File.ReadAllTextAsync(Path.Combine(_gameRoot, "mods", "b.dat")));
    }

    [Fact]
    public async Task ActivateEmptyProfile_RemovesAllActiveManagedPackages()
    {
        var package = await ImportPackageAsync("package-a", "Package A", "mods/a.dat", "alpha");
        var (activation, _) = CreateServices();

        await activation.ActivateAsync(CreateInstallation(), new[] { package }, new[] { package });
        var result = await activation.ActivateAsync(
            CreateInstallation(), new[] { package }, Array.Empty<ImportedPackage>());

        Assert.Equal(1, result.FilesRemoved);
        Assert.False(File.Exists(Path.Combine(_gameRoot, "mods", "a.dat")));
    }

    [Fact]
    public async Task ActivateProfile_PreflightFailureDoesNotRemoveCurrentProfile()
    {
        var packageA = await ImportPackageAsync("package-a", "Package A", "mods/a.dat", "alpha");
        var incompatible = await ImportPackageAsync(
            "package-b", "Package B", "mods/b.dat", "bravo", minimumBuild: "1.0.5000.0");
        var library = new[] { packageA, incompatible };
        var (activation, _) = CreateServices();

        await activation.ActivateAsync(CreateInstallation(), library, new[] { packageA });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            activation.ActivateAsync(CreateInstallation(), library, new[] { incompatible }));

        Assert.Equal("alpha", await File.ReadAllTextAsync(Path.Combine(_gameRoot, "mods", "a.dat")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, "mods", "b.dat")));
    }

    [Fact]
    public async Task ActivateProfile_RequiresArchiveForEveryActivePackage()
    {
        var package = await ImportPackageAsync("package-a", "Package A", "mods/a.dat", "alpha");
        var (activation, _) = CreateServices();

        await activation.ActivateAsync(CreateInstallation(), new[] { package }, new[] { package });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            activation.ActivateAsync(
                CreateInstallation(),
                Array.Empty<ImportedPackage>(),
                Array.Empty<ImportedPackage>()));

        Assert.Equal("alpha", await File.ReadAllTextAsync(Path.Combine(_gameRoot, "mods", "a.dat")));
    }

    [Fact]
    public async Task ActivateSameIntactProfile_IsIdempotent()
    {
        var package = await ImportPackageAsync("package-a", "Package A", "mods/a.dat", "alpha");
        var (activation, _) = CreateServices();

        await activation.ActivateAsync(CreateInstallation(), new[] { package }, new[] { package });
        var result = await activation.ActivateAsync(CreateInstallation(), new[] { package }, new[] { package });

        Assert.Equal(0, result.FilesRemoved);
        Assert.Equal(0, result.FilesApplied);
        Assert.Equal(1, result.PackagesAlreadyActive);
        Assert.Empty(result.TransactionIds);
    }

    private (ProfileActivationService Activation, TransactionalDeploymentService Deployer) CreateServices()
    {
        var processMonitor = new GameProcessMonitor();
        var deployer = new TransactionalDeploymentService(processMonitor, _transactionsRoot);
        var remover = new TransactionalRemovalService(processMonitor, _transactionsRoot);
        var coordinator = new PackageDeploymentCoordinator(deployer, _stagingRoot);
        return (new ProfileActivationService(coordinator, remover, deployer), deployer);
    }

    private async Task<ImportedPackage> ImportPackageAsync(
        string id,
        string name,
        string destination,
        string contents,
        string minimumBuild = "1.0.1000.0")
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var source = "payload/" + Path.GetFileName(destination);
        var manifest = new PackageManifest(
            1,
            id,
            name,
            "Test Author",
            "https://example.com/" + id,
            "Test-only fixture licence",
            "1.0.0",
            "GTA V Legacy",
            minimumBuild,
            "1.0.9999.0",
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { new PackageManifestFile(source, destination, hash, bytes.LongLength) });

        var archivePath = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var fileStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry(PackageArchiveService.ManifestFileName);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest));

            var payloadEntry = archive.CreateEntry(source);
            using var payload = payloadEntry.Open();
            payload.Write(bytes);
        }

        return await new PackageArchiveService(_packagesRoot).ImportAsync(archivePath);
    }

    private GameInstallation CreateInstallation() =>
        new(
            _gameRoot,
            Path.Combine(_gameRoot, "GTA5.exe"),
            "1.0.3889.0",
            "1.0.3889.0",
            "1.0.3889.0");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
