using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// In-game settings. Ctrl+Shift+P. Sliders for numeric values; hotkeys listed first.
    /// </summary>
    public class OrbitSettingsGUI : MonoBehaviour
    {
        private const KeyCode MenuHotkey = KeyCode.P;
        /// <summary>上下限跨度超過此倍率時改用數值框（並顯示上下限）。</summary>
        private const float MaxSliderSpanRatio = 80f;

        private bool _visible;
        private Rect _windowRect = new Rect(80, 60, 520, 700);
        private Vector2 _scroll;
        private GUIStyle? _labelStyle;
        private GUIStyle? _sectionStyle;
        private bool _stylesInitialized;
        private bool _lastOverrideFaintness;

        private static OrbitSettingsGUI? Instance { get; set; }

        /// <summary>Ctrl+Shift+P 設定窗是否開啟（IMGUI 不經 EventSystem，需另查）。</summary>
        internal static bool IsVisible => Instance != null && Instance._visible;

        private void OnEnable() => Instance = this;
        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _sectionStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4)
            };
            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (Event.current != null && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == MenuHotkey && Event.current.control && Event.current.shift)
            {
                _visible = !_visible;
                Event.current.Use();
            }
            if (!_visible) return;

            InitStyles();
            float maxH = Mathf.Max(280f, Screen.height - 48f);
            if (_windowRect.height > maxH)
                _windowRect.height = maxH;
            if (_windowRect.width < 480f)
                _windowRect.width = 520f;
            _windowRect = GUILayout.Window(
                9001,
                _windowRect,
                DrawWindow,
                "環視協助 — 設定（Ctrl+Shift+P）");
        }

        private void DrawWindow(int id)
        {
            var label = _labelStyle ?? GUI.skin.label;
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            float scrollH = Mathf.Max(160f, _windowRect.height - 56f);
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.Height(scrollH));

            // ─── 熱鍵（開頭）───────────────────────────────────
            Section("可用熱鍵");
            GUILayout.Label(OrbitManualHotkeys.SettingsHotkeysBlock, label);

            // ─── 狀態面板 ───────────────────────────────────────
            Section("狀態面板（左下角）");
            if (HS2OrbitAndExciter.OrbitStatusHudEnabled != null)
            {
                HS2OrbitAndExciter.OrbitStatusHudEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.OrbitStatusHudEnabled.Value,
                    " 顯示狀態面板（環視／語音巡禮進行中）");
            }
            if (HS2OrbitAndExciter.OrbitStatusHudEnabled?.Value == true)
            {
                bool pv = OrbitStatusHud.GetPanelVisible();
                pv = GUILayout.Toggle(pv, " 目前正在顯示");
                OrbitStatusHud.SetPanelVisible(pv);
            }

            // ─── 環視相機 ───────────────────────────────────────
            Section("環視相機");
            GUILayout.Label("開啟協助後相機自動環繞；關掉協助才恢復手動調視角。", label);

            DrawFloatControl(
                "轉一圈要幾秒",
                "越小越快",
                HS2OrbitAndExciter.OrbitTimePer360,
                0.5f, 60f, 0.1f, "F1", "秒");

            DrawIntControl(
                "幾圈後亂數換焦點／角度",
                "0＝不亂數",
                HS2OrbitAndExciter.OrbitCountBeforeRandom,
                0, 20);

            if (HS2OrbitAndExciter.ClothesChangeEnabled != null)
            {
                HS2OrbitAndExciter.ClothesChangeEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.ClothesChangeEnabled.Value,
                    " 依上面圈數順便換衣物階段（圈數為 0 時改為來回一趟換一次）");
            }

            GUILayout.Space(4);
            GUILayout.Label("焦點距離（相對全身長度）", label);
            DrawFloatControl("頭部", "0＝極近特寫", HS2OrbitAndExciter.OrbitDistanceHead, 0f, 3f, 0.01f, "F2", null, requestViewReapply: true);
            DrawFloatControl("胸部", "0＝極近特寫", HS2OrbitAndExciter.OrbitDistanceChest, 0f, 3f, 0.01f, "F2", null, requestViewReapply: true);
            DrawFloatControl("骨盆", "0＝極近特寫", HS2OrbitAndExciter.OrbitDistancePelvis, 0f, 3f, 0.01f, "F2", null, requestViewReapply: true);

            if (HS2OrbitAndExciter.OrbitCircleZoomEnabled != null)
            {
                HS2OrbitAndExciter.OrbitCircleZoomEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.OrbitCircleZoomEnabled.Value,
                    " 每圈隨機拉近／拉遠");
            }
            if (HS2OrbitAndExciter.OrbitCircleZoomEnabled?.Value == true)
            {
                DrawFloatControl("拉近倍率", "0＝極近；與拉遠倒掛會自動對調", HS2OrbitAndExciter.OrbitZoomNearMult, 0f, 3f, 0.01f, "F2");
                DrawFloatControl("拉遠倍率", "越大越遠；與拉近倒掛會自動對調", HS2OrbitAndExciter.OrbitZoomFarMult, 0f, 3f, 0.01f, "F2");
            }

            // ─── 流程 ───────────────────────────────────────────
            Section("流程");
            if (HS2OrbitAndExciter.CumflationEnabled != null)
            {
                HS2OrbitAndExciter.CumflationEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.CumflationEnabled.Value,
                    " 內射時肚子脹一級；愛撫／女女落地消一級（需 PregnancyPlus）");
            }

            // ─── 脫力 ───────────────────────────────────────────
            Section("脫力");
            if (HS2OrbitAndExciter.OverrideFaintness != null)
            {
                bool newFaintness = GUILayout.Toggle(
                    HS2OrbitAndExciter.OverrideFaintness.Value,
                    " 強制脫力（影響可用姿勢與鏡頭）");
                HS2OrbitAndExciter.OverrideFaintness.Value = newFaintness;
                if (newFaintness != _lastOverrideFaintness)
                {
                    _lastOverrideFaintness = newFaintness;
                    OrbitHelpers.SetGameFaintnessAndRequestViewReapply(newFaintness);
                }
            }

            // ─── 感度 ───────────────────────────────────────────
            Section("感度");
            DrawFloatControl(
                "每秒自動加感度",
                "0＝只用遊戲本身；約 0.1＝十秒左右滿條",
                HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit,
                0f, 1f, 0.001f, "F3");
            DrawFloatControl(
                "滿條後再等多久才自動高潮",
                "0＝立刻；手動點擊仍可立刻高潮",
                HS2OrbitAndExciter.ExcitementTriggerDelaySeconds,
                0f, 10f, 0.1f, "F1", "秒",
                onChanged: v => Patches.ExciterState.DelaySecondsAtFull = v);

            // ─── 語音巡禮 ───────────────────────────────────────
            Section("語音巡禮");
            GUILayout.Label("依高潮／射精依序切換音庫階段。", label);
            if (HS2OrbitAndExciter.VoiceTourEnabled != null)
            {
                HS2OrbitAndExciter.VoiceTourEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourEnabled.Value,
                    " 啟用語音巡禮");
            }
            DrawIntControl(
                "每階段要幾次才進下一段",
                null,
                HS2OrbitAndExciter.VoiceTourHitsPerStage,
                1, 10);
            if (HS2OrbitAndExciter.VoiceTourLoop != null)
            {
                HS2OrbitAndExciter.VoiceTourLoop.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourLoop.Value,
                    " 最後一段後從頭循環");
            }
            if (HS2OrbitAndExciter.VoiceTourPersistProgress != null)
            {
                HS2OrbitAndExciter.VoiceTourPersistProgress.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourPersistProgress.Value,
                    " 依角色記住進度");
            }
            if (HS2OrbitAndExciter.VoiceTourResetOnNewH != null)
            {
                HS2OrbitAndExciter.VoiceTourResetOnNewH.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourResetOnNewH.Value,
                    " 每次進 H 都從第一段重來");
            }
            if (HS2OrbitAndExciter.VoiceTourCountHoushiMaleFinish != null)
            {
                HS2OrbitAndExciter.VoiceTourCountHoushiMaleFinish.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.VoiceTourCountHoushiMaleFinish.Value,
                    " 侍奉射精也算一次（插入內射一律算）");
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重置目前角色進度", GUILayout.Width(160)))
                OrbitVoiceTour.ResetCurrentCharacterProgress();
            GUILayout.Label(
                $"現在：{OrbitVoiceTour.CurrentLabelZh}  {OrbitVoiceTour.StageIndex + 1}/{OrbitVoiceTour.StageCount}",
                label);
            GUILayout.EndHorizontal();

            // ─── 高潮特效 ───────────────────────────────────────
            Section("高潮特效");

            if (HS2OrbitAndExciter.OrgasmTattooEnabled != null)
            {
                HS2OrbitAndExciter.OrgasmTattooEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.OrgasmTattooEnabled.Value,
                    $" 高潮刺青（已貼 {OrbitOrgasmTattoo.Count} 張；T 貼下一張）");
                if (HS2OrbitAndExciter.OrgasmTattooEnabled.Value)
                {
                    DrawIntControl("最多張數", null, HS2OrbitAndExciter.OrgasmTattooMaxCount, 1, 64);
                    DrawFloatControl("最小倍率", null, HS2OrbitAndExciter.OrgasmTattooScaleMin, 1f, 20f, 0.1f, "F1", "×");
                    DrawFloatControl("最大倍率", null, HS2OrbitAndExciter.OrgasmTattooScaleMax, 1f, 20f, 0.1f, "F1", "×");
                }
            }

            if (HS2OrbitAndExciter.OrgasmBustGrowEnabled != null)
            {
                HS2OrbitAndExciter.OrgasmBustGrowEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.OrgasmBustGrowEnabled.Value,
                    " 高潮胸部變大");
                if (HS2OrbitAndExciter.OrgasmBustGrowEnabled.Value)
                {
                    DrawFloatControl("每次增大", null, HS2OrbitAndExciter.OrgasmBustGrowPercent, 0f, 50f, 1f, "F0", "%");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("立刻回復胸部（B）", GUILayout.Width(160)))
                        OrbitOrgasmBustGrowth.TryRestore(OrbitController.TryGetHScene());
                    GUILayout.Label(OrbitOrgasmBustGrowth.HudStatus, label);
                    GUILayout.EndHorizontal();
                }
            }

            if (HS2OrbitAndExciter.OrgasmNippleSprayEnabled != null)
            {
                HS2OrbitAndExciter.OrgasmNippleSprayEnabled.Value = GUILayout.Toggle(
                    HS2OrbitAndExciter.OrgasmNippleSprayEnabled.Value,
                    " 乳頭潮吹");
                if (HS2OrbitAndExciter.OrgasmNippleSprayEnabled.Value)
                {
                    GUILayout.Label(OrbitOrgasmNippleSpray.HudStatus, label);
                    if (HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm != null)
                    {
                        bool nativeOn = HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm.Value;
                        bool nativeNext = GUILayout.Toggle(
                            nativeOn,
                            nativeOn ? " 節奏：跟遊戲潮吹" : " 節奏：自訂連噴（先強後弱）");
                        HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm.Value = nativeNext;
                    }

                    bool nativeRhythm = HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm?.Value ?? false;
                    if (!nativeRhythm)
                        DrawNippleSpraySliders();
                    else
                        GUILayout.Label("（跟遊戲節奏時，下面連噴參數不會用到）", label);

                    DrawNipplePoseSliders();

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("重建噴口", GUILayout.Width(120)))
                        OrbitOrgasmNippleSpray.ForceRebuild(OrbitController.TryGetHScene());
                    if (GUILayout.Button("重設預設值", GUILayout.Width(120)))
                        OrbitOrgasmNippleSpray.ResetSettingsToDefaults(OrbitController.TryGetHScene());
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("變更會自動儲存。", label);

            GUILayout.EndScrollView();

            if (GUILayout.Button("關閉"))
                _visible = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }

        private void Section(string title)
        {
            GUILayout.Space(8);
            GUILayout.Label(title, _sectionStyle ?? GUI.skin.box);
        }

        /// <summary>
        /// 跨度合理 → 拉 bar；跨度太大 → 數值框並顯示上下限。
        /// </summary>
        private void DrawFloatControl(
            string title,
            string? hint,
            BepInEx.Configuration.ConfigEntry<float>? entry,
            float min,
            float max,
            float step,
            string format,
            string? unit = null,
            bool requestViewReapply = false,
            System.Action<float>? onChanged = null)
        {
            if (entry == null) return;
            var label = _labelStyle ?? GUI.skin.label;
            float cur = Mathf.Clamp(entry.Value, min, max);
            string unitSuffix = string.IsNullOrEmpty(unit) ? "" : " " + unit;
            string valueText = cur.ToString(format) + unitSuffix;

            // 跨度太大（例如 0.1～上千）才改數值框；一般參數一律拉 bar。
            float span = max - min;
            bool useSlider = span <= 200f
                || (min > 0.0001f && max / min <= MaxSliderSpanRatio);

            if (useSlider)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{title}  {valueText}", label, GUILayout.Width(200));
                float next = GUILayout.HorizontalSlider(cur, min, max);
                if (step > 0f)
                    next = Mathf.Round(next / step) * step;
                next = Mathf.Clamp(next, min, max);
                if (Mathf.Abs(next - entry.Value) > step * 0.4f)
                {
                    entry.Value = next;
                    onChanged?.Invoke(next);
                    if (requestViewReapply)
                        OrbitController.RequestViewReapply();
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{min.ToString(format)}", GUILayout.Width(48));
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(hint))
                    GUILayout.Label(hint, label);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{max.ToString(format)}", GUILayout.Width(48));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{title}（{min.ToString(format)}～{max.ToString(format)}）", label, GUILayout.Width(260));
                string field = GUILayout.TextField(cur.ToString(format), GUILayout.Width(72));
                if (float.TryParse(field, out float parsed))
                {
                    parsed = Mathf.Clamp(parsed, min, max);
                    if (Mathf.Abs(parsed - entry.Value) > 0.0001f)
                    {
                        entry.Value = parsed;
                        onChanged?.Invoke(parsed);
                        if (requestViewReapply)
                            OrbitController.RequestViewReapply();
                    }
                }
                GUILayout.Label(unitSuffix, GUILayout.Width(28));
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(hint))
                    GUILayout.Label(hint, label);
            }
        }

        private void DrawIntControl(
            string title,
            string? hint,
            BepInEx.Configuration.ConfigEntry<int>? entry,
            int min,
            int max)
        {
            if (entry == null) return;
            var label = _labelStyle ?? GUI.skin.label;
            int cur = Mathf.Clamp(entry.Value, min, max);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{title}  {cur}", label, GUILayout.Width(200));
            float slid = GUILayout.HorizontalSlider(cur, min, max);
            int next = Mathf.RoundToInt(slid);
            next = Mathf.Clamp(next, min, max);
            if (next != entry.Value)
                entry.Value = next;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{min}", GUILayout.Width(48));
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(hint))
                GUILayout.Label(hint, label);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{max}", GUILayout.Width(48));
            GUILayout.EndHorizontal();
        }

        private void DrawNippleSpraySliders()
        {
            DrawIntControl("連噴次數", null, HS2OrbitAndExciter.OrgasmNippleSprayBursts, 2, 20);
            DrawFloatControl("間隔", null, HS2OrbitAndExciter.OrgasmNippleSprayBurstInterval, 0.1f, 1.2f, 0.01f, "F2", "秒");
            DrawFloatControl("首噴力道", null, HS2OrbitAndExciter.OrgasmNippleSpraySpeedStart, 0.5f, 3.5f, 0.1f, "F1", "×");
            DrawFloatControl("末噴力道", null, HS2OrbitAndExciter.OrgasmNippleSpraySpeedEnd, 0.1f, 2f, 0.1f, "F1", "×");
            DrawFloatControl("總噴量", null, HS2OrbitAndExciter.OrgasmNippleSprayAmount, 0.2f, 24f, 0.1f, "F1", "×");
            DrawFloatControl("首噴量", null, HS2OrbitAndExciter.OrgasmNippleSprayAmountStart, 0.2f, 24f, 0.1f, "F1", "×");
            DrawFloatControl("末噴量", null, HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd, 0.1f, 15f, 0.1f, "F1", "×");
        }

        private void DrawNipplePoseSliders()
        {
            GUILayout.Label("噴口位置／角度（下次高潮套用）", _labelStyle ?? GUI.skin.label);
            DrawFloatControl("偏移 X", null, HS2OrbitAndExciter.OrgasmNippleSprayOffsetX, -0.1f, 0.1f, 0.01f, "F2");
            DrawFloatControl("偏移 Y", null, HS2OrbitAndExciter.OrgasmNippleSprayOffsetY, -0.1f, 0.1f, 0.01f, "F2");
            DrawFloatControl("前伸 Z", null, HS2OrbitAndExciter.OrgasmNippleSprayOffsetZ, -0.1f, 0.1f, 0.01f, "F2");
            DrawFloatControl("旋轉 X", null, HS2OrbitAndExciter.OrgasmNippleSprayRotX, -180f, 180f, 1f, "F0", "°");
            DrawFloatControl("旋轉 Y", null, HS2OrbitAndExciter.OrgasmNippleSprayRotY, -180f, 180f, 1f, "F0", "°");
            DrawFloatControl("旋轉 Z", null, HS2OrbitAndExciter.OrgasmNippleSprayRotZ, -180f, 180f, 1f, "F0", "°");
        }
    }
}
