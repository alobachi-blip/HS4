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
        /// <summary>C＝停止／恢復環視相機轉動（不關協助）。與 B／N 同排；避開 Item Layer Edit 的 V。</summary>
        internal const KeyCode StopOrbitCameraKey = KeyCode.C;

        /// <summary>Force leave Idle/AfterIdle — 語意改為「往前推」（依格：開幹／加速／選池）。</summary>
        internal const KeyCode StartSexKey = KeyCode.N;
        /// <summary>T = enable + place next stamp; Shift+T = disable.</summary>
        internal const KeyCode TattooKey = KeyCode.T;
        /// <summary>Restore bust size to H-enter / G-swap baseline.</summary>
        internal const KeyCode BustRestoreKey = KeyCode.B;
        /// <summary>Force PregnancyPlus belly reset (HS2 H-scene inflation + story size).</summary>
        internal const KeyCode BellyResetKey = KeyCode.R;

        /// <summary>熱鍵圖例（說清楚、不自創縮詞）。</summary>
        internal const string HudLegend = "G換女角·H換套裝·J亂數穿著·K換鏡頭·L換姿勢·N往前推·C停／恢復環視·T刺青·Shift+T關刺青·B胸回復";

        /// <summary>PregnancyPlus Live Inflation Shortcuts (KK_PregnancyPlus.cfg); Y/U step scaled 5×; R also forced by this plugin.</summary>
        internal const string PregnancyHudLegend = "Y肚子+·U肚子-·R清空肚子";

        /// <summary>Orgasm FX line prefix for HUD / settings.</summary>
        internal const string OrgasmFxHudPrefix = "高潮";
    }
}
