using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Validated deferred state and one acknowledged, retryable table command.</summary>
    public sealed class VenttiGameReplica
    {
        public VenttiLedgerState? Current { get; private set; }
        public VenttiRequest? Pending { get; private set; }
        private ushort _sequence;
        private int _refreshes;

        public bool Receive(uint tableId, VenttiRules rules, VenttiLedgerState state)
        {
            if (state.TableId != tableId || !IsValid(rules, state)) return false;
            if (Current != null)
            {
                uint delta = unchecked(state.Revision - Current.Revision);
                if (delta == 0 || delta > int.MaxValue) return false;
            }
            Current = Copy(state);
            return true;
        }

        public bool Queue(byte playerId, VenttiAction action)
        {
            if (Current == null || Pending != null || playerId == VenttiLedgerState.NoPlayer
                || !Available(Current, action)) return false;
            Pending = new VenttiRequest
            {
                TableId = Current.TableId, Revision = Current.Revision, PlayerId = playerId,
                Sequence = ++_sequence, Action = action,
            };
            _refreshes = 0;
            return true;
        }

        public bool Acknowledge(VenttiReceipt receipt)
        {
            var pending = Pending;
            if (pending == null || receipt.TableId != pending.TableId || receipt.PlayerId != pending.PlayerId
                || receipt.Sequence != pending.Sequence || (byte)receipt.Status > (byte)VenttiStatus.Finished
                || !BankTransferPolicy.IsFinite(receipt.CashDelta) || receipt.Outcome > VenttiTableState.LoseHouse
                || (receipt.Status != VenttiStatus.Accepted && (receipt.CashDelta != 0 || receipt.Outcome != 0))) return false;
            Pending = null;
            // State precedes the acknowledgment on the ordered channel. Refreshing
            // the command gets a new sequence; an old receipt can never settle it.
            if (receipt.Status == VenttiStatus.Refresh && Current != null && Current.Revision == receipt.Revision
                && _refreshes < 2 && Available(Current, pending.Action))
            {
                _refreshes++;
                Pending = new VenttiRequest
                {
                    TableId = pending.TableId, Revision = Current.Revision, PlayerId = pending.PlayerId,
                    Action = pending.Action, Sequence = ++_sequence,
                };
            }
            return true;
        }

        public static bool Available(VenttiLedgerState state, VenttiAction action)
        {
            if (state.PendingCash != 0 || state.Phase == VenttiPhase.Closed) return false;
            if (state.Phase == VenttiPhase.Resolved) return action == VenttiAction.NextHand;
            if (state.Phase == VenttiPhase.Playing) return action == VenttiAction.Hit || action == VenttiAction.Stand;
            return action == VenttiAction.Increase || action == VenttiAction.Decrease
                || (action == VenttiAction.Hit && (state.Stake > 0 || state.Wager != VenttiWager.Cash));
        }

        public static bool IsValid(VenttiRules rules, VenttiLedgerState s)
        {
            if ((byte)s.Phase > (byte)VenttiPhase.Closed || (byte)s.Wager > (byte)VenttiWager.House
                || s.Outcome > VenttiTableState.LoseHouse || !VenttiRules.Nonnegative(s.Stake)
                || !VenttiRules.Nonnegative(s.BetMaximum) || s.BetMaximum < rules.BetIncrement
                || !VenttiRules.Nonnegative(s.PendingCash) || !BankTransferPolicy.IsFinite(s.OpponentLoss)
                || s.PropertyStage < 0 || s.PropertyStage > 2 || (s.Wager != VenttiWager.Cash && s.Stake != 0)) return false;
            var used = new bool[53];
            if (!Hand(rules, s.PlayerCards, s.PlayerTotal, used) || !Hand(rules, s.HouseCards, s.HouseTotal, used)) return false;
            if (s.Phase == VenttiPhase.Betting)
                return s.Outcome == 0 && s.PlayerCards.Length == 0 && s.HouseCards.Length == 0
                    && (s.Wager != VenttiWager.Car || s.PropertyStage == 0)
                    && (s.Wager != VenttiWager.House || s.PropertyStage == 1);
            if (s.PlayerCards.Length < 2 || s.HouseCards.Length < 1) return false;
            if (s.Wager == VenttiWager.Cash && s.Stake <= 0) return false;
            int playerBeforeLast = s.PlayerTotal - rules.CardValue(s.PlayerCards[s.PlayerCards.Length - 1]);
            if (playerBeforeLast >= 21) return false;
            if (s.Phase == VenttiPhase.Playing)
                return s.Outcome == 0 && s.PlayerTotal < 21 && s.HouseCards.Length == 1 && s.PendingCash == 0
                    && (s.Wager != VenttiWager.Car || s.PropertyStage == 0)
                    && (s.Wager != VenttiWager.House || s.PropertyStage == 1);
            bool won = s.Outcome == VenttiTableState.Win || s.Outcome == VenttiTableState.WinCar || s.Outcome == VenttiTableState.WinHouse;
            byte expected = s.Wager == VenttiWager.Cash ? (won ? VenttiTableState.Win : VenttiTableState.Lose)
                : s.Wager == VenttiWager.Car ? (won ? VenttiTableState.WinCar : VenttiTableState.LoseCar)
                : (won ? VenttiTableState.WinHouse : VenttiTableState.LoseHouse);
            if (s.Outcome != expected || (s.Wager == VenttiWager.Car && s.PropertyStage != 1)
                || (s.Wager == VenttiWager.House && s.PropertyStage != 2)) return false;
            if (s.PlayerTotal >= 21)
            {
                if (s.HouseCards.Length != 1 || won != (s.PlayerTotal == 21)) return false;
            }
            else
            {
                if (s.HouseCards.Length < 2 || won != (s.HouseTotal > 21)
                    || (s.HouseTotal < 21 && s.HouseTotal <= s.PlayerTotal)) return false;
                int houseBeforeLast = s.HouseTotal - rules.CardValue(s.HouseCards[s.HouseCards.Length - 1]);
                if (s.HouseCards.Length > 2 && (houseBeforeLast >= 21 || houseBeforeLast > s.PlayerTotal)) return false;
            }
            if (s.PendingCash != 0 && (s.Outcome != VenttiTableState.Win || s.PendingCash != s.Stake * 2)) return false;
            return (s.Phase == VenttiPhase.Closed) == (s.Outcome == VenttiTableState.WinHouse || s.OpponentLoss >= rules.OpponentLossLimit);
        }

        private static bool Hand(VenttiRules rules, byte[] cards, int total, bool[] used)
        {
            if (cards == null || cards.Length > 21 || total < 0 || total > 33) return false;
            int sum = 0;
            foreach (byte card in cards)
            {
                if (card < 1 || card > 52 || used[card]) return false;
                used[card] = true; sum += rules.CardValue(card);
            }
            return sum == total;
        }

        public static VenttiLedgerState Copy(VenttiLedgerState s) => new VenttiLedgerState
        {
            TableId = s.TableId, Revision = s.Revision, Round = s.Round, PlayerId = s.PlayerId,
            Outcome = s.Outcome, Phase = s.Phase, Wager = s.Wager, Stake = s.Stake,
            BetMaximum = s.BetMaximum, OpponentLoss = s.OpponentLoss, PendingCash = s.PendingCash,
            PropertyStage = s.PropertyStage, PlayerTotal = s.PlayerTotal, HouseTotal = s.HouseTotal,
            PlayerCards = (byte[])s.PlayerCards.Clone(), HouseCards = (byte[])s.HouseCards.Clone(),
        };

        public void Clear() { Current = null; Pending = null; _sequence = 0; _refreshes = 0; }
    }
}
