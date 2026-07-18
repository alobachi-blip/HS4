using System;
using System.Reflection;
using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// PregnancyPlus serializes infConfig.inflationSize from
    /// OnCardBeingSaved. Orbit belly growth is a session effect, so expose the
    /// pre-H value only while that callback builds the card's PluginData.
    /// </summary>
    [HarmonyPatch]
    internal static class PregnancyPlusSavePatch
    {
        private static MethodBase? TargetMethod()
        {
            var controller = AccessTools.TypeByName(
                "KK_PregnancyPlus.PregnancyPlusCharaController");
            return controller == null
                ? null
                : AccessTools.Method(controller, "OnCardBeingSaved");
        }

        internal static bool Prepare() => TargetMethod() != null;

        [HarmonyPrefix]
        private static void Prefix(object __instance, out float __state)
        {
            __state = float.NaN;
            if (PregnancyPlusAssist.TryPrepareBellyForSave(__instance, out float runtimeValue))
                __state = runtimeValue;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, float __state)
        {
            PregnancyPlusAssist.RestoreBellyAfterSave(__instance, __state);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(object __instance, float __state, Exception? __exception)
        {
            PregnancyPlusAssist.RestoreBellyAfterSave(__instance, __state);
            return __exception;
        }
    }
}
