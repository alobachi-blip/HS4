using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Exciter: trigger orgasm only after N seconds at full gauge, or immediately on mouse click (plan §5).
    /// </summary>
    public static class ExciterState
    {
        public static float DelaySecondsAtFull = 0f;
        private static float _becameFullTime = -1f;
        private static bool _consumed;
        private static bool _forceNext;

        public static void ForceNextOrgasmTrigger()
        {
            _forceNext = true;
            _consumed = false;
            _becameFullTime = -1f;
        }

        public static bool ShouldTriggerOrgasmNow()
        {
            if (_forceNext)
            {
                _forceNext = false;
                _consumed = true;
                _becameFullTime = -1f;
                return true;
            }
            if (Input.GetMouseButtonDown(0))
            {
                if (_consumed) return false;
                _consumed = true;
                _becameFullTime = -1f;
                return true;
            }
            if (_becameFullTime < 0f)
                _becameFullTime = Time.time;
            if (DelaySecondsAtFull <= 0f)
                return true;
            if (Time.time - _becameFullTime >= DelaySecondsAtFull)
            {
                if (_consumed) return false;
                _consumed = true;
                _becameFullTime = -1f;
                return true;
            }
            return false;
        }

        public static void NotifyGaugeNotFull()
        {
            _becameFullTime = -1f;
            _consumed = false;
            _forceNext = false;
        }

        /// <summary>
        /// When game reads feel_f for "feel_f >= 1f" we return 0.99f if we want to delay trigger, else actual value.
        /// </summary>
        public static float GetFeelFForOrgasmCheck(HSceneFlagCtrl ctrlFlag)
        {
            if (ctrlFlag == null) return 0f;
            if (ctrlFlag.feel_f < 1f)
            {
                NotifyGaugeNotFull();
                return ctrlFlag.feel_f;
            }
            if (!ShouldTriggerOrgasmNow())
                return 0.99f;
            return ctrlFlag.feel_f;
        }
    }

    /// <summary>
    /// Transpiler: replace "ldfld feel_f" before "ldc.r4 1" with call to GetFeelFForOrgasmCheck
    /// so the orgasm block runs only when ShouldTriggerOrgasmNow() is true.
    /// </summary>
    [HarmonyPatch(typeof(MultiPlay_F2M1), "OLoopAibuProc")]
    public static class ExciterTranspiler_F2M1_OLoopAibuProc
    {
        private static readonly FieldInfo FeelF = AccessTools.Field(typeof(HSceneFlagCtrl), "feel_f");
        private static readonly MethodInfo GetFeelF = AccessTools.Method(typeof(ExciterState), nameof(ExciterState.GetFeelFForOrgasmCheck));

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ExciterTranspilerShared.TranspilerShared(instructions, FeelF, GetFeelF);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "OLoopSonyuProc")]
    public static class ExciterTranspiler_F2M1_OLoopSonyuProc
    {
        private static readonly FieldInfo FeelF = AccessTools.Field(typeof(HSceneFlagCtrl), "feel_f");
        private static readonly MethodInfo GetFeelF = AccessTools.Method(typeof(ExciterState), nameof(ExciterState.GetFeelFForOrgasmCheck));

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ExciterTranspilerShared.TranspilerShared(instructions, FeelF, GetFeelF);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F1M2), "OLoopAibuProc")]
    public static class ExciterTranspiler_F1M2_OLoopAibuProc
    {
        private static readonly FieldInfo FeelF = AccessTools.Field(typeof(HSceneFlagCtrl), "feel_f");
        private static readonly MethodInfo GetFeelF = AccessTools.Method(typeof(ExciterState), nameof(ExciterState.GetFeelFForOrgasmCheck));

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ExciterTranspilerShared.TranspilerShared(instructions, FeelF, GetFeelF);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F1M2), "OLoopSonyuProc")]
    public static class ExciterTranspiler_F1M2_OLoopSonyuProc
    {
        private static readonly FieldInfo FeelF = AccessTools.Field(typeof(HSceneFlagCtrl), "feel_f");
        private static readonly MethodInfo GetFeelF = AccessTools.Method(typeof(ExciterState), nameof(ExciterState.GetFeelFForOrgasmCheck));

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ExciterTranspilerShared.TranspilerShared(instructions, FeelF, GetFeelF);
        }
    }

    // Masturbation/Les/Sonyu/Aibu patches removed: methods not found in this game build, avoid loading them

    [HarmonyPatch(typeof(Spnking), "ActionProc")]
    public static class ExciterTranspiler_Spnking_ActionProc
    {
        private static readonly FieldInfo FeelF = AccessTools.Field(typeof(HSceneFlagCtrl), "feel_f");
        private static readonly MethodInfo GetFeelF = AccessTools.Method(typeof(ExciterState), nameof(ExciterState.GetFeelFForOrgasmCheck));

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ExciterTranspilerShared.TranspilerShared(instructions, FeelF, GetFeelF);
        }
    }

    internal static class ExciterTranspilerShared
    {
        /// <summary>
        /// Replace first occurrence of (ldfld feel_f, ldc.r4 1) with (call GetFeelFForOrgasmCheck, ldc.r4 1).
        /// </summary>
        public static IEnumerable<CodeInstruction> TranspilerShared(
            IEnumerable<CodeInstruction> instructions,
            FieldInfo feelFField,
            MethodInfo getFeelFMethod)
        {
            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (list[i].opcode == OpCodes.Ldfld && list[i].operand as FieldInfo == feelFField &&
                    list[i + 1].opcode == OpCodes.Ldc_R4 && (float)list[i + 1].operand == 1f)
                {
                    list[i] = new CodeInstruction(OpCodes.Call, getFeelFMethod);
                    break;
                }
            }
            return list;
        }
    }
}
