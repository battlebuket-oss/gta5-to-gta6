using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class GameInstallationStoreServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubInstallationStoreTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_PersistsExecutablePathBuildAndActualSha256()
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "GTA5.exe");
        var bytes = Enumerable.Range(0, 4096).Select(index => (byte)(index % 256)).ToArray();
        await File.WriteAllBytesAsync(executable, bytes);
        var storePath = Path.Combine(_root, "settings", "game-installation.json");
        var service = new GameInstallationStoreService(storePath);
        var installation = new GameInstallation(
            _root, executable, "1.0.3889.0", "1.0.3889.0", "1.0.3889.0");

        await service.SaveAsync(installation);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(storePath));
        var root = document.RootElement;
        Assert.Equal(Path.GetFullPath(executable), root.GetProperty("executablePath").GetString());
        Assert.Equal("1.0.3889.0", root.GetProperty("buildVersion").GetString());
        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expectedHash, root.GetProperty("executableSha256").GetString());
    }

    [Fact]
    public async Task SaveAsync_RejectsNonNumericBuildVersion()
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "GTA5.exe");
        await File.WriteAllTextAsync(executable, "not a PE executable");
        var service = new GameInstallationStoreService(Path.Combine(_root, "game-installation.json"));
        var installation = new GameInstallation(
            _root, executable, "unknown", "unknown", "unknown");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(installation));
        Assert.False(File.Exists(Path.Combine(_root, "game-installation.json")));
    }

    [Fact]
    public async Task SaveAsync_RejectsExecutableOutsideDeclaredGameRoot()
    {
        Directory.CreateDirectory(_root);
        var gameDirectory = Path.Combine(_root, "game");
        var otherDirectory = Path.Combine(_root, "other");
        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(otherDirectory);
        var executable = Path.Combine(otherDirectory, "GTA5.exe");
        await File.WriteAllTextAsync(executable, "not a PE executable");
        var service = new GameInstallationStoreService(Path.Combine(_root, "game-installation.json"));
        var installation = new GameInstallation(
            gameDirectory, executable, "1.0.3889.0", "1.0.3889.0", "1.0.3889.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(installation));
    }

    [Fact]
    public async Task RestoreAsync_ReturnsClearStateWhenNoInstallationWasSaved()
    {
        var service = new GameInstallationStoreService(Path.Combine(_root, "missing.json"));
        var validator = new GameInstallationService();

        var result = await service.RestoreAsync(validator);

        Assert.False(result.Restored);
        Assert.Null(result.Installation);
        Assert.Contains("No previously validated", result.Message);
    }

    [Fact]
    public async Task RestoreAsync_RejectsMalformedSavedRecordWithoutThrowing()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "game-installation.json");
        await File.WriteAllTextAsync(path, "{ invalid json");
        var service = new GameInstallationStoreService(path);

        var result = await service.RestoreAsync(new GameInstallationService());

        Assert.False(result.Restored);
        Assert.Null(result.Installation);
        Assert.Contains("could not be revalidated", result.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
