using System.IO.Compression;
using System.Text;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class EcologyAssetClassificationTests
{
    [Theory]
    [InlineData("x64/models/cdimages/streamedpeds_animals.rpf/animal.ytd")]
    [InlineData("update/x64/dlcpacks/custom/vegetation.ytyp")]
    [InlineData("mods/visuals/foliage.ydr")]
    [InlineData("update/update.rpf/meta/popgroups.ymt")]
    [InlineData("mods/aquatic/underwater/seagrass.ytd")]
    [InlineData("mods/marine/fish/shark.yft")]
    [InlineData("mods/water/ocean/water_material.ytd")]
    [InlineData("mods/coastal/reef/coral_reef.ymap")]
    [InlineData("mods/river/aquatic_plants/reeds.ydr")]
    public void GameAssetWithoutEcologyContract_IsBlocked(string destination)
    {
        var manifest = new PackageManifest(
            1, "asset-test", "Asset Test", "Test Author", "https://example.com",
            "Test-only fixture licence", "1.0.0", "GTA V Legacy",
            "1.0.1000.0", "1.0.9999.0", Array.Empty<string>(), Array.Empty<string>(),
            new[] { new PackageManifestFile("payload/file.bin", destination, new string('A', 64), 1) });

        Assert.NotEmpty(EcologyCompatibilityService.Validate(manifest));
    }
}
