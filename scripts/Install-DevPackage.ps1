[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$cerPath = Join-Path $repoRoot '.local\signing\RightAgent.Dev.cer'
if (-not (Test-Path -LiteralPath $cerPath -PathType Leaf)) {
    throw 'Development certificate not found. Run scripts\New-DevCertificate.ps1 first.'
}
if (-not $PackagePath) {
    $packageRoot = Join-Path $repoRoot "artifacts\package\$Configuration"
    $PackagePath = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Where-Object { $_.Name -match '^RightAgent\.Package_.+_x64\.(msix|appx)$' -and $_.DirectoryName -notmatch '\\Dependencies(\\|$)' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $PackagePath -or -not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw 'No signed package was found.'
}

Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
$signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
if ($signature.Status -ne 'Valid') {
    throw "Package signature is not valid: $($signature.Status)"
}

$dependencyDirectory = Join-Path (Split-Path -Parent $PackagePath) 'Dependencies\x64'
$dependencies = if (Test-Path -LiteralPath $dependencyDirectory -PathType Container) {
    @(Get-ChildItem -LiteralPath $dependencyDirectory -File | Where-Object { $_.Extension -in '.msix', '.appx' } | Select-Object -ExpandProperty FullName)
} else { @() }
if ($dependencies.Count -gt 0) {
    Add-AppxPackage -Path $PackagePath -DependencyPath $dependencies -ForceApplicationShutdown
} else {
    Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown
}
Write-Host 'RightAgent installed. If Explorer cached the old menu, close all Explorer windows or sign out once.'
