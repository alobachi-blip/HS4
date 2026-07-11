using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>Single-key manual actions in H scene (G/H/J/K/L/T/B/R).</summary>
    internal static class OrbitManualHotkeys
    {
        internal const KeyCode CharaKey = KeyCode.G;
        internal const KeyCode CoordinateKey = KeyCode.H;
        internal const KeyCode WearKey = KeyCode.J;
        internal const KeyCode PoseCameraKey = KeyCode.K;
        internal const KeyCode PoseKey = KeyCode.L;
        /// <summary>T = enable + place next stamp; Shift+T = disable.</summary>
        internal const KeyCode TattooKey = KeyCode.T;
        /// <summary>Restore bust size to H-enter / G-swap baseline.</summary>
        internal const KeyCode BustRestoreKey = KeyCode.B;
        /// <summary>Force PregnancyPlus belly reset (HS2 H-scene inflation + story size).</summary>
        internal const KeyCode BellyResetKey = KeyCode.R;

        /// <summary>Ultra-compact HUD legend: 角=換角, 套=換衣, 著=穿著, 鏡=姿勢鏡頭, 姿=換姿勢, 刺=T貼圖/⇧T關, 胸=胸回復.</summary>
        internal const string HudLegend = "G角·H套·J著·K鏡·L姿·T刺+·⇧T關·B胸";

        /// <summary>PregnancyPlus Live Inflation Shortcuts (KK_PregnancyPlus.cfg); Y/U step scaled 5×; R also forced by this plugin.</summary>
        internal const string PregnancyHudLegend = "Y膨+×5·U膨-×5·R清腹";

        /// <summary>Orgasm FX line prefix for HUD / settings.</summary>
        internal const string OrgasmFxHudPrefix = "高潮";
    }
}
