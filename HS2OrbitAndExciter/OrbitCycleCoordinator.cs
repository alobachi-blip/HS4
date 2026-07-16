using Manager;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Orbit cycle side effects: N rotations → random view (yaw preset only when <paramref name="allowStartYRandom"/>), clothes; M round-trips → pose.
    /// Pose changes are delegated to <see cref="OrbitPoseDirector"/> (single exit).
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
            // Clothes follow auto-advance gate (Pending does not block; Queued/Changing do).
            bool canAssist = OrbitBehaviorHub.CanAutoAdvance(ctrlFlag, out _);
            int n = HS2OrbitAndExciter.OrbitCountBeforeRandom?.Value ?? 0;
            bool hitN = n > 0 && rotationCount % n == 0;
            bool clothesEnabled = HS2OrbitAndExciter.ClothesChangeEnabled?.Value ?? false;
            bool clothesThisBoundary = clothesEnabled && canAssist && (hitN || (n == 0 && roundTripJustCompleted));

            if (clothesThisBoundary)
                orbit.InternalAdvanceClothesStage(hScene);

            if (n > 0 && hitN)
            {
                orbit.InternalRandomizeViewOption(hScene, ctrl);
                if (allowStartYRandom)
                    orbit.InternalRandomizeStartOrbitY();
            }

            if (roundTripJustCompleted)
                ApplyPoseIfNeeded(hScene, rotationCount / 2);
        }

        internal static void ApplyPoseIfNeeded(HScene hScene, int roundTripCount)
        {
            if (HS2OrbitAndExciter.ChangePoseOnCycle?.Value != true)
                return;
            int interval = HS2OrbitAndExciter.OrbitCountBeforePoseChange?.Value ?? 2;
            if (interval <= 0 || roundTripCount <= 0 || roundTripCount % interval != 0)
                return;

            OrbitPoseDirector.RequestPoseChange(hScene, PoseChangeSource.Cycle);
        }
    }
}
