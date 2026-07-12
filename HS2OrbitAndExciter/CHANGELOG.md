# HS2OrbitAndExciter 變更紀錄

## 2026-07-12

### 環視：轉速／Zoom 設定開出＋每圈 zoom 加明顯

- **轉速**：原本就有（`OrbitTimePer360`＝單向 360° 秒數）；選單改標「轉動速度」。
- **Zoom**：加回每圈拉近／拉遠，並開出開關＋近／遠倍率（預設 0.65／1.75）；可在 Ctrl+Shift+P 調。

### 環視：關搖晃、三軸輪替、關協助還原手控

- 關掉每圈 zoom／焦點俯仰搖晃；距離固定。
- 三種繞軸輪替（與上次不同）：軀幹（頭−骨盆）、世界鉛垂、身體側向；每段任意起始方位角。
- 關掉 Ctrl+Shift+O：`NoCtrlCondition=false`，還原原版滑鼠／鍵盤調視角。

### 穿牆：地圖 vanish 全補（地板可藏）

- 新增 `OrbitMapVanishAssist`：開環視協助後，把 `objMap` 下所有 Collider 寫入 `CameraControl_Ver2.lstMapVanish`（疊在原版 Excel 上）。
- 排除女／男角色；地板／傢俱可藏。機制仍是撞到 → `SetActive(false)`。
- **磁碟快取**：`BepInEx/config/HS2OrbitAndExciter/map_vanish/map_{mapID}.json`。每張地圖只完整掃描一次並落盤；之後開遊戲只讀快取。

### 熱鍵：YUIOP 整排＋還原原版 R

- **Y／U**：維持 PregnancyPlus 肚子±（本外掛不佔，只放大步進）。
- **I**：強制清腹（原 R／P）。
- **O**：停／恢復環視轉動（原 C／V 退役）。
- **P**：切換狀態面板（Ctrl+Shift+I 仍可用）；**R 還給原版相機 Reset**。

### §11 環視身體軸向（切片 9）

- 新增 `OrbitBodyAxis`：以頭−骨盆為軀幹軸＋面向建身體空間；相對角 |Δ|≥60°、非整數；依焦點小幅俯仰。
- 協助 ≠ 轉動：`OrbitBehaviorHub` 拆 `_orbitAssistActive`／`_orbitCameraSpinning`；停轉鍵見上（現為 **O**）。
- `OrbitController`：繞身體軸寫 `Rot`＋骨焦點；每圈換相對角與 zoom；空檔預算下一圈；姿預設相機機率≈0.05；貫徹 `ConfigVanish＝Shield`；停轉仍綁焦點。
- HUD：「停轉」與「換角中」分開；選單說明協助／轉動可分、軀幹軸用語。
- 未做：自製 Linecast、補沙發 vanish 清單（契約優先既有 Shield）。

### FSM 契約實作（切片 1～8＋設定選單）

- **選池** `OrbitPosePool`／`OrbitFsmFlow`：混池去重、高潮後→選池、閒置約 1 秒開幹（原版 IsStart）、L／N 依格分流。
- **輸入閘**：不擋高潮中；加確認對話框；WIdle／SIdle 改屬橋段。
- **舊債停用**：假滾輪、initiative／GetAutoAnimation、圈數換姿、PoseLandedPolicy 第二套出口。
- **感度**：協助＋橋段內自動加；下限約 0.1／秒。
- **高潮特效**：女高潮＋侍奉男射統一事件；愛撫／女女落地縮腹；脫力次數 6＋協助忽略弱體化停止。
- **Ctrl+Shift+P 選單**：用語說清楚、去掉自創縮詞；退役項改說明不顯示開關。

### §1 選池（切片 1）：混池＋本場去重＋L 接入

- 新增 `OrbitPosePool`：混池（動作線＋窺視）、優先未用／耗盡清空（B1）、空池放寬去重（D2）、換角不清已用（C1）。
- 窺視判定：`ActionCtrl.Item1==3 && Item2==6`（對齊原版 `ChangeModeCtrl`→`Peeping`）；不用 `LongAppreciationPoseIds`。
- `OrbitPoseDirector.TryQueuePoseChange` 改呼選池；L 日誌標「動作線／窺視」。
- 重用既有 `OrbitShufflePool`；換姿管線仍走 `PoseDirector`。

### 重整：狀態基 — PoseLandedPolicy＋latch 契約＋invariant

- **根因**：換姿清旗 ≠ 下一步；`auto_after_*` 散落且無視 A+B；`ClearSelection(ClearedPoseAlreadyApplied)` 先清 latch，Idle 保留死碼。
- **`OrbitPoseLandedPolicy`**：落地唯一決策 — AfterIdle→TimedEscape；Idle 非 A+B→AutoStartSex；Idle A+B→Appreciate（清 arrival latch）；N 仍強制開幹。刪 `auto_after_kick/pose/rebind/unstick`。
- **Latch**：`ClearedPoseAlreadyApplied` 不再清 latch；非 wait 落地才清；Appreciate 清 arrival latch 避免窺視姿立刻被 Idle escape 開幹。
- **晚序 invariant**：Sanitize→Resolve→Kick→Landed；殘留 `sel==now` 打 `invariant/fail_sel_eq_now`。
- HUD：`欣賞·等 L／滾輪／N`。回歸：`tools/_assert_fsm_regression.py`。詳見 `HANDOFF_fsm_stuck_root_causes.md`。

- **修復：`Changing` 黏旗鎖 L**：kick∥原版競態後 `nowChangeAnim=true` 且 `sel.id==now.id`（姿已套用）會長時間擋 L（log 見 ~120s Changing、88× hotkey fail）。每幀／L／kick finally 用 `TryResolveAppliedPoseChange` 立刻清 sel＋`nowChangeAnim`（無計時）。

### 功能：N 開始做愛＋換姿後自動進 Loop

- **N**：強制從 Idle／AfterIdle／Insert 進 `WLoop`／`D_WLoop`（解鎖黏旗、latch escape、設 auto）。
- **Case**：換姿落地 Idle 後由 **PoseLandedPolicy** 決定下一步（非 A+B→AutoStartSex；A+B→Appreciate）。`TryForceStartSex` 為執行器。
- HUD 圖例加 `N幹`。

### HUD：有時限的 lock 顯示倒數

- 協助列：`緩衝`／`UI`／`高潮後`／`脫離` 顯示剩餘秒數（`F1`）。
- 無時限鎖：`高潮中`、`欣賞·等L/滾輪/N`（A+B），避免誤以為卡死。
- 修正：`longAppreciation` 先前誤顯示成「自動·就緒」。

### 重整：FSM 去計時（擁有權契約）

- **Escape latch**：L／真實滾輪／cycle → latch；離開 Idle／AfterIdle、換姿完成、關環視才清。刪 1.5s 窗與 `TickMotionEscapeRenewal`。
- **換姿**：`PoseQueued && !NowChangeAnim` → **立刻** kick 一次（互斥）；`NowChangeAnim` 時 kick finally **不清** sel。
- **解卡**：矛盾態立即復原（orphan `NowChangeAnim`、phantom `Changing`）；刪 6s／8s／2s stale 計時清 sel。
- **保留體感延遲**：AfterIdle／非 A+B Idle 假滾輪 ≈2s、`orgasmQuiet`、UI suppress、orbit grace、assist／checkpoint interval。

### 行為：長動作可欣賞，按需才脫離（A+B）

- **只保護這 7 個姿勢**（短高潮 AfterIdle 不在內）：
  - 窺視：105 洋式トイレ覗き、106 和式トイレ覗き、107 シャワー覗き
  - 場所自慰：8 シャワー／9 洋式トイレ／15 和式トイレ／102 風呂 オナニー
- 上述姿勢：擋自動換姿／checkpoint，直到 **L**／**真實滾輪**／**迴轉換姿** latch。
- **高潮後短 AfterIdle**（`Orgasm_*_A` 等）：約 **2 秒**假滾輪／強制 `IsReStart` 自動脫離（**即使**當前仍是 A+B 姿勢 ID，也不擋這段短等待）。
- FSM：`longAppreciation`；`escape`/`request`（`L`|`wheel`|`cycle`）。

### 功能：內射肚子變大（選單，預設開）

- `CumflationEnabled`（預設 true）：`numInside` 增加時呼叫 Preg+ `HS2Inflation(false)` 升一級。
- 選單開關：「內射時肚子變大」；R 仍清腹。

### 修復：虛脫後卡 D_Orgasm_IN_A（Auto AfterIdle）

- **根因**：環視協助設 `initiative≠0` → 走 `AutoAfterTheInsideWaitingProc`，**不看滾輪**，只等 `HAutoCtrl.IsReStart()`；假滾輪對此無效 → 卡在高潮後等待，L／自動換姿全滅。
- **第一版疏漏**：`OrbitAutoAfterIdleRestartPatch` 寫了但**未** `PatchSafe` 註冊 → log 完全沒有 `afteridle` 事件。
- **修法**：註冊 Harmony；脫離改為按需 armed（見上）；Manual 強制 `nextPlay`＋wheel；Hub 再備援 `ChaControl.setPlay`。

### 重整：環視行為 FSM 契約（四層）

- **Director phase 可觀測**：`PoseQueued`≡合法 sel 且非 NowChangeAnim；`Changing`≡NowChangeAnim；`PosePending`／`Rebinding` 為插件態。
- **`CanAutoAdvance`** 取代糊成一團的 `poseTransition` suppress；**PosePending 不擋**自動協助，但仍 **凍結 Cycle 計數觸發**。
- **清 sel 唯一入口**：`OrbitBehaviorHub.ClearSelection`；Director 只 Notify。checkpoint `GetAutoAnimation` **當幀 sanitize** 虛脫非法姿（P0）。
- **External 跟蹤**：見合法 sel／NowChangeAnim 即進 Queued／Changing（不再只靠 nowAnimationInfo）。
- Cycle：Queued／Changing **排隊不搶**；`ShouldFreezeCycleCounters` 含 Pending。
- `CanAutoAdvance==false` **不再清** `initiative`／`isAutoActionChange`。
- 硬逾時可跳過 Rebinding（解卡優先；焦點歪一幀不算回歸）。
- 封閉 reason：`OrbitAssistReasons`；HUD／FSM／L fail 共用。AfterProc fallback 仍走同一 `TryPush`。

### 修復：HUD 上移＋虛脫換姿＋FSM log

- HUD 底邊上移約 176px（避開 Finish／跳出鈕列），高度上限略降，避免蓋住底部 UI。
- **虛脫**：`PickNextPose` 只抽 `nDownPtn!=0`；卡在非法選姿時立刻清 `selectAnimationListInfo`（原版 `ChangeAnimation` 會直接 abort）。
- 狀態機 NDJSON：`BepInEx/LogOutput/HS2OrbitAndExciter_fsm.ndjson`（每 0.5s SNAP＋suppress／director／虛脫／熱鍵／checkpoint 事件）。

### 修復：環視鏡頭換姿時停轉

- `PoseDirector` 換姿過渡不再暫停 yaw（只凍結迴轉計數／換姿觸發）；僅 G/H 重載才停轉。
- 過渡卡住硬逾時 6s（含 `NowChangeAnim` 黏住），避免協助永久 suppress。

### 高潮特效併入行為中心（防搶切換）

- 刺青／胸部／乳頭潮吹／語音巡禮改由 `OrbitBehaviorHub.NotifyFemaleOrgasm` 統一分派（`AddOrgasm` Postfix 只進 Hub）。
- 高潮期間：`nowOrgasm`＋約 2s quiet → **抑制**自動選段／checkpoint `GetAutoAnimation`（避免高潮動畫中硬換姿把切換器弄亂）；假滾輪僅在 `nowOrgasm` 擋，AfterIdle 仍可 bypass。
- 語音巡禮侍奉射精／內射 hit 同樣開 quiet；L 在 `nowOrgasm` 時不接受。

### 修復：L 換姿／自動推進／Idle 卡關

- **根因**：`OrbitPoseDirector`（8bac10a）把 `TryPushOrbitAutoActionAssist` 改成永遠不設 `isAutoActionChange`，checkpoint 也不再呼叫 `GetAutoAnimation`；過渡狀態若沒跑完會永久卡在 `poseTransition`，連帶 L、自動協助、體感上的滾輪推進都失效。
- **修復**：恢復自動選段旗標與 checkpoint `GetAutoAnimation`；Director 加卡住逾時／stale 清列表後重置；環視關閉時 L 不再留下未完成過渡；H 場景每幀做 stuck recovery。

## 2026-07-11

### 高潮語音巡禮（VoiceTour）

- H 內依 **女高潮**／**侍奉體外·口內射精**／**插入內射** 推進語音階段：青澀 Blank → 好意（低／高／熟練）→ 享樂 → 隷属 → 嫌悪 → 依存 → 壊れ；可 Loop。
- 只覆寫執行期 `FemaleState`／`CheckPhase`／`FemaleStateNum`，**不寫卡片**好感等；離 H 還原。
- 進度依角色鍵存 `BepInEx/config/HS2OrbitAndExciter.VoiceTour.json`，換人再回來不重來（可關 Persist 或「每次進 H 重來」）。
- 左下角 HUD（⌃⇧I）顯示當階／Num／擊數／觸發／角色鍵與繁中說明；⌃⇧P「語音巡禮」可調與重置進度。

### 高潮乳頭潮吹（urine）

- 女高潮乳頭噴改為複用女 **潮吹／噴尿（urine）**：`siruInfos[1]` + `UrineIDs`（失敗則 clone `obiFluidCtrlFemale` urine slot，再退回 urine 粒子 2–5）。
- **預設節奏**：自訂連噴（先大力／大量 → 漸弱），與改 urine 前手感相同；⌃⇧P「節奏」可改跟遊戲潮吹 `Play(-1)`。連噴滑桿常駐顯示。
- 文案：HUD「乳潮…」；選單「乳頭潮吹」。掛點／Offset／Rot／重建噴口不變。

### 高潮乳頭射精（舊，已改為潮吹）

- （歷史）曾複用男性射精 Obi；見上節。

### R 清腹＋高潮刺青

- **R**：環視／H 場景中由本插件強制呼叫 PregnancyPlus `HS2Inflation(true)`／`ResetInflation`（並清 `_currentInflationLevel`），修正僅依賴 Preg+ Live Shortcut 時 HS2 H 膨脹清不掉的問題。
- **T**：開啟高潮刺青並依序貼一張（連按可檢查掛點）；**Shift+T** 關閉。貼花用 st_paint，掛在身體掛點（大腿→臉），不進飾品欄。大小 `OrgasmTattooScaleMin/Max`，上限 `OrgasmTattooMaxCount`。H 換衣後自動重掛。
- **掛點**：略過膝蓋／肘／腕／手／肩等關節（關節樞紐在肢體內，貼花會浮出）；優先 `N_*` 飾品掛點，再退回大腿／軀幹骨。
- **刷新**：高潮當幀 `CreateBodyTexture` 常被 H 流程覆寫；改為立刻寫入後再於 EndOfFrame＋數幀重刷 paint 槽，避免只有第一次看得見。
- **穩定性**：先成功建立貼花再記入 stamp（避免 Count 脫勾）；換衣重掛改為一次 `CreateBodyTexture`。
- **部位對齊**：body paint 的 `layoutId` 依掛點名稱匹配（如 左太もも），不再隨機 UV；對不到則略過 paint，只留貼花／HUD。
- **顏色**：改抽製作模式色見本 `ColorPresets`（跳過 index 0–16 膚色），隨機自 17 起（灰／彩／深色／黑白）。
- **高潮胸部**：每次女高潮將 `BustSize`（胸サイズ）×(1+percent/100)，預設 +15%（`OrgasmBustGrowEnabled`／`OrgasmBustGrowPercent`）。

## 2026-04-05

### 環視：回程真正反向＋旋轉／迴轉雙計數

- **Yaw**：相位 1（回程）改為與相位 0 共用 `rotY = _startOrbitY + _orbitAccumulatedDegrees`，`acc` 遞減時鏡頭沿反方向掃回，不再出現「兩段都像同向繞」的錯覺。
- **計數**：新增「旋轉」（每完成單向 360° 一次，去程結束與回程結束各 +1）與「迴轉」（完整來回 +1）。**1 迴轉 = 2 旋轉**。
- **觸發**：`OrbitCountBeforeRandom` 語意改為每 **N 次旋轉** 亂數焦點；**水平角預設**僅在**迴轉完成**的觸發點套用（半圈觸發時只亂數焦點，避免 `acc=360` 時改 `_startOrbitY` 造成跳角）。換裝與同一 N 對齊；**N=0** 時關閉亂數，換裝改為**每迴轉一次**。`OrbitCountBeforePoseChange` 改為每 **M 次迴轉**換姿（搭配 `ChangePoseOnCycle`）。
- **預設**：`OrbitCountBeforeRandom` 預設由 `1` 改為 **`2`**，約略對應舊版「每完整來回亂數一次」的節奏；既有 cfg 仍保留已存數值。
- **程式結構**：週期副作用集中於新檔 `OrbitCycleCoordinator.cs`；`OrbitBehaviorHub` 仍只負責 assist／UI suppress。
- **設定與 HUD**：BepInEx 英文說明、`OrbitSettingsGUI`（Ctrl+Shift+P）繁中標籤、`OrbitHudSnapshot`／狀態面板文案改為旋轉／迴轉用語與新估算方式。

### 環視狀態 HUD（繁中）

- 環視開啟（Ctrl+Shift+O）後預設顯示左下角精簡狀態：相機仍在運轉、自動操作是否因 UI／滑鼠／啟動緩衝等暫停、準備倒數、本圈剩餘時間估算、下一個亂數焦點／換姿／換裝約略圈數與秒數。
- **Ctrl+Shift+I** 切換面板顯示；**Ctrl+Shift+P** 設定視窗可關閉整個 HUD 功能或暫時隱藏面板。
- 新增 `OrbitStatusHud`、`OrbitHudSnapshot`；`OrbitBehaviorHub` 於啟動環視時通知顯示面板，並提供 grace／UI 點擊抑制的剩餘秒數供文案使用。

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
