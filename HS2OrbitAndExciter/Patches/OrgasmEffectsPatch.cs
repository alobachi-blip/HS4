using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Female orgasm entry → <see cref="OrbitBehaviorHub.NotifyFemaleOrgasm"/> (FX + assist quiet).
    /// </summary>
    [HarmonyPatch(typeof(HSceneFlagCtrl), nameof(HSceneFlagCtrl.AddOrgasm))]
    internal static class OrgasmEffectsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HSceneFlagCtrl __instance)
        {
            // AddOrgasm is the native female-orgasm entry point.  This explicit
            // trace event lets stress tests prove that the flow keeps advancing
            // after native faintness begins, without inferring from clip names.
            OrbitStateMachineLog.Event(
                "female_orgasm",
                "add",
                "{\"faint\":" + (__instance.isFaintness ? "true" : "false")
                + ",\"faintType\":" + __instance.FaintnessType + "}");
            OrbitBehaviorHub.NotifyFemaleOrgasm(__instance);
        }
    }
}
