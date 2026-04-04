# HS4 還原點筆記 — HS2 雙外掛（環視 + 臉部骨骼除錯）與 PhotoToCard 調整

## 分支與標籤

- **分支**：`wip/all-20260318-2012`（與 origin 差距以 `git status` 為準）
- **標籤（還原點）**：`checkpoint/hs4-hs2-plugins-deploy`  
  - 含：PhotoToCard 條件編譯自動化、`Directory.Build.props`、`HS2_Plugins_Deploy.sln`、兩專案預設複製到 `BepInEx\plugins`、人物編輯「重載」問題之相關修正與除錯日誌（見下）。

## 行為摘要

### 一次建置並部署

```text
dotnet build d:\HS4\HS2_Plugins_Deploy.sln -c Release
```

- 產物複製到 **`$(HS2BepInEx)\plugins`**（預設見 `Directory.Build.props`：`D:\HS2\BepInEx`）。檔名格式見根目錄 **`Directory.Build.targets`**：`基名_<git短SHA>_<UTC日期>[_dirty].dll`（部署前會刪除同基名舊檔，避免 BepInEx 重複載入）。
- **`HS2OrbitAndExciter_…dll`**：環視／興奮劑相關（與 PhotoToCard 自動化無關）。`bin` 內仍會產生標準 **`HS2OrbitAndExciter.dll`** 與上述帶版記後綴的複本。
- **`HS2.PhotoToCard_…dll`**：**預設僅臉部骨骼除錯**（ScrollLock、樹狀選單、結構綠線、錨點子樹＋剪貼簿等）。

### PhotoToCard：自動化與預設 DLL

- 符號 **`PHOTOTOCARD_AUTOMATION`**（`-p:PhotoToCardAutomation=true`）才編入：
  - 延遲自動進 `CharaCustom`
  - `load_card_request.txt` 輪詢、`LoadCardAndScreenshot`、`game_ready.txt` 等
- **預設關閉**上述兩項，避免手動人物編輯時被載卡／重載打斷。
- **已修正**：若已身在 `CharaCustom`，`AutoEnterCharaCustom` 延遲結束後**不再** `LoadSceneAsync`（避免整場景重載、除錯狀態失效）。
- **已修正**：`EnsureBoneTree` 依 **`objHeadBone.transform` 的 InstanceID** 變化重建樹，避免 `ReloadAsync` 後舊 `Transform` 殘留。

### 複製開關與路徑

- `Directory.Build.props`：`HS2Managed`、`HS2BepInEx`、`CopyToHS2Plugins`（預設 `true`）。
- 不複製到遊戲：`dotnet build ... -p:CopyToHS2Plugins=false`。

### 除錯用寫檔（可選清理）

- `OnGUI` 路徑仍可能寫 **`d:\HS4\debug-a30eab.log`**（NDJSON，約每 0.5s Repaint）。問題已排除後若不需可再從 `HS2PhotoToCardPlugin.cs` 移除 `#region agent log` 區塊。

## 主要路徑

| 路徑 | 說明 |
|------|------|
| `Directory.Build.props` | 共用 HS2 路徑與 `CopyToHS2Plugins` |
| `HS2_Plugins_Deploy.sln` | 兩外掛同時建置 |
| `HS2OrbitAndExciter/HS2OrbitAndExciter.csproj` | 環視外掛；建置後複製 DLL |
| `BepInEx_HS2_PhotoToCard/HS2.PhotoToCard.Plugin.csproj` | 臉部骨骼除錯外掛 |
| `BepInEx_HS2_PhotoToCard/HS2PhotoToCardPlugin.cs` | 主程式（`#if PHOTOTOCARD_AUTOMATION` 包住自動化區塊） |

## 還原方式

```text
git fetch --tags
git show checkpoint/hs4-hs2-plugins-deploy
git switch -c recover/hs2-plugins checkpoint/hs4-hs2-plugins-deploy
```

## 與舊筆記

- 較早 PhotoToCard／MediaPipe 對照與標籤：`NOTES_20260328_hs4_photocard_checkpoint.md`、`checkpoint/hs4-photocard-20260328`。
- 環視 WIP 文字：`NOTES_20260328_wip_checkpoint.md`（若存在）。

---

## HS2OrbitAndExciter：H 場景「環視開著時換姿勢選單難點／點了被蓋掉」— 經驗總結（2026-04）

### 現象（使用者描述）

- 開 **Orbit（環視）** 後，H 場景裡多數 UI 正常，但 **換姿勢／動作列表** 常無法用滑鼠點選條目，或點了立刻被自動流程改寫。
- 關閉環視後有時仍壞：與 **`NoCtrlCondition` 還原不當** 有關。

### 根因（已驗證方向）

1. **`HSceneFlagCtrl.NoCtrlCondition`（經由相機控制）**  
   若每幀回傳「不可操作」，會讓 H UI 的輸入被鎖。環視期間改為 **恆為可互動**（`() => true`），並在 **關閉環視後不要還原成會回 false 的舊 delegate**，改為維持寬鬆條件（本版：`() => true`），避免「關了環視仍不能點」。

2. **`isAutoActionChange` + `initiative`（自動進動作）與 `ProcBase.Proc` 時序**  
   僅在 `LateUpdate` 設旗標會被遊戲 proc **在同一幀稍後蓋掉**。需在 **`Proc` 的 postfix** 再寫回，否則自動動作邏輯與手動點選競爭。

3. **與「手動點 UI」的競爭**  
   啟用 `OrbitAutoActionEnabled` 時，自動邏輯會在 **指標在 UI 上、剛開環視、或剛點過 UI** 時搶先改狀態。解法採 **集中判斷**（`ShouldSuppressAutoAction`）：  
   `EventSystem.IsPointerOverGameObject()`、左鍵按住、開環視後短 **grace**、最近 UI 點擊窗口、已有列表選擇時 **不強制 auto**，並對 **checkpoint 自動前進**、**cycle 換裝／換姿** 做同樣抑制。另加 **無選擇時延遲（warmup）** 再開 auto，減少一開環視就搶選單。

4. **與使用者觀察的對照**  
   關閉 `OrbitAutoActionEnabled` 並將 `OrbitCheckpointTimeoutSeconds` 設為 0 時問題消失，與「干擾來自插件驅動的自動動作／卡點推進」一致。

### 實作落點（檔案）

- `HS2OrbitAndExciter/OrbitController.cs`：`NoCtrlOrbit`、`OnOrbitToggled`、`ShouldSuppressAutoAction`、`MarkManualUiClick`、`ApplyOrbitAutoAction`、`TryAutoAdvancePastCheckpoint`、`OnOrbitCycleComplete`、`Update`（UI 點擊／懸停延長抑制）。
- `HS2OrbitAndExciter/Patches/OrbitAutoActionAfterProcPatches.cs`：`Proc` postfix 內尊重 `ShouldSuppressAutoAction`。
- 除錯階段曾用 NDJSON 寫 `debug-96606d.log`；**問題確認修復後已移除儀表化**，僅保留上述行為修正。

### 下次若再懷疑同類問題

- 先確認：**僅姿勢列表壞還是全部 UI**；**關環視後是否仍壞**（指向 `NoCtrlCondition`）。
- 再確認：**關閉 `OrbitAutoActionEnabled` 是否立刻好**（指向 auto-action／checkpoint／cycle）。
- 建置部署前 **關閉 HS2**，避免 `BepInEx\plugins\*.dll` 被鎖定導致複製失敗。
