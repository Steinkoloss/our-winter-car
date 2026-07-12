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

        // --- Vehicle cargo streaming (protocol v27) ---

        /// <summary>How long a received cargo pin stays fresh with no new cargo packet.
        /// While fresh the pin owns the item; past it (with the vehicle stream still
        /// live) the item holds its last local pose — a lost packet must not drop
        /// cargo to physics inside a moving car.</summary>
        public const float CargoRemoteHoldSeconds = 1f;

        /// <summary>
        /// Cargo membership hysteresis around the vehicle anchor: items closer than
        /// the enter radius join the set, members stay until the exit radius. The
        /// enter radius also acts as a physics shield — items the car merely drives
        /// past get streamed at their true (resting) pose, so an observer's kinematic
        /// copy of the car cannot punt them.
        /// </summary>
        public const float CargoEnterRadius = 5f;
        public const float CargoExitRadius = 7f;

        public static bool ShouldBeCargoMember(bool wasMember, float distanceSqr)
        {
            float radius = wasMember ? CargoExitRadius : CargoEnterRadius;
            return distanceSqr <= radius * radius;
        }

        public static bool IsCargoStreamFresh(float lastCargoAt, float now) =>
            now - lastCargoAt < CargoRemoteHoldSeconds;

        /// <summary>
        /// Release seeding for a dropped cargo pin. The observer samples the pin's own
        /// composed world motion each frame; on release that sample — not the car's
        /// velocity — seeds the item's physics, so an item the car merely drove past
        /// (pinned at its resting pose by the enter-radius shield) stays at rest instead
        /// of launching down the road at car speed, while a genuine rider still inherits
        /// ~the car's motion. The car's velocity remains the fallback when no fresh
        /// sample exists, and the cap keeps a compose hiccup from flinging the item.
        /// </summary>
        public const float CargoObservedVelocityMaxAge = 0.5f;
        public const float CargoReleaseSeedMaxSpeed = 30f;

        public static bool IsObservedCargoVelocityFresh(float observedAt, float now) =>
            now - observedAt < CargoObservedVelocityMaxAge;

        /// <summary>
        /// A world-space item stream may override a live cargo pin only when it comes
        /// from the cargo authority itself — that is the hand-off signal (item flung
        /// out, grabbed, or settled). Anyone else's stream is noise while the vehicle
        /// owner's physics carries the item.
        /// </summary>
        public static bool ShouldAcceptItemTransformOverCargoPin(
            bool cargoFresh, byte cargoOwner, byte messageOwner) =>
            !cargoFresh || messageOwner == cargoOwner;

        /// <summary>Items pinned by a live remote cargo stream are not proximity-claimable —
        /// unless the local player physically holds them (hands beat floor physics).</summary>
        public static bool ShouldBlockClaimForVehicleCargo(
            bool isVehicle, bool remoteCargoFresh, bool heldByLocalPlayer) =>
            !isVehicle && remoteCargoFresh && !heldByLocalPlayer;

        // --- Dead reckoning (protocol v27) ---

        /// <summary>Cap on velocity extrapolation past the last received pose: enough to
        /// bridge packet gaps, short enough that a stalled stream doesn't drive the car
        /// through scenery.</summary>
        public const float MaxExtrapolationSeconds = 0.3f;

        public static float GetExtrapolationSeconds(float lastRemoteAt, float now)
        {
            float age = now - lastRemoteAt;
            if (age < 0f) return 0f;
            return age > MaxExtrapolationSeconds ? MaxExtrapolationSeconds : age;
        }
    }
}
