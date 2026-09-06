using System.Collections;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Validates whole draws and retains the newest one until scene bindings are ready.</summary>
    public sealed class LottoDrawReplica
    {
        public LottoDrawState? Current { get; private set; }

        public bool Receive(LottoDrawState state)
        {
            if (!IsValid(state)) return false;
            var previous = Current;
            if (previous != null)
            {
                uint difference = unchecked(state.Sequence - previous.Sequence);
                if (difference == 0 || difference > int.MaxValue) return false;
            }
            Current = Copy(state);
            return true;
        }

        public void Clear() { Current = null; }

        public static bool IsValid(LottoDrawState state)
        {
            if ((state.Flags & ~LottoDrawState.AllFlags) != 0 || state.Round < 0 || state.TicketRound < 0
                || state.NationalPot < 0 || state.NationalPotMin < 0 || state.NationalPotFull < 0
                || state.Numbers == null || state.Numbers.Length != LottoDrawState.MainCount
                || state.Bonus == null || state.Bonus.Length != LottoDrawState.BonusCount
                || state.Prizes == null || state.Prizes.Length != LottoDrawState.TierCount
                || state.Winners == null || state.Winners.Length != LottoDrawState.TierCount) return false;
            ulong seen = 0;
            if (!ValidNumbers(state.Numbers, ref seen) || !ValidNumbers(state.Bonus, ref seen)) return false;
            for (int i = 0; i < LottoDrawState.TierCount; i++)
                if (state.Prizes[i] < 0 || state.Winners[i] < 0) return false;
            return true;
        }

        private static bool ValidNumbers(byte[] numbers, ref ulong seen)
        {
            int previous = 0;
            foreach (byte number in numbers)
            {
                if (number <= previous || number > LottoDrawState.MaximumNumber || (seen & (1UL << number)) != 0) return false;
                seen |= 1UL << number;
                previous = number;
            }
            return true;
        }

        /// <summary>Copies live native int ArrayLists; never accepts partial lists or coerces wrong element types.</summary>
        public static bool ReadLists(LottoDrawState state, IList numbers, IList bonus, IList prizes, IList winners)
        {
            if (numbers.Count != LottoDrawState.MainCount || bonus.Count != LottoDrawState.BonusCount
                || prizes.Count != LottoDrawState.TierCount || winners.Count != LottoDrawState.TierCount) return false;
            var result = Copy(state);
            for (int i = 0; i < numbers.Count; i++)
            {
                if (!(numbers[i] is int value) || value < 1 || value > LottoDrawState.MaximumNumber) return false;
                result.Numbers[i] = (byte)value;
            }
            for (int i = 0; i < bonus.Count; i++)
            {
                if (!(bonus[i] is int value) || value < 1 || value > LottoDrawState.MaximumNumber) return false;
                result.Bonus[i] = (byte)value;
            }
            for (int i = 0; i < prizes.Count; i++)
            {
                if (!(prizes[i] is int prize) || !(winners[i] is int count)) return false;
                result.Prizes[i] = prize; result.Winners[i] = count;
            }
            if (!IsValid(result)) return false;
            state.Numbers = result.Numbers; state.Bonus = result.Bonus;
            state.Prizes = result.Prizes; state.Winners = result.Winners;
            return true;
        }

        public static LottoDrawState Copy(LottoDrawState state) => new LottoDrawState
        {
            Sequence = state.Sequence, Round = state.Round, TicketRound = state.TicketRound,
            NationalPot = state.NationalPot, NationalPotMin = state.NationalPotMin, NationalPotFull = state.NationalPotFull,
            Numbers = (byte[])state.Numbers.Clone(), Bonus = (byte[])state.Bonus.Clone(),
            Prizes = (int[])state.Prizes.Clone(), Winners = (int[])state.Winners.Clone(), Flags = state.Flags,
        };

        public static bool SameDraw(LottoDrawState a, LottoDrawState b)
        {
            if (a.Round != b.Round || a.TicketRound != b.TicketRound || a.NationalPot != b.NationalPot
                || a.NationalPotMin != b.NationalPotMin || a.NationalPotFull != b.NationalPotFull || a.Flags != b.Flags) return false;
            for (int i = 0; i < LottoDrawState.MainCount; i++) if (a.Numbers[i] != b.Numbers[i]) return false;
            for (int i = 0; i < LottoDrawState.BonusCount; i++) if (a.Bonus[i] != b.Bonus[i]) return false;
            for (int i = 0; i < LottoDrawState.TierCount; i++)
                if (a.Prizes[i] != b.Prizes[i] || a.Winners[i] != b.Winners[i]) return false;
            return true;
        }
    }
}
