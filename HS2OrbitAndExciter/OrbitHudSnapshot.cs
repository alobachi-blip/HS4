namespace HS2OrbitAndExciter
{
    /// <summary>Read-only orbit HUD data; refreshed at end of <see cref="OrbitController.LateUpdate"/>.</summary>
    internal readonly struct OrbitHudSnapshot
    {
        /// <summary>-1 = clothes off; -2 = clothes on with N=0 (advance once per round-trip).</summary>
        internal const int ClothesHintNextRoundTrip = -2;

        internal OrbitHudSnapshot(
            bool waitingPrep,
            float prepRemainSeconds,
            int phase,
            float timeToCompleteCurrentRotation,
            float timeToCompleteCurrentRoundTrip,
            int rotationsUntilRandom,
            int rotationsUntilClothes,
            int roundTripsUntilPose,
            string suppressReasonKey,
            bool isFaintness,
            float singleRotationSeconds,
            float roundTripSeconds,
            bool cameraPaused)
        {
            WaitingPrep = waitingPrep;
            PrepRemainSeconds = prepRemainSeconds;
            Phase = phase;
            TimeToCompleteCurrentRotation = timeToCompleteCurrentRotation;
            TimeToCompleteCurrentRoundTrip = timeToCompleteCurrentRoundTrip;
            RotationsUntilRandom = rotationsUntilRandom;
            RotationsUntilClothes = rotationsUntilClothes;
            RoundTripsUntilPose = roundTripsUntilPose;
            SuppressReasonKey = suppressReasonKey;
            IsFaintness = isFaintness;
            SingleRotationSeconds = singleRotationSeconds;
            RoundTripSeconds = roundTripSeconds;
            CameraPaused = cameraPaused;
        }

        internal bool WaitingPrep { get; }
        internal float PrepRemainSeconds { get; }
        /// <summary>0 = outbound 0→360°, 1 = inbound (reverse).</summary>
        internal int Phase { get; }
        internal float TimeToCompleteCurrentRotation { get; }
        internal float TimeToCompleteCurrentRoundTrip { get; }
        internal int RotationsUntilRandom { get; }
        internal int RotationsUntilClothes { get; }
        internal int RoundTripsUntilPose { get; }
        internal string SuppressReasonKey { get; }
        internal bool IsFaintness { get; }
        internal float SingleRotationSeconds { get; }
        internal float RoundTripSeconds { get; }
        internal bool CameraPaused { get; }
    }
}
