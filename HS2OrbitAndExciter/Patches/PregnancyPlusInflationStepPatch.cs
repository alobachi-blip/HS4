using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>PregnancyPlus Y/U hotkeys step by 2 per press; scale to 10 (5×) for faster belly adjustment.</summary>
    [HarmonyPatch]
    internal static class PregnancyPlusInflationStepPatch
    {
        private const int VanillaStep = 2;
        internal const int ScaledStep = 10;

        private static MethodBase? TargetMethod()
        {
            var type = AccessTools.TypeByName("KK_PregnancyPlus.PregnancyPlusCharaController");
            return type == null ? null : AccessTools.Method(type, "WatchForUserKeyPress");
        }

        internal static bool Prepare() => TargetMethod() != null;

        [HarmonyPrefix]
        private static void Prefix()
        {
            // Also covers manual Y/U inflation before the first inside finish.
            PregnancyPlusAssist.TryRaiseMaxInflationLevel();
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Ldc_I4_2
                    || (code.opcode == OpCodes.Ldc_I4 && code.operand is int n && n == VanillaStep))
                    yield return new CodeInstruction(OpCodes.Ldc_I4, ScaledStep);
                else
                    yield return code;
            }
        }
    }
}
