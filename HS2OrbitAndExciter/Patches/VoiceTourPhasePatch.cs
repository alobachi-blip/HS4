using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>Force HVoiceCtrl.CheckPhase to voice-tour stage phase (shy/experienced/dependence/broken).</summary>
    [HarmonyPatch(typeof(HVoiceCtrl), "CheckPhase")]
    internal static class VoiceTourPhasePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref int __result)
        {
            int? forced = OrbitVoiceTour.TryGetForcedPhase();
            if (forced.HasValue)
                __result = forced.Value;
        }
    }
}
