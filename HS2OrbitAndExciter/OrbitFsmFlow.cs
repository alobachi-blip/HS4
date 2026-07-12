using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// FSM 流程動作擁有者：選池、閒置開幹排程、高潮後→選池、L／N 依格分流。
    /// 不改拓樸；過程節奏秒數可調。
    /// </summary>
    internal static class OrbitFsmFlow
    {
        /// <summary>閒置落地後再開幹的準備秒數（過程節奏）。</summary>
        internal const float IdleStartPrepSeconds = 1.0f;
        /// <summary>高潮後／虛脫：給使用者欣賞秒數，到時選池再開幹（維持 isFaintness）。</summary>
        internal const float AfterIdleAppreciateSeconds = 5.0f;
        /// <summary>動作橋段按 N 加速時，感度一次加成。</summary>
        internal const float BridgeAccelerateFeelBoost = 0.15f;
        /// <summary>動作橋段按 N 加速時，速度加成。</summary>
        internal const float BridgeAccelerateSpeedBoost = 0.35f;

        private static float _idleStartDueUnscaled = -1f;
        private static bool _idleStartArmed;
        private static float _afterIdleDueUnscaled = -1f;
        private static bool _afterIdleArmed;
        private static string _afterIdleMode = ""; // "short" | "special"

        /// <summary>進 H／開協助後抽一次起始姿，避免總卡在預設接吻。</summary>
        private static bool _entryPosePickPending;
        private static float _entryPosePickDeadlineUnscaled = -1f;
        private const float EntryPosePickTimeoutSeconds = 8f;

        private static OrbitFsmCell _lastCell = OrbitFsmCell.Unknown;

        internal static void Reset()
        {
            CancelIdleStartSchedule();
            CancelAfterIdleSchedule();
            ClearEntryPosePick();
            _lastCell = OrbitFsmCell.Unknown;
        }

        internal static void OnHSceneEntered()
        {
            Reset();
            if (!OrbitBehaviorHub.IsOrbitAssistActive())
                return;
            var hScene = OrbitController.TryGetHScene();
            if (hScene != null
                && (OrbitFsmCellClassifier.Classify(hScene) == OrbitFsmCell.AfterIdle
                    || OrbitHelpers.IsFirstFemaleInAfterIdle(hScene)))
            {
                OnEnteredAfterIdle(hScene, "進 H・高潮後欣賞");
                return;
            }
            ArmEntryPosePick("h_enter");
        }

        /// <summary>開協助時：若已在高潮後／虛脫 → 走 5s 欣賞→選池；否則進場抽姿。</summary>
        internal static void OnAssistStarted()
        {
            var hScene = OrbitController.TryGetHScene();
            if (hScene == null)
                return;

            if (OrbitFsmCellClassifier.Classify(hScene) == OrbitFsmCell.AfterIdle
                || OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
            {
                ClearEntryPosePick();
                OnEnteredAfterIdle(hScene, "協助開・高潮後欣賞");
                return;
            }

            ArmEntryPosePick("orbit_on");
        }

        /// <summary>開協助／進場抽姿：進 H 後抽一次姿。</summary>
        internal static void ArmEntryPosePick(string reason)
        {
            CancelIdleStartSchedule();
            _entryPosePickPending = true;
            _entryPosePickDeadlineUnscaled = Time.unscaledTime + EntryPosePickTimeoutSeconds;
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 排程進場抽姿（{reason}）");
        }

        private static void ClearEntryPosePick()
        {
            _entryPosePickPending = false;
            _entryPosePickDeadlineUnscaled = -1f;
        }

        /// <summary>HUD：高潮後欣賞倒數（0＝未排程／已到）。</summary>
        internal static float RemainingAfterIdleAppreciateSeconds()
        {
            if (!_afterIdleArmed || _afterIdleMode != "short" || _afterIdleDueUnscaled < 0f)
                return 0f;
            return Mathf.Max(0f, _afterIdleDueUnscaled - Time.unscaledTime);
        }

        // ─── 選池 ───────────────────────────────────────────────

        /// <summary>手動／自動共用選池入口。成功則佇列換姿。</summary>
        internal static bool RequestSelectPool(HScene hScene, string trigger)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
                return false;

            CancelIdleStartSchedule();
            CancelAfterIdleSchedule();

            // 解除黏旗後再抽
            OrbitBehaviorHub.TryResolveAppliedPoseChange(hScene);

            if (!OrbitPoseDirector.RequestSelectPoolPoseChange(hScene))
            {
                string fail = OrbitPoseDirector.LastHotkeyFailReason;
                if (string.IsNullOrEmpty(fail) || fail == OrbitAssistReasons.None)
                    fail = OrbitAssistReasons.NoPoseCandidate;
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 選池被擋 [{trigger}] {fail}");
                return false;
            }

            var next = hScene.ctrlFlag?.selectAnimationListInfo;
            string label = next == null || string.IsNullOrEmpty(next.nameAnimation) ? "?" : next.nameAnimation;
            var last = OrbitPosePool.LastPick;
            string line = last != null && last.Value.Line == PosePoolLine.Peeping ? "窺視" : "動作線";
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 選池 [{trigger}] {label}（{line}）");
            return true;
        }

        // ─── 閒置開幹（原版 IsStart → StartProc／Insert）────────

        /// <summary>換姿落地進閒置：設對話旗＋排程約 1 秒再開幹。</summary>
        internal static void OnEnteredIdle(HScene hScene, string source)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
                return;
            if (OrbitFsmCellClassifier.Classify(hScene) != OrbitFsmCell.Idle)
                return;

            // 窺視不會進這裡（Classify 優先窺視）
            TrySetDialogVoiceFlag(hScene);
            ScheduleIdleStart(source);
        }

        internal static void ScheduleIdleStart(string source)
        {
            _idleStartArmed = true;
            _idleStartDueUnscaled = Time.unscaledTime + IdleStartPrepSeconds;
            OrbitStateMachineLog.Event("閒置", "排程開幹",
                "{\"source\":\"" + (source ?? "") + "\",\"sec\":" + IdleStartPrepSeconds.ToString("F1") + "}");
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 閒置排程開幹（約 {IdleStartPrepSeconds:F1} 秒後；來源={source}）");
        }

        internal static void CancelIdleStartSchedule()
        {
            _idleStartArmed = false;
            _idleStartDueUnscaled = -1f;
        }

        /// <summary>N＝取消準備、立刻開幹（與自動共用同一開幹條件）。</summary>
        internal static bool CancelPrepAndStartSexNow(HScene hScene, string reason)
        {
            CancelIdleStartSchedule();
            _idleStartArmed = true;
            _idleStartDueUnscaled = Time.unscaledTime; // 當幀即可
            OrbitStateMachineLog.Event("閒置", "立刻開幹",
                "{\"reason\":\"" + (reason ?? "") + "\"}");
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 立刻開幹（{reason}）");
            return true;
        }

        /// <summary>給 IsStart Harmony：準備時間到才回 true，讓原版 StartProc 走 Insert／循環。</summary>
        internal static bool ShouldForceVanillaIsStart()
        {
            if (!OrbitBehaviorHub.IsOrbitAssistActive() || !_idleStartArmed)
                return false;
            var hScene = OrbitController.TryGetHScene();
            if (hScene == null)
                return false;
            if (OrbitFsmCellClassifier.Classify(hScene) != OrbitFsmCell.Idle)
            {
                CancelIdleStartSchedule();
                return false;
            }
            if (hScene.NowChangeAnim)
                return false;
            if (Time.unscaledTime < _idleStartDueUnscaled)
                return false;

            // 觸發一次後解除，避免每幀狂觸
            CancelIdleStartSchedule();
            OrbitStateMachineLog.Event("閒置", "觸發原版開始");
            return true;
        }

        private static void TrySetDialogVoiceFlag(HScene hScene)
        {
            try
            {
                var ctrl = hScene.ctrlFlag;
                if (ctrl?.voice == null)
                    return;
                // 開場／對話旗；不等語音播完（契約 B1）
                ctrl.voice.changeTaii = true;
            }
            catch { /* ignore */ }
        }

        // ─── 高潮後 → 選池 ─────────────────────────────────────

        internal static void OnEnteredAfterIdle(HScene hScene, string source)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
                return;
            if (OrbitFsmCellClassifier.Classify(hScene) != OrbitFsmCell.AfterIdle)
                return;

            CancelIdleStartSchedule();
            ClearEntryPosePick();

            if (OrbitFsmCellClassifier.IsSpecialAfterChain(hScene))
            {
                _afterIdleArmed = true;
                _afterIdleMode = "special";
                _afterIdleDueUnscaled = -1f; // 等播完
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 高潮後特殊收尾，播完再選池（來源={source}）");
            }
            else
            {
                // 虛脫／高潮後：欣賞 N 秒 → 選池 → 開幹；不清 isFaintness（維持 D_ 姿池）
                _afterIdleArmed = true;
                _afterIdleMode = "short";
                _afterIdleDueUnscaled = Time.unscaledTime + AfterIdleAppreciateSeconds;
                bool faint = hScene.ctrlFlag != null && hScene.ctrlFlag.isFaintness;
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: 高潮後欣賞約 {AfterIdleAppreciateSeconds:F0} 秒後選池" +
                    $"（維持虛脫={(faint ? "是" : "否")}；來源={source}）");
            }
            OrbitStateMachineLog.Event("高潮後", "排程選池",
                "{\"mode\":\"" + _afterIdleMode + "\",\"sec\":" + AfterIdleAppreciateSeconds.ToString("F0")
                + ",\"source\":\"" + (source ?? "") + "\"}");
        }

        internal static void CancelAfterIdleSchedule()
        {
            _afterIdleArmed = false;
            _afterIdleDueUnscaled = -1f;
            _afterIdleMode = "";
        }

        /// <summary>手動立刻選池（可砍短餘裕／特殊收尾）。</summary>
        internal static bool RequestSelectPoolImmediate(HScene hScene, string trigger)
        {
            CancelAfterIdleSchedule();
            return RequestSelectPool(hScene, trigger);
        }

        internal static void Tick(HScene? hScene)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
            {
                _lastCell = OrbitFsmCell.Unknown;
                return;
            }

            TickEntryPosePick(hScene);

            var cell = OrbitFsmCellClassifier.Classify(hScene);
            if (cell != _lastCell)
            {
                // 高潮動畫結束進 AfterIdle 不一定有換姿落地
                if (cell == OrbitFsmCell.AfterIdle && !_afterIdleArmed)
                    OnEnteredAfterIdle(hScene, "狀態進入");
                // 進 H 第一姿已是閒置：未排進場抽姿時才補開幹
                if (cell == OrbitFsmCell.Idle
                    && _lastCell == OrbitFsmCell.Unknown
                    && !_idleStartArmed
                    && !_entryPosePickPending)
                    OnEnteredIdle(hScene, "協助開／進場");
                _lastCell = cell;
            }

            TickAfterIdleToPool(hScene);
        }

        private static void TickEntryPosePick(HScene hScene)
        {
            if (!_entryPosePickPending)
                return;

            // 已在高潮後／虛脫：改走欣賞→選池，不要立刻進場抽姿
            if (OrbitFsmCellClassifier.Classify(hScene) == OrbitFsmCell.AfterIdle
                || OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
            {
                ClearEntryPosePick();
                if (!_afterIdleArmed)
                    OnEnteredAfterIdle(hScene, "進場改高潮後欣賞");
                return;
            }

            if (hScene.NowChangeAnim
                || OrbitPoseDirector.IsPoseChangeInFlight
                || OrbitPoseDirector.Phase == DirectorState.Changing
                || OrbitPoseDirector.Phase == DirectorState.PoseQueued
                || OrbitPoseDirector.Phase == DirectorState.Rebinding)
                return;

            if (Singleton<HSceneSprite>.IsInstance() && Singleton<HSceneSprite>.Instance.isFade)
                return;

            if (RequestSelectPool(hScene, "entry"))
            {
                ClearEntryPosePick();
                OrbitStateMachineLog.Event("選池", "進場抽姿", "{}");
                return;
            }

            if (_entryPosePickDeadlineUnscaled > 0f
                && Time.unscaledTime >= _entryPosePickDeadlineUnscaled)
            {
                ClearEntryPosePick();
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: 進場抽姿逾時，沿用目前姿勢");
                if (OrbitFsmCellClassifier.Classify(hScene) == OrbitFsmCell.Idle && !_idleStartArmed)
                    OnEnteredIdle(hScene, "進場抽姿失敗");
            }
        }

        private static void TickAfterIdleToPool(HScene hScene)
        {
            if (!_afterIdleArmed)
                return;

            var cell = OrbitFsmCellClassifier.Classify(hScene);
            if (cell != OrbitFsmCell.AfterIdle)
            {
                CancelAfterIdleSchedule();
                return;
            }

            if (_afterIdleMode == "special")
            {
                if (!OrbitFsmCellClassifier.IsSpecialAfterChainFinished(hScene))
                    return;
            }
            else if (_afterIdleDueUnscaled > 0f && Time.unscaledTime < _afterIdleDueUnscaled)
            {
                return;
            }

            CancelAfterIdleSchedule();
            // 選池在虛脫下只抽 nDownPtn 姿；故意不清 isFaintness
            bool faint = hScene.ctrlFlag != null && hScene.ctrlFlag.isFaintness;
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 高潮後欣賞結束→選池（維持虛脫={(faint ? "是" : "否")}）");
            RequestSelectPool(hScene, "高潮後自動");
        }

        // ─── 動作橋段加速 ─────────────────────────────────────

        internal static bool AccelerateActionBridge(HScene hScene, string reason)
        {
            if (hScene == null)
                return false;
            var ctrl = hScene.ctrlFlag;
            if (ctrl == null)
                return false;

            try
            {
                float feel = 0f;
                try { feel = (float)(TraverseFeel(ctrl) ?? 0f); } catch { /* ignore */ }
                feel = Mathf.Clamp01(feel + BridgeAccelerateFeelBoost);
                SetFeel(ctrl, feel);

                ctrl.speed = Mathf.Clamp(ctrl.speed + BridgeAccelerateSpeedBoost, 0f, 3f);
            }
            catch { /* ignore */ }

            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 動作橋段加速（{reason}）");
            OrbitStateMachineLog.Event("橋段", "加速", "{\"reason\":\"" + (reason ?? "") + "\"}");
            return true;
        }

        private static object? TraverseFeel(HSceneFlagCtrl ctrl)
        {
            var t = HarmonyLib.Traverse.Create(ctrl);
            if (t.Field("feel_f").FieldExists())
                return t.Field("feel_f").GetValue();
            return null;
        }

        private static void SetFeel(HSceneFlagCtrl ctrl, float value)
        {
            var t = HarmonyLib.Traverse.Create(ctrl);
            if (t.Field("feel_f").FieldExists())
                t.Field("feel_f").SetValue(value);
        }

        // ─── L／N 依格分流 ─────────────────────────────────────

        /// <summary>L＝換姿／手動選池（各格皆選池）。</summary>
        internal static bool HandleL(HScene hScene)
        {
            var cell = OrbitFsmCellClassifier.Classify(hScene);
            return RequestSelectPoolImmediate(hScene, "L・" + OrbitFsmCellClassifier.CellDisplayName(cell));
        }

        /// <summary>N＝該格往前推。</summary>
        internal static bool HandleN(HScene hScene)
        {
            var cell = OrbitFsmCellClassifier.Classify(hScene);
            switch (cell)
            {
                case OrbitFsmCell.Idle:
                    return CancelPrepAndStartSexNow(hScene, "N");

                case OrbitFsmCell.ActionBridge:
                    return AccelerateActionBridge(hScene, "N");

                case OrbitFsmCell.AfterIdle:
                case OrbitFsmCell.Peeping:
                    return RequestSelectPoolImmediate(hScene, "N・" + OrbitFsmCellClassifier.CellDisplayName(cell));

                default:
                    // 未辨認：若在高潮動畫中，仍允許選池（契約不擋高潮）
                    return RequestSelectPoolImmediate(hScene, "N・未辨認");
            }
        }
    }
}
