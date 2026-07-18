using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Restore temporary bust and belly growth before the native HScene
    /// releases its character references. This covers exits that do not yield
    /// a controller Update frame (scene changes and shutdown are important).
    /// </summary>
    [HarmonyPatch(typeof(HScene), "OnDestroy")]
    internal static class BustGrowthLifecyclePatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            OrbitOrgasmBustGrowth.TryRestoreForLifecycle("h_scene_destroy");
            PregnancyPlusAssist.TryRestoreForLifecycle("h_scene_destroy");
        }
    }
}
