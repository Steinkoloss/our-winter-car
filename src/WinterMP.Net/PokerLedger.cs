using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Rami-Pokeri accounting and private decks. No client supplies a card or a payout.</summary>
    public sealed class PokerLedger
    {
        public const int MaximumBalance = 9999, MaximumPendingWin = 998;
        private readonly PokerState _state;
        private readonly Func<int, int> _random;
        private readonly int[] _payouts;
        private readonly Dictionary<byte, Receipt> _receipts = new Dictionary<byte, Receipt>();
        private bool[] _used = new bool[52], _doubleUsed = new bool[52];
        private byte _hiddenDouble;
        private float _lastUse;
        private readonly int _achievementThreshold;

        private sealed class Receipt
        {
            public byte Action;
            public uint Round;
            public PokerResult Result = new PokerResult();
        }

        public PokerLedger(PokerState initial, int[] payouts, int achievementThreshold, Func<int, int> random)
        {
            if (!IsValid(initial) || initial.Phase != PokerState.Ready)
                throw new ArgumentException("Poker must be initialized between hands.");
            if (payouts.Length != 10 || payouts[0] != 0) throw new ArgumentException("Invalid poker payouts.");
            foreach (int payout in payouts)
                if (payout < 0 || payout > 50) throw new ArgumentException("Invalid poker payout.");
            _payouts = (int[])payouts.Clone();
            if (achievementThreshold < 1 || achievementThreshold > MaximumBalance)
                throw new ArgumentException("Invalid poker achievement threshold.");
            _state = Copy(initial); _state.PlayerId = PokerState.NoPlayer; _state.Revision = 1;
            _achievementThreshold = achievementThreshold;
            _random = random;
        }

        public PokerState Snapshot() { return Copy(_state); }

        public bool TryGetReceipt(PokerIntent request, out PokerResult result)
        {
            result = Result(request, PokerResult.Stale);
            if (!_receipts.TryGetValue(request.PlayerId, out var previous)
                || previous.Result.Sequence != request.Sequence) return false;
            if (previous.Action == request.Action && previous.Round == request.Round)
                result = Copy(previous.Result);
            return true;
        }

        public PokerResult Apply(PokerIntent request, float cash, float now, bool nearby, out float nextCash)
        {
            nextCash = cash;
            if (TryGetReceipt(request, out var repeated)) return repeated;
            if (_receipts.TryGetValue(request.PlayerId, out var previous))
            {
                ushort delta = (ushort)(request.Sequence - previous.Result.Sequence);
                if (delta == 0 || delta > short.MaxValue) return Result(request, PokerResult.Stale);
            }
            Advance(now);
            byte outcome;
            int transfer = 0;
            byte achievements = 0;
            if (request.MachineId != _state.MachineId || request.PlayerId == PokerState.NoPlayer
                || request.Action > PokerIntent.TakeWin || request.Round != _state.Round)
                outcome = PokerResult.Invalid;
            else if (!nearby) outcome = PokerResult.Distant;
            else if (_state.PlayerId != PokerState.NoPlayer && _state.PlayerId != request.PlayerId)
                outcome = PokerResult.Busy;
            else outcome = Perform(request.Action, cash, out nextCash, out transfer, out achievements);
            var result = Result(request, outcome);
            result.CashDelta = transfer; result.Achievements = achievements;
            _receipts[request.PlayerId] = new Receipt { Action = request.Action, Round = request.Round, Result = Copy(result) };
            if (outcome == PokerResult.Accepted)
            {
                _state.PlayerId = request.PlayerId; _lastUse = now; _state.Revision++;
            }
            return result;
        }

        private byte Perform(byte action, float cash, out float nextCash, out int transfer, out byte achievements)
        {
            nextCash = cash; transfer = 0; achievements = 0;
            if (action == PokerIntent.Insert)
            {
                // Vanilla floors cash, requires strictly more than the current bet,
                // and checks the 500 mk credit ceiling BEFORE inserting the coin.
                if (_state.Credit >= 500 || Math.Floor(cash) <= _state.Bet
                    || !TryChangeCash(cash, -_state.Bet, out nextCash)) return PokerResult.Funds;
                transfer = -_state.Bet; _state.Credit += _state.Bet;
                return PokerResult.Accepted;
            }
            if (_state.Phase == PokerState.Holding)
            {
                if (action >= PokerIntent.Hold1 && action <= PokerIntent.Hold5)
                    _state.HoldMask ^= (byte)(1 << (action - PokerIntent.Hold1));
                else if (action == PokerIntent.Deal)
                {
                    DrawReplacement();
                    if (_state.Hand == 9) achievements = PokerResult.RoyalAchievement;
                }
                else return PokerResult.Invalid;
                return PokerResult.Accepted;
            }
            if (_state.Phase == PokerState.Guessing)
            {
                if (action != PokerIntent.Low && action != PokerIntent.High) return PokerResult.Invalid;
                int rank = Rank(_hiddenDouble);
                bool correct = action == PokerIntent.Low ? rank <= 6 : rank >= 8;
                _state.DoubleCard = _hiddenDouble; _hiddenDouble = 0;
                _state.PendingWin = correct ? _state.PendingWin * 2 : 0;
                _state.Phase = correct ? PokerState.WinOffer : PokerState.Ready;
                if (_state.PendingWin >= 500) Collect();
                if (!correct) NormalizeBet();
                return PokerResult.Accepted;
            }
            if (_state.Phase == PokerState.WinOffer)
            {
                if (action == PokerIntent.Double)
                {
                    _hiddenDouble = Draw(_doubleUsed);
                    _state.DoubleCard = 0; _state.Phase = PokerState.Guessing;
                }
                else if (action == PokerIntent.TakeWin || action == PokerIntent.Deal) Collect();
                else return PokerResult.Invalid;
                return PokerResult.Accepted;
            }
            switch (action)
            {
                case PokerIntent.Bet:
                    if (_state.Credit + _state.Winnings == 0) return PokerResult.Funds;
                    int nextBet = _state.Bet % 5 + 1;
                    _state.Bet = (byte)(nextBet <= _state.Credit + _state.Winnings ? nextBet : 1);
                    break;
                case PokerIntent.Deal:
                    if (_state.Credit + _state.Winnings < _state.Bet
                        || _state.Winnings > MaximumBalance - MaximumPendingWin) return PokerResult.Funds;
                    var used = new bool[52];
                    var cards = new byte[5];
                    for (int i = 0; i < 5; i++) cards[i] = Draw(used);
                    int fromCredit = Math.Min(_state.Credit, _state.Bet);
                    _state.Credit -= fromCredit; _state.Winnings -= _state.Bet - fromCredit;
                    _state.Cards = cards; _used = used; _doubleUsed = new bool[52];
                    _state.DoubleCard = _state.HoldMask = _state.Hand = 0;
                    _state.Phase = PokerState.Holding; _state.Round++;
                    break;
                case PokerIntent.TakeWin:
                    int amount = _state.Credit + _state.Winnings;
                    if (amount == 0 || !TryChangeCash(cash, amount, out nextCash)) return PokerResult.Funds;
                    transfer = amount; _state.Credit = _state.Winnings = 0;
                    if (amount >= _achievementThreshold) achievements = PokerResult.CashoutAchievement;
                    _state.Bet = 1;
                    break;
                default: return PokerResult.Invalid;
            }
            return PokerResult.Accepted;
        }

        private byte Draw(bool[] used)
        {
            int count = 0;
            foreach (bool taken in used) if (!taken) count++;
            int index = _random(count);
            if (index < 0 || index >= count) throw new InvalidOperationException("Invalid random card index.");
            for (int i = 0; i < 52; i++)
            {
                if (used[i]) continue;
                if (index-- != 0) continue;
                used[i] = true;
                return (byte)(i + 1);
            }
            throw new InvalidOperationException("Poker deck exhausted.");
        }

        private void DrawReplacement()
        {
            var cards = (byte[])_state.Cards.Clone();
            var used = (bool[])_used.Clone();
            for (int i = 0; i < 5; i++) if ((_state.HoldMask & (1 << i)) == 0) cards[i] = Draw(used);
            _state.Cards = cards; _used = used;
            SettleHand();
        }

        private void SettleHand()
        {
            _state.Hand = Evaluate(_state.Cards); _state.PendingWin = _payouts[_state.Hand] * _state.Bet;
            _state.HoldMask = 0;
            _state.Phase = _state.PendingWin > 0 ? PokerState.WinOffer : PokerState.Ready;
            if (_state.PendingWin == 0) NormalizeBet();
        }

        private void Collect()
        {
            _state.Winnings += _state.PendingWin; _state.PendingWin = 0;
            _state.Phase = PokerState.Ready; NormalizeBet();
        }
        private void NormalizeBet() { if (_state.Bet > _state.Credit + _state.Winnings) _state.Bet = 1; }

        public bool Advance(float now)
        {
            float lease = _state.Phase == PokerState.Ready ? 30f : 300f;
            if (_state.PlayerId == PokerState.NoPlayer || now - _lastUse < lease) return false;
            // An abandoned hand stays on the machine. Neither reconnect nor changing
            // players creates a fresh deck or cancels an already committed double.
            _state.PlayerId = PokerState.NoPlayer; _state.Revision++;
            return true;
        }

        public void ForgetPlayer(byte playerId)
        {
            _receipts.Remove(playerId);
            if (_state.PlayerId != playerId) return;
            _state.PlayerId = PokerState.NoPlayer; _state.Revision++;
        }

        public bool FinishSession(float cash, out float nextCash)
        {
            // No new randomness during teardown: an unsubmitted redraw stands on
            // the existing five cards; a committed double with no guess loses.
            if (_state.Phase == PokerState.Holding) SettleHand();
            if (_state.Phase == PokerState.Guessing) _state.PendingWin = 0;
            _hiddenDouble = 0;
            Collect();
            int amount = _state.Credit + _state.Winnings;
            if (!TryChangeCash(cash, amount, out nextCash)) return false;
            _state.Credit = _state.Winnings = 0; _state.PlayerId = PokerState.NoPlayer; _state.Revision++;
            return true;
        }

        public static int Rank(byte card) { return (card - 1) % 13 + 1; }
        public static int Suit(byte card) { return (card - 1) / 13; }

        public static byte Evaluate(byte[] cards)
        {
            if (!ValidCards(cards, false)) throw new ArgumentException("Invalid poker hand.");
            int[] ranks = new int[14];
            bool flush = true;
            foreach (byte card in cards) { ranks[Rank(card)]++; flush &= Suit(card) == Suit(cards[0]); }
            bool straight = false, royal = ranks[1] == 1 && ranks[10] == 1 && ranks[11] == 1 && ranks[12] == 1 && ranks[13] == 1;
            for (int start = 1; start <= 9; start++)
            {
                bool run = true;
                for (int i = start; i < start + 5; i++) run &= ranks[i] == 1;
                straight |= run;
            }
            straight |= royal;
            int pairs = 0, threes = 0, fours = 0;
            bool highPair = false;
            for (int i = 1; i <= 13; i++)
            {
                if (ranks[i] == 2) { pairs++; highPair |= i == 1 || i >= 11; }
                if (ranks[i] == 3) threes++;
                if (ranks[i] == 4) fours++;
            }
            return (byte)(flush && royal ? 9 : flush && straight ? 8 : fours > 0 ? 7
                : threes > 0 && pairs > 0 ? 6 : flush ? 5 : straight ? 4 : threes > 0 ? 3
                : pairs == 2 ? 2 : highPair ? 1 : 0);
        }

        public static bool IsValid(PokerState s)
        {
            return s.Phase <= PokerState.Guessing && s.Bet >= 1 && s.Bet <= 5 && s.HoldMask <= 31
                && (s.Phase == PokerState.Holding || s.HoldMask == 0) && s.Hand <= 9
                && s.Credit >= 0 && s.Credit <= MaximumBalance && s.Winnings >= 0 && s.Winnings <= MaximumBalance
                && s.PendingWin >= 0 && s.PendingWin <= MaximumPendingWin
                && s.Winnings + s.PendingWin <= MaximumBalance
                && ((s.Phase == PokerState.WinOffer || s.Phase == PokerState.Guessing) == (s.PendingWin > 0))
                && (s.Phase != PokerState.Guessing || s.DoubleCard == 0) && s.DoubleCard <= 52
                && ValidCards(s.Cards, s.Phase == PokerState.Ready);
        }

        private static bool ValidCards(byte[] cards, bool allowEmpty)
        {
            if (cards.Length != 5) return false;
            if (allowEmpty && Array.TrueForAll(cards, c => c == 0)) return true;
            bool[] seen = new bool[52];
            foreach (byte card in cards)
            {
                if (card < 1 || card > 52 || seen[card - 1]) return false;
                seen[card - 1] = true;
            }
            return true;
        }

        private static bool TryChangeCash(float cash, int delta, out float next)
        {
            next = cash;
            float changed = cash + delta;
            if (!BankTransferPolicy.IsFinite(cash) || !BankTransferPolicy.IsFinite(changed)
                || Math.Abs((double)changed - cash - delta) > 0.005) return false;
            next = changed; return true;
        }
        private static PokerResult Result(PokerIntent r, byte result) => new PokerResult
            { MachineId = r.MachineId, PlayerId = r.PlayerId, Sequence = r.Sequence, Result = result };
        private static PokerResult Copy(PokerResult r) => new PokerResult
            { MachineId = r.MachineId, PlayerId = r.PlayerId, Sequence = r.Sequence, Result = r.Result, CashDelta = r.CashDelta, Achievements = r.Achievements };
        private static PokerState Copy(PokerState s) => new PokerState
        {
            MachineId = s.MachineId, Revision = s.Revision, Round = s.Round, PlayerId = s.PlayerId,
            Phase = s.Phase, Bet = s.Bet, HoldMask = s.HoldMask, Hand = s.Hand,
            Credit = s.Credit, Winnings = s.Winnings, PendingWin = s.PendingWin,
            Cards = (byte[])s.Cards.Clone(), DoubleCard = s.DoubleCard,
        };
    }
}
