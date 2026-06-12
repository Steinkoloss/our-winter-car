using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>
    /// Pure ownership / transport rules for <see cref="ItemTransform"/> streams.
    /// Kept in WinterMP.Net so unit tests can lock the behaviour without Unity.
    /// </summary>
    public static class ItemTransformPolicy
    {
        public const float ItemRemoteHoldSeconds = 0.75f;
        public const float DriverRemoteHoldSeconds = 3.5f;
        public const byte NoRemoteOwner = byte.MaxValue;

        public static float GetRemoteHoldSeconds(bool remoteIsDriver) =>
            remoteIsDriver ? DriverRemoteHoldSeconds : ItemRemoteHoldSeconds;

        public static bool IsRemoteStreamLive(float lastRemoteAt, float now, bool remoteIsDriver) =>
            now - lastRemoteAt < GetRemoteHoldSeconds(remoteIsDriver);

        public static Channel SelectSendChannel(bool isFinal, bool isDriver) =>
            isFinal || isDriver ? Channel.ReliableOrdered : Channel.UnreliableSequenced;

        /// <summary>
        /// When the local player enters the driver's seat, may they take ownership
        /// away from an active remote stream?
        /// </summary>
        public static bool ShouldSeatDriverOutClaimRemote(
            bool remoteStreamLive,
            bool remoteIsDriver,
            float lastRemoteAt,
            float now,
            byte localPlayerId,
            byte remoteOwnerId)
        {
            if (remoteStreamLive && remoteIsDriver)
                return false;

            if (remoteIsDriver && remoteOwnerId != NoRemoteOwner)
                return localPlayerId < remoteOwnerId;

            return true;
        }

        /// <summary>
        /// May a nearby non-driver proximity-claim this vehicle?
        /// </summary>
        public static bool AllowsVehicleProximityClaim(
            bool remoteIsDriver,
            byte remoteOwnerId,
            float lastRemoteAt,
            float now)
        {
            if (remoteIsDriver && now - lastRemoteAt < DriverRemoteHoldSeconds)
                return false;
            if (remoteOwnerId != NoRemoteOwner
                && now - lastRemoteAt < GetRemoteHoldSeconds(remoteIsDriver))
                return false;
            return true;
        }

        /// <summary>
        /// Resolves conflicting local vs remote ownership on one machine.
        /// </summary>
        public static bool RemoteClaimWinsOverLocal(
            bool localIsDriver,
            bool remoteIsDriver,
            byte localPlayerId,
            byte remoteOwnerId)
        {
            if (remoteIsDriver != localIsDriver) return remoteIsDriver;
            return remoteOwnerId < localPlayerId;
        }

        /// <summary>Wrap-aware stale unreliable packet check for one owner.</summary>
        public static bool IsStaleSequence(ushort lastSequence, ushort newSequence)
        {
            ushort diff = (ushort)(newSequence - lastSequence);
            return diff == 0 || diff > short.MaxValue;
        }
    }
}
