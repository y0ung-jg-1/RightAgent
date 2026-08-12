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

# 2. Build the MSIX (managed and native tests run unless -SkipTests is given).
#    Note: Debug packages are named *_x64_Debug.msix, which the package-name
#    checks in Build/Sign/Install do not match. Use the default Release.
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration -SkipTests:$SkipTests

# 3. Sign and install. The first install asks for administrator approval once to
#    trust the development certificate in Local Computer\Trusted People.
& (Join-Path $PSScriptRoot 'Sign-Package.ps1') -Configuration $Configuration

# Windows blocks reinstalling a same-version package whose contents differ, so
# remove an existing installation first. Note: this resets the packaged
# LocalState settings of the previous installation.
$installed = Get-AppxPackage -Name 'RightAgent.Dev' -ErrorAction SilentlyContinue
if ($installed) {
    Write-Host "Removing the installed $($installed.PackageFullName) first (same-version reinstalls are blocked)..."
    $installed | Remove-AppxPackage
}

& (Join-Path $PSScriptRoot 'Install-DevPackage.ps1') -Configuration $Configuration

Write-Host ''
Write-Host 'Done. Right-click a folder (or a folder background) to use RightAgent.'
Write-Host 'If the menu does not change after an upgrade, close all Explorer windows or sign out once.'
