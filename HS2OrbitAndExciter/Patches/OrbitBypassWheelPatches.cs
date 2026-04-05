using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// When orbit is active, bypass wheel checkpoints: after a short delay inject a small wheel value so the game advances from Idle (and related gates) without user input.
    /// Injection only runs when the first female's animator is in an allowed state (Idle, D_Idle, WIdle, SIdle, Insert, D_Insert) — not WLoop/OLoop, so scroll speed/Finish are not hijacked.
    /// </summary>
    internal static class OrbitBypassWheelState
    {
        public const float BypassWheelValue = 0.10f;
        public const float DelaySeconds = 2f;
        private static float _bypassStartTimeUnscaled = -1f;

        public static bool TryBypass(ref float wheel)
        {
            if (!OrbitController.IsOrbitActive() || wheel != 0f)
            {
                _bypassStartTimeUnscaled = -1f;
                return false;
            }

            if (!OrbitBypassAnimatorGate.IsBypassAllowedForCurrentHScene())
            {
                _bypassStartTimeUnscaled = -1f;
                return false;
            }

            if (_bypassStartTimeUnscaled < 0f)
                _bypassStartTimeUnscaled = Time.unscaledTime;

            float elapsed = Time.unscaledTime - _bypassStartTimeUnscaled;
            if (elapsed < DelaySeconds)
                return false;

            _bypassStartTimeUnscaled = -1f;
            wheel = BypassWheelValue;
            return true;
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "StartProcTrigger")]
    public static class OrbitBypass_StartProcTrigger
    {
        [HarmonyPrefix]
        static void Prefix(ref float _wheel)
        {
            OrbitBypassWheelState.TryBypass(ref _wheel);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "StartAibuProc")]
    public static class OrbitBypass_StartAibuProc
    {
        [HarmonyPrefix]
        static void Prefix(bool _isReStart, ref float wheel)
        {
            OrbitBypassWheelState.TryBypass(ref wheel);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "StartHoushiProc")]
    public static class OrbitBypass_StartHoushiProc
    {
        [HarmonyPrefix]
        static void Prefix(int _state, bool _restart, ref float wheel)
        {
            OrbitBypassWheelState.TryBypass(ref wheel);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "FaintnessStartProcTrigger")]
    public static class OrbitBypass_FaintnessStartProcTrigger
    {
        [HarmonyPrefix]
        static void Prefix(ref float _wheel)
        {
            OrbitBypassWheelState.TryBypass(ref _wheel);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "FaintnessStartAibuProc")]
    public static class OrbitBypass_FaintnessStartAibuProc
    {
        [HarmonyPrefix]
        static void Prefix(bool _start, ref float wheel)
        {
            OrbitBypassWheelState.TryBypass(ref wheel);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "AfterTheInsideWaitingProc")]
    public static class OrbitBypass_AfterTheInsideWaitingProc
    {
        [HarmonyPrefix]
        static void Prefix(int _state, ref float _wheel, int _modeCtrl)
        {
            OrbitBypassWheelState.TryBypass(ref _wheel);
        }
    }

    [HarmonyPatch(typeof(Masturbation), "StartProcTrriger")]
    public static class OrbitBypass_Masturbation_StartProcTrriger
    {
        [HarmonyPrefix]
        static void Prefix(ref float _wheel)
        {
            OrbitBypassWheelState.TryBypass(ref _wheel);
        }
    }

    // Aibu Idle/D_Idle auto-entry (wheel==0 gate) bypass
    [HarmonyPatch(typeof(MultiPlay_F2M1), "AutoStartProcTrigger")]
    public static class OrbitBypass_AutoStartProcTrigger
    {
        [HarmonyPrefix]
        static void Prefix(bool _start, ref float wheel)
        {
            OrbitBypassWheelState.TryBypass(ref wheel);
        }
    }

    [HarmonyPatch(typeof(MultiPlay_F2M1), "AutoStartAibuProc")]
    public static class OrbitBypass_AutoStartAibuProc
    {
        [HarmonyPrefix]
        static void Prefix(bool _isReStart, ref float wheel)
        {
            OrbitBypassWheelState.TryBypass(ref wheel);
        }
    }

    // Houshi Idle/D_Idle auto-entry bypass (wheel==0 gate)
    [HarmonyPatch(typeof(MultiPlay_F2M1), "AutoStartHoushiProc")]
    public static class OrbitBypass_AutoStartHoushiProc
    {
        [HarmonyPrefix]
        static void Prefix(int _state, bool _restart, ref float wheel)
        {
            OrbitBypassWheelState.TryBypass(ref wheel);
        }
    }

    // Sonyu Idle auto-entry bypass (wheel==0 gate)
    [HarmonyPatch(typeof(MultiPlay_F2M1), "AutoStartSonyuProc")]
    public static class OrbitBypass_AutoStartSonyuProc
    {
        [HarmonyPrefix]
        static void Prefix(bool _restart, int _state, int _modeCtrl, ref float wheel)
        {
            OrbitBypassWheelState.TryBypass(ref wheel);
        }
    }

    // Sonyu D_Idle bypass uses StartSonyuProc (not AutoStartSonyuProc)
    [HarmonyPatch(typeof(MultiPlay_F2M1), "StartSonyuProc")]
    public static class OrbitBypass_StartSonyuProc
    {
        [HarmonyPrefix]
        static void Prefix(bool _restart, int _state, int _modeCtrl, ref float wheel)
        {
            OrbitBypassWheelState.TryBypass(ref wheel);
        }
    }

    // Inside-waiting follow-up bypass (wheel==0 gate)
    [HarmonyPatch(typeof(MultiPlay_F2M1), "AutoAfterTheInsideWaitingProc")]
    public static class OrbitBypass_AutoAfterTheInsideWaitingProc
    {
        [HarmonyPrefix]
        static void Prefix(int _state, ref float _wheel)
        {
            OrbitBypassWheelState.TryBypass(ref _wheel);
        }
    }
}
