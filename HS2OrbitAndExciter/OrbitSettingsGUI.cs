using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// In-game settings window. Toggle with Ctrl+Shift+P. Values write to BepInEx config (auto-saved).
    /// </summary>
    public class OrbitSettingsGUI : MonoBehaviour
    {
        private const KeyCode MenuHotkey = KeyCode.P;
        private bool _visible;
        private bool _needSyncFromConfig;
        private Rect _windowRect = new Rect(80, 60, 500, 680);
        private Vector2 _scroll;
        private GUIStyle? _labelStyle;
        private bool _stylesInitialized;

        private string _orbitTimeStr = "";
        private string _orbitCountRandomStr = "";
        private string _excitementDelayStr = "";
        private string _feelAddPerSecStr = "";
        private string _orbitDistHeadStr = "";
        private string _orbitDistChestStr = "";
        private string _orbitDistPelvisStr = "";

        private bool _lastOverrideFaintness;

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (Event.current != null && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == MenuHotkey && Event.current.control && Event.current.shift)
            {
                _visible = !_visible;
                if (_visible) _needSyncFromConfig = true;
                Event.current.Use();
            }
            if (!_visible) return;

            if (Event.current != null && Event.current.type == EventType.Layout && _needSyncFromConfig
                && HS2OrbitAndExciter.OrbitTimePer360 != null)
            {
                _orbitTimeStr = HS2OrbitAndExciter.OrbitTimePer360.Value.ToString("F1");
                _orbitCountRandomStr = (HS2OrbitAndExciter.OrbitCountBeforeRandom?.Value ?? 0).ToString();
                _excitementDelayStr = (HS2OrbitAndExciter.ExcitementTriggerDelaySeconds?.Value ?? 0f).ToString("F1");
                _feelAddPerSecStr = (HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit?.Value ?? 0.1f).ToString("F2");
                _orbitDistHeadStr = (HS2OrbitAndExciter.OrbitDistanceHead?.Value ?? 1.4f).ToString("F2");
                _orbitDistChestStr = (HS2OrbitAndExciter.OrbitDistanceChest?.Value ?? 1.4f).ToString("F2");
                _orbitDistPelvisStr = (HS2OrbitAndExciter.OrbitDistancePelvis?.Value ?? 1.4f).ToString("F2");
                _lastOverrideFaintness = HS2OrbitAndExciter.OverrideFaintness?.Value ?? false;
                _needSyncFromConfig = false;
            }

            InitStyles();
            float maxH = Mathf.Max(280f, Screen.height - 48f);
            if (_windowRect.height > maxH)
                _windowRect.height = maxH;
            if (_windowRect.width < 480f)
                _windowRect.width = 500f;
            _windowRect = GUILayout.Window(
                9001,
                _windowRect,
                DrawWindow,
                "環視與流程協助 — 設定（Ctrl+Shift+P）");
        }

        private void DrawWindow(int id)
        {
            var label = _labelStyle ?? GUI.skin.label;
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            float scrollH = Mathf.Max(160f, _windowRect.height - 56f);
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.Height(scrollH));

            GUILayout.BeginVertical(GUI.skin.box);
            foreach (var line in PluginBuildIdentity.GetGuiLines())
                GUILayout.Label(line, label);
            GUILayout.EndVertical();

            // ─── 1. 使用方式 ───────────────────────────────────
            GUILayout.Space(6);
            GUILayout.Label("使用方式", GUI.skin.box);
            GUILayout.Label(
                "Ctrl+Shift+O：開啟／關閉環視協助（流程協助＋預設開始轉相機）。開啟後左下角可看狀態；Ctrl+Shift+I 切換狀態面板顯示。",
                label);
            GUILayout.Label(
                "協助與轉動可分開：開協助後按 V 只停／恢復相機環繞；選池、感度、高潮後換段仍繼續。換姿勢由選池（L）或高潮後／窺視等自動路徑負責，圈數本身不再換姿勢。",
                label);

            // ─── 2. 環視相機 ───────────────────────────────────
            GUILayout.Space(8);
            GUILayout.Label("環視相機", GUI.skin.box);
            GUILayout.Label(
                "名詞：單向繞身體軀幹軸 360°＝一次「旋轉」；去程加回程＝一次「迴轉」（約 2×單向秒數）。躺／跪時仍繞「頭−骨盆」軸，不是世界鉛垂。",
                label);
            GUILayout.Label(
                "每圈會換相對角（約 ≥60°、非整數）與遠近；骨焦點優先（頭／胸／骨盆）。穿牆透明沿用遊戲 Shield（ConfigVanish），不停轉時仍綁焦點。",
                label);

            if (HS2OrbitAndExciter.OrbitStatusHudEnabled != null)
            {
                HS2OrbitAndExciter.OrbitStatusHudEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.OrbitStatusHudEnabled.Value,
                    " 啟用左下角環視狀態面板（繁中；環視開啟時可用 Ctrl+Shift+I 切換顯示）");
            }
            if (HS2OrbitAndExciter.OrbitStatusHudEnabled?.Value == true)
            {
                bool pv = OrbitStatusHud.GetPanelVisible();
                pv = GUILayout.Toggle(pv, " 目前顯示狀態面板");
                OrbitStatusHud.SetPanelVisible(pv);
            }

            if (HS2OrbitAndExciter.OrbitTimePer360 != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("單向水平 360° 所需秒數：", label, GUILayout.Width(220));
                GUI.SetNextControlName("OrbitTimePer360");
                _orbitTimeStr = GUILayout.TextField(_orbitTimeStr, GUILayout.Width(60));
                if (float.TryParse(_orbitTimeStr, out float v) && v > 0.1f && v <= 120f)
                    HS2OrbitAndExciter.OrbitTimePer360.Value = v;
                GUILayout.EndHorizontal();
            }

            if (HS2OrbitAndExciter.OrbitCountBeforeRandom != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("每幾次旋轉亂數焦點與水平角（0＝關閉亂數）：", label, GUILayout.Width(280));
                GUI.SetNextControlName("OrbitCountBeforeRandom");
                _orbitCountRandomStr = GUILayout.TextField(_orbitCountRandomStr, GUILayout.Width(60));
                if (int.TryParse(_orbitCountRandomStr, out int n) && n >= 0 && n <= 99)
                    HS2OrbitAndExciter.OrbitCountBeforeRandom.Value = n;
                GUILayout.EndHorizontal();
            }

            if (HS2OrbitAndExciter.ClothesChangeEnabled != null)
            {
                HS2OrbitAndExciter.ClothesChangeEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.ClothesChangeEnabled.Value,
                    " 依上方「每幾次旋轉」切換衣物階段（次數為 0 時改為每迴轉一次）");
            }

            GUILayout.Label("焦點距離（單位：全身長倍率，建議 1～3；輸入後立即套用並寫入設定）", label);
            DrawDistField("頭部焦點距離", "OrbitDistHead", ref _orbitDistHeadStr, HS2OrbitAndExciter.OrbitDistanceHead);
            DrawDistField("胸部焦點距離", "OrbitDistChest", ref _orbitDistChestStr, HS2OrbitAndExciter.OrbitDistanceChest);
            DrawDistField("骨盆焦點距離", "OrbitDistPelvis", ref _orbitDistPelvisStr, HS2OrbitAndExciter.OrbitDistancePelvis);

            GUILayout.Label(
                "已停用／退役：依迴轉換姿勢（ChangePoseOnCycle）、環視自動選動作、檢查點逾時強制跳關。換段請用選池（L）與流程熱鍵。",
                label);

            // ─── 3. 流程熱鍵 ───────────────────────────────────
            GUILayout.Space(8);
            GUILayout.Label("流程熱鍵", GUI.skin.box);
            GUILayout.Label("單鍵操作（勿同時按 Ctrl／Shift／Alt；與環視開關無關）：", label);
            GUILayout.Label(OrbitManualHotkeys.HudLegend, label);
            GUILayout.Label(
                "L＝手動選池（各流程格皆換姿勢）。N＝依目前格往前推：閒置＝立即開始進行；動作橋段＝加速感度與速度；高潮後閒置／窺視＝選池。",
                label);
            GUILayout.Label(
                "G＝換女角色（池會排除同性格卡；短時間連換會降權、久留會優先）。H＝換套裝。J＝亂數穿著階段。K＝切換姿勢鏡頭。",
                label);
            GUILayout.Label(
                "Q／W／E＝切環視焦點（頭／胸／骨盆）；Shift＋Q／W／E＝切第二女角色焦點。V＝只停／恢復環視轉動（不關協助）。",
                label);
            GUILayout.Label(
                OrbitManualHotkeys.PregnancyHudLegend
                + " — Y／U 由 PregnancyPlus 調整肚子；R 由本插件強制清空肚子（含 H 場景膨脹）。",
                label);
            if (HS2OrbitAndExciter.CumflationEnabled != null)
            {
                HS2OrbitAndExciter.CumflationEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.CumflationEnabled.Value,
                    " 內射時肚子脹一級；愛撫／女女落地時肚子消一級（PregnancyPlus；R 仍可清空）");
            }

            // ─── 4. 脫力 ───────────────────────────────────────
            GUILayout.Space(8);
            GUILayout.Label("脫力", GUI.skin.box);
            GUILayout.Label(
                $"協助開啟時：脫力次數門檻改為 {OrbitFaintnessAssist.TargetGotoFaintnessCount}；並暫時忽略遊戲「弱體化停止」（WeakStop），關閉協助後還原。",
                label);
            if (HS2OrbitAndExciter.OverrideFaintness != null)
            {
                bool newFaintness = GUILayout.Toggle(
                    HS2OrbitAndExciter.OverrideFaintness.Value,
                    " 強制脫力開／關（影響可用姿勢表與鏡頭）");
                HS2OrbitAndExciter.OverrideFaintness.Value = newFaintness;
                if (newFaintness != _lastOverrideFaintness)
                {
                    _lastOverrideFaintness = newFaintness;
                    OrbitHelpers.SetGameFaintnessAndRequestViewReapply(newFaintness);
                }
            }

            // ─── 5. 感度 ───────────────────────────────────────
            GUILayout.Space(8);
            GUILayout.Label("感度", GUI.skin.box);
            if (HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit != null)
            {
                GUILayout.Label(
                    "環視開啟時，每秒自動累加感度條（0＝只靠遊戲／滑鼠；0.1≈約 10 秒滿條）。",
                    label);
                GUILayout.BeginHorizontal();
                GUILayout.Label("每秒感度增加量：", label, GUILayout.Width(160));
                GUI.SetNextControlName("FeelAddPerSec");
                _feelAddPerSecStr = GUILayout.TextField(_feelAddPerSecStr, GUILayout.Width(60));
                if (float.TryParse(_feelAddPerSecStr, out float feel) && feel >= 0f && feel <= 5f)
                {
                    if (feel > 0f && feel < 0.01f) feel = 0.01f;
                    HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit.Value = feel;
                }
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.ExcitementTriggerDelaySeconds != null)
            {
                GUILayout.Label(
                    "感度條滿後，再等幾秒才自動觸發高潮（0＝立刻；滑鼠點擊仍可立刻觸發）。",
                    label);
                GUILayout.BeginHorizontal();
                GUILayout.Label("滿條後延遲秒數：", label, GUILayout.Width(160));
                GUI.SetNextControlName("ExcitementTriggerDelay");
                _excitementDelayStr = GUILayout.TextField(_excitementDelayStr, GUILayout.Width(60));
                if (float.TryParse(_excitementDelayStr, out float delay) && delay >= 0f && delay <= 10f)
                {
                    HS2OrbitAndExciter.ExcitementTriggerDelaySeconds.Value = delay;
                    Patches.ExciterState.DelaySecondsAtFull = delay;
                }
                GUILayout.EndHorizontal();
            }

            // ─── 6. 語音巡禮 ───────────────────────────────────
            GUILayout.Space(8);
            GUILayout.Label("語音巡禮", GUI.skin.box);
            GUILayout.Label(
                "依女高潮／侍奉射精／插入內射，依序切換音庫（青澀→好意→享樂→隷属→嫌悪→依存→壊れ）。不改卡片好感等數值；進度可依角色記住。",
                label);
            if (HS2OrbitAndExciter.VoiceTourEnabled != null)
            {
                HS2OrbitAndExciter.VoiceTourEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourEnabled.Value,
                    " 啟用語音巡禮");
            }
            if (HS2OrbitAndExciter.VoiceTourHitsPerStage != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"每一語音階段需要幾次觸發（目前 {HS2OrbitAndExciter.VoiceTourHitsPerStage.Value}）",
                    GUILayout.Width(260));
                float h = GUILayout.HorizontalSlider(HS2OrbitAndExciter.VoiceTourHitsPerStage.Value, 1f, 10f);
                int hi = Mathf.RoundToInt(h);
                if (hi != HS2OrbitAndExciter.VoiceTourHitsPerStage.Value)
                    HS2OrbitAndExciter.VoiceTourHitsPerStage.Value = hi;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.VoiceTourLoop != null)
            {
                HS2OrbitAndExciter.VoiceTourLoop.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourLoop.Value,
                    " 到「壊れ」後從頭循環");
            }
            if (HS2OrbitAndExciter.VoiceTourPersistProgress != null)
            {
                HS2OrbitAndExciter.VoiceTourPersistProgress.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourPersistProgress.Value,
                    " 依角色記住進度（換人再回來可續接）");
            }
            if (HS2OrbitAndExciter.VoiceTourResetOnNewH != null)
            {
                HS2OrbitAndExciter.VoiceTourResetOnNewH.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourResetOnNewH.Value,
                    " 每次進入 H 場景從第一階段重來（忽略已記住進度）");
            }
            if (HS2OrbitAndExciter.VoiceTourCountHoushiMaleFinish != null)
            {
                HS2OrbitAndExciter.VoiceTourCountHoushiMaleFinish.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourCountHoushiMaleFinish.Value,
                    " 侍奉體外／口內射精也算一次觸發（插入內射一律計算）");
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重置目前角色語音進度", GUILayout.Width(200)))
                OrbitVoiceTour.ResetCurrentCharacterProgress();
            GUILayout.Label(
                $"現況：{OrbitVoiceTour.CurrentLabelZh} {OrbitVoiceTour.StageIndex + 1}/{OrbitVoiceTour.StageCount}",
                label);
            GUILayout.EndHorizontal();

            // ─── 7. 高潮特效 ───────────────────────────────────
            GUILayout.Space(8);
            GUILayout.Label("高潮特效", GUI.skin.box);
            GUILayout.Label(
                "女高潮時可觸發：身體刺青貼花、胸部變大、乳頭潮吹（複用女角色潮吹／噴尿視覺）。左下角狀態面板會顯示目前狀態。",
                label);

            if (HS2OrbitAndExciter.OrgasmTattooEnabled != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("高潮刺青", GUILayout.Width(100));
                bool tattooOn = HS2OrbitAndExciter.OrgasmTattooEnabled.Value;
                bool next = GUILayout.Toggle(
                    tattooOn,
                    tattooOn ? $"開啟（T 貼下一張）×{OrbitOrgasmTattoo.Count}" : "關閉（Shift+T）");
                if (next != tattooOn)
                    HS2OrbitAndExciter.OrgasmTattooEnabled.Value = next;
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "H 場景：T＝開啟並依序貼一張；Shift+T＝關閉自動貼。貼花掛在身體掛點，不進飾品欄；換衣會重掛；G 換角色會清空。",
                    label);

                if (HS2OrbitAndExciter.OrgasmTattooMaxCount != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"最多張數 {HS2OrbitAndExciter.OrgasmTattooMaxCount.Value}", GUILayout.Width(100));
                    float mc = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmTattooMaxCount.Value, 1f, 64f);
                    int mi = Mathf.RoundToInt(mc);
                    if (mi != HS2OrbitAndExciter.OrgasmTattooMaxCount.Value)
                        HS2OrbitAndExciter.OrgasmTattooMaxCount.Value = mi;
                    GUILayout.EndHorizontal();
                }
                if (HS2OrbitAndExciter.OrgasmTattooScaleMin != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"最小倍率 ×{HS2OrbitAndExciter.OrgasmTattooScaleMin.Value:F1}", GUILayout.Width(100));
                    float smin = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmTattooScaleMin.Value, 1f, 20f);
                    if (Mathf.Abs(smin - HS2OrbitAndExciter.OrgasmTattooScaleMin.Value) > 0.05f)
                        HS2OrbitAndExciter.OrgasmTattooScaleMin.Value = Mathf.Round(smin * 10f) / 10f;
                    GUILayout.EndHorizontal();
                }
                if (HS2OrbitAndExciter.OrgasmTattooScaleMax != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"最大倍率 ×{HS2OrbitAndExciter.OrgasmTattooScaleMax.Value:F1}", GUILayout.Width(100));
                    float smax = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmTattooScaleMax.Value, 1f, 20f);
                    if (Mathf.Abs(smax - HS2OrbitAndExciter.OrgasmTattooScaleMax.Value) > 0.05f)
                        HS2OrbitAndExciter.OrgasmTattooScaleMax.Value = Mathf.Round(smax * 10f) / 10f;
                    GUILayout.EndHorizontal();
                }
            }

            if (HS2OrbitAndExciter.OrgasmBustGrowEnabled != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("高潮胸部變大", GUILayout.Width(100));
                bool bustOn = HS2OrbitAndExciter.OrgasmBustGrowEnabled.Value;
                bool bustNext = GUILayout.Toggle(bustOn, bustOn ? "開啟" : "關閉");
                if (bustNext != bustOn)
                    HS2OrbitAndExciter.OrgasmBustGrowEnabled.Value = bustNext;
                GUILayout.EndHorizontal();
                if (HS2OrbitAndExciter.OrgasmBustGrowPercent != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"每次 +{HS2OrbitAndExciter.OrgasmBustGrowPercent.Value:F0}%", GUILayout.Width(100));
                    float p = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmBustGrowPercent.Value, 0f, 50f);
                    if (Mathf.Abs(p - HS2OrbitAndExciter.OrgasmBustGrowPercent.Value) > 0.05f)
                        HS2OrbitAndExciter.OrgasmBustGrowPercent.Value = Mathf.Round(p);
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label("相對目前胸サイズ倍率放大；B 鍵回復進入 H／換角色時的基準胸圍。", label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("回復胸部（B）", GUILayout.Width(140)))
                    OrbitOrgasmBustGrowth.TryRestore(OrbitController.TryGetHScene());
                GUILayout.Label(OrbitOrgasmBustGrowth.HudStatus, label);
                GUILayout.EndHorizontal();
            }

            if (HS2OrbitAndExciter.OrgasmNippleSprayEnabled != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("乳頭潮吹", GUILayout.Width(100));
                bool nipOn = HS2OrbitAndExciter.OrgasmNippleSprayEnabled.Value;
                bool nipNext = GUILayout.Toggle(nipOn, nipOn ? "開啟" : "關閉");
                if (nipNext != nipOn)
                    HS2OrbitAndExciter.OrgasmNippleSprayEnabled.Value = nipNext;
                GUILayout.Label(OrbitOrgasmNippleSpray.HudStatus, label);
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "複用女角色潮吹／噴尿視覺；預設自訂連噴（先強後弱）。可改跟遊戲潮吹節奏。偏移／旋轉於下次高潮套用。",
                    label);

                if (HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("噴出節奏", GUILayout.Width(100));
                    bool nativeOn = HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm.Value;
                    bool nativeNext = GUILayout.Toggle(
                        nativeOn,
                        nativeOn ? "跟遊戲潮吹節奏" : "自訂連噴（先強後弱）");
                    if (nativeNext != nativeOn)
                        HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm.Value = nativeNext;
                    GUILayout.EndHorizontal();
                }

                bool nativeRhythm = HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm?.Value ?? false;
                if (nativeRhythm)
                    GUILayout.Label("（目前跟遊戲潮吹節奏；下列連噴參數暫不套用）", label);

                DrawNippleSpraySliders();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("重建乳頭噴口", GUILayout.Width(140)))
                    OrbitOrgasmNippleSpray.ForceRebuild(OrbitController.TryGetHScene());
                if (GUILayout.Button("重設為預設", GUILayout.Width(120)))
                    OrbitOrgasmNippleSpray.ResetSettingsToDefaults(OrbitController.TryGetHScene());
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "預設：偏移 (0, 0, 0.02)／旋轉 (90, 0, 0)；連噴 5 次、先強後弱。重設後自動重建噴口。",
                    label);
            }

            GUILayout.Space(8);
            GUILayout.Label("設定值會自動儲存，保持至下次變更。", label);
            GUILayout.Label(
                "熱鍵：Ctrl+Shift+O 環視協助；Ctrl+Shift+I 狀態面板；Ctrl+Shift+P 本視窗。頂端為建置對照。",
                label);

            GUILayout.EndScrollView();

            if (GUILayout.Button("關閉"))
                _visible = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }

        private void DrawDistField(
            string title,
            string controlName,
            ref string text,
            BepInEx.Configuration.ConfigEntry<float>? entry)
        {
            if (entry == null) return;
            var label = _labelStyle ?? GUI.skin.label;
            GUILayout.BeginHorizontal();
            GUILayout.Label(title + "：", label, GUILayout.Width(120));
            GUI.SetNextControlName(controlName);
            text = GUILayout.TextField(text, GUILayout.Width(50));
            if (float.TryParse(text, out float v) && v >= 1f && v <= 3f)
            {
                entry.Value = v;
                OrbitController.RequestViewReapply();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawNippleSpraySliders()
        {
            if (HS2OrbitAndExciter.OrgasmNippleSprayBursts != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"連噴次數 {HS2OrbitAndExciter.OrgasmNippleSprayBursts.Value}", GUILayout.Width(100));
                float bn = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayBursts.Value, 2f, 20f);
                int bi = Mathf.RoundToInt(bn);
                if (bi != HS2OrbitAndExciter.OrgasmNippleSprayBursts.Value)
                    HS2OrbitAndExciter.OrgasmNippleSprayBursts.Value = bi;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayBurstInterval != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"間隔 {HS2OrbitAndExciter.OrgasmNippleSprayBurstInterval.Value:F2}s", GUILayout.Width(100));
                float iv = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayBurstInterval.Value, 0.1f, 1.2f);
                if (Mathf.Abs(iv - HS2OrbitAndExciter.OrgasmNippleSprayBurstInterval.Value) > 0.01f)
                    HS2OrbitAndExciter.OrgasmNippleSprayBurstInterval.Value = Mathf.Round(iv * 100f) / 100f;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSpraySpeedStart != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"首噴力道 ×{HS2OrbitAndExciter.OrgasmNippleSpraySpeedStart.Value:F1}", GUILayout.Width(100));
                float ss = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSpraySpeedStart.Value, 0.5f, 3.5f);
                if (Mathf.Abs(ss - HS2OrbitAndExciter.OrgasmNippleSpraySpeedStart.Value) > 0.05f)
                    HS2OrbitAndExciter.OrgasmNippleSpraySpeedStart.Value = Mathf.Round(ss * 10f) / 10f;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSpraySpeedEnd != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"末噴力道 ×{HS2OrbitAndExciter.OrgasmNippleSpraySpeedEnd.Value:F1}", GUILayout.Width(100));
                float se = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSpraySpeedEnd.Value, 0.1f, 2f);
                if (Mathf.Abs(se - HS2OrbitAndExciter.OrgasmNippleSpraySpeedEnd.Value) > 0.05f)
                    HS2OrbitAndExciter.OrgasmNippleSpraySpeedEnd.Value = Mathf.Round(se * 10f) / 10f;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayAmount != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"總噴量 ×{HS2OrbitAndExciter.OrgasmNippleSprayAmount.Value:F1}", GUILayout.Width(100));
                float am = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayAmount.Value, 0.2f, 24f);
                if (Mathf.Abs(am - HS2OrbitAndExciter.OrgasmNippleSprayAmount.Value) > 0.05f)
                    HS2OrbitAndExciter.OrgasmNippleSprayAmount.Value = Mathf.Round(am * 10f) / 10f;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayAmountStart != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"首噴量 ×{HS2OrbitAndExciter.OrgasmNippleSprayAmountStart.Value:F1}", GUILayout.Width(100));
                float as0 = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayAmountStart.Value, 0.2f, 24f);
                if (Mathf.Abs(as0 - HS2OrbitAndExciter.OrgasmNippleSprayAmountStart.Value) > 0.05f)
                    HS2OrbitAndExciter.OrgasmNippleSprayAmountStart.Value = Mathf.Round(as0 * 10f) / 10f;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"末噴量 ×{HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd.Value:F1}", GUILayout.Width(100));
                float ae = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd.Value, 0.1f, 15f);
                if (Mathf.Abs(ae - HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd.Value) > 0.05f)
                    HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd.Value = Mathf.Round(ae * 10f) / 10f;
                GUILayout.EndHorizontal();
            }

            if (HS2OrbitAndExciter.OrgasmNippleSprayOffsetX != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"偏移 X {HS2OrbitAndExciter.OrgasmNippleSprayOffsetX.Value:F2}", GUILayout.Width(100));
                float ox = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayOffsetX.Value, -0.1f, 0.1f);
                if (Mathf.Abs(ox - HS2OrbitAndExciter.OrgasmNippleSprayOffsetX.Value) > 0.001f)
                    HS2OrbitAndExciter.OrgasmNippleSprayOffsetX.Value = Mathf.Round(ox * 100f) / 100f;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayOffsetY != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"偏移 Y {HS2OrbitAndExciter.OrgasmNippleSprayOffsetY.Value:F2}", GUILayout.Width(100));
                float oy = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayOffsetY.Value, -0.1f, 0.1f);
                if (Mathf.Abs(oy - HS2OrbitAndExciter.OrgasmNippleSprayOffsetY.Value) > 0.001f)
                    HS2OrbitAndExciter.OrgasmNippleSprayOffsetY.Value = Mathf.Round(oy * 100f) / 100f;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayOffsetZ != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"前伸 Z {HS2OrbitAndExciter.OrgasmNippleSprayOffsetZ.Value:F2}", GUILayout.Width(100));
                float z = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayOffsetZ.Value, -0.1f, 0.1f);
                if (Mathf.Abs(z - HS2OrbitAndExciter.OrgasmNippleSprayOffsetZ.Value) > 0.001f)
                    HS2OrbitAndExciter.OrgasmNippleSprayOffsetZ.Value = Mathf.Round(z * 100f) / 100f;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayRotX != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"旋轉 X {HS2OrbitAndExciter.OrgasmNippleSprayRotX.Value:F0}°", GUILayout.Width(100));
                float rx = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayRotX.Value, -180f, 180f);
                if (Mathf.Abs(rx - HS2OrbitAndExciter.OrgasmNippleSprayRotX.Value) > 0.5f)
                    HS2OrbitAndExciter.OrgasmNippleSprayRotX.Value = Mathf.Round(rx);
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayRotY != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"旋轉 Y {HS2OrbitAndExciter.OrgasmNippleSprayRotY.Value:F0}°", GUILayout.Width(100));
                float ry = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayRotY.Value, -180f, 180f);
                if (Mathf.Abs(ry - HS2OrbitAndExciter.OrgasmNippleSprayRotY.Value) > 0.5f)
                    HS2OrbitAndExciter.OrgasmNippleSprayRotY.Value = Mathf.Round(ry);
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrgasmNippleSprayRotZ != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"旋轉 Z {HS2OrbitAndExciter.OrgasmNippleSprayRotZ.Value:F0}°", GUILayout.Width(100));
                float rz = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayRotZ.Value, -180f, 180f);
                if (Mathf.Abs(rz - HS2OrbitAndExciter.OrgasmNippleSprayRotZ.Value) > 0.5f)
                    HS2OrbitAndExciter.OrgasmNippleSprayRotZ.Value = Mathf.Round(rz);
                GUILayout.EndHorizontal();
            }
        }
    }
}
