using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class EcologyInventoryCurationServiceTests
{
    private const string GameHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ArchiveHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string DumpHash = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    [Fact]
    public void BuildNormalizedSnapshot_AcceptsAnimalAndPlantEvidenceForTerrestrialAndAquaticDomains()
    {
        var service = CreateService();
        var workspace = CreateValidWorkspace();

        var result = service.BuildNormalizedSnapshot(workspace);

        Assert.Empty(result.Errors);
        var snapshot = Assert.IsType<EcologyInventorySnapshot>(result.Snapshot);
        Assert.Equal(4, snapshot.Species.Count);
        Assert.Equal(2, snapshot.Habitats.Count);
        Assert.Equal(4, snapshot.PopulationEntries.Count);
        Assert.Single(snapshot.WaterBodies);
        Assert.Contains(snapshot.Species, item =>
            item.Domain == "TerrestrialAnimal" && item.ModelIdentifiers.Contains("a_c_deer"));
        Assert.Contains(snapshot.Species, item =>
            item.Domain == "AquaticAnimal" && item.ModelIdentifiers.Contains("a_c_shark"));
        Assert.Contains(snapshot.Species, item =>
            item.Domain == "TerrestrialPlant" && item.ModelIdentifiers.Contains("prop_tree_oak"));
        Assert.Contains(snapshot.Species, item =>
            item.Domain == "AquaticPlant" && item.ModelIdentifiers.Contains("prop_seaweed_kelp"));
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("not proof of exhaustive coverage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNormalizedSnapshot_RejectsAnimalEvidenceMappedToPlantDomain()
    {
        var service = CreateService();
        var workspace = CreateValidWorkspace();
        workspace.Candidates.Single(item => item.CanonicalId == "deer")
            .Category = "Plant";

        var result = service.BuildNormalizedSnapshot(workspace);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error =>
            error.Contains("deer", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("confirmed matching candidate evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNormalizedSnapshot_RejectsUnreviewedWorkspace()
    {
        var service = CreateService();
        var workspace = CreateValidWorkspace();
        workspace.HumanReviewed = false;

        var result = service.BuildNormalizedSnapshot(workspace);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error =>
            error.Contains("reviewed the candidate evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNormalizedSnapshot_RejectsInvalidSpeciesDefinitionJson()
    {
        var service = CreateService();
        var workspace = CreateValidWorkspace();
        workspace.Species[0].BehaviourDefinitionJson = "{not-json}";

        var result = service.BuildNormalizedSnapshot(workspace);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error =>
            error.Contains("invalid definition JSON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNormalizedSnapshot_RejectsCandidateEvidenceThatIsNotConfirmed()
    {
        var service = CreateService();
        var workspace = CreateValidWorkspace();
        workspace.Candidates.Single(item => item.CandidateIndex == 0).ReviewDecision = "Rejected";

        var result = service.BuildNormalizedSnapshot(workspace);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error =>
            error.Contains("deer", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("confirmed matching candidate evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNormalizedSnapshot_RejectsMissingAquaticPlantDomain()
    {
        var service = CreateService();
        var workspace = CreateValidWorkspace();
        workspace.Species.RemoveAll(item => item.Domain == "AquaticPlant");
        workspace.PopulationEntries.RemoveAll(item => item.SpeciesId == "kelp");

        var result = service.BuildNormalizedSnapshot(workspace);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error =>
            error.Contains("each terrestrial/aquatic", StringComparison.OrdinalIgnoreCase));
    }

    private static EcologyInventoryCurationService CreateService()
    {
        var root = Path.Combine(Path.GetTempPath(), "GtaHubCurationTests", Guid.NewGuid().ToString("N"));
        return new EcologyInventoryCurationService(
            dataRoot: Path.Combine(root, "rage-cli"),
            evidenceRoot: Path.Combine(root, "candidate-analysis"),
            draftRoot: Path.Combine(root, "drafts"));
    }

    private static EcologyInventoryCurationWorkspace CreateValidWorkspace()
    {
        var candidates = new List<EcologyCurationCandidateRow>
        {
            Candidate(0, "Animal", "a_c_deer", "deer"),
            Candidate(1, "Animal", "a_c_shark", "shark"),
            Candidate(2, "Plant", "prop_tree_oak", "oak"),
            Candidate(3, "Plant", "prop_seaweed_kelp", "kelp"),
            Candidate(4, "Habitat", "forest", "forest"),
            Candidate(5, "Habitat", "ocean", "ocean"),
            Candidate(6, "Population", "deer-population", "deer-pop"),
            Candidate(7, "Population", "shark-population", "shark-pop"),
            Candidate(8, "Population", "oak-population", "oak-pop"),
            Candidate(9, "Population", "kelp-population", "kelp-pop"),
            Candidate(10, "Water", "sea-water-body", "sea")
        };

        return new EcologyInventoryCurationWorkspace
        {
            GameBuild = "1.0.3095.0",
            GameExecutableSha256 = GameHash,
            CandidateAnalysisReportFileName = "ecology-candidates-test.json",
            CandidateAnalysisReportSha256 = ArchiveHash,
            SourceArchiveSha256 = new List<string> { ArchiveHash },
            HumanReviewed = true,
            ReviewNotes = "Reviewed candidate fields against the exact-build source metadata and linked records.",
            Candidates = candidates,
            Species = new List<EcologySpeciesCurationRow>
            {
                Species("deer", "TerrestrialAnimal", "a_c_deer", "forest", "0"),
                Species("shark", "AquaticAnimal", "a_c_shark", "ocean", "1"),
                Species("oak", "TerrestrialPlant", "prop_tree_oak", "forest", "2"),
                Species("kelp", "AquaticPlant", "prop_seaweed_kelp", "ocean", "3")
            },
            Habitats = new List<EcologyHabitatCurationRow>
            {
                new()
                {
                    Id = "forest",
                    HabitatType = "TerrestrialForest",
                    DefinitionJson = """{"source":"reviewed-terrestrial-habitat"}""",
                    EvidenceCandidateIndexes = "4"
                },
                new()
                {
                    Id = "ocean",
                    HabitatType = "Marine",
                    WaterBodyId = "sea",
                    DefinitionJson = """{"source":"reviewed-aquatic-habitat"}""",
                    EvidenceCandidateIndexes = "5"
                }
            },
            PopulationEntries = new List<EcologyPopulationCurationRow>
            {
                Population("deer-pop", "deer", "forest", 6),
                Population("shark-pop", "shark", "ocean", 7),
                Population("oak-pop", "oak", "forest", 8),
                Population("kelp-pop", "kelp", "ocean", 9)
            },
            WaterBodies = new List<EcologyWaterBodyCurationRow>
            {
                new()
                {
                    Id = "sea",
                    WaterType = "Marine",
                    SimulationProfileId = "sea-simulation",
                    InteractionProfileId = "sea-interaction",
                    RenderingProfileId = "sea-rendering",
                    SimulationDefinitionJson = """{"source":"reviewed-water-simulation"}""",
                    InteractionDefinitionJson = """{"source":"reviewed-water-interaction"}""",
                    RenderingDefinitionJson = """{"source":"reviewed-water-rendering"}""",
                    EvidenceCandidateIndexes = "10"
                }
            }
        };
    }

    private static EcologyCurationCandidateRow Candidate(
        int index,
        string category,
        string value,
        string canonicalId) => new()
    {
        CandidateIndex = index,
        Category = category,
        CandidateValue = value,
        ResourceName = category == "Water" ? "water.xml" : "peds.ymt",
        InternalArchivePath = category == "Water"
            ? "common/data/levels/gta5/water.xml"
            : "update.rpf/x64/data/peds.ymt",
        JsonPointer = "$/reviewed/" + index,
        FieldName = "Name",
        SourceArchivePath = @"C:\Games\GTA V\update.rpf",
        SourceArchiveSha256 = ArchiveHash,
        DumpJsonSha256 = DumpHash,
        EvidenceNote = "Human-reviewed exact-build source record.",
        ReviewDecision = "Confirmed",
        CanonicalId = canonicalId,
        ReviewerNotes = "Checked against source definition."
    };

    private static EcologySpeciesCurationRow Species(
        string id,
        string domain,
        string model,
        string habitat,
        string evidenceIndex) => new()
    {
        Id = id,
        Domain = domain,
        ModelIdentifiers = model,
        HabitatIds = habitat,
        BehaviourProfileId = "behaviour-" + id,
        BehaviourDefinitionJson = """{"reviewed":true,"source":"exact-build"}""",
        EvidenceCandidateIndexes = evidenceIndex
    };

    private static EcologyPopulationCurationRow Population(
        string id,
        string speciesId,
        string habitatId,
        int evidenceIndex) => new()
    {
        Id = id,
        SpeciesId = speciesId,
        HabitatId = habitatId,
        DefinitionJson = """{"distribution":"reviewed"}""",
        EvidenceCandidateIndexes = evidenceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}
