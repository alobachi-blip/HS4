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
        private Rect _windowRect = new Rect(100, 100, 480, 740);
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
            _windowRect = GUILayout.Window(9001, _windowRect, DrawWindow, "環視與興奮條 — 設定");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

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
                OrbitManualHotkeys.HudLegend + " — G 換女主、H 換套裝、J 亂數穿著、K 切姿勢鏡頭、L 換姿勢、T 高潮刺青開關",
                _labelStyle);
            GUILayout.Label(
                "G 池排除同性格卡；<30 秒快換降權、≥60 秒久留優先；左下角面板顯示 G 池統計與上場秒數",
                _labelStyle);
            GUILayout.Label("Q／W／E 切環視焦點（頭／胸／骨盆）；Shift＋Q／W／E 切第二女角", _labelStyle);
            GUILayout.Label(
                OrbitManualHotkeys.PregnancyHudLegend + " — Y/U 為 PregnancyPlus；R 由本插件強制清腹（含 HS2 H 膨脹）",
                _labelStyle);
            if (HS2OrbitAndExciter.OrgasmTattooEnabled != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("高潮刺青", GUILayout.Width(100));
                bool tattooOn = HS2OrbitAndExciter.OrgasmTattooEnabled.Value;
                bool next = GUILayout.Toggle(tattooOn, tattooOn ? $"開（T）×{OrbitOrgasmTattoo.Count}" : "關（T）");
                if (next != tattooOn)
                    HS2OrbitAndExciter.OrgasmTattooEnabled.Value = next;
                GUILayout.EndHorizontal();
                GUILayout.Label("st_paint 刺青：寫入皮膚 paint＋3D 貼花（非飾品欄）；T 開啟會立刻加一枚；大腿→臉", _labelStyle);
            }

            GUILayout.Space(8);
            GUILayout.Label("設定值會自動儲存，保持至下次變更。", _labelStyle);
            GUILayout.Label("環視：⌃⇧O 開關；⌃⇧I 狀態面板；⌃⇧P 本視窗。頂端為版本對照。", _labelStyle);
            if (GUILayout.Button("關閉"))
                _visible = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }
    }
}
