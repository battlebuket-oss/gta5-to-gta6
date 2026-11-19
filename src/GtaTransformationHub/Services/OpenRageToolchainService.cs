using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed record OpenRageToolchainInfo(
    string ExecutablePath,
    string Version,
    string ReleaseArchivePath,
    string ArchiveSha256,
    string ExecutableSha256);

public sealed record OpenRageOivInspection(
    string PackagePath,
    string PackageSha256,
    string Output,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> GameFolderChanges,
    bool RequiresExecutableConsent)
{
    public bool IsInstallable => Blockers.Count == 0;
}

public sealed record OpenRageCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);

/// <summary>
/// Optional external OIV/RPF installer integration. The official GPL-3.0 OpenRAGE
/// release is downloaded from its own GitHub release and verified by the SHA-256
/// of the complete upstream Windows archive. The original archive, executable,
/// notices and licence files are kept together; OpenRAGE is not bundled into this
/// application and no third-party mod binaries are redistributed.
/// </summary>
public sealed class OpenRageToolchainService
{
    public const string PinnedVersion = "0.4.0";
    public const string ReleaseUrl = "https://github.com/Two-Play/openrage/releases/tag/v0.4.0";
    public const string WindowsAssetUrl =
        "https://github.com/Two-Play/openrage/releases/download/v0.4.0/openrage-v0.4.0-windows-x86_64.zip";
    public const string WindowsAssetSha256 =
        "32F149015A8F807FEEDBF5C2A2EC34FBFDC115F00F1CCEF74A3DFEC7F0FD2C25";

    private const long MaximumArchiveBytes = 256L * 1024 * 1024;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;
    private const long MaximumEntryBytes = 512L * 1024 * 1024;
    private const int MaximumEntries = 10_000;
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan KeySetupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OivOperationTimeout = TimeSpan.FromMinutes(10);

    private readonly PackageAcquisitionService _acquisitionService;
    private readonly IRageCliProcessRunner _processRunner;
    private readonly string _installDirectory;
    private readonly string _payloadDirectory;
    private readonly string _releaseArchivePath;

    public OpenRageToolchainService(
        PackageAcquisitionService acquisitionService,
        IRageCliProcessRunner? processRunner = null,
        string? installDirectory = null)
    {
        _acquisitionService = acquisitionService ?? throw new ArgumentNullException(nameof(acquisitionService));
        _processRunner = processRunner ?? new RageCliProcessRunner();
        _installDirectory = Path.GetFullPath(installDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "tools", "openrage", PinnedVersion));
        _payloadDirectory = Path.Combine(_installDirectory, "payload");
        _releaseArchivePath = Path.Combine(_installDirectory, "openrage-v" + PinnedVersion + "-windows-x86_64.zip");
    }

    public string InstallDirectory => _installDirectory;

    public async Task<OpenRageToolchainInfo> DownloadAndInstallAsync(
        CancellationToken cancellationToken = default)
    {
        var acquired = await _acquisitionService.DownloadAsync(
            new Uri(WindowsAssetUrl),
            WindowsAssetSha256,
            cancellationToken);

        var parent = Path.GetDirectoryName(_installDirectory)!;
        Directory.CreateDirectory(parent);
        EnsureDirectoryNotReparsePoint(parent);
        var stagingDirectory = _installDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var stagedArchivePath = Path.Combine(stagingDirectory, Path.GetFileName(_releaseArchivePath));
            File.Copy(acquired.FilePath, stagedArchivePath, overwrite: false);
            var archiveHash = await ComputeSha256Async(stagedArchivePath, cancellationToken);
            if (!archiveHash.Equals(WindowsAssetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded OpenRAGE release archive failed SHA-256 verification.");

            var stagedPayload = Path.Combine(stagingDirectory, "payload");
            Directory.CreateDirectory(stagedPayload);
            await ExtractReleaseArchiveAsync(stagedArchivePath, stagedPayload, cancellationToken);
            var executable = FindCliExecutable(stagedPayload);
            var executableHash = await ComputeSha256Async(executable, cancellationToken);
            await VerifyVersionAsync(executable, cancellationToken);

            if (Directory.Exists(_installDirectory))
            {
                var previousDirectory = _installDirectory + ".previous-" + Guid.NewGuid().ToString("N");
                Directory.Move(_installDirectory, previousDirectory);
                try
                {
                    Directory.Move(stagingDirectory, _installDirectory);
                }
                catch
                {
                    if (Directory.Exists(_installDirectory))
                        Directory.Delete(_installDirectory, recursive: true);
                    Directory.Move(previousDirectory, _installDirectory);
                    throw;
                }

                try
                {
                    Directory.Delete(previousDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // The verified new installation is usable; retain the old copy for manual cleanup.
                }
                catch (UnauthorizedAccessException)
                {
                    // The verified new installation is usable; retain the old copy for manual cleanup.
                }
            }
            else
            {
                Directory.Move(stagingDirectory, _installDirectory);
            }

            var finalExe = FindCliExecutable(_payloadDirectory);
            var finalHash = await ComputeSha256Async(finalExe, cancellationToken);
            if (!finalHash.Equals(executableHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The OpenRAGE executable changed during installation.");

            AppDiagnostics("OpenRageToolInstalled",
                "Verified the upstream OpenRAGE v" + PinnedVersion + " Windows release archive and CLI.");
            return new OpenRageToolchainInfo(
                finalExe, PinnedVersion, _releaseArchivePath, archiveHash, finalHash);
        }
        finally
        {
            if (File.Exists(acquired.FilePath))
            {
                try { File.Delete(acquired.FilePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public async Task<OpenRageToolchainInfo> ValidateInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_releaseArchivePath))
            throw new FileNotFoundException(
                "OpenRAGE is not installed. Use 'Download and verify OpenRAGE' to retrieve the official release.",
                _releaseArchivePath);

        EnsureDirectoryTreeNoReparse(_installDirectory);
        EnsureDirectoryTreeNoReparse(_payloadDirectory);
        var archiveInfo = new FileInfo(_releaseArchivePath);
        if (archiveInfo.Length <= 0 || archiveInfo.Length > MaximumArchiveBytes)
            throw new InvalidDataException("The local OpenRAGE release archive is empty or exceeds the allowed size.");

        var archiveHash = await ComputeSha256Async(_releaseArchivePath, cancellationToken);
        if (!archiveHash.Equals(WindowsAssetSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The stored OpenRAGE release archive no longer matches the pinned official v" +
                PinnedVersion + " Windows x64 release digest.");

        var exePath = FindCliExecutable(_payloadDirectory);
        EnsureNoReparsePoint(exePath);
        var relative = Path.GetRelativePath(_payloadDirectory, exePath)
            .Replace(Path.DirectorySeparatorChar, '/');
        var archiveExecutableHash = await ComputeExecutableEntryHashAsync(
            _releaseArchivePath, relative, cancellationToken);
        var installedExecutableHash = await ComputeSha256Async(exePath, cancellationToken);
        if (!archiveExecutableHash.Equals(installedExecutableHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The installed OpenRAGE CLI differs from the executable inside the checksum-verified upstream archive.");

        await VerifyVersionAsync(exePath, cancellationToken);
        return new OpenRageToolchainInfo(
            exePath, PinnedVersion, _releaseArchivePath, archiveHash, installedExecutableHash);
    }

    public async Task<OpenRageOivInspection> InspectOivAsync(
        GameInstallation installation,
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ValidateGameInstallation(installation);
        ValidateOpenIvModsFolder(installation.RootPath);
        var safePackage = ValidateOivPackagePath(packagePath);
        var tool = await ValidateInstalledAsync(cancellationToken);
        await EnsureGameKeysAsync(tool, installation, cancellationToken);
        var packageHashBefore = await ComputeSha256Async(safePackage, cancellationToken);

        var result = await _processRunner.RunAsync(
            tool.ExecutablePath,
            BuildOivInfoArguments(installation.RootPath, safePackage),
            OivOperationTimeout,
            cancellationToken);

        if (result.ExitCode != 0 || result.StandardOutputTruncated || result.StandardErrorTruncated)
            throw BuildCommandException("OpenRAGE could not inspect the OIV package.", result);

        var packageHashAfter = await ComputeSha256Async(safePackage, cancellationToken);
        if (!packageHashBefore.Equals(packageHashAfter, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The OIV package changed during OpenRAGE's preview; the inspection was rejected.");

        var output = CombineOutput(result.StandardOutput, result.StandardError);
        var blockers = ParseLinesWithPrefix(output, "BLOCKED:");
        var warnings = ParseLinesWithPrefix(output, "warning:");
        var gameFolderChanges = ParseGameFolderChanges(output);
        return new OpenRageOivInspection(
            safePackage,
            packageHashAfter,
            output,
            warnings,
            blockers,
            gameFolderChanges,
            gameFolderChanges.Count > 0);
    }

    public async Task<OpenRageCommandResult> InstallOivAsync(
        GameInstallation installation,
        OpenRageOivInspection reviewedInspection,
        bool userConfirmed,
        bool allowGameFolderFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewedInspection);
        if (!userConfirmed)
            throw new InvalidOperationException("Installing an OIV requires explicit user confirmation.");
        if (!IsSha256(reviewedInspection.PackageSha256))
            throw new InvalidDataException("The reviewed OIV package has no valid SHA-256 fingerprint.");

        // Repeat the dry run immediately before install and bind it to the exact file
        // the user saw in the earlier preview. A package replaced between preview and
        // install is rejected, and the read handle prevents concurrent modification
        // while OpenRAGE parses and installs it.
        var inspection = await InspectOivAsync(
            installation, reviewedInspection.PackagePath, cancellationToken);
        if (!inspection.PackageSha256.Equals(reviewedInspection.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The OIV package changed after the user reviewed its preview. Inspect it again before installation.");
        if (!inspection.IsInstallable)
            throw new InvalidDataException(
                "OpenRAGE blocked this OIV package:" + Environment.NewLine +
                string.Join(Environment.NewLine, inspection.Blockers));

        if (inspection.RequiresExecutableConsent && !allowGameFolderFiles)
            throw new InvalidOperationException(
                "This OIV contains files that OpenRAGE would place in the GTA V game folder. " +
                "Review the changes and explicitly acknowledge this additional risk before installation.");

        var tool = await ValidateInstalledAsync(cancellationToken);
        await using var packageLock = new FileStream(
            inspection.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var lockedPackageHash = Convert.ToHexString(
            await SHA256.HashDataAsync(packageLock, cancellationToken));
        if (!lockedPackageHash.Equals(reviewedInspection.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The OIV package changed immediately before install; no changes were made.");

        var result = await _processRunner.RunAsync(
            tool.ExecutablePath,
            BuildOivInstallArguments(installation.RootPath, inspection.PackagePath),
            OivOperationTimeout,
            cancellationToken);

        if (result.ExitCode != 0 || result.StandardOutputTruncated || result.StandardErrorTruncated)
            throw BuildCommandException("OpenRAGE could not install the OIV package.", result);

        return new OpenRageCommandResult(result.ExitCode, result.StandardOutput, result.StandardError, result.Duration);
    }

    public async Task<OpenRageCommandResult> ListInstalledOivPackagesAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ValidateGameInstallation(installation);
        var tool = await ValidateInstalledAsync(cancellationToken);
        await EnsureGameKeysAsync(tool, installation, cancellationToken);
        var result = await _processRunner.RunAsync(
            tool.ExecutablePath,
            BuildOivListArguments(installation.RootPath),
            OivOperationTimeout,
            cancellationToken);
        if (result.ExitCode != 0 || result.StandardOutputTruncated || result.StandardErrorTruncated)
            throw BuildCommandException("OpenRAGE could not list its installed OIV packages.", result);
        return new OpenRageCommandResult(result.ExitCode, result.StandardOutput, result.StandardError, result.Duration);
    }

    public async Task<OpenRageCommandResult> UninstallOivPackageAsync(
        GameInstallation installation,
        string packageGroupName,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ValidateGameInstallation(installation);
        ValidatePackageGroupName(packageGroupName);
        if (!userConfirmed)
            throw new InvalidOperationException("Uninstalling an OIV requires explicit user confirmation.");

        var tool = await ValidateInstalledAsync(cancellationToken);
        await EnsureGameKeysAsync(tool, installation, cancellationToken);
        var result = await _processRunner.RunAsync(
            tool.ExecutablePath,
            BuildOivUninstallArguments(installation.RootPath, packageGroupName),
            OivOperationTimeout,
            cancellationToken);
        if (result.ExitCode != 0 || result.StandardOutputTruncated || result.StandardErrorTruncated)
            throw BuildCommandException("OpenRAGE could not uninstall the selected OIV package.", result);
        return new OpenRageCommandResult(result.ExitCode, result.StandardOutput, result.StandardError, result.Duration);
    }

    public async Task<OpenRageCommandResult> UndoLatestOpenRageChangeAsync(
        GameInstallation installation,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ValidateGameInstallation(installation);
        if (!userConfirmed)
            throw new InvalidOperationException("Undo requires explicit user confirmation.");

        var tool = await ValidateInstalledAsync(cancellationToken);
        await EnsureGameKeysAsync(tool, installation, cancellationToken);
        var result = await _processRunner.RunAsync(
            tool.ExecutablePath,
            BuildUndoArguments(installation.RootPath),
            OivOperationTimeout,
            cancellationToken);
        if (result.ExitCode != 0 || result.StandardOutputTruncated || result.StandardErrorTruncated)
            throw BuildCommandException("OpenRAGE could not undo its latest recorded change.", result);
        return new OpenRageCommandResult(result.ExitCode, result.StandardOutput, result.StandardError, result.Duration);
    }

    public static IReadOnlyList<string> BuildVersionArguments() => new[] { "--version" };

    public static IReadOnlyList<string> BuildGameKeysArguments(string gameRoot) =>
        new[] { "--game", Path.GetFullPath(gameRoot), "keys" };

    public static IReadOnlyList<string> BuildOivInfoArguments(string gameRoot, string packagePath) =>
        new[] { "--game", Path.GetFullPath(gameRoot), "oiv", "info", Path.GetFullPath(packagePath) };

    public static IReadOnlyList<string> BuildOivInstallArguments(string gameRoot, string packagePath) =>
        new[] { "--game", Path.GetFullPath(gameRoot), "oiv", "install", Path.GetFullPath(packagePath), "--yes" };

    public static IReadOnlyList<string> BuildOivListArguments(string gameRoot) =>
        new[] { "--game", Path.GetFullPath(gameRoot), "oiv", "list" };

    public static IReadOnlyList<string> BuildOivUninstallArguments(string gameRoot, string groupName)
    {
        ValidatePackageGroupName(groupName);
        return new[] { "--game", Path.GetFullPath(gameRoot), "oiv", "uninstall", groupName, "--yes" };
    }

    public static IReadOnlyList<string> BuildUndoArguments(string gameRoot) =>
        new[] { "--game", Path.GetFullPath(gameRoot), "undo" };

    public static bool IsPinnedVersionOutput(string output) =>
        !string.IsNullOrWhiteSpace(output) &&
        output.Contains("openrage", StringComparison.OrdinalIgnoreCase) &&
        output.Contains(PinnedVersion, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ParseLinesWithPrefix(string output, string prefix) =>
        (output ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public static IReadOnlyList<string> ParseGameFolderChanges(string output) =>
        (output ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line =>
                line.Contains("new file in the game folder", StringComparison.OrdinalIgnoreCase) ||
                (line.Contains("game folder", StringComparison.OrdinalIgnoreCase) &&
                 (line.Contains(".asi", StringComparison.OrdinalIgnoreCase) ||
                  line.Contains(".dll", StringComparison.OrdinalIgnoreCase) ||
                  line.Contains(".exe", StringComparison.OrdinalIgnoreCase))))
            .ToArray();

    public static void ValidateOpenIvModsFolder(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new ArgumentException("A GTA V Legacy game folder is required.", nameof(gameRoot));

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The selected GTA V game folder is missing or redirected.");

        var modsDirectory = Path.Combine(root, "mods");
        if (!Directory.Exists(modsDirectory) ||
            (File.GetAttributes(modsDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "OpenRAGE requires an existing regular 'mods' folder. Install OpenIV and create/initialize its mods folder first.");

        var openIvAsi = Path.Combine(root, "OpenIV.asi");
        if (!File.Exists(openIvAsi) || (File.GetAttributes(openIvAsi) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "OpenIV.asi is not installed in the selected GTA V Legacy folder. Install it with OpenIV ASI Manager before installing OIV packages.");
    }

    public static string ValidateOivPackagePath(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Select an OIV package.", nameof(packagePath));
        var full = Path.GetFullPath(packagePath);
        if (!full.EndsWith(".oiv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("OpenRAGE's managed installer accepts OIV archives only.");
        if (!File.Exists(full))
            throw new FileNotFoundException("The OIV package was not found.", full);
        EnsureNoReparsePoint(full);
        var info = new FileInfo(full);
        if (info.Length <= 0 || info.Length > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("The OIV archive is empty or exceeds the 2 GB safety limit.");
        return full;
    }

    public static void ValidatePackageGroupName(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName) || groupName.Length > 256 ||
            groupName.Any(char.IsControl) ||
            groupName.Contains('\r') || groupName.Contains('\n'))
            throw new ArgumentException("Enter an installed OIV package name exactly as shown by OpenRAGE.", nameof(groupName));
    }

    public static string FindCliExecutable(string payloadDirectory)
    {
        var root = Path.GetFullPath(payloadDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The extracted OpenRAGE release payload was not found.");

        var matches = Directory.EnumerateFiles(root, "openrage.exe", SearchOption.AllDirectories)
            .Where(path => !IsWithinReparsePoint(root, path))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(
                "The pinned OpenRAGE release must contain exactly one regular openrage.exe CLI executable. Found " +
                matches.Length + ".");
        return Path.GetFullPath(matches[0]);
    }

    public static bool IsUnixSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private async Task EnsureGameKeysAsync(
        OpenRageToolchainInfo tool,
        GameInstallation installation,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            tool.ExecutablePath,
            BuildGameKeysArguments(installation.RootPath),
            KeySetupTimeout,
            cancellationToken);
        if (result.ExitCode != 0 || result.StandardOutputTruncated || result.StandardErrorTruncated)
            throw BuildCommandException(
                "OpenRAGE could not derive the GTA V Legacy archive keys from the selected game installation.",
                result);
    }

    private async Task VerifyVersionAsync(string executable, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            executable, BuildVersionArguments(), VersionTimeout, cancellationToken);
        var versionOutput = CombineOutput(result.StandardOutput, result.StandardError);
        if (result.ExitCode != 0 || result.StandardOutputTruncated || result.StandardErrorTruncated ||
            !IsPinnedVersionOutput(versionOutput))
            throw new InvalidDataException(
                "The selected OpenRAGE CLI is not the pinned v" + PinnedVersion +
                " release. Reported output: " + Limit(versionOutput, 2000));
    }

    private static async Task ExtractReleaseArchiveAsync(
        string archivePath,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException("The OpenRAGE archive has no entries or exceeds the entry-count limit.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsUnixSymbolicLink(entry))
                throw new InvalidDataException("The OpenRAGE release archive unexpectedly contains a symbolic link.");

            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                              entry.FullName.EndsWith("\\", StringComparison.Ordinal);
            var relative = PackageArchiveService.ValidateArchivePath(entry.FullName, isDirectory);
            if (isDirectory)
                continue;

            if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
                throw new InvalidDataException("An OpenRAGE archive entry exceeds the 512 MB per-file limit.");
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
                throw new InvalidDataException("The OpenRAGE release archive exceeds its 1 GB expanded-size limit.");
            if (!seen.Add(relative))
                throw new InvalidDataException("The OpenRAGE release archive contains duplicate file paths.");

            var destination = ResolveContainedPath(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyEntryBoundedAsync(entry, destination, cancellationToken);
        }

        _ = FindCliExecutable(destinationRoot);
    }

    private static async Task CopyEntryBoundedAsync(
        ZipArchiveEntry entry,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        await using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

        var buffer = new byte[128 * 1024];
        long written = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;
            written = checked(written + read);
            if (written > entry.Length || written > MaximumEntryBytes)
                throw new InvalidDataException("An OpenRAGE archive entry expanded beyond its declared safe size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await output.FlushAsync(cancellationToken);
        if (written != entry.Length)
            throw new InvalidDataException("An OpenRAGE archive entry did not match its declared uncompressed size.");
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(root, normalized));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An OpenRAGE archive entry escaped the staging directory.");
        return destination;
    }

    private static void EnsureDirectoryTreeNoReparse(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException("The OpenRAGE installation folder is missing.");
        var current = full;
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The OpenRAGE installation path traverses a symbolic link or reparse point.");
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || current.Equals(parent, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }

    private static bool IsWithinReparsePoint(string root, string file)
    {
        var current = Path.GetDirectoryName(Path.GetFullPath(file));
        while (!string.IsNullOrEmpty(current) &&
               current.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;
            if (current.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase))
                break;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    private static void EnsureDirectoryNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A path containing a reparse point is not accepted.");
    }

    private static void EnsureNoReparsePoint(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The selected file is missing or is a reparse point.");
    }

    private static async Task<string> ComputeExecutableEntryHashAsync(
        string archivePath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var matches = archive.Entries.Where(entry =>
            entry.FullName.Replace('\\', '/').Equals(relativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var entry = matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException("The CLI executable was not found exactly once in the verified release archive.");

        await using var content = entry.Open();
        var hash = await SHA256.HashDataAsync(content, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void ValidateGameInstallation(GameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var gameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        var executable = Path.GetFullPath(installation.ExecutablePath);
        if (!File.Exists(executable) ||
            !Path.GetFileName(executable).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetDirectoryName(executable)!.Equals(gameRoot, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(gameRoot))
            throw new InvalidDataException("The selected GTA V Legacy executable and installation root do not match.");
    }

    private static Exception BuildCommandException(string message, RageCliProcessResult result)
    {
        var details = CombineOutput(result.StandardOutput, result.StandardError);
        return new InvalidOperationException(message + Environment.NewLine +
            "Exit code: " + result.ExitCode + Environment.NewLine + Limit(details, 5000));
    }

    private static string CombineOutput(string stdout, string stderr) =>
        string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() :
        string.IsNullOrWhiteSpace(stdout) ? stderr.Trim() :
        stdout.Trim() + Environment.NewLine + stderr.Trim();

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..length] + " [TRUNCATED]";

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void AppDiagnostics(string eventName, string message) =>
        App.Diagnostics.Write(AuditLogLevel.Information, eventName, message);
}
