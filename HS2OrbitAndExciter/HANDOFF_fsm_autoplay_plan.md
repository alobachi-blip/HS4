# 交接 Prompt：HS2Orbit FSM／自動推進／換角

把下列整段貼給下一任 agent。主計畫檔請先讀再改。

---

## 你是誰、要做什麼

你在 repo `D:\HS4` 的 **HS2OrbitAndExciter** 上，延續「H 流程 FSM 簡潔化」設計討論（多數仍在 **Plan 模式／未實作**）。  
**先讀**主計畫並與使用者對齊，**不要**在使用者說「實作／execute」前大改程式。

**主計畫（唯一真相來源）**  
`C:\Users\jason\.cursor\plans\窺視_n_l_回_idle_70e3914d.plan.md`

相關程式（現況，未必已對齊計畫）：  
`HS2OrbitAndExciter/OrbitBehaviorHub.cs`、`OrbitPoseDirector.cs`、`OrbitManualDirector.cs`、`OrbitPoseLandedPolicy.cs`、`OrbitHelpers.cs`  
原版參考：`dll_decompiled/Sonyu.cs`、`Peeping.cs`、`HScene.cs`（ShortcutKey）、`HVoiceCtrl.cs`（Houchi）

語言：對使用者用 **中文**。

---

## 產品準則（已鎖）

1. **乾淨**＝分支少、**不跨幀等語音**；不為手感在每格加 0.5～2s。  
2. **遊戲有的跟遊戲**；我們只補 **使用者不介入會卡死** 的洞。  
3. **按鍵幾秒內要有回饋**（進 routine 就算）；我們自己的閘可改；原版模態／loading 尊重。  
4. **跳出**＝全域逃生，**不畫進**狀態圖。  
5. **換角／換衣／鏡頭／刺青等**＝正交，不代替 NeedNext。

---

## FSM 定案（狀態圖）

```text
選池 NeedNext
  ├─ 動作線姿 → Idle（對話旗 + 開幹）→ 橋段內 → AfterIdle → NeedNext
  └─ 窺視 → In…Out_Loop → 播完／N／L → NeedNext
L = 使用者版 NeedNext（抽到窺視則進窺視，不保證回 Idle）
換角 G = 圖外
```

- **NeedNext**：選池事件（混窺視、本場少重複姿）；**不是** Idle 模式。  
- **Idle**：只有一種含義＝設對話旗 + AutoStartSex；**禁止**在 Idle 再選池。  
- **欣賞線**＝窺視播片（可看）；播完仍要 NeedNext（不是永久停）。  
- **動作橋段換段唯一自動點**：高潮後 **AfterIdle 出口 → NeedNext**（**禁止**再同姿 ForceStartSex 回 WLoop）。  
- 進 H 第一姿：選單已選好 → 只跑 Idle 合約，不強制先 NeedNext。  
- quiet≈2s：**不擋** AfterIdle→NeedNext。  
- 換段 **只認 NeedNext**；Cycle／GetAutoAnimation 舊換姿路徑要關或改呼叫 NeedNext。  
- 窺視協助下：Out_Loop 達標 **直接 NeedNext**，不開跳出 Confirm；使用者主動跳出仍可 SceneEnd。

---

## 橋段內定案

```text
Idle →（canInside）Insert 播完 → WLoop →（feel／原版）→ 高潮 → AfterIdle → NeedNext
無 Insert：Idle → WLoop → …
```

- **自動走 Insert**（現況 `TryForceStartSex` 仍直跳 WLoop → **實作時要改**）。  
- Insert→WLoop＝**原版 InsertProc**（播完），不加人工延遲。  
- **假滾輪**：不再用於 AfterIdle→同姿 Loop；橋段內僅當「原版自己不會高潮」時的**最小後備**（待實測）。  
- FeelAdd：現況環視+Loop 加 `feel_f`；**Feel 滿≠自動高潮**——若原版無滾輪不會自己高潮，再補最小觸發。

**下一討論／實測題（使用者已收斂範圍）**  
> 協助開啟、無滾輪時，原版會不會自行 Loop→高潮？會→少補；不會→只補反卡死最小手段。

---

## 換角 G 定案

- 一切不動只換人；**聲音重綁**；衣＝**角色卡預設**（不留舊衣）。  
- Idle／進行中（**含高潮**）允許；過渡中（換姿／換角 busy／短模態）拒。  
- **不因 `nowOrgasm` 拒**（現況 `CanAcceptHotkey` 會擋 → 實作對 G **放行**）。  
- 僅手動 G；不 NeedNext；busy 防與 L／選池搶寫。  
- 選角：**必須不同性格／聲線**；差異越大權重越高（權重加成**尚未做**）。  
- 換完：清刺青戳、胸基準、乳頭噴；rebind 鏡頭錨點；穿齊。  
- 雙女／第二女：本階段不管。

---

## 原版輸入閘（熱鍵討論用）

ShortcutKey 總閘：`isFade`、Scene fade/loading、Exit/Confirm/Config/Shortcut/Tutorial 疊層、`inputForcus`。  
`IsSpriteOver` 只擋滾輪。  
`nowOrgasm`／高潮動畫／窺視播片：**不**進 ShortcutKey 總閘（不擋讀鍵）。  
`inputForcus`＝輸入框焦點（拼錯的 input focus）。

---

## 明確不做／已否決

- 對話完再換姿；Idle 中繼／落地雙模式；進 Idle 就換姿當主路徑。  
- 跨幀等語音；每格固定 0.5～2s。  
- 窺視中 AutoStartSex；同姿自動連高潮（本階段）。  
- 把跳出畫進狀態圖。

---

## 計畫文件注意

- 計畫裡曾有過時「Idle 自動換姿／Cycle 主換姿」——已改正交段；若仍看到舊句以 **NeedNext／AfterIdle 定案** 為準。  
- 多個衍生 plan 檔（Idle入場對話、對話完再換姿等）可能過時；**以 `窺視_n_l_回_idle_70e3914d.plan.md` 為準**。

---

## 建議下一任第一步

1. 讀主計畫全文 + 本交接。  
2. 問使用者：繼續 **實測／定「無滾輪會否自己高潮」**，還是開始 **實作**（並切 Agent 模式）。  
3. 若實作，建議順序：  
   - Idle 合約（對話旗+開幹含 Insert）  
   - NeedNext API + AfterIdle→NeedNext（取代同姿回 Loop）  
   - 窺視離場→NeedNext  
   - G 放行高潮 + 性格距離權重  
   - 再處理 Loop→高潮反卡死（依實測）

---

## 一鍵開場白（可貼）

請讀 `C:\Users\jason\.cursor\plans\窺視_n_l_回_idle_70e3914d.plan.md` 與 `HS2OrbitAndExciter` 裡上述交接結論。我們在鎖 FSM：NeedNext 選池；Idle 只開幹（自動 Insert）；AfterIdle→NeedNext；窺視播完 NeedNext；換角正交。橋段內只補會卡死的洞。先確認你理解狀態圖，再依我指示繼續討論「無滾輪能否自己高潮」或開始實作。
