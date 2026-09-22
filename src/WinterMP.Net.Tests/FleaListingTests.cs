using System;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class FleaListingTests
{
    private static FleaListingState State(uint revision = 1)
    {
        var s = new FleaListingState { Revision = revision };
        s.Items.Add(new() { ItemId = 50, NativeNumber = 3, Price = 5, Position = new(2, 3, 4) });
        return s;
    }
    [Theory]
    [InlineData("chips1", true, 1)] [InlineData("chips999999", true, 999999)]
    [InlineData("chips0", false, 0)] [InlineData("chips01", false, 0)]
    [InlineData("chips1000000", false, 0)] [InlineData("chips-2", false, 0)]
    [InlineData("chips1 ", false, 0)] [InlineData("chips", false, 0)] [InlineData("juice1", false, 0)]
    public void SavedFactoryIdentityIsCanonical(string id, bool valid, uint number)
    {
        Assert.Equal(valid, FleaListingPolicy.TryNumber(id, "chips", out uint parsed));
        if (valid) Assert.Equal(number, parsed);
    }
    [Fact]
    public void NativeSaleKeySurvivesIdenticalNamesAndNativeStringSlicing()
    {
        string first = FleaListingPolicy.Key("potato chips(itemx)", 1), second = FleaListingPolicy.Key("potato chips(itemx)", 2);
        Assert.NotEqual(first, second);
        Assert.Equal("potato chips(itemx)", first.Substring(0, first.Length - 8));
        Assert.Equal("potato chips", first.Substring(0, first.Length - 15));
        Assert.EndsWith("OW000001", first);
        Assert.Throws<ArgumentOutOfRangeException>(() => FleaListingPolicy.Key("chips", 1000000));
    }
    [Fact]
    public void AllNewPacketsRoundTripAtDocumentedLengths()
    {
        var s = State(); var bytes = PacketCodec.Encode(s); Assert.Equal(45, bytes.Length);
        Assert.True(FleaListingPolicy.Same(s, Assert.IsType<FleaListingState>(PacketCodec.Decode(bytes))));
        var request = new FleaListingIntent { PlayerId = 1, ItemId = 50, Price = 999, Sequence = 65535, Revision = 2 };
        bytes = PacketCodec.Encode(request); Assert.Equal(15, bytes.Length);
        var decoded = Assert.IsType<FleaListingIntent>(PacketCodec.Decode(bytes));
        Assert.Equal(request.Price, decoded.Price); Assert.Equal(request.ItemId, decoded.ItemId); Assert.Equal(request.Revision, decoded.Revision);
        var receipt = new FleaListingResult { PlayerId = 1, ItemId = 50, Sequence = 65535, Result = FleaListingResult.Accepted };
        bytes = PacketCodec.Encode(receipt); Assert.Equal(10, bytes.Length);
        Assert.Equal(receipt.Sequence, Assert.IsType<FleaListingResult>(PacketCodec.Decode(bytes)).Sequence);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void DuplicateNetworkOrSaveIdentitiesAreRejected(bool network)
    {
        var s = State(); s.Items.Add(new() { ItemId = network ? 50u : 51u, NativeNumber = network ? 4u : 3u });
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
    }
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void NonfinitePosesAndPricesCannotReachTheGame(float bad)
    {
        var s = State(); s.Items[0].Position.X = bad;
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
        s = State(); s.Items[0].Rotation.W = bad;
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
    }
    [Fact]
    public void BoundsAreCheckedOnBothWireDirections()
    {
        var r = new FleaListingIntent { PlayerId = 1, ItemId = 4, Price = 1000 };
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(r));
        var w = new NetWriter(); w.WriteByte(1); w.WriteUInt16(2); w.WriteUInt32(1); w.WriteUInt32(4); w.WriteUInt16(1000);
        Assert.Throws<ProtocolException>(() => new FleaListingIntent().Read(new NetReader(w.ToArray())));
        w = new NetWriter(); w.WriteUInt32(1); w.WriteByte(65);
        Assert.Throws<ProtocolException>(() => new FleaListingState().Read(new NetReader(w.ToArray())));
        r.Price = 0; Assert.True(FleaListingPolicy.Valid(r));
        r.ItemId = 0; Assert.False(FleaListingPolicy.Valid(r));
    }
    [Fact]
    public void StaleAndContradictorySnapshotsCannotRestoreASoldListing()
    {
        var before = State(uint.MaxValue); var sold = new FleaListingState { Revision = 0 };
        Assert.True(FleaListingPolicy.CanApply(before, sold)); Assert.False(FleaListingPolicy.CanApply(sold, before));
        before.Revision = 0; Assert.False(FleaListingPolicy.CanApply(sold, before));
        Assert.True(FleaListingPolicy.CanApply(sold, FleaListingPolicy.Copy(sold)));
    }
    [Theory]
    [InlineData(FleaListingResult.Accepted)] [InlineData(FleaListingResult.Claimed)]
    public void RetriesKeepTheirOriginalReceiptAndCannotRewritePrice(byte code)
    {
        var receipts = new FleaListingReceipts();
        var r = new FleaListingIntent { PlayerId = 1, ItemId = 2, Price = 5, Sequence = 65535, Revision = 3 };
        Assert.False(receipts.TryGet(r, out _)); receipts.Record(r, code);
        Assert.True(receipts.TryGet(r, out var result)); Assert.Equal(code, result);
        r.Price = 6; Assert.True(receipts.TryGet(r, out result)); Assert.Equal(FleaListingResult.Stale, result);
        r.Price = 5; Assert.True(receipts.TryGet(r, out result)); Assert.Equal(code, result);
        r.Sequence = 0; Assert.False(receipts.TryGet(r, out _)); receipts.Record(r, code);
        r.Sequence = 65535; Assert.True(receipts.TryGet(r, out result)); Assert.Equal(FleaListingResult.Stale, result);
        receipts.ForgetPlayer(1); Assert.False(receipts.TryGet(r, out _));
    }
    [Fact]
    public void SnapshotCopiesDoNotShareMutableEntries()
    {
        var old = State(); var copy = FleaListingPolicy.Copy(old); copy.Items[0].Price = 7;
        Assert.Equal(5, old.Items[0].Price); Assert.False(FleaListingPolicy.Same(old, copy));
    }
    [Fact]
    public void GuestCannotForgeListingsOrSaleReceipts()
    {
        foreach (var id in new[] { MessageId.FleaListingState, MessageId.FleaListingResult })
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
        }
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.FleaListingIntent, false, true, true, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.FleaListingIntent, true, false, false, true));
    }
}
