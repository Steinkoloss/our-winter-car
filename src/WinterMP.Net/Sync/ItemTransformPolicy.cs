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

        public static float GetRemoteHoldSeconds(bool remoteIsDriver, bool isVehicle) =>
            remoteIsDriver || isVehicle ? DriverRemoteHoldSeconds : ItemRemoteHoldSeconds;

        public static bool IsRemoteStreamLive(float lastRemoteAt, float now, bool remoteIsDriver, bool isVehicle) =>
            now - lastRemoteAt < GetRemoteHoldSeconds(remoteIsDriver, isVehicle);

        /// <summary>
        /// Vehicle pose streams must be reliable — dropped unreliable packets left
        /// cars frozen at their spawn pose until the resting final packet arrived.
        /// </summary>
        public static Channel SelectSendChannel(bool isFinal, bool isVehicle) =>
            isFinal || isVehicle ? Channel.ReliableOrdered : Channel.UnreliableSequenced;

        /// <summary>
        /// When the local player enters the driver's seat, may they take ownership
        /// away from an active remote stream?
        /// </summary>
        public static bool ShouldSeatDriverOutClaimRemote(
            bool remoteStreamLive,
            bool remoteIsDriver,
            bool remoteIsVehicle,
            float lastRemoteAt,
            float now,
            byte localPlayerId,
            byte remoteOwnerId)
        {
            // Only a live remote *driver* blocks a local driver from claiming.
            // Proximity push streams (FlagVehicle without FlagDriver) must not
            // trap the person actually in the seat.
            if (remoteStreamLive && remoteIsDriver)
                return false;

            if (remoteIsDriver && remoteOwnerId != NoRemoteOwner)
                return localPlayerId < remoteOwnerId;

            return true;
        }

        // NOTE: a vehicle proximity-claim policy used to live here, but it was provably
        // unreachable as written: ItemWorldSync.UpdateItems stale-clears the remote owner
        // (and a live stream short-circuits earlier via the remote-driven branch) before
        // CanClaim runs, so the policy only ever saw NoOwner and always allowed the claim.
        // Convergence for competing proximity claims is enforced on the RECEIVE side by
        // RemoteClaimWinsOverLocal (lower playerId wins; host is id 0). Removed rather than
        // left as dead code. Re-add a send-side policy only if the claim path is ever reached
        // with a live remote owner.

        /// <summary>
        /// Resolves conflicting local vs remote ownership on one machine.
        /// </summary>
        public static bool RemoteClaimWinsOverLocal(
            bool localIsDriver,
            bool remoteIsDriver,
            byte localPlayerId,
            byte remoteOwnerId)
        {
            if (remoteIsDriver != localIsDriver)
                return remoteIsDriver;

            return remoteOwnerId < localPlayerId;
        }

        /// <summary>Wrap-aware stale unreliable packet check for one owner.</summary>
        public static bool IsStaleSequence(ushort lastSequence, ushort newSequence)
        {
            ushort diff = (ushort)(newSequence - lastSequence);
            return diff == 0 || diff > short.MaxValue;
        }

        /// <summary>Loose items inside an actively driven vehicle are not proximity-claimable.</summary>
        public static bool ShouldBlockClaimForVehicleCargo(bool isVehicle, bool insideActivelyDrivenVehicle) =>
            !isVehicle && insideActivelyDrivenVehicle;

        /// <summary>
        /// Per-item streams are ignored while cargo rides a moving vehicle; only the
        /// vehicle transform matters until the car is parked.
        /// </summary>
        public static bool ShouldIgnoreRemoteItemTransformForVehicleCargo(
            bool isVehicle,
            bool insideActivelyDrivenVehicle) =>
            !isVehicle && insideActivelyDrivenVehicle;
    }
}
