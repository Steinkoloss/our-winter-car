using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class FirewoodLoadTests
{
    private static FirewoodLoadState Load() => new() { Revision = 4, Epoch = 3, Logs = 400, Firewood = 300, Mass = 600,
        BedScale = .25f, Unloaded = 100, Unloading = true,
        Piles = new[] { new FirewoodPile { Position = new NetVector3(1, 2, 3), Scale = .25f } } };

    [Fact]
    public void LoadAndGroundManifestRoundTripWithoutTruncationOrTrailingData()
    {
        var load = Load(); var wire = PacketCodec.Encode(load);
        Assert.Equal(64, wire.Length);
        var copy = Assert.IsType<FirewoodLoadState>(PacketCodec.Decode(wire));
        Assert.Equal(load.Revision, copy.Revision); Assert.True(FirewoodLoadPolicy.Same(load, copy));
        for (int n = 2; n < wire.Length; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Take(n).ToArray()));
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Concat(new byte[1]).ToArray()));
        wire[30] = 2; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
    }

    [Theory]
    [InlineData("logs")][InlineData("wood")][InlineData("mass")][InlineData("scale")][InlineData("unloaded")]
    [InlineData("position")][InlineData("rotation")][InlineData("pile-scale")][InlineData("pile-null")][InlineData("count")]
    public void InvalidNativeOrRemoteDataCannotBeEncoded(string fault)
    {
        var s = Load();
        switch (fault)
        {
            case "logs": s.Logs = 1601; break;
            case "wood": s.Firewood = float.PositiveInfinity; break;
            case "mass": s.Mass = 0; break;
            case "scale": s.BedScale = -1; break;
            case "unloaded": s.Unloaded = float.NaN; break;
            case "position": s.Piles[0].Position = new NetVector3(float.NaN, 0, 0); break;
            case "rotation": s.Piles[0].Rotation = new NetQuaternion(); break;
            case "pile-scale": s.Piles[0].Scale = 1.01f; break;
            case "pile-null": s.Piles[0] = null!; break;
            case "count": s.Piles = new FirewoodPile[129]; break;
        }
        Assert.False(FirewoodLoadPolicy.Valid(s)); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
    }

    [Fact]
    public void PendingStateOwnsPilesAndDetectsEachProgressChange()
    {
        var source = Load(); var copy = FirewoodLoadPolicy.Copy(source);
        source.Piles[0].Scale = .75f; Assert.Equal(.25f, copy.Piles[0].Scale);
        Assert.False(FirewoodLoadPolicy.Same(copy, source));
        foreach (string field in new[] { "Logs", "Firewood", "Mass", "BedScale", "Unloaded" })
        {
            var changed = Load(); var f = typeof(FirewoodLoadState).GetField(field)!; f.SetValue(changed, (float)f.GetValue(changed)! + .1f);
            Assert.False(FirewoodLoadPolicy.Same(copy, changed));
        }
        var next = Load(); next.Epoch++; Assert.False(FirewoodLoadPolicy.Same(copy, next));
        next = Load(); next.Unloading = false; Assert.False(FirewoodLoadPolicy.Same(copy, next));
        Assert.True(FirewoodLoadPolicy.Newer(0, uint.MaxValue)); Assert.False(FirewoodLoadPolicy.Newer(uint.MaxValue, 0));
        Assert.False(FirewoodLoadPolicy.Newer(3, 3)); Assert.False(FirewoodLoadPolicy.Newer(0x80000003u, 3));
    }

    [Theory]
    [InlineData("valid", true)][InlineData("stale-load", false)][InlineData("empty", false)][InlineData("duplicate", false)]
    [InlineData("inactive", false)][InlineData("stale-or-dead", false)][InlineData("distant", false)][InlineData("nan", false)]
    [InlineData("invalid-player", false)][InlineData("cancel", true)]
    public void UnloadRequestsCannotInventLoadsOrReplayFinishedDeliveries(string fault, bool expected)
    {
        var r = new FirewoodUnloadIntent { PlayerId = 1, Epoch = 3, Sequence = 1, Unload = fault != "cancel" };
        uint epoch = fault == "stale-load" ? 4u : 3u;
        if (fault == "invalid-player") r.PlayerId = 255;
        Assert.Equal(expected, FirewoodLoadPolicy.CanUnload(r, epoch, fault == "empty" ? 0 : 400,
            fault == "duplicate" || fault == "cancel", fault != "inactive", fault != "stale-or-dead",
            fault == "nan" ? float.NaN : fault == "distant" ? 145 : 144));
    }

    [Fact]
    public void IntentWireAndSenderChannelAdmissionAreBoundToTheirRoles()
    {
        var r = new FirewoodUnloadIntent { PlayerId = 2, Epoch = 3, Sequence = uint.MaxValue, Unload = true };
        var bytes = PacketCodec.Encode(r); Assert.Equal(12, bytes.Length);
        var read = Assert.IsType<FirewoodUnloadIntent>(PacketCodec.Decode(bytes));
        Assert.Equal(r.Sequence, read.Sequence); Assert.Equal(r.Epoch, read.Epoch); Assert.True(read.Unload);
        bytes[11] = 2; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodLoadState, true, true, false, true));
        Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodLoadState, false, true, true, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodLoadState, false, true, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodUnloadIntent, false, true, true, true));
        Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodUnloadIntent, true, true, false, true));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.FirewoodUnloadIntent, Channel.UnreliableSequenced));
    }

    [Fact]
    public void DeliveryCatalogRequiresEveryBindingWithoutDisablingOtherFeatures()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        var parsed = WinterMP.Core.Catalog.SyncCatalogJson.Parse(json.ToJsonString());
        Assert.NotNull(parsed.FirewoodDelivery); Assert.Null(parsed.FirewoodDeliveryError);
        foreach (string key in WinterMP.Core.Catalog.FirewoodDeliveryData.Fields)
        {
            var broken = json.DeepClone(); broken["firewoodDelivery"]!.AsObject().Remove(key);
            parsed = WinterMP.Core.Catalog.SyncCatalogJson.Parse(broken.ToJsonString());
            Assert.Null(parsed.FirewoodDelivery); Assert.NotNull(parsed.FirewoodDeliveryError); Assert.NotNull(parsed.FirewoodBuyers);
        }
    }

    [Fact]
    public void FirewoodKeepsSignedEarlyBonusWhileOtherSitesClampNegativeValues()
    {
        Assert.Equal(-100, FirewoodLoadPolicy.SiteSecondary(JobSiteState.KindFirewood, -100));
        Assert.Equal(20, FirewoodLoadPolicy.SiteSecondary(JobSiteState.KindFirewood, 20));
        Assert.Equal(0, FirewoodLoadPolicy.SiteSecondary(JobSiteState.KindSewage, -100));
    }
}
