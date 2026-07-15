param(
    [string]$GameRoot = "D:\HS2",
    [int]$DurationSeconds = 300,
    [switch]$NoKill,
    [switch]$SkipLaunch
)

$ErrorActionPreference = "Stop"

$pluginCfg = Join-Path $GameRoot "BepInEx\config\com.hs2.orbitandexciter.cfg"
$logDir = Join-Path $GameRoot "BepInEx\LogOutput"
$tracePath = Join-Path $logDir "HS2OrbitAndExciter_fsm.ndjson"
$gameExe = Join-Path $GameRoot "HoneySelect2.exe"
$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$reportPath = Join-Path $toolDir "orbit_smoke_report.html"

function Set-OrbitTraceConfig {
    param([bool]$Enabled)

    $value = if ($Enabled) { "true" } else { "false" }
    if (!(Test-Path -LiteralPath $pluginCfg)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pluginCfg) | Out-Null
        @"
## Settings file was created by HS2 Orbit and Exciter
## Plugin GUID: com.hs2.orbitandexciter

[Diagnostics]

## Write detailed NDJSON state-machine traces. Default false; enable only for automated diagnosis runs.
# Setting type: Boolean
# Default value: false
EnableStateMachineTrace = $value
"@ | Set-Content -LiteralPath $pluginCfg -Encoding UTF8
        return
    }

    $text = Get-Content -LiteralPath $pluginCfg -Raw -Encoding UTF8
    if ($text -match "(?m)^EnableStateMachineTrace\s*=") {
        $text = [regex]::Replace($text, "(?m)^EnableStateMachineTrace\s*=.*$", "EnableStateMachineTrace = $value")
    } else {
        if ($text -notmatch "(?m)^\[Diagnostics\]") {
            $text = $text.TrimEnd() + "`r`n`r`n[Diagnostics]`r`n"
        }
        $text = $text.TrimEnd() + "`r`n`r`n## Write detailed NDJSON state-machine traces. Default false; enable only for automated diagnosis runs.`r`n# Setting type: Boolean`r`n# Default value: false`r`nEnableStateMachineTrace = $value`r`n"
    }
    Set-Content -LiteralPath $pluginCfg -Encoding UTF8 -Value $text
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Remove-Item -LiteralPath $tracePath -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $logDir -File -Filter "debug-*.log" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Set-OrbitTraceConfig -Enabled $true

$proc = $null
try {
    if (!$SkipLaunch) {
        if (!(Test-Path -LiteralPath $gameExe)) {
            throw "Game executable not found: $gameExe"
        }
        $proc = Start-Process -FilePath $gameExe -WorkingDirectory $GameRoot -PassThru -WindowStyle Hidden
        Write-Host "Started HoneySelect2 pid=$($proc.Id). Duration=${DurationSeconds}s"
    } else {
        Write-Host "SkipLaunch set. Waiting for existing game/session logs."
    }

    $deadline = (Get-Date).AddSeconds($DurationSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if ($proc -ne $null -and $proc.HasExited) {
            Write-Host "Game exited before timeout."
            break
        }
    }
}
finally {
    if ($proc -ne $null -and !$proc.HasExited -and !$NoKill) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Write-Host "Stopped HoneySelect2 pid=$($proc.Id)."
    }
    Set-OrbitTraceConfig -Enabled $false
}

if (Test-Path -LiteralPath $tracePath) {
    python (Join-Path $toolDir "_assert_fsm_regression.py") $tracePath
    python (Join-Path $toolDir "orbit_trace_report.py") $tracePath $reportPath
    Write-Host "Report: $reportPath"
} else {
    Write-Warning "No trace produced: $tracePath"
}
