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

        internal const KeyCode BellyResetKey = KeyCode.I;
        internal const KeyCode StopOrbitCameraKey = KeyCode.O;
        internal const KeyCode StatusHudKey = KeyCode.P;

        internal const KeyCode StartSexKey = KeyCode.N;
        internal const KeyCode TattooKey = KeyCode.T;
        internal const KeyCode BustRestoreKey = KeyCode.B;

        /// <summary>設定畫面最上方：完整熱鍵一覽（使用者第一眼看到）。</summary>
        internal const string SettingsHotkeysBlock =
            "【總開關】\n"
            + "  Ctrl+Shift+O　開／關環視協助\n"
            + "  Ctrl+Shift+P　開／關本設定\n"
            + "  Ctrl+Shift+I　或　P　顯示／隱藏左下狀態\n"
            + "\n"
            + "【環視中（勿同時按 Ctrl／Shift／Alt）】\n"
            + "  O　暫停／繼續相機轉動（協助仍開著）\n"
            + "  Q／W／E　焦點＝頭／胸／骨盆\n"
            + "  Shift+Q／W／E　第二女角焦點\n"
            + "\n"
            + "【流程】\n"
            + "  G　換女角（中性）　 Shift+G　降權目前女角並換下一個\n"
            + "  H　換套裝　 J　亂數穿著\n"
            + "  K　換鏡頭　 L　換姿勢　 N　往前推（開幹／加速／選池）\n"
            + "\n"
            + "【特效／肚子】\n"
            + "  T　貼下一張刺青　 Shift+T　關閉自動刺青\n"
            + "  B　胸部回復基準\n"
            + "  Y／U　肚子＋／－（需 PregnancyPlus）\n"
            + "  I　清空肚子";

        /// <summary>HUD 精簡熱鍵提醒。</summary>
        internal const string HudLegendCompact =
            "G女角 Shift+G降權 H套裝 J穿著 K鏡頭 L姿勢 N推進 | O停轉 P面板";

        internal const string HudLegend =
            "G中性換女角·Shift+G降權換女角·H換套裝·J亂數穿著·K換鏡頭·L換姿勢·N往前推·T刺青·Shift+T關刺青·B胸回復";

        internal const string PregnancyHudLegend =
            "Y肚子+·U肚子-·I清空·O停轉·P面板";

        internal const string OrgasmFxHudPrefix = "特效";
    }
}
