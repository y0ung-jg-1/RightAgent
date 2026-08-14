[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$developmentPath = Join-Path $repoRoot 'RightAgent.Package\Package.appxmanifest'
$releasePath = Join-Path $repoRoot 'RightAgent.Package\Package.Release.appxmanifest'
$commandTemplatePath = Join-Path $repoRoot 'RightAgent.CommandPackage\Package.appxmanifest.template'

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

$mainNamespaces = [Xml.XmlNamespaceManager]::new($development.NameTable)
$mainNamespaces.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$mainNamespaces.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
$mainNamespaces.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')
$mainNamespaces.AddNamespace('desktop4', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/4')

$mainApplications = @($development.SelectNodes('/f:Package/f:Applications/f:Application', $mainNamespaces))
if ($mainApplications.Count -ne 1 -or [string]$mainApplications[0].Id -cne 'App') {
    throw 'The main package must contain exactly the visible RightAgent settings application.'
}
$mainVisualElements = $mainApplications[0].SelectSingleNode('uap:VisualElements', $mainNamespaces)
if (-not $mainVisualElements -or [string]$mainVisualElements.AppListEntry -cne 'default') {
    throw 'The main RightAgent application must remain visible in the app list.'
}
$protocols = @($mainApplications[0].SelectNodes(
    'f:Extensions/uap:Extension[@Category="windows.protocol"]/uap:Protocol[@Name="rightagent"]',
    $mainNamespaces))
if ($protocols.Count -ne 1) {
    throw 'The main package must own the rightagent: settings protocol exactly once.'
}
if ($development.SelectNodes('//com:*', $mainNamespaces).Count -ne 0 -or
    $development.SelectNodes('//desktop4:*', $mainNamespaces).Count -ne 0) {
    throw 'Explorer COM and context-menu registrations must live only in the command packages.'
}

$template = [IO.File]::ReadAllText($commandTemplatePath)
$requiredTokens = @(
    '__PACKAGE_NAME__',
    '__PUBLISHER__',
    '__VERSION__',
    '__SLOT__',
    '__CLSID__'
)
foreach ($token in $requiredTokens) {
    if ($template.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
        throw "Command package manifest template is missing token: $token"
    }
}

$sampleClassId = 'F7E08D6D-676E-4D4B-950A-5B4451E19E3C'
$sampleManifestText = $template.
    Replace('__PACKAGE_NAME__', 'RightAgent.Command00').
    Replace('__PUBLISHER__', 'CN=RightAgent').
    Replace('__VERSION__', [string]$developmentIdentity.Version).
    Replace('__SLOT__', '00').
    Replace('__CLSID__', $sampleClassId)
[xml]$sampleManifest = $sampleManifestText
$commandNamespaces = [Xml.XmlNamespaceManager]::new($sampleManifest.NameTable)
$commandNamespaces.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$commandNamespaces.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
$commandNamespaces.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')
$commandNamespaces.AddNamespace('desktop4', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/4')
$commandNamespaces.AddNamespace('desktop5', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')

$commandApplications = @($sampleManifest.SelectNodes('/f:Package/f:Applications/f:Application', $commandNamespaces))
if ($commandApplications.Count -ne 1) {
    throw 'Each command package must contain exactly one hidden application identity.'
}
$commandVisualElements = $commandApplications[0].SelectSingleNode('uap:VisualElements', $commandNamespaces)
if (-not $commandVisualElements -or
    [string]$commandVisualElements.AppListEntry -cne 'none' -or
    [string]$commandVisualElements.DisplayName -cne 'RightAgent Command 00') {
    throw 'Command package applications must remain hidden and independently attributed.'
}
$classes = @($commandApplications[0].SelectNodes(
    'f:Extensions/com:Extension[@Category="windows.comServer"]/com:ComServer/com:SurrogateServer/com:Class',
    $commandNamespaces))
if ($classes.Count -ne 1 -or
    [string]$classes[0].Id -cne $sampleClassId -or
    [string]$classes[0].Path -cne 'RightAgent.Shell.dll' -or
    [string]$classes[0].ThreadingModel -cne 'STA') {
    throw 'Each command package must register exactly one aligned Explorer command COM class.'
}
$itemTypes = @($commandApplications[0].SelectNodes(
    'f:Extensions/desktop4:Extension[@Category="windows.fileExplorerContextMenus"]/desktop4:FileExplorerContextMenus/desktop5:ItemType',
    $commandNamespaces))
if ($itemTypes.Count -ne 2 -or
    [string]$itemTypes[0].Type -cne 'Directory' -or
    [string]$itemTypes[1].Type -cne 'Directory\Background') {
    throw 'Each command package must register Directory and Directory\Background exactly once.'
}
foreach ($itemType in $itemTypes) {
    $verbs = @($itemType.SelectNodes('desktop5:Verb', $commandNamespaces))
    if ($verbs.Count -ne 1 -or [string]$verbs[0].Clsid -cne $sampleClassId) {
        throw "Command package has an invalid Explorer verb registration for '$($itemType.Type)'."
    }
}

Write-Host 'Verified package manifests: settings identity plus independently attributed command package template.'
