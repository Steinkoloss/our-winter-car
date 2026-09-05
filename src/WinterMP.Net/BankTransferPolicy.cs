using System;
using System.Collections.Generic;

namespace WinterMP.Net
{
    /// <summary>Vanilla ATM denominations and cash/bank conservation, independent of Unity.</summary>
    public static class BankTransferPolicy
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsValidAmount(short amount)
        {
            // DepositTrigger inserts one 100 mk note. Withdrawal options are fixed.
            return amount == 100 || amount == -100 || amount == -200 || amount == -300
                || amount == -500 || amount == -800 || amount == -1000;
        }

        public static bool TryTransfer(float money, float bank, short amount,
            out float nextMoney, out float nextBank)
        {
            nextMoney = money;
            nextBank = bank;
            if (!IsValidAmount(amount) || !IsFinite(money) || !IsFinite(bank)) return false;
            if (amount > 0 ? money < amount : bank < -amount) return false;

            float cashResult = money - amount;
            float bankResult = bank + amount;
            // Vanilla uses floats: crossing a power of two changes the ULP even
            // at ordinary balances (2043.58 -> 2143.58). Allow sub-cent rounding,
            // while rejecting magnitudes where a note would disappear entirely.
            if (!IsFinite(cashResult) || !IsFinite(bankResult)
                || Math.Abs((double)money - cashResult - amount) > 0.005
                || Math.Abs((double)bankResult - bank - amount) > 0.005
                || Math.Abs((double)cashResult + bankResult - money - bank) > 0.005) return false;
            nextMoney = cashResult;
            nextBank = bankResult;
            return true;
        }
    }

    /// <summary>One outstanding request per player; an acknowledged retry never moves money twice.</summary>
    public sealed class BankTransferLedger
    {
        private sealed class Receipt
        {
            public ushort Sequence;
            public bool Accepted;
        }

        private readonly Dictionary<byte, Receipt> _receipts = new Dictionary<byte, Receipt>();

        public bool TryGetReceipt(byte playerId, ushort sequence, out bool accepted)
        {
            accepted = false;
            if (!_receipts.TryGetValue(playerId, out var receipt) || receipt.Sequence != sequence) return false;
            accepted = receipt.Accepted;
            return true;
        }

        public bool IsNew(byte playerId, ushort sequence)
        {
            if (!_receipts.TryGetValue(playerId, out var receipt)) return true;
            ushort delta = (ushort)(sequence - receipt.Sequence);
            return delta != 0 && delta <= short.MaxValue;
        }

        public void Record(byte playerId, ushort sequence, bool accepted)
        {
            _receipts[playerId] = new Receipt { Sequence = sequence, Accepted = accepted };
        }

        public void ForgetPlayer(byte playerId) { _receipts.Remove(playerId); }
        public void Clear() { _receipts.Clear(); }
    }
}
