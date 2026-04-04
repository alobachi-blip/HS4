# HS2 H 場景：遊戲如何定義「Idle」動畫狀態

本文整理 **Honey Select 2** 原版 `HScene` 對女角 Animator **layer 0** 的 idle 判定，供 `HS2OrbitAndExciter`（環視／滾輪 bypass 閘門等）對照使用。

來源：反編譯 `Assembly-CSharp` 中 `HScene.IsIdle(Animator _anim)`（私有方法）。

## `HScene.IsIdle` 視為 idle 的狀態名（共 6 個）

當 `Animator.runtimeAnimatorController != null` 時，下列任一狀態名即回傳 `true`：

| 狀態名 | 說明 |
|--------|------|
| `Idle` | 常見準備／待機；愛撫、奉仕、插入、女女、MultiPlay、自慰等 proc 多處與 `D_Idle` 成對分支 |
| `D_Idle` | 雙人／另一條線的待機 |
| `WIdle` | 名稱含 Idle；**打屁股（Spnking）**等流程中的段落狀態，仍被遊戲歸在 `IsIdle` |
| `SIdle` | 同上，Spnking 相關 |
| `Insert` | 插入相關段落（名稱無 Idle 字樣，仍算 `IsIdle`） |
| `D_Insert` | 雙人線插入相關 |

另：若 `runtimeAnimatorController == null`，`IsIdle` 直接回傳 `true`（退化情況）。

## 與本外掛其他邏輯的差異

- **`OrbitController.IsInPreparationState`**（準備階段）：只認 **`Idle` 或 `D_Idle`**，且須 **`speed` 很小**。比 `HScene.IsIdle` **範圍更窄**（不含 `WIdle`／`SIdle`／`Insert`／`D_Insert`）。
- **`ActionLoopStateNames`**：把 **`WIdle`、`SIdle`** 當成**動作 loop**（可累加感度等），語意是「進行中的段落」，**不要**與準備用 `Idle`／`D_Idle` 混淆。

## 實作 bypass 閘門時的取捨

- **貼近遊戲廣義 idle**：允許注入條件可對齊 **`HScene.IsIdle` 的六狀態**（方法為 private，外掛需自帶同名單或反射呼叫）。
- **貼近「只從準備進 loop」**：維持 **`Idle` + `D_Idle`**（必要時再依實測加入 `Insert`／`D_Insert`）。

## 外掛計畫約定：`OrbitBypass` 允許注入假滾輪的狀態

下列狀態在環視開啟、真實滾輪為 0 時，**可**進入延遲並注入假滾輪（實作以程式為準）：

| 狀態名 | 備註 |
|--------|------|
| `Idle` | 準備／一般待機 |
| `D_Idle` | 雙人線待機 |
| `WIdle` | Spnking；**同時**在 `ActionLoopStateNames` 內，注入後仍有被遊戲當滾輪讀取、影響速度的風險，需實測 |
| `SIdle` | 同上 |
| `Insert` | 與 `HScene.IsIdle` 對齊，外掛 `OrbitBypassAnimatorGate` 已納入 |
| `D_Insert` | 同上 |

**不**應注入（須從允許清單排除）的典型狀態：`WLoop`、`SLoop`、`OLoop`、`D_WLoop`、`D_SLoop`、`D_OLoop`、`MLoop`、`WAction`、`SAction` 等主動作 loop／高潮相關段落，以免干擾玩家用滾輪調速或 Finish。

實作：`OrbitBypassAnimatorGate.cs` 允許清單為上表六態（含 `Insert`／`D_Insert`），與 `HScene.IsIdle` 一致。

## 相關原版方法（非 idle）

同檔案中另有 `IsAfterIdle`，用於高潮後等狀態（`Orgasm_A`、`Orgasm_IN_A`…），與上表無關。

## 本 repo 內對照反編譯路徑（若存在）

`.claude/worktrees/inspiring-haibt/dll_decompiled/HScene.cs` 約行 3778–3790：`IsIdle`。
