using System.Text.Json;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class RageCliJsonStructureAnalyzerTests
{
    [Fact]
    public void Analyze_CountsJsonNodesAndPropertyNames()
    {
        using var document = JsonDocument.Parse("""
        {
          "species": [
            { "name": "deer", "hash": 123, "enabled": true },
            { "name": "fish", "hash": null, "enabled": false }
          ],
          "water": { "surface": "ocean" }
        }
        """);

        var result = RageCliJsonStructureAnalyzer.Analyze(document.RootElement);

        Assert.Equal(4, result.ObjectCount);
        Assert.Equal(1, result.ArrayCount);
        Assert.Equal(9, result.PropertyCount);
        Assert.Equal(3, result.StringValueCount);
        Assert.Equal(1, result.NumberValueCount);
        Assert.Equal(2, result.BooleanValueCount);
        Assert.Equal(1, result.NullValueCount);
        Assert.Equal(2, result.PropertyNameFrequency["name"]);
        Assert.Equal(2, result.PropertyNameFrequency["hash"]);
        Assert.False(result.PropertyNameLimitReached);
        Assert.Equal(0, result.TruncatedPropertyNameCount);
    }

    [Fact]
    public void Analyze_EnforcesUniquePropertyNameLimitWithoutLosingTotalCount()
    {
        var json = "{" + string.Join(",", Enumerable.Range(0, 5000)
            .Select(index => "\"" + "field" + index + "\":" + index)) + "}";
        using var document = JsonDocument.Parse(json);

        var result = RageCliJsonStructureAnalyzer.Analyze(document.RootElement);

        Assert.Equal(5000, result.PropertyCount);
        Assert.Equal(RageCliJsonStructureAnalyzer.MaximumUniquePropertyNames,
            result.PropertyNameFrequency.Count);
        Assert.True(result.PropertyNameLimitReached);
    }


    [Fact]
    public void Analyze_TruncatesExtremelyLongPropertyNames()
    {
        var propertyName = new string('x', 1024);
        using var document = JsonDocument.Parse(
            "{\\\"" + propertyName + "\\\": 1}");

        var result = RageCliJsonStructureAnalyzer.Analyze(document.RootElement);

        Assert.Equal(1, result.TruncatedPropertyNameCount);
        Assert.Contains("...[truncated]", Assert.Single(result.PropertyNameFrequency.Keys));
    }
\n    [Fact]
    public void Analyze_RejectsJsonBeyondMaximumDepth()
    {
        using var document = JsonDocument.Parse(
            "[" + string.Concat(Enumerable.Repeat("[", 140)) + "0" +
            string.Concat(Enumerable.Repeat("]", 140)) + "]",
            new JsonDocumentOptions { MaxDepth = 256 });

        Assert.Throws<InvalidDataException>(() =>
            RageCliJsonStructureAnalyzer.Analyze(document.RootElement));
    }
}
