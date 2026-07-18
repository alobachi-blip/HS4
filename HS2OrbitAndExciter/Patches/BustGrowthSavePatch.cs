using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIChara;
using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Keep temporary orgasm growth out of any native character-card save while
    /// preserving the enlarged runtime appearance during H.
    /// </summary>
    [HarmonyPatch]
    internal static class BustGrowthSavePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(ChaFileControl)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == nameof(ChaFileControl.SaveCharaFile));
        }

        [HarmonyPrefix]
        private static void Prefix(ChaFileControl __instance, out float __state)
        {
            __state = float.NaN;
            if (OrbitOrgasmBustGrowth.TryPrepareForSave(__instance, out float runtimeValue))
                __state = runtimeValue;
        }

        [HarmonyPostfix]
        private static void Postfix(float __state)
        {
            Restore(__state);
        }

        [HarmonyFinalizer]
        private static System.Exception? Finalizer(float __state, System.Exception? __exception)
        {
            Restore(__state);
            return __exception;
        }

        private static void Restore(float runtimeValue)
        {
            if (float.IsNaN(runtimeValue))
                return;
            OrbitOrgasmBustGrowth.RestoreAfterSave(runtimeValue);
        }
    }
}
