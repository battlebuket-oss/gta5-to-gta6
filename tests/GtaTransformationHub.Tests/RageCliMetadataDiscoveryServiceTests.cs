using System.Text.Json;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class RageCliMetadataDiscoveryServiceTests
{
    private const string SearchJson = """
    [
      {
        "archive": "C:/Games/GTA V/x64v.rpf",
        "path": "x64v.rpf/levels/gta5/popgroups.ymt",
        "name": "popgroups.ymt",
        "size": 1024,
        "mem_size": 2048,
        "kind": "resource",
        "hash": "0x12345678",
        "stem_hash": "0x87654321",
        "offset": null
      }
    ]
    """;

    [Fact]
    public void EcologyPatterns_CoverPopulationWaterAndTerrestrialAquaticLife()
    {
        Assert.Contains("*.ymt", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*.meta", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*popgroups*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*popcycle*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*pedmodelsets*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*water*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*animal*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*fish*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*vegetation*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*flora*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
        Assert.Contains("*fauna*", RageCliMetadataDiscoveryService.EcologyMetadataPatterns);
    }

    [Fact]
    public void BuildSearchArguments_UsesGameExecutableAndBoundedJsonSearch()
    {
        var arguments = RageCliMetadataDiscoveryService.BuildSearchArguments(
            @"C:\Games\GTA V\GTA5.exe",
            @"C:\Games\GTA V",
            "*popgroups*",
            200);

        Assert.Equal("--no-update-check", arguments[0]);
        Assert.Equal("--exe", arguments[1]);
        Assert.Equal(Path.GetFullPath(@"C:\Games\GTA V\GTA5.exe"), arguments[2]);
        Assert.Equal("search", arguments[3]);
        Assert.Equal(Path.GetFullPath(@"C:\Games\GTA V"), arguments[4]);
        Assert.Equal("*popgroups*", arguments[5]);
        Assert.Contains("--json", arguments);
        Assert.Equal("200", arguments[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*.ymt\n--version")]
    [InlineData("*popgroups*\r--version")]
    [InlineData("*".ymt")]
    public void BuildSearchArguments_RejectsCommandInjectionPatterns(string pattern)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            RageCliMetadataDiscoveryService.BuildSearchArguments(
                @"C:\Games\GTA V\GTA5.exe",
                @"C:\Games\GTA V",
                pattern,
                200));
    }

    [Fact]
    public void BuildSearchArguments_RejectsUnboundedOrInvalidHitLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RageCliMetadataDiscoveryService.BuildSearchArguments("GTA5.exe", ".", "*.ymt", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RageCliMetadataDiscoveryService.BuildSearchArguments("GTA5.exe", ".", "*.ymt", 1001));
    }

    [Fact]
    public void ParseSearchJson_ExtractsNestedRpfMetadataReference()
    {
        var hit = Assert.Single(RageCliMetadataDiscoveryService.ParseSearchJson(SearchJson));

        Assert.Equal(@"C:/Games/GTA V/x64v.rpf", hit.ArchivePath);
        Assert.Equal("x64v.rpf/levels/gta5/popgroups.ymt", hit.Path);
        Assert.Equal("popgroups.ymt", hit.Name);
        Assert.Equal(1024, hit.Size);
        Assert.Equal(2048, hit.MemorySize);
        Assert.Equal("resource", hit.Kind);
        Assert.Equal("0x12345678", hit.Hash);
        Assert.Equal("0x87654321", hit.StemHash);
    }

    [Fact]
    public void ParseSearchJson_RejectsTraversalOrRootedInternalPaths()
    {
        using var document = JsonDocument.Parse(SearchJson);
        var root = document.RootElement[0].Clone();
        var forged = JsonSerializer.Serialize(new[]
        {
            new
            {
                archive = "C:/Games/GTA V/x64v.rpf",
                path = "../outside.ymt",
                name = "outside.ymt",
                size = 10,
                mem_size = 10,
                kind = "resource",
                hash = "0x00000001",
                stem_hash = "0x00000002",
                offset = (int?)null
            }
        });

        Assert.Throws<InvalidDataException>(() =>
            RageCliMetadataDiscoveryService.ParseSearchJson(forged));

        var rooted = JsonSerializer.Serialize(new[]
        {
            new
            {
                archive = "C:/Games/GTA V/x64v.rpf",
                path = "C:/outside.ymt",
                name = "outside.ymt",
                size = 10,
                mem_size = 10,
                kind = "resource",
                hash = "0x00000001",
                stem_hash = "0x00000002",
                offset = (int?)null
            }
        });
        Assert.Throws<InvalidDataException>(() =>
            RageCliMetadataDiscoveryService.ParseSearchJson(rooted));
    }

    [Fact]
    public void ParseSearchJson_RejectsMalformedOrIncompleteResults()
    {
        Assert.Throws<JsonException>(() =>
            RageCliMetadataDiscoveryService.ParseSearchJson("{not-json}"));
        Assert.Throws<InvalidDataException>(() =>
            RageCliMetadataDiscoveryService.ParseSearchJson("{}"));
        Assert.Throws<InvalidDataException>(() =>
            RageCliMetadataDiscoveryService.ParseSearchJson(
                """[{"archive":"C:/Games/GTA V/x64v.rpf"}]"""));
    }

    [Fact]
    public void ParseSearchJson_AllowsNoMatches()
    {
        Assert.Empty(RageCliMetadataDiscoveryService.ParseSearchJson("[]"));
    }
}
