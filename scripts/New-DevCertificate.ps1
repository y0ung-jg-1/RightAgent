[CmdletBinding()]
param(
    [SecureString]$Password
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repoRoot '.local\signing'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

if (-not $Password) {
    $Password = Read-Host 'Choose a password for the local PFX' -AsSecureString
}

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject 'CN=RightAgent Dev' `
    -FriendlyName 'RightAgent development signing' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears(2) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')

$pfxPath = Join-Path $outputDirectory 'RightAgent.Dev.pfx'
$cerPath = Join-Path $outputDirectory 'RightAgent.Dev.cer'
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $Password | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT | Out-Null

Write-Host "PFX: $pfxPath"
Write-Host "CER: $cerPath"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host 'The .local directory is gitignored. Do not commit or share the PFX.'
