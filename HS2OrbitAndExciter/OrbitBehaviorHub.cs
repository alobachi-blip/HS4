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

        private static float _selectionListStuckSinceUnscaled = -1f;
        private static float _nowChangeStuckSinceUnscaled = -1f;
        /// <summary>User/cycle requested leave of long appreciation / waits; active until this unscaled time.</summary>
        private static float _motionEscapeUntilUnscaled = -1f;
        private static string _motionEscapeReason = "";

        internal const float WheelBypassValue = 0.10f;
        internal const float WheelBypassDelaySeconds = 2f;
        internal const float StaleSelectionClearSeconds = 8f;
        /// <summary>How long L/wheel/cycle escape stays armed for IsStart/IsReStart patches.</summary>
        internal const float MotionEscapeWindowSeconds = 1.5f;
        internal const float IdleStaleSelectionClearSeconds = 2.0f;

        internal static bool IsOrbitAssistActive() => _orbitAssistActive;

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
            _selectionListStuckSinceUnscaled = -1f;
            _nowChangeStuckSinceUnscaled = -1f;
            _motionEscapeUntilUnscaled = -1f;
            _motionEscapeReason = "";
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

            // A+B long bath/toilet/shower: do not auto-pick next pose until L/wheel/cycle arms escape.
            // Cycle pose RequestPoseChange arms first, then CanAcceptRequest may still pass.
            if (hScene != null
                && OrbitHelpers.IsLongAppreciationPose(hScene)
                && !IsMotionEscapeArmed())
            {
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

            _selectionListStuckSinceUnscaled = -1f;
            _nowChangeStuckSinceUnscaled = -1f;
            OrbitPoseDirector.NotifySelectionCleared();
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
        /// Arm leave so L / real mouse wheel / cycle pose can exit long A+B poses
        /// (and also accelerate short AfterIdle/Idle force patches).
        /// </summary>
        internal static void RequestMotionEscape(string reason)
        {
            if (!_orbitAssistActive)
                return;
            _motionEscapeUntilUnscaled = Time.unscaledTime + MotionEscapeWindowSeconds;
            _motionEscapeReason = reason ?? "";
            OrbitStateMachineLog.Event("escape", "request",
                "{\"reason\":\"" + (_motionEscapeReason.Replace("\"", "") ?? "") + "\"}");
        }

        internal static bool IsMotionEscapeArmed() =>
            _orbitAssistActive && Time.unscaledTime < _motionEscapeUntilUnscaled;

        /// <summary>
        /// Force leave AfterIdle: short orgasm waits auto after ≈2s (even on A+B pose ids),
        /// or immediately when escape armed.
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
        /// Force leave Idle: A+B long poses require armed escape; other Idle uses ≈2s auto (or armed).
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

        /// <summary>Recovery entry: sanitize, stale sel, stuck NowChangeAnim.</summary>
        internal static void TickStaleSelectionRecovery(HScene? hScene)
        {
            if (hScene == null)
            {
                _selectionListStuckSinceUnscaled = -1f;
                _nowChangeStuckSinceUnscaled = -1f;
                return;
            }
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
            {
                _selectionListStuckSinceUnscaled = -1f;
                _nowChangeStuckSinceUnscaled = -1f;
                return;
            }

            if (SanitizeSelectedPose(hScene))
                return;

            if (hScene.NowChangeAnim)
            {
                _selectionListStuckSinceUnscaled = -1f;
                if (_nowChangeStuckSinceUnscaled < 0f)
                    _nowChangeStuckSinceUnscaled = Time.unscaledTime;
                else if (Time.unscaledTime - _nowChangeStuckSinceUnscaled
                         >= OrbitPoseDirector.TransitionTimeoutSeconds)
                {
                    ClearSelection(hScene, OrbitAssistReasons.ClearedNowChangeStuck, forceClearNowChangeAnim: true);
                }
                return;
            }

            _nowChangeStuckSinceUnscaled = -1f;

            if (ctrlFlag.selectAnimationListInfo == null)
            {
                _selectionListStuckSinceUnscaled = -1f;
                return;
            }

            // Only accelerate clear while user/cycle asked to leave a wait state.
            float clearSec = IsMotionEscapeArmed()
                             && (OrbitHelpers.IsFirstFemaleInIdle(hScene)
                                 || OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
                ? IdleStaleSelectionClearSeconds
                : StaleSelectionClearSeconds;

            if (_selectionListStuckSinceUnscaled < 0f)
                _selectionListStuckSinceUnscaled = Time.unscaledTime;
            else if (Time.unscaledTime - _selectionListStuckSinceUnscaled >= clearSec)
            {
                ClearSelection(hScene, OrbitAssistReasons.ClearedAfterTimeout);
            }
        }
    }
}
