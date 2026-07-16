using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// 1v1 H modes (插入／愛撫／奉仕)：滾輪門檻與 <see cref="MultiPlay_F2M1"/> 不同類別、簽名亦不同，需獨立 patch。
    /// 簽名對照 <c>dll_decompiled/Sonyu.cs</c>、<c>Aibu.cs</c>、<c>Houshi.cs</c>。
    /// </summary>
    [HarmonyPatch(typeof(Sonyu), "StartProcTrigger")]
    public static class OrbitBypass1v1_Sonyu_StartProcTrigger
    {
        [HarmonyPrefix]
        private static void Prefix(ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Sonyu), "StartProc")]
    public static class OrbitBypass1v1_Sonyu_StartProc
    {
        [HarmonyPrefix]
        private static void Prefix(bool _restart, int _state, int _modeCtrl, ref float wheel) =>
            OrbitBypassWheelState.TryBypass(ref wheel);
    }

    [HarmonyPatch(typeof(Sonyu), "AutoStartProcTrigger")]
    public static class OrbitBypass1v1_Sonyu_AutoStartProcTrigger
    {
        [HarmonyPrefix]
        private static void Prefix(bool _start, ref float wheel) =>
            OrbitBypassWheelState.TryBypass(ref wheel);
    }

    [HarmonyPatch(typeof(Sonyu), "AutoStartProc")]
    public static class OrbitBypass1v1_Sonyu_AutoStartProc
    {
        [HarmonyPrefix]
        private static void Prefix(bool _restart, int _state, int _modeCtrl, ref float wheel) =>
            OrbitBypassWheelState.TryBypass(ref wheel);
    }

    [HarmonyPatch(typeof(Sonyu), "AfterTheInsideWaitingProc")]
    public static class OrbitBypass1v1_Sonyu_AfterTheInsideWaitingProc
    {
        [HarmonyPrefix]
        private static void Prefix(int _state, ref float _wheel) =>
            _ = OrbitSessionDirector.TryInjectInsideAfterWheel(ref _wheel)
                || OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Sonyu), "AutoAfterTheInsideWaitingProc")]
    public static class OrbitBypass1v1_Sonyu_AutoAfterTheInsideWaitingProc
    {
        [HarmonyPrefix]
        private static void Prefix(int _state, ref float _wheel, int _modeCtrl) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Aibu), "StartProcTrigger")]
    public static class OrbitBypass1v1_Aibu_StartProcTrigger
    {
        [HarmonyPrefix]
        private static void Prefix(ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Aibu), "StartProc")]
    public static class OrbitBypass1v1_Aibu_StartProc
    {
        [HarmonyPrefix]
        private static void Prefix(bool _isReStart, ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Aibu), "FaintnessStartProcTrigger")]
    public static class OrbitBypass1v1_Aibu_FaintnessStartProcTrigger
    {
        [HarmonyPrefix]
        private static void Prefix(ref float _wheel, bool _start, int _modeCtrl) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Aibu), "FaintnessStartProc")]
    public static class OrbitBypass1v1_Aibu_FaintnessStartProc
    {
        [HarmonyPrefix]
        private static void Prefix(bool _start, ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    // Aibu.AutoStartProcTrigger(bool _start) 無 wheel 參數，略過。

    [HarmonyPatch(typeof(Aibu), "AutoStartProc")]
    public static class OrbitBypass1v1_Aibu_AutoStartProc
    {
        [HarmonyPrefix]
        private static void Prefix(bool _isReStart, ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Houshi), "StartProcTrigger")]
    public static class OrbitBypass1v1_Houshi_StartProcTrigger
    {
        [HarmonyPrefix]
        private static void Prefix(ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Houshi), "StartProc")]
    public static class OrbitBypass1v1_Houshi_StartProc
    {
        [HarmonyPrefix]
        private static void Prefix(int _state, bool _restart, ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Houshi), "AutoStartProcTrigger")]
    public static class OrbitBypass1v1_Houshi_AutoStartProcTrigger
    {
        [HarmonyPrefix]
        private static void Prefix(bool _start, ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }

    [HarmonyPatch(typeof(Houshi), "AutoStartProc")]
    public static class OrbitBypass1v1_Houshi_AutoStartProc
    {
        [HarmonyPrefix]
        private static void Prefix(int _state, bool _restart, ref float _wheel) =>
            OrbitBypassWheelState.TryBypass(ref _wheel);
    }
}
