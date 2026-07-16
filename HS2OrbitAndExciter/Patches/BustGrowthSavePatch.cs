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
        private static readonly Dictionary<ChaFileControl, float> PendingRuntimeValues =
            new Dictionary<ChaFileControl, float>();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(ChaFileControl)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == nameof(ChaFileControl.SaveCharaFile));
        }

        [HarmonyPrefix]
        private static void Prefix(ChaFileControl __instance)
        {
            if (OrbitOrgasmBustGrowth.TryPrepareForSave(__instance, out float runtimeValue))
                PendingRuntimeValues[__instance] = runtimeValue;
        }

        [HarmonyPostfix]
        private static void Postfix(ChaFileControl __instance)
        {
            Restore(__instance);
        }

        [HarmonyFinalizer]
        private static System.Exception? Finalizer(ChaFileControl __instance, System.Exception? exception)
        {
            Restore(__instance);
            return exception;
        }

        private static void Restore(ChaFileControl instance)
        {
            if (!PendingRuntimeValues.TryGetValue(instance, out float runtimeValue))
                return;

            PendingRuntimeValues.Remove(instance);
            OrbitOrgasmBustGrowth.RestoreAfterSave(runtimeValue);
        }
    }
}
