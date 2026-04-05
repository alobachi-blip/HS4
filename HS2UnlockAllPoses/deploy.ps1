# Deploy HS2UnlockAllPoses to HS2 BepInEx. Backs up existing plugin with timestamp before overwrite.
# Usage: .\deploy.ps1 [-GamePath "D:\hs2"] [-HS2Managed "D:\hs2\HS2_Data\Managed"]
param(
    [string]$GamePath = "D:\hs2",
    [string]$HS2Managed = ""
)
if (-not $HS2Managed) { $HS2Managed = Join-Path $GamePath "HS2_Data\Managed" }
$PluginsDir = Join-Path $GamePath "BepInEx\plugins"
$BackupDir = Join-Path $PluginsDir "Backup"
$PluginName = "HS2UnlockAllPoses.dll"

# Build
Write-Host "Building with HS2Managed=$HS2Managed"
dotnet build -p:HS2Managed=$HS2Managed
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed. Ensure HS2Managed points to your game's HS2_Data\Managed (e.g. -HS2Managed D:\hs2\HS2_Data\Managed)"
    exit 1
}

# csproj uses AppendTargetFrameworkToOutputPath=false → bin\Debug\, not bin\Debug\net472\
$OutDir = Join-Path $PSScriptRoot "bin\Debug"
$SourceDll = Join-Path $OutDir $PluginName
if (-not (Test-Path $SourceDll)) {
    Write-Error "Build output not found: $SourceDll"
    exit 1
}

# Target dir
if (-not (Test-Path $PluginsDir)) {
    Write-Error "Plugins folder not found: $PluginsDir. Install BepInEx or set -GamePath."
    exit 1
}

# Backup existing plugin (never overwrite backups)
$ExistingPlugin = Join-Path $PluginsDir $PluginName
if (Test-Path $ExistingPlugin) {
    if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir | Out-Null }
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupName = "$PluginName.backup_$timestamp"
    $backupPath = Join-Path $BackupDir $backupName
    Copy-Item -Path $ExistingPlugin -Destination $backupPath -Force
    Write-Host "Backed up existing plugin to $backupPath"
}

# Copy plugin only (bin\Debug may contain game refs from CopyLocalLockFileAssemblies — do not copy those to plugins)
Copy-Item -Path $SourceDll -Destination $PluginsDir -Force
Write-Host "Deployed $PluginName to $PluginsDir"
