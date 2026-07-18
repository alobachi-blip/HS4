using HarmonyLib;
using UnityEngine;

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

    /// <summary>
    /// Bare L belongs to Orbit's pose director while assist is active. Preserve
    /// the H-scene ambient-light shortcut behind Ctrl+L or Alt+L so the two
    /// actions never fire from the same key press.
    /// </summary>
    [HarmonyPatch(typeof(HScene), "ShortcutKey")]
    internal static class OrbitAmbientLightHotkeyModifierPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out bool __state)
        {
            __state = Manager.Config.GraphicData.AmbientLight;
        }

        [HarmonyPostfix]
        private static void Postfix(bool __state)
        {
            if (!Input.GetKeyDown(KeyCode.L))
                return;

            bool hasModifier = Input.GetKey(KeyCode.LeftControl)
                               || Input.GetKey(KeyCode.RightControl)
                               || Input.GetKey(KeyCode.LeftAlt)
                               || Input.GetKey(KeyCode.RightAlt);
            if (!hasModifier)
                Manager.Config.GraphicData.AmbientLight = __state;
        }
    }
}
