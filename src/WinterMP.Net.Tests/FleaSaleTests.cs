using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class FleaSaleTests
{
    private static FleaSaleState State(int days = -1, float money = 0, bool offer = false) => new() {
        RentDays = (ushort)Math.Max(0, days), MoneyTotal = money, WeekPrice = 150,
        Flags = (byte)((days >= 0 ? 1 : 0) | (offer ? 2 : 0)) };
    private static FleaSaleIntent Rent(uint revision, ushort sequence = 1, byte player = 1, ushort weeks = 1) => new() {
        Action = FleaSaleIntent.PayRent, PlayerId = player, Weeks = weeks, Revision = revision, Sequence = sequence };
    private static FleaSaleIntent Collect(uint revision, ushort sequence = 1, byte player = 1) => new() {
        Action = FleaSaleIntent.CollectProceeds, PlayerId = player, Revision = revision, Sequence = sequence };

    [Fact]
    public void RentChargesAtCheckoutAndExtendsPaidDays()
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State());
        var r = l.Apply(Rent(s.Revision, weeks: 2), 500, true, true, out var cash, out var next);
        Assert.Equal(FleaSaleResult.Accepted, r.Result); Assert.Equal(200, cash);
        Assert.Equal(14, next!.RentDays); Assert.Equal(1, next.Flags);
        Assert.Equal(0, next.MoneyTotal); Assert.NotEqual(s.Revision, next.Revision);
    }
    [Fact]
    public void RentZeroIsStillAnActiveNativeDay()
    {
        var s = State(0); Assert.True(FleaSalePolicy.Valid(s));
        Assert.False(FleaSalePolicy.Same(s, State()));
        var l = new FleaSaleLedger(); s = l.Observe(s);
        Assert.Equal(FleaSaleResult.Accepted, l.Apply(Rent(s.Revision), 150, true, true, out var cash, out var next).Result);
        Assert.Equal(0, cash); Assert.Equal(7, next!.RentDays);
    }
    [Fact]
    public void ReceiptRetryDoesNotTransferAgainOrExposeMutableCache()
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State()); var request = Rent(s.Revision);
        var r = l.Apply(request, 500, true, true, out var cash, out var next);
        r.Result = FleaSaleResult.Funds;
        Assert.Equal(FleaSaleResult.Accepted, l.Apply(request, cash, false, false, out var unchanged, out var same).Result);
        Assert.Equal(cash, unchanged); Assert.True(FleaSalePolicy.Same(next!, same!));
        request.Weeks = 2;
        Assert.Equal(FleaSaleResult.Stale, l.Apply(request, cash, true, true, out unchanged, out _).Result);
        Assert.Equal(cash, unchanged);
    }
    [Fact]
    public void ConflictingPlayersCannotUseTheSameRentalQuote()
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State());
        l.Apply(Rent(s.Revision), 500, true, true, out var cash, out _);
        Assert.Equal(FleaSaleResult.Changed, l.Apply(Rent(s.Revision, player: 2), cash, true, true, out var unchanged, out _).Result);
        Assert.Equal(cash, unchanged);
    }
    [Fact]
    public void CollectionTransfersOnceAndClearsNativeProceeds()
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State(-1, 125, true)); var request = Collect(s.Revision);
        Assert.Equal(FleaSaleResult.Accepted, l.Apply(request, 100, true, true, out var cash, out var next).Result);
        Assert.Equal(225, cash); Assert.Equal(0, next!.MoneyTotal); Assert.Equal(0, next.Flags);
        Assert.Equal(FleaSaleResult.Changed, l.Apply(Collect(s.Revision, player: 2), cash, true, true, out var unchanged, out _).Result);
        Assert.Equal(cash, unchanged);
        Assert.Equal(FleaSaleResult.Accepted, l.Apply(request, cash, true, true, out unchanged, out _).Result);
        Assert.Equal(cash, unchanged);
        var loaded = new FleaSaleLedger(); var reloaded = loaded.Observe(next);
        Assert.Equal(FleaSaleResult.Unavailable, loaded.Apply(Collect(reloaded.Revision), cash, true, true, out unchanged, out _).Result);
    }
    [Theory]
    [InlineData(false, true, 1000, FleaSaleResult.Distant)]
    [InlineData(true, false, 1000, FleaSaleResult.Unavailable)]
    [InlineData(true, true, 149, FleaSaleResult.Funds)]
    [InlineData(true, true, float.NaN, FleaSaleResult.Funds)]
    [InlineData(true, true, float.PositiveInfinity, FleaSaleResult.Funds)]
    [InlineData(true, true, 1e30f, FleaSaleResult.Funds)]
    public void DeclineDoesNotAlterWalletOrDaysAndConsumesSequence(bool near, bool available, float cash, byte expected)
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State()); var r = Rent(s.Revision);
        Assert.Equal(expected, l.Apply(r, cash, near, available, out var unchanged, out var next).Result);
        Assert.Equal(cash, unchanged); Assert.True(FleaSalePolicy.Same(s, next!));
        Assert.Equal(expected, l.Apply(r, 1000, true, true, out unchanged, out _).Result);
        Assert.Equal(1000, unchanged);
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(4, 0)] [InlineData(2, 0)] [InlineData(2, 53)] [InlineData(3, 1)]
    public void InvalidAndRetiredActionsCannotBeSerialized(byte action, ushort weeks)
    {
        var r = Rent(1); r.Action = action; r.Weeks = weeks;
        Assert.False(FleaSalePolicy.Valid(r)); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(r));
        var w = new NetWriter(); w.WriteByte(action); w.WriteByte(1); w.WriteUInt16(1); w.WriteUInt32(1); w.WriteUInt16(weeks);
        Assert.Throws<ProtocolException>(() => new FleaSaleIntent().Read(new NetReader(w.ToArray())));
    }
    [Theory]
    [InlineData(0, 1, 0)] [InlineData(2, 0, 0)] [InlineData(3, 1, 5)] [InlineData(4, 0, 0)] [InlineData(1, 1007, 0)]
    public void ContradictoryNativeFlagsAreRejected(byte flags, ushort days, float money)
    {
        var s = State(); s.Flags = flags; s.RentDays = days; s.MoneyTotal = money;
        Assert.False(FleaSalePolicy.Valid(s)); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
    }
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.NegativeInfinity)] [InlineData(-1)]
    public void InvalidMoneyIsRejected(float value)
    { var s = State(); s.MoneyTotal = value; Assert.False(FleaSalePolicy.Valid(s)); }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(1000001)]
    public void InvalidRentalPricesAreRejected(float value)
    { var s = State(); s.WeekPrice = value; Assert.False(FleaSalePolicy.Valid(s)); }
    [Fact]
    public void AlreadyRentedTableCannotCollectUntilEnvelopeIsAvailable()
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State(4, 300));
        Assert.Equal(FleaSaleResult.Unavailable, l.Apply(Collect(s.Revision), 100, true, true, out var cash, out _).Result);
        Assert.Equal(100, cash);
        s = l.Observe(State(-1, 300));
        Assert.Equal(FleaSaleResult.Unavailable, l.Apply(Collect(s.Revision, 2), 100, true, true, out _, out _).Result);
    }
    [Fact]
    public void StaleSnapshotAndEqualRevisionWithDifferentMoneyAreRefused()
    {
        var old = State(); old.Revision = uint.MaxValue;
        var next = State(7); next.Revision = 0;
        Assert.True(FleaSalePolicy.CanApply(old, next)); Assert.False(FleaSalePolicy.CanApply(next, old));
        old = FleaSalePolicy.Copy(next); next.MoneyTotal++;
        Assert.False(FleaSalePolicy.CanApply(old, next)); Assert.True(FleaSalePolicy.CanApply(old, FleaSalePolicy.Copy(old)));
    }
    [Fact]
    public void SequenceWrapAndRejoinResetRemainBounded()
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State());
        l.Apply(Rent(s.Revision, ushort.MaxValue), 1000, false, true, out _, out _);
        Assert.Equal(FleaSaleResult.Accepted, l.Apply(Rent(s.Revision, 0), 1000, true, true, out _, out var next).Result);
        Assert.Equal(FleaSaleResult.Stale, l.Apply(Rent(next!.Revision, ushort.MaxValue), 1000, true, true, out _, out _).Result);
        l.ForgetPlayer(1);
        Assert.Equal(FleaSaleResult.Accepted, l.Apply(Rent(next.Revision, 1), 1000, true, true, out _, out _).Result);
    }
    [Fact]
    public void WireLayoutsAndAdmissionMatchTransactions()
    {
        var s = State(7, 100); s.Revision = 123;
        var data = PacketCodec.Encode(s); Assert.Equal(19, data.Length);
        Assert.True(FleaSalePolicy.Same(s, Assert.IsType<FleaSaleState>(PacketCodec.Decode(data))));
        var r = Rent(123, 4, 1, 2); data = PacketCodec.Encode(r); Assert.Equal(12, data.Length);
        var decoded = Assert.IsType<FleaSaleIntent>(PacketCodec.Decode(data)); Assert.Equal(2, decoded.Weeks); Assert.Equal(123u, decoded.Revision);
        var receipt = new FleaSaleResult { PlayerId = 1, Sequence = 4, Action = r.Action };
        Assert.Equal(7, PacketCodec.Encode(receipt).Length);
        foreach (var id in new[] { MessageId.FleaSaleState, MessageId.FleaSaleResult })
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, false));
        }
        Assert.True(SessionMessagePolicy.IsSenderAllowed(r.Id, true, true, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(r.Id, true, false, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(r.Id, false, true, true, true));
        foreach (var id in new[] { s.Id, r.Id, receipt.Id })
        {
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
        }
    }
    [Fact]
    public void RenewingWithUncollectedProceedsCarriesThemIntoTheNewRental()
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State(-1, 250, true));
        Assert.Equal(FleaSaleResult.Accepted, l.Apply(Rent(s.Revision), 500, true, true, out var cash, out var next).Result);
        Assert.Equal(350, cash); Assert.Equal(250, next!.MoneyTotal); Assert.Equal(1, next.Flags);
        Assert.Equal(FleaSaleResult.Unavailable, l.Apply(Collect(next.Revision, 2), cash, true, true, out var unchanged, out _).Result);
        Assert.Equal(cash, unchanged);
    }
    [Fact]
    public void ChangedNativePriceInvalidatesQuoteAndOverflowDoesNotCharge()
    {
        var l = new FleaSaleLedger(); var s = l.Observe(State(1000));
        Assert.Equal(FleaSaleResult.Unavailable, l.Apply(Rent(s.Revision), 1000, true, true, out var cash, out _).Result);
        Assert.Equal(1000, cash);
        s = l.Observe(State()); var changed = State(); changed.WeekPrice = 200; l.Observe(changed);
        Assert.Equal(FleaSaleResult.Changed, l.Apply(Rent(s.Revision, 2), 1000, true, true, out cash, out _).Result);
        Assert.Equal(1000, cash);
    }
    [Fact]
    public void CatalogRequiresCompleteNativeBindingsAndKeepsOtherSubsystemsOnFailure()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
        var valid = SyncCatalogJson.Parse(text); Assert.NotNull(valid.FleaSale); Assert.Null(valid.FleaSaleError);
        Assert.Equal("Bought", valid.FleaSale!["bought"]); Assert.Equal("Check money", valid.FleaSale["request"]);
        foreach (string key in FleaSaleData.Required)
        {
            var json = JsonNode.Parse(text)!.AsObject(); json["fleaSale"]!.AsObject().Remove(key);
            var broken = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(broken.FleaSale); Assert.NotNull(broken.FleaSaleError); Assert.NotNull(broken.AtfRefill);
        }
    }

}
