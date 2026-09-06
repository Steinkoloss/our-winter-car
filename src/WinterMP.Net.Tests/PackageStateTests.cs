using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PackageStateTests
    {
        private static PartsPackagesData Catalog() => SyncCatalogJson.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"))).PartsPackages!;
        private static PackageState State(string fsm = "Pistons", ushort quantity = 4, uint revision = 1)
        {
            var c = Catalog(); var rule = c.Factories.Single(r => r.Fsm == fsm);
            return new PackageState { FactoryId = FactoryItemIdentity.FactoryId(c["factoryPath"], fsm),
                NativeId = rule.Prefix + "1", Quantity = quantity, Revision = revision,
                Position = new NetVector3(12, 3, -4), Rotation = new NetQuaternion(0, 0, 0, 1) };
        }
        private static PackageReplica Replica(ItemSpawnLifecycle? lifecycle = null) =>
            new PackageReplica(Catalog().Identities, lifecycle ?? new ItemSpawnLifecycle());

        [Fact]
        public void WireLayoutUsesStableIdentityQuantityAndFullPose()
        {
            var state = State(revision: 0x12345678);
            byte[] wire = PacketCodec.Encode(state);
            using var reader = new BinaryReader(new MemoryStream(wire), Encoding.UTF8);
            Assert.Equal(184, reader.ReadUInt16()); Assert.Equal(state.Revision, reader.ReadUInt32());
            Assert.Equal(state.FactoryId, reader.ReadUInt32());
            Assert.Equal(state.NativeId, Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16())));
            Assert.Equal((ushort)4, reader.ReadUInt16());
            Assert.Equal(12f, reader.ReadSingle()); Assert.Equal(3f, reader.ReadSingle()); Assert.Equal(-4f, reader.ReadSingle());
            Assert.Equal(0f, reader.ReadSingle()); Assert.Equal(0f, reader.ReadSingle());
            Assert.Equal(0f, reader.ReadSingle()); Assert.Equal(1f, reader.ReadSingle());
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
            Assert.Equal(wire, PacketCodec.Encode(Assert.IsType<PackageState>(PacketCodec.Decode(wire))));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Take(wire.Length - 1).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Concat(new byte[] { 0 }).ToArray()));
        }

        [Fact]
        public void EveryBoxCanArriveFullPartlyUsedAndEmptyWithoutChangingItsIdentity()
        {
            var c = Catalog(); var replica = Replica();
            foreach (var rule in c.Factories)
            {
                ushort capacity = rule.Fsm == "Pistons" ? (ushort)4 : rule.Fsm == "Mainbearing" ? (ushort)5
                    : rule.Fsm == "Rockers" ? (ushort)8 : (ushort)1;
                Assert.Equal(capacity, rule.Capacity);
                uint expected = FactoryItemIdentity.ItemId(FactoryItemIdentity.FactoryId(c["factoryPath"], rule.Fsm), rule.Prefix + "1");
                for (int quantity = capacity; quantity >= 0; quantity--)
                {
                    var state = State(rule.Fsm, (ushort)quantity, (uint)(capacity - quantity));
                    Assert.True(replica.Receive(state, out uint id)); Assert.Equal(expected, id);
                    Assert.Equal((ushort)quantity, replica.Get(id)!.Quantity);
                }
                Assert.False(replica.Receive(State(rule.Fsm, (ushort)(capacity + 1), 100), out _));
            }
        }

        [Theory]
        [InlineData("factory")]
        [InlineData("id")]
        [InlineData("quantity")]
        [InlineData("nan-position")]
        [InlineData("infinite-position")]
        [InlineData("nan-rotation")]
        [InlineData("zero-rotation")]
        [InlineData("large-rotation")]
        public void InvalidStateNeverReplacesAUsableSnapshot(string fault)
        {
            var replica = Replica(); var valid = State(); Assert.True(replica.Receive(valid, out uint id));
            var invalid = State(revision: 2);
            switch (fault)
            {
                case "factory": invalid.FactoryId++; break;
                case "id": invalid.NativeId = "boxpistons001"; break;
                case "quantity": invalid.Quantity = 5; break;
                case "nan-position": invalid.Position = new NetVector3(float.NaN, 0, 0); break;
                case "infinite-position": invalid.Position = new NetVector3(0, float.PositiveInfinity, 0); break;
                case "nan-rotation": invalid.Rotation = new NetQuaternion(0, 0, float.NaN, 1); break;
                case "zero-rotation": invalid.Rotation = default; break;
                case "large-rotation": invalid.Rotation = new NetQuaternion(0, 0, 0, 2); break;
            }
            Assert.False(replica.Receive(invalid, out _)); Assert.Equal(1u, replica.Get(id)!.Revision);
        }

        [Fact]
        public void ReplayRepairsAMissingBoxAtANewPoseWithoutAcceptingChangedContentsAtTheSameRevision()
        {
            var replica = Replica(); var state = State(quantity: 2, revision: 8);
            Assert.True(replica.Receive(state, out uint id));
            state.Position = new NetVector3(500, 2, 4);
            Assert.True(replica.Receive(state, out _)); Assert.Equal(500, replica.Get(id)!.Position.X);
            state.Quantity = 3; Assert.False(replica.Receive(state, out _));
            state.Revision = 7; Assert.False(replica.Receive(state, out _));
            Assert.Equal((ushort)2, replica.Get(id)!.Quantity);
        }

        [Fact]
        public void StateIsCopiedOnBothReceiptAndRead()
        {
            var replica = Replica(); var state = State(); Assert.True(replica.Receive(state, out uint id));
            state.Quantity = 0; replica.Get(id)!.NativeId = "wrong";
            Assert.Equal((ushort)4, replica.Get(id)!.Quantity); Assert.Equal("boxpistons01", replica.Get(id)!.NativeId);
        }

        [Fact]
        public void RemovalBeforeOrAfterCreationIsTerminalUntilTheSessionEnds()
        {
            var lifecycle = new ItemSpawnLifecycle(); var replica = Replica(lifecycle); var state = State();
            uint id = FactoryItemIdentity.ItemId(state.FactoryId, state.NativeId);
            lifecycle.Retire(id); Assert.False(replica.Receive(state, out _)); Assert.Null(replica.Get(id));
            lifecycle.Clear(); Assert.True(replica.Receive(state, out _));
            lifecycle.Retire(id); Assert.Null(replica.Get(id));
            state.Revision++; Assert.False(replica.Receive(state, out _));
            replica.Clear(); Assert.False(replica.Receive(state, out _));
            lifecycle.Clear(); Assert.True(replica.Receive(State(), out _));
        }

        [Fact]
        public void RevisionsWrapAndRemainIndependentBetweenBoxes()
        {
            var replica = Replica(); Assert.True(replica.Receive(State(revision: uint.MaxValue), out uint id));
            Assert.True(replica.Receive(State(quantity: 3, revision: 0), out _));
            Assert.False(replica.Receive(State(revision: uint.MaxValue), out _));
            var another = State(); another.NativeId = "boxpistons02";
            Assert.True(replica.Receive(another, out uint other)); Assert.NotEqual(id, other);
            Assert.Equal((ushort)3, replica.Get(id)!.Quantity); Assert.Equal((ushort)4, replica.Get(other)!.Quantity);
        }

        [Fact]
        public void AJoinSnapshotCannotSwallowAnUpdateOwedToExistingGuests()
        {
            var publication = new PackagePublication();
            Assert.False(publication.NeedsBroadcast);
            uint full = publication.Observe(4); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(full); Assert.False(publication.NeedsBroadcast);
            uint snapshot = publication.Observe(3); Assert.True(publication.NeedsBroadcast);
            Assert.Equal(snapshot, publication.Observe(3)); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(snapshot); Assert.False(publication.NeedsBroadcast);
            publication.Observe(2); publication.MarkBroadcast(snapshot); Assert.True(publication.NeedsBroadcast);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0d)]
        [InlineData(-1d)]
        [InlineData(65d)]
        [InlineData(1.5)]
        public void CatalogRejectsMissingOrUnsafeCapacities(double? capacity)
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            json["partsPackages"]!["factories"]![0]!["capacity"] = capacity;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }
    }
}
