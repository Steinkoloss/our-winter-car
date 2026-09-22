using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class WiringTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static WiringState State(uint id, uint revision, byte flags) => new WiringState { SourceId = id, Revision = revision, Flags = flags };

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(5)] [InlineData(7)]
        public void EveryNativeAvailabilityAndBoltCombinationRoundTrips(byte flags)
        {
            var state = (WiringState)PacketCodec.Decode(PacketCodec.Encode(State(3, uint.MaxValue, flags)));
            Assert.Equal((uint)3, state.SourceId); Assert.Equal(uint.MaxValue, state.Revision); Assert.Equal(flags, state.Flags);
            Assert.True(new WiringReplica().Receive(state));
        }

        [Theory]
        [InlineData(2)] [InlineData(4)] [InlineData(6)] [InlineData(8)] [InlineData(9)] [InlineData(255)]
        public void UnknownOrUnavailableValueFlagsCannotCrossTheWire(byte flags)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(3, 1, flags)));
            var writer = new NetWriter(); writer.WriteUInt32(3); writer.WriteUInt32(1); writer.WriteByte(flags);
            Assert.Throws<ProtocolException>(() => new WiringState().Read(new NetReader(writer.ToArray())));
            Assert.False(new WiringReplica().Receive(State(3, 1, flags)));
        }

        [Theory]
        [InlineData(0, 1)] [InlineData(12, 1)] [InlineData(4294967295, 1)]
        [InlineData(1, 5)] [InlineData(5, 7)] [InlineData(6, 7)] [InlineData(7, 7)] [InlineData(8, 7)]
        public void SourceIdentityAndNativeBoltCapabilityAreBounded(uint id, byte flags)
        {
            Assert.False(new WiringReplica().Receive(State(id, 1, flags)));
            Assert.Throws<ArgumentException>(() => new WiringPublication().Observe(id, flags));
        }

        [Fact]
        public void RevisionOrderingCopiesInputAndOutputAndHandlesWraparound()
        {
            var replica = new WiringReplica(); var state = State(3, uint.MaxValue, 7);
            Assert.True(replica.Receive(state)); state.Flags = 0;
            var copy = replica.Get(3)!; copy.Flags = 0; Assert.Equal((byte)7, replica.Get(3)!.Flags);
            Assert.True(replica.Receive(State(3, uint.MaxValue, 7)));
            Assert.False(replica.Receive(State(3, uint.MaxValue, 3)));
            Assert.True(replica.Receive(State(3, 0, 1)));
            Assert.False(replica.Receive(State(3, uint.MaxValue, 7)));
            Assert.False(replica.Receive(State(3, 0x80000000, 7)));
            Assert.True(replica.Receive(State(3, 1, 0))); Assert.Equal((byte)0, replica.Get(3)!.Flags);
            Assert.True(replica.Receive(State(4, 0, 5))); Assert.Equal((byte)0, replica.Get(3)!.Flags);
            replica.Clear(); Assert.Null(replica.Get(3)); Assert.Null(replica.Get(4));
            Assert.True(replica.Receive(State(3, 0, 7)));
        }

        [Fact]
        public void JoinSnapshotDoesNotConsumeAnExistingPeersPendingLiveUpdate()
        {
            var publication = new WiringPublication();
            Assert.False(publication.NeedsBroadcast);
            var first = publication.Observe(3, 7); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(first.Revision); Assert.False(publication.NeedsBroadcast);
            var snapshot = publication.Observe(3, 3); Assert.Equal(first.Revision + 1, snapshot.Revision);
            snapshot.Flags = 0;
            var live = publication.Observe(3, 3); Assert.Equal(snapshot.Revision, live.Revision); Assert.Equal((byte)3, live.Flags);
            Assert.True(publication.NeedsBroadcast); publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(live.Revision); Assert.False(publication.NeedsBroadcast);
            var unavailable = publication.Observe(3, 0); Assert.Equal(live.Revision + 1, unavailable.Revision);
            Assert.True(publication.NeedsBroadcast);
            Assert.Throws<ArgumentException>(() => publication.Observe(4, 7));
        }

        [Fact]
        public void OnlyTheAuthenticatedSelectedHostCanDeliverOrderedWireState()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringState, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.WiringState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.WiringState, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.WiringState, Channel.ReliableBulk));
        }

        [Fact]
        public void AuditedSourcesPreserveThirteenEngineReadsWithoutInventingPartFactories()
        {
            var catalog = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(catalog.GuestEngineInputsError);
            var profile = catalog.GuestEngineInputs!; Assert.Equal(11, profile.Wires.Count);
            var entries = profile.Entries.Where(e => e.WiringSource != null).ToArray();
            Assert.Equal(11, entries.Length); Assert.Equal(13, entries.Sum(e => e.Readers.Count));
            Assert.Equal(102, profile.Entries.Count); Assert.Equal(182, profile.Entries.Sum(e => e.Readers.Count));
            Assert.Equal(39, catalog.ReplacementParts!.Factories.Count); Assert.Equal(34, catalog.PartsPackages!.Factories.Count);
            Assert.All(profile.Wires, w => {
                Assert.Equal(WiringPolicy.Name(w.Id), w.Name); Assert.Equal(WiringPolicy.SupportsBolted(w.Id), w.SupportsBolted);
                Assert.Equal("CORRIS/Wiring/DatabaseWiring/" + w.Name, w.Path); Assert.Equal("Data", w.Fsm);
                Assert.Equal(w.SupportsBolted ? "Set bolt" : "Basic state", w.SettledState);
            });
            Assert.All(entries, e => {
                Assert.Empty(e.Families); Assert.Empty(e.MountVariable); Assert.Equal(e.WiringSource!.Path, e.MountPath);
                Assert.All(e.Readers, r => { Assert.Equal("GetFsmBool", r.ActionType); Assert.False(r.EveryFrame); });
            });
            var ground = Assert.Single(entries, e => e.Fsm == "Starter" && e.WiringSource!.Id == 4);
            Assert.Equal("Installed", Assert.Single(ground.Readers).Variable);
            ground = Assert.Single(entries, e => e.Fsm == "Electrics" && e.WiringSource!.Id == 4);
            Assert.Equal("Bolted", Assert.Single(ground.Readers).Variable);
        }

        [Theory]
        [InlineData("missing wire")] [InlineData("id")] [InlineData("duplicate")] [InlineData("name")]
        [InlineData("path")] [InlineData("fsm")] [InlineData("bolt")] [InlineData("settled")]
        [InlineData("missing source")] [InlineData("reference")] [InlineData("mount")] [InlineData("family")]
        [InlineData("slot")] [InlineData("mount variable")] [InlineData("field")] [InlineData("type")]
        [InlineData("missing read")] [InlineData("writer overlap")]
        public void PartialOrMisboundWireProfilesFailWithinTheProtectedInputSubsystem(string fault)
        {
            var json = Catalog(); var profile = json["guestEngineInputs"]!;
            var wires = profile["wires"]!.AsArray(); var wire = wires[0]!;
            var entries = profile["entries"]!.AsArray(); var entry = entries.First(e => e!["wire"] != null)!;
            var read = entry["readers"]![0]!;
            switch (fault)
            {
                case "missing wire": wires.RemoveAt(0); break;
                case "id": wire["id"] = 9; break;
                case "duplicate": wires[1] = wire.DeepClone(); break;
                case "name": wire["name"] = "WiringBlockGround"; break;
                case "path": wire["path"] = "CORRIS/Elsewhere/" + WiringPolicy.Name(1); break;
                case "fsm": wire["fsm"] = "Status"; break;
                case "bolt": wire["supportsBolted"] = true; break;
                case "settled": wire["settledState"] = "Load game"; break;
                case "missing source": entries.Remove(entry); break;
                case "reference": entry["wire"] = "Unknown"; break;
                case "mount": entry["mountPath"] = "CORRIS/Other"; break;
                case "family": entry["familyPrefix"] = "VIN212"; break;
                case "slot": entry["slotIndex"] = 1; break;
                case "mount variable": entry["mountVariable"] = "VINP"; break;
                case "field": read["variable"] = "Bolted"; break;
                case "type": read["actionType"] = "GetFsmFloat"; break;
                case "missing read": entry["readers"]!.AsArray().RemoveAt(0); break;
                case "writer overlap":
                    var writer = json["guestEngineProtection"]!["writers"]!.AsArray().Single(w => w!["fsm"]!.GetValue<string>() == "Cylinders")!;
                    read["state"] = writer["actions"]![0]!["state"]!.DeepClone(); read["actionIndex"] = writer["actions"]![0]!["index"]!.DeepClone(); break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
            Assert.NotNull(parsed.GuestEngineProtection); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
