using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Host-owned machine accounting, independent of the host's active scene/UI.</summary>
    public sealed class SlotMachineLedger
    {
        public const int MaximumBalance = 1000000;
        private const float LeaseSeconds = 15f, RoundTimeoutSeconds = 10f;
        private readonly SlotMachineState _state;
        private readonly Func<int, int> _random;
        private readonly int[][] _reels;
        private readonly int[] _payouts;
        private readonly Dictionary<byte, Receipt> _receipts = new Dictionary<byte, Receipt>();
        private float _lastUse, _roundStarted, _earliestFinish;

        private sealed class Receipt
        {
            public ushort Sequence;
            public byte Action, Result;
            public uint Round;
            public int CashDelta;
        }

        public SlotMachineLedger(SlotMachineState initial, int[][] reels, int[] payouts, Func<int, int> random)
        {
            if (!IsValid(initial) || initial.Spinning) throw new ArgumentException("Invalid initial slot state.");
            if (reels.Length != 3 || payouts.Length != 10) throw new ArgumentException("Invalid slot rules.");
            _reels = new int[3][];
            for (int i = 0; i < 3; i++)
            {
                if (reels[i].Length == 0) throw new ArgumentException("Empty reel.");
                _reels[i] = (int[])reels[i].Clone();
                foreach (int symbol in _reels[i])
                    if (symbol < 1 || symbol > 9) throw new ArgumentException("Invalid reel symbol.");
            }
            _payouts = (int[])payouts.Clone();
            foreach (int multiplier in _payouts)
                if (multiplier < 0 || multiplier > 1000) throw new ArgumentException("Invalid payout.");
            _state = Copy(initial);
            _state.PlayerId = SlotMachineState.NoPlayer;
            _state.Revision = 1;
            _random = random;
        }

        public static bool IsValid(SlotMachineState state)
        {
            return state.Bet >= 1 && state.Bet <= 5 && state.HoldMask < 7
                && (state.HoldMask == 0 || state.CanHold)
                && (!state.Spinning || state.PlayerId != SlotMachineState.NoPlayer)
                && (!(state.Spinning || state.CanHold) || (state.Reel1 > 0 && state.Reel2 > 0 && state.Reel3 > 0))
                && state.Credit >= 0 && state.Credit <= MaximumBalance
                && state.Winnings >= 0 && state.Winnings <= MaximumBalance
                && state.LastWin >= 0 && state.LastWin <= 5000
                && state.Reel1 <= 9 && state.Reel2 <= 9 && state.Reel3 <= 9;
        }

        public SlotMachineState Snapshot() { return Copy(_state); }

        public bool TryGetReceipt(SlotMachineIntent request, out byte result)
        {
            return TryGetReceipt(request, out result, out _);
        }

        public bool TryGetReceipt(SlotMachineIntent request, out byte result, out int cashDelta)
        {
            result = SlotMachineResult.Stale;
            cashDelta = 0;
            if (!_receipts.TryGetValue(request.PlayerId, out var receipt) || receipt.Sequence != request.Sequence)
                return false;
            if (receipt.Action == request.Action && receipt.Round == request.Round)
            {
                result = receipt.Result;
                cashDelta = receipt.CashDelta;
            }
            return true;
        }

        public byte Apply(SlotMachineIntent request, float cash, float now, bool nearby, out float nextCash)
        {
            nextCash = cash;
            if (TryGetReceipt(request, out byte repeated)) return repeated;
            if (_receipts.TryGetValue(request.PlayerId, out var previous))
            {
                ushort delta = (ushort)(request.Sequence - previous.Sequence);
                if (delta == 0 || delta > short.MaxValue) return SlotMachineResult.Stale;
            }
            Advance(now);
            byte result;
            if (request.PlayerId == SlotMachineState.NoPlayer || request.MachineId != _state.MachineId
                || request.Action > SlotMachineIntent.Finish || (request.Action != SlotMachineIntent.Finish && request.Round != 0))
                result = SlotMachineResult.Invalid;
            else if (!nearby) result = SlotMachineResult.Distant;
            else if (_state.PlayerId != SlotMachineState.NoPlayer && _state.PlayerId != request.PlayerId)
                result = SlotMachineResult.Busy;
            else result = Perform(request, cash, now, out nextCash);

            if (result != SlotMachineResult.Retry)
                _receipts[request.PlayerId] = new Receipt
                {
                    Sequence = request.Sequence, Action = request.Action, Round = request.Round, Result = result,
                    CashDelta = result == SlotMachineResult.Accepted
                        && (request.Action == SlotMachineIntent.Pay || request.Action == SlotMachineIntent.Cashout)
                        ? (int)Math.Round((double)nextCash - cash) : 0,
                };
            if (result == SlotMachineResult.Accepted)
            {
                _state.PlayerId = request.PlayerId;
                _lastUse = now;
                _state.Revision++;
            }
            return result;
        }

        private byte Perform(SlotMachineIntent request, float cash, float now, out float nextCash)
        {
            nextCash = cash;
            if (request.Action == SlotMachineIntent.Finish)
            {
                if (request.Round != _state.Round || _state.PlayerId != request.PlayerId)
                    return SlotMachineResult.Invalid;
                if (_state.Spinning && now < _earliestFinish) return SlotMachineResult.Retry;
                CompleteRound();
                return SlotMachineResult.Accepted;
            }
            if (_state.Spinning) return SlotMachineResult.Busy;
            switch (request.Action)
            {
                case SlotMachineIntent.Pay:
                    if (_state.Credit > MaximumBalance - _state.Bet || cash < _state.Bet
                        || !TryChangeCash(cash, -_state.Bet, out nextCash)) return SlotMachineResult.Funds;
                    _state.Credit += _state.Bet;
                    break;
                case SlotMachineIntent.Bet:
                    if (_state.Bet < 5) _state.HoldMask = 0;
                    _state.Bet = (byte)(_state.Bet % 5 + 1);
                    break;
                case SlotMachineIntent.Spin:
                    if (_state.Credit < _state.Bet && _state.Winnings < _state.Bet) return SlotMachineResult.Funds;
                    if (_state.Winnings > MaximumBalance - 5000) return SlotMachineResult.Funds;
                    // Draw and evaluate before changing any balance; a broken RNG source
                    // cannot leave a partially debited, retryable transaction behind.
                    byte[] symbols = { _state.Reel1, _state.Reel2, _state.Reel3 };
                    int unlocked = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        if ((_state.HoldMask & (1 << i)) != 0) continue;
                        int index = _random(_reels[i].Length);
                        if (index < 0 || index >= _reels[i].Length) throw new InvalidOperationException("Invalid random reel index.");
                        symbols[i] = (byte)_reels[i][index];
                        unlocked++;
                    }
                    int win = PayoutMultiplier(symbols[0], symbols[1], symbols[2], _payouts) * _state.Bet;
                    if (_state.Credit >= _state.Bet) _state.Credit -= _state.Bet;
                    else _state.Winnings -= _state.Bet;
                    _state.Reel1 = symbols[0]; _state.Reel2 = symbols[1]; _state.Reel3 = symbols[2];
                    _state.LastWin = win;
                    _state.Round++;
                    _state.Spinning = true;
                    _roundStarted = now;
                    _earliestFinish = now + unlocked * 0.5f;
                    break;
                case SlotMachineIntent.Cashout:
                    if (_state.Winnings == 0 || !TryChangeCash(cash, _state.Winnings, out nextCash))
                        return SlotMachineResult.Funds;
                    _state.Winnings = 0;
                    break;
                default:
                    if (!_state.CanHold) return SlotMachineResult.Invalid;
                    int reel = request.Action - SlotMachineIntent.Hold1;
                    if ((_state.HoldMask | (1 << reel)) == 7) return SlotMachineResult.Invalid;
                    _state.HoldMask ^= (byte)(1 << reel);
                    break;
            }
            return SlotMachineResult.Accepted;
        }

        // Symbol 1 is wild. A lone wild on an outer reel pays 2x, but the centre
        // alone pays nothing. Verified by enumerating the native payout FSM.
        public static int PayoutMultiplier(byte a, byte b, byte c, int[] payouts)
        {
            if (a == 1 && b == 1 && c == 1) return payouts[1];
            for (byte symbol = 2; symbol <= 9; symbol++)
                if ((a == 1 || a == symbol) && (b == 1 || b == symbol) && (c == 1 || c == symbol))
                    return payouts[symbol];
            return a == 1 || c == 1 ? payouts[0] : 0;
        }

        public bool Advance(float now)
        {
            bool changed = false;
            if (_state.Spinning && now - _roundStarted >= RoundTimeoutSeconds)
            {
                CompleteRound();
                changed = true;
            }
            if (!_state.Spinning && _state.PlayerId != SlotMachineState.NoPlayer && now - _lastUse >= LeaseSeconds)
            {
                _state.PlayerId = SlotMachineState.NoPlayer;
                changed = true;
            }
            if (changed) _state.Revision++;
            return changed;
        }

        public void ForgetPlayer(byte playerId)
        {
            _receipts.Remove(playerId);
            if (_state.PlayerId != playerId) return;
            CompleteRound();
            _state.PlayerId = SlotMachineState.NoPlayer;
            _state.Revision++;
        }

        public void FinishSession()
        {
            CompleteRound();
            _state.PlayerId = SlotMachineState.NoPlayer;
            _state.Revision++;
        }

        private void CompleteRound()
        {
            if (!_state.Spinning) return;
            _state.Winnings += _state.LastWin;
            _state.CanHold = _state.LastWin == 0 && _state.HoldMask == 0;
            _state.HoldMask = 0;
            _state.Spinning = false;
        }

        private static bool TryChangeCash(float cash, int delta, out float changed)
        {
            changed = cash;
            float candidate = cash + delta;
            if (float.IsNaN(cash) || float.IsInfinity(cash) || float.IsNaN(candidate) || float.IsInfinity(candidate)
                || Math.Abs((double)candidate - cash - delta) > 0.005) return false;
            changed = candidate;
            return true;
        }

        private static SlotMachineState Copy(SlotMachineState s)
        {
            return new SlotMachineState
            {
                MachineId = s.MachineId, Revision = s.Revision, Round = s.Round, PlayerId = s.PlayerId,
                Spinning = s.Spinning, Bet = s.Bet, HoldMask = s.HoldMask, CanHold = s.CanHold,
                Credit = s.Credit, Winnings = s.Winnings, LastWin = s.LastWin,
                Reel1 = s.Reel1, Reel2 = s.Reel2, Reel3 = s.Reel3,
            };
        }
    }
}
