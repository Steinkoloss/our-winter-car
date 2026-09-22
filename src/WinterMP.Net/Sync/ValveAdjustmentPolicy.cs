using System.Collections;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class ValveAdjustmentPolicy
    {
        public const int Count = 8;
        public const float Minimum = 2f, Maximum = 8f, Step = .05f, RotationScale = 50f;
        public static bool Valid(float value) => value >= Minimum && value <= Maximum;
        public static bool Valid(ValveAdjustmentState state) => state != null && state.NetId != 0 && Valid(state.Setting);
        public static bool CanTurn(float value, int direction) => Valid(value)
            && ((direction == 1 && value < Maximum) || (direction == -1 && value > Minimum));
        public static bool Read(IList? values, int index, out float value)
        {
            value = 0;
            if (!ArrayShape(values) || index < 0 || index >= Count) return false;
            value = (float)values![index]; return Valid(value);
        }
        public static bool ArrayShape(IList? values)
        {
            if (values == null || values.Count != Count) return false;
            for (int i = 0; i < Count; i++)
                if (!(values[i] is float setting) || float.IsNaN(setting) || float.IsInfinity(setting)) return false;
            return true;
        }
        public static uint MixChecksum(uint crc, ValveAdjustmentState state) =>
            StableHash.Combine(StableHash.Combine(crc, state.NetId), unchecked((uint)System.Math.Round(state.Setting * 1000f)));
    }
}
