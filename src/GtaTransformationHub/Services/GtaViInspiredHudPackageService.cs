using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GtaTransformationHub.Services;

/// <summary>
/// Downloads the author's exact GTA V Legacy release of the GTA VI-Inspired HUD,
/// checks its immutable SHA-256 and expected byte count, then imports it into the
/// research-only package library. It never executes the OIV or changes GTA V.
/// </summary>
public sealed class GtaViInspiredHudPackageService
{
    public const string PackageId = "gta-vi-inspired-hud-single-player";
    public const string PackageVersion = "2.8.2 Legacy";
    public const long PackageSizeBytes = 117140655;
    public const string PackageSha256 =
        "72B8FE300BD99978756DEF3711DD5AFE4BD5A3B65BD4AEE9E4C8FDF97F250835";
    public const string PackageDownloadUrl =
        "https://github.com/Geckomacabre/GTA-VI-Inspired-HUD/releases/download/v2.8.2/GTA-VI-Inspired-HUD-2.8.2-Legacy.oiv";
    public const string AuthorSourceUrl =
        "https://github.com/Geckomacabre/GTA-VI-Inspired-HUD";

    private static readonly ModArchiveMetadata Metadata = new(
        PackageId,
        "GTA VI-Inspired HUD (Single Player)",
        "Geckomacabre x FBICat and credited contributors",
        new Uri(AuthorSourceUrl),
        "See upstream repository and the package's third-party notices; component licence terms vary.",
        PackageVersion);

    private readonly PackageAcquisitionService _acquisitionService;
    private readonly ModArchiveImportService _importService;

    public GtaViInspiredHudPackageService(
        PackageAcquisitionService acquisitionService,
        ModArchiveImportService? importService = null)
    {
        _acquisitionService = acquisitionService ??
            throw new ArgumentNullException(nameof(acquisitionService));
        _importService = importService ?? new ModArchiveImportService();
    }

    public async Task<ModArchiveImportResult> DownloadAndImportAsync(
        string packageLibraryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageLibraryRoot);
        var acquired = await _acquisitionService.DownloadAsync(
            new Uri(PackageDownloadUrl), PackageSha256, cancellationToken);

        try
        {
            if (acquired.SizeBytes != PackageSizeBytes)
                throw new InvalidDataException(
                    "The downloaded HUD OIV does not match the pinned v2.8.2 Legacy release size.");

            if (!string.Equals(acquired.Sha256, PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The downloaded HUD OIV failed the pinned upstream SHA-256 check.");

            return await _importService.ImportAsync(
                acquired.FilePath,
                packageLibraryRoot,
                Metadata,
                cancellationToken);
        }
        finally
        {
            if (File.Exists(acquired.FilePath))
            {
                try { File.Delete(acquired.FilePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
