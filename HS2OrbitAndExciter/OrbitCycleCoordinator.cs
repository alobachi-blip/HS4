using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Orbit cycle side effects: N rotations → random view (yaw preset only when <paramref name="allowStartYRandom"/>), clothes; M round-trips → pose.
    /// Suppress gates from <see cref="OrbitBehaviorHub.ShouldSuppressAssist"/>. Camera phase integration stays in <see cref="OrbitController"/>.
    /// </summary>
    internal static class OrbitCycleCoordinator
    {
        internal static void ApplyRotationEffects(
            OrbitController orbit,
            HScene hScene,
            CameraControl_Ver2 ctrl,
            int rotationCount,
            bool allowStartYRandom,
            bool roundTripJustCompleted)
        {
            var ctrlFlag = hScene.ctrlFlag;
            bool suppress = OrbitBehaviorHub.ShouldSuppressAssist(ctrlFlag, out _);
            int n = HS2OrbitAndExciter.OrbitCountBeforeRandom?.Value ?? 0;
            bool hitN = n > 0 && rotationCount % n == 0;
            bool clothesEnabled = HS2OrbitAndExciter.ClothesChangeEnabled?.Value ?? false;
            bool clothesThisBoundary = clothesEnabled && !suppress && (hitN || (n == 0 && roundTripJustCompleted));

            if (clothesThisBoundary)
                orbit.InternalAdvanceClothesStage(hScene);

            if (n > 0 && hitN)
            {
                orbit.InternalRandomizeViewOption(hScene, ctrl);
                if (allowStartYRandom)
                    orbit.InternalRandomizeStartOrbitY();
            }
        }

        internal static void ApplyPoseIfNeeded(HScene hScene, int roundTripCount)
        {
            if (HS2OrbitAndExciter.ChangePoseOnCycle?.Value != true)
                return;
            int m = HS2OrbitAndExciter.OrbitCountBeforePoseChange?.Value ?? 2;
            if (m <= 0)
                return;
            if (roundTripCount % m != 0)
                return;

            if (!TryQueueCyclePoseChange(hScene))
                OrbitBehaviorHub.MarkPendingCyclePoseChange();
        }

        /// <summary>Retry a cycle pose change that was skipped due to suppress / busy animation state.</summary>
        internal static void RetryPendingCyclePoseChange(HScene hScene)
        {
            if (!OrbitBehaviorHub.IsPendingCyclePoseChange())
                return;
            if (HS2OrbitAndExciter.ChangePoseOnCycle?.Value != true)
            {
                OrbitBehaviorHub.ClearPendingCyclePoseChange();
                return;
            }
            if (TryQueueCyclePoseChange(hScene))
                OrbitBehaviorHub.ClearPendingCyclePoseChange();
        }

        /// <summary>
        /// Queue pose via <see cref="HSceneFlagCtrl.selectAnimationListInfo"/> so HScene.Update runs the same
        /// ChangeAnimation path as vanilla auto-action. Do not StartCoroutine(ChangeAnimation) directly.
        /// </summary>
        internal static bool TryQueueCyclePoseChange(HScene hScene)
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

            var all = OrbitHelpers.GetAllPoseList();
            if (all.Count == 0)
                return false;
            var current = ctrlFlag.nowAnimationInfo;
            var next = OrbitHelpers.PickNextPose(current, all);
            if (next == null)
                return false;

            ctrlFlag.selectAnimationListInfo = next;
            OrbitBehaviorHub.NotifyCyclePoseChangeQueued();
            return true;
        }
    }
}
