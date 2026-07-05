using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>Single-key manual actions in H scene (G/H/J/K).</summary>
    internal static class OrbitManualHotkeys
    {
        internal const KeyCode CharaKey = KeyCode.G;
        internal const KeyCode CoordinateKey = KeyCode.H;
        internal const KeyCode WearKey = KeyCode.J;
        internal const KeyCode PoseCameraKey = KeyCode.K;

        /// <summary>Ultra-compact HUD legend: 角=換角, 套=換衣, 著=穿著, 鏡=姿勢鏡頭預設.</summary>
        internal const string HudLegend = "G角·H套·J著·K鏡";
    }
}
