using System.Diagnostics;
using System.IO;

namespace GtaTransformationHub.Services;

public sealed record GameInstallation(
    string RootPath,
    string ExecutablePath,
    string BuildVersion,
    string ProductVersion,
    string FileVersion);

public sealed class GameInstallationService
{
    private readonly GameProcessMonitor _processMonitor;

    public GameInstallationService(GameProcessMonitor? processMonitor = null)
    {
        _processMonitor = processMonitor ?? new GameProcessMonitor();
    }

    public GameInstallation ValidateExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Select the GTA5.exe file.", nameof(executablePath));

        var fullPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetFileName(fullPath), "GTA5.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected executable must be named GTA5.exe.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("GTA5.exe was not found.", fullPath);

        var root = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("The GTA V installation folder could not be determined.");

        _processMonitor.EnsureConfigurationUnlocked();

        var info = FileVersionInfo.GetVersionInfo(fullPath);
        var candidates = new[] { info.ProductVersion, info.FileVersion };
        var build = candidates.FirstOrDefault(value =>
            Version.TryParse(NormalizeVersion(value), out _));

        if (build is null)
            throw new InvalidDataException(
                "Windows could not read a numeric GTA V build from GTA5.exe. The installation cannot be safely matched to package compatibility ranges.");

        build = NormalizeVersion(build);
        return new GameInstallation(
            Path.TrimEndingDirectorySeparator(root),
            fullPath,
            build,
            info.ProductVersion ?? string.Empty,
            info.FileVersion ?? string.Empty);
    }

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var candidate = value.Trim();
        var separator = candidate.IndexOfAny(new[] { ' ', '-' });
        if (separator > 0)
            candidate = candidate[..separator];

        return Version.TryParse(candidate, out _) ? candidate : string.Empty;
    }
}
