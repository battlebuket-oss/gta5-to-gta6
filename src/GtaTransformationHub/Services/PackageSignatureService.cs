using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public enum PackageSignatureStatus
{
    Unsigned,
    TrustedPublisher,
    UnknownPublisher,
    Invalid
}

public sealed record PackageSignatureVerification(
    PackageSignatureStatus Status,
    string? PublisherName,
    string? KeyFingerprint,
    string Message);

/// <summary>
/// Verifies RSA-SHA256 signatures over deterministic package-manifest payloads.
/// Signature version 1 preserves the original canonical format for legacy manifests.
/// Signature version 2 authenticates all deployment and Story Mode metadata.
/// </summary>
public sealed class PackageSignatureService
{
    private sealed record CanonicalManifestV1(
        int SchemaVersion,
        string Id,
        string Name,
        string Author,
        string SourceUri,
        string Licence,
        string Version,
        string GameTarget,
        string MinimumGameBuild,
        string MaximumGameBuild,
        IReadOnlyList<string> Dependencies,
        IReadOnlyList<string> Conflicts,
        string? ExclusiveGroup,
        string SigningKeyId,
        IReadOnlyList<CanonicalFile> Files);

    private sealed record CanonicalManifestV2(
        int SignatureVersion,
        int SchemaVersion,
        string Id,
        string Name,
        string Author,
        string SourceUri,
        string Licence,
        string Version,
        string GameTarget,
        string MinimumGameBuild,
        string MaximumGameBuild,
        IReadOnlyList<string> Dependencies,
        IReadOnlyList<string> Conflicts,
        string? ExclusiveGroup,
        string SigningKeyId,
        IReadOnlyList<string> ImpactAreas,
        bool CompatibilityVerified,
        string? PackageFormat,
        CanonicalEcology? Ecology,
        CanonicalStoryContent? StoryContent,
        IReadOnlyList<CanonicalFile> Files);

    private sealed record CanonicalEcology(
        string ReviewStatus,
        string ImpactType,
        bool PreservesVanillaAnimalSpecies,
        bool PreservesVanillaPlantSpecies,
        bool PreservesVanillaVegetationSpecies,
        bool PreservesVanillaModelIdentifiers,
        bool AllowsNewSpecies,
        bool AllowsSpeciesReplacement,
        bool AllowsSpeciesRemoval,
        bool ChangesSpeciesSpawnTables,
        string VanillaIdentityInventorySha256,
        string EvidenceUri);

    private sealed record CanonicalStoryContent(
        string ContentType,
        IReadOnlyList<string> MissionIds,
        IReadOnlyList<string> ProtagonistRequirements,
        IReadOnlyList<string> StartLocations,
        string ProgressionNamespace,
        string SaveCompatibility,
        bool RequiresSaveBackup,
        bool PreservesOriginalCampaign,
        IReadOnlyList<string> VoiceLanguages,
        IReadOnlyList<string> SubtitleLanguages,
        string? TestEvidenceUri);

    private sealed record CanonicalFile(
        string SourcePath,
        string DestinationPath,
        string Sha256,
        long SizeBytes);

    private readonly TrustedPublisherStore _trustedPublisherStore;

    public PackageSignatureService(TrustedPublisherStore? trustedPublisherStore = null)
    {
        _trustedPublisherStore = trustedPublisherStore ?? new TrustedPublisherStore();
    }

    public async Task<PackageSignatureVerification> VerifyAsync(
        PackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.SignatureVersion is not (1 or 2))
            return Invalid(manifest.SigningKeyId, "The package uses an unsupported signature version.");

        if (manifest.Files is null || manifest.Dependencies is null || manifest.Conflicts is null ||
            manifest.Files.Any(file => file is null ||
                string.IsNullOrWhiteSpace(file.SourcePath) ||
                string.IsNullOrWhiteSpace(file.DestinationPath) ||
                string.IsNullOrWhiteSpace(file.Sha256)))
            return Invalid(manifest.SigningKeyId, "The package manifest is incomplete and cannot be signature-verified.");

        var hasKeyId = !string.IsNullOrWhiteSpace(manifest.SigningKeyId);
        var hasSignature = !string.IsNullOrWhiteSpace(manifest.Signature);
        if (!hasKeyId && !hasSignature)
            return new PackageSignatureVerification(
                PackageSignatureStatus.Unsigned, null, null,
                "The package is unsigned. File hashes verify integrity but do not authenticate its publisher.");

        if (!hasKeyId || !hasSignature)
            return Invalid(manifest.SigningKeyId, "A signed package must contain both signingKeyId and signature.");

        string normalizedFingerprint;
        try
        {
            normalizedFingerprint = NormalizeFingerprint(manifest.SigningKeyId!);
        }
        catch (ArgumentException exception)
        {
            return Invalid(manifest.SigningKeyId, exception.Message);
        }

        var trustedPublisher = await _trustedPublisherStore.FindAsync(normalizedFingerprint, cancellationToken);
        if (trustedPublisher is null)
            return new PackageSignatureVerification(
                PackageSignatureStatus.UnknownPublisher, null, normalizedFingerprint,
                "The package is signed, but its publisher key is not trusted on this computer.");

        byte[] signature;
        try
        {
            if (manifest.Signature!.Length > 8192)
                throw new FormatException("The package signature is too large.");
            signature = Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException exception)
        {
            return new PackageSignatureVerification(
                PackageSignatureStatus.Invalid, trustedPublisher.Name, normalizedFingerprint,
                "The package signature is not valid base64: " + exception.Message);
        }

        try
        {
            using var key = RsaPublicKey.Import(trustedPublisher.PublicKeyPem);
            var payload = CreateSigningPayload(manifest);
            var valid = key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return valid
                ? new PackageSignatureVerification(
                    PackageSignatureStatus.TrustedPublisher, trustedPublisher.Name, normalizedFingerprint,
                    "The package manifest signature is valid and its publisher key is trusted.")
                : new PackageSignatureVerification(
                    PackageSignatureStatus.Invalid, trustedPublisher.Name, normalizedFingerprint,
                    "The package manifest signature does not match its declared contents.");
        }
        catch (Exception exception) when (
            exception is CryptographicException ||
            exception is InvalidDataException ||
            exception is ArgumentException)
        {
            return new PackageSignatureVerification(
                PackageSignatureStatus.Invalid, trustedPublisher.Name, normalizedFingerprint,
                "The package signature could not be verified: " + exception.Message);
        }
    }

    /// <summary>
    /// Returns the exact canonical bytes publishers must sign with RSA-SHA256/PKCS#1 v1.5.
    /// Version 1 remains byte-compatible with existing simple manifests. Version 2 signs
    /// optional compatibility, ecology, and Story Mode metadata so it cannot be changed
    /// after signing without invalidating the publisher signature.
    /// </summary>
    public static byte[] CreateSigningPayload(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.SigningKeyId))
            throw new InvalidDataException("A signing key fingerprint is required.");
        if (manifest.SignatureVersion is not (1 or 2))
            throw new InvalidDataException("Unsupported package signature version.");
        if (manifest.Files is null || manifest.Dependencies is null || manifest.Conflicts is null ||
            manifest.Files.Any(file => file is null ||
                string.IsNullOrWhiteSpace(file.SourcePath) ||
                string.IsNullOrWhiteSpace(file.DestinationPath) ||
                string.IsNullOrWhiteSpace(file.Sha256)))
            throw new InvalidDataException("A package manifest must contain complete file, dependency and conflict declarations.");

        var fingerprint = NormalizeFingerprint(manifest.SigningKeyId);
        var files = manifest.Files
            .OrderBy(file => file.SourcePath, StringComparer.Ordinal)
            .ThenBy(file => file.DestinationPath, StringComparer.Ordinal)
            .Select(file => new CanonicalFile(
                file.SourcePath, file.DestinationPath, file.Sha256.ToUpperInvariant(), file.SizeBytes))
            .ToArray();
        var dependencies = manifest.Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var conflicts = manifest.Conflicts.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        if (manifest.SignatureVersion == 1)
        {
            if (HasVersion2Metadata(manifest))
                throw new InvalidDataException(
                    "Signature version 1 cannot authenticate compatibility, ecology, or Story Mode metadata. Re-sign this manifest with signatureVersion 2.");

            var legacy = new CanonicalManifestV1(
                manifest.SchemaVersion, manifest.Id, manifest.Name, manifest.Author, manifest.SourceUri,
                manifest.Licence, manifest.Version, manifest.GameTarget, manifest.MinimumGameBuild,
                manifest.MaximumGameBuild, dependencies, conflicts, manifest.ExclusiveGroup, fingerprint, files);
            return JsonSerializer.SerializeToUtf8Bytes(legacy);
        }

        var ecology = manifest.Ecology is null ? null : new CanonicalEcology(
            manifest.Ecology.ReviewStatus, manifest.Ecology.ImpactType,
            manifest.Ecology.PreservesVanillaAnimalSpecies, manifest.Ecology.PreservesVanillaPlantSpecies,
            manifest.Ecology.PreservesVanillaVegetationSpecies, manifest.Ecology.PreservesVanillaModelIdentifiers,
            manifest.Ecology.AllowsNewSpecies, manifest.Ecology.AllowsSpeciesReplacement,
            manifest.Ecology.AllowsSpeciesRemoval, manifest.Ecology.ChangesSpeciesSpawnTables,
            manifest.Ecology.VanillaIdentityInventorySha256, manifest.Ecology.EvidenceUri);

        var story = manifest.StoryContent is null ? null : new CanonicalStoryContent(
            manifest.StoryContent.ContentType,
            manifest.StoryContent.MissionIds?.ToArray() ?? Array.Empty<string>(),
            manifest.StoryContent.ProtagonistRequirements?.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
            manifest.StoryContent.StartLocations?.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
            manifest.StoryContent.ProgressionNamespace, manifest.StoryContent.SaveCompatibility,
            manifest.StoryContent.RequiresSaveBackup, manifest.StoryContent.PreservesOriginalCampaign,
            manifest.StoryContent.VoiceLanguages?.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
            manifest.StoryContent.SubtitleLanguages?.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
            manifest.StoryContent.TestEvidenceUri);

        var canonical = new CanonicalManifestV2(
            2, manifest.SchemaVersion, manifest.Id, manifest.Name, manifest.Author, manifest.SourceUri,
            manifest.Licence, manifest.Version, manifest.GameTarget, manifest.MinimumGameBuild,
            manifest.MaximumGameBuild, dependencies, conflicts, manifest.ExclusiveGroup, fingerprint,
            manifest.ImpactAreas?.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
            manifest.CompatibilityVerified, manifest.PackageFormat, ecology, story, files);

        return JsonSerializer.SerializeToUtf8Bytes(canonical);
    }

    public static string ComputePublisherFingerprint(string publicKeyPem)
    {
        using var key = RsaPublicKey.Import(publicKeyPem);
        return RsaPublicKey.ComputeFingerprint(key);
    }

    private static bool HasVersion2Metadata(PackageManifest manifest) =>
        manifest.CompatibilityVerified ||
        (manifest.ImpactAreas?.Count > 0) ||
        manifest.Ecology is not null ||
        manifest.StoryContent is not null ||
        !string.Equals(manifest.PackageFormat, "HubZip", StringComparison.OrdinalIgnoreCase);

    private static PackageSignatureVerification Invalid(string? fingerprint, string message) =>
        new(PackageSignatureStatus.Invalid, null, fingerprint, message);

    private static string NormalizeFingerprint(string fingerprint)
    {
        var normalized = fingerprint.Trim().Replace(":", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException("The signing key ID must be a 64-character SHA-256 public-key fingerprint.");
        return normalized.ToUpperInvariant();
    }
}
