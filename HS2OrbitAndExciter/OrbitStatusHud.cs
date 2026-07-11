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
        private const float AreaWidth = 300f;
        private const float Margin = 6f;
        private const float MaxHeightFraction = 0.32f;
        /// <summary>
        /// Clearance above the bottom H UI strip (Finish / 跳出 buttons ≈ 1–2 rows).
        /// Measured as ~72–90px per button row; use ~2 rows + padding.
        /// </summary>
        private const float BottomUiClearance = 176f;

        private OrbitController? _orbit;
        private bool _panelVisible = true;
        private bool _stylesReady;
        private Vector2 _scroll;
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
            bool voiceTourHud = OrbitVoiceTour.IsActive || (OrbitVoiceTour.Enabled && OrbitController.TryGetHScene() != null);
            if (!orbitOn && !voiceTourHud)
                return;

            InitStyles();
            var label = _smallLabel ?? GUI.skin.label;
            var box = _smallBox ?? GUI.skin.box;
            var lines = orbitOn && _orbit.TryGetCachedHudSnapshot(out var snap)
                ? BuildLines(snap)
                : BuildVoiceTourOnlyLines();
            // Always append voice tour block when relevant
            lines = AppendVoiceTourLines(lines);
            float lineH = label.lineHeight;
            float contentH = box.padding.vertical + lineH * lines.Length + 4f;
            float maxH = Mathf.Max(lineH * 4f, Screen.height * MaxHeightFraction);
            float areaH = Mathf.Min(contentH, maxH);
            bool needScroll = contentH > maxH + 0.5f;

            var area = new Rect(Margin, Screen.height - areaH - Margin - BottomUiClearance, AreaWidth, areaH);
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(box);
            if (needScroll)
            {
                _scroll = GUILayout.BeginScrollView(_scroll, false, true,
                    GUILayout.Height(areaH - box.padding.vertical));
                for (int i = 0; i < lines.Length; i++)
                    GUILayout.Label(lines[i], label, GUILayout.Height(lineH));
                GUILayout.EndScrollView();
            }
            else
            {
                for (int i = 0; i < lines.Length; i++)
                    GUILayout.Label(lines[i], label, GUILayout.Height(lineH));
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private static string[] BuildLines(in OrbitHudSnapshot snap)
        {
            string status;
            if (snap.CameraPaused)
                status = "換角中";
            else if (OrbitPoseDirector.ShouldFreezeCycleCounters)
                status = OrbitPoseDirector.Phase == DirectorState.PosePending
                    ? "運轉·換姿待"
                    : "運轉·換姿";
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
            string lockLine = FormatTimedLockLine(snap.SuppressReasonKey);
            string manual = FormatManualPoolLine();
            string orgasmFx = FormatOrgasmFxLine();
            string fsm = OrbitStateMachineLog.LogPath != null
                ? "FSM→LogOutput/HS2OrbitAndExciter_fsm.ndjson"
                : "FSM log n/a";

            if (!string.IsNullOrEmpty(lockLine))
            {
                return new[]
                {
                    $"環視·{status} {timer}",
                    assist,
                    lockLine,
                    "⌃⇧O/I/P  " + OrbitManualHotkeys.HudLegend,
                    orgasmFx,
                    OrbitManualHotkeys.PregnancyHudLegend,
                    manual,
                    fsm
                };
            }

            return new[]
            {
                $"環視·{status} {timer}",
                assist,
                "⌃⇧O/I/P  " + OrbitManualHotkeys.HudLegend,
                orgasmFx,
                OrbitManualHotkeys.PregnancyHudLegend,
                manual,
                fsm
            };
        }

        private static string[] BuildVoiceTourOnlyLines()
        {
            return new[]
            {
                "語音巡禮 HUD",
                "⌃⇧I 隱藏 · 設定 VoiceTour"
            };
        }

        private static string[] AppendVoiceTourLines(string[] baseLines)
        {
            var extra = FormatVoiceTourLines();
            if (extra.Length == 0)
                return baseLines;
            var merged = new string[baseLines.Length + extra.Length];
            for (int i = 0; i < baseLines.Length; i++)
                merged[i] = baseLines[i];
            for (int i = 0; i < extra.Length; i++)
                merged[baseLines.Length + i] = extra[i];
            return merged;
        }

        private static string[] FormatVoiceTourLines()
        {
            if (!OrbitVoiceTour.Enabled)
                return new[] { "語音巡禮：關" };

            if (!OrbitVoiceTour.IsActive && OrbitController.TryGetHScene() == null)
                return new[] { "語音巡禮：待機（進H開始）" };

            var def = OrbitVoiceTour.Stages[
                OrbitVoiceTour.StageIndex >= 0 && OrbitVoiceTour.StageIndex < OrbitVoiceTour.StageCount
                    ? OrbitVoiceTour.StageIndex
                    : 0];
            string num = def.StateNum >= 0 ? def.StateNum.ToString() : "—";
            string persist = OrbitVoiceTour.PersistProgress ? "記住" : "不存檔";
            string loop = OrbitVoiceTour.Loop ? "Loop開" : "Loop關";
            string key = string.IsNullOrEmpty(OrbitVoiceTour.CharKey) ? "—" : OrbitVoiceTour.CharKey!;

            return new[]
            {
                $"語音·{OrbitVoiceTour.CurrentLabelZh} {OrbitVoiceTour.StageIndex + 1}/{OrbitVoiceTour.StageCount}",
                $"Num{num} · 擊{OrbitVoiceTour.HitsInStage}/{OrbitVoiceTour.HitsPerStage} · {OrbitVoiceTour.LastTrigger}",
                $"鍵:{TrimKey(key)} · {persist} · {loop}",
                "說明:高潮/男射精換音庫·不改卡片",
                "推進:女高潮+侍奉體外口內+插入內射",
                "進度:依角色記住·換人再回來不重來"
            };
        }

        private static string TrimKey(string key)
        {
            if (key.Length <= 18) return key;
            return key.Substring(0, 16) + "…";
        }

        private static string FormatOrgasmFxLine()
        {
            string tattoo = OrbitOrgasmTattoo.Enabled
                ? (OrbitOrgasmTattoo.Count > 0
                    ? $"刺×{OrbitOrgasmTattoo.Count}·{OrbitOrgasmTattoo.LastSiteLabel}"
                    : "刺開·0")
                : "刺關(T)";
            string bust = OrbitOrgasmBustGrowth.HudStatus;
            string nipple = OrbitOrgasmNippleSpray.HudStatus;
            return $"{OrbitManualHotkeys.OrgasmFxHudPrefix} {tattoo} · {bust} · {nipple}";
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
                case OrbitAssistReasons.PointerOverUi: return "自動·UI上（移開游標）";
                case OrbitAssistReasons.OrbitStartGrace:
                    {
                        float g = OrbitBehaviorHub.RemainingOrbitStartGraceSeconds();
                        return g > 0.05f ? $"自動·緩衝 {g:F1}s" : "自動·緩衝";
                    }
                case OrbitAssistReasons.InputForcus: return "自動·輸入中";
                case OrbitAssistReasons.PoseQueued:
                case OrbitAssistReasons.SelectionListPresentLegacy: return "自動·選姿中";
                case OrbitAssistReasons.Changing:
                case OrbitAssistReasons.NowChangeAnim:
                case OrbitAssistReasons.PoseTransitionLegacy: return "自動·換姿中";
                case OrbitAssistReasons.Rebinding: return "自動·換姿綁定";
                case OrbitAssistReasons.PosePending: return "自動·換姿待（高潮後）";
                case OrbitAssistReasons.MouseHolding: return "自動·按住";
                case OrbitAssistReasons.RecentUiClick:
                    {
                        float u = OrbitBehaviorHub.RemainingManualUiSuppressSeconds();
                        return u > 0.05f ? $"自動·UI點擊 {u:F1}s" : "自動·UI點擊";
                    }
                case OrbitAssistReasons.ManualBusy: return "自動·換角中";
                case OrbitAssistReasons.NowOrgasm: return "鎖·高潮中（等結束）";
                case OrbitAssistReasons.OrgasmQuiet:
                    {
                        float q = OrbitBehaviorHub.RemainingOrgasmQuietSeconds();
                        return q > 0.05f ? $"鎖·高潮後安靜 {q:F1}s" : "鎖·高潮後安靜";
                    }
                case OrbitAssistReasons.LongAppreciation:
                    return OrbitBehaviorHub.IsMotionEscapeArmed()
                        ? "欣賞·已解鎖（推進中）"
                        : "欣賞·等 L／滾輪／N";
                case OrbitAssistReasons.AssistInterval: return "自動·節流";
                case OrbitAssistReasons.CheckpointInterval: return "自動·節流";
                case OrbitAssistReasons.CheckpointLegacyCooldown: return "自動·節流";
                case OrbitAssistReasons.None: return "自動·就緒";
                default: return string.IsNullOrEmpty(reasonKey) || reasonKey == OrbitAssistReasons.None
                    ? "自動·就緒"
                    : $"自動·{reasonKey}";
            }
        }

        /// <summary>Extra line for timed waits that are not (only) CanAutoAdvance reasons.</summary>
        private static string FormatTimedLockLine(string suppressReasonKey)
        {
            float after = OrbitBehaviorHub.RemainingAfterIdleAutoEscapeSeconds();
            if (after > 0.05f)
                return $"倒數·脫離AfterIdle {after:F1}s";

            float idle = OrbitBehaviorHub.RemainingIdleAutoEscapeSeconds();
            if (idle > 0.05f)
                return $"倒數·脫離Idle {idle:F1}s";

            // Mirror timed suppress on its own line when assist line is crowded / for clarity.
            if (suppressReasonKey == OrbitAssistReasons.OrgasmQuiet)
            {
                float q = OrbitBehaviorHub.RemainingOrgasmQuietSeconds();
                if (q > 0.05f)
                    return $"倒數·高潮後 {q:F1}s";
            }
            if (suppressReasonKey == OrbitAssistReasons.OrbitStartGrace)
            {
                float g = OrbitBehaviorHub.RemainingOrbitStartGraceSeconds();
                if (g > 0.05f)
                    return $"倒數·緩衝 {g:F1}s";
            }
            if (suppressReasonKey == OrbitAssistReasons.RecentUiClick)
            {
                float u = OrbitBehaviorHub.RemainingManualUiSuppressSeconds();
                if (u > 0.05f)
                    return $"倒數·UI {u:F1}s";
            }

            return "";
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
