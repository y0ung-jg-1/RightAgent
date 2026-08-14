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

    $appExecutable = Join-Path $stagingDirectory 'App\RightAgent.App.exe'
    if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
        throw 'The release bundle does not contain App\RightAgent.App.exe.'
    }
    $commandPackages = @(Get-ChildItem -LiteralPath $stagingDirectory -Filter 'RightAgent.Command*-x64.msix' -File)
    if ($commandPackages.Count -ne 16) {
        throw "Expected exactly 16 RightAgent command MSIX packages in the release bundle, but found $($commandPackages.Count)."
    }
    $certificatePath = Join-Path $stagingDirectory 'RightAgent.cer'
    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
        throw 'The release bundle does not contain RightAgent.cer.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $command00 = @($commandPackages | Where-Object { $_.Name -like 'RightAgent.Command00-*-x64.msix' } | Select-Object -First 1)
    if (-not $command00) {
        throw 'The release bundle does not contain RightAgent.Command00.'
    }
    $packageArchive = [IO.Compression.ZipFile]::OpenRead($command00.FullName)
    try {
        $manifestEntry = $packageArchive.GetEntry('AppxManifest.xml')
        if (-not $manifestEntry) {
            throw 'Command package 00 does not contain AppxManifest.xml.'
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
    for ($slot = 0; $slot -lt 16; ++$slot) {
        $expectedCommandName = "RightAgent.Command$($slot.ToString('D2'))-$displayVersion-x64.msix"
        if (@($commandPackages | Where-Object { $_.Name -ceq $expectedCommandName }).Count -ne 1) {
            throw "Release bundle is missing the exact command package '$expectedCommandName'."
        }
    }

    $commandPayloadDirectory = Join-Path $stagingDirectory 'CommandPackages'
    New-Item -ItemType Directory -Path $commandPayloadDirectory -Force | Out-Null
    foreach ($commandPackage in $commandPackages) {
        Copy-Item -LiteralPath $commandPackage.FullName -Destination (Join-Path $commandPayloadDirectory $commandPackage.Name) -Force
    }

    $repoInstallScript = Join-Path $repoRoot 'scripts\Install-Release.ps1'
    $stagedInstallScript = Join-Path $stagingDirectory 'Install-RightAgent.ps1'
    Copy-Item -LiteralPath $repoInstallScript -Destination $stagedInstallScript -Force
    $stagedScriptText = Get-Content -LiteralPath $stagedInstallScript -Raw
    if ($stagedScriptText -notmatch '(?m)^\s*\[switch\]\$SkipAppCopy\s*$') {
        throw 'Staged Install-RightAgent.ps1 is missing the -SkipAppCopy switch required by the MSI custom action.'
    }

    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
    $certThumbprint = $certificate.Thumbprint.ToUpperInvariant()
    $appSource = Join-Path $stagingDirectory 'App'
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

        $wixCli = Get-Command -Name wix -CommandType Application -ErrorAction SilentlyContinue
        if (-not $wixCli) {
            throw 'The WiX CLI (wix.exe 5.0.2) is required to sign Setup.exe. Install it with: dotnet tool install --global wix --version 5.0.2'
        }
        $wixCli = $wixCli.Source

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
            "-p:AppSource=$appSource" `
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
