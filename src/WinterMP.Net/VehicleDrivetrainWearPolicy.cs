using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    public static class VehicleDrivetrainWearPolicy
    {
        // This is a host validation budget, not a claim about vanilla physics.
        public const float MaximumLossPerCycle = 1f;

        public static bool TryInput(VehicleState? state, uint vehicleId, byte owner, bool fresh,
            float[] divisors, float[] wear, out float speed)
        {
            speed = 0;
            if (!VehicleWearSimulationPolicy.HasSample(state, vehicleId, owner, fresh)
                || !state!.DifferentialSpeedAvailable || divisors == null || wear == null || divisors.Length != 3 || wear.Length != 3) return false;
            float value = Math.Abs(state.DifferentialSpeed);
            for (int i = 0; i < 3; i++)
            {
                float divisor = divisors[i];
                if (!Finite(divisor) || divisor <= 0 || !Finite(wear[i])) return false;
                float loss = value / divisor;
                if (!Finite(loss) || loss > MaximumLossPerCycle || !Finite(wear[i] - loss)) return false;
            }
            speed = value; return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
