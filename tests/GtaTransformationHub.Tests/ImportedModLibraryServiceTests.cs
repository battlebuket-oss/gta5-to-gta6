using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class ImportedModLibraryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubLibraryTests", Guid.NewGuid().ToString("N"));

    public ImportedModLibraryServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task LoadAsync_RecomputesFileHashesAndListsResearchPackage()
    {
        var bytes = Encoding.UTF8.GetBytes("sample-setting=true");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var packageDirectory = CreatePackage(
            "sample-mod",
            "Sample Mod",
            "a".PadRight(64, 'a'),
            ("scripts/sample.ini", bytes, hash));

        var entries = await new ImportedModLibraryService().LoadAsync(_root);

        var entry = Assert.Single(entries);
        Assert.Equal("sample-mod", entry.PackageId);
        Assert.Equal("Sample Mod", entry.Name);
        Assert.Equal("Researching", entry.Status);
        Assert.True(entry.ManifestValid);
        Assert.Empty(entry.Warnings);
        Assert.Contains("re-computed", entry.VerificationNote);
        Assert.Equal(packageDirectory, entry.PackageDirectory);
    }

    [Fact]
    public async Task LoadAsync_FlagsFilesWhoseSha256DoesNotMatchManifest()
    {
        var bytes = Encoding.UTF8.GetBytes("edited-after-import=true");
        CreatePackage(
            "tampered-mod",
            "Tampered Mod",
            "d".PadRight(64, 'd'),
            ("scripts/sample.ini", bytes, "e".PadRight(64, 'e')));

        var entries = await new ImportedModLibraryService().LoadAsync(_root);

        var entry = Assert.Single(entries);
        Assert.False(entry.ManifestValid);
        Assert.Contains(entry.Warnings, warning => warning.Contains("SHA-256 does not match"));
    }

    [Fact]
    public async Task LoadAsync_DoesNotTrustEditedTestedStatusInLocalManifest()
    {
        var bytes = Encoding.UTF8.GetBytes("sample=true");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var directory = CreatePackage(
            "forged-status",
            "Forged Status",
            "f".PadRight(64, 'f'),
            ("scripts/sample.ini", bytes, hash));

        var manifestPath = Path.Combine(directory, "package-manifest.json");
        var json = await File.ReadAllTextAsync(manifestPath);
        await File.WriteAllTextAsync(manifestPath, json.Replace("\"Researching\"", "\"Tested\"", StringComparison.Ordinal));

        var entry = Assert.Single(await new ImportedModLibraryService().LoadAsync(_root));

        Assert.Equal("Researching", entry.Status);
        Assert.False(entry.ManifestValid);
        Assert.Contains(entry.Warnings, warning => warning.Contains("status was changed"));
    }

    [Fact]
    public async Task LoadAsync_FlagsMissingFilesWithoutFailingTheWholeLibrary()
    {
        CreatePackage(
            "missing-mod",
            "Missing Mod",
            "b".PadRight(64, 'b'),
            ("scripts/missing.ini", Encoding.UTF8.GetBytes("missing"), "c".PadRight(64, 'c')));

        var entries = await new ImportedModLibraryService().LoadAsync(_root);

        var entry = Assert.Single(entries);
        Assert.False(entry.ManifestValid);
        Assert.Contains(entry.Warnings, warning => warning.Contains("Imported file is missing"));
    }

    [Fact]
    public async Task LoadAsync_ReportsInvalidManifestAsUnreadableEntry()
    {
        var directory = Path.Combine(_root, "bad-manifest");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "package-manifest.json"), "{not-json");

        var entries = await new ImportedModLibraryService().LoadAsync(_root);

        var entry = Assert.Single(entries);
        Assert.Equal("Unreadable", entry.Status);
        Assert.False(entry.ManifestValid);
    }

    private string CreatePackage(
        string packageId,
        string name,
        string archiveHash,
        params (string Path, byte[] Bytes, string Hash)[] files)
    {
        var directory = Path.Combine(
            _root,
            packageId + "-" + archiveHash[..12].ToLowerInvariant());
        Directory.CreateDirectory(directory);

        var importedFiles = new List<ImportedModFile>();
        foreach (var item in files)
        {
            var target = Path.Combine(directory, item.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, item.Bytes);
            importedFiles.Add(new ImportedModFile(item.Path, item.Bytes.LongLength, item.Hash));
        }

        var metadata = new ModArchiveMetadata(
            packageId,
            name,
            "Test Author",
            new Uri("https://example.com/mod"),
            "Personal use",
            "1.0");
        var manifest = new ImportedModManifest(
            1,
            metadata,
            archiveHash,
            DateTimeOffset.UtcNow,
            CatalogueStatus.Researching,
            importedFiles);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });
        File.WriteAllText(Path.Combine(directory, "package-manifest.json"), json);
        return directory;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
