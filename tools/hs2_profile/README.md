# HS2 BepInEx Profile 工具

管理 **D:\HS2** 的 BepInEx overlay（plugins + config），與 HS4 原始碼 repo 分離，方便出問題時回復。

## 兩種回復方式

| 方式 | 用途 |
|------|------|
| **git profile repo** | 追蹤「這套插件 + 設定」的版本，可 commit / revert |
| **cfg 快照** | 改設定前快速備份，一鍵還原（不必 commit） |

## 1. 初始化 profile git repo（一次性）

```powershell
cd D:\HS4
.\tools\hs2_profile\init_profile_repo.ps1 -Hs2Root D:\HS2
```

預設建立 `D:\HS2-profile`（與 HS2 同層），內含 `BepInEx/plugins`、`BepInEx/config` 副本並 `git init`。

自訂路徑：

```powershell
.\tools\hs2_profile\init_profile_repo.ps1 -Hs2Root D:\HS2 -ProfileRoot D:\path\to\my-hs2-profile
```

## 2. 部署插件後同步 + commit

```powershell
# 建置並部署（HS4 repo）
dotnet build -c Release HS2OrbitAndExciter\HS2OrbitAndExciter.csproj

# 同步到 profile repo
.\tools\hs2_profile\sync_from_hs2.ps1 -Hs2Root D:\HS2 -ProfileRoot D:\HS2-profile

cd D:\HS2-profile
git add -A
git commit -m "update: OrbitPoseDirector deploy"
```

回復：`git log` → `git checkout <commit> -- BepInEx/` 或 `git revert`。

## 3. 設定檔快照（不改 git 時）

```powershell
# 改設定 / 試驗前
python tools/hs2_profile/hs2_bepinex_backup.py backup --hs2-root D:\HS2

# 出問題還原
python tools/hs2_profile/hs2_bepinex_backup.py restore --hs2-root D:\HS2

# 列出備份
python tools/hs2_profile/hs2_bepinex_backup.py list --hs2-root D:\HS2
```

備份位置：`D:\HS2\BepInEx\config\_hs4_backups\<timestamp>\`

還原前會自動再備份一份 `<timestamp>_pre_restore`。

## .gitignore 說明

`tools/hs2_profile/.gitignore` 會複製到 profile repo。預設追蹤全部 plugins DLL 與 cfg；若 mods 插件太多，可改為白名單（見檔內註解）。

## 不納入版控

- 遊戲本體（`HoneySelect2_Data`、`abdata`）
- mods 整包、存檔
- BepInEx log / cache
