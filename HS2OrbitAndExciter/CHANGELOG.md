# HS2OrbitAndExciter 變更紀錄

## 2026-04-05

### 清理除錯／對照用程式碼

- 移除 `CursorSessionDebugLog`、`OrbitAgentDebugLog` 與所有寫入工作區 NDJSON 的路徑；`OrbitController`／`OrbitBehaviorHub` 內僅供 Cursor 對照與假設驗證的記錄一併刪除（含 `BuildCursorAssistGateJsonFields`、SNAP/GATE/CYC、checkpoint 取樣等）。量產外掛不再寫 `debug-*.log` 或 `orbit-compare`。

### 滾輪繞過：1v1 與 Animator 閘門修正

- **1v1**：新增 `Patches/OrbitBypassWheelPatches1v1.cs`，對 `Sonyu` / `Aibu` / `Houshi` 的 `Start*` / `AutoStart*` / `AfterTheInsideWaiting*` 等與 `MultiPlay_F2M1` 簽名不同的方法獨立 Prefix（避免與雙人 patch 混掛）。
- **閘門**：`ChaInfo.animBody` 為 **屬性**，`Traverse.Field("animBody")` 會永遠為 null，導致誤判「無動畫」而擋繞過；改經 `OrbitHelpers.TryGetFemaleAnimBody`（`animBody`，必要時 `objAnim` 上取 `Animator`），閘門與 `OrbitController` 內讀取 layer0 狀態的邏輯一併改用 `AnimatorStateInfo`。
- **建置**：`HS2OrbitAndExciter.csproj` 條件引用 `UnityEngine.AnimationModule.dll`（編譯期）。

### Orbit assist 架構重整

- 新增 `OrbitBehaviorHub`，集中處理環視自動協助的 suppress/grace/cooldown 與決策節流，避免條件散落在多檔案。
- `OrbitController` 的 `ApplyOrbitAutoAction`、`TryAutoAdvancePastCheckpoint`、UI click/hover 抑制通知，改由 Hub 提供統一 API。
- suppress 鏈新增 `inputForcus` 條件，避免 UI 輸入焦點期間仍被自動協助搶旗標。

### 自動協助節流與回退開關

- 新增設定 `AutoAssistMinIntervalSeconds`（預設 `1.0`）：同時套用於 assist flag push 與 checkpoint invoke 的最短間隔。
- `ProcBase.Proc` 的 AfterProc Postfix 改為預設停用（no-op 路徑）；僅在 `EnableAfterProcAssistPostfixFallback=true` 時啟用，作為相容回退方案。
- 補充相容提醒：本插件仍採 Harmony patch，與其他同類 H 流程插件可能存在相互覆蓋或順序競爭風險；若觀察到自動推進異常，優先檢查上述 fallback 與插件組合。

## 已知問題

- **環視開啟時，畫面右上角 H 場景選單（例如「愛撫」等動作列表）無法點選**：此現象在部署「移除環視滾輪縮放、bypass 動畫狀態閘門、Ctrl+Shift+P 建置識別」等變更**之前**即已存在，**並非**該次建置才引入。後續若要修復，需另查 `NoCtrlCondition`、滑鼠事件是否被相機／全螢幕層吃掉、或遊戲 UI 射線與 `inputForcus` 等路徑。

## 2026-03-25

### 相機與環視

- **執行順序**：`OrbitController` 使用 `[DefaultExecutionOrder(-100)]`，在 `CameraControl_Ver2.LateUpdate` 之前寫入 `CamDat.Rot`；環視時**不要**指派 `CameraAngle`（其 setter 未乘 `transBase`，會與 `CameraUpdate` 衝突）。
- **滾輪（2026-03-25 當時行為）**：H 場景相機 `ZoomCondition` 恒為 false，外掛曾在環視開啟時鏡像滾輪縮放改 `CameraDir.z`；**後已移除**（改由遊戲讀滾輪；見較新變更與 `OrbitBypassAnimatorGate`）。
- **距離**：`SetDistanceForFocus` 依身高與設定倍率（預設約 1.4）限制距離，避免過近。

### Q / W / E 焦點

- **`NoCtrlCondition`**：僅在滑鼠左／右／中鍵拖曳時視為玩家接管；**不再**用 `GetKey(Q/W/E)`，與 vanilla `CameraKeyCtrl` 的 `GetKeyDown` 語意一致。
- **`ApplyOrbitFocusHotkeys`**：在 `LateUpdate` 中於寫入環視 yaw 前呼叫 `GlobalMethod.CameraKeyCtrl`，並在偵測到 Q/W/E（含 Shift 第二女角）時呼叫 `SetDistanceForFocus`、同步 `_currentViewOption`；`inputForcus` 時略過（同 `HScene.ShortcutKey`）。

### H 場景邏輯時序

- **`OrbitHSceneLateAssist`**：`[DefaultExecutionOrder(32000)]`，在 H 場景 proc 之後執行 `ApplyOrbitAutoAction`、`DebugOrbitIdlePass`、`TryAutoAdvancePastCheckpoint`，避免與相機數學搶順序。

### 自動推進與滾輪繞過

- `isAutoActionChange` 以反射 **Field** 為主；checkpoint 在無 `selectAnimationListInfo` 時累積逾時並可呼叫 `GetAutoAnimation`。
- Harmony：`OrbitBypassWheelPatches` 擴充 Idle／D_Idle 等分支的滾輪 gate 繞過（含 Aibu 等），並以 `Time.unscaledTime` 處理延遲計時。

### 除錯

- NDJSON 寫入 `d:\HS4\debug-341efe.log`（與其他 `debug-*.log`，已列入 repo 根目錄 `.gitignore`）。驗證 QWE 時可搜尋 `focusHotkey`。

### 建置產物

- `bin/`、`obj/` 已自 Git 追蹤移除，請以 `dotnet build` 本地產出 DLL；`CopyToHS2` 目標預設為 `D:\hs2\BepInEx\plugins`（可依 `HS2BepInEx` MSBuild 屬性覆寫）。
