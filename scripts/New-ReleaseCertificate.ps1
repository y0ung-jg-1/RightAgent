[CmdletBinding()]
param(
    [SecureString]$Password
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'RightAgent release certificates can only be created on Windows.'
}
if (-not (Get-PSDrive -Name 'Cert' -ErrorAction SilentlyContinue)) {
    throw 'The Cert: drive is unavailable. Run this script from a regular Windows PowerShell session.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repoRoot '.local\signing'
$pfxPath = Join-Path $outputDirectory 'RightAgent.pfx'
$cerPath = Join-Path $outputDirectory 'RightAgent.cer'
$protectedPasswordPath = Join-Path $outputDirectory 'RightAgent.pfx.password.dpapi'
$subject = 'CN=RightAgent'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$certificate = Get-ChildItem -Path 'Cert:\CurrentUser\My' |
    Where-Object {
        $_.Subject -eq $subject -and
        $_.HasPrivateKey -and
        $_.NotBefore -le (Get-Date) -and
        $_.NotAfter -gt (Get-Date)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $Password -and (Test-Path -LiteralPath $protectedPasswordPath -PathType Leaf)) {
    $protectedPassword = Get-Content -LiteralPath $protectedPasswordPath -Raw
    $Password = ConvertTo-SecureString $protectedPassword
}

if (-not $certificate -and (Test-Path -LiteralPath $pfxPath -PathType Leaf)) {
    if (-not $Password) {
        throw "The release PFX exists but its DPAPI-protected password is unavailable: $protectedPasswordPath"
    }
    $imported = @(Import-PfxCertificate -FilePath $pfxPath -Password $Password -CertStoreLocation 'Cert:\CurrentUser\My' -Exportable)
    $certificate = $imported |
        Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

if (-not $certificate) {
    $certificateParameters = @{
        Type = 'Custom'
        Subject = $subject
        FriendlyName = 'RightAgent release signing'
        CertStoreLocation = 'Cert:\CurrentUser\My'
        KeyAlgorithm = 'RSA'
        KeyLength = 3072
        HashAlgorithm = 'SHA256'
        KeyUsage = 'DigitalSignature'
        KeyExportPolicy = 'Exportable'
        NotAfter = (Get-Date).AddYears(3)
        TextExtension = @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
    }
    $certificate = New-SelfSignedCertificate @certificateParameters
}

if (-not $Password) {
    $randomBytes = New-Object byte[] 36
    $randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomNumberGenerator.GetBytes($randomBytes)
    }
    finally {
        $randomNumberGenerator.Dispose()
    }
    $plainPassword = [Convert]::ToBase64String($randomBytes)
    $Password = ConvertTo-SecureString $plainPassword -AsPlainText -Force
    $plainPassword = $null
    [Array]::Clear($randomBytes, 0, $randomBytes.Length)
}

if (Test-Path -LiteralPath $pfxPath -PathType Leaf) {
    $existingPfxCertificates = @(Get-PfxData -FilePath $pfxPath -Password $Password).EndEntityCertificates
    if ($existingPfxCertificates.Thumbprint -notcontains $certificate.Thumbprint) {
        throw "Existing release PFX does not contain certificate $($certificate.Thumbprint): $pfxPath"
    }
} else {
    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $Password | Out-Null
}

if (Test-Path -LiteralPath $cerPath -PathType Leaf) {
    $existingCer = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cerPath)
    if ($existingCer.Thumbprint -ne $certificate.Thumbprint) {
        throw "Existing release CER does not match certificate $($certificate.Thumbprint): $cerPath"
    }
} else {
    Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT | Out-Null
}

$protectedPassword = ConvertFrom-SecureString $Password
Set-Content -LiteralPath $protectedPasswordPath -Value $protectedPassword -NoNewline

Write-Host "PFX: $pfxPath"
Write-Host "CER: $cerPath"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Valid through: $($certificate.NotAfter.ToUniversalTime().ToString('u'))"
Write-Host "Password recovery: $protectedPasswordPath (DPAPI-protected for this Windows user)"
Write-Host 'The .local directory is gitignored. Never commit or share the PFX or password recovery file.'
