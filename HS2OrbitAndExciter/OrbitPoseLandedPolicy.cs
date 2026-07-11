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

    /// <summary>Next step after a pose successfully lands.</summary>
    internal enum PoseLandedDecision
    {
        None,
        /// <summary>A+B Idle: stay; clear arrival latch; wait L / wheel / N.</summary>
        Appreciate,
        /// <summary>Non-A+B Idle (or N): force WLoop / D_WLoop.</summary>
        AutoStartSex,
        /// <summary>AfterIdle: latched → immediate escape; else ≈2s via TickAfterIdleEscape.</summary>
        TimedEscape,
    }

    /// <summary>
    /// Single owner of "pose landed → what next". Replaces scattered auto_after_kick/pose/rebind/unstick.
    /// </summary>
    internal static class OrbitPoseLandedPolicy
    {
        internal static PoseLandedDecision Decide(HScene? hScene)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
                return PoseLandedDecision.None;

            if (OrbitHelpers.IsFirstFemaleInActionLoop(hScene))
                return PoseLandedDecision.None;

            // Short orgasm AfterIdle — never gate on A+B pose id.
            if (OrbitHelpers.IsFirstFemaleInAfterIdle(hScene))
                return PoseLandedDecision.TimedEscape;

            if (OrbitHelpers.IsFirstFemaleInIdle(hScene))
            {
                if (OrbitHelpers.IsLongAppreciationPose(hScene))
                    return PoseLandedDecision.Appreciate;
                return PoseLandedDecision.AutoStartSex;
            }

            return PoseLandedDecision.None;
        }

        /// <summary>Apply landed policy once for this land event.</summary>
        internal static void OnPoseLanded(HScene? hScene, PoseLandedSource source)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
                return;

            var decision = Decide(hScene);
            string src = SourceTag(source);
            OrbitStateMachineLog.Event("landed", DecisionTag(decision),
                "{\"source\":\"" + src + "\"}");

            switch (decision)
            {
                case PoseLandedDecision.Appreciate:
                    // Arrival latch from L/cycle must not immediately TickIdleEscape out of peeping/masturbation Idle.
                    OrbitBehaviorHub.ClearMotionEscapeLatch();
                    break;

                case PoseLandedDecision.AutoStartSex:
                    OrbitBehaviorHub.TryForceStartSex(hScene, "landed_" + src);
                    break;

                case PoseLandedDecision.TimedEscape:
                    // Latched → leave now; otherwise TickAfterIdleEscape / fake wheel / Harmony own the ≈2s.
                    if (OrbitBehaviorHub.IsMotionEscapeArmed())
                        OrbitBehaviorHub.TickAfterIdleEscape(hScene);
                    break;

                case PoseLandedDecision.None:
                default:
                    break;
            }
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
                PoseLandedDecision.Appreciate => "appreciate",
                PoseLandedDecision.AutoStartSex => "auto_start_sex",
                PoseLandedDecision.TimedEscape => "timed_escape",
                _ => "none",
            };
    }
}
