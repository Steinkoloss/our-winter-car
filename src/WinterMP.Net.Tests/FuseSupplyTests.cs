using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class FuseSupplyTests
    {
        private static uint Factory => FactoryItemIdentity.FactoryId("Spawner/CreateItems", "Fuse");
        private static SupplyItemState Fuse(string id = "fuse01") => new() { FactoryId = Factory, NativeId = id, Rotation = NetQuaternion.Identity };
        private static SupplyItemReplica Replica(ItemSpawnLifecycle? lifecycle = null) => new(new Dictionary<uint, string> { { Factory, "fuse0" } }, lifecycle ?? new());
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Box(JsonNode root) => root["partsPackages"]!["factories"]!.AsArray().Single(f => f!["fsm"]!.GetValue<string>() == "FusePackage")!;

        [Fact]
        public void FiveNativeFusesHaveSeparateStableIdentitiesAndReplayDoesNotReopenBox()
        {
            var catalog = SyncCatalogJson.Parse(Catalog().ToJsonString());
            var rule = Assert.Single(catalog.PartsPackages!.Factories, f => f.Fsm == "FusePackage");
            Assert.Equal("Destroy", rule.SupplyContents!.RetirementVariable);
            Assert.Equal("FusePackage", rule.Fsm); Assert.Equal("Create Fuse", rule.OpenState); Assert.Equal(5, rule.Capacity);
            Assert.Equal(2, rule.LoadClampIndex); Assert.True(rule.FixedCapacity);
            Assert.Equal("fuse0", rule.SupplyContents!.Prefix); Assert.Equal("State 5", rule.SupplyContents.ReadyState);
            var box = new PackageState { FactoryId = FactoryItemIdentity.FactoryId(rule.Path, rule.Fsm), NativeId = "fusepackage03",
                Quantity = 5, Revision = 1, Rotation = NetQuaternion.Identity };
            uint boxId = FactoryItemIdentity.ItemId(box.FactoryId, box.NativeId);
            var ledger = new PackageOpenLedger(); var outputs = Replica(); var ids = new HashSet<uint>();
            for (uint i = 1; i <= 5; i++)
            {
                var request = new PackageOpenRequest { PlayerId = 1, Token = 123, Sequence = i, ItemId = boxId, ExpectedRevision = box.Revision };
                Assert.Equal(PackageOpenStatus.Pending, PackageOpenLedger.Check(request, box, true, true, true));
                ledger.Begin(request, 1, PackageOpenStatus.Pending);
                var wire = PacketCodec.Encode(Fuse("fuse0" + i));
                var state = Assert.IsType<SupplyItemState>(PacketCodec.Decode(wire));
                Assert.Equal(wire, PacketCodec.Encode(state)); Assert.True(outputs.Receive(state, out uint id));
                Assert.NotEqual(boxId, id); Assert.True(ids.Add(id)); ledger.Complete(request, true, id);
                box.Quantity--; box.Revision++;
                Assert.Equal(id, ledger.Inspect(request, 1, out bool begin)!.ProducedItemId); Assert.False(begin);
                Assert.True(outputs.Receive(state, out uint replay)); Assert.Equal(id, replay);
            }
            var late = new PackageReplica(catalog.PartsPackages.Identities, new()); Assert.True(late.Receive(box, out _));
            Assert.Equal(0, late.Get(boxId)!.Quantity); Assert.Equal(5, ids.Count);
            Assert.Equal(PackageOpenStatus.Empty, PackageOpenLedger.Check(new() { ItemId = boxId, ExpectedRevision = box.Revision }, box, true, true, true));
        }

        [Theory]
        [InlineData("fuse01")] [InlineData("fuse01234")] [InlineData("fuse02147483647")]
        public void ColdSnapshotRetainsTheNativeIdentity(string nativeId)
        {
            Assert.True(Replica().Receive(Fuse(nativeId), out uint before));
            Assert.True(Replica().Receive(Fuse(nativeId), out uint after)); Assert.Equal(before, after);
        }

        [Theory]
        [InlineData("")] [InlineData("fuse0")] [InlineData("fuse00")] [InlineData("fuse001")]
        [InlineData("fuse0-1")] [InlineData("fuse0+1")] [InlineData("fuse02147483648")]
        [InlineData("fusepackage01")] [InlineData("fuse01\n")]
        public void InvalidNativeIdentityIsRejected(string id) => Assert.False(Replica().Receive(Fuse(id), out _));

        [Fact]
        public void RetirementBeforeMaterializationOrReplayNeverResurrectsAFuse()
        {
            var lifecycle = new ItemSpawnLifecycle(); var replica = Replica(lifecycle); var state = Fuse();
            uint id = FactoryItemIdentity.ItemId(Factory, state.NativeId);
            lifecycle.Retire(id); Assert.False(replica.Receive(state, out _)); Assert.Null(replica.Get(id));
            var second = Fuse("fuse02"); Assert.True(replica.Receive(second, out uint other));
            lifecycle.Retire(other); Assert.Null(replica.Get(other)); Assert.False(replica.Receive(second, out _));
        }

        [Fact]
        public void SnapshotsCannotAliasMutableCallerStateOrUnknownFactories()
        {
            var state = Fuse(); var replica = Replica(); Assert.True(replica.Receive(state, out uint id));
            state.NativeId = "fuse09"; Assert.Equal("fuse01", replica.Get(id)!.NativeId);
            replica.Get(id)!.NativeId = "fuse08"; Assert.Equal("fuse01", replica.Get(id)!.NativeId);
            state.FactoryId++; Assert.False(replica.Receive(state, out _));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonFiniteCreationPosesAreRejected(float value)
        {
            var state = Fuse(); state.Position.X = value; Assert.False(Replica().Receive(state, out _));
            state = Fuse(); state.Rotation.W = value; Assert.False(Replica().Receive(state, out _));
        }

        [Fact]
        public void InvalidQuaternionIsRejected() { var state = Fuse(); state.Rotation = default; Assert.False(Replica().Receive(state, out _)); }

        [Theory]
        [InlineData("prefix")] [InlineData("same prefix")] [InlineData("capacity")]
        [InlineData("open state")] [InlineData("clamp")]
        public void InvalidNativeSupplyProfilesAreRejected(string fault)
        {
            var catalog = Catalog(); var box = Box(catalog);
            if (fault == "prefix") box["supplyContents"]!["prefix"] = "";
            if (fault == "same prefix") box["supplyContents"]!["prefix"] = "fusepackage0";
            if (fault == "capacity") box["fixedCapacity"] = false;
            if (fault == "open state") box["openState"] = " ";
            if (fault == "clamp") box["loadClampIndex"] = 3;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(catalog.ToJsonString()));
        }

        [Fact]
        public void OnlyAuthenticatedHostCanPublishSupplyCreationOnReliableEvents()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.SupplyItemState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.SupplyItemState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.SupplyItemState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.SupplyItemState, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.SupplyItemState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.SupplyItemState, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.SupplyItemState, Channel.ReliableBulk));
        }

        [Fact]
        public void FourR20CellsUsePersistentIdentitiesAndTheConsumedDeletionFlag()
        {
            var catalog = SyncCatalogJson.Parse(Catalog().ToJsonString()).PartsPackages!;
            var box = Assert.Single(catalog.Factories, f => f.Fsm == "R20BatteryBox");
            var cells = box.SupplyContents!;
            Assert.Equal(4, box.Capacity); Assert.Equal(1, box.LoadClampIndex);
            Assert.True(box.FixedCapacity); Assert.Equal("Create Plug", box.OpenState);
            Assert.Equal("Consumed", cells.RetirementVariable); Assert.Equal("State 5", cells.ReadyState);
            uint factory = FactoryItemIdentity.FactoryId(box.ContentsPath, box.ContentsFsm);
            var lifecycle = new ItemSpawnLifecycle();
            var replica = new SupplyItemReplica(new Dictionary<uint, string> { { factory, cells.Prefix } }, lifecycle);
            var boxState = new PackageState { FactoryId = FactoryItemIdentity.FactoryId(box.Path, box.Fsm),
                NativeId = "r20batterybox01", Revision = 1, Quantity = 4, Rotation = NetQuaternion.Identity };
            uint boxId = FactoryItemIdentity.ItemId(boxState.FactoryId, boxState.NativeId);
            var outputs = new HashSet<uint>(); var ledger = new PackageOpenLedger();
            for (uint n = 1; n <= 4; n++)
            {
                var request = new PackageOpenRequest { PlayerId = 1, Token = 99, Sequence = n,
                    ItemId = boxId, ExpectedRevision = boxState.Revision };
                Assert.Equal(PackageOpenStatus.Pending, PackageOpenLedger.Check(request, boxState, true, true, true));
                ledger.Begin(request, 1, PackageOpenStatus.Pending);
                var state = new SupplyItemState { FactoryId = factory, NativeId = "r20battery0" + n, Rotation = NetQuaternion.Identity };
                Assert.True(replica.Receive(Assert.IsType<SupplyItemState>(PacketCodec.Decode(PacketCodec.Encode(state))), out uint id));
                Assert.NotEqual(boxId, id); Assert.True(outputs.Add(id));
                ledger.Complete(request, true, id); boxState.Quantity--; boxState.Revision++;
                Assert.Equal(id, ledger.Inspect(request, 1, out bool begin)!.ProducedItemId); Assert.False(begin);
                var cold = new SupplyItemReplica(new Dictionary<uint, string> { { factory, cells.Prefix } }, new());
                Assert.True(cold.Receive(state, out uint restored)); Assert.Equal(id, restored);
                lifecycle.Retire(id); Assert.False(replica.Receive(state, out _));
            }
            Assert.Equal(PackageOpenStatus.Empty, PackageOpenLedger.Check(new() { ItemId = boxId,
                ExpectedRevision = boxState.Revision }, boxState, true, true, true));
        }

        [Theory]
        [InlineData("")] [InlineData("Charge")] [InlineData("destroy")]
        public void UnknownR20RetirementLayoutIsRejected(string variable)
        {
            var root = Catalog();
            var box = root["partsPackages"]!["factories"]!.AsArray().Single(f => f!["fsm"]!.GetValue<string>() == "R20BatteryBox")!;
            box["supplyContents"]!["retirementVariable"] = variable;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(root.ToJsonString()));
        }
    }
}
