[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$developmentPath = Join-Path $repoRoot 'RightAgent.Package\Package.appxmanifest'
$releasePath = Join-Path $repoRoot 'RightAgent.Package\Package.Release.appxmanifest'

[xml]$development = Get-Content -LiteralPath $developmentPath -Raw
[xml]$release = Get-Content -LiteralPath $releasePath -Raw

$developmentIdentity = $development.Package.Identity
$releaseIdentity = $release.Package.Identity
if ([string]$developmentIdentity.Name -cne 'RightAgent.Dev' -or
    [string]$developmentIdentity.Publisher -cne 'CN=RightAgent Dev') {
    throw 'The development manifest identity must remain RightAgent.Dev / CN=RightAgent Dev.'
}
if ([string]$releaseIdentity.Name -cne 'RightAgent' -or
    [string]$releaseIdentity.Publisher -cne 'CN=RightAgent') {
    throw 'The release manifest identity must remain RightAgent / CN=RightAgent.'
}

foreach ($attribute in 'Version', 'ProcessorArchitecture') {
    if ([string]$developmentIdentity.$attribute -cne [string]$releaseIdentity.$attribute) {
        throw "Package manifest identity attribute '$attribute' differs between development and release."
    }
}

$releaseIdentity.SetAttribute('Name', [string]$developmentIdentity.Name)
$releaseIdentity.SetAttribute('Publisher', [string]$developmentIdentity.Publisher)
if ($development.OuterXml -cne $release.OuterXml) {
    throw 'Development and release package manifests differ outside the allowed Name and Publisher identity fields.'
}

Write-Host 'Verified package manifests: only Name and Publisher differ between development and release.'
