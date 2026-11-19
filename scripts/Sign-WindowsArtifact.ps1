[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:SIGNING_PFX_BASE64) -or
    [string]::IsNullOrWhiteSpace($env:SIGNING_PFX_PASSWORD)) {
    throw 'Signing secrets are not configured.'
}

$resolvedArtifact = (Resolve-Path -LiteralPath $ArtifactPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedArtifact -PathType Leaf)) {
    throw "Artifact does not exist: $ArtifactPath"
}

$pfxPath = Join-Path $env:RUNNER_TEMP ("gta-hub-signing-" + [Guid]::NewGuid().ToString('N') + '.pfx')
$certificate = $null

try {
    $pfxBytes = [Convert]::FromBase64String($env:SIGNING_PFX_BASE64)
    [IO.File]::WriteAllBytes($pfxPath, $pfxBytes)
    $securePassword = ConvertTo-SecureString $env:SIGNING_PFX_PASSWORD -AsPlainText -Force
    $imported = @(Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation 'Cert:\CurrentUser\My' -Password $securePassword)

    $certificate = $imported | Where-Object {
        $_.HasPrivateKey -and
        $_.NotBefore -le (Get-Date) -and
        $_.NotAfter -gt (Get-Date) -and
        @($_.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId.Value }) -contains '1.3.6.1.5.5.7.3.3'
    } | Select-Object -First 1

    if ($null -eq $certificate) {
        throw 'A currently valid code-signing certificate with a private key is required.'
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kitsRoot -PathType Container)) {
        throw "Windows SDK bin directory was not found: $kitsRoot"
    }

    $signTool = Get-ChildItem -LiteralPath $kitsRoot -Filter 'signtool.exe' -File -Recurse |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($signTool)) {
        throw 'The x64 Windows SDK signtool.exe was not found.'
    }

    & $signTool sign /fd SHA256 /td SHA256 /tr 'https://timestamp.digicert.com' /sha1 $certificate.Thumbprint /s My $resolvedArtifact
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed for $resolvedArtifact" }

    & $signTool verify /pa /v $resolvedArtifact
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signature verification failed for $resolvedArtifact" }

    Write-Host "Authenticode signature verified: $resolvedArtifact"
    Write-Host "Certificate thumbprint: $($certificate.Thumbprint)"
}
finally {
    if (Test-Path -LiteralPath $pfxPath) {
        Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $certificate) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $certificate.Thumbprint) -Force -ErrorAction SilentlyContinue
    }
    $pfxBytes = $null
    $securePassword = $null
    $env:SIGNING_PFX_BASE64 = $null
    $env:SIGNING_PFX_PASSWORD = $null
}
