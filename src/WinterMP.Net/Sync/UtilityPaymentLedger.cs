using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>One utility meter's invoice and bounded per-player payment receipts.</summary>
    public sealed class UtilityPaymentLedger
    {
        private float _debt;
        private bool _visible, _known;
        public uint Revision { get; private set; }
        private readonly Dictionary<byte, Receipt> _receipts = new Dictionary<byte, Receipt>();
        private sealed class Receipt
        {
            internal uint Revision;
            internal UtilityPaymentResult Result = new UtilityPaymentResult();
        }

        public void Observe(float debt, bool visible, bool detailChanged = false)
        {
            if (!BankTransferPolicy.IsFinite(debt) || debt < 0) throw new ArgumentException("Invalid utility invoice.");
            if (_known && _debt == debt && _visible == visible && !detailChanged) return;
            Revision++; _known = true; _debt = debt; _visible = visible;
        }

        public bool TryReceipt(UtilityPaymentIntent r, out UtilityPaymentResult result)
        {
            result = Result(r, UtilityPaymentResult.Stale);
            if (!_receipts.TryGetValue(r.PlayerId, out var old) || old.Result.Sequence != r.Sequence) return false;
            if (old.Revision == r.Revision && old.Result.Meter == r.Meter) result = Copy(old.Result);
            return true;
        }

        public UtilityPaymentResult Apply(UtilityPaymentIntent r, float cash, bool nearby, out float nextCash)
        {
            nextCash = cash;
            if (TryReceipt(r, out var previous)) return previous;
            if (_receipts.TryGetValue(r.PlayerId, out var old))
            {
                ushort delta = (ushort)(r.Sequence - old.Result.Sequence);
                if (delta == 0 || delta > short.MaxValue) return Result(r, UtilityPaymentResult.Stale);
            }
            byte code;
            float paid = 0;
            if (!_known || r.PlayerId == 255 || r.Meter > 3) code = UtilityPaymentResult.Unavailable;
            else if (r.Revision != Revision) code = UtilityPaymentResult.Changed;
            else if (!_visible || _debt <= 0) code = UtilityPaymentResult.Unavailable;
            else if (!nearby) code = UtilityPaymentResult.Distant;
            else if (!BankTransferPolicy.IsFinite(cash) || cash < _debt
                || Math.Abs((double)cash - (cash - _debt) - _debt) > .005) code = UtilityPaymentResult.Funds;
            else
            {
                code = UtilityPaymentResult.Accepted; paid = _debt; nextCash = cash - paid;
                Observe(0, false);
            }
            var result = Result(r, code); result.Paid = paid;
            _receipts[r.PlayerId] = new Receipt { Revision = r.Revision, Result = Copy(result) };
            return result;
        }

        public void ForgetPlayer(byte player) { _receipts.Remove(player); }
        private static UtilityPaymentResult Result(UtilityPaymentIntent r, byte code) => new UtilityPaymentResult
            { PlayerId = r.PlayerId, Meter = r.Meter, Sequence = r.Sequence, Result = code };
        private static UtilityPaymentResult Copy(UtilityPaymentResult r) => new UtilityPaymentResult
            { PlayerId = r.PlayerId, Meter = r.Meter, Sequence = r.Sequence, Result = r.Result, Paid = r.Paid };
    }
}
