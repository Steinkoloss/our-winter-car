using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class TaxiMeterPolicy
    {
        public static bool Valid(TaxiMeterState s)
        {
            if (s == null || (s.Flags & ~511) != 0 || s.Mode > 6 || s.Values == null || s.Values.Length != TaxiMeterState.ValueCount
                || !Text(s.Display) || !Text(s.ModeDisplay) || !Rotation(s.KnobRotation)) return false;
            foreach (float value in s.Values) if (!(value >= 0 && value <= 10000000)) return false;
            return true;
        }
        private static bool Rotation(NetQuaternion q)
        {
            float norm = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            return norm >= .9f && norm <= 1.1f;
        }
        private static bool Text(string s) => s != null && s.Length <= 128 && s.IndexOf('\0') < 0;
        public static bool CanAct(TaxiMeterState s, TaxiMeterIntent intent, byte actor, bool near, bool busy)
        {
            if (!Valid(s) || intent == null || actor == byte.MaxValue || intent.PlayerId != actor || intent.Sequence == 0
                || intent.ExpectedControlRevision != s.ControlRevision || !near || busy || (s.Flags & TaxiMeterState.Available) == 0) return false;
            switch (intent.Action)
            {
                case TaxiMeterAction.IncreaseMode: return s.Mode < 5;
                case TaxiMeterAction.DecreaseMode: return s.Mode > 0;
                case TaxiMeterAction.ToggleLight:
                case TaxiMeterAction.ResetTotals: return s.Mode < 6;
                default: return false;
            }
        }
        public static float DelegatedSpeed(VehicleState? state, uint vehicle, byte owner, bool fresh) =>
            VehicleWearSimulationPolicy.HasSample(state, vehicle, owner, fresh) ? state!.SpeedTenthsKmh / 36f : 0;
    }

    /// <summary>Consumed requests cannot repeat a native increment, toggle or destructive reset.</summary>
    public sealed class TaxiMeterIntentOrder
    {
        private readonly Dictionary<byte, uint> _last = new Dictionary<byte, uint>();
        public bool Accept(byte actor, uint sequence)
        {
            if (actor == byte.MaxValue || sequence == 0 || (_last.TryGetValue(actor, out uint last) && !TaxiServicePolicy.Newer(last, sequence))) return false;
            _last[actor] = sequence; return true;
        }
        public void Forget(byte actor) { _last.Remove(actor); }
    }
}
