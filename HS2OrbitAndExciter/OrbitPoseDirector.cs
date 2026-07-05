using HarmonyLib;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    internal enum PoseChangeSource { Cycle, External }

    internal enum DirectorState
    {
        Orbitting,
        PosePending,
        PoseTransition,
        Rebinding
    }

    /// <summary>
    /// Single coordinator for orbit pose changes: queue, transition detection, rebind.
    /// Cycle + UI manual are the only pose-change sources; auto/checkpoint do not invoke here (B1).
    /// </summary>
    internal static class OrbitPoseDirector
    {
        private const int StableReadyFramesRequired = 3;

        private static DirectorState _state = DirectorState.Orbitting;
        private static bool _pendingCycleRequest;
        private static int _stableReadyFrames;
        private static object? _queuedAnimationRef;
        /// <summary>True once NowChangeAnim was seen, or external pose already applied.</summary>
        private static bool _sawChangeAnim;

        internal static bool IsTransitionActive =>
            _state != DirectorState.Orbitting;

        internal static bool IsCameraPaused =>
            _state == DirectorState.PoseTransition || _state == DirectorState.Rebinding;

        internal static bool ShouldFreezeCycleCounters => IsCameraPaused;

        internal static void Reset()
        {
            _state = DirectorState.Orbitting;
            _pendingCycleRequest = false;
            _stableReadyFrames = 0;
            _queuedAnimationRef = null;
            _sawChangeAnim = false;
        }

        internal static void Tick(HScene hScene, OrbitController orbit, CameraControl_Ver2 ctrl)
        {
            if (hScene == null || orbit == null || ctrl == null)
                return;

            RetryPendingCycleRequest(hScene);

            switch (_state)
            {
                case DirectorState.Orbitting:
                    if (hScene.NowChangeAnim)
                        EnterPoseTransition();
                    break;

                case DirectorState.PosePending:
                    if (hScene.NowChangeAnim || hScene.ctrlFlag?.selectAnimationListInfo != null)
                        EnterPoseTransition();
                    break;

                case DirectorState.PoseTransition:
                    if (hScene.NowChangeAnim)
                        _sawChangeAnim = true;

                    if (IsTransitionComplete(hScene))
                    {
                        _stableReadyFrames++;
                        if (_stableReadyFrames >= StableReadyFramesRequired)
                        {
                            _state = DirectorState.Rebinding;
                            _stableReadyFrames = 0;
                            orbit.InternalRebindAfterPoseChange(hScene, ctrl);
                            _state = DirectorState.Orbitting;
                            _queuedAnimationRef = null;
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

        internal static bool RequestPoseChange(HScene hScene, PoseChangeSource source, HScene.AnimationListInfo? explicitNext = null)
        {
            if (hScene == null)
                return false;

            if (IsTransitionActive)
            {
                if (source == PoseChangeSource.Cycle)
                    _pendingCycleRequest = true;
                return false;
            }

            if (!CanAcceptRequest(hScene))
            {
                if (source == PoseChangeSource.Cycle)
                {
                    _pendingCycleRequest = true;
                    _state = DirectorState.PosePending;
                }
                return false;
            }

            return TryQueuePoseChange(hScene, explicitNext);
        }

        internal static void NotifyExternalPoseChange(HScene hScene)
        {
            if (hScene == null || _state != DirectorState.Orbitting)
                return;

            var nowInfo = hScene.ctrlFlag?.nowAnimationInfo;
            if (nowInfo == null)
                return;
            if (ReferenceEquals(nowInfo, _queuedAnimationRef))
                return;

            EnterPoseTransition();
            if (hScene.NowChangeAnim)
                _sawChangeAnim = true;
            else if (hScene.ctrlFlag?.selectAnimationListInfo == null)
                _sawChangeAnim = true;
        }

        private static void EnterPoseTransition()
        {
            _state = DirectorState.PoseTransition;
            _stableReadyFrames = 0;
            _sawChangeAnim = false;
        }

        private static void RetryPendingCycleRequest(HScene hScene)
        {
            if (!_pendingCycleRequest)
                return;
            if (HS2OrbitAndExciter.ChangePoseOnCycle?.Value != true)
            {
                _pendingCycleRequest = false;
                if (_state == DirectorState.PosePending)
                    _state = DirectorState.Orbitting;
                return;
            }
            if (IsTransitionActive && _state != DirectorState.PosePending)
                return;
            if (!CanAcceptRequest(hScene))
                return;

            if (TryQueuePoseChange(hScene, null))
                _pendingCycleRequest = false;
        }

        private static bool CanAcceptRequest(HScene hScene)
        {
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
                return false;
            if (OrbitBehaviorHub.ShouldSuppressAssist(ctrlFlag, out _))
                return false;
            if (hScene.NowChangeAnim)
                return false;
            if (ctrlFlag.selectAnimationListInfo != null)
                return false;
            return true;
        }

        private static bool TryQueuePoseChange(HScene hScene, HScene.AnimationListInfo? explicitNext)
        {
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
                return false;

            HScene.AnimationListInfo? next = explicitNext;
            if (next == null)
            {
                var all = OrbitHelpers.GetAllPoseList();
                if (all.Count == 0)
                    return false;
                next = OrbitHelpers.PickNextPose(ctrlFlag.nowAnimationInfo, all);
            }
            if (next == null)
                return false;

            ctrlFlag.selectAnimationListInfo = next;
            _queuedAnimationRef = next;
            EnterPoseTransition();
            return true;
        }

        private static bool IsTransitionComplete(HScene hScene)
        {
            if (!_sawChangeAnim)
                return false;
            if (hScene.NowChangeAnim)
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
