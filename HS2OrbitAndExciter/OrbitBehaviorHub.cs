using System.Reflection;
using HarmonyLib;
using Manager;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Centralized strategy, throttling, and gates for orbit assist writes to <see cref="HSceneFlagCtrl"/>.
    /// Scene state queries live in <see cref="OrbitHelpers"/>; this class composes them with config and timers.
    /// </summary>
    internal static class OrbitBehaviorHub
    {
        private const float ManualSelectionSuppressSeconds = 3.0f;
        private const float OrbitAutoActionGraceSeconds = 2.5f;
        private const float AutoActionNullSelectionMinSeconds = 1.5f;

        private static float _manualSelectionSuppressUntilUnscaled = -1f;
        private static float _orbitAutoActionGraceUntilUnscaled = -1f;
        private static float _autoActionNullSelectionSinceUnscaled = -1f;
        private static float _checkpointInvokeCooldownUntilUnscaled = -1f;
        private static float _lastAssistFlagPushTimeUnscaled = -999f;
        private static float _lastCheckpointInvokeTimeUnscaled = -999f;

        private static bool _orbitAssistActive;
        private static float _checkpointIdleTime;
        private static MethodInfo? _getAutoAnimationMethod;
        private static FieldInfo? _isAutoActionChangeField;
        private static PropertyInfo? _isAutoActionChangeProp;
        private static float _wheelBypassStartUnscaled = -1f;

        private static bool _pendingCyclePoseChange;
        private static float _cyclePoseAssistQuietUntilUnscaled = -1f;
        private static float _selectionListStuckSinceUnscaled = -1f;

        internal const float WheelBypassValue = 0.10f;
        internal const float WheelBypassDelaySeconds = 2f;
        internal const float CyclePoseAssistQuietSeconds = 15f;
        internal const float StaleSelectionClearSeconds = 8f;

        internal static bool IsOrbitAssistActive() => _orbitAssistActive;

        internal static void NotifyManualUiClick()
        {
            _manualSelectionSuppressUntilUnscaled = Time.unscaledTime + ManualSelectionSuppressSeconds;
        }

        internal static void NotifyUiHoverWhileOrbit()
        {
            float hoverUntil = Time.unscaledTime + 0.25f;
            if (hoverUntil > _manualSelectionSuppressUntilUnscaled)
                _manualSelectionSuppressUntilUnscaled = hoverUntil;
        }

        internal static void NotifyOrbitToggled(bool active)
        {
            _orbitAssistActive = active;
            _checkpointIdleTime = 0f;
            _wheelBypassStartUnscaled = -1f;
            if (active)
            {
                _orbitAutoActionGraceUntilUnscaled = Time.unscaledTime + OrbitAutoActionGraceSeconds;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
                ClearPendingCyclePoseChange();
                _cyclePoseAssistQuietUntilUnscaled = -1f;
                _selectionListStuckSinceUnscaled = -1f;
                OrbitStatusHud.NotifyOrbitActivated();
            }
            else
            {
                _orbitAutoActionGraceUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
                ClearPendingCyclePoseChange();
                _cyclePoseAssistQuietUntilUnscaled = -1f;
                _selectionListStuckSinceUnscaled = -1f;
            }
        }

        internal static void MarkPendingCyclePoseChange() => _pendingCyclePoseChange = true;

        internal static bool IsPendingCyclePoseChange() => _pendingCyclePoseChange;

        internal static void ClearPendingCyclePoseChange() => _pendingCyclePoseChange = false;

        internal static void NotifyCyclePoseChangeQueued()
        {
            _pendingCyclePoseChange = false;
            _cyclePoseAssistQuietUntilUnscaled = Time.unscaledTime + CyclePoseAssistQuietSeconds;
            _selectionListStuckSinceUnscaled = -1f;
        }

        internal static bool ShouldDeferAutoAssistForCyclePose()
        {
            if (_pendingCyclePoseChange)
                return true;
            return Time.unscaledTime < _cyclePoseAssistQuietUntilUnscaled;
        }

        internal static bool ShouldSuppressAssist(HSceneFlagCtrl? ctrlFlag, out string reason)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                reason = "pointerOverUi";
                return true;
            }
            if (Time.unscaledTime < _orbitAutoActionGraceUntilUnscaled)
            {
                reason = "orbitStartGrace";
                return true;
            }
            if (ctrlFlag != null && ctrlFlag.inputForcus)
            {
                reason = "inputForcus";
                return true;
            }
            if (ctrlFlag != null && ctrlFlag.selectAnimationListInfo != null)
            {
                reason = "selectionListPresent";
                return true;
            }
            if (Input.GetMouseButton(0))
            {
                reason = "mouseHolding";
                return true;
            }
            if (Time.unscaledTime < _manualSelectionSuppressUntilUnscaled)
            {
                reason = "recentUiClick";
                return true;
            }
            reason = "none";
            return false;
        }

        /// <summary>Seconds until orbit-start auto-action grace ends (0 if not in grace).</summary>
        internal static float RemainingOrbitStartGraceSeconds()
        {
            if (_orbitAutoActionGraceUntilUnscaled < 0f) return 0f;
            return Mathf.Max(0f, _orbitAutoActionGraceUntilUnscaled - Time.unscaledTime);
        }

        /// <summary>Seconds until post-UI-click suppress ends (0 if not active).</summary>
        internal static float RemainingManualUiSuppressSeconds()
        {
            if (_manualSelectionSuppressUntilUnscaled < 0f) return 0f;
            return Mathf.Max(0f, _manualSelectionSuppressUntilUnscaled - Time.unscaledTime);
        }

        /// <summary>Defer orbit Q/W/E focus hotkeys when game uses the same keys (vanilla <c>HScene.ShortcutKey</c> path).</summary>
        internal static bool ShouldDeferOrbitFocusHotkeysToGame(HSceneFlagCtrl? ctrlFlag) =>
            ctrlFlag != null && ctrlFlag.inputForcus;

        /// <summary>Whether orbit may add feel_f / speed this frame (prep countdown excludes; then action loop only).</summary>
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
                    reason = "assistInterval";
                    return false;
                }
            }
            _lastAssistFlagPushTimeUnscaled = Time.unscaledTime;
            reason = "none";
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
                reason = "checkpointLegacyCooldown";
                return false;
            }

            float minInterval = Mathf.Max(0f, HS2OrbitAndExciter.AutoAssistMinIntervalSeconds?.Value ?? 0f);
            if (minInterval > 0f)
            {
                float elapsed = Time.unscaledTime - _lastCheckpointInvokeTimeUnscaled;
                if (elapsed < minInterval)
                {
                    reason = "checkpointInterval";
                    return false;
                }
            }

            _lastCheckpointInvokeTimeUnscaled = Time.unscaledTime;
            reason = "none";
            return true;
        }

        /// <summary>Single path for auto-action assist: config, suppress, null-selection wait, interval, then set flags.</summary>
        internal static bool TryPushOrbitAutoActionAssist(HSceneFlagCtrl? ctrlFlag)
        {
            if (HS2OrbitAndExciter.OrbitAutoActionEnabled?.Value != true)
                return false;
            if (ShouldDeferAutoAssistForCyclePose())
                return false;
            if (ctrlFlag == null)
                return false;
            if (ShouldSuppressAssist(ctrlFlag, out _))
            {
                ctrlFlag.isAutoActionChange = false;
                ctrlFlag.initiative = 0;
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
            catch { }
            Traverse.Create(ctrlFlag).Field("initiative").SetValue(1);
            return true;
        }

        /// <summary>When orbit is on and not in action loop: accumulate idle time then invoke <c>GetAutoAnimation</c> after timeout.</summary>
        internal static void TickOrbitCheckpointAssist(HScene? hScene, float deltaTime)
        {
            float timeout = HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds?.Value ?? 2f;
            if (timeout <= 0f || hScene == null) return;
            if (ShouldDeferAutoAssistForCyclePose())
                return;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null) return;

            if (OrbitHelpers.IsFirstFemaleInActionLoop(hScene))
            {
                _checkpointIdleTime = 0f;
                return;
            }
            if (ShouldSuppressAssist(ctrlFlag, out _))
            {
                _checkpointIdleTime = 0f;
                return;
            }
            var sel = Traverse.Create(ctrlFlag).Property("selectAnimationListInfo").GetValue();
            if (sel != null)
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
                if (Traverse.Create(ctrlFlag).Property("selectAnimationListInfo").GetValue() == null)
                    _getAutoAnimationMethod.Invoke(hScene, new object[] { true });
            }
            catch { }
            MarkCheckpointInvokeLegacyCooldown(timeout);

            bool hasSelAfter = Traverse.Create(ctrlFlag).Property("selectAnimationListInfo").GetValue() != null;
            if (!hasSelAfter)
            {
                try
                {
                    const float FallbackSpeedBump = 1.2f;
                    Traverse.Create(ctrlFlag).Field("speed").SetValue(FallbackSpeedBump);
                }
                catch { }
            }
        }

        /// <summary>Orbit wheel bypass: delay then inject small wheel value when animator gate allows.</summary>
        internal static bool TryInjectOrbitWheelBypass(ref float wheel)
        {
            if (!IsOrbitAssistActive() || wheel != 0f)
            {
                _wheelBypassStartUnscaled = -1f;
                return false;
            }
            if (!OrbitBypassAnimatorGate.IsBypassAllowedForCurrentHScene())
            {
                _wheelBypassStartUnscaled = -1f;
                return false;
            }
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
        /// Clear a stuck <see cref="HSceneFlagCtrl.selectAnimationListInfo"/> when the game never started ChangeAnimation
        /// (common after duplicate-id picks or interrupted coroutines). Prevents permanent assist suppress.
        /// </summary>
        internal static void TickStaleSelectionRecovery(HScene? hScene)
        {
            if (hScene == null)
            {
                _selectionListStuckSinceUnscaled = -1f;
                return;
            }
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
            {
                _selectionListStuckSinceUnscaled = -1f;
                return;
            }
            if (ctrlFlag.selectAnimationListInfo == null)
            {
                _selectionListStuckSinceUnscaled = -1f;
                return;
            }
            if (hScene.NowChangeAnim)
            {
                _selectionListStuckSinceUnscaled = -1f;
                return;
            }

            if (_selectionListStuckSinceUnscaled < 0f)
                _selectionListStuckSinceUnscaled = Time.unscaledTime;
            else if (Time.unscaledTime - _selectionListStuckSinceUnscaled >= StaleSelectionClearSeconds)
            {
                ctrlFlag.selectAnimationListInfo = null;
                ctrlFlag.isAutoActionChange = false;
                _selectionListStuckSinceUnscaled = -1f;
            }
        }
    }
}
