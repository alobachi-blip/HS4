using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>Single-key manual actions in H scene (G/H/J/K/L).</summary>
    internal static class OrbitManualHotkeys
    {
        internal const KeyCode CharaKey = KeyCode.G;
        internal const KeyCode CoordinateKey = KeyCode.H;
        internal const KeyCode WearKey = KeyCode.J;
        internal const KeyCode PoseCameraKey = KeyCode.K;
        internal const KeyCode PoseKey = KeyCode.L;

        /// <summary>Ultra-compact HUD legend: 角=換角, 套=換衣, 著=穿著, 鏡=姿勢鏡頭, 姿=換姿勢.</summary>
        internal const string HudLegend = "G角·H套·J著·K鏡·L姿";

        /// <summary>PregnancyPlus Live Inflation Shortcuts (KK_PregnancyPlus.cfg); step scaled 5× via <see cref="Patches.PregnancyPlusInflationStepPatch"/>.</summary>
        internal const string PregnancyHudLegend = "Y膨+×5·U膨-×5·R清腹";
    }
}
