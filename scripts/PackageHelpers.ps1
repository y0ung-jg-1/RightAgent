function Get-RightAgentManifestPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [ValidateSet('Development', 'Release')]
        [string]$PackageIdentity = 'Development'
    )

    $manifestName = if ($PackageIdentity -eq 'Release') {
        'Package.Release.appxmanifest'
    } else {
        'Package.appxmanifest'
    }
    $manifestPath = Join-Path $RepoRoot "RightAgent.Package\$manifestName"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Package manifest was not found: $manifestPath"
    }

    return $manifestPath
}

function Get-RightAgentPackagePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration,

        [ValidateSet('Development', 'Release')]
        [string]$PackageIdentity = 'Development'
    )

    $manifestPath = Get-RightAgentManifestPath -RepoRoot $RepoRoot -PackageIdentity $PackageIdentity

    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $version = [string]$manifest.Package.Identity.Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'The package manifest does not contain an Identity version.'
    }

    $configurationSuffix = if ($Configuration -eq 'Debug') { '_Debug' } else { '' }
    $expectedName = "RightAgent.Package_${version}_x64${configurationSuffix}.msix"
    $packageRoot = Join-Path $RepoRoot "artifacts\package\$Configuration"
    if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
        throw "Package output directory was not found: $packageRoot"
    }

    $packages = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Where-Object {
            $_.Name -match '^RightAgent\.Package_.+_x64(?:_Debug)?\.msix$' -and
            $_.DirectoryName -notmatch '\\Dependencies(\\|$)'
        })

    if ($packages.Count -ne 1) {
        $found = if ($packages.Count -eq 0) {
            'none'
        } else {
            ($packages.FullName -join [Environment]::NewLine)
        }
        throw "Expected exactly one main package named '$expectedName', but found $($packages.Count):$([Environment]::NewLine)$found"
    }

    if ($packages[0].Name -cne $expectedName) {
        throw "Expected package '$expectedName', but found '$($packages[0].Name)'."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $packagedManifestEntry = $archive.GetEntry('AppxManifest.xml')
        if (-not $packagedManifestEntry) {
            throw "Package '$($packages[0].FullName)' does not contain AppxManifest.xml."
        }
        $reader = [IO.StreamReader]::new($packagedManifestEntry.Open())
        try {
            [xml]$packagedManifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $expectedIdentity = $manifest.Package.Identity
    $actualIdentity = $packagedManifest.Package.Identity
    foreach ($attribute in 'Name', 'Publisher', 'Version', 'ProcessorArchitecture') {
        $expectedValue = [string]$expectedIdentity.$attribute
        $actualValue = [string]$actualIdentity.$attribute
        if ($actualValue -cne $expectedValue) {
            throw "Package identity mismatch for $attribute. Expected '$expectedValue', found '$actualValue' in '$($packages[0].FullName)'."
        }
    }

    return $packages[0].FullName
}

function Get-RightAgentPackageVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [ValidateSet('Development', 'Release')]
        [string]$PackageIdentity = 'Development'
    )

    $manifestPath = Get-RightAgentManifestPath -RepoRoot $RepoRoot -PackageIdentity $PackageIdentity
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $version = [string]$manifest.Package.Identity.Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "The package manifest does not contain an Identity version: $manifestPath"
    }
    return $version
}

function Get-RightAgentDisplayVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PackageVersion
    )

    $version = [version]$PackageVersion
    $displayVersion = "$($version.Major).$($version.Minor).$($version.Build)"
    if ($version.Revision -gt 0) {
        $displayVersion += ".$($version.Revision)"
    }
    return $displayVersion
}

function Get-RightAgentAppPublishPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration
    )

    $publishDirectory = [IO.Path]::GetFullPath((Join-Path $RepoRoot "artifacts\app\$Configuration\win-x64"))
    $executablePath = Join-Path $publishDirectory 'RightAgent.App.exe'
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Unpackaged settings app was not found: $executablePath"
    }
    return $publishDirectory
}

function Get-RightAgentCommandPackagePaths {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration,

        [ValidateSet('Development', 'Release')]
        [string]$PackageIdentity = 'Development'
    )

    $manifestPath = Get-RightAgentManifestPath -RepoRoot $RepoRoot -PackageIdentity $PackageIdentity
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $mainIdentity = $manifest.Package.Identity
    $mainName = [string]$mainIdentity.Name
    $publisher = [string]$mainIdentity.Publisher
    $version = [string]$mainIdentity.Version
    if ([string]::IsNullOrWhiteSpace($mainName) -or
        [string]::IsNullOrWhiteSpace($publisher) -or
        [string]::IsNullOrWhiteSpace($version)) {
        throw "The main package manifest has an incomplete identity: $manifestPath"
    }

    $commandRoot = [IO.Path]::GetFullPath((Join-Path $RepoRoot "artifacts\package\$Configuration\Commands"))
    if (-not (Test-Path -LiteralPath $commandRoot -PathType Container)) {
        throw "Command package output directory was not found: $commandRoot"
    }
    $actualPackages = @(Get-ChildItem -LiteralPath $commandRoot -Filter '*.msix' -File)
    if ($actualPackages.Count -ne 16) {
        throw "Expected exactly 16 command packages in '$commandRoot', but found $($actualPackages.Count)."
    }

    $expectedPaths = foreach ($slot in 0..15) {
        $slotText = $slot.ToString('D2')
        $expectedName = "$mainName.Command$slotText`_${version}_x64.msix"
        $matches = @($actualPackages | Where-Object { $_.Name -ceq $expectedName })
        if ($matches.Count -ne 1) {
            throw "Expected exactly one command package named '$expectedName', but found $($matches.Count)."
        }
        $matches[0].FullName
    }

    return $expectedPaths
}

. (Join-Path $PSScriptRoot 'CommandSlotCount.ps1')
