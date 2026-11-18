using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class PackageArchiveServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubPackageTests", Guid.NewGuid().ToString("N"));
    private readonly string _packagesRoot;

    public PackageArchiveServiceTests()
    {
        _packagesRoot = Path.Combine(_root, "packages");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ImportAsync_ExtractsOnlyManifestFilesAndVerifiesHash()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("verified package payload");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var manifest = CreateManifest(hash, bytes.LongLength, "assets/example.dat", "mods/example.dat");
        var archive = CreateArchive(manifest, ("assets/example.dat", bytes));

        var imported = await new PackageArchiveService(_packagesRoot).ImportAsync(archive);

        Assert.Equal("test-package", imported.Manifest.Id);
        Assert.Equal("verified package payload", await File.ReadAllTextAsync(
            Path.Combine(imported.PackageRoot, "assets", "example.dat")));
        Assert.False(File.Exists(Path.Combine(imported.PackageRoot, "mods", "example.dat")));
    }

    [Fact]
    public async Task ImportAsync_RejectsTamperedPayload()
    {
        var declaredBytes = System.Text.Encoding.UTF8.GetBytes("declared");
        var actualBytes = System.Text.Encoding.UTF8.GetBytes("tampered");
        var manifest = CreateManifest(
            Convert.ToHexString(SHA256.HashData(declaredBytes)),
            declaredBytes.LongLength,
            "payload.bin",
            "mods/payload.bin");
        var archive = CreateArchive(manifest, ("payload.bin", actualBytes));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PackageArchiveService(_packagesRoot).ImportAsync(archive));
    }

    [Fact]
    public async Task ImportAsync_RejectsPathTraversal()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("payload");
        var manifest = CreateManifest(
            Convert.ToHexString(SHA256.HashData(bytes)),
            bytes.LongLength,
            "../outside.dat",
            "mods/outside.dat");
        var archive = CreateArchive(manifest, ("../outside.dat", bytes));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PackageArchiveService(_packagesRoot).ImportAsync(archive));
        Assert.False(File.Exists(Path.Combine(_root, "outside.dat")));
    }

    [Fact]
    public async Task ImportAsync_RejectsUndeclaredFiles()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("payload");
        var manifest = CreateManifest(
            Convert.ToHexString(SHA256.HashData(bytes)),
            bytes.LongLength,
            "payload.bin",
            "mods/payload.bin");
        var archive = CreateArchive(
            manifest,
            ("payload.bin", bytes),
            ("unexpected.bin", bytes));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PackageArchiveService(_packagesRoot).ImportAsync(archive));
    }

    private PackageManifest CreateManifest(
        string hash, long size, string source, string destination) =>
        new(
            1,
            "test-package",
            "Test Package",
            "Test Author",
            "https://example.com/package",
            "Test-only fixture licence",
            "1.0.0",
            "GTA V Legacy",
            "1.0.0.0",
            "99.0.0.0",
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { new PackageManifestFile(source, destination, hash, size) });

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
