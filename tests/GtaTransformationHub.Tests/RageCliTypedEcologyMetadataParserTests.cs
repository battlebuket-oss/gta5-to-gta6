using System.Text.Json;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class RageCliTypedEcologyMetadataParserTests
{
    private static readonly string ArchiveSha = new('A', 64);
    private static readonly string DumpSha = new('B', 64);

    [Fact]
    public void ParsePedRegistry_ExtractsVanillaPedDefinitionsAndAnimalCandidates()
    {
        const string json = """
        {
          "$type": "CPedModelInfo__InitDataList",
          "residentTxd": "comp_peds_generic",
          "InitDatas": [
            {
              "$type": "CPedModelInfo__InitData",
              "Name": "A_C_Deer",
              "PropsName": "A_C_Deer_p",
              "Pedtype": "PED_TYPE_ANIMAL",
              "MovementClipSet": "move_m@deer",
              "CreatureMetadataName": "deer",
              "AnimalAudioObject": "A_C_Deer",
              "AbilityType": "ABILITY_ANIMAL",
              "ThermalBehaviour": "TB_WARM",
              "DecisionMakerName": "DM_ANIMAL",
              "Personality": "ANIMAL",
              "CombatInfo": "ANIMAL_COMBAT",
              "PedCapsuleName": "ANIMAL_CAPSULE",
              "PedLayoutName": "ANIMAL_LAYOUT",
              "PedComponentSetName": "ANIMAL_COMPONENTS",
              "NavCapabilitiesName": "NAV_ANIMAL",
              "PerceptionInfo": "PERCEPTION_ANIMAL",
              "DefaultTaskDataSetName": "TASK_ANIMAL",
              "ScenarioPopStreamingSlot": "SCENARIO_ANIMAL",
              "DefaultSpawningPreference": "SPAWN_WILD",
              "IsStreamedGfx": true
            },
            {
              "$type": "CPedModelInfo__InitData",
              "Name": "A_C_Fish",
              "Pedtype": "PED_TYPE_FISH",
              "AnimalAudioObject": "A_C_Fish"
            },
            {
              "$type": "CPedModelInfo__InitData",
              "Name": "A_M_Y_Business_01",
              "Pedtype": "PED_TYPE_CIVMALE"
            }
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var result = RageCliTypedEcologyMetadataParser.ParsePedRegistry(
            document.RootElement,
            17,
            "peds.ymt",
            "update.rpf/x64/data/peds.ymt",
            @"C:\Games\GTA V\update.rpf",
            ArchiveSha,
            DumpSha);

        Assert.True(result.Recognized);
        Assert.True(result.Complete);
        Assert.Equal(3, result.DeclaredEntryCount);
        Assert.Equal(3, result.Entries.Count);

        var deer = Assert.Single(result.Entries, item => item.ModelName == "A_C_Deer");
        Assert.True(deer.IsAnimalCandidate);
        Assert.Equal("TerrestrialAnimal", deer.AnimalDomainCandidate);
        Assert.Contains("CreatureMetadataName", deer.ClassificationEvidence);
        Assert.Equal(64, deer.BehaviourMetadataSha256.Length);
        Assert.Equal(64, deer.PedDefinitionSha256.Length);
        Assert.Equal(ArchiveSha, deer.SourceArchiveSha256);
        Assert.Equal(DumpSha, deer.DumpJsonSha256);
        Assert.True(deer.IsStreamedGfx);

        var fish = Assert.Single(result.Entries, item => item.ModelName == "A_C_Fish");
        Assert.True(fish.IsAnimalCandidate);
        Assert.Equal("AquaticAnimal", fish.AnimalDomainCandidate);

        var human = Assert.Single(result.Entries, item => item.ModelName == "A_M_Y_Business_01");
        Assert.False(human.IsAnimalCandidate);
        Assert.Null(human.AnimalDomainCandidate);
    }

    [Fact]
    public void ParsePedRegistry_RecognizesWrongRootAsNotApplicable()
    {
        using var document = JsonDocument.Parse("""{"$type":"CMapData","InitDatas":[]}""");
        var result = RageCliTypedEcologyMetadataParser.ParsePedRegistry(
            document.RootElement, 0, "generic.ymt", "x64.rpf/generic.ymt",
            @"C:\Games\GTA V\x64.rpf", ArchiveSha, DumpSha);

        Assert.False(result.Recognized);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void ParsePedRegistry_ReportsMissingModelNamesInsteadOfDroppingSilently()
    {
        using var document = JsonDocument.Parse("""
        {
          "$type": "CPedModelInfo__InitDataList",
          "InitDatas": [
            { "$type": "CPedModelInfo__InitData", "Pedtype": "PED_TYPE_ANIMAL" },
            { "$type": "CPedModelInfo__InitData", "Name": "A_C_Coyote", "Pedtype": "PED_TYPE_ANIMAL" }
          ]
        }
        """);

        var result = RageCliTypedEcologyMetadataParser.ParsePedRegistry(
            document.RootElement, 0, "peds.ymt", "x64.rpf/peds.ymt",
            @"C:\Games\GTA V\x64.rpf", ArchiveSha, DumpSha);

        Assert.True(result.Recognized);
        Assert.False(result.Complete);
        Assert.Single(result.Entries);
        Assert.Contains(result.Warnings, warning => warning.Contains("no Name", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseWaterGeometry_ExtractsQuadsCalmingAndWaveParameters()
    {
        const string json = """
        {
          "$type": "WaterData",
          "WaterQuads": [
            {
              "$type": "Item",
              "minX": -1592.0, "maxX": -1304.0,
              "minY": -1744.0, "maxY": -1624.0,
              "Type": 0, "IsInvisible": false, "HasLimitedDepth": false,
              "z": 0.0, "a1": 26.0, "a2": 26.0, "a3": 26.0, "a4": 26.0,
              "NoStencil": false
            }
          ],
          "CalmingQuads": [
            {
              "$type": "Item",
              "minX": 1752.0, "maxX": 2076.0,
              "minY": 216.0, "maxY": 800.0,
              "fDampening": 0.05
            }
          ],
          "WaveQuads": [
            {
              "$type": "Item",
              "minX": 1664.0, "maxX": 1988.0,
              "minY": -120.0, "maxY": 132.0,
              "Amplitude": 0.1, "XDirection": -0.603208, "YDirection": -0.797584
            }
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var result = RageCliTypedEcologyMetadataParser.ParseWaterGeometry(
            document.RootElement,
            9,
            "water.xml",
            "common/data/levels/gta5/water.xml",
            @"C:\Games\GTA V\update.rpf",
            ArchiveSha,
            DumpSha);

        Assert.True(result.Recognized);
        Assert.True(result.Complete);
        var file = Assert.IsType<WaterGeometryFileDefinition>(result.File);
        var quad = Assert.Single(file.WaterQuads);
        Assert.Equal(-1592.0, quad.MinX);
        Assert.Equal(0, quad.TypeCode);
        Assert.False(quad.IsInvisible);
        Assert.Equal(new[] { 26.0, 26.0, 26.0, 26.0 }, quad.Alpha);
        Assert.Equal(0.05, Assert.Single(file.CalmingQuads).Dampening);
        var wave = Assert.Single(file.WaveQuads);
        Assert.Equal(-0.603208, wave.XDirection);
        Assert.Equal(-0.797584, wave.YDirection);
    }

    [Fact]
    public void ParseWaterGeometry_RejectsInvertedBoundsAndPreservesWarning()
    {
        using var document = JsonDocument.Parse("""
        {
          "$type":"WaterData",
          "WaterQuads":[
            {"minX":10,"maxX":1,"minY":0,"maxY":1,"Type":0,"IsInvisible":false,
             "HasLimitedDepth":false,"z":0,"a1":1,"a2":1,"a3":1,"a4":1,"NoStencil":false}
          ],
          "CalmingQuads":[],
          "WaveQuads":[]
        }
        """);

        var result = RageCliTypedEcologyMetadataParser.ParseWaterGeometry(
            document.RootElement, 1, "water.xml", "common/data/levels/gta5/water.xml",
            @"C:\Games\GTA V\update.rpf", ArchiveSha, DumpSha);

        Assert.True(result.Recognized);
        Assert.False(result.Complete);
        Assert.Empty(Assert.IsType<WaterGeometryFileDefinition>(result.File).WaterQuads);
        Assert.Contains(result.Warnings, warning => warning.Contains("inverted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseWaterGeometry_AcceptsXmlValueAttributeShapePreservedByRageJson()
    {
        using var document = JsonDocument.Parse("""
        {
          "$type":"WaterData",
          "WaterQuads":[
            {"$type":"Item",
             "minX":{"value":"-10.5"},"maxX":{"value":"20.5"},
             "minY":{"value":"-2"},"maxY":{"value":"5"},
             "Type":{"value":"2"},"IsInvisible":{"value":"true"},
             "HasLimitedDepth":{"value":"false"},"z":{"value":"1.25"},
             "a1":{"value":"1"},"a2":{"value":"2"},"a3":{"value":"3"},"a4":{"value":"4"},
             "NoStencil":{"value":"false"}}
          ],
          "CalmingQuads":[],
          "WaveQuads":[]
        }
        """);

        var result = RageCliTypedEcologyMetadataParser.ParseWaterGeometry(
            document.RootElement, 3, "water.xml", "common/data/levels/gta5/water.xml",
            @"C:\Games\GTA V\update.rpf", ArchiveSha, DumpSha);

        Assert.True(result.Complete);
        var quad = Assert.Single(Assert.IsType<WaterGeometryFileDefinition>(result.File).WaterQuads);
        Assert.Equal(-10.5, quad.MinX);
        Assert.True(quad.IsInvisible);
    }

    [Fact]
    public void ParseWaterGeometry_RecognizesNonWaterMetadataAsNotApplicable()
    {
        using var document = JsonDocument.Parse("""{"$type":"CPedModelInfo__InitDataList"}""");
        var result = RageCliTypedEcologyMetadataParser.ParseWaterGeometry(
            document.RootElement, 0, "peds.ymt", "update.rpf/x64/data/peds.ymt",
            @"C:\Games\GTA V\update.rpf", ArchiveSha, DumpSha);

        Assert.False(result.Recognized);
        Assert.Null(result.File);
    }
}
