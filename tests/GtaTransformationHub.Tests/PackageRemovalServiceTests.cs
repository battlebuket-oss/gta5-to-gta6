using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class PackageRemovalServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubRemovalTests", Guid.NewGuid().ToString("N"));
    private readonly string _packagesRoot;
    private readonly string _gameRoot;
    private readonly string _transactionsRoot;
    private readonly string _stagingRoot;

    public PackageRemovalServiceTests()
    {
        _packagesRoot = Path.Combine(_root, "packages");
        _gameRoot = Path.Combine(_root, "game");
        _transactionsRoot = Path.Combine(_root, "transactions");
        _stagingRoot = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_gameRoot);
        File.WriteAllText(Path.Combine(_gameRoot, "GTA5.exe"), "test executable marker");
    }

    [Fact]
    public async Task RemovePackage_RestoresOriginalOnRollback()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("removable package file");
        var imported = await ImportPackageAsync(content);
        var installation = CreateInstallation();
        var deployer = new TransactionalDeploymentService(dataRoot: _transactionsRoot);
        var coordinator = new PackageDeploymentCoordinator(deployer, _stagingRoot);
        await coordinator.ApplyAsync(installation, new[] { imported });

        var target = Path.Combine(_gameRoot, "mods", "sample.dat");
        Assert.True(File.Exists(target));

        var remover = new TransactionalRemovalService(
            new GameProcessMonitor(), _transactionsRoot);
        var removal = await remover.RemovePackagesAsync(installation, new[] { imported });

        Assert.Equal(1, removal.FilesApplied);
        Assert.False(File.Exists(target));

        await deployer.RollbackLatestAsync(removal.TransactionId);
        Assert.Equal("removable package file", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task RemovePackage_RejectsUserModifiedFile()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("original package file");
        var imported = await ImportPackageAsync(content);
        var installation = CreateInstallation();
        var deployer = new TransactionalDeploymentService(dataRoot: _transactionsRoot);
        var coordinator = new PackageDeploymentCoordinator(deployer, _stagingRoot);
        await coordinator.ApplyAsync(installation, new[] { imported });

        var target = Path.Combine(_gameRoot, "mods", "sample.dat");
        await File.WriteAllTextAsync(target, "user modification");
        var remover = new TransactionalRemovalService(
            new GameProcessMonitor(), _transactionsRoot);

        await Assert.ThrowsAsync<IOException>(() =>
            remover.RemovePackagesAsync(installation, new[] { imported }));
        Assert.Equal("user modification", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task RemovePackage_DoesNotDeleteUnmanagedFileEvenWhenHashMatches()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("same bytes as package");
        var imported = await ImportPackageAsync(content);
        var target = Path.Combine(_gameRoot, "mods", "sample.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, content);
        var remover = new TransactionalRemovalService(
            new GameProcessMonitor(), _transactionsRoot);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            remover.RemovePackagesAsync(CreateInstallation(), new[] { imported }));
        Assert.Equal(content, await File.ReadAllBytesAsync(target));
    }

    private async Task<ImportedPackage> ImportPackageAsync(byte[] content)
    {
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var manifest = new PackageManifest(
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
            new[] { new PackageManifestFile("mods/sample.dat", "mods/sample.dat", hash, content.LongLength) });

        var archivePath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using (var fileStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry(PackageArchiveService.ManifestFileName);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest));

            var payloadEntry = archive.CreateEntry("mods/sample.dat");
            using var payload = payloadEntry.Open();
            payload.Write(content);
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
