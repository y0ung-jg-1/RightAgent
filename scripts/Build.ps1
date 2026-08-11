[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# Visual Studio's Appx packaging task assumes this standard Windows variable is
# present and dereferences it without a null check. Some automation hosts omit it.
if ([string]::IsNullOrWhiteSpace($env:PROCESSOR_ARCHITECTURE)) {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'RightAgent requires 64-bit Windows.'
    }
    $env:PROCESSOR_ARCHITECTURE = 'AMD64'
}

& (Join-Path $PSScriptRoot 'Validate-Environment.ps1')
if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'Test.ps1') -Configuration $Configuration
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -products * -version '[18.0,19.0)' -property installationPath
$msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
    $msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
}

Push-Location $repoRoot
try {
    & $msbuild '.\RightAgent.Package\RightAgent.Package.wapproj' /restore /m /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 "/p:SolutionDir=$repoRoot\"
    if ($LASTEXITCODE -ne 0) { throw 'MSIX build failed.' }
}
finally {
    Pop-Location
}

$packages = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "artifacts\package\$Configuration") -Recurse -File |
    Where-Object { $_.Name -match '^RightAgent\.Package_.+_x64\.(msix|appx)$' -and $_.DirectoryName -notmatch '\\Dependencies(\\|$)' })
if ($packages.Count -eq 0) {
    throw 'The build completed but no MSIX/AppX package was found.'
}
$packages | ForEach-Object { Write-Host "Built: $($_.FullName)" }
