using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class WaterXmlInventoryParserServiceTests
{
    private const string WaterXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <WaterData>
          <WaterQuads>
            <Item>
              <minX value="-1592" />
              <maxX value="-1304" />
              <minY value="-1744" />
              <maxY value="-1624" />
              <Type value="0" />
              <IsInvisible value="false" />
              <HasLimitedDepth value="false" />
              <z value="0.0" />
              <a1 value="26" />
              <a2 value="26" />
              <a3 value="26" />
              <a4 value="26" />
              <NoStencil value="false" />
            </Item>
          </WaterQuads>
          <CalmingQuads>
            <Item>
              <minX value="1752" />
              <maxX value="2076" />
              <minY value="216" />
              <maxY value="800" />
              <fDampening value="0.05" />
            </Item>
          </CalmingQuads>
          <WaveQuads>
            <Item>
              <minX value="1664" />
              <maxX value="1988" />
              <minY value="-120" />
              <maxY value="132" />
              <Amplitude value="0.1" />
              <XDirection value="-0.603208" />
              <YDirection value="-0.797584" />
            </Item>
          </WaveQuads>
        </WaterData>
        """;

    [Fact]
    public void Parse_ExtractsWaterSurfaceCalmingAndWaveDefinitions()
    {
        var inventory = WaterXmlInventoryParserService.Parse("water.xml", WaterXml);

        Assert.True(inventory.HasAllRequiredSections);
        var water = Assert.Single(inventory.WaterQuads);
        Assert.Equal(-1592f, water.MinX);
        Assert.Equal(-1304f, water.MaxX);
        Assert.Equal(-1744f, water.MinY);
        Assert.Equal(-1624f, water.MaxY);
        Assert.Equal(0, water.Type);
        Assert.False(water.IsInvisible);
        Assert.False(water.HasLimitedDepth);
        Assert.Equal(0f, water.Z);
        Assert.Equal([26f, 26f, 26f, 26f], new[] { water.Alpha1, water.Alpha2, water.Alpha3, water.Alpha4 });
        Assert.False(water.NoStencil);

        var calming = Assert.Single(inventory.CalmingQuads);
        Assert.Equal(0.05f, calming.Dampening);
        var wave = Assert.Single(inventory.WaveQuads);
        Assert.Equal(0.1f, wave.Amplitude);
        Assert.Equal(-0.603208f, wave.XDirection);
        Assert.Equal(-0.797584f, wave.YDirection);
    }

    [Fact]
    public void Parse_RequiresKnownFilenameAndWaterDataRoot()
    {
        Assert.Throws<InvalidDataException>(() =>
            WaterXmlInventoryParserService.Parse("map.xml", WaterXml));
        Assert.Throws<InvalidDataException>(() =>
            WaterXmlInventoryParserService.Parse("water.xml", "<Unexpected/>"));
    }

    [Fact]
    public void Parse_RejectsMissingOrInvalidFieldsInsteadOfDefaultingToZero()
    {
        var missing = WaterXml.Replace("<NoStencil value=\"false\" />", string.Empty, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() =>
            WaterXmlInventoryParserService.Parse("water.xml", missing));

        var invalid = WaterXml.Replace("<Amplitude value=\"0.1\" />", "<Amplitude value=\"NaN\" />", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() =>
            WaterXmlInventoryParserService.Parse("water.xml", invalid));
    }

    [Fact]
    public void Parse_RejectsInvertedBoundsAndExternalEntities()
    {
        var inverted = WaterXml.Replace("minX value=\"-1592\"", "minX value=\"-1000\"", StringComparison.Ordinal)
            .Replace("maxX value=\"-1304\"", "maxX value=\"-1305\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() =>
            WaterXmlInventoryParserService.Parse("water.xml", inverted));

        const string external = """
            <!DOCTYPE WaterData [<!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini">]>
            <WaterData>&xxe;</WaterData>
            """;
        Assert.ThrowsAny<Exception>(() =>
            WaterXmlInventoryParserService.Parse("water.xml", external));
    }

    [Fact]
    public void CompareRequiredWaterFiles_DetectsAnyWaterInteractionOrWaveChange()
    {
        var baseWater = WaterXmlInventoryParserService.Parse("water.xml", WaterXml);
        var baseIsland = WaterXmlInventoryParserService.Parse("water_heistisland.xml", WaterXml);
        var changed = baseWater with
        {
            WaveQuads = baseWater.WaveQuads.Select(wave => wave with { Amplitude = 0.2f }).ToArray()
        };

        var pass = WaterXmlInventoryParserService.CompareRequiredWaterFiles(
            new[] { baseWater, baseIsland },
            new[] { baseWater, baseIsland });
        Assert.True(pass.VanillaWaterDefinitionsPreserved);

        var fail = WaterXmlInventoryParserService.CompareRequiredWaterFiles(
            new[] { baseWater, baseIsland },
            new[] { changed, baseIsland });
        Assert.False(fail.VanillaWaterDefinitionsPreserved);
        Assert.Contains(fail.Errors, error => error.Contains("wave regions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompareRequiredWaterFiles_RequiresBothNativeWaterFilesExactlyOnce()
    {
        var baseline = WaterXmlInventoryParserService.Parse("water.xml", WaterXml);
        var result = WaterXmlInventoryParserService.CompareRequiredWaterFiles(
            new[] { baseline },
            new[] { baseline, baseline });
        Assert.False(result.VanillaWaterDefinitionsPreserved);
        Assert.Contains(result.Errors, error => error.Contains("missing water_heistisland.xml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("multiple records", StringComparison.OrdinalIgnoreCase));
    }
}
