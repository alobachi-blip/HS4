# Project status: H-loop 修正與分鏡圖方向

> 寫給下一個 session。請先讀本檔，再讀 `h_loop_flow_by_family.md`。
> 語言：繁體中文。
> 日期：2026-07-15。
> Repo：`D:\HS4_h_loop_clean_v2`，外掛：`HS2OrbitAndExciter`。

---

## 1. 一句話狀態

目前 **FSM v1 已完成，H-loop session v2 的 Slice 0～5 已完成首輪落地並通過 DirectH smoke**。

已完成的是「選池／落地／閒置開幹／高潮後回選池／窺視 N/L／環視身體軸」這一層，以及 `h_loop_flow_by_family.md` 所描述的「單次選池落地後，依原版 Proc 族群代操 W/S、Finish、Pull/Drop、Spnking」首輪實作。

Slice 5 的結論是：舊 OLoop／Spnking 直跳高潮 recovery 已不在 C# 現碼；analyzer 仍把 legacy recovery trace 視為 error，並新增靜態測試防止這些 message 回到 C#。目前仍存在的 `setPlay` 只限 start/Idle/AfterIdle escape，屬於脫離等待或手動 N 熱鍵起步，不是常規高潮出口。

2026-07-16 驗證：

- local tests：`tools/run_orbit_local_tests.ps1` 通過，17 個 Python regression 通過，Debug build 0 warnings / 0 errors。
- Release build：`dotnet build .\HS2OrbitAndExciter\HS2OrbitAndExciter.csproj -c Release` 通過，0 warnings / 0 errors，並部署到 `D:\HS2\BepInEx\plugins`。
- DirectH smoke：`tools/run_orbit_smoke.ps1 -DurationSeconds 150 -ValidationProfile Fast -DirectH` 通過，12 warnings / 0 errors。
- smoke trace 觀察：`finish/set_click` 1 次且 `finish/consumed`；C 族 `maleInside` 消費到 `OrgasmM_IN`；`ina/pull_click` 走 auto pull，後續 trace 看到 `Pull/Drop` framing 並收斂到 `OrgasmM_OUT_A`；W/S script 有 4 次 step。

---

## 2. 文件真相層級

請用以下順序理解現況：

1. `docs/h_loop_flow_by_family.md`
   新的 H-loop session 規格。重點是「落地到 AfterIdle」這一段，不含選池。

2. `HANDOFF_fsm_stuck_root_causes.md`
   說明目前卡住分類、`PoseLandedPolicy`、latch、invariant。仍是外層 FSM 真相。

3. `HANDOFF_fsm_contract_review.md`
   說明 FSM v1 切片 1～9 已完成；它是外層 FSM 依據，不涵蓋 2026-07-16 這輪 H-loop Slice 0～4 實作。

4. `CHANGELOG.md`
   只代表已改過什麼，不代表新規格都已落地。

不要把舊 `HANDOFF_fsm_state_redefine.md`、`HANDOFF_fsm_autoplay_plan.md` 當最新真相。

另請注意：昨晚／今天留下的大量 `tools/_*.txt`、probe patch、臨時 MD 是 **事故與補救紀錄**。它們適合用來追卡點、對照 `NowChangeAnim`／`selectAnimationListInfo`／`OLoop` 問題，不應直接視為最終設計。正式 commit 時間線最新主要停在 2026-07-13；2026-07-15 這批多為未提交診斷資料。

---

## 3. 已完成

外層 FSM：

- `OrbitPosePool`：混池、去重、窺視判定改用 `ActionCtrl.Item1==3 && Item2==6`。
- `OrbitPoseLandedPolicy`：落地後唯一判斷入口。
- `OrbitFsmFlow`：Idle 約 1 秒開幹、AfterIdle 約 5 秒欣賞後選池、L/N 依格分流。
- `OrbitPoseDirector` / `OrbitManualDirector`：換姿 kick、resolve、黏旗清理。
- 晚序 invariant：Sanitize → Resolve → OnPoseLanded → Kick → orphan NowChangeAnim → OnPoseLanded。
- 窺視欣賞鎖：不再用硬編碼姿勢 id；用 `ActionCtrl`。

環視與正交：

- `OrbitBodyAxis`：以女角頭-骨盆軸與面向建立身體空間。
- O 鍵只停/恢復相機轉動；協助與 FEEL 照常。
- map vanish 補強；優先原版 Shield，不做自製 Linecast。
- Y/U/I/O/P 熱鍵整理。

目前可保留的結論：

- 協助 active 不等於相機正在轉。
- 環視是畫面/分鏡輔助，不應負責換段。
- 正交事件不可寫下一姿、不可開幹、不可成為第二套 FSM。

---

## 4. 新發現：原先 H scene 假設不夠準

`h_loop_flow_by_family.md` 指出原先幾個假設會造成反覆卡住：

1. 不能把所有 H mode 都當成同一種 `WLoop/SLoop/OLoop → Orgasm`。

2. 不能用 `setPlay("Orgasm...")` 當常規出口。
   原版 Proc 會在 `LoopProc` / `OLoopProc` 消費 `ctrlFlag.click`，並同步處理 `nowOrgasm`、gauge、`numInside`、`numOutSide`、voice、Obi、後續橋段等副作用。外掛直跳會漏副作用。

3. 族 B / C 的 Finish 不只是「感度滿」。
   Houshi / Sonyu 需要 `IsFinishVisible(slot)` + `ctrlFlag.click`，由原版 Proc 決定合法出口。

4. Sonyu 的男側高潮必須分軌。
   `FinishInSide`、`FinishOutSide`、`FinishSame` 不是同一件事；`Same` 不能抵扣男單獨 In/Out 的覆蓋。

5. `Orgasm_IN_A` 不應續幹。
   全代操規格是固定走下滾輪 Pull → Drop，不代 `wheel_up → WLoop`。

6. Spnking 沒有一般 W/S/O loop。
   它是 WIdle/SIdle 與 Action 節奏，不能拿 OLoop 補洞套上去。

7. 很多原版推進看的是 `normalizedTime >= N`，不是固定秒數。

---

## 5. 現碼與新規格落差

| 能力 | 現況 | 新規格 |
|------|------|--------|
| Idle 開幹 | `OrbitFsmFlow.ShouldForceVanillaIsStart` | 維持 |
| AfterIdle 選池 | `OrbitFsmFlow` 5 秒後選池 | 維持 |
| FEEL | `AccumulateFeelWhenOrbit` 同時加 `feel_f/feel_m`，W/S cap 0.74 | 維持灌條，但改為配合 session 劇本 |
| W/S 劇本 | 已補 `OrbitSessionDirector` 弱強節奏 | 跨 session 交替 W/S 順序 |
| W/S → O | 舊 `TickWsToOLoopRecovery` 未在 C# 現碼找到 | 優先讓 Proc 自然消費 |
| OLoop → 高潮 | 已補 `OrbitFinishDirector`；舊 `TickOLoopToOrgasmRecovery` 未在 C# 現碼找到 | 常規走 `IsFinishVisible + ctrlFlag.click` |
| Finish 選擇 | 已補 `OrbitFinishPathLedger` | 歷史比例最低，同分偏較長橋段 |
| Sonyu 男側 In/Out/Same | 已補 C 族 path gating：`canInside`、Same 只限 OLoop、69 down 外射限制 | 分軌帳本，In/Out/Same 各自合法出口 |
| IN_A Pull/Drop | 已補 `OrbitSessionDirector` + `HAutoCtrl.IsPull` override | 固定下滾輪/auto pull → Drop，永不續幹 |
| Spnking | 已補 `OrbitSpankingWheelPatch` 假滾輪節奏 | 讓原版 ActionProc 跑 |
| BetterHScenes offset/IK | 已補 `OrbitBhsCompat` trace | 採用 BHS solver/offset 品質層；AutoFinish 與 Orbit FinishDirector 不併存 |
| 全姿態開放 | 已補 `OrbitPoseUnlockPolicy` / `OrbitPoseUnlockPatches` | 安全放寬 state/pain/faintness，保留人數/事件/地點/AppendEV |

2026-07-16 已新增：

- `OrbitFinishDirector`
- `OrbitFinishPathLedger`
- `OrbitSessionDirector`
- `OrbitPoseUnlockPolicy`
- `OrbitBhsCompat`
- `Patches/OrbitSpankingWheelPatch`
- `Patches/OrbitAutoPullPatch`

明確仍存在：

- `TryForceFemaleAnim(... setPlay ...)`：目前只服務 `startsex`、`idle`、`afteridle` escape，不是常規 Finish/高潮出口。
- 舊全姿態開放來源：`D:\HS4\.claude\worktrees\flamboyant-bhaskara\HS2UnlockAllPoses`
- BHS source clone：`D:\HS4\third_party\BetterHScenes`

---

## 6. 下一步修復順序

Slice 0～5 已首輪落地；下面保留修復順序作為設計索引與後續驗證清單。

### Slice 0：恢復兩個前置品質層

這一片不改 H-loop 出口，但會影響可選姿勢與姿勢品質。

1. **全姿態開放回補**
   參考舊 `HS2UnlockAllPoses/Patches/HSceneSpritePatches.cs`。恢復 `CheckMotionLimit`、`CheckMotionLimitRecover`、`CheckAutoMotionLimit` postfix，但只放寬 state/achievement/pain/faintness；保留人數、`EventNo==19`、`CheckEventLimit`、`CheckPlace`、`CheckAppendEV`。

2. **BetterHScenes solver/offset 相容**
   BHS AutoFinish 不採用；BHS saved offsets、Animation Fixer、broken Animation Tables、kiss correction 採用。短期偵測已安裝 BHS 與 config，trace 記錄是否啟用；中期才評估是否移植最小 fixer。

3. **trace 欄位補充**
   增加 `posePool.total/afterUnlock/afterFaintness`、`bhsAutoFinishEnabled`、`bhsOffsetApplied`、`bhsSolverEnabled`。

### Slice 1：建立只觀測不改動的 session trace

目的：先證明每一族實際走到哪，不再靠猜。

每 0.5 秒或狀態變化時記錄：

- `mode` / `modeCtrl`
- `ActionCtrl`
- `nowAnimationInfo.id` / `nameAnimation`
- 第一女 layer0 state
- normalizedTime
- `feel_f` / `feel_m`
- `speed`
- `ctrlFlag.click`
- `nowOrgasm`
- `isFaintness`
- `selectAnimationListInfo`
- 是否 `IsFinishVisible(slot)`（如果能安全取得）

輸出延續 `HS2OrbitAndExciter_fsm.ndjson` 或新增 `HS2OrbitAndExciter_session.ndjson`。

### Slice 2：新增 `OrbitFinishPathLedger`

只做策略，不碰 Proc：

- B 族：`drink` / `vomit` / `outSide`
- C 族：`maleInside` / `maleOutside` / `same` / `femaleOnly`
- 使用 `count[path] / total`，挑最低比例。
- 同分優先較長橋段。
- `same` 不抵扣 In/Out。

### Slice 3：新增 `OrbitFinishDirector`

門控：

- Ctrl+Shift+O 協助開
- FSM cell 是 ActionBridge
- 非 nowChangeAnim
- 不是 Peeping

執行：

- 讀當前族群與 `modeCtrl`
- 查 `IsFinishVisible(slot)`
- 設 `ctrlFlag.click = ClickKind`
- 不 `setPlay`
- 等原版 Proc 消費；看到 `Orgasm_*` 或 AfterIdle 後才 ledger count++

此 slice 完成前，不要移除舊 `TickOLoopToOrgasmRecovery`。

### Slice 4：新增 `OrbitSessionDirector`

負責單次落地到 AfterIdle 的劇本：

- W/S 弱強順序，每次 session 只跑一種劇本，跨 session 交替。
- D 族用 W → M → S 等價。
- E 族用 WAction/SAction 節奏。
- `Orgasm_IN_A` 固定下滾輪 Pull → Drop。
- 等特殊橋段播完再讓 `OrbitFsmFlow` 接 AfterIdle。

### Slice 5：降級舊 setPlay recovery

2026-07-16 首輪完成：

- C# 現碼已找不到 `force_ws_to_oloop`、`force_oloop_to_orgasm`、`force_insert_to_wloop`、`force_spank_to_orgasm`。
- `tools/_assert_fsm_regression.py` 仍把上述 legacy recovery message 視為 `legacy_setplay_recovery` error。
- `tools/tests/test_orbit_trace_tools.py` 新增靜態測試，掃 C# source 防止 legacy recovery message 回歸。
- 仍保留 start/Idle/AfterIdle escape 的 `setPlay(WLoop/D_WLoop)`，這是等待脫離保險，不是高潮出口。

後續若要再收斂：

- `TickOLoopToOrgasmRecovery` 從常規出口降成診斷/最後保險。
- 族 B/C 不再用 setPlay 直跳高潮。
- 族 A 的 OLoop 卡住優先用 feel / `FinishBefore`，不是 `setPlay("Orgasm")`。
- Spnking 不再直推 `Orgasm` 當主路。

---

## 7. 分鏡圖與本地模型生成影片方向

使用者希望把 H scene 當「分鏡圖」來源，再交給本地模型生成影片。這應該是 **新的正交模組**，不要跟 FSM 修復混在同一層。

建議名稱：

- `OrbitStoryboardRecorder`
- `OrbitShotTimeline`
- `HS2OrbitAndExciter_storyboard.ndjson`

第一版只做被動記錄，不控制流程。

每個 shot 建議記錄：

- shot id / session id / timestamp
- `mode` / `modeCtrl` / `ActionCtrl`
- pose id / pose name
- FSM cell：Idle / ActionBridge / AfterIdle / Peeping
- animator state / normalizedTime
- camera：`TargetPos` / `Rot` / `Dir.z` / `Fov`
- focus：主女/第二女、頭/胸/骨盆、骨點是否成功
- body axis：軀幹軸、面向、相對角
- visible state：衣服階段、刺青、Preg+ 腹部級別等可選
- event marker：落地、開幹、W/S 切換、Finish click、高潮、Pull/Drop、AfterIdle、選池
- screenshot/keyframe path（若之後要自動截圖）

本地影片模型需要的不是「H 流程控制」，而是穩定、可重放的 shot timeline：

```text
H scene 原版/Orbit 流程
  -> passive recorder
  -> storyboard ndjson + keyframes
  -> prompt/caption builder
  -> local video model
```

重要邊界：

- Recorder 不可推進 FSM。
- Recorder 不可改 `click`、`speed`、`feel`、姿勢、相機。
- Recorder 可以讀相機與狀態，但第一版不要自動生成影片。
- 先把 H-loop v2 修穩，再把穩定狀態輸出成分鏡資料。

---

## 8. 自動驗證方案

使用者不應當人肉測試機。後續流程要改成：

```text
CLI build
  -> 清舊 log
  -> 短時啟動 HS2 / 或接上正在跑的 HS2
  -> 暫時開 Diagnostics.EnableStateMachineTrace
  -> 收 NDJSON
  -> 跑 regression assert
  -> 產 HTML report
  -> Codex 先讀報告
  -> 最後才請使用者做少量實機確認
```

已新增工具：

- `tools/run_orbit_smoke.ps1`：清舊 log、暫時打開 `EnableStateMachineTrace`、啟動/等待 HS2、跑回歸檢查、產報告，結束後恢復 trace=false。
- `tools/orbit_trace_report.py`：把 `HS2OrbitAndExciter_fsm.ndjson` 轉成 `orbit_smoke_report.html`，方便 Codex/使用者直接看事件統計、疑似問題、最後 200 行。

目前限制：

- HS2 H 場景不是純 headless，CLI 只能負責啟動、收 log、產報告；若要完全自動進 H 場景，需要另做 in-game smoke driver。
- HTML report 是第一階段「我看得到資料」；若要我看畫面，可在 StoryboardRecorder 階段追加 keyframe screenshot，HTML 直接嵌圖。

---

## 9. 給下一個 session 的開場白

請讀：

1. `HS2OrbitAndExciter/docs/PROJECT_STATUS_20260715_h_loop_and_storyboard.md`
2. `HS2OrbitAndExciter/docs/h_loop_flow_by_family.md`
3. `HS2OrbitAndExciter/HANDOFF_fsm_stuck_root_causes.md`

目前目標不是重新設計外層 FSM。外層 FSM v1 已可用；真正要修的是單次 H-loop session：用原版 Proc 的 `IsFinishVisible + ctrlFlag.click` 取代 `setPlay` 直跳高潮，並補上 W/S 劇本、Finish Ledger、IN_A Pull、Spnking 節奏。

請先做 Slice 0/1，再做 `OrbitFinishPathLedger` / `OrbitFinishDirector`。不要先刪現有 recovery，避免又讓流程直接卡死。

分鏡圖/本地影片生成是下一個正交模組，先記錄 shot timeline，不要讓 recorder 介入 H 流程。
