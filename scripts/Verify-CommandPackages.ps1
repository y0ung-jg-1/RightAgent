[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Development', 'Release')]
    [string]$PackageIdentity = 'Development'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageHelpers.ps1')

$mainManifestPath = Get-RightAgentManifestPath -RepoRoot $repoRoot -PackageIdentity $PackageIdentity
[xml]$mainManifest = Get-Content -LiteralPath $mainManifestPath -Raw
$mainIdentity = $mainManifest.Package.Identity
$mainName = [string]$mainIdentity.Name
$publisher = [string]$mainIdentity.Publisher
$version = [string]$mainIdentity.Version
$packagePaths = @(Get-RightAgentCommandPackagePaths `
    -RepoRoot $repoRoot `
    -Configuration $Configuration `
    -PackageIdentity $PackageIdentity)

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($slot in 0..15) {
    $slotText = $slot.ToString('D2')
    $expectedClassId = 'F7E08D{0:X2}-676E-4D4B-950A-5B4451E19E3C' -f (0x6D + $slot)
    $packagePath = $packagePaths[$slot]
    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entries = @{}
        foreach ($entry in $archive.Entries) {
            $entries[$entry.FullName.Replace('\', '/')] = $entry
        }
        foreach ($requiredEntry in @(
            'AppxManifest.xml',
            'RightAgent.Shell.dll',
            'RightAgent.Launcher.exe',
            'LICENSE',
            'THIRD_PARTY_NOTICES.md',
            'Assets/Agents/cursor.ico',
            'Assets/Agents/rightagent.ico'
        )) {
            if (-not $entries.ContainsKey($requiredEntry)) {
                throw "Command package $slotText is missing required entry: $requiredEntry"
            }
        }

        $reader = [IO.StreamReader]::new($entries['AppxManifest.xml'].Open())
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

    $identity = $manifest.Package.Identity
    if ([string]$identity.Name -cne "$mainName.Command$slotText" -or
        [string]$identity.Publisher -cne $publisher -or
        [string]$identity.Version -cne $version -or
        [string]$identity.ProcessorArchitecture -cne 'x64') {
        throw "Command package $slotText has an invalid identity."
    }

    $namespaces = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaces.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespaces.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
    $namespaces.AddNamespace('uap3', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3')
    $namespaces.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')
    $namespaces.AddNamespace('desktop4', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/4')
    $namespaces.AddNamespace('desktop5', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')

    if ($manifest.SelectNodes('//uap3:MainPackageDependency', $namespaces).Count -ne 0) {
        throw "Command package $slotText must keep an independent package identity for root-menu attribution."
    }
    $applications = @($manifest.SelectNodes('/f:Package/f:Applications/f:Application', $namespaces))
    if ($applications.Count -ne 1 -or [string]$applications[0].Id -cne "Command$slotText") {
        throw "Command package $slotText must contain exactly one aligned application."
    }
    $visualElements = $applications[0].SelectSingleNode('uap:VisualElements', $namespaces)
    if (-not $visualElements -or
        [string]$visualElements.AppListEntry -cne 'none' -or
        [string]$visualElements.DisplayName -cne "RightAgent Command $slotText") {
        throw "Command package $slotText must keep its unique hidden application attribution."
    }
    $classes = @($applications[0].SelectNodes(
        'f:Extensions/com:Extension[@Category="windows.comServer"]/com:ComServer/com:SurrogateServer/com:Class',
        $namespaces))
    if ($classes.Count -ne 1 -or
        [string]$classes[0].Id -cne $expectedClassId -or
        [string]$classes[0].Path -cne 'RightAgent.Shell.dll' -or
        [string]$classes[0].ThreadingModel -cne 'STA') {
        throw "Command package $slotText has an invalid COM class registration."
    }
    $itemTypes = @($applications[0].SelectNodes(
        'f:Extensions/desktop4:Extension[@Category="windows.fileExplorerContextMenus"]/desktop4:FileExplorerContextMenus/desktop5:ItemType',
        $namespaces))
    if ($itemTypes.Count -ne 2 -or
        [string]$itemTypes[0].Type -cne 'Directory' -or
        [string]$itemTypes[1].Type -cne 'Directory\Background') {
        throw "Command package $slotText must register both folder targets exactly once."
    }
    foreach ($itemType in $itemTypes) {
        $verbs = @($itemType.SelectNodes('desktop5:Verb', $namespaces))
        if ($verbs.Count -ne 1 -or [string]$verbs[0].Clsid -cne $expectedClassId) {
            throw "Command package $slotText has an invalid Explorer verb for '$($itemType.Type)'."
        }
    }
}

Write-Host "Verified 16 independently attributed RightAgent command packages ($PackageIdentity identity)."
