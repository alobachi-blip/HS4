using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Left-bottom status HUD. Clear “what’s happening now” layout. P / Ctrl+Shift+I toggle.
    /// </summary>
    public class OrbitStatusHud : MonoBehaviour
    {
        private const KeyCode ToggleHotkey = KeyCode.I;
        private const float AreaWidth = 320f;
        private const float Margin = 8f;
        private const float MaxHeightFraction = 0.36f;
        private const float BottomUiClearance = 176f;

        private OrbitController? _orbit;
        private bool _panelVisible = true;
        private bool _stylesReady;
        private Vector2 _scroll;
        private GUIStyle? _titleLabel;
        private GUIStyle? _bodyLabel;
        private GUIStyle? _dimLabel;
        private GUIStyle? _alertLabel;
        private GUIStyle? _boxStyle;

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
            _titleLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                padding = new RectOffset(0, 0, 0, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _bodyLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _dimLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _alertLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _alertLabel.normal.textColor = new Color(1f, 0.22f, 0.18f, 1f);
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
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
                if (OrbitBehaviorHub.IsOrbitAssistActive()
                    || OrbitVoiceTour.IsActive
                    || (OrbitVoiceTour.Enabled && OrbitController.TryGetHScene() != null))
                {
                    _panelVisible = !_panelVisible;
                    Event.current.Use();
                }
            }

            if (!_panelVisible || _orbit == null)
                return;

            bool orbitOn = OrbitBehaviorHub.IsOrbitAssistActive();
            bool voiceTourHud = OrbitVoiceTour.IsActive
                || (OrbitVoiceTour.Enabled && OrbitController.TryGetHScene() != null);
            if (!orbitOn && !voiceTourHud)
                return;

            InitStyles();
            var lines = BuildDisplayLines(orbitOn);
            float lineH = (_bodyLabel ?? GUI.skin.label).lineHeight + 2f;
            float contentH = (_boxStyle ?? GUI.skin.box).padding.vertical + lineH * lines.Length + 8f;
            float maxH = Mathf.Max(lineH * 5f, Screen.height * MaxHeightFraction);
            float areaH = Mathf.Min(contentH, maxH);
            bool needScroll = contentH > maxH + 0.5f;

            var area = new Rect(Margin, Screen.height - areaH - Margin - BottomUiClearance, AreaWidth, areaH);
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(_boxStyle ?? GUI.skin.box);
            if (needScroll)
            {
                _scroll = GUILayout.BeginScrollView(_scroll, false, true,
                    GUILayout.Height(areaH - (_boxStyle ?? GUI.skin.box).padding.vertical));
                DrawLines(lines);
                GUILayout.EndScrollView();
            }
            else
            {
                DrawLines(lines);
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawLines(HudLine[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var style = lines[i].Kind switch
                {
                    LineKind.Title => _titleLabel,
                    LineKind.Dim => _dimLabel,
                    LineKind.Alert => _alertLabel,
                    _ => _bodyLabel
                } ?? GUI.skin.label;
                GUILayout.Label(lines[i].Text, style);
            }
        }

        private HudLine[] BuildDisplayLines(bool orbitOn)
        {
            var list = new System.Collections.Generic.List<HudLine>(16);

            if (orbitOn && _orbit != null && _orbit.TryGetCachedHudSnapshot(out var snap))
            {
                list.Add(new HudLine(LineKind.Title, "現在：" + FormatNowStatus(snap)));
                list.Add(new HudLine(LineKind.Body, FormatProgress(snap)));

                string next = FormatNextHint(snap);
                if (!string.IsNullOrEmpty(next))
                    list.Add(new HudLine(LineKind.Body, "接著：" + next));

                string wait = FormatWaiting(snap);
                if (!string.IsNullOrEmpty(wait))
                {
                    bool blocked = !string.IsNullOrEmpty(snap.SuppressReasonKey)
                        && snap.SuppressReasonKey != OrbitAssistReasons.None;
                    bool appreciate = snap.SuppressReasonKey == OrbitAssistReasons.LongAppreciation
                        && !OrbitBehaviorHub.IsMotionEscapeArmed();
                    list.Add(new HudLine(
                        blocked || appreciate ? LineKind.Alert : LineKind.Body,
                        wait));
                }

                if (OrbitBehaviorHub.ShouldPauseOrbitCameraForUi()
                    && OrbitBehaviorHub.IsOrbitCameraSpinning())
                {
                    // 與「擋推進」分開：選單只停相機
                    list.Add(new HudLine(LineKind.Dim, "環視：選單操作中暫停轉動（推進不擋）"));
                }

                list.Add(new HudLine(LineKind.Body, FormatOrgasmFx()));
                string pool = FormatPoolShort();
                if (!string.IsNullOrEmpty(pool))
                    list.Add(new HudLine(LineKind.Dim, pool));
            }
            else
            {
                list.Add(new HudLine(LineKind.Title, "現在：語音巡禮（環視未開）"));
            }

            AppendVoiceTour(list);

            list.Add(new HudLine(LineKind.Dim, "──"));
            list.Add(new HudLine(LineKind.Dim, OrbitManualHotkeys.HudLegendCompact));
            list.Add(new HudLine(LineKind.Dim, "Ctrl+Shift+O 協助｜Ctrl+Shift+P 設定｜P 隱藏本面板"));

            return list.ToArray();
        }

        private static string FormatNowStatus(in OrbitHudSnapshot snap)
        {
            string core;
            if (snap.CameraPaused)
                core = "正在換視角";
            else if (!OrbitBehaviorHub.IsOrbitCameraSpinning())
                core = "相機已暫停（按 O 繼續）";
            else if (OrbitBehaviorHub.ShouldPauseOrbitCameraForUi())
                core = "相機暫停：操作選單中";
            else if (OrbitPoseDirector.ShouldFreezeCycleCounters)
                core = OrbitPoseDirector.Phase == DirectorState.PosePending
                    ? "環視中・等待換姿勢"
                    : "環視中・正在換姿勢";
            else
                core = "環視轉動中";

            // 虛脫只在高潮後 5s 欣賞窗提示；開幹後當一般流程，不一直掛「脫力」
            float appreciate = OrbitFsmFlow.RemainingAfterIdleAppreciateSeconds();
            if (appreciate > 0.05f)
                core = $"高潮後欣賞中（{appreciate:F0}s）";
            return core;
        }

        private static string FormatProgress(in OrbitHudSnapshot snap)
        {
            if (snap.WaitingPrep)
                return $"準備中…還有 {snap.PrepRemainSeconds:F0} 秒";

            string leg = snap.Phase == 0 ? "去程" : "回程";
            return $"{leg}還有 {snap.TimeToCompleteCurrentRotation:F0} 秒轉完";
        }

        private static string FormatWaiting(in OrbitHudSnapshot snap)
        {
            string assist = FormatAssistPlain(snap.SuppressReasonKey);
            string timed = FormatTimedPlain(snap.SuppressReasonKey);
            bool blocked = !string.IsNullOrEmpty(snap.SuppressReasonKey)
                && snap.SuppressReasonKey != OrbitAssistReasons.None;
            string core = !string.IsNullOrEmpty(timed) && timed != assist
                ? assist + "｜" + timed
                : assist;
            if (blocked)
                return "擋推進：" + core;
            return core;
        }

        private static string FormatAssistPlain(string reasonKey)
        {
            switch (reasonKey)
            {
                case OrbitAssistReasons.PointerOverUi:
                    // 已不擋推進；若舊 log／殘留 reason 仍出現則說明清楚
                    return "游標在 UI（不擋推進；僅可能停環視）";
                case OrbitAssistReasons.OrbitStartGrace:
                    {
                        float g = OrbitBehaviorHub.RemainingOrbitStartGraceSeconds();
                        return g > 0.05f ? $"剛啟動緩衝 {g:F0}s" : "剛啟動緩衝";
                    }
                case OrbitAssistReasons.InputForcus: return "正在輸入";
                case OrbitAssistReasons.PoseQueued:
                case OrbitAssistReasons.SelectionListPresentLegacy: return "正在選姿勢";
                case OrbitAssistReasons.Changing:
                case OrbitAssistReasons.NowChangeAnim:
                case OrbitAssistReasons.PoseTransitionLegacy: return "正在換姿勢";
                case OrbitAssistReasons.Rebinding: return "換姿勢綁定中";
                case OrbitAssistReasons.PosePending: return "高潮後等待換姿勢";
                case OrbitAssistReasons.MouseHolding: return "按住滑鼠";
                case OrbitAssistReasons.RecentUiClick:
                    {
                        float u = OrbitBehaviorHub.RemainingManualUiSuppressSeconds();
                        return u > 0.05f ? $"剛點過 UI，再等 {u:F0}s" : "剛點過 UI";
                    }
                case OrbitAssistReasons.ManualBusy: return "手動換角中";
                case OrbitAssistReasons.NowOrgasm: return "高潮進行中";
                case OrbitAssistReasons.OrgasmQuiet:
                    {
                        float q = OrbitBehaviorHub.RemainingOrgasmQuietSeconds();
                        return q > 0.05f ? $"高潮後安靜 {q:F0}s" : "高潮後安靜";
                    }
                case OrbitAssistReasons.LongAppreciation:
                    return OrbitBehaviorHub.IsMotionEscapeArmed()
                        ? "欣賞姿：可推進"
                        : "欣賞中：按 L／滾輪／N 可手動跳出";
                case OrbitAssistReasons.AssistInterval:
                case OrbitAssistReasons.CheckpointInterval: return "稍候再自動推進";
                case OrbitAssistReasons.None: return "推進：就緒";
                default:
                    return string.IsNullOrEmpty(reasonKey) || reasonKey == OrbitAssistReasons.None
                        ? "推進：就緒"
                        : reasonKey;
            }
        }

        private static string FormatTimedPlain(string suppressReasonKey)
        {
            float afterAppreciate = OrbitFsmFlow.RemainingAfterIdleAppreciateSeconds();
            if (afterAppreciate > 0.05f)
                return $"虛脫欣賞 {afterAppreciate:F0}s→選池再開幹";

            float after = OrbitBehaviorHub.RemainingAfterIdleAutoEscapeSeconds();
            if (after > 0.05f)
                return $"倒數脫離閒置 {after:F0}s";

            float idle = OrbitBehaviorHub.RemainingIdleAutoEscapeSeconds();
            if (idle > 0.05f)
                return $"倒數脫離待機 {idle:F0}s";

            if (suppressReasonKey == OrbitAssistReasons.OrgasmQuiet)
            {
                float q = OrbitBehaviorHub.RemainingOrgasmQuietSeconds();
                if (q > 0.05f) return $"倒數 {q:F0}s";
            }
            return "";
        }

        private static string FormatNextHint(in OrbitHudSnapshot snap)
        {
            float rotT = snap.SingleRotationSeconds;
            if (rotT <= 0f) return "";

            float bestSec = float.MaxValue;
            string best = "";

            if (snap.RotationsUntilRandom == 1)
            {
                float sec = snap.TimeToCompleteCurrentRotation;
                if (sec < bestSec) { bestSec = sec; best = $"亂數換焦點（約 {sec:F0}s）"; }
            }
            if (snap.RotationsUntilClothes == OrbitHudSnapshot.ClothesHintNextRoundTrip)
            {
                float sec = snap.TimeToCompleteCurrentRoundTrip;
                if (sec < bestSec) { bestSec = sec; best = $"換衣（約 {sec:F0}s）"; }
            }
            else if (snap.RotationsUntilClothes == 1)
            {
                float sec = snap.TimeToCompleteCurrentRotation;
                if (sec < bestSec) { bestSec = sec; best = $"換衣（約 {sec:F0}s）"; }
            }
            if (snap.RoundTripsUntilPose == 1)
            {
                float sec = snap.TimeToCompleteCurrentRoundTrip;
                if (sec < bestSec) { bestSec = sec; best = $"換姿勢（約 {sec:F0}s）"; }
            }

            return best;
        }

        private static string FormatOrgasmFx()
        {
            string tattoo = OrbitOrgasmTattoo.Enabled
                ? (OrbitOrgasmTattoo.Count > 0
                    ? $"刺青×{OrbitOrgasmTattoo.Count}"
                    : "刺青開")
                : "刺青關";
            return $"特效：{tattoo}｜{OrbitOrgasmBustGrowth.HudStatus}｜{OrbitOrgasmNippleSpray.HudStatus}";
        }

        private static string FormatPoolShort()
        {
            var s = OrbitManualDirector.GetHudStats();
            if (s.CharaPool <= 0 && !s.OnStageTracked)
                return "";
            string line = $"女角池 {s.CharaPool}";
            if (s.Excluded > 0) line += $"｜已排除 {s.Excluded}";
            if (s.Preferred > 0) line += $"｜優先 {s.Preferred}";
            return line;
        }

        private static void AppendVoiceTour(System.Collections.Generic.List<HudLine> list)
        {
            if (!OrbitVoiceTour.Enabled)
            {
                list.Add(new HudLine(LineKind.Dim, "語音巡禮：關"));
                return;
            }

            if (!OrbitVoiceTour.IsActive && OrbitController.TryGetHScene() == null)
            {
                list.Add(new HudLine(LineKind.Body, "語音巡禮：進 H 後開始"));
                return;
            }

            list.Add(new HudLine(LineKind.Body,
                $"語音：{OrbitVoiceTour.CurrentLabelZh}  {OrbitVoiceTour.StageIndex + 1}/{OrbitVoiceTour.StageCount}  （本段 {OrbitVoiceTour.HitsInStage}/{OrbitVoiceTour.HitsPerStage}）"));
            if (!string.IsNullOrEmpty(OrbitVoiceTour.LastTrigger) && OrbitVoiceTour.LastTrigger != "—")
                list.Add(new HudLine(LineKind.Dim, "上次觸發：" + OrbitVoiceTour.LastTrigger));
        }

        private enum LineKind { Title, Body, Dim, Alert }

        private readonly struct HudLine
        {
            internal HudLine(LineKind kind, string text)
            {
                Kind = kind;
                Text = text;
            }
            internal LineKind Kind { get; }
            internal string Text { get; }
        }
    }
}
