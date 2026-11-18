using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class EcologyInventoryDiffServiceTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void Compare_AllowsNewLandAndAquaticSpecies_AndWaterRenderingUpgrade()
    {
        var baseline = CreateBaseline();
        var candidate = baseline with
        {
            Species = baseline.Species.Concat(new[]
            {
                new EcologySpeciesIdentity(
                    "reef-fish", "AquaticAnimal", new[] { "a_coral_fish_01" },
                    new[] { "pacific-ocean" }, "fish-schooling-v1"),
                new EcologySpeciesIdentity(
                    "marsh-lily", "AquaticPlant", new[] { "prop_marsh_lily_01" },
                    new[] { "coastal-wetland" }, "plant-static-v1"),
                new EcologySpeciesIdentity(
                    "wild-orchid", "TerrestrialPlant", new[] { "prop_wild_orchid_01" },
                    new[] { "marsh-edge" }, "plant-static-v1"),
                new EcologySpeciesIdentity(
                    "scrub-fox", "TerrestrialAnimal", new[] { "a_scrub_fox_01" },
                    new[] { "marsh-edge" }, "fox-walk-v1")
            }).ToArray(),
            Habitats = baseline.Habitats.Concat(new[]
            {
                new EcologyHabitatIdentity("coastal-wetland", "Wetland", "pacific-ocean"),
                new EcologyHabitatIdentity("marsh-edge", "Terrestrial", "")
            }).ToArray(),
            PopulationEntries = baseline.PopulationEntries.Concat(new[]
            {
                new EcologyPopulationEntry("reef-fish-school", "reef-fish", "pacific-ocean", HashB),
                new EcologyPopulationEntry("marsh-lily-patch", "marsh-lily", "coastal-wetland", HashB),
                new EcologyPopulationEntry("wild-orchid-patch", "wild-orchid", "sandy-hills", HashB),
                new EcologyPopulationEntry("scrub-fox-range", "scrub-fox", "sandy-hills", HashB)
            }).ToArray(),
            WaterBodies = baseline.WaterBodies.Select(water => water with
            {
                RenderingProfileId = "water-rendering-higher-fidelity-v2"
            }).ToArray()
        };

        var result = EcologyInventoryDiffService.Compare(baseline, candidate);

        Assert.True(result.VanillaPreserved, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(result.Errors);
        Assert.Equal(4, result.AddedSpecies.Count);
        Assert.Contains(result.AddedSpecies, item => item.Domain == "AquaticAnimal");
        Assert.Contains(result.AddedSpecies, item => item.Domain == "AquaticPlant");
        Assert.Contains(result.AddedSpecies, item => item.Domain == "TerrestrialAnimal");
        Assert.Contains(result.AddedSpecies, item => item.Domain == "TerrestrialPlant");
    }

    [Fact]
    public void Compare_RejectsRemovedOrChangedVanillaSpeciesAndPopulationEntries()
    {
        var baseline = CreateBaseline();
        var candidate = baseline with
        {
            Species = baseline.Species
                .Where(item => item.Id != "vanilla-deer")
                .Append(new EcologySpeciesIdentity(
                    "vanilla-eagle", "TerrestrialAnimal", new[] { "a_eagle" },
                    new[] { "sandy-hills" }, "bird-fly-v1"))
                .ToArray(),
            PopulationEntries = baseline.PopulationEntries.Select(entry =>
                entry.Id == "deer-range"
                    ? entry with { DefinitionSha256 = HashB }
                    : entry).ToArray()
        };

        var result = EcologyInventoryDiffService.Compare(baseline, candidate);

        Assert.False(result.VanillaPreserved);
        Assert.Contains(result.Errors, error => error.Contains("Vanilla species 'vanilla-deer' was removed"));
        Assert.Contains(result.Errors, error => error.Contains("Vanilla population entry 'deer-range' was changed"));
    }

    [Fact]
    public void Compare_RejectsWaterSimulationOrInteractionIdentityChanges()
    {
        var baseline = CreateBaseline();
        var candidate = baseline with
        {
            WaterBodies = baseline.WaterBodies.Select(water =>
                water with { SimulationProfileId = "replacement-wave-simulation" }).ToArray()
        };

        var result = EcologyInventoryDiffService.Compare(baseline, candidate);

        Assert.False(result.VanillaPreserved);
        Assert.Contains(result.Errors, error => error.Contains("simulation or interaction identity"));
    }

    [Fact]
    public void Compare_RejectsNewSpeciesThatReusesVanillaModelIdentifier()
    {
        var baseline = CreateBaseline();
        var candidate = baseline with
        {
            Species = baseline.Species.Append(new EcologySpeciesIdentity(
                "new-fish", "AquaticAnimal", new[] { "a_deer" },
                new[] { "pacific-ocean" }, "fish-schooling-v1")).ToArray(),
            PopulationEntries = baseline.PopulationEntries.Append(new EcologyPopulationEntry(
                "new-fish-pop", "new-fish", "pacific-ocean", HashB)).ToArray()
        };

        var result = EcologyInventoryDiffService.Compare(baseline, candidate);

        Assert.False(result.VanillaPreserved);
        Assert.Contains(result.Errors, error => error.Contains("reuses a vanilla model identifier"));
    }

    [Fact]
    public void Compare_RejectsDifferentGameBuildOrExecutableFingerprint()
    {
        var baseline = CreateBaseline();
        var candidate = baseline with { GameBuild = "different-build" };

        var result = EcologyInventoryDiffService.Compare(baseline, candidate);

        Assert.False(result.VanillaPreserved);
        Assert.Contains(result.Errors, error => error.Contains("different GTA V game build"));
    }

    private static EcologyInventorySnapshot CreateBaseline() =>
        new(
            1,
            "GTA V Legacy 1.0.1000.0",
            HashA,
            "verified-fixture-extractor",
            "1.0.0",
            new[] { HashA },
            new[]
            {
                new EcologySpeciesIdentity(
                    "vanilla-deer", "TerrestrialAnimal", new[] { "a_deer" },
                    new[] { "sandy-hills" }, "deer-walk-v1"),
                new EcologySpeciesIdentity(
                    "vanilla-seagrass", "AquaticPlant", new[] { "prop_seagrass_01" },
                    new[] { "pacific-ocean" }, "plant-static-v1")
            },
            new[]
            {
                new EcologyHabitatIdentity("sandy-hills", "Terrestrial", ""),
                new EcologyHabitatIdentity("pacific-ocean", "Ocean", "pacific-ocean")
            },
            new[]
            {
                new EcologyPopulationEntry("deer-range", "vanilla-deer", "sandy-hills", HashA),
                new EcologyPopulationEntry("seagrass-bed", "vanilla-seagrass", "pacific-ocean", HashA)
            },
            new[]
            {
                new EcologyWaterBodyIdentity(
                    "pacific-ocean", "Ocean", "ocean-wave-simulation-v1",
                    "ocean-interaction-v1", "ocean-render-v1")
            });
}
