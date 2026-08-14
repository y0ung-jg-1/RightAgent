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
$allPackagePaths = @($CommandPackagePaths)
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

$legacyDev = Get-AppxPackage -Name 'RightAgent.Dev' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ceq 'RightAgent.Dev' -and $_.Publisher -ceq 'CN=RightAgent Dev' } |
    Select-Object -First 1
$dataDirectory = Join-Path $env:LOCALAPPDATA 'RightAgent'
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$settingsPath = Join-Path $dataDirectory 'settings.json'
if ($legacyDev) {
    $legacySettings = Join-Path $env:LOCALAPPDATA "Packages\$($legacyDev.PackageFamilyName)\LocalState\settings.json"
    if ((Test-Path -LiteralPath $legacySettings -PathType Leaf) -and -not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        Copy-Item -LiteralPath $legacySettings -Destination $settingsPath -Force
    }
    $legacyDev | Remove-AppxPackage -ErrorAction Stop
}

$appExecutable = Join-Path $repoRoot "RightAgent.App\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\RightAgent.App.exe"
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    $published = Join-Path $repoRoot "artifacts\app\$Configuration\win-x64\RightAgent.App.exe"
    if (Test-Path -LiteralPath $published -PathType Leaf) {
        $appExecutable = $published
    }
}
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "RightAgent.App.exe was not found for $Configuration."
}

$installRecord = [ordered]@{
    packageName = 'RightAgent.Dev'
    publisher = 'CN=RightAgent Dev'
    appPath = $appExecutable
    version = (Get-RightAgentPackageVersion -RepoRoot $repoRoot -PackageIdentity Development)
}
[IO.File]::WriteAllText(
    (Join-Path $dataDirectory 'install.json'),
    ($installRecord | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))

$cacheDirectory = Join-Path $dataDirectory 'CommandPackages'
New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
for ($slot = 0; $slot -lt $CommandPackagePaths.Count; ++$slot) {
    Copy-Item -LiteralPath $CommandPackagePaths[$slot] -Destination (Join-Path $cacheDirectory ('{0:D2}.msix' -f $slot)) -Force -ErrorAction Stop
}
$requiredSlots = Get-RightAgentRequiredCommandSlotCount -SettingsPath $settingsPath

for ($slot = 0; $slot -lt $requiredSlots; ++$slot) {
    Add-AppxPackage -Path $CommandPackagePaths[$slot] -ForceApplicationShutdown -ForceUpdateFromAnyVersion
}
for ($slot = $requiredSlots; $slot -lt 16; ++$slot) {
    $commandPackageName = "RightAgent.Dev.Command$($slot.ToString('D2'))"
    $extraPackages = @(Get-AppxPackage -Name $commandPackageName -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ceq $commandPackageName -and $_.Publisher -ceq 'CN=RightAgent Dev' })
    foreach ($extraPackage in $extraPackages) {
        $extraPackage | Remove-AppxPackage -ErrorAction Stop
    }
}

Get-Process -Name explorer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe')
}

Write-Host 'RightAgent installed. Explorer was refreshed so the context menu matches the current menu mode.'
