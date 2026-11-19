[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$validatorPath = Join-Path $PSScriptRoot 'Validate-Catalogue.ps1'
if (-not (Test-Path -LiteralPath $validatorPath -PathType Leaf)) {
    throw "Catalogue validator is missing: $validatorPath"
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gta-hub-catalogue-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

function New-ValidCatalogue {
    return @{
        schemaVersion = 1
        gameTarget = 'GTA V Legacy'
        categories = @(@{ id = 'graphics'; name = 'Graphics' })
        packages = @(@{
            id = 'test-package'
            name = 'Test Package'
            category = 'graphics'
            author = 'Test Author'
            sourceUri = 'https://example.org/test-package'
            licence = 'Research-only test fixture'
            version = '1.0.0'
            status = 'Researching'
            installAdapter = 'Manifest-only test fixture'
            restoreStrategy = 'No files are deployed by this fixture'
            performanceTier = 'Unknown; test fixture only'
            notes = 'Synthetic fixture used only to test catalogue validation.'
            dependencies = @()
            conflicts = @()
            externalRequirements = @()
        })
        profiles = @(@{
            id = 'test-profile'
            name = 'Test Profile'
            enabledPackageIds = @('test-package')
        })
    }
}

function Copy-Catalogue([object]$Catalogue) {
    return ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $Catalogue -Depth 100 -Compress) -Depth 100
}

function Invoke-CatalogueCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][object]$Catalogue,
        [Parameter(Mandatory = $true)][bool]$ShouldPass
    )

    $path = Join-Path $testRoot ($Name + '.json')
    $json = ConvertTo-Json -InputObject $Catalogue -Depth 100
    [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
    $actualPass = $true
    $validationError = $null
    try {
        & $validatorPath -CataloguePath $path | Out-Null
    } catch {
        $actualPass = $false
        $validationError = $_.Exception.Message
    }

    if ($actualPass -ne $ShouldPass) {
        throw "Catalogue validator test '$Name' failed. Expected pass=$ShouldPass, actual pass=$actualPass. Validator error: $validationError"
    }
    Write-Host "PASS: $Name"
}

try {
    $valid = New-ValidCatalogue
    Invoke-CatalogueCase -Name 'valid-research-catalogue' -Catalogue $valid -ShouldPass $true

    $duplicate = Copy-Catalogue $valid
    $duplicate.packages += $duplicate.packages[0]
    Invoke-CatalogueCase -Name 'duplicate-package-id-rejected' -Catalogue $duplicate -ShouldPass $false

    $badSource = Copy-Catalogue $valid
    $badSource.packages[0].sourceUri = 'http://example.org/test-package'
    Invoke-CatalogueCase -Name 'non-https-source-rejected' -Catalogue $badSource -ShouldPass $false

    $badCategory = Copy-Catalogue $valid
    $badCategory.packages[0].category = 'missing-category'
    Invoke-CatalogueCase -Name 'unknown-category-rejected' -Catalogue $badCategory -ShouldPass $false

    $missingDependency = Copy-Catalogue $valid
    $missingDependency.packages[0].dependencies = @('missing-package')
    Invoke-CatalogueCase -Name 'missing-dependency-rejected' -Catalogue $missingDependency -ShouldPass $false

    $missingProfilePackage = Copy-Catalogue $valid
    $missingProfilePackage.profiles[0].enabledPackageIds = @('missing-package')
    Invoke-CatalogueCase -Name 'missing-profile-package-rejected' -Catalogue $missingProfilePackage -ShouldPass $false

    $unqualifiedApproval = Copy-Catalogue $valid
    $unqualifiedApproval.packages[0].status = 'Tested'
    $unqualifiedApproval.packages[0] | Add-Member -NotePropertyName sha256 -NotePropertyValue $null -Force
    $unqualifiedApproval.packages[0] | Add-Member -NotePropertyName minimumGameBuild -NotePropertyValue '1.0.0.0' -Force
    $unqualifiedApproval.packages[0] | Add-Member -NotePropertyName maximumGameBuild -NotePropertyValue '1.0.9.0' -Force
    $unqualifiedApproval.packages[0] | Add-Member -NotePropertyName installAdapter -NotePropertyValue 'Not implemented' -Force
    Invoke-CatalogueCase -Name 'tested-package-without-evidence-rejected' -Catalogue $unqualifiedApproval -ShouldPass $false

    Write-Host 'All catalogue validator tests passed.'
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
