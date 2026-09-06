using System;
using System.Collections;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Ticket payment receipts and lifetime claim deduplication, independent of player admission.</summary>
    public sealed class LottoTicketLedger
    {
        private sealed class Receipt
        {
            public LottoTicketRequest Request = new LottoTicketRequest();
            public LottoTicketReceipt Result = new LottoTicketReceipt();
        }
        private readonly Dictionary<byte, Receipt> _receipts = new Dictionary<byte, Receipt>();
        private readonly HashSet<string> _retired = new HashSet<string>(StringComparer.Ordinal);

        public bool IsRetired(string id) => _retired.Contains(id);
        public void Retire(string id) { if (ValidId(id)) _retired.Add(id); }
        public void ForgetPlayer(byte player) { _receipts.Remove(player); }
        public void Clear() { _receipts.Clear(); _retired.Clear(); }

        public bool TryReceipt(LottoTicketRequest r, out LottoTicketReceipt receipt)
        {
            receipt = Result(r, LottoTicketReceipt.Stale);
            if (!_receipts.TryGetValue(r.PlayerId, out var old)) return false;
            if (old.Request.Token != r.Token) return true;
            uint delta = unchecked(r.Sequence - old.Request.Sequence);
            if (delta != 0 && delta <= int.MaxValue) return false;
            if (delta == 0 && SameRequest(old.Request, r)) receipt = Copy(old.Result);
            return true;
        }

        // The native creation callback runs only after all checks, and must either return
        // a fully configured persistent ticket ID or clean up and return null. No money
        // or receipt is committed on that transient failure.
        public LottoTicketReceipt? Buy(LottoTicketRequest r, int currentRound, float linePrice,
            float cash, bool nearby, Func<string?> create, out float nextCash)
        {
            nextCash = cash;
            if (TryReceipt(r, out var previous)) return previous;
            byte code;
            float total = linePrice * r.LineCount;
            if (!ValidRequest(r) || r.Operation != LottoTicketRequest.Buy
                || !BankTransferPolicy.IsFinite(linePrice) || linePrice <= 0) code = LottoTicketReceipt.Invalid;
            else if (r.Round != currentRound) code = LottoTicketReceipt.Changed;
            else if (!nearby) code = LottoTicketReceipt.Distant;
            else if (!TryMoney(cash, -total, out nextCash)) code = LottoTicketReceipt.Funds;
            else
            {
                string? id = create();
                if (!ValidId(id)) { nextCash = cash; return null; }
                var accepted = Result(r, LottoTicketReceipt.Accepted);
                accepted.TicketId = id!; accepted.Amount = total;
                return Remember(r, accepted);
            }
            return Remember(r, Result(r, code));
        }

        public LottoTicketReceipt Claim(LottoTicketRequest r, float? hostWinnings, float bankThreshold,
            float cash, float bank, bool nearby, out float nextCash, out float nextBank)
        {
            nextCash = cash; nextBank = bank;
            if (TryReceipt(r, out var previous)) return previous;
            var result = Result(r, LottoTicketReceipt.Invalid);
            if (!ValidRequest(r) || r.Operation != LottoTicketRequest.Claim
                || !BankTransferPolicy.IsFinite(bankThreshold) || bankThreshold <= 0) return Remember(r, result);
            result.TicketId = r.TicketId;
            if (_retired.Contains(r.TicketId)) result.Result = LottoTicketReceipt.Redeemed;
            else if (!hostWinnings.HasValue || !BankTransferPolicy.IsFinite(hostWinnings.Value)) result.Result = LottoTicketReceipt.Unavailable;
            else if (!nearby) result.Result = LottoTicketReceipt.Distant;
            else
            {
                float amount = Math.Max(0, hostWinnings.Value);
                bool toBank = amount >= bankThreshold;
                if (!TryMoney(toBank ? bank : cash, amount, out float balance)) result.Result = LottoTicketReceipt.Funds;
                else
                {
                    if (toBank) nextBank = balance; else nextCash = balance;
                    result.Result = LottoTicketReceipt.Accepted; result.Amount = amount;
                    result.Destination = (byte)(toBank ? 1 : 0);
                    _retired.Add(r.TicketId);
                }
            }
            return Remember(r, result);
        }

        public static bool TryMoney(float balance, float delta, out float result)
        {
            result = balance;
            if (!BankTransferPolicy.IsFinite(balance) || !BankTransferPolicy.IsFinite(delta)) return false;
            float next = balance + delta;
            if (!BankTransferPolicy.IsFinite(next) || (delta < 0 && next < 0)
                || Math.Abs((double)next - balance - delta) > .005) return false;
            result = next; return true;
        }

        public static bool ValidId(string? id)
        {
            if (string.IsNullOrEmpty(id) || id!.Length > 80) return false;
            foreach (char c in id) if (c < 32 || c > 126) return false;
            return true;
        }

        public static bool ValidRequest(LottoTicketRequest r)
        {
            if (r.PlayerId == byte.MaxValue || r.Token == 0 || r.Numbers == null || r.Numbers.Length != 21) return false;
            if (r.Operation == LottoTicketRequest.Claim)
            {
                if (!ValidId(r.TicketId) || r.Round != 0 || r.LineCount != 0) return false;
                foreach (byte n in r.Numbers) if (n != 0) return false;
                return true;
            }
            if (r.Operation != LottoTicketRequest.Buy || r.TicketId != string.Empty
                || r.Round < 0 || r.Round == 8888 || r.LineCount < 1 || r.LineCount > 3) return false;
            for (int line = 0; line < 3; line++)
            {
                ulong seen = 0;
                for (int i = line * 7; i < line * 7 + 7; i++)
                {
                    int n = r.Numbers[i];
                    if (line >= r.LineCount) { if (n != 0) return false; continue; }
                    if (n < 1 || n > 39 || (seen & (1UL << n)) != 0) return false;
                    seen |= 1UL << n;
                }
            }
            return true;
        }

        /// <summary>Only paid complete rows survive; partially edited following rows are not free bets.</summary>
        public static bool CaptureLines(IList[] lists, int count, out byte[] numbers)
        {
            numbers = new byte[21];
            if (lists.Length != 3 || count < 1 || count > 3) return false;
            for (int line = 0; line < count; line++)
            {
                if (lists[line] == null || lists[line].Count != 7) return false;
                for (int i = 0; i < 7; i++)
                {
                    if (!(lists[line][i] is int n) || n < 1 || n > 39) return false;
                    numbers[line * 7 + i] = (byte)n;
                }
            }
            return ValidRequest(new LottoTicketRequest { Token = 1, LineCount = (byte)count, Numbers = numbers });
        }

        private LottoTicketReceipt Remember(LottoTicketRequest r, LottoTicketReceipt result)
        {
            _receipts[r.PlayerId] = new Receipt { Request = new LottoTicketRequest
            {
                PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, Operation = r.Operation,
                Round = r.Round, LineCount = r.LineCount, TicketId = r.TicketId,
                Numbers = r.Numbers == null ? new byte[0] : (byte[])r.Numbers.Clone(),
            }, Result = Copy(result) };
            return result;
        }
        private static bool SameRequest(LottoTicketRequest a, LottoTicketRequest b)
        {
            if (a.Operation != b.Operation || a.Round != b.Round || a.LineCount != b.LineCount || a.TicketId != b.TicketId
                || b.Numbers == null || a.Numbers.Length != b.Numbers.Length) return false;
            for (int i = 0; i < a.Numbers.Length; i++) if (a.Numbers[i] != b.Numbers[i]) return false;
            return true;
        }
        private static LottoTicketReceipt Result(LottoTicketRequest r, byte code) => new LottoTicketReceipt
            { PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, Result = code };
        private static LottoTicketReceipt Copy(LottoTicketReceipt r) => new LottoTicketReceipt
        {
            PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, Result = r.Result,
            TicketId = r.TicketId, Amount = r.Amount, Destination = r.Destination,
        };
    }
}
