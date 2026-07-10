using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    [HarmonyPatch(typeof(HSceneFlagCtrl), nameof(HSceneFlagCtrl.AddOrgasm))]
    internal static class OrgasmTattooPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HSceneFlagCtrl __instance)
        {
            OrbitOrgasmTattoo.OnOrgasm(__instance);
        }
    }
}
