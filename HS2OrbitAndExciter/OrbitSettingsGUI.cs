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
        private Rect _windowRect = new Rect(100, 100, 420, 460);
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
                _excitementDelayStr = (HS2OrbitAndExciter.ExcitementTriggerDelaySeconds?.Value ?? 0f).ToString("F1");
                _feelAddPerSecStr = (HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit?.Value ?? 0.1f).ToString("F2");
                _orbitDistHeadStr = (HS2OrbitAndExciter.OrbitDistanceHead?.Value ?? 0.3f).ToString("F2");
                _orbitDistChestStr = (HS2OrbitAndExciter.OrbitDistanceChest?.Value ?? 0.3f).ToString("F2");
                _orbitDistPelvisStr = (HS2OrbitAndExciter.OrbitDistancePelvis?.Value ?? 0.3f).ToString("F2");
                _lastOverrideFaintness = HS2OrbitAndExciter.OverrideFaintness?.Value ?? false;
                _needSyncFromConfig = false;
            }

            InitStyles();
            _windowRect = GUILayout.Window(9001, _windowRect, DrawWindow, "HS2 Orbit and Exciter — 設定");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            GUILayout.BeginVertical(GUI.skin.box);
            foreach (var line in PluginBuildIdentity.GetGuiLines())
                GUILayout.Label(line, _labelStyle ?? GUI.skin.label);
            GUILayout.EndVertical();

            GUILayout.Label("環視 POV (Orbit)", GUI.skin.box);
            if (HS2OrbitAndExciter.OrbitTimePer360 != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("環視 POV 一圈 360° 時間 (秒):", _labelStyle, GUILayout.Width(220));
                GUI.SetNextControlName("OrbitTimePer360");
                _orbitTimeStr = GUILayout.TextField(_orbitTimeStr, GUILayout.Width(60));
                if (float.TryParse(_orbitTimeStr, out float v) && v > 0.1f && v <= 120f)
                    HS2OrbitAndExciter.OrbitTimePer360.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrbitCountBeforeRandom != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("幾次 360 後亂數焦點/角度 (0=不亂數):", _labelStyle, GUILayout.Width(220));
                GUI.SetNextControlName("OrbitCountBeforeRandom");
                _orbitCountRandomStr = GUILayout.TextField(_orbitCountRandomStr, GUILayout.Width(60));
                if (int.TryParse(_orbitCountRandomStr, out int v) && v >= 0 && v <= 99)
                    HS2OrbitAndExciter.OrbitCountBeforeRandom.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.OrbitCountBeforePoseChange != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("幾次 360 後換姿勢:", _labelStyle, GUILayout.Width(220));
                GUI.SetNextControlName("OrbitCountBeforePoseChange");
                _orbitCountPoseStr = GUILayout.TextField(_orbitCountPoseStr, GUILayout.Width(60));
                if (int.TryParse(_orbitCountPoseStr, out int v) && v >= 1 && v <= 99)
                    HS2OrbitAndExciter.OrbitCountBeforePoseChange.Value = v;
                GUILayout.EndHorizontal();
            }
            if (HS2OrbitAndExciter.ChangePoseOnCycle != null)
                HS2OrbitAndExciter.ChangePoseOnCycle.Value = GUILayout.Toggle(HS2OrbitAndExciter.ChangePoseOnCycle.Value, " 每 n 次 360 後換姿勢 (ChangePoseOnCycle)");
            if (HS2OrbitAndExciter.ClothesChangeEnabled != null)
                HS2OrbitAndExciter.ClothesChangeEnabled.Value = GUILayout.Toggle(HS2OrbitAndExciter.ClothesChangeEnabled.Value, " 每完成 1 次 360 切換衣物階段 (ClothesChangeEnabled)");
            if (HS2OrbitAndExciter.OrbitAutoActionEnabled != null)
                HS2OrbitAndExciter.OrbitAutoActionEnabled.Value = GUILayout.Toggle(HS2OrbitAndExciter.OrbitAutoActionEnabled.Value, " 環視時自動進動作／少操作 (OrbitAutoActionEnabled)");
            if (HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("環視時卡點超過幾秒自動下一階段 (0=不強制):", _labelStyle, GUILayout.Width(260));
                GUI.SetNextControlName("OrbitCheckpointTimeout");
                _checkpointTimeoutStr = GUILayout.TextField(_checkpointTimeoutStr, GUILayout.Width(60));
                if (float.TryParse(_checkpointTimeoutStr, out float v) && v >= 0f && v <= 60f)
                    HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds.Value = v;
                GUILayout.EndHorizontal();
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
            GUILayout.Label("狀態 (State)", GUI.skin.box);
            if (HS2OrbitAndExciter.OverrideFaintness != null)
            {
                bool newFaintness = GUILayout.Toggle(HS2OrbitAndExciter.OverrideFaintness.Value, " 脫力 (OverrideFaintness)");
                HS2OrbitAndExciter.OverrideFaintness.Value = newFaintness;
                if (newFaintness != _lastOverrideFaintness)
                {
                    _lastOverrideFaintness = newFaintness;
                    OrbitHelpers.SetGameFaintnessAndRequestViewReapply(newFaintness);
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("興奮條 (Exciter)", GUI.skin.box);
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
            GUILayout.Label("熱鍵：Ctrl+Shift+O 環視開關，Ctrl+Shift+P 本視窗；頂部為建置識別。", _labelStyle);
            if (GUILayout.Button("關閉"))
                _visible = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }
    }
}
