using System.Text;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class RageCliNamesHarvestServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubNamesHarvestTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildHarvestArguments_UsesPinnedGameAndExplicitOutput()
    {
        var exe = Path.Combine(_root, "GTA5.exe");
        var output = Path.Combine(_root, "rage names", "names.txt");

        var args = RageCliNamesHarvestService.BuildHarvestArguments(exe, output);

        Assert.Equal(new[]
        {
            "--no-update-check",
            "--exe", Path.GetFullPath(exe),
            "names", "harvest",
            "--output", Path.GetFullPath(output)
        }, args);
    }

    [Fact]
    public void ParseBuildNumber_UsesGtaVBuildComponent()
    {
        Assert.Equal(3889, RageCliNamesHarvestService.ParseBuildNumber("1.0.3889.0"));
        Assert.Throws<InvalidDataException>(() => RageCliNamesHarvestService.ParseBuildNumber("unknown"));
    }

    [Fact]
    public async Task ReadNamesFileSummary_CountsNamesAndReadsBuildHeader()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "names.txt");
        await File.WriteAllTextAsync(path,
            "# Names for rage\n# source: harvest GTA5.exe\n# build: 3889\nprop_animal_deer\nA_C_FISH\nprop_tree_pine\n",
            Encoding.UTF8);

        var summary = await RageCliNamesHarvestService.ReadNamesFileSummaryAsync(path);

        Assert.Equal(3, summary.NameCount);
        Assert.Equal(3889, summary.BuildCoverage);
    }

    [Fact]
    public async Task ReadNamesFileSummary_RejectsInvalidIdentifiers()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "names.txt");
        await File.WriteAllTextAsync(path, "# build: 3889\n../unsafe\n");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RageCliNamesHarvestService.ReadNamesFileSummaryAsync(path));
    }

    [Fact]
    public async Task ReadNamesFileSummary_RejectsMissingOrEmptyFile()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "names.txt");
        await File.WriteAllTextAsync(path, "");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RageCliNamesHarvestService.ReadNamesFileSummaryAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
