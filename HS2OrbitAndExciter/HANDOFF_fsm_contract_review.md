# 交接：FSM 契約定稿＋審查收斂（下一任讀此檔）

> 上一任對話 context 已滿。**先讀本檔＋主計畫，再問使用者或實作。**  
> 未說「實作／execute」前不要大改程式。

---

## 你是誰、要做什麼

Repo：`D:\HS4`，外掛 **HS2OrbitAndExciter**。  
把 H 流程收成：**狀態驅動、一格一出口、換段只認選池**；正交掛事件。

**主計畫（唯一真相）**  
`C:\Users\jason\.cursor\plans\fsm_狀態圖確認_bd3a5656.plan.md`

舊檔可能過時（勿當真相）：  
`HANDOFF_fsm_state_redefine.md`、`HANDOFF_fsm_autoplay_plan.md`、舊窺視 plan。

語言：對使用者 **繁體中文**；不自創縮詞。  
過程節奏可調；**改 FSM 切換／出口必先警告**，預設禁止。

---

## 產品目標

開 Ctrl+Shift+O 後：幾乎不介入也能 開幹→高潮→換段→窺視；可手動；不卡死。

## 狀態圖（拓樸鎖死）

```text
選池
  ├─ 動作線 → 閒置 → 動作橋段 → 高潮後閒置 → 選池
  └─ 窺視 → In…Out_Loop → 主出口 N／L → 選池
L＝手動選池；G＝換角；環視／衣／刺青等＝圖外正交
```

---

## 進度（截至交接時）

| 區塊 | 狀態 |
|------|------|
| 0～10 流程＋舊債 | **已定** |
| 11～23 正交（含環視） | **已定** |
| §22 WeakStop | **已定＝甲**（協助開忽略 WeakStop；次數 6） |
| 獨立審查報告 | 已收；P1 已寫回主計畫 |
| 實作 | **切片 1～8＋選單已完成**；§11 環視身體軸向尚未做 |

### 審查收斂狀態

| 項 | 狀態 |
|----|------|
| P1-1～P1-6、換衣例外 | **已關** |
| P1-2 WeakStop | **已關＝甲** |

### 實作進度

| 切片 | 狀態 |
|------|------|
| 1 §1 選池＋接 L | **已完成** |
| 2 §6 輸入閘＋鍵意 | **已完成** |
| 3 §4 高潮後→選池 | **已完成** |
| 4 §2 閒置開幹 | **已完成** |
| 5 §7～10 拆舊債 | **已完成**（停用） |
| 6 §5 窺視 N／L→選池 | **已完成** |
| 7 §3／21 感度 | **已完成**（下限預算） |
| 8 §16～19／22 | **已完成** |
| 選單 Ctrl+Shift+P | **已完成**（清楚用語） |
| 9 §11 環視身體軸向 | **未做**（下一步） |

---

## 下一任：可開工（等使用者說實作）

契約已齊。使用者回「實作」或「execute」後依主計畫＋審查切片開工。

**§22 定稿**：協助開 → 忽略 `WeakStop`；`gotoFaintnessCount` 等效＝6；保留強制脫力設定。

---

## 實作時必守（審查強調）

1. **拆舊債與建選池同片／先建後拆**：假滾輪／escape／checkpoint 是現在唯一自動出口；先拆後建會卡死閒置。建議切片順序見審查報告第 6 節（選池→閘→高潮後→閒置→再拆 7～10→窺視→感度→正交→環視最後）。  
2. **協助 active ≠ 環視轉動中**：拆兩狀態（P1-4）。  
3. **狀態名→格**：Idle／D_Idle＝閒置；WIdle／SIdle＝橋段；AfterIdle 名單＝高潮後；窺視用 ActionCtrl 不看 LongAppreciation。  
4. **環視**：身體軸向、|Δ|≥60° 非整數、每圈 zoom、1A 骨焦點、穿牆透明優先、停止鍵只停相機。工程量大，正交可最後做。

---

## 關鍵現碼落差（精簡）

詳見審查報告表格；實作必碰：

- `OrbitBehaviorHub`：initiative／auto-action／假滾輪／AfterIdle／quiet  
- `OrbitPoseLandedPolicy`／escape 門閂 → 拆第二套出口  
- `OrbitHelpers.PickNextPose` → 真選池  
- `OrbitController`／`OrbitCycleCoordinator`：圈數換姿拆；換衣續綁圈；FEEL 解耦轉動  
- `CanAcceptHotkey`：拆 nowOrgasm 總擋；加 ConfirmDialog  
- `OrgasmEffectsPatch`：擴大男射／女女掛點  
- `gotoFaintnessCount` readonly＝3 → 改 6；WeakStop 依甲／乙

原版參考：`dll_decompiled/Sonyu.cs`、`Peeping.cs`、`HScene.cs`（IsAfterIdle、ConfigVanish）、`Config/HSystem.cs`（WeakStop）。

---

## 對使用者說話模板

契約已齊。要開工時請回：

> 「實作」或「execute」

**給新對話的完整實作 PROMPT（含多 commit／push、重用既有碼）：**  
[`PROMPT_fsm_implement.md`](PROMPT_fsm_implement.md)

---

## 審查報告原文位置

使用者訊息／對話中已貼完整「HS2OrbitAndExciter FSM 契約定稿審查報告」（含 P0～P2、落差表、切片）。以該報告＋本交接＋主計畫三者對齊；衝突時以**主計畫最新修訂**為準。
