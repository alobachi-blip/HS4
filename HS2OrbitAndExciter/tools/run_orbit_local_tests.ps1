param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$pluginDir = Split-Path -Parent $toolDir
$repoRoot = Split-Path -Parent $pluginDir

Write-Host "== Python trace/report tests =="
python -m unittest discover -s (Join-Path $toolDir "tests") -v
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== PowerShell smoke syntax =="
$smokePath = Join-Path $toolDir "run_orbit_smoke.ps1"
$smokeText = Get-Content -Raw -Encoding UTF8 -LiteralPath $smokePath
$null = [scriptblock]::Create($smokeText)
Write-Host "PowerShell syntax OK"

Write-Host "== Plugin build =="
$projectPath = Join-Path $pluginDir "HS2OrbitAndExciter.csproj"
dotnet build $projectPath -c $Configuration -p:CopyToHS2Plugins=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Orbit local tests OK"
