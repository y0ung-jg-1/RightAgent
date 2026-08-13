[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$PackagePath,
    [string[]]$CommandPackagePaths
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageHelpers.ps1')
$cerPath = Join-Path $repoRoot '.local\signing\RightAgent.Dev.cer'
if (-not (Test-Path -LiteralPath $cerPath -PathType Leaf)) {
    throw 'Development certificate not found. Run scripts\New-DevCertificate.ps1 first.'
}
if (-not $PackagePath) {
    $PackagePath = Get-RightAgentPackagePath -RepoRoot $repoRoot -Configuration $Configuration
}
if (-not $PackagePath -or -not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw 'No signed package was found.'
}
if (-not $CommandPackagePaths) {
    $CommandPackagePaths = @(Get-RightAgentCommandPackagePaths `
        -RepoRoot $repoRoot `
        -Configuration $Configuration `
        -PackageIdentity Development)
}
$CommandPackagePaths = @($CommandPackagePaths)
if ($CommandPackagePaths.Count -ne 16) {
    throw "Expected exactly 16 signed command packages, but found $($CommandPackagePaths.Count)."
}
$allPackagePaths = @($PackagePath) + $CommandPackagePaths
foreach ($candidatePackagePath in $allPackagePaths) {
    if (-not (Test-Path -LiteralPath $candidatePackagePath -PathType Leaf)) {
        throw "Signed development package was not found: $candidatePackagePath"
    }
}

$expectedCertificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
foreach ($candidatePackagePath in $allPackagePaths) {
    $signature = Get-AuthenticodeSignature -LiteralPath $candidatePackagePath
    $signerMatchesCertificate =
        $null -ne $signature.SignerCertificate -and
        $signature.SignerCertificate.Thumbprint -eq $expectedCertificate.Thumbprint

    # Get-AuthenticodeSignature reports UnknownError for a correctly signed MSIX
    # when its development certificate is trusted through TrustedPeople rather than
    # installed as a system root. Accept that specific case only when the embedded
    # signer exactly matches the certificate shipped alongside the package.
    $isAcceptedDevelopmentSignature =
        $signature.Status -eq 'UnknownError' -and
        $signerMatchesCertificate

    if ($signature.Status -ne 'Valid' -and -not $isAcceptedDevelopmentSignature) {
        throw "Package signature is not valid for '$candidatePackagePath': $($signature.Status)"
    }
}

$trustedPeopleStore = 'Cert:\LocalMachine\TrustedPeople'
$trustedCertificate = Get-ChildItem -LiteralPath $trustedPeopleStore |
    Where-Object { $_.Thumbprint -eq $expectedCertificate.Thumbprint } |
    Select-Object -First 1

if (-not $trustedCertificate) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($isAdministrator) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation $trustedPeopleStore | Out-Null
    } else {
        $escapedCertificatePath = $cerPath.Replace("'", "''")
        $elevatedCommand = @"
`$ErrorActionPreference = 'Stop'
try {
    Import-Certificate -FilePath '$escapedCertificatePath' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    exit 0
} catch {
    Write-Error `$_
    exit 1
}
"@
        $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($elevatedCommand))
        $powerShellExecutable = if ($PSVersionTable.PSEdition -eq 'Core') {
            Join-Path $PSHOME 'pwsh.exe'
        } else {
            Join-Path $PSHOME 'powershell.exe'
        }

        Write-Host 'Administrator approval is required once to trust the RightAgent development certificate for MSIX deployment.'
        try {
            $elevatedProcess = Start-Process `
                -FilePath $powerShellExecutable `
                -Verb RunAs `
                -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', $encodedCommand) `
                -WindowStyle Hidden `
                -Wait `
                -PassThru
        } catch {
            throw 'Administrator approval was cancelled or could not be requested. The package was not installed.'
        }

        if ($elevatedProcess.ExitCode -ne 0) {
            throw 'The development certificate could not be added to Local Computer\Trusted People. The package was not installed.'
        }
    }

    $trustedCertificate = Get-ChildItem -LiteralPath $trustedPeopleStore |
        Where-Object { $_.Thumbprint -eq $expectedCertificate.Thumbprint } |
        Select-Object -First 1
    if (-not $trustedCertificate) {
        throw 'The development certificate is still not trusted by the local computer. The package was not installed.'
    }
}

$dependencyDirectory = Join-Path (Split-Path -Parent $PackagePath) 'Dependencies\x64'
$dependencies = if (Test-Path -LiteralPath $dependencyDirectory -PathType Container) {
    @(Get-ChildItem -LiteralPath $dependencyDirectory -File | Where-Object { $_.Extension -in '.msix', '.appx' } | Select-Object -ExpandProperty FullName)
} else { @() }
$classIds = @(0..15 | ForEach-Object {
    'F7E08D{0:X2}-676E-4D4B-950A-5B4451E19E3C' -f (0x6D + $_)
})
$classIdPattern = ($classIds | ForEach-Object { [Regex]::Escape($_) }) -join '|'
$surrogates = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop |
    Where-Object {
        $_.Name -ieq 'dllhost.exe' -and
        -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
        $_.CommandLine -match $classIdPattern
    })
foreach ($surrogate in $surrogates) {
    Stop-Process -Id $surrogate.ProcessId -Force -ErrorAction Stop
}
if ($dependencies.Count -gt 0) {
    Add-AppxPackage -Path $PackagePath -DependencyPath $dependencies -ForceApplicationShutdown -ForceUpdateFromAnyVersion
} else {
    Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown -ForceUpdateFromAnyVersion
}
foreach ($commandPackagePath in $CommandPackagePaths) {
    Add-AppxPackage -Path $commandPackagePath -ForceApplicationShutdown -ForceUpdateFromAnyVersion
}
Write-Host 'RightAgent installed. If Explorer cached the old menu, close all Explorer windows or sign out once.'
