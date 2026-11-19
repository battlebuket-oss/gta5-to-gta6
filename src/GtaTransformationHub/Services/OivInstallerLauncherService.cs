using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

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
        var currentInspection = InspectRetainedPackage(packagePath, expectedArchiveSha256);
        return OpenPackageInInstallerCore(
            installerExecutablePath, expectedInstallerSha256, packagePath,
            gameExecutablePath, currentInspection, allowUnspecifiedLegacyTarget);
    }

    public Process OpenPackageInInstaller(
        string installerExecutablePath,
        string? expectedInstallerSha256,
        string packagePath,
        string expectedArchiveSha256,
        string gameExecutablePath,
        OivManifestInspectionResult expectedInspection,
        bool allowUnspecifiedLegacyTarget = false)
    {
        ArgumentNullException.ThrowIfNull(expectedInspection);
        var currentInspection = InspectRetainedPackage(packagePath, expectedArchiveSha256);
        if (!InspectionsMatch(expectedInspection, currentInspection))
            throw new InvalidDataException("The OIV manifest differs from the inspection displayed to the user. Re-import the package.");

        return OpenPackageInInstallerCore(
            installerExecutablePath, expectedInstallerSha256, packagePath,
            gameExecutablePath, currentInspection, allowUnspecifiedLegacyTarget);
    }

    private Process OpenPackageInInstallerCore(
        string installerExecutablePath,
        string? expectedInstallerSha256,
        string packagePath,
        string gameExecutablePath,
        OivManifestInspectionResult inspection,
        bool allowUnspecifiedLegacyTarget)
    {
        if (expectedInstallerSha256 is not null && !IsSha256(expectedInstallerSha256))
            throw new InvalidDataException("A valid selected-installer SHA-256 digest is required.");

        _processMonitor.EnsureConfigurationUnlocked();

        var installer = InspectInstallerExecutable(installerExecutablePath);
        if (expectedInstallerSha256 is not null &&
            !installer.Sha256.Equals(expectedInstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected OIV installer changed after it was approved. Re-select the external installer.");

        _ = new GameInstallationDetector().Inspect(gameExecutablePath);
        ValidatePackageForLegacyTarget(packagePath, inspection, allowUnspecifiedLegacyTarget);

        var startInfo = CreateStartInfo(installer.ExecutablePath, packagePath);
        var process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException("Windows did not start the external OIV installer.");
    }

    private static bool InspectionsMatch(
        OivManifestInspectionResult expected,
        OivManifestInspectionResult actual) =>
        string.Equals(expected.FormatVersion, actual.FormatVersion, StringComparison.Ordinal) &&
        string.Equals(expected.PackageId, actual.PackageId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.Target, actual.Target, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.Name, actual.Name, StringComparison.Ordinal) &&
        string.Equals(expected.Author, actual.Author, StringComparison.Ordinal) &&
        string.Equals(expected.ModVersion, actual.ModVersion, StringComparison.Ordinal) &&
        string.Equals(expected.VersionTag, actual.VersionTag, StringComparison.Ordinal) &&
        string.Equals(expected.GameVersionRequirement, actual.GameVersionRequirement, StringComparison.Ordinal) &&
        string.Equals(expected.Licence, actual.Licence, StringComparison.Ordinal) &&
        expected.Operations.SequenceEqual(actual.Operations) &&
        expected.UnsupportedCommands.SequenceEqual(actual.UnsupportedCommands, StringComparer.Ordinal) &&
        expected.PayloadSourceIssues.SequenceEqual(actual.PayloadSourceIssues, StringComparer.Ordinal) &&
        (expected.ManifestWarnings ?? Array.Empty<string>())
            .SequenceEqual(actual.ManifestWarnings ?? Array.Empty<string>(), StringComparer.Ordinal);

    public async Task<Process> OpenPackageInInstallerAsync(
        string installerExecutablePath,
        string expectedInstallerSha256,
        string packagePath,
        string expectedArchiveSha256,
        string gameExecutablePath,
        OivManifestInspectionResult expectedInspection,
        bool allowUnspecifiedLegacyTarget = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedInspection);
        _processMonitor.EnsureConfigurationUnlocked();

        var installer = InspectInstallerExecutable(installerExecutablePath);
        if (!IsSha256(expectedInstallerSha256) ||
            !installer.Sha256.Equals(expectedInstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The selected OIV installer changed after it was approved. Re-select the external installer.");

        var game = new GameInstallationDetector().Inspect(gameExecutablePath);

        // Re-hash and re-parse the retained source package at handoff time. Never trust
        // an in-memory manifest alone after the user has left the import screen.
        var currentInspection = InspectRetainedPackage(packagePath, expectedArchiveSha256);
        if (!InspectionsMatch(expectedInspection, currentInspection))
            throw new InvalidDataException(
                "The OIV manifest differs from the inspection displayed to the user. Re-import the package.");

        ValidatePackageForLegacyTarget(packagePath, currentInspection, allowUnspecifiedLegacyTarget);

        // CodeWalker's GUI loads its saved game directory when it starts. Set the
        // default to the Legacy root selected in this Hub and read it back through
        // CodeWalker's own CLI before opening its GUI. A stale Enhanced or other
        // install path must not silently become the OIV target.
        await ConfigureDefaultGameFolderAsync(
            installer.ExecutablePath, game.GameDirectory, cancellationToken);

        _processMonitor.EnsureConfigurationUnlocked();

        // Re-check both external binaries after the awaited configuration step.
        var finalInstaller = InspectInstallerExecutable(installer.ExecutablePath);
        if (!finalInstaller.Sha256.Equals(expectedInstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The external OIV installer changed during target configuration.");

        var finalInspection = InspectRetainedPackage(packagePath, expectedArchiveSha256);
        if (!InspectionsMatch(currentInspection, finalInspection))
            throw new InvalidDataException("The OIV package changed during target configuration. Re-import it.");

        ValidatePackageForLegacyTarget(packagePath, finalInspection, allowUnspecifiedLegacyTarget);

        var process = Process.Start(CreateStartInfo(finalInstaller.ExecutablePath, packagePath));
        return process ?? throw new InvalidOperationException("Windows did not start the external OIV installer.");
    }

    public static ProcessStartInfo CreateSetGameStartInfo(string installerExecutablePath, string gameFolder)
    {
        var startInfo = CreateCliStartInfo(installerExecutablePath);
        startInfo.ArgumentList.Add("--set-game");
        startInfo.ArgumentList.Add(Path.GetFullPath(gameFolder));
        return startInfo;
    }

    public static ProcessStartInfo CreateGetGameStartInfo(string installerExecutablePath)
    {
        var startInfo = CreateCliStartInfo(installerExecutablePath);
        startInfo.ArgumentList.Add("--get-game");
        return startInfo;
    }

    public static bool OutputConfirmsDefaultGameFolder(string output, string expectedGameFolder)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(expectedGameFolder))
            return false;

        const string prefix = "Default game folder:";
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var actualPath = trimmed[prefix.Length..].Trim();
            try
            {
                return string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(actualPath)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedGameFolder)),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or NotSupportedException)
            {
                return false;
            }
        }

        return false;
    }

    private static ProcessStartInfo CreateCliStartInfo(string installerExecutablePath)
    {
        var installer = Path.GetFullPath(installerExecutablePath);
        if (!string.Equals(
                Path.GetFileName(installer),
                "CodeWalker.OIVInstaller.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected executable is not CodeWalker.OIVInstaller.exe.");

        return new ProcessStartInfo
        {
            FileName = installer,
            WorkingDirectory = Path.GetDirectoryName(installer)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
    }

    private static async Task ConfigureDefaultGameFolderAsync(
        string installerExecutablePath,
        string gameFolder,
        CancellationToken cancellationToken)
    {
        var setResult = await RunCliCommandAsync(
            CreateSetGameStartInfo(installerExecutablePath, gameFolder), cancellationToken);
        if (setResult.ExitCode != 0)
            throw new InvalidOperationException(
                "CodeWalker rejected the selected GTA V Legacy folder. " +
                setResult.StandardError + Environment.NewLine + setResult.StandardOutput);

        var getResult = await RunCliCommandAsync(
            CreateGetGameStartInfo(installerExecutablePath), cancellationToken);
        if (getResult.ExitCode != 0 ||
            !OutputConfirmsDefaultGameFolder(getResult.StandardOutput, gameFolder))
            throw new InvalidOperationException(
                "CodeWalker did not confirm the selected GTA V Legacy folder as its active OIV target. " +
                "The package was not opened. " +
                getResult.StandardError + Environment.NewLine + getResult.StandardOutput);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliCommandAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Windows did not start the CodeWalker OIV installer CLI.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Preserve the timeout/cancellation exception.
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("CodeWalker did not complete the game-folder command within 15 seconds.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
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
        string expectedArchiveSha256,
        OivManifestInspectionResult inspection,
        bool allowUnspecifiedLegacyTarget = false)
    {
        if (!IsSha256(expectedArchiveSha256))
            throw new InvalidDataException("A valid expected source-archive SHA-256 digest is required.");

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
            throw new FileNotFoundException("The original OIV package is no longer available.", fullPackagePath);

        using (var stream = new FileStream(fullPackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!actualHash.Equals(expectedArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The OIV archive changed after inspection. Re-import it before continuing.");
        }

        ValidatePackageForLegacyTarget(fullPackagePath, inspection, allowUnspecifiedLegacyTarget);
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
        if (!File.Exists(package) || (File.GetAttributes(package) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Select an existing OIV file that is not a symbolic link or reparse point.");

        var startInfo = new ProcessStartInfo
        {
            FileName = installer,
            WorkingDirectory = Path.GetDirectoryName(installer)!,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        // The documented GUI workflow is to open the installer, then use Browse
        // or drag-and-drop the retained OIV package. Do not assume a CLI file argument.
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
