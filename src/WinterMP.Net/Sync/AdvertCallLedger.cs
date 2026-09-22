using System.Collections.Generic;
namespace WinterMP.Net.Sync
{
    /// <summary>One native enrolment, reserved until a complete call or cancellation.</summary>
    public sealed class AdvertCallLedger
    {
        public const float Duration = 72f, Lease = 8f;
        private readonly Dictionary<byte, uint> _last = new Dictionary<byte, uint>();
        public uint Call { get; private set; }
        public byte Player { get; private set; }
        public byte Phone { get; private set; }
        private float _began, _heard;
        public bool Active => Call != 0;
        public bool Matches(byte player, byte phone, uint call) => Active && Player == player && Phone == phone && Call == call;
        public bool Begin(byte player, byte phone, uint call, float now, bool eligible)
        {
            if (player == 255 || phone > 2 || call == 0 || !Time(now)
                || _last.TryGetValue(player, out uint last) && !AdvertPolicy.Newer(call, last)) return false;
            _last[player] = call;
            if (Active || !eligible) return false;
            Player = player; Phone = phone; Call = call; _began = _heard = now; return true;
        }
        public bool Keep(byte player, byte phone, uint call, float now)
        {
            if (!Matches(player, phone, call) || !Live(now)) return false;
            _heard = now; return true;
        }
        public bool Live(float now) => Time(now) && now >= _heard && now - _heard <= Lease && now - _began <= Duration + Lease;
        public bool Finishing(float now) => Active && Live(now) && now - _began >= Duration - 1;
        public bool Complete(byte player, byte phone, uint call, float now)
        {
            if (!Matches(player, phone, call) || !Live(now) || now - _began < Duration) return false;
            Cancel(); return true;
        }
        public void Cancel() { Call = 0; }
        public void Forget(byte player) { if (Active && Player == player) Cancel(); _last.Remove(player); }
        public void Clear() { Cancel(); _last.Clear(); }
        private static bool Time(float v) => v >= 0 && v < float.PositiveInfinity;
    }
}
