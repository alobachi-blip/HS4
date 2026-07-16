param(
    [string]$GameRoot = "D:\HS2",
    [int]$DurationSeconds = 300,
    [ValidateSet("Fast", "Full")]
    [string]$ValidationProfile = "Fast",
    [switch]$DirectH,
    [switch]$Coverage,
    [switch]$FaintnessStress,
    [ValidateRange(1, 100)]
    [int]$FemaleOrgasmTarget = 10,
    [ValidateRange(0.01, 20)]
    [double]$FaintnessStressFeelPerSecond = 2.5,
    [switch]$NoDirectHOrbitAssist,
    [int]$DirectHMapId = 3,
    [int]$DirectHEventNo = -1,
    [double]$DirectHDelaySeconds = 8,
    [string]$DirectHFemaleCardPath = "",
    [string]$DirectHSecondFemaleCardPath = "",
    [string]$DirectHMaleCardPath = "",
    [switch]$NoKill,
    [switch]$SkipLaunch
)

$ErrorActionPreference = "Stop"

$pluginCfg = Join-Path $GameRoot "BepInEx\config\com.hs2.orbitandexciter.cfg"
$logDir = Join-Path $GameRoot "BepInEx\LogOutput"
$tracePath = Join-Path $logDir "HS2OrbitAndExciter_fsm.ndjson"
$keyframeDir = Join-Path $logDir "OrbitSmokeKeyframes"
$gameExe = Join-Path $GameRoot "HoneySelect2.exe"
$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$reportPath = Join-Path $toolDir "orbit_smoke_report.html"

function Set-OrbitConfigValue {
    param(
        [string]$Section,
        [string]$Key,
        [string]$Value,
        [string]$Description = ""
    )

    if (!(Test-Path -LiteralPath $pluginCfg)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pluginCfg) | Out-Null
        @"
## Settings file was created by HS2 Orbit and Exciter
## Plugin GUID: com.hs2.orbitandexciter

[$Section]
$Description
$Key = $Value
"@ | Set-Content -LiteralPath $pluginCfg -Encoding UTF8
        return
    }

    $text = Get-Content -LiteralPath $pluginCfg -Raw -Encoding UTF8
    $escapedSection = [regex]::Escape($Section)
    $escapedKey = [regex]::Escape($Key)
    $line = "$Key = $Value"
    $desc = if ([string]::IsNullOrWhiteSpace($Description)) { "" } else { $Description.TrimEnd() + "`r`n" }
    $sectionPattern = "(?ms)(^\[$escapedSection\]\s*(?:\r?\n))(?<body>.*?)(?=^\[|\z)"
    $sectionMatch = [regex]::Match($text, $sectionPattern)

    if ($sectionMatch.Success) {
        $bodyGroup = $sectionMatch.Groups["body"]
        $body = $bodyGroup.Value
        $keyPattern = "(?m)^$escapedKey\s*=.*$"
        if ([regex]::IsMatch($body, $keyPattern)) {
            $newBody = [regex]::Replace(
                $body,
                $keyPattern,
                [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $line }
            )
        } else {
            $newBody = $body.TrimEnd() + "`r`n`r`n$desc$line`r`n"
        }
        $text = $text.Substring(0, $bodyGroup.Index) + $newBody + $text.Substring($bodyGroup.Index + $bodyGroup.Length)
    } else {
        $text = $text.TrimEnd() + "`r`n`r`n[$Section]`r`n$desc$line`r`n"
    }
    Set-Content -LiteralPath $pluginCfg -Encoding UTF8 -Value $text
}

function Get-OrbitConfigValue {
    param(
        [string]$Section,
        [string]$Key
    )

    if (!(Test-Path -LiteralPath $pluginCfg)) {
        return $null
    }
    $text = Get-Content -LiteralPath $pluginCfg -Raw -Encoding UTF8
    $escapedSection = [regex]::Escape($Section)
    $escapedKey = [regex]::Escape($Key)
    $sectionPattern = "(?ms)(^\[$escapedSection\]\s*(?:\r?\n))(?<body>.*?)(?=^\[|\z)"
    $sectionMatch = [regex]::Match($text, $sectionPattern)
    if (!$sectionMatch.Success) {
        return $null
    }
    $keyMatch = [regex]::Match($sectionMatch.Groups["body"].Value, "(?m)^$escapedKey\s*=\s*(?<value>.*)$")
    if (!$keyMatch.Success) {
        return $null
    }
    return $keyMatch.Groups["value"].Value.Trim()
}

function Set-OrbitTraceConfig {
    param([bool]$Enabled)

    $value = if ($Enabled) { "true" } else { "false" }
    Set-OrbitConfigValue `
        -Section "Diagnostics" `
        -Key "EnableStateMachineTrace" `
        -Value $value `
        -Description "## Write detailed NDJSON state-machine traces. Default false; enable only for automated diagnosis runs.`r`n# Setting type: Boolean`r`n# Default value: false"
}

function Set-OrbitDirectHConfig {
    param([bool]$Enabled)

    $enabledValue = if ($Enabled) { "true" } else { "false" }
    Set-OrbitConfigValue -Section "Smoke" -Key "EnableDirectHSmokeDriver" -Value $enabledValue
    Set-OrbitConfigValue -Section "Smoke" -Key "DirectHSmokeDelaySeconds" -Value ([string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0}", $DirectHDelaySeconds))
    Set-OrbitConfigValue -Section "Smoke" -Key "DirectHSmokeMapId" -Value ([string]$DirectHMapId)
    Set-OrbitConfigValue -Section "Smoke" -Key "DirectHSmokeEventNo" -Value ([string]$DirectHEventNo)
    Set-OrbitConfigValue -Section "Smoke" -Key "DirectHSmokeFemaleCardPath" -Value $DirectHFemaleCardPath
    Set-OrbitConfigValue -Section "Smoke" -Key "DirectHSmokeSecondFemaleCardPath" -Value $DirectHSecondFemaleCardPath
    Set-OrbitConfigValue -Section "Smoke" -Key "DirectHSmokeMaleCardPath" -Value $DirectHMaleCardPath
    $orbitAssistValue = if ($Enabled -and !$NoDirectHOrbitAssist) { "true" } else { "false" }
    Set-OrbitConfigValue -Section "Smoke" -Key "EnableDirectHSmokeOrbitAssist" -Value $orbitAssistValue
    Set-OrbitConfigValue -Section "Smoke" -Key "EnableSmokeKeyframeScreenshots" -Value $enabledValue
    Set-OrbitConfigValue -Section "Smoke" -Key "SmokeKeyframeDirectory" -Value $keyframeDir
    $coverageValue = if ($Enabled -and $Coverage) { "true" } else { "false" }
    Set-OrbitConfigValue -Section "Smoke" -Key "EnableSmokeFamilyCoverage" -Value $coverageValue
    Set-OrbitConfigValue -Section "Smoke" -Key "SmokeFamilyCoverageSequence" -Value "A_Aibu,B_Houshi,C_Sonyu,D_Masturbation,E_Spnking,A_Les"
}

function Resolve-DefaultFemaleCardPath {
    if (![string]::IsNullOrWhiteSpace($DirectHFemaleCardPath)) {
        return $DirectHFemaleCardPath
    }

    $femaleDir = Join-Path $GameRoot "UserData\chara\female"
    if (!(Test-Path -LiteralPath $femaleDir)) {
        return ""
    }

    $card = Get-ChildItem -LiteralPath $femaleDir -Filter "*.png" -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        Select-Object -First 1

    if ($null -eq $card) {
        return ""
    }

    Write-Host "DirectH auto female card: $($card.FullName)"
    return $card.FullName
}

if ($DirectH) {
    $DirectHFemaleCardPath = Resolve-DefaultFemaleCardPath
    if ($Coverage -and [string]::IsNullOrWhiteSpace($DirectHSecondFemaleCardPath)) {
        $DirectHSecondFemaleCardPath = $DirectHFemaleCardPath
    }
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
New-Item -ItemType Directory -Force -Path $keyframeDir | Out-Null
Remove-Item -LiteralPath $tracePath -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $keyframeDir -File -Filter "*.png" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $logDir -File -Filter "debug-*.log" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Set-OrbitTraceConfig -Enabled $true
Set-OrbitDirectHConfig -Enabled ([bool]$DirectH)
$originalFeelAddPerSecond = Get-OrbitConfigValue -Section "Exciter" -Key "FeelAddPerSecondWhenOrbit"
if ($FaintnessStress) {
    Set-OrbitConfigValue -Section "Exciter" -Key "FeelAddPerSecondWhenOrbit" -Value ([string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0}", $FaintnessStressFeelPerSecond))
    Write-Host "Faintness stress: target=$FemaleOrgasmTarget female orgasms, feel/s=$FaintnessStressFeelPerSecond"
}

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
    Set-OrbitDirectHConfig -Enabled $false
    if ($FaintnessStress) {
        $restoreFeel = if ($null -ne $originalFeelAddPerSecond) { $originalFeelAddPerSecond } else { "0.1" }
        Set-OrbitConfigValue -Section "Exciter" -Key "FeelAddPerSecondWhenOrbit" -Value $restoreFeel
    }
}

if (Test-Path -LiteralPath $tracePath) {
    $closureSeconds = if ($ValidationProfile -eq "Full") { 20 } else { 8 }
    $directHSeconds = if ($ValidationProfile -eq "Full") { 180 } else { 120 }
    $assertArgs = @(
        (Join-Path $toolDir "_assert_fsm_regression.py"),
        $tracePath,
        "--closure-seconds", $closureSeconds,
        "--direct-h-seconds", $directHSeconds
    )
    if ($FaintnessStress) {
        $assertArgs += @("--female-orgasm-target", $FemaleOrgasmTarget)
    }
    & python @assertArgs
    $assertExit = $LASTEXITCODE
    python (Join-Path $toolDir "orbit_trace_report.py") $tracePath $reportPath
    $reportExit = $LASTEXITCODE
    Write-Host "Report: $reportPath"
    if ($reportExit -ne 0) {
        exit $reportExit
    }
    if ($assertExit -ne 0) {
        exit $assertExit
    }
} else {
    Write-Warning "No trace produced: $tracePath"
    exit 1
}
