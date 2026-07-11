using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Leave AfterIdle/Idle only when <see cref="OrbitBehaviorHub.RequestMotionEscape"/> was armed
    /// (L / real mouse wheel / cycle pose). Long waits (bath/toilet) stay until then.
    /// </summary>
    [HarmonyPatch(typeof(HAutoCtrl), nameof(HAutoCtrl.IsReStart))]
    public static class OrbitAutoAfterIdleRestartPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (__result)
                return;
            if (!OrbitBehaviorHub.ShouldForceAfterIdleEscape())
                return;
            __result = true;
            OrbitStateMachineLog.Event("afteridle", "force_IsReStart");
        }
    }

    /// <summary>Auto Idle start — only when escape armed (not time-based).</summary>
    [HarmonyPatch(typeof(HAutoCtrl), nameof(HAutoCtrl.IsStart))]
    public static class OrbitAutoIdleStartPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (__result)
                return;
            if (!OrbitBehaviorHub.ShouldForceIdleEscape())
                return;
            __result = true;
            OrbitStateMachineLog.Event("idle", "force_IsStart");
        }
    }

    /// <summary>Sonyu Auto AfterIdle: skip IsReStart gate and play WLoop/D_WLoop.</summary>
    [HarmonyPatch(typeof(Sonyu), "AutoAfterTheInsideWaitingProc")]
    public static class OrbitForceSonyuAutoAfterIdlePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(object __instance, int _state, ref bool __result)
        {
            if (!OrbitBehaviorHub.ShouldForceAfterIdleEscape())
                return true;
            if (!OrbitAfterIdleEscape.TryForceLoop(__instance, _state))
                return true;
            __result = true;
            OrbitStateMachineLog.Event("afteridle", "force_Sonyu_AutoAfterIdle");
            return false;
        }
    }

    /// <summary>Sonyu Manual AfterIdle: inject positive wheel so nextPlay advances.</summary>
    [HarmonyPatch(typeof(Sonyu), "AfterTheInsideWaitingProc")]
    public static class OrbitForceSonyuManualAfterIdlePatch
    {
        [HarmonyPrefix]
        private static void Prefix(object __instance, int _state, ref float _wheel)
        {
            OrbitBypassWheelState.TryBypass(ref _wheel);
            if (!OrbitBehaviorHub.ShouldForceAfterIdleEscape())
                return;
            if (_wheel == 0f)
                _wheel = OrbitBehaviorHub.WheelBypassValue;
            // Also force nextPlay into the "go to loop" branch when still stuck.
            try
            {
                var t = Traverse.Create(__instance);
                int nextPlay = t.Field("nextPlay").GetValue<int>();
                if (nextPlay == 0)
                    t.Field("nextPlay").SetValue(1);
            }
            catch { /* ignore */ }
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "AutoAfterTheInsideWaitingProc")]
    public static class OrbitForceF2M1AutoAfterIdlePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(object __instance, int _state, ref bool __result)
        {
            if (!OrbitBehaviorHub.ShouldForceAfterIdleEscape())
                return true;
            if (!OrbitAfterIdleEscape.TryForceLoop(__instance, _state))
                return true;
            __result = true;
            OrbitStateMachineLog.Event("afteridle", "force_F2M1_AutoAfterIdle");
            return false;
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F1M2), "AutoAfterTheInsideWaitingProc")]
    public static class OrbitForceF1M2AutoAfterIdlePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(object __instance, int _state, ref bool __result)
        {
            if (!OrbitBehaviorHub.ShouldForceAfterIdleEscape())
                return true;
            if (!OrbitAfterIdleEscape.TryForceLoop(__instance, _state))
                return true;
            __result = true;
            OrbitStateMachineLog.Event("afteridle", "force_F1M2_AutoAfterIdle");
            return false;
        }
    }

    internal static class OrbitAfterIdleEscape
    {
        internal static bool TryForceLoop(object procInstance, int state)
        {
            try
            {
                string anim = state == 0 ? "WLoop" : "D_WLoop";
                Traverse.Create(procInstance).Method("setPlay", anim).GetValue();
                var ctrlFlag = Traverse.Create(procInstance).Field("ctrlFlag").GetValue() as HSceneFlagCtrl;
                if (ctrlFlag != null)
                {
                    ctrlFlag.speed = 0f;
                    try { Traverse.Create(ctrlFlag).Field("loopType").SetValue(0); } catch { /* ignore */ }
                    ctrlFlag.nowSpeedStateFast = false;
                    ctrlFlag.nowOrgasm = false;
                }
                var auto = Traverse.Create(procInstance).Field("auto").GetValue() as HAutoCtrl;
                auto?.ReStartInit();
                auto?.PullInit();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
