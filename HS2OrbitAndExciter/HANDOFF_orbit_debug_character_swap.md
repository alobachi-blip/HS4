# 交接：環視「換人物後像停住」與對照日誌

## 症狀（使用者描述）

- 開環視（Orbit）後，**更換人物**（或相關 H 流程）之後，**畫面／環視像停下來**。
- 需區分：是 **遊戲暫停（timeScale）**、**場景／相機參考遺失**，還是 **外掛邏輯**。

## 相關 Git 參考（僅脈絡）

| 點 | 分支／SHA | 說明 |
|----|-----------|------|
| 基線邏輯 | `a59f143` | 無 Hub 大改前 |
| 基線 + 僅 NDJSON | `debug/a59f143-log-only` → `a882e16` | 與 a59 同邏輯，只加 `OrbitAgentDebugLog` |
| Hub 重構 + NDJSON | `debug/orbit-compare-logging` → `70e92d0` | 目前主要開發／部署分支 |

## 兩套日誌（不要搞混）

1. **`d:\HS4\orbit-compare\<版本化 DLL 主檔名>.ndjson`**  
   - `OrbitAgentDebugLog`：`sessionTag` 在 a59 分支為 `a59f143_log`，70e 分支為 `b93d2d`。  
   - 事件：`boot`、`G0` gameplay_snapshot（約 0.5s）、`G1` 相位、`G2` feel 滿、`G3` 週期、`H1`–`H5` 等。

2. **`d:\HS4\debug-b93d2d.log`**（Cursor 除錯工作階段）  
   - `CursorSessionDebugLog.cs` 追加 NDJSON，**固定** `sessionId`: `b93d2d`。  
   - 寫檔路徑硬編碼：`d:\HS4\debug-b93d2d.log`（與遊戲安裝目錄無關）。

## 已觀察到的現象（orbit-compare 歷史樣本）

- 同一 `.ndjson` 內曾出現 **兩個不同 `runId`**，且 **`unscaledTime` 從 ~162 落到 ~99** → **非同一 Unity 行程的連續時間**（重開遊戲或整段重載），不是單純「同一局內連續秒數」。
- **尾段凍結樣本**：連續多筆 `G0` 的 **`orbitAccumDeg` / `rotY` 完全不變**，但 **`unscaledTime` 仍約每 0.5s 增加**。  
  - 環視角速度在 `OrbitController.LateUpdate` 使用 **`Time.deltaTime`** 積分。  
  - **強烈相容於 `Time.timeScale == 0` 或 `deltaTime == 0`（暫停）**，與「外掛沒跑 LateUpdate」不符（因仍有 G0）。

## 為對照「正常 vs 換人物」已加的程式（Cursor 埋點）

**檔案**

- `HS2OrbitAndExciter/CursorSessionDebugLog.cs`：`Append(...)` → `debug-b93d2d.log`
- `HS2OrbitAndExciter/OrbitController.cs`：`LateUpdate` 內  
  - **`SNAP`**（約 1Hz）：`timeScale`、`deltaTime`、`unscaledDeltaTime`、`female0InstId`、orbit 狀態、anim hash、`inActionLoop`、`inputForcus`、`deferFocusHotkeys`、`waitingPrep`  
  - **`CHAR`**：`female0_instance_changed` / `female0_missing`（`GetInstanceID()`）  
  - **`REF`**：`hscene_null_while_assist`、`camera_not_ver2_or_null`（節流）  
  - **`TS`**：`deltaTime_near_zero`（節流）  
- 開環視時重置部分 cursor 追蹤欄位（`OnOrbitToggled(true)`）；關環視／非 assist 時清 `female` 追蹤。

**尚未用新埋點完成的結論**

- 需在 **`debug-b93d2d.log`** 用 **`SNAP` + `TS` + `CHAR`** 對齊「換人物」時間點後再下結論。  
- **勿在未讀新 log 前** 以推測改邏輯；若證實為 `deltaTime==0`，再討論是否改為 `unscaledDeltaTime` 驅動環視（產品決策 + 回歸）。

## 給下一個 Agent 的建議流程

1. 讀 `debug-b93d2d.log`：比對 **換人前後** 的 `SNAP`；看 **`TS`** 是否與 `orbitAccumDeg` 凍結同時出現；看 **`CHAR`** 是否落在同一時段。  
2. 若 log 缺失：請使用者 **刪 `debug-b93d2d.log` 後重跑**（或專案內用 `delete_file` 只刪此檔），再 **建置部署** 目前分支 DLL。  
3. 比對時可並行看 `orbit-compare\*.ndjson` 的 `G0`（較密）與 `debug-b93d2d.log` 的 `SNAP`（較適合對齊 timeScale）。  
4. 修復前應有 **log 引證**；修復後保留埋點至使用者確認再收斂。

## 使用者慣用語言

- 說明與回覆偏好 **中文**。

---
*本文件僅供交接脈絡；規格以 repo 內實際程式為準。*
