using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace GtaTransformationHub.Services;

/// <summary>
/// Opens a retained, hash-verified OIV package in a user-supplied CodeWalker installer.
/// The external installer owns the actual install and rollback; this Hub never applies
/// OIV operations itself.
/// </summary>
public sealed class OivInstallerLauncherService
{
    private const int MaximumOivEntries = 2500;
    private const long MaximumOivBytes = 1024L * 1024 * 1024;

    private readonly GameProcessMonitor _processMonitor;

    public OivInstallerLauncherService(GameProcessMonitor? processMonitor = null)
    {
        _processMonitor = processMonitor ?? new GameProcessMonitor();
    }

    public OivInstallerExecutableInfo InspectInstallerExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Select CodeWalker.OIVInstaller.exe.", nameof(executablePath));

        var fullPath = Path.GetFullPath(executablePath);
        if (!string.Equals(
                Path.GetFileName(fullPath),
                "CodeWalker.OIVInstaller.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select the CodeWalker.OIVInstaller.exe application from its official release package.");

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The external OIV installer executable does not exist.", fullPath);

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The external OIV installer cannot be a symbolic link or reparse point.");

        var version = FileVersionInfo.GetVersionInfo(fullPath);
        string sha256;
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            sha256 = Convert.ToHexString(SHA256.HashData(stream));

        return new OivInstallerExecutableInfo(
            fullPath,
            version.FileVersion ?? string.Empty,
            version.ProductName ?? string.Empty,
            sha256,
            "The selected executable is an external third-party tool. Its publisher/signature is not verified by this Hub.");
    }

    public Process OpenPackageInInstaller(
        string installerExecutablePath,
        string packagePath,
        string gameExecutablePath,
        string expectedArchiveSha256,
        bool allowUnspecifiedLegacyTarget = false) =>
        OpenPackageInInstaller(
            installerExecutablePath,
            expectedInstallerSha256: null,
            packagePath,
            expectedArchiveSha256,
            gameExecutablePath,
            allowUnspecifiedLegacyTarget);

    public Process OpenPackageInInstaller(
        string installerExecutablePath,
        string? expectedInstallerSha256,
        string packagePath,
        string expectedArchiveSha256,
        string gameExecutablePath,
        bool allowUnspecifiedLegacyTarget = false)
    {
        _processMonitor.EnsureConfigurationUnlocked();

        var installer = InspectInstallerExecutable(installerExecutablePath);
        if (expectedInstallerSha256 is not null &&
            (!IsSha256(expectedInstallerSha256) ||
             !installer.Sha256.Equals(expectedInstallerSha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The selected OIV installer changed after it was approved. Re-select the external installer.");

        _ = new GameInstallationDetector().Inspect(gameExecutablePath);

        // Re-hash and re-parse the retained package immediately before handoff. Do not
        // trust an in-memory manifest object that might belong to a different archive.
        var currentInspection = InspectRetainedPackage(packagePath, expectedArchiveSha256);
        ValidatePackageForLegacyTarget(packagePath, currentInspection, allowUnspecifiedLegacyTarget);

        var startInfo = CreateStartInfo(installer.ExecutablePath, packagePath);
        var process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException("Windows did not start the external OIV installer.");
    }

    public static OivManifestInspectionResult InspectRetainedPackage(
        string packagePath,
        string expectedArchiveSha256)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("A retained OIV package path is required.", nameof(packagePath));
        if (!IsSha256(expectedArchiveSha256))
            throw new InvalidDataException("A valid expected source-archive SHA-256 digest is required.");

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
            throw new FileNotFoundException("The retained OIV package is no longer available.", fullPackagePath);
        if (!string.Equals(Path.GetExtension(fullPackagePath), ".oiv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only a retained .oiv package can be opened in the external installer.");
        if ((File.GetAttributes(fullPackagePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("OIV package files cannot be symbolic links or reparse points.");

        var info = new FileInfo(fullPackagePath);
        if (info.Length <= 0 || info.Length > MaximumOivBytes)
            throw new InvalidDataException("The retained OIV package is empty or exceeds the 1 GiB handoff limit.");

        using var stream = new FileStream(fullPackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!actualHash.Equals(expectedArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The retained OIV package changed after import. Re-import it before opening the installer.");

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumOivEntries)
            throw new InvalidDataException("The retained OIV package is empty or exceeds the 2,500-entry inspection limit.");

        return OivManifestInspectionService.Inspect(archive);
    }

    public static void ValidatePackageForLegacyTarget(
        string packagePath,
        OivManifestInspectionResult inspection,
        bool allowUnspecifiedLegacyTarget = false)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
            throw new FileNotFoundException("The original OIV package is no longer available.", fullPackagePath);
        if (!string.Equals(Path.GetExtension(fullPackagePath), ".oiv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only an inspected .oiv package can be opened in the external OIV installer.");
        if ((File.GetAttributes(fullPackagePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("OIV package files cannot be symbolic links or reparse points.");

        if (!inspection.IsGtaVTarget)
            throw new InvalidDataException("This OIV package does not target Grand Theft Auto V.");

        if (inspection.IsExplicitlyEnhanced)
            throw new InvalidDataException("This OIV package explicitly targets GTA V Enhanced. It cannot be opened for the GTA V Legacy installation.");

        if (inspection.UnsupportedCommands.Count > 0 || inspection.PayloadSourceIssues.Count > 0)
            throw new InvalidDataException(
                "OIV inspection found unsupported commands or missing payload references. Resolve those issues before using an installer.");

        if (!inspection.IsExplicitlyLegacy && !inspection.IsAnyGameVersion && !allowUnspecifiedLegacyTarget)
            throw new InvalidDataException(
                "The OIV manifest does not explicitly identify GTA V Legacy. Review the author instructions and confirm the target before opening it.");
    }

    public static ProcessStartInfo CreateStartInfo(string installerExecutablePath, string packagePath)
    {
        var installer = Path.GetFullPath(installerExecutablePath);
        var package = Path.GetFullPath(packagePath);

        if (!string.Equals(
                Path.GetFileName(installer),
                "CodeWalker.OIVInstaller.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected executable is not CodeWalker.OIVInstaller.exe.");
        if (!string.Equals(Path.GetExtension(package), ".oiv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The external installer launcher accepts only .oiv files.");

        var startInfo = new ProcessStartInfo
        {
            FileName = installer,
            WorkingDirectory = Path.GetDirectoryName(installer)!,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        // CodeWalker's GUI reads an OIV file path from its command-line arguments.
        // ArgumentList handles spaces safely and avoids shell-string concatenation.
        startInfo.ArgumentList.Add(package);
        return startInfo;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed record OivInstallerExecutableInfo(
    string ExecutablePath,
    string FileVersion,
    string ProductName,
    string Sha256,
    string VerificationNote);
