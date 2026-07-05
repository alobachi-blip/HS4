# 從 HS2 遊戲目錄同步 BepInEx plugins + config 到 profile repo
#
# 用法：
#   .\tools\hs2_profile\sync_from_hs2.ps1 -Hs2Root D:\HS2 -ProfileRoot D:\HS2-profile

param(
    [Parameter(Mandatory = $true)]
    [string]$Hs2Root,
    [Parameter(Mandatory = $true)]
    [string]$ProfileRoot
)

$ErrorActionPreference = "Stop"
$Hs2Root = (Resolve-Path $Hs2Root).Path
if (-not (Test-Path $ProfileRoot)) {
    New-Item -ItemType Directory -Force -Path $ProfileRoot | Out-Null
}
$ProfileRoot = (Resolve-Path $ProfileRoot).Path

$srcPlugins = Join-Path $Hs2Root "BepInEx\plugins"
$srcConfig  = Join-Path $Hs2Root "BepInEx\config"
$dstPlugins = Join-Path $ProfileRoot "BepInEx\plugins"
$dstConfig  = Join-Path $ProfileRoot "BepInEx\config"

New-Item -ItemType Directory -Force -Path $dstPlugins | Out-Null
New-Item -ItemType Directory -Force -Path $dstConfig | Out-Null

if (Test-Path $srcPlugins) {
    Copy-Item -Path (Join-Path $srcPlugins "*.dll") -Destination $dstPlugins -Force -ErrorAction SilentlyContinue
    $nDll = (Get-ChildItem $dstPlugins -Filter "*.dll" -ErrorAction SilentlyContinue).Count
    Write-Host "Synced plugins: $nDll dll(s) -> $dstPlugins"
} else {
    Write-Warning "Plugins dir not found: $srcPlugins"
}

if (Test-Path $srcConfig) {
    Get-ChildItem $srcConfig -Filter "*.cfg" -File | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $dstConfig $_.Name) -Force
    }
    $nCfg = (Get-ChildItem $dstConfig -Filter "*.cfg" -ErrorAction SilentlyContinue).Count
    Write-Host "Synced config: $nCfg cfg(s) -> $dstConfig"
} else {
    Write-Warning "Config dir not found: $srcConfig"
}

Write-Host "Sync complete: $ProfileRoot"
