using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Stable parked-vehicle state, independent of the peer's role and stream counters.</summary>
    public static class VehicleChecksum
    {
        public static uint Fold(uint crc, uint vehicleId, byte flags, byte fuel,
            uint damageMask, VehicleCondition? condition)
        {
            crc = StableHash.Combine(crc, vehicleId);
            crc = StableHash.Combine(crc, flags);
            crc = StableHash.Combine(crc, fuel);
            crc = StableHash.Combine(crc, damageMask & VehicleDamage.ConcretePartsMask);
            // Availability distinguishes missing data from known zero. Ignored
            // payload bytes cannot cause a resync for an unavailable field.
            // Do not fold owners, sequences, binding bookkeeping or continuous wear.
            byte available = condition != null ? (byte)(condition.Availability & VehicleCondition.AvailableAll) : (byte)0;
            crc = StableHash.Combine(crc, available);
            crc = StableHash.Combine(crc, condition != null && condition.HasPressure ? condition.TirePressure : 0u);
            crc = StableHash.Combine(crc, condition != null && condition.HasDrivetrain ? condition.DrivetrainDamage : 0u);
            crc = StableHash.Combine(crc, condition != null && condition.HasWheel(0) ? condition.HealthFL : 0u);
            crc = StableHash.Combine(crc, condition != null && condition.HasWheel(1) ? condition.HealthFR : 0u);
            crc = StableHash.Combine(crc, condition != null && condition.HasWheel(2) ? condition.HealthRL : 0u);
            crc = StableHash.Combine(crc, condition != null && condition.HasWheel(3) ? condition.HealthRR : 0u);
            int wheels = available >> 2;
            return StableHash.Combine(crc, condition != null ? (uint)(condition.Flags & (wheels | wheels << 4)) : 0u);
        }
    }
}
