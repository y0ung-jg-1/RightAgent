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

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($development.NameTable)
$namespaceManager.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespaceManager.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
$namespaceManager.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')
$namespaceManager.AddNamespace('desktop4', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/4')
$namespaceManager.AddNamespace('desktop5', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')

$rootClassId = 'F7E08D6D-676E-4D4B-950A-5B4451E19E3C'
$classes = @($development.SelectNodes('//com:Class', $namespaceManager))
if ($classes.Count -ne 1 -or [string]$classes[0].Id -cne $rootClassId) {
    throw 'Package manifest must register exactly one root Explorer command COM class.'
}

$applications = @($development.SelectNodes('/f:Package/f:Applications/f:Application', $namespaceManager))
if ($applications.Count -ne 1 -or [string]$applications[0].Id -cne 'App') {
    throw 'Package manifest must contain exactly one visible application identity.'
}

$application = $applications[0]
$visualElements = $application.SelectSingleNode('uap:VisualElements', $namespaceManager)
if (-not $visualElements -or [string]$visualElements.AppListEntry -cne 'default') {
    throw "Application 'App' must remain visible with AppListEntry='default'."
}

$itemTypes = @($application.SelectNodes(
    'f:Extensions/desktop4:Extension[@Category="windows.fileExplorerContextMenus"]/desktop4:FileExplorerContextMenus/desktop5:ItemType',
    $namespaceManager))
if ($itemTypes.Count -ne 2 -or
    [string]$itemTypes[0].Type -cne 'Directory' -or
    [string]$itemTypes[1].Type -cne 'Directory\Background') {
    throw "Application 'App' must register Directory and Directory\Background exactly once."
}

$expectedVerbIds = @('RightAgentOpenDirectory', 'RightAgentOpenDirectoryBackground')
for ($target = 0; $target -lt $itemTypes.Count; ++$target) {
    $verbs = @($itemTypes[$target].SelectNodes('desktop5:Verb', $namespaceManager))
    if ($verbs.Count -ne 1 -or
        [string]$verbs[0].Id -cne $expectedVerbIds[$target] -or
        [string]$verbs[0].Clsid -cne $rootClassId) {
        throw "Application 'App' has an invalid Explorer verb registration."
    }
}

Write-Host 'Verified package manifests: identities match, with one aligned root Explorer command for both folder targets.'
