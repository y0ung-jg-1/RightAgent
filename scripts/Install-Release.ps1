[CmdletBinding()]
param(
    [string]$PackagePath,
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

        [string[]]$DependencyPath = @()
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

        $percentComplete = [Math]::Min(100, [Math]::Max(0, [int]$progressRecord.PercentComplete))
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

        Write-RightAgentInstallationProgress -PercentComplete 0
        [void]$packageInstaller.Invoke()

        if ($packageInstaller.HadErrors -or $packageInstaller.Streams.Error.Count -gt 0) {
            $deploymentErrors = @($packageInstaller.Streams.Error | ForEach-Object { $_.ToString() })
            $deploymentDetail = ($deploymentErrors -join [Environment]::NewLine).Trim()
            if (-not $deploymentDetail) {
                $deploymentDetail = 'The Windows package deployment operation failed without returning an error message.'
            }
            throw $deploymentDetail
        }

        Write-RightAgentInstallationProgress -PercentComplete 100
    }
    finally {
        $packageInstaller.Streams.Progress.remove_DataAdded($progressHandler)
        $packageInstaller.Dispose()
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

    $packageVersion = [version]([string]$manifest.Package.Identity.Version)
    $sameVersionPackage = Get-AppxPackage -Name 'RightAgent' -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Publisher -ceq 'CN=RightAgent' -and
            [version]$_.Version -eq $packageVersion
        } |
        Select-Object -First 1
    if ($sameVersionPackage) {
        Write-Host "RightAgent $packageVersion is already installed for the current user."
        Write-RightAgentInstallationProgress -PercentComplete 100
        return
    }

    $dependencyDirectory = Join-Path $PSScriptRoot 'Dependencies\x64'
    $dependencies = if (Test-Path -LiteralPath $dependencyDirectory -PathType Container) {
        @(Get-ChildItem -LiteralPath $dependencyDirectory -File |
            Where-Object { $_.Extension -in '.msix', '.appx' } |
            Select-Object -ExpandProperty FullName)
    } else {
        @()
    }

    Add-RightAgentAppxPackage -Path $PackagePath -DependencyPath $dependencies

    Write-Host 'RightAgent installed. If Explorer cached the old menu, close all Explorer windows or sign out once.'
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
