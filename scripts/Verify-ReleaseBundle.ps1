[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ZipPath,

    [string]$ZipChecksumPath
)

$ErrorActionPreference = 'Stop'
$ZipPath = [IO.Path]::GetFullPath($ZipPath)
if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "Release ZIP was not found: $ZipPath"
}
if (-not $ZipChecksumPath) {
    $ZipChecksumPath = "$ZipPath.sha256"
}
$ZipChecksumPath = [IO.Path]::GetFullPath($ZipChecksumPath)
if (-not (Test-Path -LiteralPath $ZipChecksumPath -PathType Leaf)) {
    throw "Release ZIP checksum was not found: $ZipChecksumPath"
}

$zipHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedZipChecksumLine = (Get-Content -LiteralPath $ZipChecksumPath -Raw).Trim()
if ($expectedZipChecksumLine -notmatch '^([a-fA-F0-9]{64})  ([^/\\]+)$') {
    throw 'The release ZIP checksum file has an invalid format.'
}
if ($Matches[1].ToLowerInvariant() -cne $zipHash -or
    $Matches[2] -cne [IO.Path]::GetFileName($ZipPath)) {
    throw 'The release ZIP does not match its external SHA-256 checksum.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $entries = @{}
    foreach ($entry in $archive.Entries) {
        $normalizedName = $entry.FullName.Replace('\', '/')
        if ($normalizedName.StartsWith('/') -or
            $normalizedName -match '(^|/)\.\.(/|$)' -or
            $normalizedName -match '(?i)(\.(pfx|p12|key)$|password|dpapi)') {
            throw "Unsafe or private release entry: $normalizedName"
        }
        if ($entries.ContainsKey($normalizedName)) {
            throw "Duplicate release entry: $normalizedName"
        }
        $entries[$normalizedName] = $entry
    }

    $requiredNames = @(
        'Install-RightAgent.ps1',
        'LICENSE',
        'README.md',
        'README.en.md',
        'RightAgent.cer',
        'SHA256SUMS.txt',
        'THIRD_PARTY_NOTICES.md'
    )
    foreach ($requiredName in $requiredNames) {
        if (-not $entries.ContainsKey($requiredName)) {
            throw "Required release entry is missing: $requiredName"
        }
    }

    $packages = @($entries.Keys | Where-Object { $_ -match '^RightAgent-[0-9.]+-x64\.msix$' })
    if ($packages.Count -ne 1) {
        throw "Expected exactly one RightAgent release MSIX, but found $($packages.Count)."
    }
    $commandPackages = @($entries.Keys | Where-Object { $_ -match '^RightAgent\.Command[0-9]{2}-[0-9.]+-x64\.msix$' })
    if ($commandPackages.Count -ne 16) {
        throw "Expected exactly 16 RightAgent command MSIX packages, but found $($commandPackages.Count)."
    }

    $checksumReader = [IO.StreamReader]::new($entries['SHA256SUMS.txt'].Open())
    try {
        $checksumLines = @($checksumReader.ReadToEnd() -split '\r?\n' | Where-Object { $_ })
    }
    finally {
        $checksumReader.Dispose()
    }

    $expectedChecksums = @{}
    foreach ($line in $checksumLines) {
        if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') {
            throw "Invalid SHA256SUMS line: $line"
        }
        $entryName = $Matches[2].Replace('\', '/')
        if ($expectedChecksums.ContainsKey($entryName)) {
            throw "Duplicate SHA256SUMS entry: $entryName"
        }
        $expectedChecksums[$entryName] = $Matches[1].ToLowerInvariant()
    }

    $contentNames = @($entries.Keys | Where-Object { $_ -cne 'SHA256SUMS.txt' })
    if ($expectedChecksums.Count -ne $contentNames.Count) {
        throw 'SHA256SUMS does not cover every release file exactly once.'
    }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($entryName in $contentNames) {
            if (-not $expectedChecksums.ContainsKey($entryName)) {
                throw "SHA256SUMS is missing: $entryName"
            }
            $entryStream = $entries[$entryName].Open()
            try {
                $actualHash = [BitConverter]::ToString($sha256.ComputeHash($entryStream)).Replace('-', '').ToLowerInvariant()
            }
            finally {
                $entryStream.Dispose()
            }
            if ($actualHash -cne $expectedChecksums[$entryName]) {
                throw "Release entry checksum mismatch: $entryName"
            }
        }
    }
    finally {
        $sha256.Dispose()
    }

    $certificateStream = [IO.MemoryStream]::new()
    $sourceCertificateStream = $entries['RightAgent.cer'].Open()
    try {
        $sourceCertificateStream.CopyTo($certificateStream)
    }
    finally {
        $sourceCertificateStream.Dispose()
    }
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificateStream.ToArray())
    $certificateStream.Dispose()
    if ($certificate.Subject -cne 'CN=RightAgent') {
        throw "Unexpected bundled certificate subject: $($certificate.Subject)"
    }

    $msixStream = [IO.MemoryStream]::new()
    $sourceMsixStream = $entries[$packages[0]].Open()
    try {
        $sourceMsixStream.CopyTo($msixStream)
    }
    finally {
        $sourceMsixStream.Dispose()
    }
    $msixStream.Position = 0
    $msixArchive = [IO.Compression.ZipArchive]::new($msixStream, [IO.Compression.ZipArchiveMode]::Read, $false)
    try {
        $manifestEntry = $msixArchive.GetEntry('AppxManifest.xml')
        $signatureEntry = $msixArchive.GetEntry('AppxSignature.p7x')
        if (-not $manifestEntry -or -not $signatureEntry -or $signatureEntry.Length -eq 0) {
            throw 'The bundled MSIX is missing its manifest or package signature.'
        }
        $manifestReader = [IO.StreamReader]::new($manifestEntry.Open())
        try {
            [xml]$manifest = $manifestReader.ReadToEnd()
        }
        finally {
            $manifestReader.Dispose()
        }
    }
    finally {
        $msixArchive.Dispose()
        $msixStream.Dispose()
    }

    if ([string]$manifest.Package.Identity.Name -cne 'RightAgent' -or
        [string]$manifest.Package.Identity.Publisher -cne $certificate.Subject) {
        throw 'The bundled MSIX identity does not match the bundled release certificate.'
    }

    $mainVersion = [string]$manifest.Package.Identity.Version
    $parsedVersion = [version]$mainVersion
    $displayVersion = "$($parsedVersion.Major).$($parsedVersion.Minor).$($parsedVersion.Build)"
    if ($parsedVersion.Revision -gt 0) {
        $displayVersion += ".$($parsedVersion.Revision)"
    }
    foreach ($slot in 0..15) {
        $slotText = $slot.ToString('D2')
        $commandEntryName = "RightAgent.Command$slotText-$displayVersion-x64.msix"
        if (-not $entries.ContainsKey($commandEntryName)) {
            throw "Release bundle is missing command package: $commandEntryName"
        }

        $commandMsixStream = [IO.MemoryStream]::new()
        $sourceCommandStream = $entries[$commandEntryName].Open()
        try {
            $sourceCommandStream.CopyTo($commandMsixStream)
        }
        finally {
            $sourceCommandStream.Dispose()
        }
        $commandMsixStream.Position = 0
        $commandArchive = [IO.Compression.ZipArchive]::new($commandMsixStream, [IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            $commandManifestEntry = $commandArchive.GetEntry('AppxManifest.xml')
            $commandSignatureEntry = $commandArchive.GetEntry('AppxSignature.p7x')
            if (-not $commandManifestEntry -or -not $commandSignatureEntry -or $commandSignatureEntry.Length -eq 0) {
                throw "Command package $slotText is missing its manifest or package signature."
            }
            $commandManifestReader = [IO.StreamReader]::new($commandManifestEntry.Open())
            try {
                [xml]$commandManifest = $commandManifestReader.ReadToEnd()
            }
            finally {
                $commandManifestReader.Dispose()
            }
        }
        finally {
            $commandArchive.Dispose()
            $commandMsixStream.Dispose()
        }

        if ([string]$commandManifest.Package.Identity.Name -cne "RightAgent.Command$slotText" -or
            [string]$commandManifest.Package.Identity.Publisher -cne $certificate.Subject -or
            [string]$commandManifest.Package.Identity.Version -cne $mainVersion) {
            throw "Command package $slotText identity does not match the main release package."
        }
    }

    Write-Host "Verified release bundle: $ZipPath"
    Write-Host "Version: $([string]$manifest.Package.Identity.Version)"
    Write-Host "Certificate: $($certificate.Thumbprint)"
    Write-Host "SHA256: $zipHash"
}
finally {
    $archive.Dispose()
}
