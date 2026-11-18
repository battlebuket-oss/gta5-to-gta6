namespace GtaTransformationHub.Services;

/// <summary>
/// Blocks destinations that the Hub must never replace. RPF archive editing is
/// intentionally not delegated to the copy-only transaction engine.
/// </summary>
public static class ProtectedGamePathPolicy
{
    private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GTA5.exe", "GTA5_Enhanced.exe", "PlayGTAV.exe",
        "GTAVLauncher.exe", "Launcher.exe", "SocialClubHelper.exe"
    };

    private static readonly HashSet<string> ProtectedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Profiles", "SavedGames", "SaveGames", "UserData"
    };

    public static void ValidateDestination(string relativePath)
    {
        var normalized = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new InvalidDataException("A deployment destination is empty.");

        var fileName = segments[^1];
        if (ProtectedFileNames.Contains(fileName) ||
            Path.GetExtension(fileName).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replacing executable or launcher files is blocked.");

        if (Path.GetExtension(fileName).Equals(".rpf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replacing an entire RPF archive is blocked. Archive-internal changes require a separately audited adapter.");

        if (segments.Any(segment => ProtectedDirectoryNames.Contains(segment)))
            throw new InvalidDataException("Mod packages cannot write into GTA V save or user-profile directories.");
    }
}
