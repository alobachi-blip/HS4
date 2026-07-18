param(
    [string]$GameRoot = "D:\HS2",
    [switch]$ForceBuild,
    [switch]$BuildOnly,
    [switch]$FullPlugins
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot "HS2DirectHLauncher\HS2DirectHLauncher.csproj"
$leanManifest = Join-Path $repoRoot "HS2DirectHLauncher\lean_plugins.txt"
$leanPatcherProject = Join-Path $repoRoot "HS2DirectHLeanProfile\HS2DirectHLeanProfile.csproj"
$managed = Join-Path $GameRoot "HoneySelect2_Data\Managed"
$bepInEx = Join-Path $GameRoot "BepInEx"
$pluginDir = Join-Path $bepInEx "plugins"
$leanPluginDir = Join-Path $bepInEx "directh_plugins"
$patcherDir = Join-Path $bepInEx "patchers"
$leanPatcherDll = Join-Path $patcherDir "HS2DirectHLeanProfile.dll"
$configDir = Join-Path $bepInEx "config"
$marker = Join-Path $configDir "HS2DirectHLauncher.run"
$fullPluginsMarker = Join-Path $configDir "HS2DirectHLauncher.fullplugins"
$gameExe = Join-Path $GameRoot "HoneySelect2.exe"
$splashConfig = Join-Path $configDir "BepInEx.SplashScreen.cfg"

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

$leanPatcherSourceNewest = Get-ChildItem -LiteralPath (Split-Path -Parent $leanPatcherProject) -File |
    Where-Object { $_.Extension -in ".cs", ".csproj" } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
$deployedLeanPatcher = Get-Item -LiteralPath $leanPatcherDll -ErrorAction SilentlyContinue
$needsLeanPatcherBuild = $ForceBuild -or $null -eq $deployedLeanPatcher -or
    $deployedLeanPatcher.LastWriteTimeUtc -lt $leanPatcherSourceNewest.LastWriteTimeUtc
if ($needsLeanPatcherBuild) {
    Write-Host "Building and deploying the Direct-H lean-profile preloader..."
    $bepInExCore = Join-Path $bepInEx "core"
    dotnet build $leanPatcherProject -c Release "-p:BepInExCore=$bepInExCore"
    if ($LASTEXITCODE -ne 0) {
        throw "Direct-H lean-profile preloader build failed with exit code $LASTEXITCODE"
    }
    $builtLeanPatcher = Join-Path (Split-Path -Parent $leanPatcherProject) "bin\Release\net472\HS2DirectHLeanProfile.dll"
    Copy-Item -LiteralPath $builtLeanPatcher -Destination $leanPatcherDll -Force
} else {
    Write-Host "Direct-H lean-profile preloader is current; skipping build."
}

if (!$FullPlugins) {
    if (!(Test-Path -LiteralPath $leanManifest)) {
        throw "Lean plugin manifest not found: $leanManifest"
    }

    $expectedLeanPath = [System.IO.Path]::GetFullPath((Join-Path $bepInEx "directh_plugins"))
    $resolvedLeanPath = [System.IO.Path]::GetFullPath($leanPluginDir)
    if (![string]::Equals($resolvedLeanPath, $expectedLeanPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to rebuild unexpected lean plugin path: $resolvedLeanPath"
    }
    if (Test-Path -LiteralPath $resolvedLeanPath) {
        Remove-Item -LiteralPath $resolvedLeanPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedLeanPath -Force | Out-Null

    $pluginRootWithSeparator = [System.IO.Path]::GetFullPath($pluginDir).TrimEnd('\') + '\'
    $linked = 0
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in [System.IO.File]::ReadAllLines($leanManifest)) {
        $relative = $entry.Trim()
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative.StartsWith('#')) {
            continue
        }

        $matches = @(Get-ChildItem -Path (Join-Path $pluginDir $relative) -File -ErrorAction SilentlyContinue)
        if ($matches.Count -eq 0) {
            $missing.Add($relative)
            continue
        }
        foreach ($source in $matches) {
            $sourcePath = [System.IO.Path]::GetFullPath($source.FullName)
            if (!$sourcePath.StartsWith($pluginRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Lean-profile source escaped the plugin root: $sourcePath"
            }
            $relativeSource = $sourcePath.Substring($pluginRootWithSeparator.Length)
            $destination = Join-Path $resolvedLeanPath $relativeSource
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            New-Item -ItemType HardLink -Path $destination -Target $sourcePath | Out-Null
            $linked++
        }
    }
    if ($missing.Count -gt 0) {
        Write-Warning ("Lean profile entries not found: " + ($missing -join ', '))
    }
    Write-Host "Prepared Direct-H lean plugin profile: $linked files linked."
} else {
    Write-Host "Full plugin mode requested; Direct-H lean profile will not be activated."
}

if ($BuildOnly) {
    Write-Host "Build/deploy/profile verification complete."
    exit 0
}

$running = Get-Process -Name "HoneySelect2" -ErrorAction SilentlyContinue
if ($null -ne $running) {
    throw "HoneySelect2 is already running. Close it before using the one-shot Direct-H launcher."
}

New-Item -ItemType Directory -Force -Path $configDir | Out-Null

# The splash patcher can appear whenever the BepInEx console setting changes.
# Disable it explicitly for a launcher whose purpose is the shortest direct path.
if (Test-Path -LiteralPath $splashConfig) {
    $splashText = [System.IO.File]::ReadAllText($splashConfig)
    $updatedSplashText = [System.Text.RegularExpressions.Regex]::Replace(
        $splashText,
        '(?m)^Enabled\s*=\s*true\s*$',
        'Enabled = false')
    if ($updatedSplashText -ne $splashText) {
        [System.IO.File]::WriteAllText($splashConfig, $updatedSplashText, [System.Text.UTF8Encoding]::new($false))
        Write-Host "Disabled the BepInEx splash screen."
    }
}

if ($FullPlugins) {
    [System.IO.File]::WriteAllText($fullPluginsMarker, [DateTime]::UtcNow.ToString("O"), [System.Text.UTF8Encoding]::new($false))
} else {
    Remove-Item -LiteralPath $fullPluginsMarker -Force -ErrorAction SilentlyContinue
}
[System.IO.File]::WriteAllText($marker, [DateTime]::UtcNow.ToString("O"), [System.Text.UTF8Encoding]::new($false))

try {
    $process = Start-Process -FilePath $gameExe -WorkingDirectory $GameRoot -PassThru
    Write-Host "HoneySelect2 started (PID $($process.Id)); Direct-H one-shot is armed."
}
catch {
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $fullPluginsMarker -Force -ErrorAction SilentlyContinue
    throw
}
