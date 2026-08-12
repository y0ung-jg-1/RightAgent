[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$Repository,
    [string]$Environment = 'release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$pfxPath = Join-Path $repoRoot '.local\signing\RightAgent.pfx'
$protectedPasswordPath = Join-Path $repoRoot '.local\signing\RightAgent.pfx.password.dpapi'

foreach ($requiredPath in $pfxPath, $protectedPasswordPath) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Release signing material is missing: $requiredPath. Run scripts\New-ReleaseCertificate.ps1 first."
    }
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    throw 'GitHub CLI (gh) was not found.'
}
& $gh.Source auth status
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated.'
}

if (-not $Repository) {
    $Repository = (& $gh.Source repo view --json nameWithOwner --jq '.nameWithOwner').Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($Repository)) {
        throw 'Could not resolve the current GitHub repository.'
    }
}
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Invalid GitHub repository name: $Repository"
}
if ($Environment -notmatch '^[A-Za-z0-9_.-]+$') {
    throw "Invalid GitHub environment name: $Environment"
}
if (-not $PSCmdlet.ShouldProcess(
    "GitHub repository '$Repository', environment '$Environment'",
    'Create the environment if needed and upload the encrypted RightAgent signing secrets'
)) {
    Write-Host 'No GitHub settings or secrets were changed.'
    return
}

& $gh.Source api --method PUT "repos/$Repository/environments/$Environment" -F wait_timer=0 -F prevent_self_review=false --silent
if ($LASTEXITCODE -ne 0) {
    throw "Could not create or update GitHub environment '$Environment'."
}

function Set-EnvironmentSecret {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Value
    )

    $processInfo = [Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $gh.Source
    $processInfo.Arguments = "secret set $Name --repo $Repository --env $Environment"
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardInput = $true
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $processInfo.CreateNoWindow = $true

    $process = [Diagnostics.Process]::Start($processInfo)
    try {
        $process.StandardInput.Write($Value)
        $process.StandardInput.Close()
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "GitHub CLI could not set $Name. $standardError"
        }
        if ($standardOutput) {
            Write-Host $standardOutput.Trim()
        }
    }
    finally {
        $process.Dispose()
    }
}

$pfxBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath))
$securePassword = ConvertTo-SecureString (Get-Content -LiteralPath $protectedPasswordPath -Raw)
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    Set-EnvironmentSecret -Name 'RIGHTAGENT_SIGNING_PFX_BASE64' -Value $pfxBase64
    Set-EnvironmentSecret -Name 'RIGHTAGENT_SIGNING_PFX_PASSWORD' -Value $plainPassword
}
finally {
    $plainPassword = $null
    $pfxBase64 = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
}

$configuredSecrets = @(& $gh.Source secret list --repo $Repository --env $Environment --json name --jq '.[].name')
if ($LASTEXITCODE -ne 0 -or
    $configuredSecrets -notcontains 'RIGHTAGENT_SIGNING_PFX_BASE64' -or
    $configuredSecrets -notcontains 'RIGHTAGENT_SIGNING_PFX_PASSWORD') {
    throw 'GitHub did not confirm both RightAgent release signing secrets.'
}

Write-Host "Configured RightAgent signing secrets for $Repository environment '$Environment'."
