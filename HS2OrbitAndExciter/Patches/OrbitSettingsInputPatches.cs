using HarmonyLib;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Ctrl+Shift+P is a modal IMGUI window. IMGUI does not register with the
    /// game's EventSystem, so explicitly keep its input away from the H scene.
    /// </summary>
    [HarmonyPatch(typeof(HSceneSprite), nameof(HSceneSprite.IsSpriteOver))]
    internal static class OrbitSettingsSpriteInputPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (OrbitSettingsGUI.IsVisible)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(HScene), "ShortcutKey")]
    internal static class OrbitSettingsShortcutInputPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (!OrbitSettingsGUI.IsVisible)
                return true;

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraControl_Ver2), "LateUpdate")]
    internal static class OrbitSettingsCameraInputPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !OrbitSettingsGUI.IsVisible;
    }
}
