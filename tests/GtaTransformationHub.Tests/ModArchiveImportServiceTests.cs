using System.IO.Compression;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class ModArchiveImportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubImportTests", Guid.NewGuid().ToString("N"));
    private readonly string _archiveRoot;
    private readonly string _importRoot;

    public ModArchiveImportServiceTests()
    {
        _archiveRoot = Path.Combine(_root, "archives");
        _importRoot = Path.Combine(_root, "imports");
        Directory.CreateDirectory(_archiveRoot);
        Directory.CreateDirectory(_importRoot);
    }

    [Fact]
    public async Task ImportAsync_ExtractsFilesAndWritesResearchOnlyManifest()
    {
        var archive = CreateZip("valid.zip", ("scripts/example.ini", "setting=true"));
        var result = await new ModArchiveImportService().ImportAsync(
            archive, _importRoot, Metadata());

        Assert.Single(result.Files);
        Assert.Equal("scripts/example.ini", result.Files[0].RelativePath);
        Assert.Equal("setting=true", await File.ReadAllTextAsync(
            Path.Combine(result.PackageDirectory, "scripts", "example.ini")));
        Assert.True(File.Exists(result.ManifestPath));

        var manifest = await File.ReadAllTextAsync(result.ManifestPath);
        Assert.Contains("\"Researching\"", manifest);
        Assert.Contains(result.Files[0].Sha256, manifest);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("folder/../outside.txt")]
    [InlineData("folder/CON.txt")]
    public async Task ImportAsync_RejectsUnsafePaths(string path)
    {
        var archive = CreateZip("unsafe-" + Guid.NewGuid().ToString("N") + ".zip", (path, "payload"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ModArchiveImportService().ImportAsync(archive, _importRoot, Metadata()));

        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
        Assert.Empty(Directory.EnumerateDirectories(_importRoot));
    }

    [Fact]
    public async Task ImportAsync_RejectsCaseInsensitiveDuplicatePaths()
    {
        var archive = CreateZip("duplicate.zip", ("mods/item.dat", "one"), ("MODS/ITEM.DAT", "two"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ModArchiveImportService().ImportAsync(archive, _importRoot, Metadata()));
    }

    [Fact]
    public async Task ImportAsync_RejectsAFileUsedAsAParentDirectory()
    {
        var archive = CreateZip("parent-conflict.zip", ("mods", "file"), ("mods/item.dat", "child"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ModArchiveImportService().ImportAsync(archive, _importRoot, Metadata()));
    }

    [Fact]
    public async Task ImportAsync_RejectsArchivesOverConfiguredSizeLimit()
    {
        var archive = CreateZip("large.zip", ("large.bin", new string('x', 2048)));
        var service = new ModArchiveImportService(new ModArchiveImportLimits(
            MaximumEntries: 10,
            MaximumEntryBytes: 1024,
            MaximumTotalBytes: 4096,
            MaximumCompressionRatio: 1000));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportAsync(archive, _importRoot, Metadata()));
    }

    [Fact]
    public async Task ImportAsync_RejectsSymbolicLinkEntries()
    {
        var archive = Path.Combine(_archiveRoot, "symlink.zip");
        using (var file = new FileStream(archive, FileMode.CreateNew))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("link");
            entry.ExternalAttributes = unchecked((int)(0xA000 << 16));
            using var stream = entry.Open();
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("target"));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ModArchiveImportService().ImportAsync(archive, _importRoot, Metadata()));
    }

    [Fact]
    public async Task ImportAsync_RejectsNonHttpsSourceMetadata()
    {
        var archive = CreateZip("metadata.zip", ("mod.ini", "x"));
        var metadata = Metadata() with { SourceUri = new Uri("http://example.com/mod") };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ModArchiveImportService().ImportAsync(archive, _importRoot, metadata));
    }


    [Fact]
    public async Task ImportAsync_RejectsDirectorySymbolicLinkEntries()
    {
        var archive = Path.Combine(_archiveRoot, "directory-symlink.zip");
        using (var file = new FileStream(archive, FileMode.CreateNew))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("linked-folder/");
            entry.ExternalAttributes = unchecked((int)(0xA000 << 16));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ModArchiveImportService().ImportAsync(archive, _importRoot, Metadata()));
    }

    [Fact]
    public async Task ImportAsync_RejectsRepeatedExactArchiveInsteadOfOverwritingImport()
    {
        var archive = CreateZip("repeat.zip", ("mod.ini", "setting=true"));
        var service = new ModArchiveImportService();
        await service.ImportAsync(archive, _importRoot, Metadata());

        await Assert.ThrowsAsync<IOException>(() =>
            service.ImportAsync(archive, _importRoot, Metadata()));
    }

    private string CreateZip(string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_archiveRoot, name);
        using var file = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryPath).Open());
            writer.Write(content);
        }
        return path;
    }

    private static ModArchiveMetadata Metadata() => new(
        "sample-mod",
        "Sample Mod",
        "Test Author",
        new Uri("https://example.com/mod"),
        "All rights reserved; user-provided copy",
        "1.0");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
