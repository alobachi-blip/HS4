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
        /// <summary>§11：相機是否在轉。協助開時預設 true；停止環視鍵只關此旗標。</summary>
        private static bool _orbitCameraSpinning;

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
            SuppressVanillaAutoAction(ctrlFlag);
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
                _orbitCameraSpinning = true; // 開協助時預設開始轉
                _orbitAutoActionGraceUntilUnscaled = Time.unscaledTime + OrbitAutoActionGraceSeconds;
                _checkpointInvokeCooldownUntilUnscaled = -1f;
                _autoActionNullSelectionSinceUnscaled = -1f;
                _lastAssistFlagPushTimeUnscaled = -999f;
                _lastCheckpointInvokeTimeUnscaled = -999f;
                OrbitPoseDirector.Reset();
                OrbitManualDirector.Reset();
                OrbitFsmFlow.Reset();
                OrbitFinishDirector.Reset();
                OrbitSessionDirector.Reset();
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
                OrbitFinishDirector.Reset();
                OrbitSessionDirector.Reset();
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

        private static void SuppressVanillaAutoAction(HSceneFlagCtrl? ctrlFlag)
        {
            if (ctrlFlag == null)
                return;
            try { ctrlFlag.isAutoActionChange = false; } catch { /* ignore */ }
        }

        /// <summary>Legacy auto-action hook. Orbit flow disarms vanilla auto-pose changes and uses the pose pool instead.</summary>
        internal static bool TryPushOrbitAutoActionAssist(HSceneFlagCtrl? ctrlFlag)
        {
            // §8 甲1：協助下不推 isAutoActionChange／initiative；換段只認選池
            SuppressVanillaAutoAction(ctrlFlag);
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
                SuppressVanillaAutoAction(ctrlFlag);
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
                SuppressVanillaAutoAction(ctrlFlag);
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
                SuppressVanillaAutoAction(ctrlFlag);
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
