namespace HS2OrbitAndExciter
{
    /// <summary>Where a pose-land event originated (for FSM log / reason).</summary>
    internal enum PoseLandedSource
    {
        Kick,
        Resolve,
        Rebind,
        Unstick,
    }

    /// <summary>落地後辨認進哪一格（出口交給 §2／§4／§5，不在此換段）。</summary>
    internal enum PoseLandedDecision
    {
        None,
        /// <summary>進閒置 → 排程開幹。</summary>
        EnterIdle,
        /// <summary>進高潮後閒置 → 排程選池。</summary>
        EnterAfterIdle,
        /// <summary>進窺視 → 純播出（等 N／L）。</summary>
        EnterPeeping,
        /// <summary>進動作橋段 → 不動作。</summary>
        EnterBridge,
    }

    /// <summary>
    /// 落地只辨認格子；出口由 <see cref="OrbitFsmFlow"/> 擁有（契約 D1）。
    /// </summary>
    internal static class OrbitPoseLandedPolicy
    {
        internal static PoseLandedDecision Decide(HScene? hScene)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
                return PoseLandedDecision.None;

            var cell = OrbitFsmCellClassifier.Classify(hScene);
            return cell switch
            {
                OrbitFsmCell.Idle => PoseLandedDecision.EnterIdle,
                OrbitFsmCell.AfterIdle => PoseLandedDecision.EnterAfterIdle,
                OrbitFsmCell.Peeping => PoseLandedDecision.EnterPeeping,
                OrbitFsmCell.ActionBridge => PoseLandedDecision.EnterBridge,
                _ => PoseLandedDecision.None,
            };
        }

        internal static void OnPoseLanded(HScene? hScene, PoseLandedSource source)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
                return;

            var decision = Decide(hScene);
            string src = SourceTag(source);
            OrbitStateMachineLog.Event("落地", DecisionTag(decision),
                "{\"source\":\"" + src + "\"}");

            // 愛撫／女女落地縮腹（§19）
            TryDeflateOnCaressOrLesbianLand(hScene);

            switch (decision)
            {
                case PoseLandedDecision.EnterIdle:
                    OrbitFsmFlow.OnEnteredIdle(hScene, "落地_" + src);
                    break;

                case PoseLandedDecision.EnterAfterIdle:
                    OrbitFsmFlow.OnEnteredAfterIdle(hScene, "落地_" + src);
                    break;

                case PoseLandedDecision.EnterPeeping:
                    OrbitFsmFlow.CancelIdleStartSchedule();
                    OrbitFsmFlow.CancelAfterIdleSchedule();
                    HS2OrbitAndExciter.Log?.LogInfo("Orbit: 進入窺視段（純播出；N／L→選池）");
                    break;

                case PoseLandedDecision.EnterBridge:
                case PoseLandedDecision.None:
                default:
                    break;
            }
        }

        private static void TryDeflateOnCaressOrLesbianLand(HScene hScene)
        {
            var info = hScene.ctrlFlag?.nowAnimationInfo;
            if (info == null)
                return;
            int cat = info.ActionCtrl.Item1;
            // 愛撫 Item1==0；女女 Item1==4（契約 §19）
            if (cat != 0 && cat != 4)
                return;
            PregnancyPlusAssist.TryDeflateOneLevel(hScene);
        }

        private static string SourceTag(PoseLandedSource source) =>
            source switch
            {
                PoseLandedSource.Kick => "kick",
                PoseLandedSource.Resolve => "resolve",
                PoseLandedSource.Rebind => "rebind",
                PoseLandedSource.Unstick => "unstick",
                _ => "unknown",
            };

        private static string DecisionTag(PoseLandedDecision decision) =>
            decision switch
            {
                PoseLandedDecision.EnterIdle => "進閒置",
                PoseLandedDecision.EnterAfterIdle => "進高潮後",
                PoseLandedDecision.EnterPeeping => "進窺視",
                PoseLandedDecision.EnterBridge => "進橋段",
                _ => "無",
            };
    }
}
