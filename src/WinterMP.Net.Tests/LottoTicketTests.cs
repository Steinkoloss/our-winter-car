using System;
using System.Collections;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class LottoTicketTests
{
    private static LottoTicketRequest Buy(byte player = 1, uint sequence = 1, int count = 1) => new()
    {
        PlayerId = player, Token = 42, Sequence = sequence, Round = 25, LineCount = (byte)count,
        Numbers = Enumerable.Range(0, 21).Select(i => i < count * 7 ? (byte)(i % 7 + 1) : (byte)0).ToArray(),
    };
    private static LottoTicketRequest Claim(string id = "LotteryTicket123", byte player = 1, uint sequence = 1) => new()
        { PlayerId = player, Token = 42, Sequence = sequence, Operation = LottoTicketRequest.Claim, TicketId = id };
    private static LottoTicketState Ticket(uint sequence = 1) => new()
    {
        Sequence = sequence, TicketId = "LotteryTicket123", Round = 25, Numbers = Buy(count: 3).Numbers,
        Winnings = 16527675, Position = new NetVector3(1, 2, 3), Rotation = new NetQuaternion(0, 0, 0, 1),
    };

    [Fact]
    public void LostAcknowledgmentDoesNotIssueOrChargeAgainAndPayloadCannotBeSwapped()
    {
        var ledger = new LottoTicketLedger(); var request = Buy(count: 3); int issued = 0;
        string? Issue() { issued++; return "LotteryTicket123"; }
        var receipt = ledger.Buy(request, 25, 3, 100, true, Issue, out float cash)!;
        Assert.Equal(LottoTicketReceipt.Accepted, receipt.Result); Assert.Equal(91, cash);
        receipt.TicketId = "tampered";
        var repeated = ledger.Buy(request, 26, 3, cash, false, Issue, out cash)!;
        Assert.Equal("LotteryTicket123", repeated.TicketId); Assert.Equal(91, cash); Assert.Equal(1, issued);
        request.Numbers[0] = 39;
        var changed = ledger.Buy(request, 25, 3, cash, true, Issue, out cash)!;
        Assert.Equal(LottoTicketReceipt.Stale, changed.Result); Assert.Equal(91, cash); Assert.Equal(1, issued);
    }

    [Theory]
    [InlineData(26, 100, true, LottoTicketReceipt.Changed)]
    [InlineData(25, 2, true, LottoTicketReceipt.Funds)]
    [InlineData(25, 100, false, LottoTicketReceipt.Distant)]
    public void DeclinedPurchasesNeverSpawnOrDebit(int round, float cash, bool nearby, byte code)
    {
        var ledger = new LottoTicketLedger();
        var result = ledger.Buy(Buy(), round, 3, cash, nearby, () => throw new Exception("Must not spawn"), out float next)!;
        Assert.Equal(code, result.Result); Assert.Equal(cash, next);
    }

    [Fact]
    public void NativeFactoryNotReadyRetriesTheSameRequestWithoutConsumingItsReceipt()
    {
        var ledger = new LottoTicketLedger(); var request = Buy();
        Assert.Null(ledger.Buy(request, 25, 3, 100, true, () => null, out float next)); Assert.Equal(100, next);
        Assert.False(ledger.TryReceipt(request, out _));
        Assert.Equal(LottoTicketReceipt.Accepted, ledger.Buy(request, 25, 3, next, true, () => "LotteryTicket1", out next)!.Result);
        Assert.Equal(97, next);
    }

    [Fact]
    public void ConcurrentPurchasersUseTheirOwnRowsAndOnlyOneCanSpendTheLastCash()
    {
        var ledger = new LottoTicketLedger(); int count = 0;
        string? Issue() => "LotteryTicket" + ++count;
        Assert.Equal(LottoTicketReceipt.Accepted, ledger.Buy(Buy(1), 25, 3, 3, true, Issue, out float cash)!.Result);
        Assert.Equal(LottoTicketReceipt.Funds, ledger.Buy(Buy(2), 25, 3, cash, true, Issue, out cash)!.Result);
        Assert.Equal(0, cash); Assert.Equal(1, count);
        var second = Buy(2, 2); second.Numbers[0] = 39;
        Assert.Equal(LottoTicketReceipt.Accepted, ledger.Buy(second, 25, 3, 6, true, Issue, out cash)!.Result);
        Assert.Equal(3, cash); Assert.Equal(2, count);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(999, 999, 0)]
    [InlineData(1000, 0, 1000)]
    [InlineData(16527675, 0, 16527675)]
    public void ClaimsUseOnlyHostWinningsAndNativeCashBankBoundary(float winnings, float cashGain, float bankGain)
    {
        var ledger = new LottoTicketLedger(); var request = Claim();
        var result = ledger.Claim(request, winnings, 1000, 100, 200, true, out float cash, out float bank);
        Assert.Equal(LottoTicketReceipt.Accepted, result.Result);
        Assert.Equal(100 + cashGain, cash); Assert.Equal(200 + bankGain, bank);
        Assert.Equal(Math.Max(winnings, 0), result.Amount); Assert.Equal(bankGain > 0 ? 1 : 0, result.Destination);
        var repeated = ledger.Claim(request, winnings, 1000, cash, bank, false, out cash, out bank);
        Assert.Equal(LottoTicketReceipt.Accepted, repeated.Result);
        Assert.Equal(100 + cashGain, cash); Assert.Equal(200 + bankGain, bank);
        var other = ledger.Claim(Claim(player: 2), winnings, 1000, cash, bank, true, out cash, out bank);
        Assert.Equal(LottoTicketReceipt.Redeemed, other.Result);
        ledger.ForgetPlayer(1);
        var reconnect = ledger.Claim(request, winnings, 1000, cash, bank, true, out cash, out bank);
        Assert.Equal(LottoTicketReceipt.Redeemed, reconnect.Result);
        Assert.Equal(100 + cashGain, cash); Assert.Equal(200 + bankGain, bank);
    }

    [Fact]
    public void MissingDistantOrUnrepresentableClaimDoesNotRetireTicket()
    {
        var ledger = new LottoTicketLedger(); var request = Claim();
        Assert.Equal(LottoTicketReceipt.Unavailable, ledger.Claim(request, null, 1000, 1, 1, true, out _, out _).Result);
        request.Sequence++;
        Assert.Equal(LottoTicketReceipt.Distant, ledger.Claim(request, 30, 1000, 1, 1, false, out _, out _).Result);
        request.Sequence++;
        Assert.Equal(LottoTicketReceipt.Funds, ledger.Claim(request, 3, 1000, 100000000, 1, true, out _, out _).Result);
        Assert.False(ledger.IsRetired(request.TicketId)); request.Sequence++;
        Assert.Equal(LottoTicketReceipt.Accepted, ledger.Claim(request, 30, 1000, 1, 1, true, out float cash, out _).Result);
        Assert.Equal(31, cash);
    }

    [Fact]
    public void ReceiptOrderingWrapAndAdmissionDoNotClearTicketTombstones()
    {
        var ledger = new LottoTicketLedger(); var r = Claim(sequence: uint.MaxValue);
        ledger.Claim(r, 100, 1000, 0, 0, true, out _, out _);
        r.Sequence = 0; r.TicketId = "LotteryTicket456";
        Assert.Equal(LottoTicketReceipt.Accepted, ledger.Claim(r, 100, 1000, 0, 0, true, out _, out _).Result);
        r.Sequence = uint.MaxValue;
        Assert.Equal(LottoTicketReceipt.Stale, ledger.Claim(r, 100, 1000, 0, 0, true, out _, out _).Result);
        r.Token++;
        Assert.Equal(LottoTicketReceipt.Stale, ledger.Claim(r, 100, 1000, 0, 0, true, out _, out _).Result);
        ledger.ForgetPlayer(1);
        Assert.True(ledger.IsRetired("LotteryTicket123")); Assert.True(ledger.IsRetired("LotteryTicket456"));
        ledger.Clear(); Assert.False(ledger.IsRetired("LotteryTicket123"));
    }

    [Fact]
    public void DiscardingANativeTicketPreventsLaterCreditWithoutAnEarlierClaim()
    {
        var ledger = new LottoTicketLedger(); ledger.Retire("LotteryTicket123"); ledger.ForgetPlayer(1);
        Assert.Equal(LottoTicketReceipt.Redeemed,
            ledger.Claim(Claim(), 5000, 1000, 10, 20, true, out float cash, out float bank).Result);
        Assert.Equal(10, cash); Assert.Equal(20, bank);
    }

    [Fact]
    public void CaptureOmitsUnpaidPartialRowsAndCopiesSelections()
    {
        IList[] lists = { new ArrayList(new[] { 1, 2, 3, 4, 5, 6, 7 }), new ArrayList(new[] { 10, 11, 0, 0, 0, 0, 0 }), new ArrayList() };
        Assert.True(LottoTicketLedger.CaptureLines(lists, 1, out var numbers));
        Assert.All(numbers.Skip(7), n => Assert.Equal(0, n));
        lists[0][0] = 39; Assert.Equal(1, numbers[0]);
        Assert.False(LottoTicketLedger.CaptureLines(lists, 2, out _));
        lists[0][0] = 1f; Assert.False(LottoTicketLedger.CaptureLines(lists, 1, out _));
    }

    [Theory]
    [InlineData("duplicate")][InlineData("zero")][InlineData("above39")][InlineData("short")]
    [InlineData("free-row")][InlineData("no-lines")][InlineData("four-lines")][InlineData("bad-token")]
    [InlineData("bad-player")][InlineData("round")][InlineData("issued-id")][InlineData("operation")]
    public void MalformedPurchaseCannotIssueTicket(string fault)
    {
        var r = Buy();
        switch (fault)
        {
            case "duplicate": r.Numbers[1] = r.Numbers[0]; break;
            case "zero": r.Numbers[0] = 0; break;
            case "above39": r.Numbers[0] = 40; break;
            case "short": r.Numbers = new byte[20]; break;
            case "free-row": r.Numbers[7] = 1; break;
            case "no-lines": r.LineCount = 0; break;
            case "four-lines": r.LineCount = 4; break;
            case "bad-token": r.Token = 0; break;
            case "bad-player": r.PlayerId = 255; break;
            case "round": r.Round = 8888; break;
            case "issued-id": r.TicketId = "LotteryTicket123"; break;
            case "operation": r.Operation = 3; break;
        }
        var result = new LottoTicketLedger().Buy(r, r.Round, 3, 100, true, () => throw new Exception("Must not issue"), out float cash)!;
        Assert.Equal(LottoTicketReceipt.Invalid, result.Result); Assert.Equal(100, cash);
    }

    [Fact]
    public void ReplicaPreservesPendingRowsAndRetirementAcrossLateBindingAndStaleStates()
    {
        var replica = new LottoTicketReplica(); var state = Ticket(uint.MaxValue);
        Assert.True(replica.Receive(state)); state.Numbers[0] = 39;
        Assert.Equal(1, replica.States.Single().Numbers[0]);
        var next = Ticket(0); next.Retired = true;
        Assert.True(replica.Receive(next)); Assert.False(replica.Receive(Ticket(1)));
        Assert.False(replica.Receive(Ticket(uint.MaxValue)));
        Assert.True(replica.States.Single().Retired);
        replica.Clear(); Assert.True(replica.Receive(Ticket(1)));
        var invalid = Ticket(2); invalid.Position.X = float.NaN;
        Assert.False(replica.Receive(invalid)); Assert.True(replica.Receive(Ticket(2)));
        invalid = Ticket(3); invalid.Numbers[0] = 39;
        Assert.False(replica.Receive(invalid)); Assert.True(replica.Receive(Ticket(3)));
    }

    [Fact]
    public void TicketMessagesRoundTripAndRejectTruncationAndBadArraySizes()
    {
        IMessage[] messages = { Buy(count: 3), Claim(), Ticket(), new LottoTicketReceipt
        { PlayerId = 3, Token = ulong.MaxValue, Sequence = uint.MaxValue, TicketId = "LotteryTicket123", Amount = 1234.5f, Destination = 1 } };
        foreach (var message in messages)
        {
            var bytes = PacketCodec.Encode(message);
            Assert.Equal(bytes, PacketCodec.Encode(PacketCodec.Decode(bytes)));
            for (int n = 2; n < bytes.Length; n++)
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(n).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(message.Id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(message.Id, Channel.UnreliableSequenced));
        }
        var r = Buy(); r.Numbers = new byte[22]; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(r));
        var s = Ticket(); s.Numbers = null!; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
    }

    [Fact]
    public void SeededBuyClaimReconnectAndAckLossLoopConservesBothBalances()
    {
        var ledger = new LottoTicketLedger(); var random = new Random(103);
        float cash = 100000, bank = 5000;
        double expected = cash + bank; int issued = 0;
        var sequences = new uint[4];
        for (int i = 0; i < 1000; i++)
        {
            byte player = (byte)random.Next(4);
            if (i % 7 == 0) ledger.ForgetPlayer(player);
            var buy = Buy(player, ++sequences[player], random.Next(1, 4));
            buy = (LottoTicketRequest)PacketCodec.Decode(PacketCodec.Encode(buy));
            string? Create() => "LotteryTicket" + ++issued;
            var bought = ledger.Buy(buy, 25, 3, cash, true, Create, out cash)!;
            Assert.Equal(LottoTicketReceipt.Accepted, bought.Result); expected -= bought.Amount;
            Assert.Equal(bought.TicketId, ledger.Buy(buy, 25, 3, cash, true, Create, out cash)!.TicketId);
            float winnings = random.Next(0, 2501);
            var claim = Claim(bought.TicketId, player, ++sequences[player]);
            var paid = ledger.Claim(claim, winnings, 1000, cash, bank, true, out cash, out bank);
            Assert.Equal(LottoTicketReceipt.Accepted, paid.Result); expected += winnings;
            ledger.Claim(claim, winnings, 1000, cash, bank, true, out cash, out bank);
            ledger.ForgetPlayer(player);
            Assert.Equal(LottoTicketReceipt.Redeemed,
                ledger.Claim(claim, winnings, 1000, cash, bank, true, out cash, out bank).Result);
            Assert.Equal(expected, (double)cash + bank);
        }
        Assert.Equal(1000, issued);
    }
}
