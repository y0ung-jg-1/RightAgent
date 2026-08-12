[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [SecureString]$Password
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if ($OutputDirectory.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $OutputDirectory.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The durable certificate backup must be outside the RightAgent repository.'
}
if (-not $Password) {
    $Password = Read-Host 'Choose a durable backup password and save it in your password manager' -AsSecureString
}

$certificate = Get-ChildItem -Path 'Cert:\CurrentUser\My' |
    Where-Object {
        $_.Subject -eq 'CN=RightAgent' -and
        $_.HasPrivateKey -and
        $_.NotAfter -gt (Get-Date)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if (-not $certificate) {
    throw 'A valid RightAgent release certificate was not found. Run scripts\New-ReleaseCertificate.ps1 first.'
}

if (Test-Path -LiteralPath $OutputDirectory) {
    $outputItem = Get-Item -LiteralPath $OutputDirectory -Force
    if (-not $outputItem.PSIsContainer) {
        throw "Backup target is not a directory: $OutputDirectory"
    }
    if (($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to export a release key through a reparse point: $OutputDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$suffix = $certificate.Thumbprint.ToLowerInvariant()
$pfxPath = Join-Path $OutputDirectory "RightAgent-release-$suffix.pfx"
$cerPath = Join-Path $OutputDirectory "RightAgent-release-$suffix.cer"
$infoPath = Join-Path $OutputDirectory "RightAgent-release-$suffix.txt"
foreach ($path in $pfxPath, $cerPath, $infoPath) {
    if (Test-Path -LiteralPath $path) {
        throw "Refusing to overwrite an existing backup file: $path"
    }
}

Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $Password | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT | Out-Null
$information = @(
    'RightAgent release signing certificate backup',
    "Subject: $($certificate.Subject)",
    "Thumbprint: $($certificate.Thumbprint)",
    "Valid from: $($certificate.NotBefore.ToUniversalTime().ToString('u'))",
    "Valid through: $($certificate.NotAfter.ToUniversalTime().ToString('u'))",
    '',
    'The PFX contains the private signing key. Store it offline and restrict access.',
    'Store its password separately in a password manager. Never commit or publish the PFX.'
)
Set-Content -LiteralPath $infoPath -Value $information -Encoding utf8

Write-Host "Encrypted PFX backup: $pfxPath"
Write-Host "Public certificate: $cerPath"
Write-Host "Backup record: $infoPath"
Write-Warning 'The password is not recoverable from this backup. Save it separately now.'
