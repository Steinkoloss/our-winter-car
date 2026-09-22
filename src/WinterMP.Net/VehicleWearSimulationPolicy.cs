using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Driver telemetry supplies inputs; native host calculations own wear.</summary>
    public static class VehicleWearSimulationPolicy
    {
        public static bool Delegated(bool activeHost, bool protectedGuestSave, bool localDriver, byte remoteOwner)
            => activeHost && !protectedGuestSave && !localDriver && remoteOwner != 0 && remoteOwner != byte.MaxValue;

        public static bool HasSample(VehicleState? state, uint vehicleId, byte owner, bool fresh)
            => fresh && owner != 0 && owner != byte.MaxValue && state != null && VehicleStateStreamPolicy.IsValid(state) && state.VehicleId == vehicleId && state.OwnerPlayerId == owner
                && state.Sequence != VehicleState.SnapshotSequence;
    }
}
