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
| **A. 欣賞鎖** | 窺視姿不動 | `suppress=longAppreciation`、`peeping=true`／`actCtrl=3,6`、HUD「欣賞中…」 | **僅窺視**（ActionCtrl→Peeping）；需 L／真實滾輪／cycle／**N**。**不**再認硬編碼 id／自慰 |
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
| AfterIdle | `TimedEscape`／高潮後欣賞→選池；不因窺視 id 擋短收尾 |
| Idle（非窺視） | `AutoStartSex`（約 1s） |
| 窺視（ActionCtrl 3,6） | 純播出／欣賞鎖（等 L／滾輪／N）；Classify 優先 `Peeping` |
| 熱鍵 N | 強制開幹／選池（可跳出欣賞鎖） |

`TryForceStartSex` 是執行器，不是落地政策本身。已刪 `auto_after_kick`／`pose`／`rebind`／`unstick`。

### Latch／ClearSelection

- `ClearedPoseAlreadyApplied`：**不清** latch（Idle／AfterIdle 保留給 policy／Tick*Escape）。  
- 窺視落地：`PoseLandedPolicy`→`EnterPeeping`（取消閒置／高潮後排程；等 N／L）。  
- `PoseKickDone`／orphan stuck／非 wait 落地：清 latch。

### 欣賞鎖判定（2026-07-13）

```text
同一筆 AnimationListInfo：
  nameAnimation  → UI 按鈕字／FSM nowAnim 顯示（日文或譯名；勿比對字串）
  id             → 列表編號（動畫包可改寫同號內容；勿當「姿種類」）
  ActionCtrl     → ChangeModeCtrl 分流唯一依據

欣賞鎖 = IsPeepingPose = (Item1==3 && Item2==6) → lstProc Peeping
自慰   = Item1==3 && Item2∈{4,5} → Masturbation（動作線，不鎖欣賞）
```

**禁止**：再維護 `LongAppreciationPoseIds` 硬編碼表。

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

對 last `runId` assert：PoseQueued≥2s、Changing+sel==now 跨 SNAP、非窺視 land→start、窺視 longAppreciation（`peeping`／`actCtrl=3,6`）、AfterIdle≤3s。缺證據則 SKIP，有違規則 FAIL。

---

## 現況已做（勿重複發明）

- Escape latch（刪 1.5s window／renewal）  
- Pose kick + `TryResolveAppliedPoseChange`  
- 矛盾態 recovery（非逾時清 sel）  
- 窺視欣賞鎖＝ActionCtrl→Peeping（**已刪**硬編碼七姿 id）  
- HUD 倒數／欣賞文案（含 N）  
- **N** + `TryForceStartSex` 執行器  
- **`OrbitPoseLandedPolicy`**（窺視／閒置／高潮後）  
- latch／`ClearedPoseAlreadyApplied` 契約  
- 晚序 invariant + `tools/_assert_fsm_regression.py`（認 `peeping`／`actCtrl`）

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
| `OrbitPosePool.cs` | `IsPeepingPose`（ActionCtrl 3,6） |
| `OrbitHelpers.cs` | `IsLongAppreciationPose`→轉呼叫窺視判定；Idle／Loop |
| `OrbitAssistReasons.cs` | suppress／clear／landed reason 字串 |
| `OrbitStatusHud.cs` | 倒數／鎖文案 |
| `OrbitStateMachineLog.cs` | SNAP 含 `actCtrl`／`peeping` |
