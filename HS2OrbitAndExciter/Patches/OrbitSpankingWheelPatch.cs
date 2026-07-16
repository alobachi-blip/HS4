using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter.Patches
{
    [HarmonyPatch(typeof(Spnking), "SpankingProc")]
    internal static class OrbitSpankingWheelPatch
    {
        private static readonly MethodInfo InputGetAxis =
            AccessTools.Method(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) });
        private static readonly MethodInfo GetSpankingWheelAxis =
            AccessTools.Method(typeof(OrbitSessionDirector), nameof(OrbitSessionDirector.GetSpankingWheelAxis));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(InputGetAxis))
                    yield return new CodeInstruction(instruction) { operand = GetSpankingWheelAxis };
                else
                    yield return instruction;
            }
        }
    }
}
