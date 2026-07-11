namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Closed reason strings for <see cref="OrbitBehaviorHub.CanAutoAdvance"/>,
    /// hotkeys, and FSM/HUD. Do not invent ad-hoc strings at call sites.
    /// </summary>
    internal static class OrbitAssistReasons
    {
        public const string None = "none";
        public const string Ok = "ok";

        // Pose / director (observable)
        public const string PosePending = "posePending";
        public const string PoseQueued = "poseQueued";
        public const string Changing = "changing";
        public const string Rebinding = "rebinding";

        // Legacy aliases (migration / log readers) — prefer the names above.
        public const string PoseTransitionLegacy = "poseTransition";
        public const string SelectionListPresentLegacy = "selectionListPresent";

        // Orgasm / UI / grace
        public const string NowOrgasm = "nowOrgasm";
        public const string OrgasmQuiet = "orgasmQuiet";
        public const string OrbitStartGrace = "orbitStartGrace";
        public const string PointerOverUi = "pointerOverUi";
        public const string InputForcus = "inputForcus";
        public const string MouseHolding = "mouseHolding";
        public const string RecentUiClick = "recentUiClick";
        public const string ManualBusy = "manualBusy";

        // Throttle
        public const string AssistInterval = "assistInterval";
        public const string CheckpointInterval = "checkpointInterval";
        public const string CheckpointLegacyCooldown = "checkpointLegacyCooldown";

        // Hotkey / cycle
        public const string SpriteFade = "spriteFade";
        public const string NoHScene = "noHScene";
        public const string NoValidDownPose = "noValidDownPose";
        public const string NoPoseCandidate = "noPoseCandidate";
        public const string CycleQueued = "cycleQueued";
        public const string NowChangeAnim = "NowChangeAnim";
        /// <summary>Bath/toilet/shower long poses (A+B): wait for L / wheel / cycle escape.</summary>
        public const string LongAppreciation = "longAppreciation";

        // Recovery log msg keys
        public const string ClearedFaintnessInvalid = "cleared_faintness_invalid_pose";
        /// <summary>Obsolete: timer-based sel clear removed; kept for log readers / ClearSelection branch.</summary>
        public const string ClearedAfterTimeout = "cleared_after_timeout";
        public const string ClearedNowChangeStuck = "cleared_nowChange_stuck";
        public const string ClearedPoseAlreadyApplied = "cleared_pose_already_applied";
        public const string PoseKickDone = "pose_kick_done";
        public const string PoseKickStart = "pose_kick_start";

        // PoseLandedPolicy log msg keys (id=landed)
        public const string LandedAppreciate = "appreciate";
        public const string LandedAutoStartSex = "auto_start_sex";
        public const string LandedTimedEscape = "timed_escape";
        public const string LandedNone = "none";
    }
}
