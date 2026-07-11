using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    [HarmonyPatch(typeof(HSceneFlagCtrl), nameof(HSceneFlagCtrl.AddOrgasm))]
    internal static class OrgasmEffectsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HSceneFlagCtrl __instance)
        {
            OrbitOrgasmTattoo.OnOrgasm(__instance);
            OrbitOrgasmBustGrowth.OnOrgasm(__instance);
            OrbitOrgasmNippleSpray.OnOrgasm(__instance);
            OrbitVoiceTour.OnFemaleOrgasm();
        }
    }
}
