using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Compact Traditional-Chinese status overlay when orbit is on: Ctrl+Shift+I toggles; Ctrl+Shift+O turns orbit on and shows panel by default.
    /// </summary>
    public class OrbitStatusHud : MonoBehaviour
    {
        private const KeyCode ToggleHotkey = KeyCode.I;
        private const KeyCode Modifier = KeyCode.LeftShift;
        private const KeyCode Modifier2 = KeyCode.LeftControl;
        private const float AreaWidth = 360f;
        private const float AreaMaxHeight = 220f;
        private const float Margin = 8f;

        private OrbitController? _orbit;
        private bool _panelVisible = true;
        private bool _stylesReady;
        private GUIStyle? _smallLabel;
        private GUIStyle? _smallBox;

        private void Awake()
        {
            _orbit = GetComponent<OrbitController>();
        }

        /// <summary>Called when orbit assist is toggled on (from hub).</summary>
        internal static void NotifyOrbitActivated()
        {
            if (HS2OrbitAndExciter.OrbitStatusHudEnabled?.Value == false)
                return;
            if (Instance != null)
                Instance._panelVisible = true;
        }

        private static OrbitStatusHud? Instance { get; set; }

        private void OnEnable() => Instance = this;
        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        internal static void SetPanelVisible(bool visible)
        {
            if (Instance != null) Instance._panelVisible = visible;
        }

        internal static bool GetPanelVisible() => Instance?._panelVisible ?? false;

        private void InitStyles()
        {
            if (_stylesReady) return;
            _smallLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };
            _smallBox = new GUIStyle(GUI.skin.box) { padding = new RectOffset(6, 6, 4, 4) };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            if (HS2OrbitAndExciter.OrbitStatusHudEnabled?.Value == false)
                return;

            if (Event.current != null && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == ToggleHotkey && Event.current.control && Event.current.shift)
            {
                if (OrbitBehaviorHub.IsOrbitAssistActive())
                {
                    _panelVisible = !_panelVisible;
                    Event.current.Use();
                }
            }

            if (!_panelVisible || !OrbitBehaviorHub.IsOrbitAssistActive() || _orbit == null)
                return;

            if (!_orbit.TryGetCachedHudSnapshot(out var snap))
                return;

            InitStyles();
            float areaH = Mathf.Min(AreaMaxHeight, Screen.height * 0.35f);
            var area = new Rect(Margin, Screen.height - areaH - Margin, AreaWidth, areaH);
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(_smallBox ?? GUI.skin.box);
            DrawSnapshot(snap);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawSnapshot(in OrbitHudSnapshot snap)
        {
            var label = _smallLabel ?? GUI.skin.label;
            string header = snap.CameraPaused
                ? "環視中 · 鏡頭暫停（換姿勢中）"
                : "環視中 · 相機運轉中";
            string assistLine = FormatAssistStatus(snap.SuppressReasonKey);
            GUILayout.Label($"{header}\n{assistLine}", label);

            if (snap.WaitingPrep)
                GUILayout.Label($"準備倒數：約 {snap.PrepRemainSeconds:F1} 秒後開始動作", label);

            string leg = snap.Phase == 0 ? "去程" : "回程";
            GUILayout.Label($"{leg} · 本段旋轉剩餘約 {snap.TimeToCompleteCurrentRotation:F0} 秒（估算）", label);

            string next = FormatNextCycleHints(snap);
            if (!string.IsNullOrEmpty(next))
                GUILayout.Label(next, label);

            if (snap.IsFaintness)
                GUILayout.Label("狀態：虛脫", label);

            GUILayout.Label("Ctrl+Shift+I 切換本面板 · 單向 360°＝一次旋轉；去程＋回程＝一次迴轉 · 秒數為估算", label);
        }

        private static string FormatAssistStatus(string reasonKey)
        {
            switch (reasonKey)
            {
                case "pointerOverUi":
                    return "自動操作：滑鼠在 UI 上，已暫停（相機仍轉）";
                case "orbitStartGrace":
                    {
                        float g = OrbitBehaviorHub.RemainingOrbitStartGraceSeconds();
                        return g > 0.01f
                            ? $"自動操作：啟動緩衝約 {g:F1} 秒"
                            : "自動操作：啟動緩衝";
                    }
                case "inputForcus":
                    return "自動操作：輸入焦點在 UI";
                case "selectionListPresent":
                    return "自動操作：選單開啟中";
                case "mouseHolding":
                    return "自動操作：滑鼠按住中";
                case "recentUiClick":
                    {
                        float u = OrbitBehaviorHub.RemainingManualUiSuppressSeconds();
                        return u > 0.01f
                            ? $"自動操作：剛點過 UI，約 {u:F1} 秒後恢復"
                            : "自動操作：剛點過 UI";
                    }
                case "poseTransition":
                    return "自動操作：換姿過渡，已暫停";
                case "none":
                default:
                    return "自動操作：就緒";
            }
        }

        private static string FormatNextCycleHints(in OrbitHudSnapshot snap)
        {
            float rotT = snap.SingleRotationSeconds;
            float rtT = snap.RoundTripSeconds;
            if (rotT <= 0f) return "";

            var parts = new System.Collections.Generic.List<string>(4);
            if (snap.RotationsUntilRandom >= 1)
            {
                float sec = snap.TimeToCompleteCurrentRotation + (snap.RotationsUntilRandom - 1) * rotT;
                parts.Add($"亂數焦點／水平角：再 {snap.RotationsUntilRandom} 次旋轉（約 {sec:F0} 秒）");
            }
            if (snap.RotationsUntilClothes == OrbitHudSnapshot.ClothesHintNextRoundTrip)
                parts.Add($"換裝階段：本迴轉結束時（約 {snap.TimeToCompleteCurrentRoundTrip:F0} 秒）");
            else if (snap.RotationsUntilClothes >= 1)
            {
                float sec = snap.TimeToCompleteCurrentRotation + (snap.RotationsUntilClothes - 1) * rotT;
                parts.Add($"換裝階段：再 {snap.RotationsUntilClothes} 次旋轉（約 {sec:F0} 秒）");
            }
            if (snap.RoundTripsUntilPose >= 1)
            {
                float sec = snap.TimeToCompleteCurrentRoundTrip + (snap.RoundTripsUntilPose - 1) * rtT;
                parts.Add($"換姿勢：再 {snap.RoundTripsUntilPose} 次迴轉（約 {sec:F0} 秒）");
            }
            return parts.Count == 0 ? "" : string.Join("\n", parts);
        }
    }
}
