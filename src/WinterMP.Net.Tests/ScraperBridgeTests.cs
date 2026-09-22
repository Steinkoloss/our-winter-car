using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class ScraperBridgeTests
    {
        public static IEnumerable<object[]> PaneBindingKeys()
        { foreach (string key in PaneScrapeData.Keys) yield return new object[] { key }; }

        [Theory]
        [MemberData(nameof(PaneBindingKeys))]
        public void CatalogCannotRedirectAnyAuditedPaneBinding(string key)
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            Assert.NotNull(SyncCatalogJson.Parse(json.ToJsonString()).PaneScrape);
            json["paneScrape"]![key] = "unaudited";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void PickupMustPrecedeEquipAndCannotStealOrReplayDeniedAttempt()
        {
            var lease = new ScraperLease(42, 77);
            Assert.False(lease.Apply(2, Request(1, ScraperOperation.Equip), true, true, 1));
            Assert.False(lease.Apply(2, Request(1, ScraperOperation.Pickup), true, true, 1));
            Assert.True(lease.Apply(2, Request(2, ScraperOperation.Pickup), true, true, 1));
            Assert.False(lease.Equipped);
            Assert.False(lease.Apply(3, Request(3, ScraperOperation.Pickup, 3), true, true, 1));
            Assert.True(lease.Apply(2, Request(3, ScraperOperation.Equip), true, false, 1.1f));
            Assert.True(lease.Equipped);
            Assert.Equal((byte)2, lease.Holder);
            lease.Revoke(2);
            Assert.False(lease.Equipped);
            Assert.Equal(3u, lease.Seen(2)); // reconnect must allocate above all denied/accepted operations
            Assert.False(lease.Apply(2, Request(4, ScraperOperation.Equip), true, true, 2));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void PickupRequiresLivePoseAndHostToolContact(bool alive, bool contact)
        {
            var lease = new ScraperLease(42, 77);
            Assert.False(lease.Apply(2, Request(1, ScraperOperation.Pickup), alive, contact, 1));
            Assert.Equal((byte)255, lease.Holder);
            Assert.Equal(1u, lease.Seen(2));
        }

        [Fact]
        public void ActorEpochAndToolCannotBeSpoofedAndLeaseExpires()
        {
            var lease = new ScraperLease(42, 77);
            Assert.False(lease.Apply(3, Request(900, ScraperOperation.Pickup), true, true, 1));
            var wrong = Request(900, ScraperOperation.Pickup); wrong.Epoch++;
            Assert.False(lease.Apply(2, wrong, true, true, 1));
            Assert.Equal(0u, lease.Seen(2));
            wrong = Request(1, ScraperOperation.Pickup); wrong.ToolId++;
            Assert.False(lease.Apply(2, wrong, true, true, 1));
            Assert.True(lease.Apply(2, Request(2, ScraperOperation.Pickup), true, true, 1));
            lease.Expire(2);
            Assert.Equal((byte)255, lease.Holder);
        }

        [Fact]
        public void WireRoundTripAndAdmissionAreDirectionalOrderedOnly()
        {
            var request = Request(9, ScraperOperation.Stroke);
            request.Eye = new NetVector3(1, 2, 3); request.Direction = new NetVector3(0, 0, 1);
            var copy = Assert.IsType<ScraperAction>(PacketCodec.Decode(PacketCodec.Encode(request)));
            Assert.Equal(9u, copy.Sequence); Assert.Equal(request.Eye.Y, copy.Eye.Y);
            Assert.Equal(ScraperOperation.Stroke, copy.Operation);
            Assert.True(SessionMessagePolicy.IsSenderAllowed(request.Id, true, true, false, false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(request.Id, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.PaneScrapeUpdate, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(request.Id, Channel.UnreliableSequenced));
            var update = new PaneScrapeUpdate { Epoch = 42, VehicleId = 9, Revision = 4, Cutoff = .255f,
                Actor = 2, Sequence = 9, Status = PaneScrapeStatus.Accepted, IsDecision = true,
                ToolId = 77, Holder = 2, Equipped = true, HighWater = 12 };
            var result = Assert.IsType<PaneScrapeUpdate>(PacketCodec.Decode(PacketCodec.Encode(update)));
            Assert.Equal(update.Cutoff, result.Cutoff); Assert.Equal(12u, result.HighWater);
            var replica = new PaneScrapeReplica(9, 42, 2);
            Assert.True(replica.ReceiveDecision(true, result.Decision(), out bool effect));
            Assert.True(effect);
            Assert.True(replica.ReceiveDecision(true, result.Decision(), out effect));
            Assert.False(effect);
        }

        [Fact]
        public void RejoiningReplicaUsesTargetedAdmissionHighWaterWithoutReplayingOldPersonalEffects()
        {
            var replica = new PaneScrapeReplica(9, 42, 2);
            replica.ResumePersonalEffectsAfter(12);
            var old = new PaneScrapeUpdate { Epoch = 42, VehicleId = 9, Revision = 4, Cutoff = .255f,
                Actor = 2, Sequence = 9, Status = PaneScrapeStatus.Accepted, IsDecision = true };
            Assert.True(replica.ReceiveDecision(true, old.Decision(), out bool effect));
            Assert.False(effect);
            old.Sequence = 13; old.Revision++;
            Assert.True(replica.ReceiveDecision(true, old.Decision(), out effect));
            Assert.True(effect);
        }

        [Fact]
        public void OffRemovesStrokePermissionAndHeartbeatCannotEquipOrAcquire()
        {
            var lease = new ScraperLease(42, 77);
            Assert.False(lease.Apply(2, Request(1, ScraperOperation.KeepAlive), true, true, 1));
            Assert.True(lease.Apply(2, Request(2, ScraperOperation.Pickup), true, true, 1));
            Assert.True(lease.Apply(2, Request(3, ScraperOperation.Equip), true, false, 1.1f));
            Assert.True(lease.Apply(2, Request(4, ScraperOperation.Off), true, false, 1.2f));
            Assert.False(lease.Apply(2, Request(5, ScraperOperation.Stroke), true, false, 1.3f));
            Assert.False(lease.Equipped);
            Assert.True(lease.Apply(2, Request(6, ScraperOperation.KeepAlive), true, false, 1.4f));
            Assert.False(lease.Equipped);
            Assert.False(lease.Apply(2, Request(7, ScraperOperation.Equip), false, false, 1.5f));
            Assert.Equal((byte)255, lease.Holder);
        }

        private static ScraperAction Request(uint sequence, ScraperOperation operation, byte actor = 2)
            => new ScraperAction { Epoch = 42, ToolId = 77, VehicleId = 9, Pane = 1,
                Actor = actor, Sequence = sequence, Operation = operation };
    }
}
