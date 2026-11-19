using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class RageCliEcologyCandidateAnalysisServiceTests
{
    [Theory]
    [InlineData("popgroups.ymt", "CPopulationGroup", "$/pedModels/0/modelName", "modelName", "Population")]
    [InlineData("water.xml", "CWaterConfig", "$/water/waveHeight", "waveHeight", "Water")]
    [InlineData("animals.ymt", "CAnimalModel", "$/animal/modelName", "modelName", "Animal")]
    [InlineData("vegetation.ymt", "CPlantModel", "$/plant/modelName", "modelName", "Plant")]
    [InlineData("habitats.ymt", "CHabitatDefinition", "$/habitat/name", "name", "Habitat")]
    public void ClassifyCandidateField_DetectsReviewCategoryFromPathAndMetadataContext(
        string resource, string structure, string path, string field, string expected)
    {
        Assert.Equal(expected,
            RageCliEcologyCandidateAnalysisService.ClassifyCandidateField(resource, structure, path, field));
    }

    [Fact]
    public void ClassifyCandidateField_LeavesGenericFieldsUnclassified()
    {
        Assert.Null(RageCliEcologyCandidateAnalysisService.ClassifyCandidateField(
            "generic.ymt", "CGenericRecord", "$/displayName", "displayName"));
    }

    [Fact]
    public void ExtractCandidatesFromJson_CollectsHeuristicRecordsAndSourceProvenance()
    {
        const string json = """
        {
          "$type": "CPopulationGroup",
          "animalModels": [
            { "$type": "CAnimalModel", "modelName": "a_c_deer", "speciesName": "deer" }
          ],
          "waterSettings": { "$type": "CWaterConfig", "waveHeight": 1.25 }
        }
        """;

        var candidates = RageCliEcologyCandidateAnalysisService.ExtractCandidatesFromJson(
            json,
            "popgroups_and_water.ymt",
            "x64v.rpf/common/data/popgroups_and_water.ymt",
            @"C:\\Games\\GTA V\\x64v.rpf",
            new string('A', 64),
            new string('B', 64));

        Assert.Contains(candidates, item =>
            item.Category == "Population" && item.CandidateValue == "a_c_deer");
        Assert.Contains(candidates, item =>
            item.Category == "Animal" && item.CandidateValue == "deer");
        Assert.Contains(candidates, item =>
            item.Category == "Water" && item.CandidateValue == "1.25");
        Assert.All(candidates, item =>
        {
            Assert.Equal(new string('A', 64), item.SourceArchiveSha256);
            Assert.Equal(new string('B', 64), item.DumpJsonSha256);
            Assert.Contains("Heuristic", item.EvidenceNote, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ExtractCandidatesFromJson_DoesNotInventCandidatesForUnmatchedValues()
    {
        const string json = """{"$type":"CGeneric","label":"ordinary text","number":7}""";
        var candidates = RageCliEcologyCandidateAnalysisService.ExtractCandidatesFromJson(
            json, "generic.ymt", "x64v.rpf/generic.ymt",
            @"C:\\Games\\GTA V\\x64v.rpf",
            new string('A', 64), new string('B', 64));
        Assert.Empty(candidates);
    }
}
