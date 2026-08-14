# Keep this rule identical to CommandSlotPlanner.RequiredSlotCount.
function Get-RightAgentRequiredCommandSlotCount {
    param(
        [string]$SettingsPath
    )

    if ([string]::IsNullOrWhiteSpace($SettingsPath)) {
        if (Get-Command -Name 'Get-RightAgentUserLocalAppData' -CommandType Function -ErrorAction SilentlyContinue) {
            $SettingsPath = Join-Path (Get-RightAgentUserLocalAppData) 'RightAgent\settings.json'
        }
        else {
            $SettingsPath = Join-Path $env:LOCALAPPDATA 'RightAgent\settings.json'
        }
    }

    if (-not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) {
        return 1
    }

    try {
        $settings = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Write-Host "Could not parse settings at '$SettingsPath': $($_.Exception.Message). Defaulting to one command slot."
        return 1
    }

    if ($settings.PSObject.Properties.Name -contains 'menuEnabled' -and -not [bool]$settings.menuEnabled) {
        return 0
    }

    $enabled = @($settings.agents | Where-Object {
        if (-not $_.enabled) {
            return $false
        }

        $actionType = [string]$_.action.type
        $actionValue = ([string]$_.action.value).Trim()
        if ([string]::IsNullOrWhiteSpace($actionValue)) {
            return $false
        }

        if ($actionType -ieq 'url') {
            return $actionValue -match '^https?://'
        }

        return $true
    })
    if ($enabled.Count -eq 0) {
        return 0
    }
    if ([string]$settings.menuMode -ieq 'multiDirect') {
        return [Math]::Min(16, [int]$enabled.Count)
    }
    return 1
}
