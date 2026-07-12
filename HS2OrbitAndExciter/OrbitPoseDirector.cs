using HarmonyLib;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    internal enum PoseChangeSource { Cycle, External, Hotkey, SelectPool }

    /// <summary>
    /// Director phases. Observable mapping (game flags):
    /// PoseQueued ≡ sel != null &amp;&amp; !NowChangeAnim (legal);
    /// Changing ≡ NowChangeAnim;
    /// PosePending / Rebinding ≡ plugin-only.
    /// </summary>
    internal enum DirectorState
    {
        Orbitting,
        PosePending,
        PoseQueued,
        Changing,
        Rebinding
    }

    /// <summary>
    /// Single coordinator for orbit pose changes: queue, transition detection, rebind.
    /// Plugin-initiated writes go through <see cref="TryQueuePoseChange"/>;
    /// game/checkpoint writes are tracked via <see cref="SyncFromGameFlags"/>.
    /// Does not clear <c>selectAnimationListInfo</c> — Hub owns <see cref="OrbitBehaviorHub.ClearSelection"/>.
    /// </summary>
    internal static class OrbitPoseDirector
    {
        private const int StableReadyFramesRequired = 3;

        private static DirectorState _state = DirectorState.Orbitting;
        private static bool _pendingCycleRequest;
        private static int _stableReadyFrames;
        private static object? _queuedAnimationRef;
        private static bool _sawChangeAnim;
        private static float _phaseEnteredUnscaled = -1f;
        private static string _lastHotkeyFailReason = OrbitAssistReasons.None;

        internal static string DebugStateName => _state.ToString();
        internal static DirectorState Phase => _state;
        internal static bool HasPendingCycleRequest => _pendingCycleRequest;
        internal static string LastHotkeyFailReason => _lastHotkeyFailReason;
        internal static float PhaseEnteredUnscaled => _phaseEnteredUnscaled;

        /// <summary>Queued / Changing / Rebinding — blocks auto-advance and L overwrite.</summary>
        internal static bool IsPoseChangeInFlight =>
            _state == DirectorState.PoseQueued
            || _state == DirectorState.Changing
            || _state == DirectorState.Rebinding;

        /// <summary>Legacy name: in-flight pose change (excludes PosePending).</summary>
        internal static bool IsTransitionActive => IsPoseChangeInFlight;

        internal static bool IsCameraPaused => false;

        /// <summary>
        /// Freeze rotation/round-trip side-effects and further Cycle pose triggers.
        /// Includes PosePending (owe one pose; do not stack). Does NOT block CanAutoAdvance.
        /// </summary>
        internal static bool ShouldFreezeCycleCounters =>
            _state != DirectorState.Orbitting || OrbitManualDirector.IsCameraPaused;

        internal static void Reset()
        {
            _state = DirectorState.Orbitting;
            _pendingCycleRequest = false;
            _stableReadyFrames = 0;
            _queuedAnimationRef = null;
            _sawChangeAnim = false;
            _phaseEnteredUnscaled = -1f;
            _lastHotkeyFailReason = OrbitAssistReasons.None;
        }

        /// <summary>Hub cleared sel (sanitize / timeout). Preserve cycle pending → PosePending.</summary>
        internal static void NotifySelectionCleared()
        {
            bool keepPending = _pendingCycleRequest;
            _queuedAnimationRef = null;
            _sawChangeAnim = false;
            _stableReadyFrames = 0;
            if (keepPending)
            {
                _state = DirectorState.PosePending;
                _phaseEnteredUnscaled = Time.unscaledTime;
            }
            else
            {
                _state = DirectorState.Orbitting;
                _phaseEnteredUnscaled = -1f;
            }
        }

        /// <summary>Obsolete name kept for call-site compatibility.</summary>
        internal static void NotifySelectionClearedWithoutTransition() => NotifySelectionCleared();

        internal static void TickStuckRecovery(HScene? hScene)
        {
            if (hScene == null)
                return;
            if (_state == DirectorState.Orbitting && !_pendingCycleRequest)
                return;

            var sel = hScene.ctrlFlag?.selectAnimationListInfo;

            // PoseQueued with NowChangeAnim → Changing (ownership handoff).
            if (_state == DirectorState.PoseQueued && hScene.NowChangeAnim)
            {
                EnterChanging();
                return;
            }

            // Still queued, vanilla did not start — kick immediately (idempotent).
            if (_state == DirectorState.PoseQueued
                && !hScene.NowChangeAnim
                && sel != null)
            {
                OrbitBehaviorHub.TryKickQueuedChangeAnimation(hScene);
            }

            // Phantom Changing: entered Changing without ever seeing NowChangeAnim (e.g. NotifyExternal).
            // Normal completion also has !NowChangeAnim && sel==null but _sawChangeAnim — leave that to Tick.
            if (_state == DirectorState.Changing
                && !hScene.NowChangeAnim
                && sel == null
                && !_sawChangeAnim)
            {
                OrbitStateMachineLog.Event("director", "reset_phantom_changing");
                Reset();
                return;
            }

            // Pending-only with nothing owed forever: drop if game already has a live transition.
            if (_state == DirectorState.PosePending)
            {
                SyncFromGameFlags(hScene);
                return;
            }

            if (_state == DirectorState.PoseQueued || _state == DirectorState.Changing)
            {
                if (hScene.NowChangeAnim)
                    _sawChangeAnim = true;

                if (!OrbitBehaviorHub.IsOrbitAssistActive()
                    && _sawChangeAnim
                    && !hScene.NowChangeAnim
                    && sel == null)
                {
                    NotifySelectionCleared();
                }
            }
        }

        internal static void Tick(HScene hScene, OrbitController orbit, CameraControl_Ver2 ctrl)
        {
            if (hScene == null || orbit == null || ctrl == null)
                return;

            SyncFromGameFlags(hScene);
            RetryPendingCycleRequest(hScene);
            TickStuckRecovery(hScene);

            switch (_state)
            {
                case DirectorState.Orbitting:
                case DirectorState.PosePending:
                    break;

                case DirectorState.PoseQueued:
                    if (hScene.NowChangeAnim)
                        EnterChanging();
                    break;

                case DirectorState.Changing:
                    if (hScene.NowChangeAnim)
                        _sawChangeAnim = true;

                    if (IsTransitionComplete(hScene))
                    {
                        _stableReadyFrames++;
                        if (_stableReadyFrames >= StableReadyFramesRequired)
                        {
                            _state = DirectorState.Rebinding;
                            _phaseEnteredUnscaled = Time.unscaledTime;
                            _stableReadyFrames = 0;
                            orbit.InternalRebindAfterPoseChange(hScene, ctrl);
                            _state = DirectorState.Orbitting;
                            _queuedAnimationRef = null;
                            _phaseEnteredUnscaled = -1f;
                            OrbitPoseLandedPolicy.OnPoseLanded(hScene, PoseLandedSource.Rebind);
                            RetryPendingCycleRequest(hScene);
                        }
                    }
                    else
                    {
                        _stableReadyFrames = 0;
                    }
                    break;

                case DirectorState.Rebinding:
                    break;
            }
        }

        /// <summary>
        /// Track game/checkpoint sel and NowChangeAnim into Queued/Changing.
        /// Illegal sel is not tracked — Hub sanitize owns that.
        /// </summary>
        internal static void SyncFromGameFlags(HScene hScene)
        {
            if (hScene == null)
                return;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
                return;

            if (hScene.NowChangeAnim)
            {
                if (_state != DirectorState.Changing && _state != DirectorState.Rebinding)
                    EnterChanging();
                return;
            }

            var sel = ctrlFlag.selectAnimationListInfo;
            if (sel != null)
            {
                if (!OrbitHelpers.IsPoseAllowedUnderFaintness(sel, ctrlFlag))
                    return; // Hub will sanitize

                if (_state == DirectorState.Orbitting || _state == DirectorState.PosePending)
                {
                    _queuedAnimationRef = sel;
                    EnterPoseQueued();
                }
                else if (_state == DirectorState.Changing && !hScene.NowChangeAnim)
                {
                    // Change ended but sel still set briefly — stay/move Queued until Hub/game clears
                    EnterPoseQueued();
                }
            }
        }

        /// <summary>Legacy entry from nowAnimationInfo change — prefer SyncFromGameFlags.</summary>
        internal static void NotifyExternalPoseChange(HScene hScene)
        {
            if (hScene == null)
                return;
            SyncFromGameFlags(hScene);
            if (_state != DirectorState.Orbitting)
                return;

            var nowInfo = hScene.ctrlFlag?.nowAnimationInfo;
            if (nowInfo == null)
                return;
            if (ReferenceEquals(nowInfo, _queuedAnimationRef))
                return;

            // Pose already applied without us seeing sel/NowChangeAnim this frame.
            if (!hScene.NowChangeAnim && hScene.ctrlFlag?.selectAnimationListInfo == null)
            {
                EnterChanging();
                _sawChangeAnim = true;
            }
        }

        internal static bool RequestPoseChange(HScene hScene, PoseChangeSource source, HScene.AnimationListInfo? explicitNext = null)
        {
            _lastHotkeyFailReason = OrbitAssistReasons.None;
            if (hScene == null)
            {
                _lastHotkeyFailReason = OrbitAssistReasons.NoHScene;
                return false;
            }

            // Cycle／選池：換姿中可 latch；選池手動仍走閘
            if (source == PoseChangeSource.Cycle || source == PoseChangeSource.Hotkey || source == PoseChangeSource.SelectPool)
            {
                if (source != PoseChangeSource.SelectPool)
                {
                    OrbitBehaviorHub.RequestMotionEscape(source == PoseChangeSource.Cycle ? "cycle" : "L");
                    OrbitBehaviorHub.TickAfterIdleEscape(hScene);
                    OrbitBehaviorHub.TickIdleEscape(hScene);
                }
            }

            if (IsPoseChangeInFlight)
            {
                if (source == PoseChangeSource.Cycle)
                {
                    _pendingCycleRequest = true;
                    _lastHotkeyFailReason = OrbitAssistReasons.CycleQueued;
                    return false;
                }

                // Hotkey／選池：清掉廢棄的插件-only 態
                if ((source == PoseChangeSource.Hotkey || source == PoseChangeSource.SelectPool)
                    && !hScene.NowChangeAnim
                    && hScene.ctrlFlag?.selectAnimationListInfo == null
                    && (_state == DirectorState.PosePending || _state == DirectorState.Rebinding))
                {
                    bool keep = _pendingCycleRequest;
                    Reset();
                    _pendingCycleRequest = keep;
                    if (keep)
                    {
                        _state = DirectorState.PosePending;
                        _phaseEnteredUnscaled = Time.unscaledTime;
                    }
                }
                else if (IsPoseChangeInFlight)
                {
                    // Stuck PoseQueued：kick 而非永遠失敗
                    if ((source == PoseChangeSource.Hotkey || source == PoseChangeSource.SelectPool)
                        && _state == DirectorState.PoseQueued
                        && !hScene.NowChangeAnim
                        && hScene.ctrlFlag?.selectAnimationListInfo != null
                        && OrbitBehaviorHub.TryKickQueuedChangeAnimation(hScene))
                    {
                        _lastHotkeyFailReason = OrbitAssistReasons.None;
                        return true;
                    }
                    _lastHotkeyFailReason = DescribeBlockFromFlags(hScene);
                    return false;
                }
            }

            if (!CanAcceptRequestForSource(hScene, source, out string blockReason))
            {
                _lastHotkeyFailReason = blockReason;
                if (source == PoseChangeSource.Cycle)
                {
                    _pendingCycleRequest = true;
                    if (_state == DirectorState.Orbitting)
                    {
                        _state = DirectorState.PosePending;
                        _phaseEnteredUnscaled = Time.unscaledTime;
                    }
                    if (blockReason == OrbitAssistReasons.None)
                        _lastHotkeyFailReason = OrbitAssistReasons.CycleQueued;
                }
                return false;
            }

            return TryQueuePoseChange(hScene, explicitNext, source);
        }

        internal static bool RequestHotkeyPoseChange(HScene hScene) =>
            RequestPoseChange(hScene, PoseChangeSource.Hotkey);

        /// <summary>§1 選池專用：自動／手動共用；閘門不含 nowOrgasm。</summary>
        internal static bool RequestSelectPoolPoseChange(HScene hScene) =>
            RequestPoseChange(hScene, PoseChangeSource.SelectPool);

        internal static string DescribeBlockFromFlags(HScene? hScene)
        {
            if (hScene == null) return OrbitAssistReasons.NoHScene;
            if (OrbitManualDirector.IsBusy) return OrbitAssistReasons.ManualBusy;
            if (hScene.NowChangeAnim || _state == DirectorState.Changing)
                return OrbitAssistReasons.Changing;
            if (hScene.ctrlFlag?.selectAnimationListInfo != null || _state == DirectorState.PoseQueued)
                return OrbitAssistReasons.PoseQueued;
            if (_state == DirectorState.Rebinding) return OrbitAssistReasons.Rebinding;
            // §6a：高潮中不擋選池／熱鍵（不再以 nowOrgasm 總擋）
            return OrbitAssistReasons.None;
        }

        private static void EnterPoseQueued()
        {
            _state = DirectorState.PoseQueued;
            _stableReadyFrames = 0;
            _sawChangeAnim = false;
            _phaseEnteredUnscaled = Time.unscaledTime;
        }

        private static void EnterChanging()
        {
            _state = DirectorState.Changing;
            _stableReadyFrames = 0;
            _sawChangeAnim = true;
            _phaseEnteredUnscaled = Time.unscaledTime;
        }

        private static void RetryPendingCycleRequest(HScene hScene)
        {
            if (!_pendingCycleRequest)
                return;
            if (HS2OrbitAndExciter.ChangePoseOnCycle?.Value != true)
            {
                _pendingCycleRequest = false;
                if (_state == DirectorState.PosePending)
                    Reset();
                return;
            }
            if (IsPoseChangeInFlight)
                return;
            if (!CanAcceptRequest(hScene, out _))
                return;

            if (TryQueuePoseChange(hScene, null, PoseChangeSource.Cycle))
                _pendingCycleRequest = false;
        }

        private static bool CanAcceptRequestForSource(HScene hScene, PoseChangeSource source, out string reason)
        {
            if (source == PoseChangeSource.Hotkey || source == PoseChangeSource.SelectPool)
            {
                reason = OrbitManualDirector.DescribeHotkeyBlockReason(hScene);
                return reason == OrbitAssistReasons.None;
            }

            return CanAcceptRequest(hScene, out reason);
        }

        private static bool CanAcceptRequest(HScene hScene, out string reason)
        {
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
            {
                reason = OrbitAssistReasons.NoHScene;
                return false;
            }
            if (hScene.NowChangeAnim)
            {
                reason = OrbitAssistReasons.Changing;
                return false;
            }
            if (ctrlFlag.selectAnimationListInfo != null)
            {
                reason = OrbitAssistReasons.PoseQueued;
                return false;
            }
            // Cycle may run while PosePending; ignore pose/manual busy for write gate —
            // but still respect orgasm/UI via CanAutoAdvance-like checks without poseQueued.
            if (!OrbitBehaviorHub.CanQueueCyclePose(ctrlFlag, out reason))
                return false;
            reason = OrbitAssistReasons.None;
            return true;
        }

        private static bool TryQueuePoseChange(
            HScene hScene,
            HScene.AnimationListInfo? explicitNext,
            PoseChangeSource source)
        {
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
            {
                _lastHotkeyFailReason = OrbitAssistReasons.NoHScene;
                return false;
            }

            HScene.AnimationListInfo? next = explicitNext;
            if (next == null)
            {
                // §1 選池：混池／本場去重／空池放寬；窺視用 ActionCtrl（非 LongAppreciation）
                var all = OrbitHelpers.GetAllPoseList();
                if (all.Count == 0)
                {
                    _lastHotkeyFailReason = OrbitAssistReasons.NoPoseCandidate;
                    return false;
                }
                string trigger = source == PoseChangeSource.Hotkey ? "L"
                    : source == PoseChangeSource.SelectPool ? "選池"
                    : source == PoseChangeSource.Cycle ? "cycle"
                    : "auto";
                var poolPick = OrbitPosePool.TryPick(
                    ctrlFlag.nowAnimationInfo, all, ctrlFlag, trigger);
                if (poolPick == null)
                {
                    _lastHotkeyFailReason = OrbitAssistReasons.NoPoseCandidate;
                    return false;
                }
                next = poolPick.Value.Info;
            }
            if (next == null)
            {
                _lastHotkeyFailReason = OrbitAssistReasons.NoPoseCandidate;
                return false;
            }
            if (!OrbitHelpers.IsPoseAllowedUnderFaintness(next, ctrlFlag))
            {
                _lastHotkeyFailReason = OrbitAssistReasons.NoValidDownPose;
                OrbitStateMachineLog.Event("pose_reject", OrbitAssistReasons.NoValidDownPose,
                    "{\"id\":" + next.id + ",\"down\":" + next.nDownPtn + "}");
                return false;
            }

            ctrlFlag.selectAnimationListInfo = next;
            _queuedAnimationRef = next;
            // Successful write satisfies any owed cycle pose.
            _pendingCycleRequest = false;

            if (OrbitBehaviorHub.IsOrbitAssistActive())
                EnterPoseQueued();
            else
                Reset();

            _lastHotkeyFailReason = OrbitAssistReasons.None;
            return true;
        }

        private static bool IsTransitionComplete(HScene hScene)
        {
            if (!_sawChangeAnim)
                return false;
            if (hScene.NowChangeAnim)
                return false;
            if (hScene.ctrlFlag?.selectAnimationListInfo != null)
                return false;

            var fade = Traverse.Create(hScene).Field("fade").GetValue();
            if (fade != null)
            {
                var isEndProp = fade.GetType().GetProperty("isEnd");
                if (isEndProp != null && !(bool)(isEndProp.GetValue(fade) ?? false))
                    return false;
            }

            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || chaFemales.Length == 0 || chaFemales[0] == null)
                return false;
            if (OrbitHelpers.TryGetFemaleAnimBody(chaFemales[0]) == null)
                return false;

            var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
            if (ctrl == null || ctrl.transBase == null)
                return false;

            return true;
        }
    }
}
