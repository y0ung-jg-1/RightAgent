[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Development', 'Release')]
    [string]$PackageIdentity = 'Development',

    [string]$PackagePath,
    [string]$CertificateThumbprint,

    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageHelpers.ps1')

$manifestPath = Get-RightAgentManifestPath -RepoRoot $repoRoot -PackageIdentity $PackageIdentity
[xml]$sourceManifest = Get-Content -LiteralPath $manifestPath -Raw
$expectedPublisher = [string]$sourceManifest.Package.Identity.Publisher
if ([string]::IsNullOrWhiteSpace($expectedPublisher)) {
    throw "The package manifest does not declare a publisher: $manifestPath"
}

if ($CertificateThumbprint) {
    $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    $certificatePath = "Cert:\CurrentUser\My\$normalizedThumbprint"
    $certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
} else {
    $certificate = Get-ChildItem -Path 'Cert:\CurrentUser\My' |
        Where-Object {
            $_.Subject -eq $expectedPublisher -and
            $_.HasPrivateKey -and
            $_.NotBefore -le (Get-Date) -and
            $_.NotAfter -gt (Get-Date)
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

if (-not $certificate) {
    $creationScript = if ($PackageIdentity -eq 'Release') {
        'scripts\New-ReleaseCertificate.ps1'
    } else {
        'scripts\New-DevCertificate.ps1'
    }
    throw "A valid signing certificate for '$expectedPublisher' was not found. Run $creationScript first."
}
if (-not $certificate.HasPrivateKey) {
    throw "The signing certificate does not have an accessible private key: $($certificate.Thumbprint)"
}
if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
    throw "The signing certificate is outside its validity period: $($certificate.Thumbprint)"
}
if ($certificate.Subject -cne $expectedPublisher) {
    throw "Certificate subject '$($certificate.Subject)' does not match package publisher '$expectedPublisher'."
}
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$ekuOids = @($certificate.EnhancedKeyUsageList | ForEach-Object {
    if ($_.ObjectId -is [Security.Cryptography.Oid]) {
        $_.ObjectId.Value
    } else {
        [string]$_.ObjectId
    }
})
if ($ekuOids -notcontains $codeSigningOid) {
    throw "Certificate $($certificate.Thumbprint) is not valid for code signing."
}

if (-not $PackagePath) {
    $PackagePath = Get-RightAgentPackagePath -RepoRoot $repoRoot -Configuration $Configuration -PackageIdentity $PackageIdentity
}
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw 'No package was found to sign.'
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
        [xml]$packagedManifest = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $archive.Dispose()
}

$packagedPublisher = [string]$packagedManifest.Package.Identity.Publisher
if ($packagedPublisher -cne $expectedPublisher) {
    throw "Packaged publisher '$packagedPublisher' does not match expected publisher '$expectedPublisher'."
}

$signTool = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Filter signtool.exe -Recurse -File |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $signTool) {
    throw 'x64 signtool.exe was not found.'
}

$timestampUri = $null
if ($PackageIdentity -eq 'Release') {
    if (-not [Uri]::TryCreate($TimestampServer, [UriKind]::Absolute, [ref]$timestampUri) -or
        $timestampUri.Scheme -notin 'http', 'https') {
        throw "Invalid RFC 3161 timestamp server URL: $TimestampServer"
    }
}

$existingSignature = Get-AuthenticodeSignature -LiteralPath $PackagePath
if ($existingSignature.Status -ne 'NotSigned') {
    throw "Package is already signed ($($existingSignature.Status)). Rebuild it before signing."
}

$unsignedBackupPath = [IO.Path]::GetTempFileName()
Copy-Item -LiteralPath $PackagePath -Destination $unsignedBackupPath -Force
try {
    $maximumAttempts = if ($PackageIdentity -eq 'Release') { 3 } else { 1 }
    $signed = $false
    foreach ($attempt in 1..$maximumAttempts) {
        Copy-Item -LiteralPath $unsignedBackupPath -Destination $PackagePath -Force
        $signArguments = @(
            'sign',
            '/fd', 'SHA256',
            '/sha1', $certificate.Thumbprint,
            '/s', 'My'
        )
        if ($PackageIdentity -eq 'Release') {
            $signArguments += @('/tr', $timestampUri.AbsoluteUri, '/td', 'SHA256')
        }
        $signArguments += $PackagePath

        & $signTool @signArguments
        if ($LASTEXITCODE -eq 0) {
            $signed = $true
            break
        }
        if ($attempt -lt $maximumAttempts) {
            Write-Warning "Signing attempt $attempt failed; restoring the unsigned package and retrying."
            Start-Sleep -Seconds 2
        }
    }
    if (-not $signed) {
        Copy-Item -LiteralPath $unsignedBackupPath -Destination $PackagePath -Force
        throw 'SignTool failed; the original unsigned package was restored.'
    }
}
finally {
    Remove-Item -LiteralPath $unsignedBackupPath -Force -ErrorAction Stop
}

$signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
$signerMatches =
    $null -ne $signature.SignerCertificate -and
    $signature.SignerCertificate.Thumbprint -eq $certificate.Thumbprint
if (-not $signerMatches -or $signature.Status -notin 'Valid', 'UnknownError') {
    throw "Signed package verification failed: $($signature.Status)"
}
if ($PackageIdentity -eq 'Release' -and -not $signature.TimeStamperCertificate) {
    throw 'The release package signature does not contain a timestamp.'
}

Write-Host "Signed ($PackageIdentity identity): $PackagePath"
Write-Host "Signer: $($certificate.Subject) [$($certificate.Thumbprint)]"
if ($signature.TimeStamperCertificate) {
    Write-Host "Timestamp authority: $($signature.TimeStamperCertificate.Subject)"
}
Write-Host "Signature status on this machine: $($signature.Status)"
