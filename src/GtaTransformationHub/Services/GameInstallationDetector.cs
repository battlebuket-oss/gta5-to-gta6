using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace GtaTransformationHub.Services;

/// <summary>
/// Identifies a GTA V Legacy executable and records a stable local fingerprint.
/// Detection is not a mod-compatibility approval and never enables game launch.
/// </summary>
public sealed class GameInstallationDetector
{
    private readonly Func<string, ExecutableVersionDetails> _versionReader;

    public GameInstallationDetector()
        : this(ReadExecutableVersion)
    {
    }

    public GameInstallationDetector(Func<string, ExecutableVersionDetails> versionReader)
    {
        _versionReader = versionReader ?? throw new ArgumentNullException(nameof(versionReader));
    }

    public GameInstallationInfo Inspect(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Select the GTA V Legacy GTA5.exe file.", nameof(executablePath));

        var fullPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetFileName(fullPath), "GTA5.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the GTA V Legacy GTA5.exe executable is supported. GTA V Enhanced is not a valid target.");

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The selected GTA5.exe file does not exist.", fullPath);

        var attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("GTA5.exe cannot be a symbolic link or reparse point.");

        var gameDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            throw new InvalidDataException("The selected executable has no valid installation directory.");

        var directoryAttributes = File.GetAttributes(gameDirectory);
        if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The GTA V installation directory cannot be a symbolic link or reparse point.");

        var updateArchive = Path.Combine(gameDirectory, "update", "update.rpf");
        if (!File.Exists(updateArchive))
            throw new InvalidDataException("The selected folder does not contain update\\update.rpf and is not recognised as a complete GTA V Legacy installation.");

        var version = _versionReader(fullPath);
        if (version is null || string.IsNullOrWhiteSpace(version.FileVersion))
            throw new InvalidDataException("Windows did not provide a readable file version for GTA5.exe.");

        if (!Version.TryParse(version.FileVersion, out _))
            throw new InvalidDataException("GTA5.exe reported an unrecognised file version: " + version.FileVersion);

        if (!string.IsNullOrWhiteSpace(version.ProductName) &&
            !version.ProductName.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected executable's product metadata does not identify Grand Theft Auto V.");

        string executableHash;
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            executableHash = Convert.ToHexString(SHA256.HashData(stream));

        return new GameInstallationInfo(
            fullPath,
            gameDirectory,
            version.ProductName ?? string.Empty,
            version.FileVersion,
            version.ProductVersion ?? string.Empty,
            executableHash,
            "GTA V Legacy",
            "Unknown - exact game build and installed mod compatibility still require approval");
    }

    private static ExecutableVersionDetails ReadExecutableVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new ExecutableVersionDetails(
            info.FileVersion,
            info.ProductVersion,
            info.ProductName);
    }
}

public sealed record ExecutableVersionDetails(
    string? FileVersion,
    string? ProductVersion,
    string? ProductName);

public sealed record GameInstallationInfo(
    string ExecutablePath,
    string GameDirectory,
    string ProductName,
    string FileVersion,
    string ProductVersion,
    string ExecutableSha256,
    string Edition,
    string CompatibilityStatus);
