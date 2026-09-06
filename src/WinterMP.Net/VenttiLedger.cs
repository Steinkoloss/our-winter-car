using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    public enum VenttiAction : byte { Increase, Decrease, Hit, Stand, NextHand }
    public enum VenttiPhase : byte { Betting, Playing, Resolved, Closed }
    public enum VenttiWager : byte { Cash, Car, House }
    public enum VenttiStatus : byte { Accepted, Busy, Funds, Invalid, Distant, Refresh, Stale, Finished }

    /// <summary>
    /// Host Ventti escrow, private deck and exactly-once settlements. This engine has
    /// no Unity callbacks; Core applies cash and native effects before acknowledging commands.
    /// </summary>
    public sealed class VenttiLedger
    {
        public const float BettingLeaseSeconds = 15f, PlayingLeaseSeconds = 300f;
        private readonly VenttiRules _rules;
        private readonly Func<int, int> _random;
        private readonly Dictionary<byte, SavedReceipt> _receipts = new Dictionary<byte, SavedReceipt>();
        private VenttiLedgerState _state;
        private byte[] _deck = new byte[0];
        private int _cursor;
        private float _lastUse;
        private bool _finished;

        private sealed class SavedReceipt
        {
            public VenttiAction Action;
            public uint RequestRevision;
            public VenttiReceipt Receipt = new VenttiReceipt();
        }

        public VenttiLedger(uint tableId, VenttiRules rules, float betMaximum, int propertyStage,
            float opponentLoss, float paidStake, VenttiWager wager, Func<int, int> random)
        {
            if (rules == null || random == null) throw new ArgumentNullException(rules == null ? nameof(rules) : nameof(random));
            if (!VenttiRules.Nonnegative(betMaximum) || betMaximum < rules.BetIncrement
                || !BankTransferPolicy.IsFinite(opponentLoss) || !VenttiRules.Nonnegative(paidStake)
                || propertyStage < 0 || propertyStage > 2 || (byte)wager > (byte)VenttiWager.House
                || (wager != VenttiWager.Cash && paidStake != 0)
                || (wager == VenttiWager.Car && propertyStage != 0) || (wager == VenttiWager.House && propertyStage != 1))
                throw new ArgumentException("Ventti must be adopted between hands with a valid paid stake.");
            _rules = rules; _random = random;
            _state = new VenttiLedgerState
            {
                TableId = tableId, Revision = 1, BetMaximum = betMaximum, PropertyStage = propertyStage,
                OpponentLoss = opponentLoss, Stake = paidStake, Wager = wager,
            };
        }

        public VenttiLedgerState Snapshot() => Copy(_state);

        public VenttiReceipt Apply(VenttiRequest request, float cash, float now, bool nearby, out float nextCash)
        {
            nextCash = cash;
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.TableId != _state.TableId || request.PlayerId == VenttiLedgerState.NoPlayer
                || (byte)request.Action > (byte)VenttiAction.NextHand || !BankTransferPolicy.IsFinite(cash)
                || !VenttiRules.Nonnegative(now)) return Result(request, VenttiStatus.Invalid);
            if (TryGetReceipt(request, out var repeated)) return repeated;
            if (_receipts.TryGetValue(request.PlayerId, out var previous))
            {
                ushort delta = unchecked((ushort)(request.Sequence - previous.Receipt.Sequence));
                if (delta == 0 || delta > short.MaxValue) return Result(request, VenttiStatus.Stale);
            }
            Advance(now);
            var previousPhase = _state.Phase;
            VenttiStatus status;
            if (_finished) status = VenttiStatus.Finished;
            else if (!nearby) status = VenttiStatus.Distant;
            else if (_state.PlayerId != VenttiLedgerState.NoPlayer && _state.PlayerId != request.PlayerId) status = VenttiStatus.Busy;
            else if (request.Revision != _state.Revision) status = VenttiStatus.Refresh;
            else
            {
                var candidate = Copy(_state);
                byte[] deck = _deck;
                int cursor = _cursor;
                status = Perform(candidate, request.Action, cash, ref deck, ref cursor, out nextCash);
                if (status == VenttiStatus.Accepted)
                {
                    candidate.PlayerId = candidate.Phase == VenttiPhase.Closed ? VenttiLedgerState.NoPlayer : request.PlayerId;
                    candidate.Revision++;
                    _state = candidate; _deck = deck; _cursor = cursor; _lastUse = now;
                }
                else nextCash = cash;
            }
            var receipt = Result(request, status);
            receipt.CashDelta = nextCash - cash;
            if (status == VenttiStatus.Accepted && (previousPhase == VenttiPhase.Betting || previousPhase == VenttiPhase.Playing)
                && (_state.Phase == VenttiPhase.Resolved || _state.Phase == VenttiPhase.Closed))
            {
                receipt.Round = _state.Round;
                receipt.Outcome = _state.Outcome;
            }
            _receipts[request.PlayerId] = new SavedReceipt
            {
                Action = request.Action, RequestRevision = request.Revision, Receipt = Copy(receipt),
            };
            return receipt;
        }

        public bool TryGetReceipt(VenttiRequest request, out VenttiReceipt receipt)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            receipt = Result(request, VenttiStatus.Stale);
            if (request.TableId != _state.TableId || !_receipts.TryGetValue(request.PlayerId, out var previous)
                || previous.Receipt.Sequence != request.Sequence) return false;
            if (previous.Action == request.Action && previous.RequestRevision == request.Revision)
                receipt = Copy(previous.Receipt);
            return true;
        }

        private VenttiStatus Perform(VenttiLedgerState state, VenttiAction action, float cash,
            ref byte[] deck, ref int cursor, out float nextCash)
        {
            nextCash = cash;
            if (state.Phase == VenttiPhase.Closed) return VenttiStatus.Finished;
            if (state.PendingCash != 0) return VenttiStatus.Funds;
            if (action == VenttiAction.NextHand)
            {
                if (state.Phase != VenttiPhase.Resolved) return VenttiStatus.Invalid;
                state.Phase = VenttiPhase.Betting; state.Wager = VenttiWager.Cash;
                state.Stake = 0; state.Outcome = VenttiTableState.None;
                state.PlayerCards = state.HouseCards = new byte[0];
                state.PlayerTotal = state.HouseTotal = 0;
                deck = new byte[0]; cursor = 0;
                return VenttiStatus.Accepted;
            }
            if (state.Phase == VenttiPhase.Betting)
            {
                if (action == VenttiAction.Increase || action == VenttiAction.Decrease)
                    return ChangeBet(state, action, cash, out nextCash);
                if (action != VenttiAction.Hit) return VenttiStatus.Invalid;
                if (state.Stake <= 0 && state.Wager == VenttiWager.Cash) return VenttiStatus.Funds;
                // Commit the whole private deck before revealing any card. Later cash
                // retries, disconnects and teardown must never get a fresh random draw.
                deck = Shuffle(); cursor = 0;
                state.Round++; state.Phase = VenttiPhase.Playing;
                Draw(state, true, deck, ref cursor);
                Draw(state, false, deck, ref cursor);
                Draw(state, true, deck, ref cursor);
                if (state.PlayerTotal >= 21) Resolve(state, state.PlayerTotal == 21);
            }
            else if (state.Phase == VenttiPhase.Playing)
            {
                if (action == VenttiAction.Hit)
                {
                    Draw(state, true, deck, ref cursor);
                    if (state.PlayerTotal >= 21) Resolve(state, state.PlayerTotal == 21);
                }
                else if (action == VenttiAction.Stand) Stand(state, deck, ref cursor);
                else return VenttiStatus.Invalid;
            }
            else return VenttiStatus.Invalid;
            PayPending(state, cash, out nextCash);
            return VenttiStatus.Accepted;
        }

        private VenttiStatus ChangeBet(VenttiLedgerState state, VenttiAction action, float cash, out float nextCash)
        {
            nextCash = cash;
            bool propertyCancelled = state.Wager != VenttiWager.Cash;
            state.Wager = VenttiWager.Cash;
            float stake;
            if (action == VenttiAction.Increase)
            {
                if (state.Stake > _rules.PropertyThreshold && state.PropertyStage < 2)
                {
                    if (!TryTransfer(cash, state.Stake, out nextCash)) return VenttiStatus.Funds;
                    // Native progression reaches 4050 mk before swapping it for a
                    // property wager. Refund the actual escrow, never mint a fixed sum.
                    state.Stake = 0;
                    state.Wager = state.PropertyStage == 0 ? VenttiWager.Car : VenttiWager.House;
                    return VenttiStatus.Accepted;
                }
                if (state.Stake >= state.BetMaximum || cash < _rules.BetIncrement)
                    return propertyCancelled ? VenttiStatus.Accepted : VenttiStatus.Funds;
                stake = Add(state.Stake, _rules.BetIncrement);
            }
            else if (state.Stake > 0) stake = Math.Max(0, state.Stake - _rules.BetIncrement);
            else
            {
                // Right-click on zero wraps to the largest affordable stepped bet;
                // native checks its cap after adding each step, so odd caps round up.
                double steps = Math.Min(Math.Ceiling((double)state.BetMaximum / _rules.BetIncrement),
                    Math.Floor((double)cash / _rules.BetIncrement));
                if (steps <= 0) return propertyCancelled ? VenttiStatus.Accepted : VenttiStatus.Funds;
                stake = ExactFloat(steps * _rules.BetIncrement);
            }
            float delta = ExactFloat((double)state.Stake - stake);
            if (!TryTransfer(cash, delta, out nextCash)) return VenttiStatus.Funds;
            state.Stake = stake;
            return VenttiStatus.Accepted;
        }

        private byte[] Shuffle()
        {
            var remaining = new List<byte>(52);
            for (byte card = 1; card <= 52; card++) remaining.Add(card);
            var deck = new byte[52];
            for (int i = 0; i < deck.Length; i++)
            {
                int index = _random(remaining.Count);
                if (index < 0 || index >= remaining.Count) throw new InvalidOperationException("Invalid Ventti random index.");
                deck[i] = remaining[index]; remaining.RemoveAt(index);
            }
            return deck;
        }

        private void Draw(VenttiLedgerState state, bool player, byte[] deck, ref int cursor)
        {
            if (cursor >= deck.Length) throw new InvalidOperationException("Ventti deck exhausted.");
            byte card = deck[cursor++];
            var hand = player ? state.PlayerCards : state.HouseCards;
            var cards = new byte[hand.Length + 1];
            Array.Copy(hand, cards, hand.Length); cards[hand.Length] = card;
            if (player) { state.PlayerCards = cards; state.PlayerTotal += _rules.CardValue(card); }
            else { state.HouseCards = cards; state.HouseTotal += _rules.CardValue(card); }
        }

        private void Stand(VenttiLedgerState state, byte[] deck, ref int cursor)
        {
            // The house always draws its second card on stand, then hits on ties.
            do { Draw(state, false, deck, ref cursor); }
            while (state.HouseTotal < 21 && state.HouseTotal <= state.PlayerTotal);
            Resolve(state, state.HouseTotal > 21);
        }

        private void Resolve(VenttiLedgerState state, bool won)
        {
            if (state.Phase != VenttiPhase.Playing) throw new InvalidOperationException("Ventti hand already resolved.");
            if (state.Wager == VenttiWager.Cash)
            {
                state.Outcome = won ? VenttiTableState.Win : VenttiTableState.Lose;
                state.OpponentLoss = Add(state.OpponentLoss, won ? state.Stake : -state.Stake);
                if (won)
                {
                    state.PendingCash = ExactFloat((double)state.Stake * 2);
                    state.BetMaximum = Add(state.BetMaximum, _rules.WinLimitIncrease);
                }
            }
            else
            {
                state.Outcome = state.Wager == VenttiWager.Car
                    ? (won ? VenttiTableState.WinCar : VenttiTableState.LoseCar)
                    : (won ? VenttiTableState.WinHouse : VenttiTableState.LoseHouse);
                state.PropertyStage++;
                state.OpponentLoss = won ? _rules.PropertyWinLoss : _rules.PropertyLoseLoss;
            }
            state.Phase = state.Outcome == VenttiTableState.WinHouse || state.OpponentLoss >= _rules.OpponentLossLimit
                ? VenttiPhase.Closed : VenttiPhase.Resolved;
        }

        /// <summary>Returns an owed payout/refund exactly once, when float cash can represent it.</summary>
        public bool TryCollect(float cash, out float nextCash)
        {
            if (!PayPending(_state, cash, out nextCash)) return false;
            if (nextCash != cash) _state.Revision++;
            return true;
        }

        private static bool PayPending(VenttiLedgerState state, float cash, out float nextCash)
        {
            if (!TryTransfer(cash, state.PendingCash, out nextCash)) return false;
            state.PendingCash = 0;
            return true;
        }

        public bool ResetResolved()
        {
            if (_finished || _state.Phase != VenttiPhase.Resolved || _state.PendingCash != 0) return false;
            var state = Copy(_state);
            byte[] deck = _deck;
            int cursor = _cursor;
            if (Perform(state, VenttiAction.NextHand, 0, ref deck, ref cursor, out _) != VenttiStatus.Accepted) return false;
            state.Revision++;
            _state = state; _deck = deck; _cursor = cursor;
            return true;
        }

        public bool Advance(float now)
        {
            if (!VenttiRules.Nonnegative(now) || _state.PlayerId == VenttiLedgerState.NoPlayer
                || now - _lastUse < (_state.Phase == VenttiPhase.Playing ? PlayingLeaseSeconds : BettingLeaseSeconds)) return false;
            _state.PlayerId = VenttiLedgerState.NoPlayer; _state.Revision++;
            return true;
        }

        public void ForgetPlayer(byte playerId)
        {
            _receipts.Remove(playerId);
            if (_state.PlayerId != playerId) return;
            _state.PlayerId = VenttiLedgerState.NoPlayer; _state.Revision++;
        }

        /// <summary>Refund an undealt stake or stand on the committed deck; repeat to retry an owed return.</summary>
        public bool FinishSession(float cash, out float nextCash)
        {
            nextCash = cash;
            if (!_finished)
            {
                var candidate = Copy(_state);
                int cursor = _cursor;
                if (candidate.Phase == VenttiPhase.Playing) Stand(candidate, _deck, ref cursor);
                else if (candidate.Phase == VenttiPhase.Betting)
                {
                    candidate.PendingCash = candidate.Stake; candidate.Stake = 0;
                    candidate.Wager = VenttiWager.Cash;
                }
                candidate.PlayerId = VenttiLedgerState.NoPlayer; candidate.Revision++;
                _state = candidate; _cursor = cursor; _finished = true;
            }
            return TryCollect(cash, out nextCash);
        }

        private static bool TryTransfer(float cash, float delta, out float nextCash)
        {
            nextCash = cash;
            float changed = cash + delta;
            if (!BankTransferPolicy.IsFinite(cash) || !BankTransferPolicy.IsFinite(delta)
                || !BankTransferPolicy.IsFinite(changed) || (delta < 0 && changed < 0)
                || (delta != 0 && changed == cash)
                || Math.Abs((double)changed - cash - delta) > 0.005) return false;
            nextCash = changed;
            return true;
        }

        private static float Add(float value, float amount) => ExactFloat((double)value + amount);
        private static float ExactFloat(double value)
        {
            float result = (float)value;
            if (!BankTransferPolicy.IsFinite(result) || Math.Abs(result - value) > 0.005)
                throw new InvalidOperationException("Ventti amount cannot be represented without losing money.");
            return result;
        }

        private VenttiReceipt Result(VenttiRequest request, VenttiStatus status) => new VenttiReceipt
        {
            TableId = request.TableId, PlayerId = request.PlayerId, Sequence = request.Sequence,
            Revision = _state.Revision, Status = status,
        };
        private static VenttiReceipt Copy(VenttiReceipt r) => new VenttiReceipt
        {
            TableId = r.TableId, PlayerId = r.PlayerId, Sequence = r.Sequence,
            Revision = r.Revision, Status = r.Status, CashDelta = r.CashDelta, Round = r.Round, Outcome = r.Outcome,
        };
        private static VenttiLedgerState Copy(VenttiLedgerState s) => new VenttiLedgerState
        {
            TableId = s.TableId, Revision = s.Revision, Round = s.Round, PlayerId = s.PlayerId,
            Outcome = s.Outcome, Phase = s.Phase, Wager = s.Wager, Stake = s.Stake,
            BetMaximum = s.BetMaximum, OpponentLoss = s.OpponentLoss, PendingCash = s.PendingCash,
            PropertyStage = s.PropertyStage, PlayerTotal = s.PlayerTotal, HouseTotal = s.HouseTotal,
            PlayerCards = (byte[])s.PlayerCards.Clone(), HouseCards = (byte[])s.HouseCards.Clone(),
        };
    }
}
