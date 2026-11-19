using System;
using System.IO;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class GtaViInspiredHudProfileAdapterTests
{
    [Fact]
    public void ParseInstalledGroups_ParsesExactNamesAndRecordedChangeCounts()
    {
        const string output = """
            GTA VI-Inspired HUD 2.8.2 (GTA V Legacy) 2.8 LEGACY 2.8.2  (431 recorded changes)
            Sea Life V 1.0  (12 recorded changes)
            """;

        var groups = GtaViInspiredHudProfileAdapter.ParseInstalledGroups(output);

        Assert.Equal(2, groups.Count);
        Assert.Equal(
            "GTA VI-Inspired HUD 2.8.2 (GTA V Legacy) 2.8 LEGACY 2.8.2",
            groups[0].Name);
        Assert.Equal(431, groups[0].RecordedChanges);
        Assert.Equal("Sea Life V 1.0", groups[1].Name);
        Assert.Equal(12, groups[1].RecordedChanges);
    }

    [Theory]
    [InlineData("")]
    [InlineData("No packages installed with OpenRAGE.")]
    [InlineData("  No packages installed with OpenRAGE.  ")]
    public void ParseInstalledGroups_HandlesEmptyJournal(string output)
    {
        Assert.Empty(GtaViInspiredHudProfileAdapter.ParseInstalledGroups(output));
    }

    [Fact]
    public void ParseInstalledGroups_FailsClosedOnUnknownOutput()
    {
        Assert.Throws<InvalidDataException>(() =>
            GtaViInspiredHudProfileAdapter.ParseInstalledGroups(
                "GTA VI HUD seems installed"));
    }

    [Fact]
    public void ParseInstalledGroups_RejectsDuplicateGroupNames()
    {
        const string output = """
            HUD 1.0  (3 recorded changes)
            HUD 1.0  (3 recorded changes)
            """;

        Assert.Throws<InvalidDataException>(() =>
            GtaViInspiredHudProfileAdapter.ParseInstalledGroups(output));
    }

    [Fact]
    public void InspectLoosePlugins_FindsTheTwoRequiredHudFilesInGameRootOrModsFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "GtaHudMarkerTests", Guid.NewGuid().ToString("N"));
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(mods);
        try
        {
            File.WriteAllText(Path.Combine(root, "ViMinimap.asi"), "minimap");
            File.WriteAllText(Path.Combine(mods, "ViPreload.asi"), "preload");

            var presence = GtaViInspiredHudProfileAdapter.InspectLoosePlugins(Installation(root));

            Assert.True(presence.BothPresent);
            Assert.Equal(2, presence.LocatedFiles.Count);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateJournalAndPluginPresence_RejectsManualFilesWithoutOpenRageGroup()
    {
        var presence = new HudLoosePluginPresence(
            true, true, new[] { @"C:\GTA5\ViMinimap.asi", @"C:\GTA5\ViPreload.asi" });

        Assert.Throws<InvalidDataException>(() =>
            GtaViInspiredHudProfileAdapter.ValidateJournalAndPluginPresence(
                null, presence));
    }

    [Fact]
    public void ValidateJournalAndPluginPresence_RejectsJournalGroupWithMissingRequiredPlugin()
    {
        var group = new OpenRageInstalledOivGroup("GTA VI HUD 2.8.2", 431);
        var presence = new HudLoosePluginPresence(
            true, false, new[] { @"C:\GTA5\ViMinimap.asi" });

        Assert.Throws<InvalidDataException>(() =>
            GtaViInspiredHudProfileAdapter.ValidateJournalAndPluginPresence(
                group, presence));
    }

    [Fact]
    public void ValidateJournalAndPluginPresence_RejectsDuplicatePluginsAcrossRootAndMods()
    {
        var group = new OpenRageInstalledOivGroup("GTA VI HUD 2.8.2", 431);
        var presence = new HudLoosePluginPresence(
            true, true,
            new[]
            {
                @"C:\\GTA5\\ViMinimap.asi",
                @"C:\\GTA5\\mods\\ViMinimap.asi",
                @"C:\\GTA5\\ViPreload.asi"
            });

        Assert.Throws<InvalidDataException>(() =>
            GtaViInspiredHudProfileAdapter.ValidateJournalAndPluginPresence(
                group, presence));
    }

    [Fact]
    public void ValidateJournalAndPluginPresence_RejectsEmptyOpenRageChangeJournal()
    {
        var group = new OpenRageInstalledOivGroup("GTA VI HUD 2.8.2", 0);
        var presence = new HudLoosePluginPresence(
            true, true, new[] { "ViMinimap.asi", "ViPreload.asi" });

        Assert.Throws<InvalidDataException>(() =>
            GtaViInspiredHudProfileAdapter.ValidateJournalAndPluginPresence(
                group, presence));
    }

    [Fact]
    public void ValidateJournalAndPluginPresence_AcceptsConsistentInstalledOrUninstalledState()
    {
        var group = new OpenRageInstalledOivGroup("GTA VI HUD 2.8.2", 431);

        GtaViInspiredHudProfileAdapter.ValidateJournalAndPluginPresence(
            group, new HudLoosePluginPresence(true, true, new[] { "ViMinimap.asi", "ViPreload.asi" }));
        GtaViInspiredHudProfileAdapter.ValidateJournalAndPluginPresence(
            null, new HudLoosePluginPresence(false, false, Array.Empty<string>()));
    }

    private static GameInstallation Installation(string root) =>
        new(root, Path.Combine(root, "GTA5.exe"), "1.0.0.0", "1.0.0.0", "1.0.0.0");

    [Fact]
    public void GetPackageGroupName_UsesManifestNameAndParsedVersionRatherThanCatalogueDisplayName()
    {
        var inspection = new OivManifestInspectionResult(
            "2.1",
            "{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}",
            "Five",
            "GTA VI-Inspired HUD 2.8.2 (GTA V Legacy)",
            "Geckomacabre x FBICat",
            "2.8 LEGACY 2.8.2",
            "LEGACY 2.8.2",
            "Unspecified",
            "Personal use",
            true,
            Array.Empty<OivOperationSummary>(),
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Equal(
            "GTA VI-Inspired HUD 2.8.2 (GTA V Legacy) 2.8 LEGACY 2.8.2",
            GtaViInspiredHudProfileAdapter.GetPackageGroupName(inspection));
    }
}
