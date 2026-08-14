[CmdletBinding()]
param(
    [string]$CertificatePath,
    [string]$AppDirectory,
    [string[]]$CommandPackagePaths,
    [string]$OutputDirectory,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [switch]$SkipSigning
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageHelpers.ps1')

if (-not $CertificatePath) {
    $CertificatePath = Join-Path $repoRoot '.local\signing\RightAgent.cer'
}
$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "Certificate was not found: $CertificatePath"
}

if (-not $AppDirectory) {
    $AppDirectory = Get-RightAgentAppPublishPath -RepoRoot $repoRoot -Configuration Release
}
$AppDirectory = [IO.Path]::GetFullPath($AppDirectory)
$appExecutable = Join-Path $AppDirectory 'RightAgent.App.exe'
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "Unpackaged settings app was not found: $appExecutable"
}

if (-not $CommandPackagePaths) {
    $CommandPackagePaths = @(Get-RightAgentCommandPackagePaths `
        -RepoRoot $repoRoot `
        -Configuration Release `
        -PackageIdentity Release)
}
$CommandPackagePaths = @($CommandPackagePaths | ForEach-Object { [IO.Path]::GetFullPath($_) })
if ($CommandPackagePaths.Count -ne 16) {
    throw "Expected exactly 16 command packages, but found $($CommandPackagePaths.Count)."
}
foreach ($commandPackagePath in $CommandPackagePaths) {
    if (-not (Test-Path -LiteralPath $commandPackagePath -PathType Leaf)) {
        throw "Command package was not found: $commandPackagePath"
    }
}
& (Join-Path $PSScriptRoot 'Verify-CommandPackages.ps1') -Configuration Release -PackageIdentity Release

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
if ($certificate.Subject -cne 'CN=RightAgent') {
    throw "Unexpected release certificate subject: $($certificate.Subject)"
}
if (-not $SkipSigning) {
    foreach ($signedPackagePath in $CommandPackagePaths) {
        $signature = Get-AuthenticodeSignature -LiteralPath $signedPackagePath
        $signerMatches =
            $null -ne $signature.SignerCertificate -and
            $signature.SignerCertificate.Thumbprint -eq $certificate.Thumbprint
        if (-not $signerMatches -or $signature.Status -notin 'Valid', 'UnknownError') {
            throw "Release package signature verification failed for '$signedPackagePath': $($signature.Status)"
        }
        if (-not $signature.TimeStamperCertificate) {
            throw "Release package signature does not contain the required RFC 3161 timestamp: $signedPackagePath"
        }
    }
}

$installScript = Join-Path $repoRoot 'scripts\Install-Release.ps1'
$installScriptText = Get-Content -LiteralPath $installScript -Raw
if ($installScriptText -notmatch '(?m)^\s*\[switch\]\$SkipAppCopy\s*$') {
    throw 'Install-Release.ps1 is missing the -SkipAppCopy switch required by the MSI custom action.'
}

$packageVersion = Get-RightAgentPackageVersion -RepoRoot $repoRoot -PackageIdentity Release
$displayVersion = Get-RightAgentDisplayVersion -PackageVersion $packageVersion

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
foreach ($candidate in @($artifactsRoot, $installerRoot, $stagingDirectory, $AppDirectory)) {
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
    $commandPayloadDirectory = Join-Path $stagingDirectory 'CommandPackages'
    New-Item -ItemType Directory -Path $commandPayloadDirectory -Force | Out-Null
    for ($slot = 0; $slot -lt $CommandPackagePaths.Count; ++$slot) {
        $slotText = $slot.ToString('D2')
        $commandReleaseName = "RightAgent.Command$slotText-$displayVersion-x64.msix"
        Copy-Item -LiteralPath $CommandPackagePaths[$slot] -Destination (Join-Path $commandPayloadDirectory $commandReleaseName)
    }
    Copy-Item -LiteralPath $CertificatePath -Destination (Join-Path $stagingDirectory 'RightAgent.cer') -Force

    $certThumbprint = $certificate.Thumbprint.ToUpperInvariant()
    $repoDir = $repoRoot.TrimEnd('\') + '\'
    $packageProject = Join-Path $repoRoot 'installer\RightAgent.Package.wixproj'
    $bundleProject = Join-Path $repoRoot 'installer\RightAgent.Bundle.wixproj'
    $skus = @(
        @{ PerUser = 'false'; OutputName = "RightAgent-$displayVersion-x64-Setup.exe"; Label = 'per-machine' }
        @{ PerUser = 'true'; OutputName = "RightAgent-$displayVersion-x64-UserSetup.exe"; Label = 'per-user' }
    )

    Get-ChildItem -LiteralPath $OutputDirectory -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^RightAgent-.+-x64-(User)?Setup\.msi(\.sha256)?$' } |
        Remove-Item -Force

    $signTool = $null
    $privateCertificate = $null
    $publicCertificate = $null
    if (-not $SkipSigning) {
        $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $stagingDirectory 'RightAgent.cer'))
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

        $wixCli = Get-Command -Name wix -CommandType Application -ErrorAction SilentlyContinue
        if (-not $wixCli) {
            throw 'The WiX CLI (wix.exe 7.0.0) is required to sign Setup.exe. Install it with: dotnet tool install --global wix --version 7.0.0'
        }
        $wixCli = $wixCli.Source
        $wixVersion = (& $wixCli --version 2>$null | Select-Object -First 1)
        if ($wixVersion -notmatch '^7\.') {
            throw "The WiX CLI must be 7.x (found '$wixVersion'). Install it with: dotnet tool install --global wix --version 7.0.0"
        }
        & $wixCli eula accept wix7
        if ($LASTEXITCODE -ne 0) {
            throw 'Accepting the WiX 7 OSMF EULA failed.'
        }

        $timestampUri = $null
        if (-not [Uri]::TryCreate($TimestampServer, [UriKind]::Absolute, [ref]$timestampUri) -or
            $timestampUri.Scheme -notin 'http', 'https') {
            throw "Invalid RFC 3161 timestamp server URL: $TimestampServer"
        }
    }

    foreach ($sku in $skus) {
        $msiOutputDirectory = Join-Path $installerRoot "obj\msi-$($sku.PerUser)"
        $bundleOutputDirectory = Join-Path $installerRoot "obj\bundle-$($sku.PerUser)"
        foreach ($generatedDirectory in @($msiOutputDirectory, $bundleOutputDirectory)) {
            if (Test-Path -LiteralPath $generatedDirectory) {
                Remove-Item -LiteralPath $generatedDirectory -Recurse -Force -ErrorAction Stop
            }
            New-Item -ItemType Directory -Path $generatedDirectory -Force | Out-Null
        }

        & dotnet build $packageProject -c Release --nologo `
            "-p:PerUser=$($sku.PerUser)" `
            "-p:Version=$packageVersion" `
            "-p:AppSource=$AppDirectory" `
            "-p:CommandSource=$commandPayloadDirectory" `
            "-p:PayloadDir=$stagingDirectory" `
            "-p:RepoDir=$repoDir" `
            "-p:BaseIntermediateOutputPath=$msiOutputDirectory\obj\" `
            "-p:OutputPath=$msiOutputDirectory\"
        if ($LASTEXITCODE -ne 0) {
            throw "WiX $($sku.Label) MSI build failed."
        }

        $msiName = if ($sku.PerUser -eq 'true') { 'RightAgentUser.msi' } else { 'RightAgent.msi' }
        $msiCandidates = @(Get-ChildItem -LiteralPath $msiOutputDirectory -Filter $msiName -File -Recurse)
        $msiPath = $msiCandidates |
            Where-Object { $_.Directory.Name -eq 'en-us' } |
            Select-Object -First 1
        if (-not $msiPath) {
            $msiPath = $msiCandidates | Select-Object -First 1
        }
        if (-not $msiPath) {
            throw "Expected '$msiName' in '$msiOutputDirectory'."
        }

        if (-not $SkipSigning) {
            & $signTool sign /fd SHA256 /sha1 $privateCertificate.Thumbprint /s My /tr $timestampUri.AbsoluteUri /td SHA256 $msiPath.FullName
            if ($LASTEXITCODE -ne 0) {
                throw "Signing the $($sku.Label) MSI failed."
            }
        }

        & dotnet build $bundleProject -c Release --nologo `
            "-p:PerUser=$($sku.PerUser)" `
            "-p:Version=$packageVersion" `
            "-p:MsiPath=$($msiPath.FullName)" `
            "-p:PayloadDir=$stagingDirectory" `
            "-p:RepoDir=$repoDir" `
            "-p:CertThumbprint=$certThumbprint" `
            "-p:BaseIntermediateOutputPath=$bundleOutputDirectory\obj\" `
            "-p:OutputPath=$bundleOutputDirectory\"
        if ($LASTEXITCODE -ne 0) {
            throw "WiX $($sku.Label) Setup bundle build failed."
        }

        $bundleName = if ($sku.PerUser -eq 'true') { 'RightAgentUserSetup.exe' } else { 'RightAgentSetup.exe' }
        $bundleCandidates = @(Get-ChildItem -LiteralPath $bundleOutputDirectory -Filter $bundleName -File -Recurse)
        $builtBundle = $bundleCandidates | Select-Object -First 1
        if (-not $builtBundle) {
            throw "Expected '$bundleName' in '$bundleOutputDirectory'."
        }

        $setupPath = Join-Path $OutputDirectory $sku.OutputName
        Copy-Item -LiteralPath $builtBundle.FullName -Destination $setupPath -Force
        if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
            throw "The expected setup package was not produced: $setupPath"
        }

        if (-not $SkipSigning) {
            $enginePath = Join-Path $bundleOutputDirectory 'burn-engine.exe'
            & $wixCli burn detach $setupPath -engine $enginePath
            if ($LASTEXITCODE -ne 0) {
                throw "Detaching the $($sku.Label) Burn engine failed."
            }

            & $signTool sign /fd SHA256 /sha1 $privateCertificate.Thumbprint /s My /tr $timestampUri.AbsoluteUri /td SHA256 $enginePath
            if ($LASTEXITCODE -ne 0) {
                throw "Signing the $($sku.Label) Burn engine failed."
            }

            & $wixCli burn reattach $setupPath -engine $enginePath -o $setupPath
            if ($LASTEXITCODE -ne 0) {
                throw "Reattaching the $($sku.Label) Burn engine failed."
            }

            & $signTool sign /fd SHA256 /sha1 $privateCertificate.Thumbprint /s My /tr $timestampUri.AbsoluteUri /td SHA256 $setupPath
            if ($LASTEXITCODE -ne 0) {
                throw "Signing the $($sku.Label) setup executable failed."
            }

            $signature = Get-AuthenticodeSignature -LiteralPath $setupPath
            if ($null -eq $signature.SignerCertificate -or
                $signature.SignerCertificate.Thumbprint -ne $publicCertificate.Thumbprint -or
                $signature.Status -notin 'Valid', 'UnknownError' -or
                $null -eq $signature.TimeStamperCertificate) {
                throw "Setup signature verification failed for $($sku.Label): $($signature.Status)"
            }
        }

        $setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -LiteralPath "$setupPath.sha256" -Value "$setupHash  $($sku.OutputName)" -Encoding ascii
        Write-Host "Setup package ($($sku.Label)): $setupPath"
        Write-Host "SHA256: $setupHash"
    }
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
