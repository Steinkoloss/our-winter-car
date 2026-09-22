using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class FleaSalePolicy
    {
        public static bool Valid(FleaSaleState s) => s != null && s.Flags <= 3 && s.RentDays <= 1006
            && BankTransferPolicy.IsFinite(s.MoneyTotal) && s.MoneyTotal >= 0
            && BankTransferPolicy.IsFinite(s.WeekPrice) && s.WeekPrice > 0 && s.WeekPrice <= 1000000
            && ((s.Flags & 1) != 0 || s.RentDays == 0)
            && ((s.Flags & 2) == 0 || (s.Flags == 2 && s.MoneyTotal > 0));
        public static bool Valid(FleaSaleIntent r) => r != null && r.PlayerId != 255
            && (r.Action == FleaSaleIntent.PayRent ? r.Weeks >= 1 && r.Weeks <= 52
                : r.Action == FleaSaleIntent.CollectProceeds && r.Weeks == 0);
        public static bool Same(FleaSaleState a, FleaSaleState b) => a.MoneyTotal == b.MoneyTotal
            && a.RentDays == b.RentDays && a.Flags == b.Flags && a.WeekPrice == b.WeekPrice;
        public static FleaSaleState Copy(FleaSaleState s) => new FleaSaleState { Sequence = s.Sequence,
            Revision = s.Revision, MoneyTotal = s.MoneyTotal, RentDays = s.RentDays, Flags = s.Flags, WeekPrice = s.WeekPrice };
        public static bool CanApply(FleaSaleState? old, FleaSaleState next)
        {
            if (!Valid(next)) return false;
            if (old == null) return true;
            if (old.Revision == next.Revision) return Same(old, next);
            uint delta = unchecked(next.Revision - old.Revision);
            return delta < 0x80000000u;
        }
    }

    /// <summary>One table revision and one immutable last receipt per admitted player.</summary>
    public sealed class FleaSaleLedger
    {
        private FleaSaleState? _state;
        private sealed class Receipt
        {
            internal uint Revision;
            internal ushort Weeks;
            internal FleaSaleResult Result = new FleaSaleResult();
        }
        private readonly Dictionary<byte, Receipt> _receipts = new Dictionary<byte, Receipt>();
        public FleaSaleState Observe(FleaSaleState native)
        {
            if (!FleaSalePolicy.Valid(native)) throw new ArgumentException("Invalid native flea table.");
            uint revision = _state == null ? 1u : _state.Revision;
            if (_state != null && !FleaSalePolicy.Same(_state, native)) revision = unchecked(revision + 1);
            _state = FleaSalePolicy.Copy(native); _state.Revision = revision;
            return FleaSalePolicy.Copy(_state);
        }
        public bool TryReceipt(FleaSaleIntent r, out FleaSaleResult result)
        {
            result = Reply(r, FleaSaleResult.Stale);
            if (!_receipts.TryGetValue(r.PlayerId, out var old) || old.Result.Sequence != r.Sequence) return false;
            if (old.Result.Action == r.Action && old.Revision == r.Revision && old.Weeks == r.Weeks) result = Copy(old.Result);
            return true;
        }
        public FleaSaleResult Apply(FleaSaleIntent r, float cash, bool nearby, bool available,
            out float nextCash, out FleaSaleState? nextState)
        {
            nextCash = cash; nextState = _state == null ? null : FleaSalePolicy.Copy(_state);
            if (TryReceipt(r, out var receipt)) return receipt;
            if (_receipts.TryGetValue(r.PlayerId, out var old))
            {
                ushort delta = unchecked((ushort)(r.Sequence - old.Result.Sequence));
                if (delta == 0 || delta > short.MaxValue) return Reply(r, FleaSaleResult.Stale);
            }
            byte code;
            bool rent = r.Action == FleaSaleIntent.PayRent;
            float cost = _state == null ? 0 : rent ? r.Weeks * _state.WeekPrice : -_state.MoneyTotal;
            int days = _state == null ? 0 : _state.RentDays + r.Weeks * 7;
            if (!FleaSalePolicy.Valid(r) || _state == null || !available) code = FleaSaleResult.Unavailable;
            else if (r.Revision != _state.Revision) code = FleaSaleResult.Changed;
            else if (rent ? days > 1006 : (_state.Flags & FleaSaleState.FlagCollectable) == 0) code = FleaSaleResult.Unavailable;
            else if (!nearby) code = FleaSaleResult.Distant;
            else if (!BankTransferPolicy.IsFinite(cash) || !BankTransferPolicy.IsFinite(cash - cost)
                || (rent && cash < cost) || Math.Abs((double)cash - (cash - cost) - cost) > .005) code = FleaSaleResult.Funds;
            else
            {
                code = FleaSaleResult.Accepted; nextCash = cash - cost;
                var next = FleaSalePolicy.Copy(_state);
                if (rent) { next.RentDays = (ushort)days; next.Flags = FleaSaleState.FlagRented; }
                else { next.MoneyTotal = 0; next.Flags = (byte)(next.Flags & ~FleaSaleState.FlagCollectable); }
                nextState = Observe(next);
            }
            var result = Reply(r, code);
            if (r.PlayerId != 255) _receipts[r.PlayerId] = new Receipt { Revision = r.Revision, Weeks = r.Weeks, Result = Copy(result) };
            return result;
        }
        public void ForgetPlayer(byte player) { _receipts.Remove(player); }
        public void Clear() { _state = null; _receipts.Clear(); }
        private static FleaSaleResult Reply(FleaSaleIntent r, byte result) => new FleaSaleResult
            { PlayerId = r.PlayerId, Action = r.Action, Sequence = r.Sequence, Result = result };
        private static FleaSaleResult Copy(FleaSaleResult r) => new FleaSaleResult
            { PlayerId = r.PlayerId, Action = r.Action, Sequence = r.Sequence, Result = r.Result };
    }
}
