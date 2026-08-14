[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Get-PSDrive -Name 'Cert' -ErrorAction SilentlyContinue)) {
    throw 'The Cert: drive is unavailable in this PowerShell host. Run this script from a regular PowerShell window.'
}

# 1. Development signing certificate (one-time). A random password keeps this
#    non-interactive; signing uses the certificate store, never the PFX password.
$certificate = Get-ChildItem -Path 'Cert:\CurrentUser\My' |
    Where-Object { $_.Subject -eq 'CN=RightAgent Dev' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
$cerPath = Join-Path $repoRoot '.local\signing\RightAgent.Dev.cer'
if (-not $certificate -or -not (Test-Path -LiteralPath $cerPath -PathType Leaf)) {
    Write-Host 'Creating the RightAgent development signing certificate (one-time step)...'
    $password = New-Object System.Security.SecureString
    foreach ($character in [Guid]::NewGuid().ToString('N').ToCharArray()) { $password.AppendChar($character) }
    & (Join-Path $PSScriptRoot 'New-DevCertificate.ps1') -Password $password
}

# 2. Build the MSIX package set (managed and native tests run unless -SkipTests is given).
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration -SkipTests:$SkipTests

# 3. Sign and install. The first install asks for administrator approval once to
#    trust the development certificate in Local Computer\Trusted People.
& (Join-Path $PSScriptRoot 'Sign-PackageSet.ps1') -Configuration $Configuration

$legacyDev = @(Get-AppxPackage -Name 'RightAgent.Dev' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ceq 'RightAgent.Dev' -and $_.Publisher -ceq 'CN=RightAgent Dev' })
if ($legacyDev.Count -gt 0) {
    Write-Host 'Removing leftover packaged RightAgent.Dev settings app...'
    foreach ($installed in $legacyDev) {
        $installed | Remove-AppxPackage -ErrorAction Stop
    }
}

& (Join-Path $PSScriptRoot 'Install-DevPackage.ps1') -Configuration $Configuration

Write-Host ''
Write-Host 'Done. Right-click a folder (or a folder background) to use RightAgent.'
Write-Host 'If the menu does not change after an upgrade, close all Explorer windows or sign out once.'
