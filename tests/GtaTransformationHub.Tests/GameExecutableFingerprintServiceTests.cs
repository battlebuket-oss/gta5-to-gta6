using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class GameExecutableFingerprintServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubFingerprintTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ComputeSha256Async_ReturnsStableLowercaseFingerprint()
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "GTA5.exe");
        await File.WriteAllBytesAsync(executable, Enumerable.Range(0, 2048).Select(value => (byte)(value % 256)).ToArray());

        var first = await GameExecutableFingerprintService.ComputeSha256Async(executable);
        var second = await GameExecutableFingerprintService.ComputeSha256Async(executable);

        Assert.Equal(64, first.Length);
        Assert.Equal(first, first.ToLowerInvariant());
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ComputeSha256Async_RejectsAnyFileOtherThanGta5Exe()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "not-the-game.exe");
        await File.WriteAllTextAsync(file, "test");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            GameExecutableFingerprintService.ComputeSha256Async(file));
    }

    [Fact]
    public async Task ComputeSha256Async_RejectsMissingExecutable()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            GameExecutableFingerprintService.ComputeSha256Async(Path.Combine(_root, "GTA5.exe")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
