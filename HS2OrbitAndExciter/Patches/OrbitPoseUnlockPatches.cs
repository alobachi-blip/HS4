using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Restores the safe part of the old all-pose unlock: relax state/achievement/pain/faintness
    /// failures, while preserving hard limits for actors, event, place, and AppendEV.
    /// </summary>
    [HarmonyPatch]
    public static class OrbitPoseUnlockPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneSprite), nameof(HSceneSprite.CheckMotionLimit))]
        private static void CheckMotionLimitPostfix(
            HSceneSprite __instance,
            HScene.AnimationListInfo lstAnimInfo,
            ref bool __result)
        {
            OrbitPoseUnlockPolicy.TryRelaxSafeChecks(
                __instance,
                lstAnimInfo,
                OrbitPoseUnlockCheckKind.MotionLimit,
                ref __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneSprite), nameof(HSceneSprite.CheckMotionLimitRecover))]
        private static void CheckMotionLimitRecoverPostfix(
            HSceneSprite __instance,
            HScene.AnimationListInfo lstAnimInfo,
            ref bool __result)
        {
            OrbitPoseUnlockPolicy.TryRelaxSafeChecks(
                __instance,
                lstAnimInfo,
                OrbitPoseUnlockCheckKind.MotionLimitRecover,
                ref __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneSprite), "CheckAutoMotionLimit")]
        private static void CheckAutoMotionLimitPostfix(
            HSceneSprite __instance,
            HScene.AnimationListInfo lstAnimInfo,
            ref bool __result)
        {
            OrbitPoseUnlockPolicy.TryRelaxSafeChecks(
                __instance,
                lstAnimInfo,
                OrbitPoseUnlockCheckKind.AutoMotionLimit,
                ref __result);
        }
    }
}
