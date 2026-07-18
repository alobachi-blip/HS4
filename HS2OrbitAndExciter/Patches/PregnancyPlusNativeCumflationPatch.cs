using System.Reflection;
using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Orbit owns male-finish growth while its all-orgasm option is enabled.
    /// Suppress PregnancyPlus' native finish hook without changing its config
    /// file, otherwise inside/drink finishes are counted twice and outside
    /// finishes can reset Orbit's visible result.
    /// </summary>
    [HarmonyPatch]
    internal static class PregnancyPlusNativeCumflationPatch
    {
        private static MethodBase? TargetMethod()
        {
            var hooks = AccessTools.TypeByName(
                "KK_PregnancyPlus.PregnancyPlusPlugin+Hooks_HS2_Inflation");
            return hooks == null ? null : AccessTools.Method(hooks, "TriggerInflation");
        }

        internal static bool Prepare() => TargetMethod() != null;

        [HarmonyPrefix]
        private static bool Prefix()
        {
            return HS2OrbitAndExciter.CumflationInflateOnInside?.Value != true;
        }
    }
}
