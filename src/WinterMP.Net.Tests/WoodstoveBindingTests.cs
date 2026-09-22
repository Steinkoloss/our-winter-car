using System;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class WoodstoveBindingTests
    {
        private sealed class Native : IWoodstoveFuelHost, IWoodstoveDeferredFuelHost
        {
            public int Fuel = 1, Cache = 0, Calls;
            public bool Destroyed, Expired, WrongDelta;
            public WoodstoveFeedContext Context = new WoodstoveFeedContext {
                ActorPresent = true, ActorAlive = true, SourceReady = true, ResourceId = 51,
                ResourceAvailable = true, ResourceAvailableToActor = true, ResourceIsFirewood = true,
                ResourceAtSource = true, EquipmentReady = true };
            public WoodstoveFuelValues ReadFuel() => new WoodstoveFuelValues(Fuel, .18f, false);
            public WoodstoveFeedContext Observe(byte actor, uint id) => Context;
            public void FeedOne(uint id) { Calls++; Fuel += WrongDelta ? 2 : 1; }
            public bool IsConsumed(uint id) => Destroyed;
            public bool CompletionExpired => Expired;
        }
        private static WoodstoveFeedRequest Feed(byte actor, uint seq = 1) =>
            new WoodstoveFeedRequest(WoodstoveFuelAuthority.CabinSourceId, 7, actor, seq, 51);

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void BusyAttemptIsRetainedAcrossCompletionAndRejoin(bool spoofed)
        {
            var native = new Native(); var authority = new WoodstoveFuelAuthority(7, native);
            Assert.Equal(WoodstoveFeedStatus.Pending, authority.Decide(2, Feed(2)).Status);
            var attempt = new WoodstoveFeedRequest(WoodstoveFuelAuthority.CabinSourceId, 7, 2, 8, 52);
            Assert.Equal(WoodstoveFeedStatus.Busy, authority.Decide(spoofed ? (byte)3 : (byte)2, attempt).Status);
            Assert.Equal(spoofed ? 1u : 8u, authority.Seen(2));
            var stale = new WoodstoveFeedRequest(WoodstoveFuelAuthority.CabinSourceId, 6, 2, uint.MaxValue, 52);
            Assert.Equal(WoodstoveFeedStatus.Busy, authority.Decide(2, stale).Status);
            Assert.Equal(spoofed ? 1u : 8u, authority.Seen(2));
            native.Destroyed = true;
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Poll()!.Status);
            native.Context.ResourceId = 52;
            if (!spoofed)
            {
                Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, authority.Decide(2, attempt).Status);
                Assert.Equal(1, native.Calls);
            }
            var client = new WoodstoveFuelClient(2);
            Assert.True(client.Receive(true, new WoodstoveFuelUpdate { Actor = 2, HighWater = authority.Seen(2),
                Snapshot = authority.Capture(), ResourceId = 52, Shape = 1, Rotation = NetQuaternion.Identity }));
            var fresh = client.Create(52)!;
            Assert.Equal(spoofed ? 2u : 9u, fresh.Sequence);
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Decide(2, fresh.Request()).Status);
            Assert.Equal(2, native.Calls);
        }

        [Theory]
        [InlineData((byte)0)] [InlineData((byte)2)]
        public void BothActorsReserveOnceAwaitDestructionAndReceiveMatchingAbsoluteResult(byte actor)
        {
            var native = new Native(); var authority = new WoodstoveFuelAuthority(7, native);
            var pending = authority.Decide(actor, Feed(actor));
            Assert.Equal(WoodstoveFeedStatus.Pending, pending.Status);
            Assert.Null(pending.Snapshot); Assert.Equal(1, native.Calls); Assert.Equal(2, native.Fuel);
            Assert.Equal(0, native.Cache); // canonical WoodTrigger, never SetFire cache
            Assert.Equal(WoodstoveFeedStatus.Busy, authority.Decide(actor, Feed(actor, 2)).Status);
            Assert.Null(authority.Poll()); Assert.Throws<InvalidOperationException>(() => authority.Capture());
            native.Destroyed = true;
            var result = authority.Poll()!;
            Assert.Equal(WoodstoveFeedStatus.Accepted, result.Status);
            Assert.Equal(actor, result.Actor); Assert.Equal(1u, result.Sequence);
            Assert.Equal(2, result.Snapshot!.Values.Fuel); Assert.Equal(new uint[] { 51 }, result.Snapshot.ConsumedResources);
            var guest = new WoodstoveFuelReplica(7); Assert.True(guest.Receive(true, result.Snapshot));
            Assert.Equal(native.Fuel, guest.Current!.Values.Fuel); Assert.Null(authority.Poll());
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, authority.Decide(actor, Feed(actor)).Status);
            Assert.Equal(1, native.Calls);
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void DeferredTimeoutOrPartialMutationNeverSucceedsOrRetries(bool partial)
        {
            var native = new Native { WrongDelta = partial }; var a = new WoodstoveFuelAuthority(7, native);
            var result = a.Decide(2, Feed(2));
            if (!partial) { native.Expired = true; result = a.Poll()!; }
            Assert.Equal(WoodstoveFeedStatus.NativeFailure, result.Status); Assert.Null(result.Snapshot);
            native.Destroyed = true;
            Assert.Null(a.Poll()); Assert.Equal(WoodstoveFeedStatus.NativeFailure, a.Decide(2, Feed(2, 2)).Status);
            Assert.Equal(1, native.Calls);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void AuditedParentTagCapacityAndWrongColliderRejectWithoutAdapter(int kind)
        {
            var native = new Native(); var a = new WoodstoveFuelAuthority(7, native);
            if (kind == 0) native.Context.ResourceParented = true;
            if (kind == 1) native.Context.ResourceTagIsPart = false;
            if (kind == 2) native.Fuel = 4;
            if (kind == 3) native.Context.ResourceId = 52;
            Assert.NotEqual(WoodstoveFeedStatus.Accepted, a.Decide(2, Feed(2)).Status);
            Assert.Equal(0, native.Calls);
        }

        [Fact]
        public void DepletionDuringDeferredRetirementIsObservedNotRestored()
        {
            var native = new Native(); var a = new WoodstoveFuelAuthority(7, native);
            a.Decide(2, Feed(2)); native.Fuel = 1; native.Destroyed = true;
            var final = a.Poll()!;
            Assert.Equal(WoodstoveFeedStatus.Accepted, final.Status);
            Assert.Equal(1, final.Snapshot!.Values.Fuel); Assert.Equal(1, native.Calls);
        }

        [Fact]
        public void OverCapacitySnapshotIsNotAValidCanonicalStove()
        {
            var snapshot = new WoodstoveFuelSnapshot(WoodstoveFuelAuthority.CabinSourceId, 7, 1,
                new WoodstoveFuelValues(5, .18f, false), new uint[0]);
            Assert.False(new WoodstoveFuelReplica(7).Receive(true, snapshot));
        }

        [Theory]
        [InlineData((byte)0)] [InlineData((byte)2)]
        public void DecodedIntentAndResultUseProductionAdmissionAndTheSameAuthority(byte actor)
        {
            var native = new Native(); var authority = new WoodstoveFuelAuthority(7, native);
            var client = new WoodstoveFuelClient(actor);
            Assert.True(client.Receive(true, new WoodstoveFuelUpdate { Actor = actor, Snapshot = authority.Capture(),
                ResourceId = 51, Shape = 1, Rotation = NetQuaternion.Identity }));
            var request = client.Create(51)!;
            var decoded = Assert.IsType<WoodstoveFeedIntent>(PacketCodec.Decode(PacketCodec.Encode(request)));
            Assert.Equal(WoodstoveFeedStatus.Pending, authority.Decide(actor, decoded.Request()).Status);
            Assert.Null(client.Create(51)); Assert.Equal(1, client.Current!.Values.Fuel);
            native.Destroyed = true; var decision = authority.Poll()!;
            var wire = new WoodstoveFuelUpdate { Actor = actor, IsDecision = true, Sequence = decision.Sequence,
                HighWater = decision.HighWater, Status = decision.Status, Snapshot = decision.Snapshot };
            Assert.True(client.Receive(true, Assert.IsType<WoodstoveFuelUpdate>(PacketCodec.Decode(PacketCodec.Encode(wire)))));
            Assert.Equal(1, native.Calls); Assert.Equal(native.Fuel, client.Current!.Values.Fuel);
            Assert.Equal(request.Sequence, client.LastDecision!.Sequence); Assert.Null(client.Create(51));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void MalformedResultNeverReachesGameState(int kind)
        {
            var update = new WoodstoveFuelUpdate { Epoch = 7, ResourceId = 51, Shape = 1,
                Rotation = NetQuaternion.Identity };
            if (kind == 0) update.Epoch = 0;
            if (kind == 1) update.Shape = 3;
            if (kind == 2) update.Position = new NetVector3(float.NaN, 0, 0);
            if (kind == 3) { update.IsDecision = true; update.Status = WoodstoveFeedStatus.Accepted; }
            if (kind == 4) update.Status = (WoodstoveFeedStatus)255;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(update));
        }

        [Fact]
        public void CatalogBindsExactlyAuditedSourceAndFactory()
        {
            var data = WinterMP.Core.Catalog.SyncCatalogJson.Parse(System.IO.File.ReadAllText("sync-catalog.json"));
            Assert.NotNull(data.WoodstoveFuel);
            Assert.Equal(WoodstoveFuelAuthority.CabinPath, data.WoodstoveFuel!["source"]);
            Assert.Equal("Create log", data.WoodstoveFuel["createState"]);
            Assert.Equal(14, (int)WoodstoveFeedStatus.Pending);
        }

        [Fact]
        public void WireAdmissionCarriesEpochHighWaterAndRetirementAndAuthenticatesDirection()
        {
            var snapshot = new WoodstoveFuelSnapshot(WoodstoveFuelAuthority.CabinSourceId, 7, 2,
                new WoodstoveFuelValues(2, .18f, false), new uint[] { 51 });
            var update = new WoodstoveFuelUpdate { Snapshot = snapshot, Actor = 2, HighWater = 9,
                ResourceId = 51, Shape = 1, Position = new NetVector3(1, 2, 3), Rotation = new NetQuaternion(0, 0, 0, 1) };
            var copy = Assert.IsType<WoodstoveFuelUpdate>(PacketCodec.Decode(PacketCodec.Encode(update)));
            Assert.Equal(7u, copy.Epoch); Assert.Equal(9u, copy.HighWater);
            Assert.Equal(new uint[] { 51 }, copy.Snapshot!.ConsumedResources);
            var replica = new WoodstoveFuelReplica(copy.Epoch); Assert.True(replica.Receive(true, copy.Snapshot));
            Assert.False(replica.Receive(true, new WoodstoveFuelSnapshot(snapshot.SourceId, 7, 3, snapshot.Values, new uint[0])));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WoodstoveFuelUpdate, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WoodstoveFeedIntent, true, false, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.WoodstoveFeedIntent, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.WoodstoveFeedIntent, Channel.UnreliableSequenced));
            var intent = new WoodstoveFeedIntent { SourceId = snapshot.SourceId, Epoch = 7, Actor = 2, Sequence = 10, ResourceId = 51 };
            var request = Assert.IsType<WoodstoveFeedIntent>(PacketCodec.Decode(PacketCodec.Encode(intent))).Request();
            Assert.Equal(10u, request.Sequence); Assert.Equal(51u, request.ResourceId);
        }

        [Fact]
        public void SenderAndChannelMatrixOnlyAdmitsAuthenticatedGuestAndSelectedReadyHost()
        {
            for (int flags = 0; flags < 16; flags++)
            {
                bool host = (flags & 1) != 0, authenticated = (flags & 2) != 0;
                bool selected = (flags & 4) != 0, ready = (flags & 8) != 0;
                Assert.Equal(host && authenticated, SessionMessagePolicy.IsSenderAllowed(
                    MessageId.WoodstoveFeedIntent, host, authenticated, selected, ready));
                Assert.Equal(!host && selected && ready, SessionMessagePolicy.IsSenderAllowed(
                    MessageId.WoodstoveFuelUpdate, host, authenticated, selected, ready));
            }
            foreach (var id in new[] { MessageId.WoodstoveFeedIntent, MessageId.WoodstoveFuelUpdate })
                foreach (var channel in new[] { Channel.ReliableOrdered, Channel.UnreliableSequenced, Channel.ReliableBulk, (Channel)255 })
                    Assert.Equal(channel == Channel.ReliableOrdered, SessionMessagePolicy.IsChannelAllowed(id, channel));
        }

        [Theory]
        [InlineData("COTTAGE/Stuff/Fireplace")]
        [InlineData("COTTAGE/Stuff/Sauna/Stove")]
        [InlineData("COTTAGE/Stuff/Grill/Fireplace")]
        public void CatalogCannotExtendFixedCabinSeamToOtherHeatSources(string source)
        {
            string json = System.IO.File.ReadAllText("sync-catalog.json").Replace(
                "\"source\": \"" + WoodstoveFuelAuthority.CabinPath + "\"", "\"source\": \"" + source + "\"");
            Assert.Throws<FormatException>(() => WinterMP.Core.Catalog.SyncCatalogJson.Parse(json));
        }
    }
}
