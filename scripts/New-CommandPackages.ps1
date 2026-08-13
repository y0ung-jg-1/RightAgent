[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Development', 'Release')]
    [string]$PackageIdentity = 'Development',

    [ValidateRange(1, 16)]
    [int]$SlotCount = 16
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageHelpers.ps1')

$manifestPath = Get-RightAgentManifestPath -RepoRoot $repoRoot -PackageIdentity $PackageIdentity
[xml]$mainManifest = Get-Content -LiteralPath $manifestPath -Raw
$mainIdentity = $mainManifest.Package.Identity
$mainPackageName = [string]$mainIdentity.Name
$publisher = [string]$mainIdentity.Publisher
$version = [string]$mainIdentity.Version
if ([string]::IsNullOrWhiteSpace($mainPackageName) -or
    [string]::IsNullOrWhiteSpace($publisher) -or
    [string]::IsNullOrWhiteSpace($version)) {
    throw "The main package manifest has an incomplete identity: $manifestPath"
}

$templatePath = Join-Path $repoRoot 'RightAgent.CommandPackage\Package.appxmanifest.template'
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Command package manifest template was not found: $templatePath"
}
$manifestTemplate = [IO.File]::ReadAllText($templatePath)

$binaryDirectory = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\bin\$Configuration\x64"))
$shellPath = Join-Path $binaryDirectory 'RightAgent.Shell.dll'
$launcherPath = Join-Path $binaryDirectory 'RightAgent.Launcher.exe'
foreach ($requiredBinary in @($shellPath, $launcherPath)) {
    if (-not (Test-Path -LiteralPath $requiredBinary -PathType Leaf)) {
        throw "Required command package binary was not found: $requiredBinary"
    }
}

$windowsKitsBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeAppx = Get-ChildItem -LiteralPath $windowsKitsBin -Filter makeappx.exe -Recurse -File |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $makeAppx) {
    throw 'x64 makeappx.exe was not found.'
}

$packageRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\package\$Configuration"))
$outputDirectory = [IO.Path]::GetFullPath((Join-Path $packageRoot 'Commands'))
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $packageRoot '.command-staging'))
foreach ($candidate in @($packageRoot, $outputDirectory, $stagingRoot)) {
    if (-not $candidate.StartsWith($packageRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and
        -not $candidate.Equals($packageRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use a command package path outside the package root: $candidate"
    }
    if (Test-Path -LiteralPath $candidate) {
        $candidateItem = Get-Item -LiteralPath $candidate -Force
        if (($candidateItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to use a command package path that is a reparse point: $candidate"
        }
    }
}
foreach ($generatedDirectory in @($outputDirectory, $stagingRoot)) {
    if (Test-Path -LiteralPath $generatedDirectory) {
        Remove-Item -LiteralPath $generatedDirectory -Recurse -Force -ErrorAction Stop
    }
    New-Item -ItemType Directory -Path $generatedDirectory -Force | Out-Null
}

$logoSourceDirectory = Join-Path $repoRoot 'RightAgent.Package\Assets'
$licensePath = Join-Path $repoRoot 'LICENSE'
$noticesPath = Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md'
$utf8WithoutBom = [Text.UTF8Encoding]::new($false)

try {
    foreach ($slot in 0..($SlotCount - 1)) {
        $slotText = $slot.ToString('D2')
        $classId = 'F7E08D{0:X2}-676E-4D4B-950A-5B4451E19E3C' -f (0x6D + $slot)
        $packageName = "$mainPackageName.Command$slotText"
        $packageStaging = Join-Path $stagingRoot $slotText
        $assetsDirectory = Join-Path $packageStaging 'Assets'
        $agentAssetsDirectory = Join-Path $assetsDirectory 'Agents'
        New-Item -ItemType Directory -Path $agentAssetsDirectory -Force | Out-Null

        $generatedManifest = $manifestTemplate.
            Replace('__PACKAGE_NAME__', $packageName).
            Replace('__PUBLISHER__', $publisher).
            Replace('__VERSION__', $version).
            Replace('__SLOT__', $slotText).
            Replace('__CLSID__', $classId)
        if ($generatedManifest -match '__[A-Z_]+__') {
            throw "Command package manifest still contains an unresolved token for slot $slotText."
        }
        [IO.File]::WriteAllText((Join-Path $packageStaging 'AppxManifest.xml'), $generatedManifest, $utf8WithoutBom)

        Copy-Item -LiteralPath $shellPath -Destination (Join-Path $packageStaging 'RightAgent.Shell.dll')
        Copy-Item -LiteralPath $launcherPath -Destination (Join-Path $packageStaging 'RightAgent.Launcher.exe')
        Copy-Item -LiteralPath (Join-Path $logoSourceDirectory 'Square44x44Logo.png') -Destination $assetsDirectory
        Copy-Item -LiteralPath (Join-Path $logoSourceDirectory 'Square150x150Logo.png') -Destination $assetsDirectory
        Copy-Item -LiteralPath (Join-Path $logoSourceDirectory 'StoreLogo.png') -Destination $assetsDirectory
        Copy-Item -Path (Join-Path $logoSourceDirectory 'Agents\*.ico') -Destination $agentAssetsDirectory
        Copy-Item -LiteralPath $licensePath -Destination $packageStaging
        Copy-Item -LiteralPath $noticesPath -Destination $packageStaging

        $packagePath = Join-Path $outputDirectory "$packageName`_${version}_x64.msix"
        & $makeAppx pack /d $packageStaging /p $packagePath /o
        if ($LASTEXITCODE -ne 0) {
            throw "makeappx failed for command package slot $slotText."
        }
        Write-Host "Built command package $slotText`: $packagePath"
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        $stagingItem = Get-Item -LiteralPath $stagingRoot -Force
        if (($stagingItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to clean a command package staging reparse point: $stagingRoot"
        }
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction Stop
    }
}
