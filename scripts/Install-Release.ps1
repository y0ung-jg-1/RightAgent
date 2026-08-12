[CmdletBinding()]
param(
    [string]$PackagePath,
    [string]$CertificatePath
)

$ErrorActionPreference = 'Stop'

if (-not (Get-PSDrive -Name 'Cert' -ErrorAction SilentlyContinue)) {
    throw 'The Cert: drive is unavailable. Run this script from a regular Windows PowerShell session.'
}

if (-not $PackagePath) {
    $packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.msix' -File)
    if ($packages.Count -ne 1) {
        throw "Expected exactly one MSIX next to this installer, but found $($packages.Count)."
    }
    $PackagePath = $packages[0].FullName
}
if (-not $CertificatePath) {
    $CertificatePath = Join-Path $PSScriptRoot 'RightAgent.cer'
}

$PackagePath = [IO.Path]::GetFullPath($PackagePath)
$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "RightAgent package was not found: $PackagePath"
}
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "RightAgent public certificate was not found: $CertificatePath"
}

$expectedCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
if ($expectedCertificate.Subject -cne 'CN=RightAgent') {
    throw "Unexpected release certificate subject: $($expectedCertificate.Subject)"
}
if ($expectedCertificate.NotBefore -gt (Get-Date) -or $expectedCertificate.NotAfter -le (Get-Date)) {
    throw 'The RightAgent release certificate is outside its validity period.'
}

$signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
$signerMatchesCertificate =
    $null -ne $signature.SignerCertificate -and
    $signature.SignerCertificate.Thumbprint -eq $expectedCertificate.Thumbprint
$isAcceptedUntrustedSignature =
    $signature.Status -eq 'UnknownError' -and
    $signerMatchesCertificate
if ($signature.Status -ne 'Valid' -and -not $isAcceptedUntrustedSignature) {
    throw "Package signature verification failed: $($signature.Status)"
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
if ([string]$manifest.Package.Identity.Name -cne 'RightAgent' -or
    [string]$manifest.Package.Identity.Publisher -cne 'CN=RightAgent') {
    throw 'The package identity is not the public RightAgent release identity.'
}

$trustedPeopleStore = 'Cert:\LocalMachine\TrustedPeople'
$trustedCertificate = Get-ChildItem -LiteralPath $trustedPeopleStore |
    Where-Object { $_.Thumbprint -eq $expectedCertificate.Thumbprint } |
    Select-Object -First 1

if (-not $trustedCertificate) {
    Write-Warning 'RightAgent uses a project-owned self-signed certificate. Administrator approval is required once to trust only its public certificate in Local Computer\Trusted People.'
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($isAdministrator) {
        Import-Certificate -FilePath $CertificatePath -CertStoreLocation $trustedPeopleStore | Out-Null
    } else {
        $escapedCertificatePath = $CertificatePath.Replace("'", "''")
        $elevatedTemplate = @'
$ErrorActionPreference = 'Stop'
try {
    Import-Certificate -FilePath '__CERTIFICATE_PATH__' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    exit 0
} catch {
    Write-Error $_
    exit 1
}
'@
        $elevatedCommand = $elevatedTemplate.Replace('__CERTIFICATE_PATH__', $escapedCertificatePath)
        $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($elevatedCommand))
        $powerShellExecutable = if ($PSVersionTable.PSEdition -eq 'Core') {
            Join-Path $PSHOME 'pwsh.exe'
        } else {
            Join-Path $PSHOME 'powershell.exe'
        }

        try {
            $elevatedProcess = Start-Process -FilePath $powerShellExecutable -Verb RunAs -ArgumentList @(
                '-NoLogo',
                '-NoProfile',
                '-NonInteractive',
                '-EncodedCommand',
                $encodedCommand
            ) -WindowStyle Hidden -Wait -PassThru
        }
        catch {
            throw 'Administrator approval was cancelled or could not be requested. RightAgent was not installed.'
        }
        if ($elevatedProcess.ExitCode -ne 0) {
            throw 'The RightAgent release certificate could not be trusted. RightAgent was not installed.'
        }
    }
}

$trustedCertificate = Get-ChildItem -LiteralPath $trustedPeopleStore |
    Where-Object { $_.Thumbprint -eq $expectedCertificate.Thumbprint } |
    Select-Object -First 1
if (-not $trustedCertificate) {
    throw 'The RightAgent release certificate is still not present in Local Computer\Trusted People.'
}

$dependencyDirectory = Join-Path $PSScriptRoot 'Dependencies\x64'
$dependencies = if (Test-Path -LiteralPath $dependencyDirectory -PathType Container) {
    @(Get-ChildItem -LiteralPath $dependencyDirectory -File |
        Where-Object { $_.Extension -in '.msix', '.appx' } |
        Select-Object -ExpandProperty FullName)
} else {
    @()
}

if ($dependencies.Count -gt 0) {
    Add-AppxPackage -Path $PackagePath -DependencyPath $dependencies -ForceApplicationShutdown -ForceUpdateFromAnyVersion
} else {
    Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown -ForceUpdateFromAnyVersion
}

Write-Host 'RightAgent installed. If Explorer cached the old menu, close all Explorer windows or sign out once.'
