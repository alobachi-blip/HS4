using System;
using System.Collections;
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
        /// <summary>§11：相機是否在轉。協助開時預設 true；停止環視鍵只關此旗標。</summary>
        private static bool _orbitCameraSpinning;

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
        /// <summary>協助開且未按停止環視。</summary>
        internal static bool IsOrbitCameraSpinning() => _orbitAssistActive && _orbitCameraSpinning;
        internal static bool IsPoseKickInFlight => _poseKickInFlight;

        /// <summary>
        /// 游標在遊戲 UI／設定窗上時暫停環視轉動，避免操作選單時畫面跟著轉。
        /// </summary>
        internal static bool ShouldPauseOrbitCameraForUi()
        {
            if (!_orbitAssistActive)
                return false;
            if (OrbitSettingsGUI.IsVisible)
                return true;
            try
            {
                if (ConfirmDialog.active)
                    return true;
            }
            catch { /* ConfirmDialog 未載入 */ }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return true;
            return false;
        }

        /// <summary>只停／恢復相機轉動；不關協助／FSM／FEEL。</summary>
        internal static void SetOrbitCameraSpinning(bool spinning)
        {
            if (!_orbitAssistActive)
                return;
            if (_orbitCameraSpinning == spinning)
                return;
            _orbitCameraSpinning = spinning;
            HS2OrbitAndExciter.Log?.LogInfo(
                spinning ? "Orbit: 恢復環視轉動" : "Orbit: 停止環視轉動（協助／感度／流程仍開啟；換衣因綁圈暫停）");
            OrbitStateMachineLog.Event("環視", spinning ? "恢復轉動" : "停止轉動");
        }

        internal static void ToggleOrbitCameraSpinning() =>
            SetOrbitCameraSpinning(!_orbitCameraSpinning);

        internal static void NotifyManualUiClick()
        {
            _manualSelectionSuppressUntilUnscaled = Time.unscaledTime + ManualSelectionSuppressSeconds;
        }

        internal static void NotifyManualHotkeyCompleted(HScene? hScene)
        {
            ResetNullSelectionTracking();
            _autoActionNullSelectionSinceUnscaled = Time.unscaledTime - AutoActionNullSelectionMinSeconds;
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

        internal static void NotifyFemaleOrgasm(HSceneFlagCtrl? ctrlFlag) =>
            NotifyOrgasmEvent(ctrlFlag, "女高潮");

        /// <summary>§16～18：進高潮統一事件（女高潮／男射／女女等）。</summary>
        internal static void NotifyOrgasmEvent(HSceneFlagCtrl? ctrlFlag, string kind)
        {
            if (ctrlFlag == null)
                return;

            // quiet 僅節奏用，不擋選池（選池走 OrbitFsmFlow）
            BeginOrgasmAssistQuiet();
            ResetNullSelectionTracking();

            OrbitOrgasmTattoo.OnOrgasm(ctrlFlag);
            OrbitOrgasmBustGrowth.OnOrgasm(ctrlFlag);
            OrbitOrgasmNippleSpray.OnOrgasm(ctrlFlag);
            if (kind == "女高潮")
                OrbitVoiceTour.OnFemaleOrgasm();
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 高潮事件（{kind}）→ 刺青／胸／噴");
        }

        internal static void NotifyVoiceTourHit(string triggerLabel)
        {
            BeginOrgasmAssistQuiet();
            _ = triggerLabel;
        }

        private static void BeginOrgasmAssistQuiet()
        {
            _orgasmAssistQuietUntilUnscaled = Time.unscaledTime + OrgasmAssistQuietSeconds;
        }

        internal static void NotifyOrbitToggled(bool active)
        {
            _orbitAssistActive = active;
            _afterIdleAutoEscapeSinceUnscaled = -1f;
            _idleAutoEscapeSinceUnscaled = -1f;
            _orgasmAssistQuietUntilUnscaled = -1f;
            ClearMotionEscapeLatch();
            _poseKickInFlight = false;
            if (active)
            {
                _orbitCameraSpinning = true; // 開協助時預設開始轉
                _orbitAutoActionGraceUntilUnscaled = Time.unscaledTime + OrbitAutoActionGraceSeconds;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
                OrbitPoseDirector.Reset();
                OrbitManualDirector.Reset();
                OrbitFsmFlow.Reset();
                OrbitFsmFlow.OnAssistStarted();
                OrbitFaintnessAssist.ApplyOnAssistStart();
                OrbitStatusHud.NotifyOrbitActivated();
            }
            else
            {
                _orbitCameraSpinning = false;
                _orbitAutoActionGraceUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
                OrbitPoseDirector.Reset();
                OrbitManualDirector.Reset();
                OrbitFsmFlow.Reset();
                OrbitFaintnessAssist.RestoreOnAssistStop();
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
            // 游標在 UI 上只暫停環視轉動（ShouldPauseOrbitCameraForUi），不擋自動推進
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

            // 窺視＝純播出：ActionCtrl→Peeping 時擋自動換姿，直到 L／滾輪／cycle／N latch。
            // 不用硬編碼姿勢 id（會誤傷插入／口交等改寫後的同號姿）。
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
            // 游標在 UI 上只暫停環視轉動（ShouldPauseOrbitCameraForUi），不擋自動推進
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
        /// Seconds until timed Idle auto-leave（0 if 窺視欣賞鎖、latched、not Idle, or ready）。
        /// </summary>
        internal static float RemainingIdleAutoEscapeSeconds()
        {
            if (!_orbitAssistActive || IsMotionEscapeArmed())
                return 0f;
            var hScene = OrbitController.TryGetHScene();
            if (hScene == null || !OrbitHelpers.IsFirstFemaleInIdle(hScene) || hScene.NowChangeAnim)
                return 0f;
            // 窺視不會 Classify 成 Idle；此處防禦：若仍落到 Idle 亦不倒數開幹
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
            if (!IsOrbitAssistActive()) return false;
            // §21：協助開＋動作橋段內加 FEEL；不綁相機是否在轉
            var cell = OrbitFsmCellClassifier.Classify(hScene);
            return cell == OrbitFsmCell.ActionBridge
                   || OrbitHelpers.IsFirstFemaleInActionLoop(hScene);
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
            // §8 甲1：協助下不推 isAutoActionChange／initiative；換段只認選池
            return false;
        }

        internal static void TickOrbitCheckpointAssist(HScene? hScene, float deltaTime)
        {
            // §8 乙1：關掉外掛逾時／GetAutoAnimation 換段；換段只認選池
        }

        internal static bool TryInjectOrbitWheelBypass(ref float wheel)
        {
            // §7 A-還輪：停用全部假滾輪；真實滾輪還給原版
            return false;
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

        /// <summary>§4：高潮後改由 <see cref="OrbitFsmFlow"/> 選池；不再強制回循環。</summary>
        internal static bool ShouldForceAfterIdleEscape() => false;

        /// <summary>§2：閒置開幹改走原版 IsStart（見 <see cref="OrbitFsmFlow.ShouldForceVanillaIsStart"/>）。</summary>
        internal static bool ShouldForceIdleEscape() =>
            OrbitFsmFlow.ShouldForceVanillaIsStart();

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
        /// Runtime: Orbit can fill gauges while LoopProc never advances W/S/M→O.
        /// After ~1.5s still on W/S/M at threshold, force OLoop. Ignores vanilla pain gate.
        /// Houshi uses feel_m; others use feel_f. Peeping (mode 5) excluded.
        /// </summary>
        private static float _wsToOStuckSinceUnscaled = -1f;

        internal static void TickWsToOLoopRecovery(HScene? hScene)
        {
            if (hScene == null || !IsOrbitAssistActive())
            {
                _wsToOStuckSinceUnscaled = -1f;
                return;
            }
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null || ctrlFlag.nowOrgasm)
            {
                _wsToOStuckSinceUnscaled = -1f;
                return;
            }
            if (!OrbitHelpers.IsFirstFemaleInWsLoop(hScene))
            {
                _wsToOStuckSinceUnscaled = -1f;
                return;
            }

            int mode = -1;
            try { mode = Traverse.Create(hScene).Field("mode").GetValue<int>(); } catch { /* ignore */ }
            if (mode == 5) // Peeping — appreciation route, do not force
            {
                _wsToOStuckSinceUnscaled = -1f;
                return;
            }

            bool houshi = mode == 1;
            // MultiPlay houshi submodes
            if (mode == 7 || mode == 8)
            {
                int mc = -1;
                try { mc = Traverse.Create(hScene).Field("modeCtrl").GetValue<int>(); } catch { /* ignore */ }
                houshi = mc == 1 || mc == 2;
            }
            float gauge = houshi ? ctrlFlag.feel_m : ctrlFlag.feel_f;
            if (gauge < 0.74f)
            {
                _wsToOStuckSinceUnscaled = -1f;
                return;
            }
            if (_wsToOStuckSinceUnscaled < 0f)
                _wsToOStuckSinceUnscaled = Time.unscaledTime;
            // Sticky: NowChangeAnim flicker must not wipe the arm timer.
            if (hScene.NowChangeAnim)
                return;
            if (Time.unscaledTime - _wsToOStuckSinceUnscaled < 1.5f)
                return;

            bool useD = OrbitHelpers.IsFirstFemaleOnDMotion(hScene)
                        || (ctrlFlag.isFaintness && ctrlFlag.FaintnessType != 2);
            string anim = useD ? "D_OLoop" : "OLoop";
            if (!TryForceFemaleAnim(hScene, anim))
                return;

            try { Traverse.Create(ctrlFlag).Field("loopType").SetValue(2); } catch { /* ignore */ }
            ctrlFlag.speed = 0f;
            ctrlFlag.nowSpeedStateFast = false;
            if (houshi)
            {
                if (ctrlFlag.feel_m < 0.75f) ctrlFlag.feel_m = 0.75f;
            }
            else if (ctrlFlag.feel_f < 0.75f)
            {
                ctrlFlag.feel_f = 0.75f;
            }

            OrbitStateMachineLog.Event("feel", "force_ws_to_oloop",
                "{\"anim\":\"" + anim + "\",\"mode\":" + mode + "}");
            _wsToOStuckSinceUnscaled = -1f;
        }

        /// <summary>
        /// OLoop + full gauge but OLoopProc not running → force climax.
        /// Runtime (79c2c8d9): arming logs showed armed==ut every sample → timer reset every frame
        /// by brief !IsOLoop during animator transitions. Sticky leave + direct IsName + 0.6s force.
        /// </summary>
        private static float _oToOrgasmStuckSinceUnscaled = -1f;
        private static float _oLoopLeftSinceUnscaled = -1f;
        private const float OLoopForceSeconds = 0.6f;
        private const float OLoopLeaveGraceSeconds = 0.45f;

        internal static void TickOLoopToOrgasmRecovery(HScene? hScene)
        {
            if (hScene == null || !IsOrbitAssistActive())
            {
                _oToOrgasmStuckSinceUnscaled = -1f;
                _oLoopLeftSinceUnscaled = -1f;
                return;
            }
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null || ctrlFlag.nowOrgasm)
            {
                _oToOrgasmStuckSinceUnscaled = -1f;
                _oLoopLeftSinceUnscaled = -1f;
                return;
            }

            int mode = -1;
            int modeCtrl = -1;
            try { mode = Traverse.Create(hScene).Field("mode").GetValue<int>(); } catch { /* ignore */ }
            try { modeCtrl = Traverse.Create(hScene).Field("modeCtrl").GetValue<int>(); } catch { /* ignore */ }
            if (mode == 5)
            {
                _oToOrgasmStuckSinceUnscaled = -1f;
                _oLoopLeftSinceUnscaled = -1f;
                return;
            }

            bool inO = OrbitHelpers.IsFirstFemaleInOLoop(hScene);
            OrgasmForceKind kind = ClassifyOLoopOrgasm(mode, modeCtrl, ctrlFlag.feel_f, ctrlFlag.feel_m);

            if (!inO)
            {
                // Sticky: single-frame animator miss must NOT wipe the arm timer.
                if (_oToOrgasmStuckSinceUnscaled >= 0f)
                {
                    if (_oLoopLeftSinceUnscaled < 0f)
                        _oLoopLeftSinceUnscaled = Time.unscaledTime;
                    if (Time.unscaledTime - _oLoopLeftSinceUnscaled < OLoopLeaveGraceSeconds)
                        return;
                }
                _oToOrgasmStuckSinceUnscaled = -1f;
                _oLoopLeftSinceUnscaled = -1f;
                return;
            }
            _oLoopLeftSinceUnscaled = -1f;

            if (kind == OrgasmForceKind.None)
            {
                // Still on OLoop but gauge dipped — keep timer if already armed.
                if (_oToOrgasmStuckSinceUnscaled < 0f)
                    return;
            }
            else if (_oToOrgasmStuckSinceUnscaled < 0f)
            {
                _oToOrgasmStuckSinceUnscaled = Time.unscaledTime;
            }

            float armedFor = _oToOrgasmStuckSinceUnscaled < 0f
                ? 0f
                : Time.unscaledTime - _oToOrgasmStuckSinceUnscaled;

            // Pose change in flight: after grace, cancel sel so climax can proceed.
            if (hScene.NowChangeAnim || ctrlFlag.selectAnimationListInfo != null)
            {
                if (armedFor < 1.2f)
                    return;
                try { ctrlFlag.selectAnimationListInfo = null; } catch { /* ignore */ }
                try
                {
                    var nca = Traverse.Create(hScene).Field("nowChangeAnim");
                    if (nca.FieldExists()) nca.SetValue(false);
                }
                catch { /* ignore */ }
            }

            if (kind == OrgasmForceKind.None)
                return;

            if (armedFor < OLoopForceSeconds)
                return;

            bool useD = OrbitHelpers.IsFirstFemaleOnDMotion(hScene)
                        || (ctrlFlag.isFaintness && ctrlFlag.FaintnessType != 2);
            string anim = OrgasmAnimName(kind, useD);

            if (!TryForceFemaleAnim(hScene, anim))
                return;

            try { Traverse.Create(ctrlFlag).Field("loopType").SetValue(-1); } catch { /* ignore */ }
            ctrlFlag.speed = 0f;
            ctrlFlag.isGaugeHit = false;
            try { ctrlFlag.isGaugeHit_M = false; } catch { /* ignore */ }
            ctrlFlag.nowOrgasm = true;

            bool maleClimax = kind == OrgasmForceKind.HoushiOut || kind == OrgasmForceKind.SonyuMaleOut;
            if (maleClimax)
            {
                ctrlFlag.feel_m = 0f;
                if (ctrlFlag.feel_f > 0.5f) ctrlFlag.feel_f = 0.5f;
                try { ctrlFlag.numOutSide = Mathf.Clamp(ctrlFlag.numOutSide + 1, 0, 999999); } catch { /* ignore */ }
            }
            else
            {
                ctrlFlag.feel_f = 0f;
                try { ctrlFlag.numOrgasm = Mathf.Clamp(ctrlFlag.numOrgasm + 1, 0, 10); } catch { /* ignore */ }
                try { ctrlFlag.AddOrgasm(); } catch { /* ignore */ }
            }

            OrbitStateMachineLog.Event("feel", "force_oloop_to_orgasm",
                "{\"anim\":\"" + anim + "\",\"mode\":" + mode + ",\"kind\":\"" + kind + "\"}");
            _oToOrgasmStuckSinceUnscaled = -1f;
            _oLoopLeftSinceUnscaled = -1f;
        }

        private enum OrgasmForceKind
        {
            None,
            AibuOrgasm,      // Orgasm / D_Orgasm
            HoushiOut,       // Orgasm_OUT
            SonyuFemaleIn,   // OrgasmF_IN
            SonyuMaleOut,    // OrgasmM_OUT (auto male finish when feel_m full)
        }

        /// <summary>
        /// Pick climax kind from lstProc mode + MultiPlay modeCtrl + gauges.
        /// Female gauge preferred when both full (matches vanilla F-first on Sonyu OLoop).
        /// </summary>
        private static OrgasmForceKind ClassifyOLoopOrgasm(int mode, int modeCtrl, float feelF, float feelM)
        {
            bool fFull = feelF >= 0.99f;
            bool mFull = feelM >= 0.99f;
            if (!fFull && !mFull)
                return OrgasmForceKind.None;

            // MultiPlay: modeCtrl 0=Aibu, 1|2=Houshi, 3|4=Sonyu
            if (mode == 7 || mode == 8)
            {
                if (modeCtrl == 1 || modeCtrl == 2)
                    return mFull ? OrgasmForceKind.HoushiOut : OrgasmForceKind.None;
                if (modeCtrl == 0)
                    return fFull ? OrgasmForceKind.AibuOrgasm : OrgasmForceKind.None;
                // Sonyu-like
                if (fFull) return OrgasmForceKind.SonyuFemaleIn;
                if (mFull) return OrgasmForceKind.SonyuMaleOut;
                return OrgasmForceKind.None;
            }

            switch (mode)
            {
                case 0: // Aibu
                case 4: // Masturbation
                case 6: // Les
                case 3: // Spnking rarely on OLoop; still Orgasm
                    return fFull ? OrgasmForceKind.AibuOrgasm : OrgasmForceKind.None;
                case 1: // Houshi
                    return mFull ? OrgasmForceKind.HoushiOut : OrgasmForceKind.None;
                case 2: // Sonyu
                    if (fFull) return OrgasmForceKind.SonyuFemaleIn;
                    if (mFull) return OrgasmForceKind.SonyuMaleOut;
                    return OrgasmForceKind.None;
                default:
                    if (fFull) return OrgasmForceKind.AibuOrgasm;
                    if (mFull) return OrgasmForceKind.HoushiOut;
                    return OrgasmForceKind.None;
            }
        }

        private static string OrgasmAnimName(OrgasmForceKind kind, bool useD)
        {
            switch (kind)
            {
                case OrgasmForceKind.HoushiOut:
                    return useD ? "D_Orgasm_OUT" : "Orgasm_OUT";
                case OrgasmForceKind.SonyuFemaleIn:
                    return useD ? "D_OrgasmF_IN" : "OrgasmF_IN";
                case OrgasmForceKind.SonyuMaleOut:
                    return useD ? "D_OrgasmM_OUT" : "OrgasmM_OUT";
                default:
                    return useD ? "D_Orgasm" : "Orgasm";
            }
        }

        /// <summary>
        /// Insert stuck with full feel (Proc never advances to WLoop) → force W/D_WLoop.
        /// </summary>
        private static float _insertStuckSinceUnscaled = -1f;

        internal static void TickInsertToLoopRecovery(HScene? hScene)
        {
            if (hScene == null || !IsOrbitAssistActive())
            {
                _insertStuckSinceUnscaled = -1f;
                return;
            }
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null || ctrlFlag.nowOrgasm || hScene.NowChangeAnim)
            {
                _insertStuckSinceUnscaled = -1f;
                return;
            }
            if (!OrbitHelpers.IsFirstFemaleInInsert(hScene))
            {
                _insertStuckSinceUnscaled = -1f;
                return;
            }
            int mode = -1;
            try { mode = Traverse.Create(hScene).Field("mode").GetValue<int>(); } catch { /* ignore */ }
            if (mode == 5)
            {
                _insertStuckSinceUnscaled = -1f;
                return;
            }
            float gauge = Mathf.Max(ctrlFlag.feel_f, ctrlFlag.feel_m);
            if (gauge < 0.74f)
            {
                _insertStuckSinceUnscaled = -1f;
                return;
            }
            if (_insertStuckSinceUnscaled < 0f)
                _insertStuckSinceUnscaled = Time.unscaledTime;
            if (Time.unscaledTime - _insertStuckSinceUnscaled < 0.6f)
                return;

            bool useD = OrbitHelpers.IsFirstFemaleOnDMotion(hScene)
                        || (ctrlFlag.isFaintness && ctrlFlag.FaintnessType != 2);
            string anim = useD ? "D_WLoop" : "WLoop";
            if (!TryForceFemaleAnim(hScene, anim))
                return;
            try { Traverse.Create(ctrlFlag).Field("loopType").SetValue(0); } catch { /* ignore */ }
            ctrlFlag.speed = 1f;
            OrbitStateMachineLog.Event("feel", "force_insert_to_wloop",
                "{\"anim\":\"" + anim + "\"}");
            _insertStuckSinceUnscaled = -1f;
        }

        /// <summary>
        /// Spnking (mode 3): no OLoop — orgasm from WIdle/SIdle when feel_f≥1.
        /// </summary>
        private static float _spankStuckSinceUnscaled = -1f;

        internal static void TickSpankIdleToOrgasmRecovery(HScene? hScene)
        {
            if (hScene == null || !IsOrbitAssistActive())
            {
                _spankStuckSinceUnscaled = -1f;
                return;
            }
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null || ctrlFlag.nowOrgasm || hScene.NowChangeAnim)
            {
                _spankStuckSinceUnscaled = -1f;
                return;
            }
            int mode = -1;
            try { mode = Traverse.Create(hScene).Field("mode").GetValue<int>(); } catch { /* ignore */ }
            if (mode != 3)
            {
                _spankStuckSinceUnscaled = -1f;
                return;
            }
            if (!OrbitHelpers.TryGetFirstFemaleLayer0Name(hScene, out string? clip) || clip == null)
            {
                _spankStuckSinceUnscaled = -1f;
                return;
            }
            if (clip != "WIdle" && clip != "SIdle" && clip != "WAction" && clip != "SAction" && clip != "D_Action")
            {
                _spankStuckSinceUnscaled = -1f;
                return;
            }
            if (ctrlFlag.selectAnimationListInfo != null || ctrlFlag.feel_f < 0.99f)
            {
                _spankStuckSinceUnscaled = -1f;
                return;
            }
            if (_spankStuckSinceUnscaled < 0f)
                _spankStuckSinceUnscaled = Time.unscaledTime;
            if (Time.unscaledTime - _spankStuckSinceUnscaled < 1.5f)
                return;

            bool useD = ctrlFlag.isFaintness && ctrlFlag.FaintnessType != 2;
            string anim = useD ? "D_Orgasm" : "Orgasm";
            if (!TryForceFemaleAnim(hScene, anim))
                return;
            ctrlFlag.speed = 0f;
            ctrlFlag.nowOrgasm = true;
            ctrlFlag.feel_f = 0f;
            try { ctrlFlag.numOrgasm = Mathf.Clamp(ctrlFlag.numOrgasm + 1, 0, 10); } catch { /* ignore */ }
            try { ctrlFlag.AddOrgasm(); } catch { /* ignore */ }
            OrbitStateMachineLog.Event("feel", "force_spank_to_orgasm",
                "{\"anim\":\"" + anim + "\"}");
            _spankStuckSinceUnscaled = -1f;
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
