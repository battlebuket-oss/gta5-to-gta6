using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace GtaTransformationHub.Services;

/// <summary>
/// Opens a user-supplied CodeWalker OIV Package Installer with an inspected OIV
/// package preloaded. The external installer owns the actual install and rollback.
/// The Hub never executes OIV operations itself.
/// </summary>
public sealed class OivInstallerLauncherService
{
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
        string expectedInstallerSha256,
        string packagePath,
        string expectedPackageSha256,
        string gameExecutablePath,
        OivManifestInspectionResult inspection,
        bool allowUnspecifiedLegacyTarget = false)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        _processMonitor.EnsureConfigurationUnlocked();

        var installer = InspectInstallerExecutable(installerExecutablePath);
        RequireSha256Match(installer.Sha256, expectedInstallerSha256, "The external OIV installer changed after it was selected.");
        _ = new GameInstallationDetector().Inspect(gameExecutablePath);
        ValidatePackageForLegacyTarget(
            packagePath, expectedPackageSha256, inspection, allowUnspecifiedLegacyTarget);

        _processMonitor.EnsureConfigurationUnlocked();
        var startInfo = CreateStartInfo(installer.ExecutablePath, packagePath);
        var process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException("Windows did not start the external OIV installer.");
    }

    public static void ValidatePackageForLegacyTarget(
        string packagePath,
        string expectedPackageSha256,
        OivManifestInspectionResult inspection,
        bool allowUnspecifiedLegacyTarget = false)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
            throw new FileNotFoundException("The retained OIV source archive is no longer available.", fullPackagePath);
        if (!string.Equals(Path.GetExtension(fullPackagePath), ".oiv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only an inspected .oiv package can be opened in the external OIV installer.");
        if ((File.GetAttributes(fullPackagePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("OIV package files cannot be symbolic links or reparse points.");

        var actualArchiveHash = ComputeSha256(fullPackagePath);
        RequireSha256Match(
            actualArchiveHash, expectedPackageSha256,
            "The retained OIV source archive has changed since it was inspected.");

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

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RequireSha256Match(string actual, string expected, string message)
    {
        if (expected is not { Length: 64 } || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("The expected SHA-256 digest is missing or malformed.");
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected)))
            throw new InvalidDataException(message);
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

        // CodeWalker's GUI reads an OIV file path from its command-line arguments
        // and opens that package for review. ArgumentList handles spaces safely.
        startInfo.ArgumentList.Add(package);
        return startInfo;
    }
}

public sealed record OivInstallerExecutableInfo(
    string ExecutablePath,
    string FileVersion,
    string ProductName,
    string Sha256,
    string VerificationNote);
