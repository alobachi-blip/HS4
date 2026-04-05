# HS2 環視除錯／清理—會話總結

**日期**：2026-04-05（對照 CHANGELOG 同日條目）  
**分支**：`debug/orbit-compare-logging`（由「orbit compare NDJSON」工作延伸）

## 觀測結論（執行期證據）

- **長時間 `suppressAssist: true`** 時，常見原因為 **`pointerOverUi`**（`EventSystem.IsPointerOverGameObject()`），以及 **`orbitStartGrace`**、**`recentUiClick`** 等；與「游標停在 H 場景 UI 上時，自動換姿／輔助暫停」的設計一致。
- **完整來回一圈**後才觸發 `OnOrbitCycleComplete`（隨機視角、換裝、換姿）；**`checkpointTimeout_cfg: 0`** 時 checkpoint 強制推進路徑等於關閉。
- 曾加入 **`BuildCursorAssistGateJsonFields`**（含滑鼠／螢幕取樣）與 **`debug-aa23f7.log`** 對照；後以 try／catch 與實測確認**未觸發**該路徑例外，進場問題與對照用程式之因果未由日誌證實為主因。

## 已落地程式變更（量產導向）

- **刪除**：`CursorSessionDebugLog.cs`、`OrbitAgentDebugLog.cs`。
- **精簡**：[`HS2OrbitAndExciter/OrbitController.cs`](HS2OrbitAndExciter/OrbitController.cs)、[`OrbitBehaviorHub.cs`](HS2OrbitAndExciter/OrbitBehaviorHub.cs)、[`HS2OrbitAndExciter.cs`](HS2OrbitAndExciter/HS2OrbitAndExciter.cs) — 移除 SNAP/GATE/CYC、CHAR/REF、`BuildCursorAssistGateJsonFields`、checkpoint／wheel／assist 等僅供對照之寫檔與 log。
- **文件**：[`HS2OrbitAndExciter/CHANGELOG.md`](HS2OrbitAndExciter/CHANGELOG.md) 新增「清理除錯／對照用程式碼」小節。

外掛**不再**寫入工作區 `debug-*.log` 或 `orbit-compare` NDJSON。

## 未實作（產品向，另案）

- **繁中 HUD**：抑制原因白話、虛脫、動畫代號、換視角／換裝／換姿倒數等—規格見 Cursor 計畫「環視狀態 HUD」附錄（本 repo 可後續複製計畫檔至 `.cursor/plans` 若需版本化）。

## 可選清理

- [`HS2OrbitAndExciter/HANDOFF_orbit_debug_character_swap.md`](HS2OrbitAndExciter/HANDOFF_orbit_debug_character_swap.md) 仍引用已刪類別名，若造成混淆可刪除或改寫為歷史說明。
- 工作區未追蹤目錄 `.cursor/`、`orbit-compare/`、`orbit-tools/` 是否納入版控，依團隊慣例決定（本提交未包含）。
