# HANDOFF：為何一直「卡」＋當初設計漏了什麼

> Log：`D:\HS2\BepInEx\LogOutput\HS2OrbitAndExciter_fsm.ndjson`  
> 分析：`tools/_analyze_fsm_hang.py`、`_analyze_fsm_lock.py`、`_analyze_fsm_last.py`  
> 回歸 assert：`tools/_assert_fsm_regression.py`  
> 部署：關遊戲後 `dotnet build HS2OrbitAndExciter/HS2OrbitAndExciter.csproj -c Release`  
> **FSM 契約以本文件＋ CHANGELOG「狀態基：PoseLandedPolicy」為準**（`ORBIT_BEHAVIOR_AND_ARCHITECTURE.md` 的 yaw／頻率分類另案，勿當 FSM 真相來源）。

---

## 一句話

使用者體感的「卡」大半不是單一 bug，而是 **三層意圖疊在一起卻沒有明確「落地後要做什麼」契約**：  
(1) 原版 H 場景旗標／消費路徑不可靠、(2) A+B 欣賞鎖是「故意停」、(3) 換姿／escape 擁有權清旗時機與 Idle 落地衝突。

**2026-07-12 狀態基收斂後**：落地政策已抽出 `OrbitPoseLandedPolicy`；latch／`ClearedPoseAlreadyApplied` 契約已修；晚序 invariant 集中 Sanitize→Resolve→Kick→Landed。

---

## 「卡」的真實分類（先對號，再改碼）

| 類型 | 體感 | Log 特徵 | 本質 |
|------|------|----------|------|
| **A. 欣賞鎖** | Idle／窺視／自慰姿不動 | `suppress=longAppreciation`、HUD「欣賞·等 L／滾輪／N」、`landed/appreciate` | **設計如此**；需 L／真實滾輪／cycle／**N** |
| **B. 假卡（UI／高潮）** | 滾輪／L 沒反應 | `pointerOverUi`、`mouseHolding`、`nowOrgasm`、`orgasmQuiet` | 閘門擋協助；不是 FSM 死鎖 |
| **C. 換姿沒被原版吃** | 按了 L／選了姿但畫面不變 | `PoseQueued` 久、`nowChangeAnim=false`、`sel` 有值 | 寫了 `sel`，`HScene` **不**啟 `ChangeAnimation` → kick（晚序 invariant） |
| **D. 換姿黏旗** | 姿其實已換、L 全滅 | `Changing` 很久、`sel.id==now.id`、hotkey fail `changing` | `TryResolveAppliedPoseChange` → `landed/*` |
| **E. Idle 落地乾等** | 換姿完成停在 Idle／D_Idle | 非 A+B 應見 `landed/auto_start_sex`＋`startsex` | 由 **PoseLandedPolicy** 決定下一步 |
| **F. 虛脫 AfterIdle** | 高潮後永遠等 | `D_Orgasm_*_A`、`initiative≠0` | patch + `TickAfterIdleEscape`；落地為 `landed/timed_escape` |

不要把 A/B 當 bug 狂修。

---

## 三域擁有權（現況契約）

1. **PoseChangeOwner**：Queued→kick；`sel.id==now.id`→resolve；清 sel 唯一 `ClearSelection`。  
2. **WaitEscapeOwner**：L／滾輪／cycle／N arm latch；離開 wait／關環視／Appreciate 落地清 latch。  
3. **PoseLandedPolicy**（`OrbitPoseLandedPolicy.OnPoseLanded`）：

| 條件 | 決策 |
|------|------|
| AfterIdle | `TimedEscape`（已 latch → 立刻；否則 ≈2s）；不因 A+B id 擋 |
| Idle ∧ 非 A+B | `AutoStartSex` |
| Idle ∧ A+B | `Appreciate`（清 arrival latch；等 L／滾輪／N） |
| 熱鍵 N | 強制 `AutoStartSex`（無視欣賞鎖） |

`TryForceStartSex` 是執行器，不是落地政策本身。已刪 `auto_after_kick`／`pose`／`rebind`／`unstick`。

### Latch／ClearSelection

- `ClearedPoseAlreadyApplied`：**不清** latch（Idle／AfterIdle 保留給 policy／Tick*Escape）。  
- A+B Idle **Appreciate**：落地時 **清** arrival latch（避免 L 換到窺視姿後立刻被 `TickIdleEscape` 開幹）。  
- `PoseKickDone`／orphan stuck／非 wait 落地：清 latch。

### 每幀晚序 invariant（`TickPoseFlagRecovery`）

```text
Sanitize → Resolve(sel==now) → OnPoseLanded → Kick(Queued) → orphan NowChangeAnim → OnPoseLanded(Unstick)
```

違反 `sel==now` 殘留會打 `invariant/fail_sel_eq_now`。

---

## 設計漏了什麼（歷史根因；多數已補）

### 1. 沒有「Pose Landed → 下一步」契約（E）— **已補**

`OrbitPoseLandedPolicy` 單一入口。

### 2. Escape latch 與 `ClearSelection` 打架 — **已修**

`ClearedPoseAlreadyApplied` 不再在 `ClearSelection` 無條件清 latch。

### 3. 「原版會消費 sel」當公理（C）— **契約已寫死**

插件擁有換姿執行權：Queued 且非 inflight → kick。

### 4. `Changing` 只有旗標沒有進度（D）— **已升 invariant**

每幀 `sel.id == now.id` → resolve。

### 5. HUD 欣賞文案 — **已含 N**

`欣賞·等 L／滾輪／N`。

### 6. 歷史疏漏模式（避免再犯）

- Patch 未 `PatchSafe` → log 無事件。  
- 計時解卡掩蓋矛盾態。  
- 假滾輪對 `initiative≠0` AfterIdle 無效。

---

## 回歸

```text
python HS2OrbitAndExciter/tools/_assert_fsm_regression.py
```

對 last `runId` assert：PoseQueued≥2s、Changing+sel==now 跨 SNAP、非 A+B land→start、A+B appreciate、AfterIdle≤3s。缺證據則 SKIP，有違規則 FAIL。

---

## 現況已做（勿重複發明）

- Escape latch（刪 1.5s window／renewal）  
- Pose kick + `TryResolveAppliedPoseChange`  
- 矛盾態 recovery（非逾時清 sel）  
- A+B 七姿欣賞鎖；短 AfterIdle 仍 ≈2s  
- HUD 倒數／欣賞文案（含 N）  
- **N** + `TryForceStartSex` 執行器  
- **`OrbitPoseLandedPolicy`**（Appreciate／AutoStartSex／TimedEscape）  
- latch／`ClearedPoseAlreadyApplied` 契約  
- 晚序 invariant + `tools/_assert_fsm_regression.py`

---

## 關鍵檔案

| 檔 | 職責 |
|----|------|
| `OrbitPoseLandedPolicy.cs` | 落地下一步唯一決策 |
| `OrbitBehaviorHub.cs` | CanAutoAdvance、latch、kick、resolve、TryForceStartSex、ClearSelection、invariant |
| `OrbitPoseDirector.cs` | Director phase、rebind→OnPoseLanded |
| `OrbitManualDirector.cs` | L、sticky resolve→OnPoseLanded |
| `OrbitController.cs` | 熱鍵 N/L、LateAssist 順序 |
| `Patches/OrbitAutoAfterIdleRestartPatch.cs` | Idle／AfterIdle 強制脫離 |
| `OrbitHelpers.cs` | `IsLongAppreciationPose`、Idle／Loop 判定 |
| `OrbitAssistReasons.cs` | suppress／clear／landed reason 字串 |
| `OrbitStatusHud.cs` | 倒數／鎖文案 |
