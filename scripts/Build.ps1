[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Development', 'Release')]
    [string]$PackageIdentity = 'Development',

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageHelpers.ps1')

# Visual Studio's Appx packaging task assumes this standard Windows variable is
# present and dereferences it without a null check. Some automation hosts omit it.
if ([string]::IsNullOrWhiteSpace($env:PROCESSOR_ARCHITECTURE)) {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'RightAgent requires 64-bit Windows.'
    }
    $env:PROCESSOR_ARCHITECTURE = 'AMD64'
}

& (Join-Path $PSScriptRoot 'Verify-PackageManifests.ps1')
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

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$packageBase = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'package'))
$packageOutput = [IO.Path]::GetFullPath((Join-Path $packageBase $Configuration))
if (-not [IO.Directory]::GetParent($packageOutput).FullName.Equals($packageBase, [StringComparison]::OrdinalIgnoreCase) -or
    -not [IO.Path]::GetFileName($packageOutput).Equals($Configuration, [StringComparison]::Ordinal)) {
    throw "Refusing to clean an unexpected package output directory: $packageOutput"
}
foreach ($candidate in @($artifactsRoot, $packageBase, $packageOutput)) {
    if (Test-Path -LiteralPath $candidate) {
        $candidateItem = Get-Item -LiteralPath $candidate -Force
        if (($candidateItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to clean through a package output reparse point: $candidate"
        }
    }
}
if (Test-Path -LiteralPath $packageOutput) {
    Write-Host "Cleaning package output: $packageOutput"
    Remove-Item -LiteralPath $packageOutput -Recurse -Force -ErrorAction Stop
}

Push-Location $repoRoot
try {
    & $msbuild '.\RightAgent.Package\RightAgent.Package.wapproj' /restore /m /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 "/p:RightAgentPackageIdentity=$PackageIdentity" "/p:SolutionDir=$repoRoot\"
    if ($LASTEXITCODE -ne 0) { throw 'MSIX build failed.' }
}
finally {
    Pop-Location
}

$packagePath = Get-RightAgentPackagePath -RepoRoot $repoRoot -Configuration $Configuration -PackageIdentity $PackageIdentity
& (Join-Path $PSScriptRoot 'Verify-PackageCompliance.ps1') -PackagePath $packagePath
Write-Host "Built ($PackageIdentity identity): $packagePath"
