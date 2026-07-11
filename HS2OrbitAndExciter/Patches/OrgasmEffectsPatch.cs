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
        private static void Postfix(HSceneFlagCtrl __instance) =>
            OrbitBehaviorHub.NotifyFemaleOrgasm(__instance);
    }
}
