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

        internal const float WheelBypassValue = 0.10f;
        internal const float WheelBypassDelaySeconds = 2f;

        internal static bool IsOrbitAssistActive() => _orbitAssistActive;

        private static float _dbgLastPushLogUnscaled = -999f;
        private static float _dbgCkptSampleUnscaled = -999f;

        // #region agent log
        private static void DbgPushOutcome(string code, bool pushed, string? suppressDetail = null)
        {
            string det = suppressDetail == null ? "null" : "\"" + OrbitAgentDebugLog.JsonEscape(suppressDetail) + "\"";
            string data = "{\"code\":\"" + OrbitAgentDebugLog.JsonEscape(code) + "\",\"detail\":" + det + "}";
            if (pushed)
                OrbitAgentDebugLog.Write("H2", "TryPushOrbitAutoActionAssist", "pushed", data);
            else
            {
                if (Time.unscaledTime - _dbgLastPushLogUnscaled < 3f) return;
                _dbgLastPushLogUnscaled = Time.unscaledTime;
                OrbitAgentDebugLog.Write("H2", "TryPushOrbitAutoActionAssist", "not_pushed", data);
            }
        }
        // #endregion

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
            // #region agent log
            OrbitAgentDebugLog.Write("H4", "NotifyOrbitToggled", active ? "orbit_on" : "orbit_off", "{}");
            // #endregion
            if (active)
            {
                _orbitAutoActionGraceUntilUnscaled = Time.unscaledTime + OrbitAutoActionGraceSeconds;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
            }
            else
            {
                _orbitAutoActionGraceUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
            }
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
            {
                DbgPushOutcome("cfg_off", false);
                return false;
            }
            if (ctrlFlag == null)
            {
                DbgPushOutcome("no_ctrl", false);
                return false;
            }
            if (ShouldSuppressAssist(ctrlFlag, out var supReason))
            {
                ctrlFlag.isAutoActionChange = false;
                ctrlFlag.initiative = 0;
                ResetNullSelectionTracking();
                DbgPushOutcome("suppress", false, supReason);
                return false;
            }
            if (ctrlFlag.selectAnimationListInfo != null)
            {
                ResetNullSelectionTracking();
                DbgPushOutcome("has_selection", false);
                return false;
            }
            if (!IsNullSelectionReadyForAssist())
            {
                DbgPushOutcome("null_wait", false);
                return false;
            }
            if (!TryConsumeAssistFlagPush(out _))
            {
                DbgPushOutcome("interval", false);
                return false;
            }

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
            DbgPushOutcome("ok", true);
            return true;
        }

        /// <summary>When orbit is on and not in action loop: accumulate idle time then invoke <c>GetAutoAnimation</c> after timeout.</summary>
        internal static void TickOrbitCheckpointAssist(HScene? hScene, float deltaTime)
        {
            float timeout = HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds?.Value ?? 2f;
            if (timeout <= 0f || hScene == null) return;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null) return;

            // #region agent log
            if (Time.unscaledTime - _dbgCkptSampleUnscaled >= 2f)
            {
                _dbgCkptSampleUnscaled = Time.unscaledTime;
                bool inLoop = OrbitHelpers.IsFirstFemaleInActionLoop(hScene);
                ShouldSuppressAssist(ctrlFlag, out var ckSup);
                var selCk = Traverse.Create(ctrlFlag).Property("selectAnimationListInfo").GetValue();
                bool legacyCd = IsCheckpointInvokeOnLegacyCooldown();
                bool mouse = Input.GetMouseButton(0);
                OrbitAgentDebugLog.Write("H3", "TickOrbitCheckpointAssist", "state_sample",
                    "{\"checkpointIdle\":" + _checkpointIdleTime.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"timeout\":" + timeout.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"inActionLoop\":" + (inLoop ? "true" : "false")
                    + ",\"suppress\":\"" + OrbitAgentDebugLog.JsonEscape(ckSup) + "\""
                    + ",\"hasSelection\":" + (selCk != null ? "true" : "false")
                    + ",\"legacyCooldown\":" + (legacyCd ? "true" : "false")
                    + ",\"mouseLmb\":" + (mouse ? "true" : "false") + "}");
            }
            // #endregion

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
            // #region agent log
            OrbitAgentDebugLog.Write("H3", "TickOrbitCheckpointAssist", "invoke_get_auto", "{}");
            // #endregion

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
            // #region agent log
            OrbitAgentDebugLog.Write("H5", "TryInjectOrbitWheelBypass", "wheel_injected", "{}");
            // #endregion
            return true;
        }
    }
}
