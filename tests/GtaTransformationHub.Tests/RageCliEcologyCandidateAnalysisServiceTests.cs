using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class RageCliEcologyCandidateAnalysisServiceTests
{
    [Fact]
    public void ValidateDumpBatchContract_AcceptsFirstBatchWhenMoreCandidatesRemain()
    {
        var errors = RageCliEcologyCandidateAnalysisService.ValidateDumpBatchContract(
            3, 135, 0, 50, 50, true, true,
            Enumerable.Range(0, 50).ToArray(),
            Array.Empty<string>());

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateDumpBatchContract_AcceptsFinalPartialBatch()
    {
        var errors = RageCliEcologyCandidateAnalysisService.ValidateDumpBatchContract(
            3, 135, 100, 135, 35, false, true,
            Enumerable.Range(100, 35).ToArray(),
            Array.Empty<string>());

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateDumpBatchContract_AllowsPartialFailedBatchAndKeepsFirstMissingOffset()
    {
        var dumped = Enumerable.Range(50, 50).Where(index => index != 52 && index != 88).ToArray();
        var errors = RageCliEcologyCandidateAnalysisService.ValidateDumpBatchContract(
            3, 135, 50, 52, 50, true, false,
            dumped,
            new[] { "Two metadata resources could not be dumped." });

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateDumpBatchContract_RejectsPartialBatchWithoutRecordedErrors()
    {
        var indexes = Enumerable.Range(0, 50).Where(index => index != 4).ToArray();
        var errors = RageCliEcologyCandidateAnalysisService.ValidateDumpBatchContract(
            3, 135, 0, 4, 50, true, false,
            indexes,
            Array.Empty<string>());

        Assert.Contains(errors, error => error.Contains("must record at least one", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDumpBatchContract_RejectsMissingErrorCollection()
    {
        var errors = RageCliEcologyCandidateAnalysisService.ValidateDumpBatchContract(
            3, 135, 0, 50, 50, true, true,
            Enumerable.Range(0, 50).ToArray(),
            null);

        Assert.Contains(errors, error => error.Contains("error collection is missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, 135, 0, 50, 50, true, true)]
    [InlineData(3, 135, 0, 50, 50, false, true)]
    [InlineData(3, 135, 0, 40, 50, true, true)]
    [InlineData(3, 135, 0, 50, 49, true, true)]
    [InlineData(3, 135, 50, 100, 50, true, false)]
    public void ValidateDumpBatchContract_RejectsInconsistentBatchMetadata(
        int schemaVersion,
        int total,
        int start,
        int next,
        int selected,
        bool more,
        bool complete)
    {
        var errors = RageCliEcologyCandidateAnalysisService.ValidateDumpBatchContract(
            schemaVersion, total, start, next, selected, more, complete,
            Enumerable.Range(start, Math.Max(0, selected)).ToArray(),
            Array.Empty<string>());

        Assert.NotEmpty(errors);
    }

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
