[CmdletBinding()]
param(
    [string]$PackagePath,
    [string[]]$CommandPackagePaths,
    [string]$CertificatePath,
    [string]$ResultPath,
    [switch]$TrustCertificateOnly
)

$ErrorActionPreference = 'Stop'

function Write-RightAgentInstallationProgress {
    param(
        [Parameter(Mandatory)]
        [ValidateRange(0, 100)]
        [int]$PercentComplete
    )

    [Console]::Out.WriteLine("RIGHTAGENT_PROGRESS:$PercentComplete")
    [Console]::Out.Flush()
}

function Add-RightAgentAppxPackage {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [string[]]$DependencyPath = @(),

        [ValidateRange(0, 100)]
        [int]$BasePercent = 0,

        [ValidateRange(0, 100)]
        [int]$SpanPercent = 100
    )

    # Add-AppxPackage receives DeploymentProgress from the Windows deployment API
    # and exposes its percentage through the PowerShell progress stream. Host the
    # cmdlet in a runspace so Setup can consume those records as line-delimited
    # output while the deployment is still running.
    $packageInstaller = [PowerShell]::Create()
    $progressHandler = [EventHandler[Management.Automation.DataAddedEventArgs]] {
        param($sender, $eventArgs)

        $progressRecord = $sender[$eventArgs.Index]
        if ($null -eq $progressRecord -or $progressRecord.PercentComplete -lt 0) {
            return
        }

        $deploymentPercent = [Math]::Min(100, [Math]::Max(0, [int]$progressRecord.PercentComplete))
        $percentComplete = [Math]::Min(
            100,
            $BasePercent + [int][Math]::Round($deploymentPercent * $SpanPercent / 100.0))
        [Console]::Out.WriteLine("RIGHTAGENT_PROGRESS:$percentComplete")
        [Console]::Out.Flush()
    }

    try {
        $packageInstaller.Streams.Progress.add_DataAdded($progressHandler)
        [void]$packageInstaller.AddCommand('Add-AppxPackage')
        [void]$packageInstaller.AddParameter('Path', $Path)
        if ($DependencyPath.Count -gt 0) {
            [void]$packageInstaller.AddParameter('DependencyPath', $DependencyPath)
        }
        [void]$packageInstaller.AddParameter('ForceApplicationShutdown')
        [void]$packageInstaller.AddParameter('ForceUpdateFromAnyVersion')
        [void]$packageInstaller.AddParameter('ErrorAction', [Management.Automation.ActionPreference]::Stop)

        Write-RightAgentInstallationProgress -PercentComplete $BasePercent
        [void]$packageInstaller.Invoke()

        if ($packageInstaller.HadErrors -or $packageInstaller.Streams.Error.Count -gt 0) {
            $deploymentErrors = @($packageInstaller.Streams.Error | ForEach-Object { $_.ToString() })
            $deploymentDetail = ($deploymentErrors -join [Environment]::NewLine).Trim()
            if (-not $deploymentDetail) {
                $deploymentDetail = 'The Windows package deployment operation failed without returning an error message.'
            }
            throw $deploymentDetail
        }

        Write-RightAgentInstallationProgress -PercentComplete ([Math]::Min(100, $BasePercent + $SpanPercent))
    }
    finally {
        $packageInstaller.Streams.Progress.remove_DataAdded($progressHandler)
        $packageInstaller.Dispose()
    }
}

function Get-RightAgentAppxManifest {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $manifestEntry = $archive.GetEntry('AppxManifest.xml')
        if (-not $manifestEntry) {
            throw "Package does not contain AppxManifest.xml: $Path"
        }
        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try {
            return [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Install-RightAgentCommandPackageCache {
    param(
        [Parameter(Mandatory)]
        [string]$MainPackageName,

        [Parameter(Mandatory)]
        [string]$Publisher,

        [Parameter(Mandatory)]
        [string[]]$CommandPackagePaths
    )

    if ($CommandPackagePaths.Count -ne 16) {
        throw "Expected exactly 16 command packages to cache, but found $($CommandPackagePaths.Count)."
    }

    $mainPackage = Get-AppxPackage -Name $MainPackageName -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ceq $MainPackageName -and $_.Publisher -ceq $Publisher } |
        Select-Object -First 1
    if (-not $mainPackage) {
        throw "Cannot cache command packages because '$MainPackageName' is not installed."
    }

    $cacheDirectory = Join-Path $env:LOCALAPPDATA "Packages\$($mainPackage.PackageFamilyName)\LocalState\CommandPackages"
    New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
    for ($slot = 0; $slot -lt 16; ++$slot) {
        $destination = Join-Path $cacheDirectory ('{0:D2}.msix' -f $slot)
        Copy-Item -LiteralPath $CommandPackagePaths[$slot] -Destination $destination -Force -ErrorAction Stop
        if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
            throw "Failed to cache command package slot $($slot.ToString('D2'))."
        }
    }
}

function Get-RightAgentMainPackage {
    param(
        [Parameter(Mandatory)]
        [string]$MainPackageName,

        [Parameter(Mandatory)]
        [string]$Publisher
    )

    Get-AppxPackage -Name $MainPackageName -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ceq $MainPackageName -and $_.Publisher -ceq $Publisher } |
        Select-Object -First 1
}

function Get-RightAgentRequiredCommandSlotCount {
    param(
        [Parameter(Mandatory)]
        [string]$MainPackageName,

        [Parameter(Mandatory)]
        [string]$Publisher
    )

    $mainPackage = Get-RightAgentMainPackage -MainPackageName $MainPackageName -Publisher $Publisher
    if (-not $mainPackage) {
        return 1
    }

    $settingsPath = Join-Path $env:LOCALAPPDATA "Packages\$($mainPackage.PackageFamilyName)\LocalState\settings.json"
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        return 1
    }

    try {
        $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return 1
    }

    if ($settings.PSObject.Properties.Name -contains 'menuEnabled' -and -not [bool]$settings.menuEnabled) {
        return 0
    }

    $enabled = @($settings.agents | Where-Object { $_.enabled })
    if ($enabled.Count -eq 0) {
        return 0
    }
    if ([string]$settings.menuMode -eq 'multiDirect') {
        return [Math]::Min(16, [int]$enabled.Count)
    }
    return 1
}

function Restart-RightAgentExplorer {
    Get-Process -Name explorer -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
    if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe')
    }
}

function Stop-RightAgentComSurrogates {
    $installedRightAgentPackages = @(
        @(Get-AppxPackage -Name 'RightAgent' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ceq 'RightAgent' -and $_.Publisher -ceq 'CN=RightAgent' })
        @(Get-AppxPackage -Name 'RightAgent.Command*' -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -match '^RightAgent\.Command(0[0-9]|1[0-5])$' -and
                $_.Publisher -ceq 'CN=RightAgent'
            })
    )
    if ($installedRightAgentPackages.Count -eq 0) {
        return
    }

    if (-not (Get-Command -Name 'Get-CimInstance' -CommandType Cmdlet -ErrorAction SilentlyContinue)) {
        throw 'RightAgent command packages are active, but the installer cannot inspect their COM surrogate processes. Close all File Explorer windows or sign out, then run Setup again.'
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
        try {
            Stop-Process -Id $surrogate.ProcessId -Force -ErrorAction Stop
        }
        catch {
            throw "RightAgent could not release Explorer's cached menu process $($surrogate.ProcessId). Close all File Explorer windows or sign out, then run Setup again."
        }
    }
    if ($surrogates.Count -gt 0) {
        Write-Host "Released $($surrogates.Count) cached RightAgent Explorer command process(es) before package deployment."
    }
}

$installationMutex = [Threading.Mutex]::new($false, 'Local\RightAgent.PackageInstallation')
$installationMutexAcquired = $false
try {
    try {
        $installationMutexAcquired = $installationMutex.WaitOne(0)
    }
    catch [Threading.AbandonedMutexException] {
        $installationMutexAcquired = $true
    }

    if (-not $installationMutexAcquired) {
        [Console]::Error.WriteLine('Another RightAgent installation is already running. Wait for it to finish before trying again.')
        exit 1618
    }

    if (-not (Get-PSDrive -Name 'Cert' -ErrorAction SilentlyContinue)) {
        $securityModulePath = Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1'
        if (Test-Path -LiteralPath $securityModulePath -PathType Leaf) {
            Import-Module -Name $securityModulePath -ErrorAction Stop
        } else {
            Import-Module -Name 'Microsoft.PowerShell.Security' -ErrorAction Stop
        }
    }
    if (-not (Get-PSDrive -Name 'Cert' -ErrorAction SilentlyContinue)) {
        throw 'The Cert: drive is unavailable after loading Microsoft.PowerShell.Security.'
    }
    $pkiModulePath = Join-Path $PSHOME 'Modules\PKI\PKI.psd1'
    if (Test-Path -LiteralPath $pkiModulePath -PathType Leaf) {
        Import-Module -Name $pkiModulePath -ErrorAction Stop
    } else {
        Import-Module -Name 'PKI' -ErrorAction Stop
    }
    if (-not (Get-Command -Name 'Import-Certificate' -CommandType Cmdlet -ErrorAction SilentlyContinue)) {
        throw 'The Import-Certificate cmdlet is unavailable after loading the PKI module.'
    }

    if (-not $PackagePath) {
        $packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'RightAgent-*-x64.msix' -File)
        if ($packages.Count -ne 1) {
            throw "Expected exactly one main RightAgent MSIX next to this installer, but found $($packages.Count)."
        }
        $PackagePath = $packages[0].FullName
    }
    if (-not $CommandPackagePaths) {
        $CommandPackagePaths = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'RightAgent.Command*-x64.msix' -File |
            Sort-Object Name |
            Select-Object -ExpandProperty FullName)
    }
    $CommandPackagePaths = @($CommandPackagePaths)
    if ($CommandPackagePaths.Count -ne 16) {
        throw "Expected exactly 16 RightAgent command MSIX packages next to this installer, but found $($CommandPackagePaths.Count)."
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
    $CommandPackagePaths = @($CommandPackagePaths | ForEach-Object {
        $resolvedCommandPackagePath = [IO.Path]::GetFullPath($_)
        if (-not (Test-Path -LiteralPath $resolvedCommandPackagePath -PathType Leaf)) {
            throw "RightAgent command package was not found: $resolvedCommandPackagePath"
        }
        $resolvedCommandPackagePath
    })

    $expectedCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
    if ($expectedCertificate.Subject -cne 'CN=RightAgent') {
        throw "Unexpected release certificate subject: $($expectedCertificate.Subject)"
    }
    if ($expectedCertificate.NotBefore -gt (Get-Date) -or $expectedCertificate.NotAfter -le (Get-Date)) {
        throw 'The RightAgent release certificate is outside its validity period.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $allPackagePaths = @($PackagePath) + $CommandPackagePaths
    foreach ($signedPackagePath in $allPackagePaths) {
        $signature = Get-AuthenticodeSignature -LiteralPath $signedPackagePath
        $signerMatchesCertificate =
            $null -ne $signature.SignerCertificate -and
            $signature.SignerCertificate.Thumbprint -eq $expectedCertificate.Thumbprint
        $isAcceptedUntrustedSignature =
            $signature.Status -eq 'UnknownError' -and
            $signerMatchesCertificate
        if ($signature.Status -ne 'Valid' -and -not $isAcceptedUntrustedSignature) {
            throw "Package signature verification failed for '$signedPackagePath': $($signature.Status)"
        }
    }

    [xml]$manifest = Get-RightAgentAppxManifest -Path $PackagePath
    if ([string]$manifest.Package.Identity.Name -cne 'RightAgent' -or
        [string]$manifest.Package.Identity.Publisher -cne 'CN=RightAgent' -or
        [string]$manifest.Package.Identity.ProcessorArchitecture -cne 'x64') {
        throw 'The package identity is not the public RightAgent release identity.'
    }
    $packageVersion = [version]([string]$manifest.Package.Identity.Version)

    $commandPackagesBySlot = @{}
    foreach ($commandPackagePath in $CommandPackagePaths) {
        [xml]$commandManifest = Get-RightAgentAppxManifest -Path $commandPackagePath
        $commandIdentity = $commandManifest.Package.Identity
        $commandName = [string]$commandIdentity.Name
        if ($commandName -notmatch '^RightAgent\.Command(0[0-9]|1[0-5])$') {
            throw "Unexpected RightAgent command package identity '$commandName': $commandPackagePath"
        }
        $slotText = $Matches[1]
        if ($commandPackagesBySlot.ContainsKey($slotText)) {
            throw "Duplicate RightAgent command package slot $slotText."
        }
        if ([string]$commandIdentity.Publisher -cne 'CN=RightAgent' -or
            [version]([string]$commandIdentity.Version) -ne $packageVersion -or
            [string]$commandIdentity.ProcessorArchitecture -cne 'x64') {
            throw "RightAgent command package $slotText does not match the main package identity."
        }
        $commandPackagesBySlot[$slotText] = $commandPackagePath
    }
    $CommandPackagePaths = @(foreach ($slot in 0..15) {
        $slotText = $slot.ToString('D2')
        if (-not $commandPackagesBySlot.ContainsKey($slotText)) {
            throw "RightAgent command package slot $slotText is missing."
        }
        $commandPackagesBySlot[$slotText]
    })

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
    $securityModulePath = Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1'
    if (Test-Path -LiteralPath $securityModulePath -PathType Leaf) {
        Import-Module -Name $securityModulePath -ErrorAction Stop
    } else {
        Import-Module -Name 'Microsoft.PowerShell.Security' -ErrorAction Stop
    }
    $pkiModulePath = Join-Path $PSHOME 'Modules\PKI\PKI.psd1'
    if (Test-Path -LiteralPath $pkiModulePath -PathType Leaf) {
        Import-Module -Name $pkiModulePath -ErrorAction Stop
    } else {
        Import-Module -Name 'PKI' -ErrorAction Stop
    }
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

    if ($TrustCertificateOnly) {
        Write-Host 'The RightAgent release certificate is trusted in Local Computer\Trusted People.'
        Write-RightAgentInstallationProgress -PercentComplete 100
        return
    }

    $mainPackageName = 'RightAgent'
    $publisher = 'CN=RightAgent'
    $sameVersionMain = $null -ne (Get-AppxPackage -Name $mainPackageName -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -ceq $mainPackageName -and
            $_.Publisher -ceq $publisher -and
            [version]$_.Version -eq $packageVersion
        } |
        Select-Object -First 1)

    $dependencyDirectory = Join-Path $PSScriptRoot 'Dependencies\x64'
    $dependencies = if (Test-Path -LiteralPath $dependencyDirectory -PathType Container) {
        @(Get-ChildItem -LiteralPath $dependencyDirectory -File |
            Where-Object { $_.Extension -in '.msix', '.appx' } |
            Select-Object -ExpandProperty FullName)
    } else {
        @()
    }

    Stop-RightAgentComSurrogates
    if ($sameVersionMain) {
        Write-RightAgentInstallationProgress -PercentComplete 36
    }
    else {
        Add-RightAgentAppxPackage -Path $PackagePath -DependencyPath $dependencies -BasePercent 0 -SpanPercent 36
    }

    Install-RightAgentCommandPackageCache -MainPackageName $mainPackageName -Publisher $publisher -CommandPackagePaths $CommandPackagePaths
    Write-RightAgentInstallationProgress -PercentComplete 50

    $requiredSlots = Get-RightAgentRequiredCommandSlotCount -MainPackageName $mainPackageName -Publisher $publisher
    $slotSpan = if ($requiredSlots -gt 0) { [int][Math]::Floor(40 / $requiredSlots) } else { 0 }
    for ($slot = 0; $slot -lt $requiredSlots; ++$slot) {
        $commandPackageName = "RightAgent.Command$($slot.ToString('D2'))"
        $sameVersionCommand = $null -ne (Get-AppxPackage -Name $commandPackageName -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -ceq $commandPackageName -and
                $_.Publisher -ceq $publisher -and
                [version]$_.Version -eq $packageVersion
            } |
            Select-Object -First 1)
        $basePercent = 50 + ($slotSpan * $slot)
        if ($sameVersionCommand) {
            Write-RightAgentInstallationProgress -PercentComplete ([Math]::Min(90, $basePercent + $slotSpan))
        }
        else {
            Add-RightAgentAppxPackage `
                -Path $CommandPackagePaths[$slot] `
                -BasePercent $basePercent `
                -SpanPercent $slotSpan
        }
    }

    for ($slot = $requiredSlots; $slot -lt 16; ++$slot) {
        $commandPackageName = "RightAgent.Command$($slot.ToString('D2'))"
        $extraPackages = @(Get-AppxPackage -Name $commandPackageName -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ceq $commandPackageName -and $_.Publisher -ceq $publisher })
        foreach ($extraPackage in $extraPackages) {
            $extraPackage | Remove-AppxPackage -ErrorAction Stop
        }
    }
    Write-RightAgentInstallationProgress -PercentComplete 96

    $installedMain = Get-RightAgentMainPackage -MainPackageName $mainPackageName -Publisher $publisher
    if (-not $installedMain -or [version]$installedMain.Version -ne $packageVersion) {
        throw "RightAgent installation verification failed for package '$mainPackageName' version $packageVersion."
    }
    for ($slot = 0; $slot -lt $requiredSlots; ++$slot) {
        $commandPackageName = "RightAgent.Command$($slot.ToString('D2'))"
        $installedCommand = Get-AppxPackage -Name $commandPackageName -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -ceq $commandPackageName -and
                $_.Publisher -ceq $publisher -and
                [version]$_.Version -eq $packageVersion
            } |
            Select-Object -First 1
        if (-not $installedCommand) {
            throw "RightAgent installation verification failed for package '$commandPackageName' version $packageVersion."
        }
    }
    for ($slot = $requiredSlots; $slot -lt 16; ++$slot) {
        $commandPackageName = "RightAgent.Command$($slot.ToString('D2'))"
        $unexpected = Get-AppxPackage -Name $commandPackageName -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ceq $commandPackageName -and $_.Publisher -ceq $publisher } |
            Select-Object -First 1
        if ($unexpected) {
            throw "RightAgent left unused command package '$commandPackageName' registered."
        }
    }

    Restart-RightAgentExplorer
    Write-RightAgentInstallationProgress -PercentComplete 100
    Write-Host 'RightAgent installed. Explorer was refreshed so the context menu matches the current menu mode.'
}
catch {
    $installationFailure = $_
    if ($ResultPath) {
        try {
            $resolvedResultPath = [IO.Path]::GetFullPath($ResultPath)
            $failureDetail = @(
                $installationFailure.Exception.Message
                $installationFailure.InvocationInfo.PositionMessage
                $installationFailure.ScriptStackTrace
            ) -join [Environment]::NewLine
            [IO.File]::WriteAllText(
                $resolvedResultPath,
                $failureDetail,
                [Text.UTF8Encoding]::new($true)
            )
        }
        catch {
            # Preserve the original installation failure if diagnostics cannot be written.
        }
    }
    throw $installationFailure
}
finally {
    if ($installationMutexAcquired) {
        $installationMutex.ReleaseMutex()
    }
    $installationMutex.Dispose()
}
