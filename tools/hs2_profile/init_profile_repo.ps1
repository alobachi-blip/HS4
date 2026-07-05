# 初始化 HS2 BepInEx overlay 的 git profile repo（與 HS4 原始碼 repo 分離）
#
# 用法：
#   .\tools\hs2_profile\init_profile_repo.ps1 -Hs2Root D:\HS2
#   .\tools\hs2_profile\init_profile_repo.ps1 -Hs2Root D:\HS2 -ProfileRoot D:\HS2-profile
#
param(
    [Parameter(Mandatory = $true)]
    [string]$Hs2Root,
    [string]$ProfileRoot = "",
    [switch]$NoInitialCommit
)

$ErrorActionPreference = "Stop"
$Hs2Root = (Resolve-Path $Hs2Root).Path
if (-not $ProfileRoot) { $ProfileRoot = Join-Path (Split-Path $Hs2Root -Parent) "HS2-profile" }

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)

Write-Host "HS2 root:     $Hs2Root"
Write-Host "Profile repo: $ProfileRoot"

New-Item -ItemType Directory -Force -Path $ProfileRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $ProfileRoot "BepInEx\plugins") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $ProfileRoot "BepInEx\config") | Out-Null

Copy-Item -Force (Join-Path $ScriptDir ".gitignore") (Join-Path $ProfileRoot ".gitignore")

& (Join-Path $ScriptDir "sync_from_hs2.ps1") -Hs2Root $Hs2Root -ProfileRoot $ProfileRoot

Push-Location $ProfileRoot
try {
    if (-not (Test-Path ".git")) {
        git init
        Write-Host "git init in $ProfileRoot"
    }
    if (-not $NoInitialCommit) {
        git add .gitignore BepInEx
        $status = git status --porcelain
        if ($status) {
            git commit -m "chore: initial HS2 BepInEx profile snapshot from $Hs2Root"
            Write-Host "Initial commit created."
        } else {
            Write-Host "Nothing to commit (BepInEx overlay empty?)."
        }
    }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Edit plugins tracking in $ProfileRoot\.gitignore if needed"
Write-Host "  2. After deploy/build: .\tools\hs2_profile\sync_from_hs2.ps1 -Hs2Root $Hs2Root -ProfileRoot $ProfileRoot"
Write-Host "  3. cd $ProfileRoot ; git add -A ; git commit -m 'update overlay'"
Write-Host "  Config backup (no git): python tools/hs2_profile/hs2_bepinex_backup.py backup --hs2-root $Hs2Root"
