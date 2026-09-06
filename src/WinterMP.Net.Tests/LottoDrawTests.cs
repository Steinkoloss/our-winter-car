using System;
using System.Collections;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class LottoDrawTests
{
    private static LottoDrawState Draw(uint sequence = 1) => new()
    {
        Sequence = sequence, Round = 123456, TicketRound = 123455,
        NationalPot = 16527675, NationalPotMin = 20000000, NationalPotFull = 49583027,
        Numbers = new byte[] { 5, 7, 12, 15, 19, 26, 34 }, Bonus = new byte[] { 1, 21, 28 },
        Prizes = new[] { 16527675, 826383, 82638, 8263, 82 }, Winners = new[] { 2, 10, 116, 7502, 146262 },
        Flags = LottoDrawState.AllFlags,
    };
    private static ArrayList List(byte[] values) => new(values.Select(v => (int)v).ToArray());

    [Fact]
    public void WireHasFixed77BytesAndPreservesFullNativeAmounts()
    {
        var state = Draw(0x01020304);
        var packet = PacketCodec.Encode(state);
        Assert.Equal(77, packet.Length);
        Assert.Equal(new byte[] { 180, 0, 4, 3, 2, 1 }, packet.Take(6));
        Assert.Equal(state.Numbers, packet.Skip(26).Take(7));
        Assert.Equal(state.Bonus, packet.Skip(33).Take(3));
        Assert.Equal(new byte[] { 0x3b, 0x31, 0xfc, 0 }, packet.Skip(36).Take(4));
        Assert.Equal(LottoDrawState.AllFlags, packet[76]);
        var decoded = Assert.IsType<LottoDrawState>(PacketCodec.Decode(packet));
        Assert.True(LottoDrawReplica.SameDraw(state, decoded));
        Assert.Equal(state.Sequence, decoded.Sequence);
        for (int length = 2; length < packet.Length; length++)
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet.Take(length).ToArray()));
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet.Concat(new byte[] { 0 }).ToArray()));
        state.Numbers = new byte[6];
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("above-range")]
    [InlineData("duplicate-main")]
    [InlineData("duplicate-bonus")]
    [InlineData("overlap")]
    [InlineData("unsorted")]
    [InlineData("short-main")]
    [InlineData("long-bonus")]
    [InlineData("short-prizes")]
    [InlineData("missing-winners")]
    [InlineData("negative-round")]
    [InlineData("negative-ticket")]
    [InlineData("negative-pot")]
    [InlineData("negative-min")]
    [InlineData("negative-full")]
    [InlineData("negative-prize")]
    [InlineData("negative-winners")]
    [InlineData("unknown-flags")]
    public void InvalidDrawNeverConsumesTheSequenceOrReplacesPendingState(string fault)
    {
        var replica = new LottoDrawReplica();
        Assert.True(replica.Receive(Draw()));
        var bad = Draw(2);
        switch (fault)
        {
            case "zero": bad.Numbers[0] = 0; break;
            case "above-range": bad.Bonus[2] = 40; break;
            case "duplicate-main": bad.Numbers[1] = 5; break;
            case "duplicate-bonus": bad.Bonus[1] = 1; break;
            case "overlap": bad.Bonus[0] = 5; break;
            case "unsorted": Array.Reverse(bad.Numbers); break;
            case "short-main": bad.Numbers = new byte[6]; break;
            case "long-bonus": bad.Bonus = new byte[4]; break;
            case "short-prizes": bad.Prizes = new int[4]; break;
            case "missing-winners": bad.Winners = null!; break;
            case "negative-round": bad.Round = -1; break;
            case "negative-ticket": bad.TicketRound = -1; break;
            case "negative-pot": bad.NationalPot = -1; break;
            case "negative-min": bad.NationalPotMin = -1; break;
            case "negative-full": bad.NationalPotFull = -1; break;
            case "negative-prize": bad.Prizes[0] = -1; break;
            case "negative-winners": bad.Winners[4] = -1; break;
            case "unknown-flags": bad.Flags |= 4; break;
        }
        Assert.False(replica.Receive(bad));
        Assert.Equal(1u, replica.Current!.Sequence);
        Assert.True(replica.Receive(Draw(2)));
    }

    [Fact]
    public void PendingDrawIsOwnedAndSurvivesLateBindingsButCannotRollBack()
    {
        var replica = new LottoDrawReplica();
        var incoming = Draw(uint.MaxValue);
        Assert.True(replica.Receive(incoming));
        incoming.Numbers[0] = 39; incoming.Prizes[0] = 0; incoming.Winners[0] = 0; incoming.Bonus[0] = 39;
        Assert.Equal(Draw().Numbers, replica.Current!.Numbers);
        Assert.Equal(Draw().Bonus, replica.Current.Bonus);
        Assert.Equal(Draw().Prizes, replica.Current.Prizes);
        Assert.Equal(Draw().Winners, replica.Current.Winners);
        Assert.False(replica.Receive(Draw(uint.MaxValue)));
        Assert.False(replica.Receive(Draw(uint.MaxValue - 1)));
        Assert.True(replica.Receive(Draw(0)));
        Assert.False(replica.Receive(Draw(0x80000000)));
        Assert.False(replica.Receive(Draw(uint.MaxValue)));
        replica.Clear();
        Assert.Null(replica.Current);
        Assert.True(replica.Receive(Draw(uint.MaxValue - 1)));
    }

    [Fact]
    public void NativeListsRequireACompleteSortedDisjointDrawAndExactIntElements()
    {
        var expected = Draw(); var sample = Draw();
        var numbers = List(expected.Numbers); var bonus = List(expected.Bonus);
        var prizes = new ArrayList(expected.Prizes); var winners = new ArrayList(expected.Winners);
        Assert.True(LottoDrawReplica.ReadLists(sample, numbers, bonus, prizes, winners));
        Assert.True(LottoDrawReplica.SameDraw(expected, sample));
        numbers.RemoveAt(6);
        Assert.False(LottoDrawReplica.ReadLists(sample, numbers, bonus, prizes, winners));
        Assert.True(LottoDrawReplica.SameDraw(expected, sample));
        numbers.Add(34);
        foreach (object invalid in new object[] { 261, -251, 5f, "5", (byte)5 })
        {
            numbers[0] = invalid;
            Assert.False(LottoDrawReplica.ReadLists(sample, numbers, bonus, prizes, winners));
        }
        numbers[0] = 5; bonus[0] = 5;
        Assert.False(LottoDrawReplica.ReadLists(sample, numbers, bonus, prizes, winners));
        bonus[0] = 1; prizes[0] = (long)16527675;
        Assert.False(LottoDrawReplica.ReadLists(sample, numbers, bonus, prizes, winners));
        prizes[0] = 16527675; winners[4] = -1;
        Assert.False(LottoDrawReplica.ReadLists(sample, numbers, bonus, prizes, winners));
        Assert.True(LottoDrawReplica.SameDraw(expected, sample));
    }

    [Fact]
    public void SameRoundChangesToEveryListAndScalarAreBroadcastWorthy()
    {
        var original = Draw();
        Action<LottoDrawState>[] edits = {
            d => d.Round++, d => d.TicketRound++, d => d.NationalPot++, d => d.NationalPotMin++, d => d.NationalPotFull++,
            d => d.Flags ^= LottoDrawState.FlagResultsVisible, d => d.Flags ^= LottoDrawState.FlagDrawDone,
        };
        foreach (var edit in edits)
        {
            var changed = Draw(); edit(changed);
            Assert.False(LottoDrawReplica.SameDraw(original, changed));
        }
        for (int i = 0; i < LottoDrawState.MainCount; i++)
        {
            var changed = Draw(); changed.Numbers[i]++;
            Assert.False(LottoDrawReplica.SameDraw(original, changed));
        }
        for (int i = 0; i < LottoDrawState.BonusCount; i++)
        {
            var changed = Draw(); changed.Bonus[i]++;
            Assert.False(LottoDrawReplica.SameDraw(original, changed));
        }
        for (int i = 0; i < LottoDrawState.TierCount; i++)
        {
            var changed = Draw(); changed.Prizes[i]++;
            Assert.False(LottoDrawReplica.SameDraw(original, changed));
            changed = Draw(); changed.Winners[i]++;
            Assert.False(LottoDrawReplica.SameDraw(original, changed));
        }
        Assert.True(LottoDrawReplica.SameDraw(original, Draw(999)));
    }

    [Fact]
    public void ValidZeroAndLargeNativeValuesDoNotInventDrawCompletionOrPayoutRules()
    {
        var state = Draw();
        state.Round = 0; state.TicketRound = int.MaxValue; state.Flags = 0;
        state.NationalPot = 0; state.NationalPotFull = int.MaxValue;
        state.Numbers[6] = LottoDrawState.MaximumNumber;
        state.Prizes[0] = int.MaxValue; state.Winners[0] = 0;
        Assert.True(LottoDrawReplica.IsValid(state));
        // DrawDone is only native data; the Core adapter gates on completed native states separately.
        state.Flags = LottoDrawState.FlagDrawDone;
        Assert.True(LottoDrawReplica.IsValid(state));
    }

    [Fact]
    public void LottoUsesReliableOrderedAndSelectedAuthenticatedHostAdmission()
    {
        Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.LottoDrawState, Channel.ReliableOrdered));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.LottoDrawState, Channel.UnreliableSequenced));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.LottoDrawState, Channel.ReliableBulk));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.LottoDrawState, false, true, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.LottoDrawState, false, true, true, false));
        Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.LottoDrawState, false, true, true, true));
        var old = new LotteryDrawState { WinningNumbers = "Lotto7" };
        Assert.IsType<LotteryDrawState>(PacketCodec.Decode(PacketCodec.Encode(old)));
        Assert.NotEqual(old.Id, new LottoDrawState().Id);
    }
}
