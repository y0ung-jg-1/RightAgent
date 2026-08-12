[CmdletBinding()]
param(
    [string]$BundlePath,
    [string]$OutputDirectory,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [switch]$SkipSigning
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $BundlePath) {
    $releaseDirectory = Join-Path $repoRoot 'artifacts\release'
    $bundles = @(Get-ChildItem -LiteralPath $releaseDirectory -Filter 'RightAgent-*-x64.zip' -File -ErrorAction Stop)
    if ($bundles.Count -ne 1) {
        throw "Expected exactly one release ZIP in '$releaseDirectory', but found $($bundles.Count)."
    }
    $BundlePath = $bundles[0].FullName
}
$BundlePath = [IO.Path]::GetFullPath($BundlePath)
if (-not (Test-Path -LiteralPath $BundlePath -PathType Leaf)) {
    throw "Release ZIP was not found: $BundlePath"
}

& (Join-Path $PSScriptRoot 'Verify-ReleaseBundle.ps1') -ZipPath $BundlePath

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$installerRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'installer'))
if (-not $OutputDirectory) {
    $OutputDirectory = $installerRoot
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.Equals($installerRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer output must be the repository's exact artifacts\installer directory: $installerRoot"
}

$stagingDirectory = [IO.Path]::GetFullPath((Join-Path $installerRoot 'staging'))
if (-not [IO.Directory]::GetParent($stagingDirectory).FullName.Equals($installerRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use an unexpected installer staging directory: $stagingDirectory"
}
foreach ($candidate in @($artifactsRoot, $installerRoot, $stagingDirectory)) {
    if (Test-Path -LiteralPath $candidate) {
        $candidateItem = Get-Item -LiteralPath $candidate -Force
        if (($candidateItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to use an installer path that is a reparse point: $candidate"
        }
    }
}
if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction Stop
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

try {
    Expand-Archive -LiteralPath $BundlePath -DestinationPath $stagingDirectory -Force

    $packages = @(Get-ChildItem -LiteralPath $stagingDirectory -Filter 'RightAgent-*-x64.msix' -File)
    if ($packages.Count -ne 1) {
        throw "Expected exactly one RightAgent MSIX in the release bundle, but found $($packages.Count)."
    }
    $certificatePath = Join-Path $stagingDirectory 'RightAgent.cer'
    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
        throw 'The release bundle does not contain RightAgent.cer.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packageArchive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $manifestEntry = $packageArchive.GetEntry('AppxManifest.xml')
        if (-not $manifestEntry) {
            throw 'The bundled MSIX does not contain AppxManifest.xml.'
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
        $packageArchive.Dispose()
    }

    $packageVersion = [version]([string]$manifest.Package.Identity.Version)
    $displayVersion = "$($packageVersion.Major).$($packageVersion.Minor).$($packageVersion.Build)"
    if ($packageVersion.Revision -gt 0) {
        $displayVersion += ".$($packageVersion.Revision)"
    }

    $isccCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $iscc = $isccCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if (-not $iscc) {
        throw 'Inno Setup 6 compiler was not found. Install JRSoftware.InnoSetup 6.7.3 first.'
    }

    $languageCacheDirectory = Join-Path $repoRoot '.local\installer'
    $chineseLanguageFile = Join-Path $languageCacheDirectory 'ChineseSimplified.isl'
    $chineseLanguageUri = 'https://raw.githubusercontent.com/jrsoftware/issrc/791ae13f404dd74012fe7ad6f660521dcfb815b7/Files/Languages/ChineseSimplified.isl'
    $expectedLanguageHash = 'e0b0b350e2245f3c5e65586dfe43d574f6e7f06f2261149aba284954b3fc9a8d'
    $languageHash = if (Test-Path -LiteralPath $chineseLanguageFile -PathType Leaf) {
        (Get-FileHash -LiteralPath $chineseLanguageFile -Algorithm SHA256).Hash.ToLowerInvariant()
    } else {
        $null
    }
    if ($languageHash -cne $expectedLanguageHash) {
        New-Item -ItemType Directory -Path $languageCacheDirectory -Force | Out-Null
        $downloadPath = "$chineseLanguageFile.download"
        try {
            Invoke-WebRequest -Uri $chineseLanguageUri -OutFile $downloadPath
            $downloadHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($downloadHash -cne $expectedLanguageHash) {
                throw "The downloaded Inno Setup language file has an unexpected SHA-256: $downloadHash"
            }
            Move-Item -LiteralPath $downloadPath -Destination $chineseLanguageFile -Force
        }
        finally {
            if (Test-Path -LiteralPath $downloadPath -PathType Leaf) {
                Remove-Item -LiteralPath $downloadPath -Force -ErrorAction Stop
            }
        }
    }

    $installerScript = Join-Path $repoRoot 'installer\RightAgent.iss'
    & $iscc "/DPayloadDir=$stagingDirectory" "/DOutputDir=$OutputDirectory" "/DAppVersion=$displayVersion" "/DPackageVersion=$packageVersion" "/DChineseLanguageFile=$chineseLanguageFile" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw 'Inno Setup compilation failed.'
    }

    $setupPath = Join-Path $OutputDirectory "RightAgent-$displayVersion-x64-Setup.exe"
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
        throw "The expected setup executable was not produced: $setupPath"
    }

    if (-not $SkipSigning) {
        $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
        $privateCertificatePath = "Cert:\CurrentUser\My\$($publicCertificate.Thumbprint)"
        $privateCertificate = Get-Item -LiteralPath $privateCertificatePath -ErrorAction SilentlyContinue
        if (-not $privateCertificate -or -not $privateCertificate.HasPrivateKey) {
            throw "The private release certificate is unavailable: $($publicCertificate.Thumbprint)"
        }

        $signTool = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Filter signtool.exe -Recurse -File |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not $signTool) {
            throw 'x64 signtool.exe was not found.'
        }

        $timestampUri = $null
        if (-not [Uri]::TryCreate($TimestampServer, [UriKind]::Absolute, [ref]$timestampUri) -or
            $timestampUri.Scheme -notin 'http', 'https') {
            throw "Invalid RFC 3161 timestamp server URL: $TimestampServer"
        }

        & $signTool sign /fd SHA256 /sha1 $privateCertificate.Thumbprint /s My /tr $timestampUri.AbsoluteUri /td SHA256 $setupPath
        if ($LASTEXITCODE -ne 0) {
            throw 'Signing the setup executable failed.'
        }

        $signature = Get-AuthenticodeSignature -LiteralPath $setupPath
        if ($null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne $publicCertificate.Thumbprint -or
            $signature.Status -notin 'Valid', 'UnknownError' -or
            $null -eq $signature.TimeStamperCertificate) {
            throw "Setup signature verification failed: $($signature.Status)"
        }
    }

    $setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = "$setupPath.sha256"
    Set-Content -LiteralPath $checksumPath -Value "$setupHash  $([IO.Path]::GetFileName($setupPath))" -Encoding ascii

    Write-Host "Setup executable: $setupPath"
    Write-Host "SHA256: $setupHash"
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        $stagingItem = Get-Item -LiteralPath $stagingDirectory -Force
        if (($stagingItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to clean an installer staging reparse point: $stagingDirectory"
        }
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction Stop
    }
}
