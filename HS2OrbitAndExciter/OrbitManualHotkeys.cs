using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>Single-key manual actions in H scene.</summary>
    internal static class OrbitManualHotkeys
    {
        internal const KeyCode CharaKey = KeyCode.G;
        internal const KeyCode CoordinateKey = KeyCode.H;
        internal const KeyCode WearKey = KeyCode.J;
        internal const KeyCode PoseCameraKey = KeyCode.K;
        internal const KeyCode PoseKey = KeyCode.L;

        /// <summary>YUIOP 列：I＝強制清空肚子（PregnancyPlus）；R 還給原版相機 Reset。</summary>
        internal const KeyCode BellyResetKey = KeyCode.I;
        /// <summary>YUIOP 列：O＝停止／恢復環視轉動（不關協助；Ctrl+Shift+O 仍為開／關協助）。</summary>
        internal const KeyCode StopOrbitCameraKey = KeyCode.O;
        /// <summary>YUIOP 列：P＝切換狀態面板（Ctrl+Shift+I 仍可用）。</summary>
        internal const KeyCode StatusHudKey = KeyCode.P;

        /// <summary>Force leave Idle/AfterIdle — 語意改為「往前推」（依格：開幹／加速／選池）。</summary>
        internal const KeyCode StartSexKey = KeyCode.N;
        /// <summary>T = enable + place next stamp; Shift+T = disable.</summary>
        internal const KeyCode TattooKey = KeyCode.T;
        /// <summary>Restore bust size to H-enter / G-swap baseline.</summary>
        internal const KeyCode BustRestoreKey = KeyCode.B;

        /// <summary>熱鍵圖例（說清楚、不自創縮詞）。</summary>
        internal const string HudLegend =
            "G換女角·H換套裝·J亂數穿著·K換鏡頭·L換姿勢·N往前推·T刺青·Shift+T關刺青·B胸回復";

        /// <summary>YUIOP 列：Y／U 由 Preg+；I／O／P 本外掛。</summary>
        internal const string PregnancyHudLegend =
            "Y肚子+·U肚子-·I清空肚子·O停／恢復環視·P狀態面板";

        /// <summary>Orgasm FX line prefix for HUD / settings.</summary>
        internal const string OrgasmFxHudPrefix = "高潮";
    }
}
