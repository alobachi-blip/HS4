param(
    [string]$GameRoot = "D:\HS2",
    [switch]$ForceBuild,
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot "HS2DirectHLauncher\HS2DirectHLauncher.csproj"
$managed = Join-Path $GameRoot "HoneySelect2_Data\Managed"
$bepInEx = Join-Path $GameRoot "BepInEx"
$pluginDir = Join-Path $bepInEx "plugins"
$configDir = Join-Path $bepInEx "config"
$marker = Join-Path $configDir "HS2DirectHLauncher.run"
$gameExe = Join-Path $GameRoot "HoneySelect2.exe"

if (!(Test-Path -LiteralPath $gameExe)) {
    throw "HoneySelect2.exe not found: $gameExe"
}
if (!(Test-Path -LiteralPath (Join-Path $managed "Assembly-CSharp.dll"))) {
    throw "HS2 managed assemblies not found: $managed"
}

$sourceNewest = Get-ChildItem -LiteralPath (Split-Path -Parent $project) -File |
    Where-Object { $_.Extension -in ".cs", ".csproj" } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
$deployedNewest = Get-ChildItem -LiteralPath $pluginDir -File -Filter "HS2DirectHLauncher*.dll" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

$needsBuild = $ForceBuild -or $null -eq $deployedNewest -or $deployedNewest.LastWriteTimeUtc -lt $sourceNewest.LastWriteTimeUtc
if ($needsBuild) {
    Write-Host "Building and deploying Direct-H launcher (first run or source changed)..."
    dotnet build $project -c Release `
        -p:HS2Managed=$managed `
        -p:HS2BepInEx=$bepInEx `
        -p:CopyToHS2Plugins=true
    if ($LASTEXITCODE -ne 0) {
        throw "Direct-H launcher build failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Host "Direct-H plugin is current; skipping build."
}

if ($BuildOnly) {
    Write-Host "Build/deploy verification complete."
    exit 0
}

$running = Get-Process -Name "HoneySelect2" -ErrorAction SilentlyContinue
if ($null -ne $running) {
    throw "HoneySelect2 is already running. Close it before using the one-shot Direct-H launcher."
}

New-Item -ItemType Directory -Force -Path $configDir | Out-Null
[System.IO.File]::WriteAllText($marker, [DateTime]::UtcNow.ToString("O"), [System.Text.UTF8Encoding]::new($false))

try {
    $process = Start-Process -FilePath $gameExe -WorkingDirectory $GameRoot -PassThru
    Write-Host "HoneySelect2 started (PID $($process.Id)); Direct-H one-shot is armed."
}
catch {
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    throw
}
