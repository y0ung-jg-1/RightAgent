[CmdletBinding()]
param(
    # Optional dedicated settings directory for the app under test. When omitted the
    # unpackaged fallback %LOCALAPPDATA%\RightAgent\settings.json is used.
    [string]$SettingsPath,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repoRoot 'RightAgent.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\RightAgent.App.exe'

if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot 'RightAgent.App\RightAgent.App.csproj') -c Debug -p:Platform=x64 --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'RightAgent.App build failed.' }
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "RightAgent.App executable not found: $executable"
}

# Close a previous instance started from this build output. A packaged
# installation lives at a different path and is deliberately left alone.
Get-Process RightAgent.App -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path -ieq $executable } |
    Stop-Process -Force

if ($SettingsPath) {
    $env:RIGHTAGENT_SETTINGS_PATH = $SettingsPath
    Write-Host "RIGHTAGENT_SETTINGS_PATH = $SettingsPath"
}
else {
    Write-Host "Settings file: $env:LOCALAPPDATA\RightAgent\settings.json (unpackaged fallback)"
}

Start-Process -FilePath $executable
Write-Host 'RightAgent settings app started (Debug x64, unpackaged).'
