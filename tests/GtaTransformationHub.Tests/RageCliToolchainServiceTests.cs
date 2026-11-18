using System.Security.Cryptography;
using System.Text;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class RageCliToolchainServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubRageCliTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PinnedReleaseMetadata_IsAValidSha256AndPublicReleaseReference()
    {
        Assert.Equal(64, RageCliToolchainService.PinnedSha256.Length);
        Assert.Equal(
            "B7A9AA1D6196632544889CDC97481DB001936DADC8E046784EADEB38ACD3B1AC",
            RageCliToolchainService.PinnedSha256);
        Assert.All(RageCliToolchainService.PinnedSha256, character =>
            Assert.True(Uri.IsHexDigit(character)));
        Assert.Equal("0.28.1", RageCliToolchainService.PinnedVersion);
        Assert.StartsWith("https://github.com/", RageCliToolchainService.ReleaseUrl);
        Assert.StartsWith("https://github.com/", RageCliToolchainService.WindowsAssetUrl);
    }

    [Fact]
    public void ValidateExecutablePath_RequiresOfficialWindowsAssetNameAndExistingFile()
    {
        Directory.CreateDirectory(_root);
        var validPath = Path.Combine(_root, "rage-windows-x86_64.exe");
        File.WriteAllText(validPath, "test executable fixture");

        Assert.Equal(
            Path.GetFullPath(validPath),
            RageCliToolchainService.ValidateExecutablePath(validPath));

        var renamedPath = Path.Combine(_root, "rage.exe");
        File.WriteAllText(renamedPath, "test executable fixture");
        Assert.Throws<InvalidDataException>(() =>
            RageCliToolchainService.ValidateExecutablePath(renamedPath));
    }

    [Fact]
    public async Task ComputeSha256Async_ReturnsTheStandardDigest()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "hash-fixture.txt");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("abc"));

        var digest = await RageCliToolchainService.ComputeSha256Async(path);

        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("abc"))),
            digest);
    }

    [Fact]
    public void BuildDirectoryNameFromExecutableHash_IsStableAndBuildSpecific()
    {
        var first = new string('A', 64);
        var second = "B" + new string('A', 63);

        Assert.Equal("aaaaaaaaaaaaaaaa", RageCliToolchainService.BuildDirectoryNameFromExecutableHash(first));
        Assert.NotEqual(
            RageCliToolchainService.BuildDirectoryNameFromExecutableHash(first),
            RageCliToolchainService.BuildDirectoryNameFromExecutableHash(second));
        Assert.Throws<ArgumentException>(() =>
            RageCliToolchainService.BuildDirectoryNameFromExecutableHash("not-a-sha256"));
    }

    [Fact]
    public void BuildCatalogueArguments_UsesExplicitPathsAndDisablesBackgroundUpdateChecks()
    {
        var gameRoot = Path.Combine(_root, "Grand Theft Auto V");
        var exe = Path.Combine(gameRoot, "GTA5.exe");
        var database = Path.Combine(_root, "GTA Hub", "catalog.sqlite");
        var report = Path.Combine(_root, "GTA Hub", "catalog report.json");

        var arguments = RageCliToolchainService.BuildCatalogueArguments(
            exe, gameRoot, database, report);

        Assert.Equal("--no-update-check", arguments[0]);
        Assert.Equal("--exe", arguments[1]);
        Assert.Equal(Path.GetFullPath(exe), arguments[2]);
        Assert.Equal("catalog", arguments[3]);
        Assert.Equal("--db", arguments[4]);
        Assert.Equal(Path.GetFullPath(database), arguments[5]);
        Assert.Contains("--full", arguments);
        Assert.Contains("--strict", arguments);
        Assert.Equal(Path.GetFullPath(report), arguments[^1]);
    }

    [Fact]
    public async Task ProcessRunner_UsesArgumentListAndCapturesOutputWithoutShellExecution()
    {
        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(commandInterpreter));

        var result = await new RageCliProcessRunner().RunAsync(
            commandInterpreter!,
            new[] { "/d", "/c", "echo rage 0.28.1" },
            TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("rage 0.28.1", result.StandardOutput);
        Assert.False(result.StandardOutputTruncated);
        Assert.False(result.StandardErrorTruncated);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
