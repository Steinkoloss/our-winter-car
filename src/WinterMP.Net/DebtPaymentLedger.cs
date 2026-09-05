using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Versioned rent-debt quotes and exactly-once shared-cash settlements.</summary>
    public sealed class DebtPaymentLedger
    {
        private DebtLetterState? _state;
        private float _interest, _cost1, _cost2;
        private readonly Dictionary<byte, Receipt> _receipts = new Dictionary<byte, Receipt>();
        private sealed class Receipt
        {
            public uint Revision;
            public DebtPaymentResult Result = new DebtPaymentResult();
        }

        public static bool TryQuote(float debt, float interest, float cost1, float cost2, out float total)
        {
            total = 0;
            if (!BankTransferPolicy.IsFinite(debt) || debt < 0 || !BankTransferPolicy.IsFinite(interest) || interest <= 0
                || !BankTransferPolicy.IsFinite(cost1) || cost1 < 0 || !BankTransferPolicy.IsFinite(cost2) || cost2 < 0) return false;
            if (debt == 0) return true;
            // Each vanilla action stores back into an FsmFloat; preserve that order.
            total = debt * interest;
            total += cost1;
            total += cost2;
            return BankTransferPolicy.IsFinite(total) && total > 0;
        }

        public bool Observe(float debt, float interest, float cost1, float cost2, bool envelope)
        {
            if (!TryQuote(debt, interest, cost1, cost2, out float total))
                throw new ArgumentException("Invalid native debt-letter amounts.");
            bool available = envelope && debt > 0;
            if (_state != null && _state.Debt == debt && _state.Total == total && _state.Available == available
                && _interest == interest && _cost1 == cost1 && _cost2 == cost2) return false;
            uint revision = _state != null ? _state.Revision + 1 : 1;
            _state = new DebtLetterState { Revision = revision, Debt = debt, Total = total, Available = available };
            _interest = interest; _cost1 = cost1; _cost2 = cost2;
            return true;
        }

        public DebtLetterState? Snapshot() => _state == null ? null : new DebtLetterState
            { Revision = _state.Revision, Debt = _state.Debt, Total = _state.Total, Available = _state.Available };

        public bool TryGetReceipt(DebtPaymentIntent request, out DebtPaymentResult result)
        {
            result = Result(request, DebtPaymentResult.Stale);
            if (!_receipts.TryGetValue(request.PlayerId, out var previous) || previous.Result.Sequence != request.Sequence) return false;
            if (previous.Revision == request.Revision) result = Copy(previous.Result);
            return true;
        }

        public DebtPaymentResult Apply(DebtPaymentIntent request, float cash, bool nearby, out float nextCash)
        {
            nextCash = cash;
            if (TryGetReceipt(request, out var repeated)) return repeated;
            if (_receipts.TryGetValue(request.PlayerId, out var previous))
            {
                ushort delta = (ushort)(request.Sequence - previous.Result.Sequence);
                if (delta == 0 || delta > short.MaxValue) return Result(request, DebtPaymentResult.Stale);
            }
            byte code;
            float paid = 0;
            if (_state == null || request.PlayerId == byte.MaxValue) code = DebtPaymentResult.Unavailable;
            else if (request.Revision != _state.Revision) code = DebtPaymentResult.Changed;
            else if (!_state.Available) code = DebtPaymentResult.Unavailable;
            else if (!nearby) code = DebtPaymentResult.Distant;
            else if (!TryDebit(cash, _state.Total, out nextCash)) code = DebtPaymentResult.Funds;
            else
            {
                code = DebtPaymentResult.Accepted; paid = _state.Total;
                Observe(0, _interest, _cost1, _cost2, false);
            }
            var result = Result(request, code); result.Paid = paid;
            _receipts[request.PlayerId] = new Receipt { Revision = request.Revision, Result = Copy(result) };
            return result;
        }

        private static bool TryDebit(float cash, float total, out float nextCash)
        {
            nextCash = cash;
            if (!BankTransferPolicy.IsFinite(cash) || cash < total) return false;
            float next = cash - total;
            if (!BankTransferPolicy.IsFinite(next) || Math.Abs((double)cash - next - total) > 0.005) return false;
            nextCash = next; return true;
        }
        public static bool IsValid(DebtLetterState state) => BankTransferPolicy.IsFinite(state.Debt) && state.Debt >= 0
            && BankTransferPolicy.IsFinite(state.Total) && state.Total >= 0 && ((state.Debt > 0) == (state.Total > 0))
            && (!state.Available || state.Debt > 0);
        public void ForgetPlayer(byte player) { _receipts.Remove(player); }
        public void Clear() { _receipts.Clear(); _state = null; _interest = _cost1 = _cost2 = 0; }
        private static DebtPaymentResult Result(DebtPaymentIntent r, byte result) => new DebtPaymentResult
            { PlayerId = r.PlayerId, Sequence = r.Sequence, Result = result };
        private static DebtPaymentResult Copy(DebtPaymentResult r) => new DebtPaymentResult
            { PlayerId = r.PlayerId, Sequence = r.Sequence, Result = r.Result, Paid = r.Paid };
    }
}
