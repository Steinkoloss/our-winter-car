using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>
    /// Distance-based streaming rules for host-driven <see cref="NpcTransform"/> packets.
    /// </summary>
    public static class NpcTransformPolicy
    {
        public const float NearDistanceMeters = 80f;
        public const float MidDistanceMeters = 200f;
        public const float NearRateHz = 8f;
        public const float MidRateHz = 3f;
        public const float FarMovingRateHz = 1f;
        public const float RemoteHoldSeconds = 2f;
        public const float StillSeconds = 2f;

        public static float GetSendRateHz(float distanceMeters)
        {
            if (distanceMeters <= NearDistanceMeters) return NearRateHz;
            if (distanceMeters <= MidDistanceMeters) return MidRateHz;
            return 0f;
        }

        public static float GetEffectiveSendRateHz(float distanceMeters, bool moving)
        {
            float rate = GetSendRateHz(distanceMeters);
            return rate > 0f ? rate : moving ? FarMovingRateHz : 0f;
        }

        public static bool ShouldStream(float distanceMeters, bool moving) =>
            GetEffectiveSendRateHz(distanceMeters, moving) > 0f;

        public static bool IsRemoteStreamLive(float lastRemoteAt, float now) =>
            now - lastRemoteAt < RemoteHoldSeconds;

        public static Channel SelectSendChannel(bool isFinal) =>
            isFinal ? Channel.ReliableOrdered : Channel.UnreliableSequenced;

        public static bool IsStaleSequence(ushort lastSequence, ushort newSequence) =>
            ItemTransformPolicy.IsStaleSequence(lastSequence, newSequence);
    }
}
