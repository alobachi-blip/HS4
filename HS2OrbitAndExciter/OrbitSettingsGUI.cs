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
        private Rect _windowRect = new Rect(100, 100, 440, 560);
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
            if (HS2OrbitAndExciter.OrbitTimePer360 != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("水平繞一圈所需秒數：", _labelStyle, GUILayout.Width(220));
                GUI.SetNextControlName("OrbitTimePer360");
                _orbitTimeStr = GUILayout.TextField(_orbitTimeStr, GUILayout.Width(60));
                if (float.TryParse(_orbitTimeStr, out float v) && v > 0.1f && v <= 120f)
                    HS2OrbitAndExciter.OrbitTimePer360.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrbitCountBeforeRandom != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("繞幾圈後亂數焦點與角度（0＝不要亂數）：", _labelStyle, GUILayout.Width(220));
                GUI.SetNextControlName("OrbitCountBeforeRandom");
                _orbitCountRandomStr = GUILayout.TextField(_orbitCountRandomStr, GUILayout.Width(60));
                if (int.TryParse(_orbitCountRandomStr, out int v) && v >= 0 && v <= 99)
                    HS2OrbitAndExciter.OrbitCountBeforeRandom.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrbitCountBeforePoseChange != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("繞幾圈後換姿勢（需開啟下方開關）：", _labelStyle, GUILayout.Width(220));
                GUI.SetNextControlName("OrbitCountBeforePoseChange");
                _orbitCountPoseStr = GUILayout.TextField(_orbitCountPoseStr, GUILayout.Width(60));
                if (int.TryParse(_orbitCountPoseStr, out int v) && v >= 1 && v <= 99)
                    HS2OrbitAndExciter.OrbitCountBeforePoseChange.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.ChangePoseOnCycle != null)
                HS2OrbitAndExciter.ChangePoseOnCycle.Value = GUILayout.Toggle(HS2OrbitAndExciter.ChangePoseOnCycle.Value, " 依上方面數，每繞滿幾圈就換姿勢");
            if (HS2OrbitAndExciter.ClothesChangeEnabled != null)
                HS2OrbitAndExciter.ClothesChangeEnabled.Value = GUILayout.Toggle(HS2OrbitAndExciter.ClothesChangeEnabled.Value, " 每繞滿一圈就切換衣物階段（脫／穿序列）");
            if (HS2OrbitAndExciter.OrbitAutoActionEnabled != null)
                HS2OrbitAndExciter.OrbitAutoActionEnabled.Value = GUILayout.Toggle(HS2OrbitAndExciter.OrbitAutoActionEnabled.Value, " 環視開著時，自動幫你選下一個動作（少自己點）");
            if (HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds != null)
            {
                GUILayout.Label("（僅在開啟「自動幫你選下一個動作」時有效；與「每繞幾圈換姿／換裝」無關。）", _labelStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("卡關幾秒後強制往下一階段（0＝不要強制）：", _labelStyle, GUILayout.Width(260));
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
            GUILayout.Label("設定值會自動儲存，保持至下次變更。", _labelStyle);
            GUILayout.Label("熱鍵：左 Ctrl＋左 Shift＋O 開關環視；左 Ctrl＋左 Shift＋P 開本視窗。頂端為版本對照資訊。", _labelStyle);
            if (GUILayout.Button("關閉"))
                _visible = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }
    }
}
