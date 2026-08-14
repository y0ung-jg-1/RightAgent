[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CertificatePath,

    [string]$AppDirectory,

    [string[]]$CommandPackagePaths,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageHelpers.ps1')

$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "Certificate was not found: $CertificatePath"
}

if (-not $AppDirectory) {
    $AppDirectory = Get-RightAgentAppPublishPath -RepoRoot $repoRoot -Configuration Release
}
$AppDirectory = [IO.Path]::GetFullPath($AppDirectory)
$appExecutable = Join-Path $AppDirectory 'RightAgent.App.exe'
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "Unpackaged settings app was not found: $appExecutable"
}

if (-not $CommandPackagePaths) {
    $CommandPackagePaths = @(Get-RightAgentCommandPackagePaths `
        -RepoRoot $repoRoot `
        -Configuration Release `
        -PackageIdentity Release)
}
$CommandPackagePaths = @($CommandPackagePaths | ForEach-Object { [IO.Path]::GetFullPath($_) })
if ($CommandPackagePaths.Count -ne 16) {
    throw "Expected exactly 16 command packages, but found $($CommandPackagePaths.Count)."
}
foreach ($commandPackagePath in $CommandPackagePaths) {
    if (-not (Test-Path -LiteralPath $commandPackagePath -PathType Leaf)) {
        throw "Command package was not found: $commandPackagePath"
    }
}
& (Join-Path $PSScriptRoot 'Verify-CommandPackages.ps1') -Configuration Release -PackageIdentity Release

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
foreach ($signedPackagePath in $CommandPackagePaths) {
    $signature = Get-AuthenticodeSignature -LiteralPath $signedPackagePath
    $signerMatches =
        $null -ne $signature.SignerCertificate -and
        $signature.SignerCertificate.Thumbprint -eq $certificate.Thumbprint
    if (-not $signerMatches -or $signature.Status -notin 'Valid', 'UnknownError') {
        throw "Release package signature verification failed for '$signedPackagePath': $($signature.Status)"
    }
    if (-not $signature.TimeStamperCertificate) {
        throw "Release package signature does not contain the required RFC 3161 timestamp: $signedPackagePath"
    }
}

$packageVersion = Get-RightAgentPackageVersion -RepoRoot $repoRoot -PackageIdentity Release
$displayVersion = Get-RightAgentDisplayVersion -PackageVersion $packageVersion

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$expectedOutputDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'release'))
if (-not $OutputDirectory) {
    $OutputDirectory = $expectedOutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.Equals($expectedOutputDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output must be the repository's exact artifacts\release directory: $expectedOutputDirectory"
}
foreach ($candidate in $artifactsRoot, $OutputDirectory) {
    if (Test-Path -LiteralPath $candidate) {
        $candidateItem = Get-Item -LiteralPath $candidate -Force
        if (($candidateItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to clean through a release output reparse point: $candidate"
        }
    }
}
if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force -ErrorAction Stop
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

$stagingDirectory = Join-Path $OutputDirectory "RightAgent-$displayVersion-x64"
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
$appStagingDirectory = Join-Path $stagingDirectory 'App'
New-Item -ItemType Directory -Path $appStagingDirectory | Out-Null
Copy-Item -Path (Join-Path $AppDirectory '*') -Destination $appStagingDirectory -Recurse -Force
if (-not (Test-Path -LiteralPath (Join-Path $appStagingDirectory 'RightAgent.App.exe') -PathType Leaf)) {
    throw "Failed to stage the unpackaged settings app from '$AppDirectory'."
}
for ($slot = 0; $slot -lt $CommandPackagePaths.Count; ++$slot) {
    $slotText = $slot.ToString('D2')
    $commandReleaseName = "RightAgent.Command$slotText-$displayVersion-x64.msix"
    Copy-Item -LiteralPath $CommandPackagePaths[$slot] -Destination (Join-Path $stagingDirectory $commandReleaseName)
}
Copy-Item -LiteralPath $CertificatePath -Destination (Join-Path $stagingDirectory 'RightAgent.cer')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Release.ps1') -Destination (Join-Path $stagingDirectory 'Install-RightAgent.ps1')
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\SIDELOAD_INSTALL.md') -Destination (Join-Path $stagingDirectory 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\SIDELOAD_INSTALL.en.md') -Destination (Join-Path $stagingDirectory 'README.en.md')

$checksumLines = foreach ($file in Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File | Sort-Object FullName) {
    $stagingPrefix = $stagingDirectory.TrimEnd('\') + '\'
    if (-not $file.FullName.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release file escaped its staging directory: $($file.FullName)"
    }
    $relativePath = $file.FullName.Substring($stagingPrefix.Length).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relativePath"
}
$checksumPath = Join-Path $stagingDirectory 'SHA256SUMS.txt'
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding utf8

$zipPath = Join-Path $OutputDirectory "RightAgent-$displayVersion-x64.zip"
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipChecksumPath = "$zipPath.sha256"
Set-Content -LiteralPath $zipChecksumPath -Value "$zipHash  $([IO.Path]::GetFileName($zipPath))" -Encoding ascii
$stagingItem = Get-Item -LiteralPath $stagingDirectory -Force
if (($stagingItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to clean a release staging reparse point: $stagingDirectory"
}
Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction Stop

Write-Host "Release bundle: $zipPath"
Write-Host "SHA256: $zipHash"
