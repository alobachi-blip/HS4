param(
    [string]$GameRoot = "D:\HS2",
    [int[]]$MapIds = @(3, 5, 6, 8, 18, 690101),
    [ValidateRange(30, 900)]
    [int]$DurationSeconds = 90,
    [string]$Label = "baseline",
    [string]$OutputRoot = "D:\HS4\Output\occlusion_survey",
    [string]$FemaleCardPath = ""
)

$ErrorActionPreference = "Stop"
$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$smokeScript = Join-Path $toolDir "run_orbit_smoke.ps1"
$analyzer = Join-Path $toolDir "analyze_occlusion_survey.py"
$configPath = Join-Path $GameRoot "BepInEx\config\com.hs2.orbitandexciter.cfg"
$tracePath = Join-Path $GameRoot "BepInEx\LogOutput\HS2OrbitAndExciter_fsm.ndjson"
$keyframeDir = Join-Path $GameRoot "BepInEx\LogOutput\OrbitSmokeKeyframes"
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$runDir = Join-Path $OutputRoot "${Label}_$stamp"
$backupPath = Join-Path $runDir "com.hs2.orbitandexciter.before.cfg"

if ([string]::IsNullOrWhiteSpace($FemaleCardPath)) {
    $femaleDir = Join-Path $GameRoot "UserData\chara\female"
    $FemaleCardPath = Get-ChildItem -LiteralPath $femaleDir -File -Filter "*.png" -Recurse |
        Where-Object FullName -NotMatch '[\\/]_autosave[\\/]' |
        Sort-Object Length, FullName |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($FemaleCardPath) -or !(Test-Path -LiteralPath $FemaleCardPath)) {
    throw "No usable female character card was found."
}

if (Get-Process HoneySelect2 -ErrorAction SilentlyContinue) {
    throw "HoneySelect2 is already running. Close the existing session before the automated survey."
}

New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$hadConfig = Test-Path -LiteralPath $configPath
if ($hadConfig) {
    Copy-Item -LiteralPath $configPath -Destination $backupPath -Force
}

$traceFiles = @()
try {
    foreach ($mapId in $MapIds) {
        Write-Host "Survey map=$mapId duration=${DurationSeconds}s"
        $arguments = @(
            "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", $smokeScript,
            "-GameRoot", $GameRoot,
            "-DurationSeconds", [string]$DurationSeconds,
            "-DirectH",
            "-DirectHMapId", [string]$mapId,
            "-DirectHDelaySeconds", "5",
            "-DirectHFemaleCardPath", $FemaleCardPath,
            "-OcclusionSurvey",
            "-SkipAssertions"
        )
        $process = Start-Process powershell.exe -ArgumentList $arguments -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -ne 0) {
            throw "Survey smoke run failed for map $mapId (exit $($process.ExitCode))."
        }
        if (!(Test-Path -LiteralPath $tracePath)) {
            throw "Survey trace missing for map $mapId."
        }
        if (!(Select-String -LiteralPath $tracePath -SimpleMatch '"msg":"direct_h_orbit_on"' -Quiet)) {
            throw "Map $mapId never reached an active automated orbit. Increase DurationSeconds or inspect the smoke trace."
        }
        if (!(Select-String -LiteralPath $tracePath -SimpleMatch '"id":"occlusion_survey"' -Quiet)) {
            throw "Map $mapId produced no occlusion survey samples."
        }

        $mapDir = Join-Path $runDir "map_$mapId"
        New-Item -ItemType Directory -Force -Path $mapDir | Out-Null
        $traceCopy = Join-Path $mapDir "trace.ndjson"
        Copy-Item -LiteralPath $tracePath -Destination $traceCopy -Force
        $traceFiles += $traceCopy
        if (Test-Path -LiteralPath $keyframeDir) {
            Get-ChildItem -LiteralPath $keyframeDir -File -Filter "*.png" -ErrorAction SilentlyContinue |
                Copy-Item -Destination $mapDir -Force
        }
    }
}
finally {
    if ($hadConfig -and (Test-Path -LiteralPath $backupPath)) {
        Copy-Item -LiteralPath $backupPath -Destination $configPath -Force
    }
}

$jsonPath = Join-Path $runDir "survey.json"
$markdownPath = Join-Path $runDir "survey.md"
& python $analyzer @traceFiles --json $jsonPath --markdown $markdownPath
if ($LASTEXITCODE -ne 0) {
    throw "Occlusion analyzer failed with exit $LASTEXITCODE."
}
Write-Host "Survey output: $runDir"
