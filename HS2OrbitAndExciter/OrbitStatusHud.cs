using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Minimal orbit status overlay (Traditional Chinese). Ctrl+Shift+I toggles while orbit is on.
    /// </summary>
    public class OrbitStatusHud : MonoBehaviour
    {
        private const KeyCode ToggleHotkey = KeyCode.I;
        private const KeyCode Modifier = KeyCode.LeftShift;
        private const KeyCode Modifier2 = KeyCode.LeftControl;
        private const float AreaWidth = 200f;
        private const float Margin = 6f;

        private OrbitController? _orbit;
        private bool _panelVisible = true;
        private bool _stylesReady;
        private GUIStyle? _smallLabel;
        private GUIStyle? _smallBox;

        private void Awake()
        {
            _orbit = GetComponent<OrbitController>();
        }

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
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _smallBox = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(4, 4, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
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
            var label = _smallLabel ?? GUI.skin.label;
            var box = _smallBox ?? GUI.skin.box;
            var lines = BuildLines(snap);
            float lineH = label.lineHeight;
            float areaH = box.padding.vertical + lineH * lines.Length;
            var area = new Rect(Margin, Screen.height - areaH - Margin, AreaWidth, areaH);
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(box);
            for (int i = 0; i < lines.Length; i++)
                GUILayout.Label(lines[i], label, GUILayout.Height(lineH));
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private static string[] BuildLines(in OrbitHudSnapshot snap)
        {
            string status;
            if (snap.CameraPaused)
                status = OrbitManualDirector.IsBusy ? "換角中" : "換姿中";
            else
                status = "運轉";
            if (snap.IsFaintness)
                status += "·虛脫";

            string leg = snap.Phase == 0 ? "去" : "回";
            string timer = snap.WaitingPrep
                ? $"準 {snap.PrepRemainSeconds:F0}s"
                : $"{leg} {snap.TimeToCompleteCurrentRotation:F0}s";

            string next = FormatNextHint(snap);
            if (!string.IsNullOrEmpty(next))
                timer += " " + next;

            string assist = FormatAssistShort(snap.SuppressReasonKey);
            string manual = FormatManualPoolLine();
            string tattooLine = OrbitOrgasmTattoo.Enabled
                ? (OrbitOrgasmTattoo.Count > 0
                    ? $"刺青×{OrbitOrgasmTattoo.Count} 剛加:{OrbitOrgasmTattoo.LastSiteLabel}"
                    : "刺青開·尚未加")
                : "刺青關(T)";
            return new[]
            {
                $"環視·{status} {timer}",
                assist,
                "⌃⇧O/I/P QWE",
                OrbitManualHotkeys.HudLegend + "·" + OrbitOrgasmTattoo.HudStatus,
                tattooLine,
                OrbitManualHotkeys.PregnancyHudLegend,
                manual
            };
        }

        /// <summary>G pool: eligible count, strike-1 disliked, strike-2 excluded, long-stay preferred; on-stage timer.</summary>
        private static string FormatManualPoolLine()
        {
            var s = OrbitManualDirector.GetHudStats();
            if (s.CharaPool <= 0 && !s.OnStageTracked)
                return "G池·未掃";

            string line = $"G池{s.CharaPool}";
            if (s.Disliked > 0)
                line += $" 降{s.Disliked}";
            if (s.Excluded > 0)
                line += $" 排{s.Excluded}";
            if (s.Preferred > 0)
                line += $" 優{s.Preferred}";
            if (s.OnStageTracked && s.OnStageSeconds >= 0f)
            {
                if (s.OnStageSeconds < 30f)
                    line += $" ·{s.OnStageSeconds:F0}s";
                else if (s.OnStageSeconds >= 60f)
                    line += $" ·{s.OnStageSeconds:F0}s久";
            }
            return line;
        }

        private static string FormatAssistShort(string reasonKey)
        {
            switch (reasonKey)
            {
                case "pointerOverUi": return "自動·UI";
                case "orbitStartGrace":
                    {
                        float g = OrbitBehaviorHub.RemainingOrbitStartGraceSeconds();
                        return g > 0.01f ? $"自動·緩衝{g:F0}s" : "自動·緩衝";
                    }
                case "inputForcus": return "自動·輸入";
                case "selectionListPresent": return "自動·選單";
                case "mouseHolding": return "自動·按住";
                case "recentUiClick":
                    {
                        float u = OrbitBehaviorHub.RemainingManualUiSuppressSeconds();
                        return u > 0.01f ? $"自動·UI{u:F0}s" : "自動·UI";
                    }
                case "poseTransition": return "自動·換姿";
                case "manualBusy": return "自動·換角";
                case "assistInterval": return "自動·節流";
                case "checkpointInterval": return "自動·節流";
                default: return "自動·就緒";
            }
        }

        /// <summary>Only show the nearest upcoming cycle event, ultra-compact.</summary>
        private static string FormatNextHint(in OrbitHudSnapshot snap)
        {
            float rotT = snap.SingleRotationSeconds;
            float rtT = snap.RoundTripSeconds;
            if (rotT <= 0f) return "";

            float bestSec = float.MaxValue;
            string best = "";

            if (snap.RotationsUntilRandom == 1)
            {
                float sec = snap.TimeToCompleteCurrentRotation;
                if (sec < bestSec) { bestSec = sec; best = "亂"; }
            }
            if (snap.RotationsUntilClothes == OrbitHudSnapshot.ClothesHintNextRoundTrip)
            {
                float sec = snap.TimeToCompleteCurrentRoundTrip;
                if (sec < bestSec) { bestSec = sec; best = "衣"; }
            }
            else if (snap.RotationsUntilClothes == 1)
            {
                float sec = snap.TimeToCompleteCurrentRotation;
                if (sec < bestSec) { bestSec = sec; best = "衣"; }
            }
            if (snap.RoundTripsUntilPose == 1)
            {
                float sec = snap.TimeToCompleteCurrentRoundTrip;
                if (sec < bestSec) { bestSec = sec; best = "姿"; }
            }

            return best.Length == 0 ? "" : $"次·{best}";
        }
    }
}
