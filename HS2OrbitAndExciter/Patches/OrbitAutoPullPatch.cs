using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    [HarmonyPatch(typeof(HAutoCtrl), nameof(HAutoCtrl.IsPull))]
    internal static class OrbitAutoPullPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (__result)
                return;
            if (OrbitSessionDirector.TryForceInsideAfterAutoPull())
                __result = true;
        }
    }
}
