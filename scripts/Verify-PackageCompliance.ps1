[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$resolvedPackagePath = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $resolvedPackagePath -PathType Leaf)) {
    throw "Package was not found: $resolvedPackagePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedPackagePath)
try {
    $entries = @{}
    foreach ($entry in $archive.Entries) {
        $entries[$entry.FullName.Replace('\', '/')] = $entry
    }

    $requiredEntries = @(
        'LICENSE',
        'THIRD_PARTY_NOTICES.md',
        'ThirdPartyNotices/WindowsAppSDK/LICENSE.txt',
        'ThirdPartyNotices/WindowsAppSDK/NOTICE.txt',
        'ThirdPartyNotices/WindowsAppSDKWinUI/LICENSE.txt',
        'ThirdPartyNotices/WindowsAppSDKWinUI/NOTICE.txt',
        'ThirdPartyNotices/WindowsAppSDKML/LICENSE.txt',
        'ThirdPartyNotices/WindowsAppSDKML/THIRD_PARTY_NOTICES.txt',
        'ThirdPartyNotices/DotNETRuntime/LICENSE.txt',
        'ThirdPartyNotices/DotNETRuntime/THIRD_PARTY_NOTICES.txt',
        'ThirdPartyNotices/WebView2/LICENSE.txt',
        'ThirdPartyNotices/WebView2/NOTICE.txt',
        'ThirdPartyNotices/SystemNumericsTensors/LICENSE.txt',
        'ThirdPartyNotices/SystemNumericsTensors/THIRD_PARTY_NOTICES.txt',
        'Assets/Agents/cursor.svg',
        'Assets/Agents/cursor.ico'
    )
    foreach ($requiredEntry in $requiredEntries) {
        if (-not $entries.ContainsKey($requiredEntry)) {
            throw "Required package notice is missing: $requiredEntry"
        }
    }

    if (-not $entries.ContainsKey('RightAgent.App.deps.json')) {
        throw 'RightAgent.App.deps.json is missing from the package.'
    }

    $reader = [IO.StreamReader]::new($entries['RightAgent.App.deps.json'].Open())
    try {
        $dependencies = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    $libraryNames = @($dependencies.libraries.PSObject.Properties.Name)
    $documentedLibraries = @(
        'Microsoft.WindowsAppSDK/2.3.1',
        'Microsoft.WindowsAppSDK.WinUI/2.3.0',
        'Microsoft.WindowsAppSDK.ML/2.1.74',
        'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.11',
        'Microsoft.Web.WebView2/1.0.3719.77',
        'System.Numerics.Tensors/9.0.0'
    )
    foreach ($documentedLibrary in $documentedLibraries) {
        if ($libraryNames -cnotcontains $documentedLibrary) {
            throw "The packaged dependency inventory changed. Refresh THIRD_PARTY_NOTICES.md and ThirdPartyNotices/ for: $documentedLibrary"
        }
    }

    Write-Host "Verified required package files: $($requiredEntries.Count); $($documentedLibraries.Count) documented dependency versions."
}
finally {
    $archive.Dispose()
}
