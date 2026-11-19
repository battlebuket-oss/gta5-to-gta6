using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class EcologyInventoryEvidenceServiceTests : IDisposable
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubEcologyEvidenceTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompareAndRecordAsync_ValidatesExactGameAndWritesSemanticOnlyReport()
    {
        Directory.CreateDirectory(_root);
        var gameRoot = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameRoot);
        var executable = Path.Combine(gameRoot, "GTA5.exe");
        await File.WriteAllBytesAsync(executable, Enumerable.Range(0, 8192)
            .Select(index => (byte)(index % 251)).ToArray());

        var gameBuild = "1.0.3889.0";
        var executableHash = await GameExecutableFingerprintService.ComputeSha256Async(executable);
        var baseline = CreateSnapshot(gameBuild, executableHash);
        var candidate = baseline with
        {
            WaterBodies = baseline.WaterBodies.Select(water => water with
            {
                RenderingProfileId = "water-higher-fidelity",
                RenderingDefinitionSha256 = HashB
            }).ToArray()
        };

        var baselinePath = Path.Combine(_root, "vanilla.json");
        var candidatePath = Path.Combine(_root, "candidate.json");
        await WriteSnapshotAsync(baselinePath, baseline);
        await WriteSnapshotAsync(candidatePath, candidate);

        var installation = new GameInstallation(
            gameRoot, executable, gameBuild, gameBuild, gameBuild);
        var service = new EcologyInventoryEvidenceService(Path.Combine(_root, "evidence"));

        var result = await service.CompareAndRecordAsync(
            baselinePath, candidatePath, installation);

        Assert.True(result.Diff.VanillaPreserved, string.Join(Environment.NewLine, result.Diff.Errors));
        Assert.Empty(result.Diff.Errors);
        Assert.False(result.CompletenessVerified);
        Assert.False(result.PackageEligibilityUnlocked);
        Assert.True(File.Exists(result.ReportPath));
        Assert.Equal(64, result.ReportSha256.Length);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(result.ReportPath));
        Assert.True(report.RootElement.GetProperty("semanticComparisonPassed").GetBoolean());
        Assert.False(report.RootElement.GetProperty("completenessVerified").GetBoolean());
        Assert.False(report.RootElement.GetProperty("packageEligibilityUnlocked").GetBoolean());
        Assert.Contains("bounded set of metadata candidates",
            report.RootElement.GetProperty("limitation").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompareAndRecordAsync_RejectsSnapshotFromAnotherExecutableFingerprint()
    {
        Directory.CreateDirectory(_root);
        var gameRoot = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameRoot);
        var executable = Path.Combine(gameRoot, "GTA5.exe");
        await File.WriteAllTextAsync(executable, "selected executable");

        var gameBuild = "1.0.3889.0";
        var executableHash = await GameExecutableFingerprintService.ComputeSha256Async(executable);
        var baseline = CreateSnapshot(gameBuild, HashB);
        var candidate = CreateSnapshot(gameBuild, HashB);
        var baselinePath = Path.Combine(_root, "vanilla.json");
        var candidatePath = Path.Combine(_root, "candidate.json");
        await WriteSnapshotAsync(baselinePath, baseline);
        await WriteSnapshotAsync(candidatePath, candidate);

        var installation = new GameInstallation(
            gameRoot, executable, gameBuild, gameBuild, gameBuild);
        var service = new EcologyInventoryEvidenceService(Path.Combine(_root, "evidence"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.CompareAndRecordAsync(baselinePath, candidatePath, installation));

        Assert.Contains("Baseline snapshot GTA5.exe SHA-256", exception.Message);
        Assert.False(Directory.Exists(service.EvidenceRoot));
        Assert.NotEqual(HashB, executableHash);
    }

    [Fact]
    public async Task CompareAndRecordAsync_RejectsAnotherGameBuild()
    {
        Directory.CreateDirectory(_root);
        var gameRoot = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameRoot);
        var executable = Path.Combine(gameRoot, "GTA5.exe");
        await File.WriteAllTextAsync(executable, "selected executable");
        var executableHash = await GameExecutableFingerprintService.ComputeSha256Async(executable);

        var baselinePath = Path.Combine(_root, "vanilla.json");
        var candidatePath = Path.Combine(_root, "candidate.json");
        await WriteSnapshotAsync(baselinePath, CreateSnapshot("1.0.3888.0", executableHash));
        await WriteSnapshotAsync(candidatePath, CreateSnapshot("1.0.3888.0", executableHash));

        var installation = new GameInstallation(
            gameRoot, executable, "1.0.3889.0", "1.0.3889.0", "1.0.3889.0");
        var service = new EcologyInventoryEvidenceService(Path.Combine(_root, "evidence"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.CompareAndRecordAsync(baselinePath, candidatePath, installation));

        Assert.Contains("targets GTA V build 1.0.3888.0", exception.Message);
    }

    [Fact]
    public async Task CompareAndRecordAsync_RejectsEmptySemanticInventories()
    {
        Directory.CreateDirectory(_root);
        var gameRoot = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameRoot);
        var executable = Path.Combine(gameRoot, "GTA5.exe");
        await File.WriteAllTextAsync(executable, "selected executable");
        var build = "1.0.3889.0";
        var executableHash = await GameExecutableFingerprintService.ComputeSha256Async(executable);

        var populated = CreateSnapshot(build, executableHash);
        var empty = populated with
        {
            Species = Array.Empty<EcologySpeciesIdentity>(),
            Habitats = Array.Empty<EcologyHabitatIdentity>(),
            PopulationEntries = Array.Empty<EcologyPopulationEntry>(),
            WaterBodies = Array.Empty<EcologyWaterBodyIdentity>()
        };

        var baselinePath = Path.Combine(_root, "vanilla.json");
        var candidatePath = Path.Combine(_root, "candidate.json");
        await WriteSnapshotAsync(baselinePath, empty);
        await WriteSnapshotAsync(candidatePath, empty);

        var installation = new GameInstallation(
            gameRoot, executable, build, build, build);
        var service = new EcologyInventoryEvidenceService(Path.Combine(_root, "evidence"));
        var result = await service.CompareAndRecordAsync(
            baselinePath, candidatePath, installation);

        Assert.False(result.Diff.VanillaPreserved);
        Assert.Contains(result.Diff.Errors, error => error.Contains("has no species identities"));
        Assert.False(result.CompletenessVerified);
        Assert.False(result.PackageEligibilityUnlocked);
    }

    [Fact]
    public async Task CompareAndRecordAsync_RejectsMalformedJson()
    {
        Directory.CreateDirectory(_root);
        var baseline = Path.Combine(_root, "bad.json");
        var candidate = Path.Combine(_root, "candidate.json");
        await File.WriteAllTextAsync(baseline, "{ not valid json }");
        await File.WriteAllTextAsync(candidate, "{}");
        var gameRoot = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameRoot);
        var executable = Path.Combine(gameRoot, "GTA5.exe");
        await File.WriteAllTextAsync(executable, "selected executable");
        var build = "1.0.3889.0";
        var installation = new GameInstallation(gameRoot, executable, build, build, build);
        var service = new EcologyInventoryEvidenceService(Path.Combine(_root, "evidence"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.CompareAndRecordAsync(baseline, candidate, installation));
    }

    private static EcologyInventorySnapshot CreateSnapshot(string gameBuild, string executableSha256) =>
        new(
            1,
            gameBuild,
            executableSha256,
            "gta-hub-semantic-import",
            "1.0.0",
            new[] { HashA, HashB },
            new[]
            {
                new EcologySpeciesIdentity(
                    "vanilla-deer", "TerrestrialAnimal", new[] { "a_deer" },
                    new[] { "sandy-hills" }, "deer-walk", HashA),
                new EcologySpeciesIdentity(
                    "vanilla-seagrass", "AquaticPlant", new[] { "prop_seagrass_01" },
                    new[] { "pacific-ocean" }, "plant-static", HashA)
            },
            new[]
            {
                new EcologyHabitatIdentity("sandy-hills", "Terrestrial", "", HashA),
                new EcologyHabitatIdentity("pacific-ocean", "Ocean", "pacific-ocean", HashA)
            },
            new[]
            {
                new EcologyPopulationEntry("deer-range", "vanilla-deer", "sandy-hills", HashA),
                new EcologyPopulationEntry("seagrass-bed", "vanilla-seagrass", "pacific-ocean", HashA)
            },
            new[]
            {
                new EcologyWaterBodyIdentity(
                    "pacific-ocean", "Ocean", "ocean-sim-v1", "ocean-interaction-v1",
                    "ocean-render-v1", HashA, HashA, HashA)
            });

    private static async Task WriteSnapshotAsync(
        string path,
        EcologyInventorySnapshot snapshot)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
