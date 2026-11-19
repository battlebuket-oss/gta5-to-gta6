[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$CataloguePath = "src/GtaTransformationHub/Catalogue/catalogue.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $CataloguePath -PathType Leaf)) {
    throw "Catalogue file not found: $CataloguePath"
}

try {
    $catalogue = Get-Content -LiteralPath $CataloguePath -Raw | ConvertFrom-Json -Depth 100
} catch {
    throw "Catalogue is not valid JSON: $($_.Exception.Message)"
}

if ($catalogue.schemaVersion -ne 1) {
    throw "Unsupported catalogue schemaVersion '$($catalogue.schemaVersion)'."
}
if ([string]::IsNullOrWhiteSpace([string]$catalogue.gameTarget)) {
    throw "Catalogue gameTarget is required."
}
if (-not ($catalogue.categories -is [System.Array]) -or $catalogue.categories.Count -eq 0) {
    throw "Catalogue must contain at least one category."
}
if (-not ($catalogue.packages -is [System.Array])) {
    throw "Catalogue packages must be an array."
}

$categoryIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($category in $catalogue.categories) {
    if ([string]::IsNullOrWhiteSpace([string]$category.id) -or
        [string]::IsNullOrWhiteSpace([string]$category.name)) {
        throw "Every category requires a non-empty id and name."
    }
    if (-not $categoryIds.Add([string]$category.id)) {
        throw "Duplicate category id: $($category.id)"
    }
}

$packageIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($package in $catalogue.packages) {
    foreach ($field in @("id", "name", "category", "author", "sourceUri", "licence", "version", "status")) {
        if ([string]::IsNullOrWhiteSpace([string]$package.$field)) {
            throw "Package '$($package.id)' is missing required field '$field'."
        }
    }

    if (-not $packageIds.Add([string]$package.id)) {
        throw "Duplicate package id: $($package.id)"
    }
    if (-not $categoryIds.Contains([string]$package.category)) {
        throw "Package '$($package.id)' references unknown category '$($package.category)'."
    }

    $source = $null
    if (-not [Uri]::TryCreate([string]$package.sourceUri, [UriKind]::Absolute, [ref]$source) -or
        $source.Scheme -ne "https") {
        throw "Package '$($package.id)' must have an absolute HTTPS sourceUri."
    }

    foreach ($listName in @("dependencies", "conflicts", "externalRequirements")) {
        $values = @($package.$listName)
        if ($null -ne $package.$listName) {
            $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
            foreach ($value in $values) {
                if ([string]::IsNullOrWhiteSpace([string]$value)) {
                    throw "Package '$($package.id)' contains an empty $listName entry."
                }
                if (-not $seen.Add([string]$value)) {
                    throw "Package '$($package.id)' contains duplicate $listName entry '$value'."
                }
            }
        }
    }

    if ($package.status -eq "Approved") {
        if ([string]::IsNullOrWhiteSpace([string]$package.sha256) -or
            [string]$package.sha256 -notmatch '^[A-Fa-f0-9]{64}$') {
            throw "Approved package '$($package.id)' must have a verified 64-character SHA-256."
        }
        if ([string]$package.minimumGameBuild -notmatch '^\d+(\.\d+){1,3}$' -or
            [string]$package.maximumGameBuild -notmatch '^\d+(\.\d+){1,3}$') {
            throw "Approved package '$($package.id)' must pin numeric minimum and maximum game builds."
        }
        if ([string]$package.licence -match '(?i)(not yet|require review|pending|unknown|prohibit|all rights reserved)') {
            throw "Approved package '$($package.id)' has unresolved licence language."
        }
        if ([string]::IsNullOrWhiteSpace([string]$package.installAdapter) -or
            [string]$package.installAdapter -match '(?i)(not implemented|manual only)') {
            throw "Approved package '$($package.id)' does not have an implemented install adapter."
        }
    }
}

foreach ($package in $catalogue.packages) {
    foreach ($dependency in @($package.dependencies)) {
        if (-not $packageIds.Contains([string]$dependency)) {
            throw "Package '$($package.id)' references missing dependency '$dependency'."
        }
    }
    foreach ($conflict in @($package.conflicts)) {
        if ($packageIds.Contains([string]$conflict) -and $conflict -eq $package.id) {
            throw "Package '$($package.id)' cannot conflict with itself."
        }
    }
}

if ($null -ne $catalogue.profiles) {
    $profileIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($profile in $catalogue.profiles) {
        if ([string]::IsNullOrWhiteSpace([string]$profile.id) -or
            [string]::IsNullOrWhiteSpace([string]$profile.name)) {
            throw "Every profile requires a non-empty id and name."
        }
        if (-not $profileIds.Add([string]$profile.id)) {
            throw "Duplicate profile id: $($profile.id)"
        }
        foreach ($packageId in @($profile.enabledPackageIds)) {
            if (-not $packageIds.Contains([string]$packageId)) {
                throw "Profile '$($profile.id)' references missing package '$packageId'."
            }
        }
    }
}

Write-Host "Catalogue validation passed: $($catalogue.categories.Count) categories, $($catalogue.packages.Count) packages, $(@($catalogue.profiles).Count) profiles."
