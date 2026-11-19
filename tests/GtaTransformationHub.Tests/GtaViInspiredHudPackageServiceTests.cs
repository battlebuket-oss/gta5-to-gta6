using System;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class GtaViInspiredHudPackageServiceTests
{
    [Fact]
    public void PinnedLegacyRelease_UsesAuthorReleaseAndValidDigest()
    {
        Assert.Equal("gta-vi-inspired-hud-single-player", GtaViInspiredHudPackageService.PackageId);
        Assert.Equal("2.8.2 Legacy", GtaViInspiredHudPackageService.PackageVersion);
        Assert.Equal(117140655, GtaViInspiredHudPackageService.PackageSizeBytes);
        Assert.Equal(64, GtaViInspiredHudPackageService.PackageSha256.Length);
        Assert.All(GtaViInspiredHudPackageService.PackageSha256,
            character => Assert.True(Uri.IsHexDigit(character)));
        Assert.StartsWith(
            "https://github.com/Geckomacabre/GTA-VI-Inspired-HUD/releases/download/v2.8.2/",
            GtaViInspiredHudPackageService.PackageDownloadUrl);
        Assert.EndsWith("-Legacy.oiv", GtaViInspiredHudPackageService.PackageDownloadUrl);
    }
}
