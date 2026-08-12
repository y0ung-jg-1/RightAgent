[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [string]$CertificatePath,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$PackagePath = [IO.Path]::GetFullPath($PackagePath)
$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Package was not found: $PackagePath"
}
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "Certificate was not found: $CertificatePath"
}

& (Join-Path $PSScriptRoot 'Verify-PackageCompliance.ps1') -PackagePath $PackagePath

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
$signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
$signerMatches =
    $null -ne $signature.SignerCertificate -and
    $signature.SignerCertificate.Thumbprint -eq $certificate.Thumbprint
if (-not $signerMatches -or $signature.Status -notin 'Valid', 'UnknownError') {
    throw "Release package signature verification failed: $($signature.Status)"
}
if (-not $signature.TimeStamperCertificate) {
    throw 'The release package signature does not contain the required RFC 3161 timestamp.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    if (-not $manifestEntry) {
        throw 'The package does not contain AppxManifest.xml.'
    }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try {
        [xml]$manifest = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $archive.Dispose()
}

$identity = $manifest.Package.Identity
if ([string]$identity.Name -cne 'RightAgent' -or [string]$identity.Publisher -cne 'CN=RightAgent') {
    throw 'The signed package does not use the public RightAgent release identity.'
}
$version = [version]([string]$identity.Version)
$displayVersion = "$($version.Major).$($version.Minor).$($version.Build)"
if ($version.Revision -gt 0) {
    $displayVersion += ".$($version.Revision)"
}

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
$releasePackageName = "RightAgent-$displayVersion-x64.msix"
Copy-Item -LiteralPath $PackagePath -Destination (Join-Path $stagingDirectory $releasePackageName)
Copy-Item -LiteralPath $CertificatePath -Destination (Join-Path $stagingDirectory 'RightAgent.cer')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Release.ps1') -Destination (Join-Path $stagingDirectory 'Install-RightAgent.ps1')
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\SIDELOAD_INSTALL.md') -Destination (Join-Path $stagingDirectory 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\SIDELOAD_INSTALL.en.md') -Destination (Join-Path $stagingDirectory 'README.en.md')

$sourceDependencyDirectory = Join-Path (Split-Path -Parent $PackagePath) 'Dependencies\x64'
if (Test-Path -LiteralPath $sourceDependencyDirectory -PathType Container) {
    $targetDependencyDirectory = Join-Path $stagingDirectory 'Dependencies\x64'
    New-Item -ItemType Directory -Path $targetDependencyDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceDependencyDirectory '*') -Destination $targetDependencyDirectory -Recurse
}

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
