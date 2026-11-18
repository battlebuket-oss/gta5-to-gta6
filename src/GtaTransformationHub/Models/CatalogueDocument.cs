using System;
using System.Collections.Generic;

namespace GtaTransformationHub.Models;

public sealed record CatalogueDocument(
    int SchemaVersion,
    string GameTarget,
    string CatalogueVersion,
    IReadOnlyList<CatalogueCategory> Categories,
    IReadOnlyList<ModPackage> Packages,
    IReadOnlyList<CatalogueProfile> Profiles,
    string CataloguePolicy);

public sealed record CatalogueCategory(string Id, string Name);

public sealed record CatalogueProfile(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> EnabledPackageIds);

public sealed record ProfileValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
