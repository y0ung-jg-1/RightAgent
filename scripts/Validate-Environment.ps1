[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Write-Check {
    param([string]$Name, [bool]$Passed, [string]$Details)
    $status = if ($Passed) { '[OK]' } else { '[MISSING]' }
    Write-Host "$status $Name - $Details"
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$sdks = if ($dotnet) { @(& dotnet --list-sdks) } else { @() }
$hasDotNet10 = $sdks | Where-Object { $_ -match '^10\.0\.' }
$sdkDetails = if ($sdks.Count -gt 0) { $sdks -join '; ' } else { 'dotnet not found' }
Write-Check '.NET 10 SDK' ([bool]$hasDotNet10) $sdkDetails

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = if (Test-Path -LiteralPath $vswhere) {
    & $vswhere -latest -products * -version '[18.0,19.0)' -property installationPath
} else { $null }
$msbuild = if ($vsPath) { Join-Path $vsPath 'MSBuild\Current\Bin\amd64\MSBuild.exe' } else { $null }
$vsDetails = if ($vsPath) { $vsPath } else { 'not found or installation incomplete' }
Write-Check 'Visual Studio 2026' ([bool]($vsPath -and (Test-Path -LiteralPath $msbuild))) $vsDetails

$minWindowsSdk = [version]'10.0.26100.0'
$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$includeRoot = Join-Path $kitsRoot 'Include'
$sdkCandidates = @()
if (Test-Path -LiteralPath $includeRoot) {
    $sdkCandidates = @(
        Get-ChildItem -LiteralPath $includeRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' -and [version]$_.Name -ge $minWindowsSdk } |
            Sort-Object { [version]$_.Name } -Descending
    )
}
$selectedSdk = $null
foreach ($candidate in $sdkCandidates) {
    $bin = Join-Path $kitsRoot "bin\$($candidate.Name)\x64"
    $required = @(
        (Join-Path $candidate.FullName 'um\windows.h'),
        (Join-Path $bin 'MrmSupport.dll'),
        (Join-Path $bin 'MakeAppx.exe'),
        (Join-Path $bin 'MakePri.exe'),
        (Join-Path $bin 'SignTool.exe')
    )
    if ($required | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }) {
        continue
    }
    $selectedSdk = $candidate
    break
}
$sdkRoot = if ($selectedSdk) { $selectedSdk.FullName } else { Join-Path $includeRoot '10.0.26100.0' }
$sdkDetails = if ($selectedSdk) { $selectedSdk.FullName } else { "need Windows SDK $minWindowsSdk or newer with um headers" }
Write-Check 'Windows 11 SDK' ([bool]$selectedSdk) $sdkDetails

$sdkBin = if ($selectedSdk) { Join-Path $kitsRoot "bin\$($selectedSdk.Name)\x64" } else { Join-Path $kitsRoot 'bin\10.0.26100.0\x64' }
$packagingTools = @(
    (Join-Path $sdkBin 'MrmSupport.dll'),
    (Join-Path $sdkBin 'MakeAppx.exe'),
    (Join-Path $sdkBin 'MakePri.exe'),
    (Join-Path $sdkBin 'SignTool.exe')
)
$missingPackagingTools = @($packagingTools | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
$packagingDetails = if ($missingPackagingTools.Count -eq 0) { $sdkBin } else { 'missing: ' + ($missingPackagingTools -join ', ') }
Write-Check 'SDK MSIX packaging tools' ($missingPackagingTools.Count -eq 0) $packagingDetails

$desktopBridge = if ($vsPath) { Join-Path $vsPath 'MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.targets' } else { $null }
$bridgeDetails = if ($desktopBridge) { $desktopBridge } else { 'Visual Studio 2026 not found' }
Write-Check 'MSIX / WAP targets' ([bool]($desktopBridge -and (Test-Path -LiteralPath $desktopBridge))) $bridgeDetails

$v145Marker = if ($vsPath) { Join-Path $vsPath 'VC\Auxiliary\Build\Microsoft.VCToolsVersion.v145.default.txt' } else { $null }
$v145Details = if ($v145Marker) { $v145Marker } else { 'Visual Studio 2026 not found' }
Write-Check 'MSVC v145 x64 tools' ([bool]($v145Marker -and (Test-Path -LiteralPath $v145Marker))) $v145Details

foreach ($command in 'git', 'wt', 'claude', 'codex', 'kimi', 'cursor-agent') {
    $resolved = Get-Command $command -ErrorAction SilentlyContinue
    $resolvedDetails = if ($resolved) { $resolved.Source } else { 'not found' }
    Write-Check $command ([bool]$resolved) $resolvedDetails
}

if (-not ($hasDotNet10 -and $vsPath -and (Test-Path -LiteralPath $msbuild) -and (Test-Path -LiteralPath $sdkRoot) -and $missingPackagingTools.Count -eq 0 -and (Test-Path -LiteralPath $desktopBridge) -and (Test-Path -LiteralPath $v145Marker))) {
    Write-Error 'RightAgent build prerequisites are incomplete. Install them, open a new terminal, and run this script again.'
}
