using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class LottoTicketReplica
    {
        private readonly Dictionary<string, LottoTicketState> _states = new Dictionary<string, LottoTicketState>();
        public IEnumerable<LottoTicketState> States => _states.Values;
        public void Clear() { _states.Clear(); }
        public bool Receive(LottoTicketState state)
        {
            if (!Valid(state)) return false;
            if (_states.TryGetValue(state.TicketId, out var old))
            {
                uint delta = unchecked(state.Sequence - old.Sequence);
                if (delta == 0 || delta > int.MaxValue || (old.Retired && !state.Retired)) return false;
                if (old.Round != state.Round || !SameNumbers(old.Numbers, state.Numbers)) return false;
            }
            _states[state.TicketId] = Copy(state);
            return true;
        }
        public static uint ItemId(string ticketId) => StableHash.Fnv1a32("lotto:" + ticketId);
        public static bool Valid(LottoTicketState s)
        {
            if (!LottoTicketLedger.ValidId(s.TicketId) || s.Round < 0 || s.Round == 8888 || s.Numbers == null
                || s.Numbers.Length != 21 || !BankTransferPolicy.IsFinite(s.Winnings) || s.Winnings < -1) return false;
            foreach (byte n in s.Numbers) if (n > 39) return false;
            if (!FinitePosition(s.Position.X) || !FinitePosition(s.Position.Y) || !FinitePosition(s.Position.Z)) return false;
            double norm = (double)s.Rotation.X * s.Rotation.X + (double)s.Rotation.Y * s.Rotation.Y
                + (double)s.Rotation.Z * s.Rotation.Z + (double)s.Rotation.W * s.Rotation.W;
            return norm >= .9 && norm <= 1.1;
        }
        private static bool FinitePosition(float v) => BankTransferPolicy.IsFinite(v) && v >= -100000 && v <= 100000;
        public static bool SameNumbers(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        public static LottoTicketState Copy(LottoTicketState s) => new LottoTicketState
        {
            Sequence = s.Sequence, TicketId = s.TicketId, Round = s.Round, Numbers = (byte[])s.Numbers.Clone(),
            Winnings = s.Winnings, Retired = s.Retired, Position = s.Position, Rotation = s.Rotation,
        };
    }
}
