using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class WheelPunctureTests
{
    private static VehicleWheelHealthState Healthy() => new() { VehicleId = 42, Availability = 15,
        HealthFL = 80, HealthFR = 80, HealthRL = 80, HealthRR = 80 };
    private static WheelPunctureRequest Request(int wheel = 0, uint epoch = 1) => new() {
        VehicleId = 42, PlayerId = 1, Wheel = (byte)wheel, Epoch = epoch, Sequence = 7 };

    [Fact]
    public void RequestAndEpochsHaveExactWireLayoutAndRejectTruncationAndTrailingData()
    {
        var request = Request(); var bytes = PacketCodec.Encode(request); Assert.Equal(14, bytes.Length);
        var read = Assert.IsType<WheelPunctureRequest>(PacketCodec.Decode(bytes));
        Assert.Equal(request.Epoch, read.Epoch); Assert.Equal(request.Wheel, read.Wheel); Assert.Equal(request.Sequence, read.Sequence);
        var state = new VehicleWheelHealthPublication().Observe(Healthy());
        for (int i = 0; i < 4; i++) state.SetEpoch(i, (uint)(i + 20));
        var packet = PacketCodec.Encode(state); Assert.Equal(43, packet.Length);
        for (int i = 0; i < 4; i++) Assert.Equal((uint)(i + 20), BitConverter.ToUInt32(packet, 27 + 4 * i));
        Assert.True(state.SameState(Assert.IsType<VehicleWheelHealthState>(PacketCodec.Decode(packet))));
        foreach (var wire in new[] { bytes, packet })
        {
            for (int n = 2; n < wire.Length; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Take(n).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Concat(new byte[1]).ToArray()));
        }
    }

    [Theory]
    [InlineData("vehicle")][InlineData("host")][InlineData("player")][InlineData("wheel")][InlineData("epoch")]
    public void InvalidPunctureRequestsCannotBeEncoded(string fault)
    {
        var request = Request();
        switch (fault) { case "vehicle": request.VehicleId = 0; break; case "host": request.PlayerId = 0; break;
            case "player": request.PlayerId = 255; break; case "wheel": request.Wheel = 4; break; case "epoch": request.Epoch = 0; break; }
        Assert.False(request.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
    }

    [Fact]
    public void IndependentTyresAcceptConcurrentPuncturesAndOrdinaryWearDoesNotRetireRequests()
    {
        var publication = new VehicleWheelHealthPublication(); var native = Healthy(); var state = publication.Observe(native);
        Assert.True(Request().Matches(state)); Assert.True(Request(1).Matches(state));
        native.HealthFL = 79.9f; state = publication.Observe(native); Assert.True(Request().Matches(state));
        native.HealthFL = 0; state = publication.Observe(native);
        Assert.False(Request().Matches(state)); Assert.True(Request(1).Matches(state));
        native.HealthFR = 0; state = publication.Observe(native); Assert.False(Request(1).Matches(state));
    }

    [Fact]
    public void RepairsReplacementWithdrawalAndRestoreRetireOldTyreLives()
    {
        var publication = new VehicleWheelHealthPublication(); var native = Healthy(); publication.Observe(native);
        native.HealthFL = 81; var repaired = publication.Observe(native); Assert.False(Request().Matches(repaired));
        uint old = repaired.EpochFL; var replaced = publication.Observe(native, 1);
        Assert.False(Request(0, old).Matches(replaced)); Assert.Equal(old + 1, replaced.EpochFL);
        old = replaced.EpochFL; native.Availability = 14; native.HealthFL = 0; var missing = publication.Observe(native);
        Assert.False(Request(0, old).Matches(missing));
        native.Availability = 15; native.HealthFL = 81; var restored = publication.Observe(native);
        Assert.False(Request(0, old).Matches(restored)); Assert.True(Request(0, restored.EpochFL).Matches(restored));
        Assert.True(publication.NeedsBroadcast); publication.MarkBroadcast(restored.Revision); Assert.False(publication.NeedsBroadcast);
    }

    [Fact]
    public void EpochOnlyChangesArePublishedAndConflictingEqualRevisionsAreRefused()
    {
        var publication = new VehicleWheelHealthPublication(); var a = publication.Observe(Healthy()); publication.MarkBroadcast(a.Revision);
        var b = publication.Observe(Healthy(), 2); Assert.True(a.SameHealth(b)); Assert.False(a.SameState(b));
        Assert.Equal(a.Revision + 1, b.Revision); Assert.True(publication.NeedsBroadcast);
        Assert.True(b.SameState(publication.Observe(Healthy())));
        var replica = new VehicleWheelHealthReplica(); Assert.True(replica.Receive(a));
        b.Revision = a.Revision; Assert.False(replica.Receive(b));
        b.Revision++; Assert.True(replica.Receive(b)); replica.Get()!.EpochFR = 999; Assert.Equal(b.EpochFR, replica.Get()!.EpochFR);
    }

    [Fact]
    public void OnlyAuthenticatedGuestCallbacksReachTheHostOnReliableEvents()
    {
        Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.WheelPunctureRequest, true, true, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WheelPunctureRequest, true, false, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WheelPunctureRequest, false, true, true, true));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.WheelPunctureRequest, Channel.UnreliableSequenced));
        var callbacks = new VehicleCallbackPolicy(4, 1);
        Assert.False(callbacks.Receive(42, 1, 7, 2, 1, false, 1));
        Assert.False(callbacks.Receive(42, 1, 7, 1, 2, false, 1));
        Assert.False(callbacks.Receive(42, 1, 7, 1, 1, true, 1));
        Assert.True(callbacks.Receive(42, 1, 7, 1, 1, false, 1));
        Assert.False(callbacks.Receive(42, 1, 7, 1, 1, false, 1));
        callbacks.ForgetPlayer(1); Assert.True(callbacks.Receive(42, 1, 1, 1, 1, false, 2));
    }
}
