using System.IO.Compression;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class OpenRageToolchainServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubOpenRageTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PinnedReleaseMetadata_IsValidAndUsesOfficialUpstreamRelease()
    {
        Assert.Equal(64, OpenRageToolchainService.WindowsAssetSha256.Length);
        Assert.All(OpenRageToolchainService.WindowsAssetSha256, value => Assert.True(Uri.IsHexDigit(value)));
        Assert.Equal("0.4.0", OpenRageToolchainService.PinnedVersion);
        Assert.StartsWith("https://github.com/Two-Play/openrage/releases/tag/", OpenRageToolchainService.ReleaseUrl);
        Assert.StartsWith("https://github.com/Two-Play/openrage/releases/download/", OpenRageToolchainService.WindowsAssetUrl);
    }

    [Theory]
    [InlineData("openrage 0.4.0", true)]
    [InlineData("OpenRAGE 0.4.0", true)]
    [InlineData("openrage 0.3.0", false)]
    [InlineData("rage 0.4.0", false)]
    [InlineData("", false)]
    public void IsPinnedVersionOutput_AcceptsOnlyThePinnedOpenRageRelease(string output, bool expected)
    {
        Assert.Equal(expected, OpenRageToolchainService.IsPinnedVersionOutput(output));
    }

    [Fact]
    public void BuildOivArguments_UseExplicitGameAndPackagePaths()
    {
        var root = Path.Combine(_root, "Grand Theft Auto V");
        var oiv = Path.Combine(_root, "GTA VI HUD.oiv");

        Assert.Equal(
            new[] { "--game", Path.GetFullPath(root), "oiv", "info", Path.GetFullPath(oiv) },
            OpenRageToolchainService.BuildOivInfoArguments(root, oiv));
        Assert.Equal(
            new[] { "--game", Path.GetFullPath(root), "oiv", "install", Path.GetFullPath(oiv), "--yes" },
            OpenRageToolchainService.BuildOivInstallArguments(root, oiv));
        Assert.Equal(
            new[] { "--game", Path.GetFullPath(root), "oiv", "list" },
            OpenRageToolchainService.BuildOivListArguments(root));
        Assert.Equal(
            new[] { "--game", Path.GetFullPath(root), "oiv", "uninstall", "My Mod 1.0", "--yes" },
            OpenRageToolchainService.BuildOivUninstallArguments(root, "My Mod 1.0"));
        Assert.Equal(
            new[] { "--game", Path.GetFullPath(root), "undo" },
            OpenRageToolchainService.BuildUndoArguments(root));
    }

    [Fact]
    public void BuildGameKeysArguments_UsesTheSelectedLegacyInstallation()
    {
        var root = Path.Combine(_root, "Grand Theft Auto V");
        Assert.Equal(
            new[] { "--game", Path.GetFullPath(root), "keys" },
            OpenRageToolchainService.BuildGameKeysArguments(root));
    }

    [Fact]
    public void ParseLinesWithPrefix_ReturnsOnlyBlockersAndWarnings()
    {
        const string output = """
            Test Mod 1.0 by Author (format 2.2)
              Add     update/update.rpf/common/data/new.dat
              warning: optional file not found
              BLOCKED: PSO/META edits are not supported yet
            """;

        Assert.Single(OpenRageToolchainService.ParseLinesWithPrefix(output, "BLOCKED:"));
        Assert.Single(OpenRageToolchainService.ParseLinesWithPrefix(output, "warning:"));
    }

    [Fact]
    public void ParseGameFolderChanges_DetectsLooseGameFolderAndExecutableChanges()
    {
        const string output = """
              Add      Trainer.asi  (new file in the game folder)
              Add      update/update.rpf/common/data/water.xml  (in the mods folder)
              Add      scripts/Unknown.dll  (new file in the game folder)
            """;

        var changes = OpenRageToolchainService.ParseGameFolderChanges(output);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, line => line.Contains("Trainer.asi", StringComparison.Ordinal));
        Assert.Contains(changes, line => line.Contains("Unknown.dll", StringComparison.Ordinal));
        Assert.DoesNotContain(changes, line => line.Contains("water.xml", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatePackageGroupName_RejectsEmptyControlCharactersAndExcessiveLength()
    {
        Assert.Throws<ArgumentException>(() => OpenRageToolchainService.ValidatePackageGroupName(""));
        Assert.Throws<ArgumentException>(() => OpenRageToolchainService.ValidatePackageGroupName("Mod\nInjected"));
        Assert.Throws<ArgumentException>(() => OpenRageToolchainService.ValidatePackageGroupName(new string('a', 257)));
        OpenRageToolchainService.ValidatePackageGroupName("GTA VI HUD 1.3");
    }

    [Fact]
    public void ValidateOpenIvModsFolder_RequiresModsDirectoryAndOpenIvAsi()
    {
        var root = Path.Combine(_root, "game");
        Directory.CreateDirectory(root);

        Assert.Throws<InvalidDataException>(() =>
            OpenRageToolchainService.ValidateOpenIvModsFolder(root));

        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(mods);
        Assert.Throws<InvalidDataException>(() =>
            OpenRageToolchainService.ValidateOpenIvModsFolder(root));

        File.WriteAllBytes(Path.Combine(root, "OpenIV.asi"), new byte[] { 1, 2, 3 });
        OpenRageToolchainService.ValidateOpenIvModsFolder(root);
    }

    [Fact]
    public void ValidateOivPackagePath_RequiresExistingOivFileAndRejectsOtherFormats()
    {
        Directory.CreateDirectory(_root);
        var oiv = Path.Combine(_root, "mod.oiv");
        File.WriteAllBytes(oiv, new byte[] { 1, 2, 3 });
        var zip = Path.Combine(_root, "mod.zip");
        File.WriteAllBytes(zip, new byte[] { 1, 2, 3 });

        Assert.Equal(Path.GetFullPath(oiv), OpenRageToolchainService.ValidateOivPackagePath(oiv));
        Assert.Throws<InvalidDataException>(() => OpenRageToolchainService.ValidateOivPackagePath(zip));
        Assert.Throws<FileNotFoundException>(() =>
            OpenRageToolchainService.ValidateOivPackagePath(Path.Combine(_root, "missing.oiv")));
    }

    [Fact]
    public void FindCliExecutable_RequiresExactlyOneOpenRageExecutable()
    {
        var payload = Path.Combine(_root, "payload");
        Directory.CreateDirectory(payload);
        var nested = Path.Combine(payload, "bin");
        Directory.CreateDirectory(nested);
        var exe = Path.Combine(nested, "openrage.exe");
        File.WriteAllText(exe, "fixture");

        Assert.Equal(Path.GetFullPath(exe), OpenRageToolchainService.FindCliExecutable(payload));

        File.WriteAllText(Path.Combine(payload, "openrage.exe"), "duplicate");
        Assert.Throws<InvalidDataException>(() => OpenRageToolchainService.FindCliExecutable(payload));
    }

    [Fact]
    public void IsUnixSymbolicLink_RecognisesUnixSymlinkMode()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = unchecked((int)(0xA000u << 16));
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.True(OpenRageToolchainService.IsUnixSymbolicLink(Assert.Single(readArchive.Entries)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
