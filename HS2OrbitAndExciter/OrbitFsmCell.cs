using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>契約狀態圖格子（拓樸鎖死）。</summary>
    internal enum OrbitFsmCell
    {
        /// <summary>尚無法辨認（換姿中／無動畫等）。</summary>
        Unknown,
        /// <summary>閒置：Idle／D_Idle；不含 WIdle／SIdle。</summary>
        Idle,
        /// <summary>動作橋段：Insert／各 Loop／WIdle／SIdle 等。</summary>
        ActionBridge,
        /// <summary>高潮後閒置：Orgasm_*_A、Drink_A、Vomit_A 等。</summary>
        AfterIdle,
        /// <summary>窺視段：會進原版 Peeping 的姿勢。</summary>
        Peeping,
    }

    /// <summary>
    /// 狀態名＋是否窺視 → 格。權威見契約 §狀態名→格。
    /// </summary>
    internal static class OrbitFsmCellClassifier
    {
        private static readonly string[] IdleOnlyNames = { "Idle", "D_Idle" };

        private static readonly string[] BridgeNames =
        {
            "Insert", "D_Insert",
            "WLoop", "SLoop", "OLoop", "D_WLoop", "D_SLoop", "D_OLoop",
            "MLoop",
            "WIdle", "SIdle", "WAction", "SAction", "D_Action",
        };

        private static readonly string[] SpecialAfterChainNames =
        {
            "Drink", "Vomit", "D_Drink", "D_Vomit",
            "Drink_A", "Vomit_A", "D_Drink_A", "D_Vomit_A",
        };

        internal static OrbitFsmCell Classify(HScene? hScene)
        {
            if (hScene == null)
                return OrbitFsmCell.Unknown;

            // 窺視優先：姿本身是窺視就不走閒置／橋段合約
            if (OrbitPosePool.IsPeepingPose(hScene.ctrlFlag?.nowAnimationInfo))
                return OrbitFsmCell.Peeping;

            var cha = OrbitHelpers.GetChaFemales(hScene);
            if (cha == null || cha.Length == 0 || cha[0] == null)
                return OrbitFsmCell.Unknown;
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha[0]);
            if (anim == null)
                return OrbitFsmCell.Unknown;

            var state = anim.GetCurrentAnimatorStateInfo(0);

            if (OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
                return OrbitFsmCell.AfterIdle;

            // Drink／Vomit 進場鏈（尚未到 *_A）仍屬高潮後收尾
            foreach (string name in SpecialAfterChainNames)
            {
                if (state.IsName(name))
                    return OrbitFsmCell.AfterIdle;
            }

            foreach (string name in IdleOnlyNames)
            {
                if (state.IsName(name))
                    return OrbitFsmCell.Idle;
            }

            foreach (string name in BridgeNames)
            {
                if (state.IsName(name))
                    return OrbitFsmCell.ActionBridge;
            }

            if (OrbitHelpers.IsFirstFemaleInActionLoop(hScene))
                return OrbitFsmCell.ActionBridge;

            return OrbitFsmCell.Unknown;
        }

        /// <summary>特殊收尾（Drink／Vomit 鏈）：自動路徑要播完才選池；手動可砍。</summary>
        internal static bool IsSpecialAfterChain(HScene? hScene)
        {
            if (hScene == null)
                return false;
            var cha = OrbitHelpers.GetChaFemales(hScene);
            if (cha == null || cha.Length == 0 || cha[0] == null)
                return false;
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha[0]);
            if (anim == null)
                return false;
            var state = anim.GetCurrentAnimatorStateInfo(0);
            foreach (string name in SpecialAfterChainNames)
            {
                if (state.IsName(name))
                    return true;
            }
            return false;
        }

        /// <summary>特殊收尾是否已播完（到 *_A 且 normalizedTime≥1，或已離開該鏈）。</summary>
        internal static bool IsSpecialAfterChainFinished(HScene? hScene)
        {
            if (hScene == null || !IsSpecialAfterChain(hScene))
                return true;
            var cha = OrbitHelpers.GetChaFemales(hScene);
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha![0]);
            if (anim == null)
                return true;
            var state = anim.GetCurrentAnimatorStateInfo(0);
            // Drink_A／Vomit_A：播完一輪即可
            if (state.IsName("Drink_A") || state.IsName("Vomit_A")
                || state.IsName("D_Drink_A") || state.IsName("D_Vomit_A"))
                return state.normalizedTime >= 1f;
            // Drink／Vomit 本體尚未進 *_A → 未完
            return false;
        }

        internal static string CellDisplayName(OrbitFsmCell cell) =>
            cell switch
            {
                OrbitFsmCell.Idle => "閒置",
                OrbitFsmCell.ActionBridge => "動作橋段",
                OrbitFsmCell.AfterIdle => "高潮後閒置",
                OrbitFsmCell.Peeping => "窺視",
                _ => "未辨認",
            };
    }
}
