using System.Collections;
using System.Reflection;
using HarmonyLib;
using Manager;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Assist strategy, throttling, selection sanitize/recovery.
    /// <see cref="CanAutoAdvance"/> is the single gate for auto-action / checkpoint.
    /// Only this class clears <c>selectAnimationListInfo</c> via <see cref="ClearSelection"/>.
    /// </summary>
    internal static class OrbitBehaviorHub
    {
        private const float ManualSelectionSuppressSeconds = 3.0f;
        private const float OrbitAutoActionGraceSeconds = 2.5f;
        private const float AutoActionNullSelectionMinSeconds = 1.5f;
        private const float OrgasmAssistQuietSeconds = 2.0f;

        private static float _manualSelectionSuppressUntilUnscaled = -1f;
        private static float _orbitAutoActionGraceUntilUnscaled = -1f;
        private static float _autoActionNullSelectionSinceUnscaled = -1f;
        private static float _checkpointInvokeCooldownUntilUnscaled = -1f;
        private static float _lastAssistFlagPushTimeUnscaled = -999f;
        private static float _lastCheckpointInvokeTimeUnscaled = -999f;
        private static float _orgasmAssistQuietUntilUnscaled = -1f;

        private static bool _orbitAssistActive;
        private static float _checkpointIdleTime;
        private static MethodInfo? _getAutoAnimationMethod;
        private static FieldInfo? _isAutoActionChangeField;
        private static PropertyInfo? _isAutoActionChangeProp;
        private static float _wheelBypassStartUnscaled = -1f;
        private static float _afterIdleAutoEscapeSinceUnscaled = -1f;
        private static float _idleAutoEscapeSinceUnscaled = -1f;

        /// <summary>L / real wheel / cycle asked to leave waits or open A+B auto; cleared by latch rules.</summary>
        private static bool _motionEscapeLatched;
        private static bool _motionEscapeSawWait;
        private static string _motionEscapeReason = "";
        private static bool _poseKickInFlight;

        internal const float WheelBypassValue = 0.10f;
        internal const float WheelBypassDelaySeconds = 2f;

        internal static bool IsOrbitAssistActive() => _orbitAssistActive;
        internal static bool IsPoseKickInFlight => _poseKickInFlight;

        internal static void NotifyManualUiClick()
        {
            _manualSelectionSuppressUntilUnscaled = Time.unscaledTime + ManualSelectionSuppressSeconds;
        }

        internal static void NotifyManualHotkeyCompleted(HScene? hScene)
        {
            ResetNullSelectionTracking();
            _autoActionNullSelectionSinceUnscaled = Time.unscaledTime - AutoActionNullSelectionMinSeconds;
            _checkpointIdleTime = 0f;
            ResetCheckpointInvokeCooldown();

            if (hScene?.ctrlFlag == null)
                return;
            if (HS2OrbitAndExciter.OrbitAutoActionEnabled?.Value != true)
                return;
            TryKickAutoActionAfterManualHotkey(hScene.ctrlFlag);
        }

        private static void TryKickAutoActionAfterManualHotkey(HSceneFlagCtrl ctrlFlag)
        {
            if (!CanAutoAdvance(ctrlFlag, out _))
                return;
            if (ctrlFlag.selectAnimationListInfo != null)
                return;

            ctrlFlag.isAutoActionChange = true;
            try
            {
                Traverse.Create(ctrlFlag).Field("initiative").SetValue(1);
            }
            catch { /* ignore */ }
        }

        internal static void NotifyUiHoverWhileOrbit()
        {
            float hoverUntil = Time.unscaledTime + 0.25f;
            if (hoverUntil > _manualSelectionSuppressUntilUnscaled)
                _manualSelectionSuppressUntilUnscaled = hoverUntil;
        }

        internal static void NotifyFemaleOrgasm(HSceneFlagCtrl? ctrlFlag)
        {
            if (ctrlFlag == null)
                return;

            BeginOrgasmAssistQuiet();
            ResetNullSelectionTracking();

            OrbitOrgasmTattoo.OnOrgasm(ctrlFlag);
            OrbitOrgasmBustGrowth.OnOrgasm(ctrlFlag);
            OrbitOrgasmNippleSpray.OnOrgasm(ctrlFlag);
            OrbitVoiceTour.OnFemaleOrgasm();
        }

        internal static void NotifyVoiceTourHit(string triggerLabel)
        {
            BeginOrgasmAssistQuiet();
            _ = triggerLabel;
        }

        private static void BeginOrgasmAssistQuiet()
        {
            _orgasmAssistQuietUntilUnscaled = Time.unscaledTime + OrgasmAssistQuietSeconds;
            _checkpointIdleTime = 0f;
        }

        internal static void NotifyOrbitToggled(bool active)
        {
            _orbitAssistActive = active;
            _checkpointIdleTime = 0f;
            _wheelBypassStartUnscaled = -1f;
            _afterIdleAutoEscapeSinceUnscaled = -1f;
            _idleAutoEscapeSinceUnscaled = -1f;
            _orgasmAssistQuietUntilUnscaled = -1f;
            ClearMotionEscapeLatch();
            _poseKickInFlight = false;
            if (active)
            {
                _orbitAutoActionGraceUntilUnscaled = Time.unscaledTime + OrbitAutoActionGraceSeconds;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
                OrbitPoseDirector.Reset();
                OrbitManualDirector.Reset();
                OrbitStatusHud.NotifyOrbitActivated();
            }
            else
            {
                _orbitAutoActionGraceUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
                OrbitPoseDirector.Reset();
                OrbitManualDirector.Reset();
            }
        }

        /// <summary>
        /// Single gate for auto-action flag push and checkpoint GetAutoAnimation.
        /// PosePending does NOT block. Does not clear initiative when false.
        /// </summary>
        internal static bool CanAutoAdvance(HSceneFlagCtrl? ctrlFlag, out string reason)
        {
            if (OrbitManualDirector.IsBusy)
            {
                reason = OrbitAssistReasons.ManualBusy;
                return false;
            }

            var phase = OrbitPoseDirector.Phase;
            if (phase == DirectorState.Changing)
            {
                reason = OrbitAssistReasons.Changing;
                return false;
            }
            if (phase == DirectorState.Rebinding)
            {
                reason = OrbitAssistReasons.Rebinding;
                return false;
            }
            if (phase == DirectorState.PoseQueued
                || (ctrlFlag != null && ctrlFlag.selectAnimationListInfo != null))
            {
                reason = OrbitAssistReasons.PoseQueued;
                return false;
            }

            var hScene = OrbitController.TryGetHScene();
            if (hScene != null && hScene.NowChangeAnim)
            {
                reason = OrbitAssistReasons.Changing;
                return false;
            }

            if (ctrlFlag != null && ctrlFlag.nowOrgasm)
            {
                reason = OrbitAssistReasons.NowOrgasm;
                return false;
            }
            if (Time.unscaledTime < _orgasmAssistQuietUntilUnscaled)
            {
                reason = OrbitAssistReasons.OrgasmQuiet;
                return false;
            }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                reason = OrbitAssistReasons.PointerOverUi;
                return false;
            }
            if (Time.unscaledTime < _orbitAutoActionGraceUntilUnscaled)
            {
                reason = OrbitAssistReasons.OrbitStartGrace;
                return false;
            }
            if (ctrlFlag != null && ctrlFlag.inputForcus)
            {
                reason = OrbitAssistReasons.InputForcus;
                return false;
            }
            if (Input.GetMouseButton(0))
            {
                reason = OrbitAssistReasons.MouseHolding;
                return false;
            }
            if (Time.unscaledTime < _manualSelectionSuppressUntilUnscaled)
            {
                reason = OrbitAssistReasons.RecentUiClick;
                return false;
            }

            // A+B long bath/toilet/shower: do not auto-pick next pose until L/wheel/cycle latch.
            // Cycle pose RequestPoseChange latches first, then CanAcceptRequest may still pass.
            if (hScene != null
                && OrbitHelpers.IsLongAppreciationPose(hScene)
                && !IsMotionEscapeArmed())
            {
                // Drop leftover isAutoActionChange only (keep initiative).
                if (ctrlFlag != null && ctrlFlag.isAutoActionChange)
                    ctrlFlag.isAutoActionChange = false;
                reason = OrbitAssistReasons.LongAppreciation;
                return false;
            }

            reason = OrbitAssistReasons.None;
            return true;
        }

        /// <summary>Cycle may write sel when UI/orgasm allow; PosePending does not block.</summary>
        internal static bool CanQueueCyclePose(HSceneFlagCtrl? ctrlFlag, out string reason)
        {
            if (OrbitManualDirector.IsBusy)
            {
                reason = OrbitAssistReasons.ManualBusy;
                return false;
            }
            if (ctrlFlag != null && ctrlFlag.nowOrgasm)
            {
                reason = OrbitAssistReasons.NowOrgasm;
                return false;
            }
            if (Time.unscaledTime < _orgasmAssistQuietUntilUnscaled)
            {
                reason = OrbitAssistReasons.OrgasmQuiet;
                return false;
            }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                reason = OrbitAssistReasons.PointerOverUi;
                return false;
            }
            if (Time.unscaledTime < _orbitAutoActionGraceUntilUnscaled)
            {
                reason = OrbitAssistReasons.OrbitStartGrace;
                return false;
            }
            if (ctrlFlag != null && ctrlFlag.inputForcus)
            {
                reason = OrbitAssistReasons.InputForcus;
                return false;
            }
            if (Input.GetMouseButton(0))
            {
                reason = OrbitAssistReasons.MouseHolding;
                return false;
            }
            if (Time.unscaledTime < _manualSelectionSuppressUntilUnscaled)
            {
                reason = OrbitAssistReasons.RecentUiClick;
                return false;
            }
            reason = OrbitAssistReasons.None;
            return true;
        }

        /// <summary>Backward-compatible alias: true when auto-advance must not run.</summary>
        internal static bool ShouldSuppressAssist(HSceneFlagCtrl? ctrlFlag, out string reason) =>
            !CanAutoAdvance(ctrlFlag, out reason);

        internal static float RemainingOrgasmQuietSeconds()
        {
            if (_orgasmAssistQuietUntilUnscaled < 0f) return 0f;
            return Mathf.Max(0f, _orgasmAssistQuietUntilUnscaled - Time.unscaledTime);
        }

        internal static float RemainingOrbitStartGraceSeconds()
        {
            if (_orbitAutoActionGraceUntilUnscaled < 0f) return 0f;
            return Mathf.Max(0f, _orbitAutoActionGraceUntilUnscaled - Time.unscaledTime);
        }

        internal static float RemainingManualUiSuppressSeconds()
        {
            if (_manualSelectionSuppressUntilUnscaled < 0f) return 0f;
            return Mathf.Max(0f, _manualSelectionSuppressUntilUnscaled - Time.unscaledTime);
        }

        /// <summary>Seconds until timed AfterIdle auto-leave (0 if latched, not in AfterIdle, or ready).</summary>
        internal static float RemainingAfterIdleAutoEscapeSeconds()
        {
            if (!_orbitAssistActive || IsMotionEscapeArmed())
                return 0f;
            var hScene = OrbitController.TryGetHScene();
            if (hScene == null || !OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
                return 0f;
            if (_afterIdleAutoEscapeSinceUnscaled < 0f)
                return WheelBypassDelaySeconds;
            return Mathf.Max(0f, WheelBypassDelaySeconds - (Time.unscaledTime - _afterIdleAutoEscapeSinceUnscaled));
        }

        /// <summary>
        /// Seconds until timed Idle auto-leave (0 if A+B, latched, not Idle, or ready).
        /// </summary>
        internal static float RemainingIdleAutoEscapeSeconds()
        {
            if (!_orbitAssistActive || IsMotionEscapeArmed())
                return 0f;
            var hScene = OrbitController.TryGetHScene();
            if (hScene == null || !OrbitHelpers.IsFirstFemaleInIdle(hScene) || hScene.NowChangeAnim)
                return 0f;
            if (OrbitHelpers.IsLongAppreciationPose(hScene))
                return 0f;
            if (_idleAutoEscapeSinceUnscaled < 0f)
                return WheelBypassDelaySeconds;
            return Mathf.Max(0f, WheelBypassDelaySeconds - (Time.unscaledTime - _idleAutoEscapeSinceUnscaled));
        }

        internal static bool ShouldDeferOrbitFocusHotkeysToGame(HSceneFlagCtrl? ctrlFlag) =>
            ctrlFlag != null && ctrlFlag.inputForcus;

        internal static bool CanAccumulateFeelDuringOrbit(HScene? hScene, bool waitingForPrepStart)
        {
            if (waitingForPrepStart) return false;
            return OrbitHelpers.IsFirstFemaleInActionLoop(hScene);
        }

        internal static void ResetNullSelectionTracking()
        {
            _autoActionNullSelectionSinceUnscaled = -1f;
        }

        internal static bool IsNullSelectionReadyForAssist()
        {
            if (_autoActionNullSelectionSinceUnscaled < 0f)
                _autoActionNullSelectionSinceUnscaled = Time.unscaledTime;

            float elapsed = Time.unscaledTime - _autoActionNullSelectionSinceUnscaled;
            return elapsed >= AutoActionNullSelectionMinSeconds;
        }

        internal static bool TryConsumeAssistFlagPush(out string reason)
        {
            float minInterval = Mathf.Max(0f, HS2OrbitAndExciter.AutoAssistMinIntervalSeconds?.Value ?? 0f);
            if (minInterval > 0f)
            {
                float elapsed = Time.unscaledTime - _lastAssistFlagPushTimeUnscaled;
                if (elapsed < minInterval)
                {
                    reason = OrbitAssistReasons.AssistInterval;
                    return false;
                }
            }
            _lastAssistFlagPushTimeUnscaled = Time.unscaledTime;
            reason = OrbitAssistReasons.None;
            return true;
        }

        internal static void ResetCheckpointInvokeCooldown()
        {
            _checkpointInvokeCooldownUntilUnscaled = -1f;
        }

        internal static bool IsCheckpointInvokeOnLegacyCooldown()
        {
            return Time.unscaledTime < _checkpointInvokeCooldownUntilUnscaled;
        }

        internal static void MarkCheckpointInvokeLegacyCooldown(float timeoutSeconds)
        {
            float cooldownSec = Mathf.Max(4f, timeoutSeconds * 3f);
            _checkpointInvokeCooldownUntilUnscaled = Time.unscaledTime + cooldownSec;
        }

        internal static bool TryConsumeCheckpointInvoke(out string reason)
        {
            if (IsCheckpointInvokeOnLegacyCooldown())
            {
                reason = OrbitAssistReasons.CheckpointLegacyCooldown;
                return false;
            }

            float minInterval = Mathf.Max(0f, HS2OrbitAndExciter.AutoAssistMinIntervalSeconds?.Value ?? 0f);
            if (minInterval > 0f)
            {
                float elapsed = Time.unscaledTime - _lastCheckpointInvokeTimeUnscaled;
                if (elapsed < minInterval)
                {
                    reason = OrbitAssistReasons.CheckpointInterval;
                    return false;
                }
            }

            _lastCheckpointInvokeTimeUnscaled = Time.unscaledTime;
            reason = OrbitAssistReasons.None;
            return true;
        }

        /// <summary>
        /// Sole owner of clearing selectAnimationListInfo (and optional NowChangeAnim stuck).
        /// Notifies Director afterward. May clear isAutoActionChange on the selection only.
        /// </summary>
        internal static void ClearSelection(HScene? hScene, string reason, bool forceClearNowChangeAnim = false)
        {
            var ctrlFlag = hScene?.ctrlFlag;
            int id = -1;
            int down = -1;
            if (ctrlFlag?.selectAnimationListInfo != null)
            {
                id = ctrlFlag.selectAnimationListInfo.id;
                down = ctrlFlag.selectAnimationListInfo.nDownPtn;
                ctrlFlag.selectAnimationListInfo = null;
                ctrlFlag.isAutoActionChange = false;
            }

            if (forceClearNowChangeAnim && hScene != null)
            {
                try
                {
                    Traverse.Create(hScene).Field("nowChangeAnim").SetValue(false);
                }
                catch
                {
                    try { Traverse.Create(hScene).Property("NowChangeAnim").SetValue(false); }
                    catch { /* ignore */ }
                }
            }

            OrbitPoseDirector.NotifySelectionCleared();
            // Drop escape latch for aborted / orphan clears — NOT ClearedPoseAlreadyApplied
            // (Idle/AfterIdle land must keep latch for PoseLandedPolicy / Tick*Escape).
            if (reason == OrbitAssistReasons.PoseKickDone
                || reason == OrbitAssistReasons.ClearedAfterTimeout
                || reason == OrbitAssistReasons.ClearedNowChangeStuck)
                ClearMotionEscapeLatch();
            OrbitStateMachineLog.Event("stale_sel", reason,
                "{\"id\":" + id + ",\"down\":" + down + "}");
        }

        /// <summary>P0: drop illegal faintness selection immediately. Returns true if cleared.</summary>
        internal static bool SanitizeSelectedPose(HScene? hScene)
        {
            var ctrlFlag = hScene?.ctrlFlag;
            if (ctrlFlag?.selectAnimationListInfo == null)
                return false;
            if (OrbitHelpers.IsPoseAllowedUnderFaintness(ctrlFlag.selectAnimationListInfo, ctrlFlag))
                return false;

            ClearSelection(hScene, OrbitAssistReasons.ClearedFaintnessInvalid);
            return true;
        }

        /// <summary>Single path for auto-action assist. Does not clear initiative when gated off.</summary>
        internal static bool TryPushOrbitAutoActionAssist(HSceneFlagCtrl? ctrlFlag)
        {
            if (HS2OrbitAndExciter.OrbitAutoActionEnabled?.Value != true)
                return false;
            if (ctrlFlag == null)
                return false;
            if (!CanAutoAdvance(ctrlFlag, out _))
            {
                ResetNullSelectionTracking();
                return false;
            }
            if (ctrlFlag.selectAnimationListInfo != null)
            {
                ResetNullSelectionTracking();
                return false;
            }
            if (!IsNullSelectionReadyForAssist())
                return false;
            if (!TryConsumeAssistFlagPush(out _))
                return false;

            var flagType = ctrlFlag.GetType();
            if (_isAutoActionChangeField == null && _isAutoActionChangeProp == null)
            {
                _isAutoActionChangeField = flagType.GetField("isAutoActionChange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (_isAutoActionChangeField == null)
                    _isAutoActionChangeProp = flagType.GetProperty("isAutoActionChange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            try
            {
                if (_isAutoActionChangeField != null)
                    _isAutoActionChangeField.SetValue(ctrlFlag, true);
                else
                    _isAutoActionChangeProp?.SetValue(ctrlFlag, true, null);
            }
            catch { /* ignore */ }
            Traverse.Create(ctrlFlag).Field("initiative").SetValue(1);
            OrbitStateMachineLog.Event("assist", "push_auto_action");
            return true;
        }

        internal static void TickOrbitCheckpointAssist(HScene? hScene, float deltaTime)
        {
            float timeout = HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds?.Value ?? 2f;
            if (timeout <= 0f || hScene == null) return;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null) return;

            if (OrbitHelpers.IsFirstFemaleInActionLoop(hScene))
            {
                _checkpointIdleTime = 0f;
                return;
            }
            if (!CanAutoAdvance(ctrlFlag, out _))
            {
                _checkpointIdleTime = 0f;
                return;
            }
            if (ctrlFlag.selectAnimationListInfo != null)
            {
                _checkpointIdleTime = 0f;
                ResetCheckpointInvokeCooldown();
                return;
            }
            if (Input.GetMouseButton(0))
            {
                _checkpointIdleTime = 0f;
                return;
            }
            if (IsCheckpointInvokeOnLegacyCooldown())
                return;

            _checkpointIdleTime += deltaTime;
            if (_checkpointIdleTime < timeout) return;
            _checkpointIdleTime = 0f;
            if (!TryConsumeCheckpointInvoke(out _))
                return;

            if (_getAutoAnimationMethod == null)
            {
                _getAutoAnimationMethod = typeof(HScene).GetMethod("GetAutoAnimation", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_getAutoAnimationMethod == null)
                    return;
            }
            try
            {
                _getAutoAnimationMethod.Invoke(hScene, new object[] { false });
                if (ctrlFlag.selectAnimationListInfo == null)
                    _getAutoAnimationMethod.Invoke(hScene, new object[] { true });
            }
            catch { /* ignore */ }

            // P0: same-frame sanitize — never leave nDownPtn==0 under faintness.
            if (SanitizeSelectedPose(hScene))
            {
                OrbitStateMachineLog.Event("checkpoint", "get_auto_sanitized_faint");
                MarkCheckpointInvokeLegacyCooldown(timeout);
                return;
            }

            MarkCheckpointInvokeLegacyCooldown(timeout);

            bool hasSelAfter = ctrlFlag.selectAnimationListInfo != null;
            if (hasSelAfter)
                OrbitPoseDirector.SyncFromGameFlags(hScene);

            OrbitStateMachineLog.Event("checkpoint", hasSelAfter ? "get_auto_ok" : "get_auto_empty_speed_bump");
            if (!hasSelAfter)
            {
                try
                {
                    const float FallbackSpeedBump = 1.2f;
                    Traverse.Create(ctrlFlag).Field("speed").SetValue(FallbackSpeedBump);
                }
                catch { /* ignore */ }
            }
        }

        internal static bool TryInjectOrbitWheelBypass(ref float wheel)
        {
            if (!IsOrbitAssistActive() || wheel != 0f)
            {
                _wheelBypassStartUnscaled = -1f;
                return false;
            }
            var hScene = OrbitController.TryGetHScene();
            if (hScene?.ctrlFlag != null && hScene.ctrlFlag.nowOrgasm)
            {
                _wheelBypassStartUnscaled = -1f;
                return false;
            }
            if (!OrbitBypassAnimatorGate.IsBypassAllowedForCurrentHScene())
            {
                _wheelBypassStartUnscaled = -1f;
                return false;
            }

            // A+B long poses: fake wheel only after L / real scroll / cycle arms escape.
            // Exception: post-orgasm AfterIdle (Orgasm_*_A) still uses timed bypass even on those pose ids.
            if (OrbitHelpers.IsLongAppreciationPose(hScene)
                && !OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
            {
                _wheelBypassStartUnscaled = -1f;
                if (!IsMotionEscapeArmed())
                    return false;
                wheel = WheelBypassValue;
                return true;
            }

            // Short orgasm AfterIdle / normal Idle / Insert: timed fake-wheel advance.
            if (_wheelBypassStartUnscaled < 0f)
                _wheelBypassStartUnscaled = Time.unscaledTime;
            float elapsed = Time.unscaledTime - _wheelBypassStartUnscaled;
            if (elapsed < WheelBypassDelaySeconds)
                return false;
            _wheelBypassStartUnscaled = -1f;
            wheel = WheelBypassValue;
            return true;
        }

        /// <summary>
        /// Latch leave / open A+B auto until wait state is exited, pose finishes, or orbit off.
        /// No time window.
        /// </summary>
        internal static void RequestMotionEscape(string reason)
        {
            if (!_orbitAssistActive)
                return;
            _motionEscapeLatched = true;
            _motionEscapeReason = reason ?? "";
            OrbitStateMachineLog.Event("escape", "request",
                "{\"reason\":\"" + (_motionEscapeReason.Replace("\"", "") ?? "") + "\"}");
        }

        internal static bool IsMotionEscapeArmed() =>
            _orbitAssistActive && _motionEscapeLatched;

        internal static void ClearMotionEscapeLatch()
        {
            _motionEscapeLatched = false;
            _motionEscapeSawWait = false;
            _motionEscapeReason = "";
        }

        /// <summary>
        /// Clear latch when leaving Idle/AfterIdle after having been in a wait while latched.
        /// Arming during action loop keeps latch until pose change / orbit off.
        /// </summary>
        internal static void TickMotionEscapeLatch(HScene? hScene)
        {
            if (!_orbitAssistActive || !_motionEscapeLatched || hScene == null)
                return;
            bool inWait = OrbitHelpers.IsFirstFemaleInIdle(hScene)
                          || OrbitHelpers.IsFirstFemaleInAfterIdle(hScene);
            if (_motionEscapeSawWait && !inWait)
            {
                ClearMotionEscapeLatch();
                OrbitStateMachineLog.Event("escape", "clear_left_wait");
                return;
            }
            if (inWait)
                _motionEscapeSawWait = true;
        }

        /// <summary>
        /// Force leave AfterIdle: short orgasm waits auto after ≈2s (even on A+B pose ids),
        /// or immediately when escape latched.
        /// </summary>
        internal static bool ShouldForceAfterIdleEscape()
        {
            if (!_orbitAssistActive)
            {
                _afterIdleAutoEscapeSinceUnscaled = -1f;
                return false;
            }
            var hScene = OrbitController.TryGetHScene();
            if (hScene == null || !OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
            {
                _afterIdleAutoEscapeSinceUnscaled = -1f;
                return false;
            }
            if (IsMotionEscapeArmed())
                return true;
            // Orgasm_*_A is always the short wait — never gate on long-appreciation pose id.
            if (_afterIdleAutoEscapeSinceUnscaled < 0f)
                _afterIdleAutoEscapeSinceUnscaled = Time.unscaledTime;
            return Time.unscaledTime - _afterIdleAutoEscapeSinceUnscaled >= WheelBypassDelaySeconds;
        }

        /// <summary>
        /// Force leave Idle: A+B long poses require escape latch; other Idle uses ≈2s auto (or latch).
        /// </summary>
        internal static bool ShouldForceIdleEscape()
        {
            if (!_orbitAssistActive)
            {
                _idleAutoEscapeSinceUnscaled = -1f;
                return false;
            }
            var hScene = OrbitController.TryGetHScene();
            if (hScene == null || !OrbitHelpers.IsFirstFemaleInIdle(hScene) || hScene.NowChangeAnim)
            {
                _idleAutoEscapeSinceUnscaled = -1f;
                return false;
            }
            if (OrbitHelpers.IsLongAppreciationPose(hScene))
                return IsMotionEscapeArmed();
            if (IsMotionEscapeArmed())
                return true;
            if (_idleAutoEscapeSinceUnscaled < 0f)
                _idleAutoEscapeSinceUnscaled = Time.unscaledTime;
            return Time.unscaledTime - _idleAutoEscapeSinceUnscaled >= WheelBypassDelaySeconds;
        }

        /// <summary>
        /// When sel is set but HScene.Update never starts ChangeAnimation (seen on faint D_WLoop),
        /// start the same coroutine vanilla would. Idempotent while in-flight.
        /// </summary>
        internal static bool TryKickQueuedChangeAnimation(HScene? hScene)
        {
            if (_poseKickInFlight || hScene == null || !IsOrbitAssistActive())
                return false;
            var ctrlFlag = hScene.ctrlFlag;
            var sel = ctrlFlag?.selectAnimationListInfo;
            if (sel == null || hScene.NowChangeAnim)
                return false;
            if (!OrbitHelpers.IsPoseAllowedUnderFaintness(sel, ctrlFlag))
                return false;

            _poseKickInFlight = true;
            int id = sel.id;
            int down = sel.nDownPtn;
            OrbitStateMachineLog.Event("pose_kick", OrbitAssistReasons.PoseKickStart,
                "{\"id\":" + id + ",\"down\":" + down + "}");
            hScene.StartCoroutine(KickChangeAnimationCo(hScene, sel));
            return true;
        }

        private static IEnumerator KickChangeAnimationCo(HScene hScene, HScene.AnimationListInfo sel)
        {
            try
            {
                bool useFade = true;
                try
                {
                    if (hScene.ctrlFlag != null && hScene.ctrlFlag.pointMoveAnimChange)
                        useFade = false;
                }
                catch { /* ignore */ }

                yield return hScene.StartCoroutine(
                    hScene.ChangeAnimation(sel, _isForceResetCamera: false, _isForceLoopAction: false, _UseFade: useFade));
            }
            finally
            {
                _poseKickInFlight = false;
                // Pose already applied but flags left sticky (common after kick∥vanilla race).
                if (TryResolveAppliedPoseChange(hScene))
                {
                    OrbitStateMachineLog.Event("pose_kick", "done_resolved_applied");
                    OrbitPoseLandedPolicy.OnPoseLanded(hScene, PoseLandedSource.Kick);
                }
                else if (hScene != null && hScene.NowChangeAnim)
                {
                    OrbitStateMachineLog.Event("pose_kick", "done_leave_inflight");
                }
                else if (hScene?.ctrlFlag?.selectAnimationListInfo != null)
                {
                    ClearSelection(hScene, OrbitAssistReasons.PoseKickDone);
                    try { hScene.ctrlFlag.isAutoActionChange = false; } catch { /* ignore */ }
                    OrbitStateMachineLog.Event("pose_kick", OrbitAssistReasons.PoseKickDone);
                }
                else
                {
                    try
                    {
                        if (hScene?.ctrlFlag != null)
                            hScene.ctrlFlag.isAutoActionChange = false;
                    }
                    catch { /* ignore */ }
                    ClearMotionEscapeLatch();
                    OrbitStateMachineLog.Event("pose_kick", "done_clean");
                }
            }
        }

        /// <summary>
        /// True when sel is already the current pose but NowChangeAnim/sel were left sticky —
        /// clears both (no timer). Used by kick finally and per-frame recovery.
        /// Does not clear escape latch while still in Idle/AfterIdle (PoseLandedPolicy owns that).
        /// </summary>
        internal static bool TryResolveAppliedPoseChange(HScene? hScene)
        {
            if (hScene?.ctrlFlag == null || _poseKickInFlight)
                return false;
            var ctrlFlag = hScene.ctrlFlag;
            var sel = ctrlFlag.selectAnimationListInfo;
            var now = ctrlFlag.nowAnimationInfo;
            if (sel == null || now == null)
                return false;
            if (sel.id != now.id)
                return false;
            // Same pose already live; sticky NowChangeAnim and/or leftover sel locks L/auto.
            // ClearedPoseAlreadyApplied does not clear latch inside ClearSelection.
            ClearSelection(hScene, OrbitAssistReasons.ClearedPoseAlreadyApplied, forceClearNowChangeAnim: true);
            try { ctrlFlag.isAutoActionChange = false; } catch { /* ignore */ }
            // Non-wait land: drop latch. Wait land: keep for policy / Tick*Escape.
            if (!OrbitHelpers.IsFirstFemaleInIdle(hScene)
                && !OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
                ClearMotionEscapeLatch();
            return true;
        }

        /// <summary>
        /// Force start action loop (WLoop/D_WLoop) from Idle / AfterIdle / Insert.
        /// Used by N hotkey and after pose lands on Idle.
        /// </summary>
        internal static bool TryForceStartSex(HScene? hScene, string reason = "N")
        {
            if (hScene == null || !IsOrbitAssistActive())
                return false;

            TryResolveAppliedPoseChange(hScene);
            RequestMotionEscape(reason);

            if (OrbitHelpers.IsFirstFemaleInActionLoop(hScene))
            {
                OrbitStateMachineLog.Event("startsex", "already_loop",
                    "{\"reason\":\"" + (reason ?? "") + "\"}");
                return true;
            }

            var ctrlFlag = hScene.ctrlFlag;
            bool faint = ctrlFlag != null && ctrlFlag.isFaintness && ctrlFlag.FaintnessType != 2;
            string anim = faint ? "D_WLoop" : "WLoop";

            if (!TryForceFemaleAnim(hScene, anim))
            {
                OrbitStateMachineLog.Event("startsex", "setPlay_fail",
                    "{\"anim\":\"" + anim + "\"}");
                return false;
            }

            if (ctrlFlag != null)
            {
                ctrlFlag.speed = 1f;
                ctrlFlag.nowOrgasm = false;
                try { Traverse.Create(ctrlFlag).Field("loopType").SetValue(0); } catch { /* ignore */ }
                ctrlFlag.isAutoActionChange = true;
                try { Traverse.Create(ctrlFlag).Field("initiative").SetValue(1); } catch { /* ignore */ }
            }

            try
            {
                var auto = hScene.ctrlAuto;
                auto?.ReStartInit();
                auto?.PullInit();
            }
            catch { /* ignore */ }

            OrbitStateMachineLog.Event("startsex", "force_cha_setPlay",
                "{\"anim\":\"" + anim + "\",\"reason\":\"" + (reason ?? "") + "\"}");
            return true;
        }

        /// <summary>If armed and still in AfterIdle, force WLoop/D_WLoop.</summary>
        internal static void TickAfterIdleEscape(HScene? hScene)
        {
            if (hScene == null || !ShouldForceAfterIdleEscape())
                return;

            var ctrlFlag = hScene.ctrlFlag;
            bool faint = ctrlFlag != null && ctrlFlag.isFaintness && ctrlFlag.FaintnessType != 2;
            string anim = faint ? "D_WLoop" : "WLoop";

            if (!TryForceFemaleAnim(hScene, anim))
                return;

            if (ctrlFlag != null)
            {
                ctrlFlag.speed = 1f;
                ctrlFlag.nowOrgasm = false;
                try { Traverse.Create(ctrlFlag).Field("loopType").SetValue(0); } catch { /* ignore */ }
            }

            // Match Sonyu AfterIdle escape: leave auto wait state so later sel can be consumed.
            try
            {
                var auto = hScene.ctrlAuto;
                auto?.ReStartInit();
                auto?.PullInit();
            }
            catch { /* ignore */ }

            OrbitStateMachineLog.Event("afteridle", "force_cha_setPlay",
                "{\"anim\":\"" + anim + "\",\"reason\":\"" + _motionEscapeReason + "\"}");
        }

        /// <summary>If armed and still in Idle, force WLoop/D_WLoop.</summary>
        internal static void TickIdleEscape(HScene? hScene)
        {
            if (hScene == null || !ShouldForceIdleEscape())
                return;

            var ctrlFlag = hScene.ctrlFlag;
            bool faint = ctrlFlag != null && ctrlFlag.isFaintness && ctrlFlag.FaintnessType != 2;
            string anim = faint ? "D_WLoop" : "WLoop";

            if (!TryForceFemaleAnim(hScene, anim))
                return;

            if (ctrlFlag != null)
            {
                ctrlFlag.speed = 1f;
                try { Traverse.Create(ctrlFlag).Field("loopType").SetValue(0); } catch { /* ignore */ }
                ctrlFlag.isAutoActionChange = true;
                try { Traverse.Create(ctrlFlag).Field("initiative").SetValue(1); } catch { /* ignore */ }
            }

            OrbitStateMachineLog.Event("idle", "force_cha_setPlay",
                "{\"anim\":\"" + anim + "\",\"reason\":\"" + _motionEscapeReason + "\"}");
        }

        /// <summary>Real mouse wheel (not fake bypass) while orbit: arm escape from long waits.</summary>
        internal static void TickUserWheelEscape()
        {
            if (!_orbitAssistActive)
                return;
            float axis = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(axis) < 0.01f)
                return;
            RequestMotionEscape("wheel");
        }

        private static bool TryForceFemaleAnim(HScene hScene, string anim)
        {
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            if (cha == null) return false;
            try
            {
                cha.setPlay(anim, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Per-frame pose-change invariants (no timer stale clears):
        /// Sanitize → Resolve(sel==now) → Kick(Queued) → orphan NowChangeAnim.
        /// Landed next-step is owned by <see cref="OrbitPoseLandedPolicy"/>.
        /// </summary>
        internal static void TickPoseFlagRecovery(HScene? hScene)
        {
            if (hScene == null)
                return;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
                return;

            if (SanitizeSelectedPose(hScene))
                return;

            // Invariant: sel.id == now.id → resolve (Landed).
            if (TryResolveAppliedPoseChange(hScene))
            {
                OrbitPoseLandedPolicy.OnPoseLanded(hScene, PoseLandedSource.Resolve);
                AssertPoseChangeInvariants(hScene, "after_resolve");
                return;
            }

            // Invariant: Queued ∧ ¬NowChangeAnim ∧ ¬kickInFlight → kick.
            if (ctrlFlag.selectAnimationListInfo != null
                && !hScene.NowChangeAnim
                && !_poseKickInFlight)
            {
                TryKickQueuedChangeAnimation(hScene);
            }

            // Orphan NowChangeAnim: change flag set but no sel and we are not kicking.
            if (hScene.NowChangeAnim
                && ctrlFlag.selectAnimationListInfo == null
                && !_poseKickInFlight)
            {
                ClearSelection(hScene, OrbitAssistReasons.ClearedNowChangeStuck, forceClearNowChangeAnim: true);
                OrbitPoseLandedPolicy.OnPoseLanded(hScene, PoseLandedSource.Unstick);
            }

            AssertPoseChangeInvariants(hScene, "end");
        }

        /// <summary>Log if sel==now still sticky after recovery (should be empty).</summary>
        private static void AssertPoseChangeInvariants(HScene? hScene, string where)
        {
            if (hScene?.ctrlFlag == null || _poseKickInFlight)
                return;
            var sel = hScene.ctrlFlag.selectAnimationListInfo;
            var now = hScene.ctrlFlag.nowAnimationInfo;
            if (sel != null && now != null && sel.id == now.id)
            {
                OrbitStateMachineLog.Event("invariant", "fail_sel_eq_now",
                    "{\"where\":\"" + where + "\",\"id\":" + sel.id + "}");
            }
        }

        /// <summary>Obsolete name — use <see cref="TickPoseFlagRecovery"/>.</summary>
        internal static void TickStaleSelectionRecovery(HScene? hScene) =>
            TickPoseFlagRecovery(hScene);
    }
}
