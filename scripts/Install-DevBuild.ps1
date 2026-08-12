[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$ResetInstalledPackage
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
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration -SkipTests:$SkipTests

# 3. Sign and install. The first install asks for administrator approval once to
#    trust the development certificate in Local Computer\Trusted People.
& (Join-Path $PSScriptRoot 'Sign-Package.ps1') -Configuration $Configuration

# Windows blocks replacing a package with different contents at the same version.
# Preserve LocalState for real upgrades/downgrades, and require an explicit opt-in
# before uninstalling a same-version development package.
$installedPackages = @(Get-AppxPackage -Name 'RightAgent.Dev' -ErrorAction SilentlyContinue)
if ($installedPackages.Count -gt 1) {
    throw "Expected at most one installed RightAgent.Dev package, but found $($installedPackages.Count)."
}
if ($installedPackages.Count -eq 1) {
    $installed = $installedPackages[0]
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'RightAgent.Package\Package.appxmanifest') -Raw
    $targetVersion = [version]$manifest.Package.Identity.Version
    $installedVersion = [version]$installed.Version
    if ($installedVersion -eq $targetVersion) {
        if (-not $ResetInstalledPackage) {
            throw "RightAgent.Dev $targetVersion is already installed. Increment the manifest version to preserve LocalState, or rerun with -ResetInstalledPackage to explicitly uninstall it and erase that package's settings."
        }

        Write-Warning "Resetting $($installed.PackageFullName); this erases that development package's LocalState settings."
        $installed | Remove-AppxPackage -ErrorAction Stop
    }
}

& (Join-Path $PSScriptRoot 'Install-DevPackage.ps1') -Configuration $Configuration

Write-Host ''
Write-Host 'Done. Right-click a folder (or a folder background) to use RightAgent.'
Write-Host 'If the menu does not change after an upgrade, close all Explorer windows or sign out once.'
