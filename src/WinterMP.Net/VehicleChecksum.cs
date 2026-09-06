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
            // Missing systems contribute zero on vehicles without those bindings.
            // Do not fold owners, sequences, binding bookkeeping or continuous wear.
            crc = StableHash.Combine(crc, condition != null ? condition.TirePressure : 0u);
            crc = StableHash.Combine(crc, condition != null ? condition.DrivetrainDamage : 0u);
            crc = StableHash.Combine(crc, condition != null ? condition.HealthFL : 0u);
            crc = StableHash.Combine(crc, condition != null ? condition.HealthFR : 0u);
            crc = StableHash.Combine(crc, condition != null ? condition.HealthRL : 0u);
            crc = StableHash.Combine(crc, condition != null ? condition.HealthRR : 0u);
            return StableHash.Combine(crc, condition != null ? condition.Flags : 0u);
        }
    }
}
