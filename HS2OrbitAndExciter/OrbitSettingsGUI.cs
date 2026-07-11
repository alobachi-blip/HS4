using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// In-game settings window. Toggle with Ctrl+Shift+P. Values write to BepInEx config (auto-saved).
    /// </summary>
    public class OrbitSettingsGUI : MonoBehaviour
    {
        private const KeyCode MenuHotkey = KeyCode.P;
        private const KeyCode Modifier = KeyCode.LeftShift;
        private const KeyCode Modifier2 = KeyCode.LeftControl;
        private bool _visible;
        private bool _needSyncFromConfig; // 每次打開視窗時從 config 同步顯示，確保看到的是已保存的值
        private Rect _windowRect = new Rect(100, 100, 480, 640);
        private Vector2 _scroll;
        private GUIStyle? _labelStyle;
        private bool _stylesInitialized;
        // Per-field strings so TextField isn't reset from config every frame (which hides typing)
        private string _orbitTimeStr = "";
        private string _orbitCountRandomStr = "";
        private string _orbitCountPoseStr = "";
        private string _checkpointTimeoutStr = "";
        private string _excitementDelayStr = "";
        private string _feelAddPerSecStr = "";
        private string _orbitDistHeadStr = "";
        private string _orbitDistChestStr = "";
        private string _orbitDistPelvisStr = "";
        private string _autoAssistMinIntervalStr = "";

        private bool _lastOverrideFaintness;

        private void Update()
        {
            // Hotkey moved to OnGUI (Event.current) so it works when game/UI has focus and after window is closed
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            // Handle hotkey in GUI event so it isn't consumed by game/UI; works even when window is closed
            if (Event.current != null && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == MenuHotkey && Event.current.control && Event.current.shift)
            {
                _visible = !_visible;
                if (_visible) _needSyncFromConfig = true;
                Event.current.Use();
            }
            if (!_visible) return;

            // 每次打開視窗時從 config 同步到輸入框，顯示的是已保存的設定值
            if (Event.current != null && Event.current.type == EventType.Layout && _needSyncFromConfig && HS2OrbitAndExciter.OrbitTimePer360 != null)
            {
                _orbitTimeStr = HS2OrbitAndExciter.OrbitTimePer360.Value.ToString("F1");
                _orbitCountRandomStr = (HS2OrbitAndExciter.OrbitCountBeforeRandom?.Value ?? 0).ToString();
                _orbitCountPoseStr = (HS2OrbitAndExciter.OrbitCountBeforePoseChange?.Value ?? 2).ToString();
                _checkpointTimeoutStr = (HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds?.Value ?? 2f).ToString("F1");
                _autoAssistMinIntervalStr = (HS2OrbitAndExciter.AutoAssistMinIntervalSeconds?.Value ?? 1f).ToString("F2");
                _excitementDelayStr = (HS2OrbitAndExciter.ExcitementTriggerDelaySeconds?.Value ?? 0f).ToString("F1");
                _feelAddPerSecStr = (HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit?.Value ?? 0.1f).ToString("F2");
                _orbitDistHeadStr = (HS2OrbitAndExciter.OrbitDistanceHead?.Value ?? 0.3f).ToString("F2");
                _orbitDistChestStr = (HS2OrbitAndExciter.OrbitDistanceChest?.Value ?? 0.3f).ToString("F2");
                _orbitDistPelvisStr = (HS2OrbitAndExciter.OrbitDistancePelvis?.Value ?? 0.3f).ToString("F2");
                _lastOverrideFaintness = HS2OrbitAndExciter.OverrideFaintness?.Value ?? false;
                _needSyncFromConfig = false;
            }

            InitStyles();
            float maxH = Mathf.Max(280f, Screen.height - 48f);
            if (_windowRect.height > maxH)
                _windowRect.height = maxH;
            if (_windowRect.width < 420f)
                _windowRect.width = 420f;
            _windowRect = GUILayout.Window(9001, _windowRect, DrawWindow, "環視與興奮條 — 設定");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            float scrollH = Mathf.Max(160f, _windowRect.height - 56f);
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.Height(scrollH));

            GUILayout.BeginVertical(GUI.skin.box);
            foreach (var line in PluginBuildIdentity.GetGuiLines())
                GUILayout.Label(line, _labelStyle ?? GUI.skin.label);
            GUILayout.EndVertical();

            GUILayout.Label("環視相機", GUI.skin.box);
            GUILayout.Label("名詞：單向繞水平 360°＝一次「旋轉」；去程加回程＝一次「迴轉」（約 2×單向秒數）。", _labelStyle ?? GUI.skin.label);
            if (HS2OrbitAndExciter.OrbitStatusHudEnabled != null)
            {
                HS2OrbitAndExciter.OrbitStatusHudEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.OrbitStatusHudEnabled.Value,
                    " 啟用左下角環視狀態面板（繁中；環視開啟時 Ctrl+Shift+I 切換顯示）");
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
                GUILayout.Label("單向水平 360° 所需秒數：", _labelStyle, GUILayout.Width(220));
                GUI.SetNextControlName("OrbitTimePer360");
                _orbitTimeStr = GUILayout.TextField(_orbitTimeStr, GUILayout.Width(60));
                if (float.TryParse(_orbitTimeStr, out float v) && v > 0.1f && v <= 120f)
                    HS2OrbitAndExciter.OrbitTimePer360.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrbitCountBeforeRandom != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("每幾次旋轉亂數焦點＋水平角（0＝關閉亂數；換裝改每迴轉一次）：", _labelStyle, GUILayout.Width(260));
                GUI.SetNextControlName("OrbitCountBeforeRandom");
                _orbitCountRandomStr = GUILayout.TextField(_orbitCountRandomStr, GUILayout.Width(60));
                if (int.TryParse(_orbitCountRandomStr, out int v) && v >= 0 && v <= 99)
                    HS2OrbitAndExciter.OrbitCountBeforeRandom.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrbitCountBeforePoseChange != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("每幾次迴轉換姿勢（需開啟下方開關）：", _labelStyle, GUILayout.Width(260));
                GUI.SetNextControlName("OrbitCountBeforePoseChange");
                _orbitCountPoseStr = GUILayout.TextField(_orbitCountPoseStr, GUILayout.Width(60));
                if (int.TryParse(_orbitCountPoseStr, out int v) && v >= 1 && v <= 99)
                    HS2OrbitAndExciter.OrbitCountBeforePoseChange.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.ChangePoseOnCycle != null)
                HS2OrbitAndExciter.ChangePoseOnCycle.Value = GUILayout.Toggle(HS2OrbitAndExciter.ChangePoseOnCycle.Value, " 依上方面數，每滿幾次迴轉就換姿勢");
            if (HS2OrbitAndExciter.ClothesChangeEnabled != null)
                HS2OrbitAndExciter.ClothesChangeEnabled.Value = GUILayout.Toggle(HS2OrbitAndExciter.ClothesChangeEnabled.Value, " 依上方「每幾次旋轉」切換衣物階段（N＝0 時改為每迴轉一次）");
            if (HS2OrbitAndExciter.OrbitAutoActionEnabled != null)
                HS2OrbitAndExciter.OrbitAutoActionEnabled.Value = GUILayout.Toggle(HS2OrbitAndExciter.OrbitAutoActionEnabled.Value, " 環視開著時，自動幫你選下一個動作（少自己點）");
            if (HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("卡關超過幾秒就自動往下一階段（0＝不強制跳關）：", _labelStyle, GUILayout.Width(260));
                GUI.SetNextControlName("OrbitCheckpointTimeout");
                _checkpointTimeoutStr = GUILayout.TextField(_checkpointTimeoutStr, GUILayout.Width(60));
                if (float.TryParse(_checkpointTimeoutStr, out float v) && v >= 0f && v <= 60f)
                    HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.AutoAssistMinIntervalSeconds != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("自動協助最短間隔（秒；0＝最積極、最容易搶選單）：", _labelStyle, GUILayout.Width(260));
                GUI.SetNextControlName("AutoAssistMinInterval");
                _autoAssistMinIntervalStr = GUILayout.TextField(_autoAssistMinIntervalStr, GUILayout.Width(60));
                if (float.TryParse(_autoAssistMinIntervalStr, out float iv) && iv >= 0f && iv <= 30f)
                    HS2OrbitAndExciter.AutoAssistMinIntervalSeconds.Value = iv;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.EnableAfterProcAssistPostfixFallback != null)
            {
                GUILayout.Label("僅在「停很久都不會自動往下走」時再開；一般請維持關閉。", _labelStyle);
                HS2OrbitAndExciter.EnableAfterProcAssistPostfixFallback.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.EnableAfterProcAssistPostfixFallback.Value,
                    " 相容模式：每段流程結束後再補一次自動旗標（舊版行為）");
            }
            GUILayout.Label("焦點距離（單位：全身長倍率，1～3，設定會記錄；輸入後立即套用）", _labelStyle);
            if (HS2OrbitAndExciter.OrbitDistanceHead != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("頭部焦點距離:", _labelStyle, GUILayout.Width(120));
                GUI.SetNextControlName("OrbitDistHead");
                _orbitDistHeadStr = GUILayout.TextField(_orbitDistHeadStr, GUILayout.Width(50));
                if (float.TryParse(_orbitDistHeadStr, out float v) && v >= 1f && v <= 3f)
                {
                    HS2OrbitAndExciter.OrbitDistanceHead.Value = v;
                    OrbitController.RequestViewReapply();
                }
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrbitDistanceChest != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("胸部焦點距離:", _labelStyle, GUILayout.Width(120));
                GUI.SetNextControlName("OrbitDistChest");
                _orbitDistChestStr = GUILayout.TextField(_orbitDistChestStr, GUILayout.Width(50));
                if (float.TryParse(_orbitDistChestStr, out float v) && v >= 1f && v <= 3f)
                {
                    HS2OrbitAndExciter.OrbitDistanceChest.Value = v;
                    OrbitController.RequestViewReapply();
                }
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrbitDistancePelvis != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("骨盆焦點距離:", _labelStyle, GUILayout.Width(120));
                GUI.SetNextControlName("OrbitDistPelvis");
                _orbitDistPelvisStr = GUILayout.TextField(_orbitDistPelvisStr, GUILayout.Width(50));
                if (float.TryParse(_orbitDistPelvisStr, out float v) && v >= 1f && v <= 3f)
                {
                    HS2OrbitAndExciter.OrbitDistancePelvis.Value = v;
                    OrbitController.RequestViewReapply();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("角色狀態", GUI.skin.box);
            if (HS2OrbitAndExciter.OverrideFaintness != null)
            {
                bool newFaintness = GUILayout.Toggle(HS2OrbitAndExciter.OverrideFaintness.Value, " 強制脫力開／關（影響姿勢表與鏡頭）");
                HS2OrbitAndExciter.OverrideFaintness.Value = newFaintness;
                if (newFaintness != _lastOverrideFaintness)
                {
                    _lastOverrideFaintness = newFaintness;
                    OrbitHelpers.SetGameFaintnessAndRequestViewReapply(newFaintness);
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("興奮條", GUI.skin.box);
            if (HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("興奮條上升速度（0=僅滑鼠，0.01=100秒滿，0.1=10秒滿）:", _labelStyle, GUILayout.Width(320));
                GUI.SetNextControlName("FeelAddPerSec");
                _feelAddPerSecStr = GUILayout.TextField(_feelAddPerSecStr, GUILayout.Width(60));
                if (float.TryParse(_feelAddPerSecStr, out float v) && v >= 0f && v <= 5f)
                {
                    if (v > 0f && v < 0.01f) v = 0.01f; // 最慢 = 100 秒滿
                    HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit.Value = v;
                }
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.ExcitementTriggerDelaySeconds != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("滿量表後幾秒才觸發 (0=馬上，點滑鼠仍馬上):", _labelStyle, GUILayout.Width(260));
                GUI.SetNextControlName("ExcitementTriggerDelay");
                _excitementDelayStr = GUILayout.TextField(_excitementDelayStr, GUILayout.Width(60));
                if (float.TryParse(_excitementDelayStr, out float v) && v >= 0f && v <= 10f)
                {
                    HS2OrbitAndExciter.ExcitementTriggerDelaySeconds.Value = v;
                    Patches.ExciterState.DelaySecondsAtFull = v;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("H 場景熱鍵", GUI.skin.box);
            GUILayout.Label("單鍵操作（勿同時按 Ctrl／Shift／Alt；環視開關不影響）：", _labelStyle);
            GUILayout.Label(
                OrbitManualHotkeys.HudLegend + " — G 換女主、H 換套裝、J 亂數穿著、K 切姿勢鏡頭、L 換姿勢、T 刺青、B 胸回復",
                _labelStyle);
            GUILayout.Label(
                "G 池排除同性格卡；<30 秒快換降權、≥60 秒久留優先；左下角面板顯示 G 池統計與上場秒數",
                _labelStyle);
            GUILayout.Label("Q／W／E 切環視焦點（頭／胸／骨盆）；Shift＋Q／W／E 切第二女角", _labelStyle);
            GUILayout.Label(
                OrbitManualHotkeys.PregnancyHudLegend + " — Y/U 為 PregnancyPlus；R 由本插件強制清腹（含 HS2 H 膨脹）",
                _labelStyle);

            GUILayout.Space(8);
            GUILayout.Label("高潮特效", GUI.skin.box);
            GUILayout.Label(
                "女高潮時觸發：刺青／胸部變大／乳頭射精（複用男性射精 Obi／粒子）。左下角 HUD「高潮 …」列顯示狀態。",
                _labelStyle);

            if (HS2OrbitAndExciter.OrgasmTattooEnabled != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("刺青", GUILayout.Width(100));
                bool tattooOn = HS2OrbitAndExciter.OrgasmTattooEnabled.Value;
                bool next = GUILayout.Toggle(tattooOn, tattooOn ? $"開（T貼）×{OrbitOrgasmTattoo.Count}" : "關（⇧T）");
                if (next != tattooOn)
                    HS2OrbitAndExciter.OrgasmTattooEnabled.Value = next;
                GUILayout.EndHorizontal();
                GUILayout.Label("H：T＝開＋依序貼一張；Shift+T＝關。貼花＋皮膚 paint，不進飾品欄；換衣重掛；G 換角清空", _labelStyle);
            }

            if (HS2OrbitAndExciter.OrgasmBustGrowEnabled != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("胸部變大", GUILayout.Width(100));
                bool bustOn = HS2OrbitAndExciter.OrgasmBustGrowEnabled.Value;
                bool bustNext = GUILayout.Toggle(bustOn, bustOn ? "開" : "關");
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
                GUILayout.Label("相對目前胸サイズ倍率放大；B 鍵回復進 H／換角時的基準胸圍", _labelStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("回復胸部（B）", GUILayout.Width(140)))
                    OrbitOrgasmBustGrowth.TryRestore(OrbitController.TryGetHScene());
                GUILayout.Label(OrbitOrgasmBustGrowth.HudStatus, _labelStyle ?? GUI.skin.label);
                GUILayout.EndHorizontal();
            }

            if (HS2OrbitAndExciter.OrgasmNippleSprayEnabled != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("乳頭射精", GUILayout.Width(100));
                bool nipOn = HS2OrbitAndExciter.OrgasmNippleSprayEnabled.Value;
                bool nipNext = GUILayout.Toggle(nipOn, nipOn ? "開" : "關");
                if (nipNext != nipOn)
                    HS2OrbitAndExciter.OrgasmNippleSprayEnabled.Value = nipNext;
                GUILayout.Label(OrbitOrgasmNippleSpray.HudStatus, _labelStyle ?? GUI.skin.label);
                GUILayout.EndHorizontal();
                GUILayout.Label("複用男性射精；潮吹式多段連噴，開頭較強、之後遞減。偏移／旋轉下次高潮自動套用。", _labelStyle);

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
                    float am = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayAmount.Value, 0.2f, 8f);
                    if (Mathf.Abs(am - HS2OrbitAndExciter.OrgasmNippleSprayAmount.Value) > 0.05f)
                        HS2OrbitAndExciter.OrgasmNippleSprayAmount.Value = Mathf.Round(am * 10f) / 10f;
                    GUILayout.EndHorizontal();
                }
                if (HS2OrbitAndExciter.OrgasmNippleSprayAmountStart != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"首噴量 ×{HS2OrbitAndExciter.OrgasmNippleSprayAmountStart.Value:F1}", GUILayout.Width(100));
                    float as0 = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayAmountStart.Value, 0.2f, 8f);
                    if (Mathf.Abs(as0 - HS2OrbitAndExciter.OrgasmNippleSprayAmountStart.Value) > 0.05f)
                        HS2OrbitAndExciter.OrgasmNippleSprayAmountStart.Value = Mathf.Round(as0 * 10f) / 10f;
                    GUILayout.EndHorizontal();
                }
                if (HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"末噴量 ×{HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd.Value:F1}", GUILayout.Width(100));
                    float ae = GUILayout.HorizontalSlider(HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd.Value, 0.1f, 5f);
                    if (Mathf.Abs(ae - HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd.Value) > 0.05f)
                        HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd.Value = Mathf.Round(ae * 10f) / 10f;
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
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("重建乳頭噴口", GUILayout.Width(140)))
                    OrbitOrgasmNippleSpray.ForceRebuild(OrbitController.TryGetHScene());
                GUILayout.Label("調完偏移／旋轉後按此立即套用", _labelStyle ?? GUI.skin.label);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("設定值會自動儲存，保持至下次變更。", _labelStyle);
            GUILayout.Label("環視：⌃⇧O 開關；⌃⇧I 狀態面板；⌃⇧P 本視窗。頂端為版本對照。", _labelStyle);

            GUILayout.EndScrollView();

            if (GUILayout.Button("關閉"))
                _visible = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }
    }
}
