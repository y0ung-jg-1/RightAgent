[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'Validate-Environment.ps1')

Push-Location $repoRoot
try {
    & dotnet test '.\RightAgent.Core.Tests\RightAgent.Core.Tests.csproj' -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Managed tests failed.' }

    & dotnet test '.\RightAgent.App.Tests\RightAgent.App.Tests.csproj' -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'App tests failed.' }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vsPath = & $vswhere -latest -products * -version '[18.0,19.0)' -property installationPath
    $msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
        $msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
    }
    & $msbuild '.\RightAgent.Native.Tests\RightAgent.Native.Tests.vcxproj' /m /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 "/p:SolutionDir=$repoRoot\"
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed to build.' }

    & (Join-Path $repoRoot "artifacts\bin\$Configuration\x64\RightAgent.Native.Tests.exe")
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
}
finally {
    Pop-Location
}
