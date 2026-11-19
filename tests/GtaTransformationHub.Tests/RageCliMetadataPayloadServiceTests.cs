using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class RageCliMetadataPayloadServiceTests
{
    [Fact]
    public void BuildNestedArchiveExtractionPlan_ResolvesEachNestedRpfLayer()
    {
        var plan = RageCliMetadataPayloadService.BuildNestedArchiveExtractionPlan(
            "x64v.rpf/levels/gta5/props/props.rpf/dlc/content.rpf/data/popgroups.ymt",
            "x64v.rpf");

        Assert.Equal(
            new[]
            {
                "levels/gta5/props/props.rpf",
                "dlc/content.rpf"
            },
            plan.NestedArchivePaths);
        Assert.Equal("data/popgroups.ymt", plan.ResourcePath);
    }

    [Fact]
    public void BuildNestedArchiveExtractionPlan_HandlesDirectResourcesWithoutNestedRpf()
    {
        var plan = RageCliMetadataPayloadService.BuildNestedArchiveExtractionPlan(
            "update.rpf/common/data/popgroups.ymt",
            "update.rpf");

        Assert.Empty(plan.NestedArchivePaths);
        Assert.Equal("common/data/popgroups.ymt", plan.ResourcePath);
    }

    [Fact]
    public void BuildNestedArchiveExtractionPlan_RejectsMissingPathArguments()
    {
        Assert.Throws<ArgumentException>(() =>
            RageCliMetadataPayloadService.BuildNestedArchiveExtractionPlan("", "x64v.rpf"));
        Assert.Throws<ArgumentException>(() =>
            RageCliMetadataPayloadService.BuildNestedArchiveExtractionPlan("x64v.rpf/file.ymt", ""));
    }

    [Theory]
    [InlineData("../outside.rpf/popgroups.ymt")]
    [InlineData("x64v.rpf/../outside.ymt")]
    [InlineData("x64v.rpf/C:/outside.ymt")]
    [InlineData("other.rpf/path/popgroups.ymt")]
    [InlineData("x64v.rpf/levels/nested.rpf")]
    public void BuildNestedArchiveExtractionPlan_RejectsUnsafeOrIncompletePaths(string path)
    {
        Assert.Throws<InvalidDataException>(() =>
            RageCliMetadataPayloadService.BuildNestedArchiveExtractionPlan(path, "x64v.rpf"));
    }

    [Fact]
    public void BuildNestedArchiveExtractionArguments_UsesOnlySpecificNestedArchivePath()
    {
        var args = RageCliMetadataPayloadService.BuildNestedArchiveExtractionArguments(
            @"C:\Games\GTA V\GTA5.exe",
            @"C:\Games\GTA V\x64v.rpf",
            "levels/gta5/props.rpf",
            @"C:\Users\Test\AppData\Local\staging");

        Assert.Equal("--no-update-check", args[0]);
        Assert.Equal("--exe", args[1]);
        Assert.Equal("extract", args[3]);
        Assert.Equal(Path.GetFullPath(@"C:\Games\GTA V\x64v.rpf"), args[4]);
        Assert.Equal("levels/gta5/props.rpf", args[5]);
        Assert.Equal("--output", args[6]);
        Assert.Equal(Path.GetFullPath(@"C:\Users\Test\AppData\Local\staging"), args[7]);
        Assert.DoesNotContain("--recursive", args);
    }

    [Fact]
    public void BuildResourceDumpArguments_AddsVerifiedNameDictionaryWhenProvided()
    {
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GtaHubRageNamesArgs"));
        var namesPath = Path.Combine(Path.GetTempPath(), "GtaHubRageNamesArgs", "names.txt");
        File.WriteAllText(namesPath, "# build: 3889\nprop_animal_deer\n");

        try
        {
            var args = RageCliMetadataPayloadService.BuildResourceDumpArguments(
                @"C:\Games\GTA V\GTA5.exe",
                @"C:\Users\Test\AppData\Local\nested.rpf",
                "common/data/popgroups.ymt",
                @"C:\Users\Test\AppData\Local\popgroups.json",
                namesPath);

            var namesIndex = Array.IndexOf(args.ToArray(), "--names");
            Assert.True(namesIndex >= 0);
            Assert.Equal(Path.GetFullPath(namesPath), args[namesIndex + 1]);
            Assert.Equal("--output", args[^2]);
        }
        finally
        {
            File.Delete(namesPath);
        }
    }

    [Fact]
    public void BuildResourceDumpArguments_RejectsMissingOrRedirectedNameDictionary()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "names.txt");
        Assert.Throws<InvalidDataException>(() =>
            RageCliMetadataPayloadService.BuildResourceDumpArguments(
                @"C:\Games\GTA V\GTA5.exe",
                @"C:\Users\Test\nested.rpf",
                "common/data/popgroups.ymt",
                @"C:\Users\Test\popgroups.json",
                missing));
    }

    [Fact]
    public void BuildResourceDumpArguments_UsesArchiveAndJsonOutput()
    {
        var args = RageCliMetadataPayloadService.BuildResourceDumpArguments(
            @"C:\Games\GTA V\GTA5.exe",
            @"C:\Users\Test\AppData\Local\nested.rpf",
            "common/data/popgroups.ymt",
            @"C:\Users\Test\AppData\Local\popgroups.json");

        Assert.Equal("--no-update-check", args[0]);
        Assert.Equal("--exe", args[1]);
        Assert.Equal("resource", args[3]);
        Assert.Equal("dump", args[4]);
        Assert.Equal("common/data/popgroups.ymt", args[5]);
        Assert.Equal("--archive", args[6]);
        Assert.Equal(Path.GetFullPath(@"C:\Users\Test\AppData\Local\nested.rpf"), args[7]);
        Assert.Contains("--json", args);
        Assert.Equal("--output", args[^2]);
        Assert.Equal(Path.GetFullPath(@"C:\Users\Test\AppData\Local\popgroups.json"), args[^1]);
    }
}
