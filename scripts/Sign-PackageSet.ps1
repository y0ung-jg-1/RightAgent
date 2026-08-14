[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Development', 'Release')]
    [string]$PackageIdentity = 'Development',

    [string]$CertificateThumbprint,

    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageHelpers.ps1')

$commandPackagePaths = @(Get-RightAgentCommandPackagePaths `
    -RepoRoot $repoRoot `
    -Configuration $Configuration `
    -PackageIdentity $PackageIdentity)
$packagePaths = @($commandPackagePaths)
if ($packagePaths.Count -ne 16) {
    throw "Expected 16 command packages, but found $($packagePaths.Count)."
}

foreach ($packagePath in $packagePaths) {
    & (Join-Path $PSScriptRoot 'Sign-Package.ps1') `
        -Configuration $Configuration `
        -PackageIdentity $PackageIdentity `
        -PackagePath $packagePath `
        -CertificateThumbprint $CertificateThumbprint `
        -TimestampServer $TimestampServer
}

Write-Host "Signed complete RightAgent package set: $($packagePaths.Count) packages."
