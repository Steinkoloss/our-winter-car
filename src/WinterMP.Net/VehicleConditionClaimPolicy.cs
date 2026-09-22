using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Saved-part inputs retained from an accepted condition when a guest claims physics.</summary>
    public static class VehicleConditionClaimPolicy
    {
        private static bool Valid(VehicleCondition? state) => VehicleConditionStreamPolicy.IsValid(state)
            && (state!.Sequence != VehicleCondition.SnapshotSequence || state.OwnerPlayerId == 0);

        public static VehicleCondition? Capture(VehicleCondition? accepted, VehicleCondition? parked, uint vehicleId, byte previousOwner)
        {
            if (Valid(accepted) && VehicleConditionStreamPolicy.CanPresent(accepted, vehicleId, false, previousOwner))
                return VehicleConditionStreamPolicy.Copy(accepted!);
            if (Valid(parked) && VehicleConditionStreamPolicy.CanPresentParked(parked, vehicleId, false, previousOwner))
                return VehicleConditionStreamPolicy.Copy(parked!);
            return null;
        }

        public static bool CanRead(VehicleCondition? input, uint vehicleId, byte claimant, byte localPlayer,
            bool locallyOwned, byte remoteOwner) => Valid(input) && input!.VehicleId == vehicleId
            && localPlayer != 0 && localPlayer != VehicleStateStreamPolicy.NoOwner && claimant == localPlayer
            && locallyOwned && remoteOwner == VehicleStateStreamPolicy.NoOwner;

        public static bool TryGetHealth(VehicleCondition? input, int wheel, out byte health)
        {
            health = 0;
            if (!Valid(input) || wheel < 0 || wheel > 3 || !input!.HasWheel(wheel)) return false;
            switch (wheel)
            {
                case 0: health = input.HealthFL; break;
                case 1: health = input.HealthFR; break;
                case 2: health = input.HealthRL; break;
                default: health = input.HealthRR; break;
            }
            return true;
        }

        public static bool TryGetDrivetrain(VehicleCondition? input, out byte damage)
        {
            damage = 0;
            if (!Valid(input) || !input!.HasDrivetrain) return false;
            damage = input.DrivetrainDamage;
            return true;
        }

        /// <summary>Reconcile a fresh capture; never change the retained input or make a missing native consumer ready.</summary>
        public static void ApplyToCapture(VehicleCondition capture, VehicleCondition input)
        {
            if (!Valid(input) || capture.VehicleId != input.VehicleId) return;
            // Pressure and discrete native wheel transitions remain delegated.
            // Missing shared saved-part inputs cannot become known local-save values.
            capture.Availability &= (byte)(input.Availability | VehicleCondition.AvailablePressure);
            capture.DrivetrainDamage = capture.HasDrivetrain ? input.DrivetrainDamage : (byte)0;
            capture.HealthFL = capture.HasWheel(0) ? input.HealthFL : (byte)0;
            capture.HealthFR = capture.HasWheel(1) ? input.HealthFR : (byte)0;
            capture.HealthRL = capture.HasWheel(2) ? input.HealthRL : (byte)0;
            capture.HealthRR = capture.HasWheel(3) ? input.HealthRR : (byte)0;
            for (int i = 0; i < 4; i++)
                if (!capture.HasWheel(i)) capture.Flags &= unchecked((byte)~((1 << i) | (16 << i)));
        }
    }
}
