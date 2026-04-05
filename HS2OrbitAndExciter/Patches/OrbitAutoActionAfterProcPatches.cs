using HarmonyLib;
using HS2OrbitAndExciter;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// After each H scene Proc (Aibu/Sonyu/etc.) runs in Update, re-apply isAutoActionChange and initiative
    /// so the game's subsequent check (isAutoActionChange && selectAnimationListInfo == null) sees true
    /// and calls GetAutoAnimation. Without this, Proc overwrites our LateUpdate-set values before the check.
    /// </summary>
    [HarmonyPatch(typeof(ProcBase), nameof(ProcBase.Proc))]
    public static class OrbitAutoActionAfterProcPatches
    {
        [HarmonyPostfix]
        public static void Postfix(ProcBase __instance)
        {
            if (__instance == null) return;
            if (!OrbitBehaviorHub.IsOrbitAssistActive()) return;
            if (HS2OrbitAndExciter.EnableAfterProcAssistPostfixFallback?.Value != true)
                return;
            var ctrlFlag = Traverse.Create(__instance).Field("ctrlFlag").GetValue() as HSceneFlagCtrl;
            OrbitBehaviorHub.TryPushOrbitAutoActionAssist(ctrlFlag);
        }
    }
}
