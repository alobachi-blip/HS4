# 實作啟動 PROMPT（貼到新對話）

把下面整段複製給新 Agent 即可。

---

```text
# 任務：依 FSM 契約實作 HS2OrbitAndExciter

## 角色與語言
你是實作工程師。對使用者用**繁體中文**；不自創縮詞。
會改 FSM 切換／出口的提案必須先警告「這會改邏輯」，預設禁止；過程節奏（秒數）可調。

## 必讀（唯一真相）
1. 主計畫：`C:\Users\jason\.cursor\plans\fsm_狀態圖確認_bd3a5656.plan.md`（0～23 已全部定稿）
2. 交接：`D:\HS4\HS2OrbitAndExciter\HANDOFF_fsm_contract_review.md`
3. Repo：`D:\HS4`，外掛：`HS2OrbitAndExciter/`

舊 HANDOFF／舊窺視 plan 可能過時 → 以主計畫為準。

## 兩條硬規則（使用者特別要求）

### 1) 盡量使用既有、已在 HS2 驗證過的 CODE
- 優先改／搬／重用現有模組與 API，不要重寫輪子。
- 可重用範例（視契約需要取捨，勿違反契約）：
  - `OrbitShufflePool`（prefer-unused 去重模式）→ 選池去重
  - `OrbitHelpers` 骨點／身高／姿列表 → 環視與選池
  - `OrbitPoseDirector` 換姿佇列／落地通知 → 接選池，不要另造換姿管線
  - 既有高潮特效（刺青／胸／噴）、`PregnancyPlusAssist` → 擴掛點，勿另起系統
  - 原版路徑：開幹走 `IsStart`／`StartProc`（Insert），不要再 `setPlay(WLoop)` 直跳
  - 穿牆透明：強化既有 `ConfigVanish`／Shield，不要先發明 Linecast 障礙換角
- 只有契約明確要求「拆掉」的才拆（假滾輪、initiative／GetAutoAnimation、圈數換姿、PoseLandedPolicy 第二套出口、LongAppreciation 綁自慰等）。
- 不確定是否已有現成實作時：先在 `HS2OrbitAndExciter/` 與 `dll_decompiled/` **搜尋再寫**。

### 2) 盡量多 commit and push
- 每完成一個可驗證切片就 **commit + push**（不要等全部做完）。
- Commit 訊息簡短說明「為什麼／對應契約哪一節」；用 HEREDOC；不要 -i；不要改 git config。
- Push 用目前分支 `git push -u origin HEAD`（或已追蹤則 `git push`）；需要網路權限就申請。
- 勿把 secrets、整包 `bin/`、遊戲 DLL 誤加入 commit；只 stage 相關原始碼／交接／必要設定。
- 使用者已明確要求頻繁 push → 此任務視為已授權 push（仍勿 force push main／master）。

## 產品目標
開 Ctrl+Shift+O：幾乎不介入也能 開幹→高潮→換段→窺視；可手動；不卡死。
換段只認選池；正交不改流程出口。

## 實作順序（審查核定・不可先拆後建）
舊債是現在唯一自動出口 → **先建選池／替代出口，再拆假滾輪等**。

建議切片（每步結束：測一下概念 → commit → push）：

1. **§1 選池模組**：混池、本場去重、耗盡清空、空池放寬、窺視分支（ActionCtrl）。先接 L。重用 ShufflePool。
2. **§6a／6c 輸入閘＋鍵意**：拆 nowOrgasm 總擋；加 ConfirmDialog；L／N 依格分流。
3. **§4 高潮後→選池**：B1 短餘裕；Drink／Vomit 自動播完、手動可砍；拆 OrgasmQuiet 擋換段。
4. **§2 閒置開幹**：觸發原版開始走 Insert；落地約 1 秒；N＝取消排程立刻開幹。
5. **§7～10 同片拆舊債**：假滾輪、auto-action／GetAutoAnimation、圈數換姿、PoseLandedPolicy 出口／escape 門閂。
6. **§5 窺視**：選池進 Peeping；N／L→選池；刪 LongAppreciation 綁自慰。
7. **§3 感度預算**：橋段內 FEEL；不綁「是否在轉環視」。
8. **§16～19／22**：高潮統一事件（含男射／女女）；腹脹／縮腹；脫力次數 6＋協助忽略 WeakStop（22-甲）。
9. **§11 環視（最後）**：身體軸向、|Δ|≥60° 非整數、每圈 zoom、1A 骨焦點、停止鍵只停相機、協助≠轉動；換衣續綁圈；穿牆透明優先。

## 契約速記（易踩坑）
- 窺視 A-NL：可不做播完→選池；主靠 N／L；撞原版確認窗可接受。
- 停止環視 ≠ 關協助；FEEL 跟協助不跟轉動；換衣綁圈則停轉暫停換衣。
- Idle／D_Idle＝閒置；WIdle／SIdle＝橋段；AfterIdle 含 Drink_A／Vomit_A。
- 日誌詞彙改成格進出／選池／開幹（§23），清舊 escape／checkpoint 噪音。

## 完成定義（每個切片）
- 對應契約條款可指出
- 盡量有日誌或 HUD 可觀察
- commit + push 完成
- 向使用者用 3～5 句報告：做了什麼、怎麼驗、下一步

## 不要做
- 不要重開契約問答（除非 P0 級矛盾；格式：狀況→選項→建議）
- 不要順便改狀態圖拓樸
- 不要 force push；不要 commit 無關大檔

開始前先讀主計畫 overview＋交接＋`git status`／目前分支，然後從切片 1 開工並立刻準備第一次 commit。
```

---

## 給你自己（使用者）

新對話第一則訊息貼上面 fenced 區塊內文即可。  
若要強調：可再加一句「先做切片 1 選池，做完就 commit push」。
