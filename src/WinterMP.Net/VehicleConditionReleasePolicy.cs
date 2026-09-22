using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    public static class VehicleConditionReleasePolicy
    {
        public static VehicleConditionReleaseAck? Create(VehicleCondition? accepted, byte previousOwner, ItemTransform release)
        {
            var condition = VehicleConditionStreamPolicy.CaptureReleasedCondition(accepted, previousOwner, release);
            if (condition == null || condition.OwnerPlayerId == 0) return null;
            return new VehicleConditionReleaseAck { ReleaseSequence = release.Sequence, Condition = condition };
        }

        public static bool Matches(VehicleConditionReleaseAck? pending, VehicleConditionReleaseAck? received, byte localPlayer) =>
            pending != null && received != null && pending.Valid && received.Valid
            && pending.ReleaseSequence == received.ReleaseSequence && received.Condition.OwnerPlayerId == localPlayer
            && pending.Condition.OwnerPlayerId == localPlayer && pending.Condition.VehicleId == received.Condition.VehicleId
            && pending.Condition.Sequence == received.Condition.Sequence;
    }
}
