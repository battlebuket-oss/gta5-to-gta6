using System.Security.Cryptography;

namespace GtaTransformationHub.Services;

/// <summary>
/// Computes the exact executable fingerprint used to bind manual compatibility and
/// Story Mode evidence to a particular GTA V Legacy installation.
/// </summary>
public static class GameExecutableFingerprintService
{
    public static async Task<string> ComputeSha256Async(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("A GTA5.exe path is required.", nameof(executablePath));

        var fullPath = Path.GetFullPath(executablePath);
        if (!Path.GetFileName(fullPath).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only GTA5.exe can be fingerprinted by this service.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("GTA5.exe was not found.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A reparse-point GTA5.exe cannot be fingerprinted.");

        await using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
