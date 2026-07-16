using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Restore temporary bust growth before the native HScene releases its
    /// character references. This covers exits that do not yield a controller
    /// Update frame (scene changes and shutdown are the important cases).
    /// </summary>
    [HarmonyPatch(typeof(HScene), "OnDestroy")]
    internal static class BustGrowthLifecyclePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            OrbitOrgasmBustGrowth.TryRestoreForLifecycle("h_scene_destroy");
        }
    }
}
