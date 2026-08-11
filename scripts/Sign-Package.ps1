[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$certificate = Get-ChildItem -Path 'Cert:\CurrentUser\My' |
    Where-Object { $_.Subject -eq 'CN=RightAgent Dev' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if (-not $certificate) { throw 'A valid RightAgent development signing certificate was not found. Run scripts\New-DevCertificate.ps1 first.' }
if (-not $PackagePath) {
    $packageRoot = Join-Path $repoRoot "artifacts\package\$Configuration"
    $PackagePath = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Where-Object { $_.Name -match '^RightAgent\.Package_.+_x64\.(msix|appx)$' -and $_.DirectoryName -notmatch '\\Dependencies(\\|$)' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $PackagePath -or -not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw 'No package was found to sign.'
}

$signTool = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Filter signtool.exe -Recurse -File |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $signTool) { throw 'x64 signtool.exe was not found.' }

& $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $PackagePath
if ($LASTEXITCODE -ne 0) { throw 'SignTool failed.' }

Write-Host "Signed: $PackagePath"
