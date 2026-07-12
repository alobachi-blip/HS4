# HS2OrbitAndExciter FSM 契約定稿審查報告

> 來源：獨立 Reviewer。已收斂進主計畫與 `HANDOFF_fsm_contract_review.md`。  
> 主計畫：`C:\Users\jason\.cursor\plans\fsm_狀態圖確認_bd3a5656.plan.md`

## 1. 總評

契約整體自洽、拓樸清楚、可以進入實作；六欄格式讓每格的擁有者與出口都有單一答案，且「現況落差」欄大多寫對了現碼實情。最大風險有三：

1. 拆舊債與建新選池必須成對同片上線——現在的假滾輪／escape／checkpoint 是唯一讓流程動起來的東西，先拆後建的中間態會直接卡死在閒置。
2. 環視改身體軸向與現行「每幀只寫 CamDat.Rot 的 yaw」管線差距大，是工程量最大的一塊，幸好正交、可最後做。
3. §22 對原版脫力條件的前提有誤（詳見 P1-2），契約要小修但不影響拓樸。

無致命矛盾，建議修正 P1 條款後即可開工。

## 2. 致命問題（P0）

無。

## 3. 應修未修（P1）— 收斂狀態（交接後）

| 項 | 原問題 | 收斂 |
|----|--------|------|
| P1-1 | 總則「每格至少一條自動出口」vs 窺視 A-NL | **已關**：總則加窺視例外 |
| P1-2 | §22 誤把 resist／Libido 當脫力條件；真閘是 WeakStop | **已關**：22-甲＝協助開忽略 WeakStop；次數 6 |
| P1-3 | §6c N 總結句與各格表矛盾 | **已關**：表為準；N＝往前推 |
| P1-4 | 停止環視與協助同一開關 | **已關**：只停相機；協助／FEEL 照常；換衣綁圈則暫停 |
| P1-5 | Drink／Vomit 手動可否砍 | **已關**：自動播完；手動 N／L 可砍 |
| P1-6 | WIdle／SIdle 雙屬閒置與橋段 | **已關**：屬橋段，不回閒置 |

## 4. 實作前建議澄清（P2）— 仍供實作參考

- 停止環視鍵鍵位（熱鍵已滿）。
- vanish 清單怎補：先 spike 注入驗證 VanishProc。
- `gotoFaintnessCount` readonly＝3 → 反射寫或 Harmony（建議前者＋進 H 寫一次）。
- §16–18 高潮掛點清單（男射／女女／AddOrgasm）。
- 混池去重鍵：id 還是 id＋nDownPtn；窺視候選空屬正常。
- 輸入閘加 `ConfirmDialog.active`。
- B1 短餘裕起點寫死（進入 AfterIdle 那幀）。

## 5. 現碼落差清單（摘要）

詳見原審查對話；實作必拆／改：

- `OrbitBehaviorHub`：initiative／auto-action／假滾輪／AfterIdle／quiet
- `OrbitPoseLandedPolicy`／escape → 拆第二套出口
- `PickNextPose` → 真選池（可借 `OrbitShufflePool`）
- `CanAcceptHotkey`：拆 nowOrgasm；加 ConfirmDialog
- `OrbitCycleCoordinator`：拆圈數換姿；換衣續綁圈
- `OrgasmEffectsPatch`：擴大掛點
- 環視：身體軸向（最後做）

## 6. 建議實作切片（有序・先建後拆）

1. §1 選池（先接 L）  
2. §6a／6c 輸入閘＋鍵意  
3. §4 高潮後→選池  
4. §2 閒置開幹（原版 Insert）  
5. §7～10 同片拆舊債  
6. §5 窺視  
7. §3 感度預算  
8. §16–19／22 高潮事件＋脫力  
9. §11 環視（最後）

## 7. Reviewer 自信度

高——契約與現碼／反編譯有對照；環視身體軸向與 vanish 注入未實測，該兩塊工程量評估當「中」信心。
