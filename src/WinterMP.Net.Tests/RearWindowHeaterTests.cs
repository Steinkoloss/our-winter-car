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
    public class RearWindowHeaterTests
    {
        private static HeaterState State(byte heater, byte rear, uint revision = 1) => new HeaterState
            { Flags = heater, Wear = heater == 3 ? 80 : 0, RearWindowFlags = rear, Revision = revision };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        public static IEnumerable<object[]> Combinations() => new byte[] { 0, 1, 3 }
            .SelectMany(heater => new byte[] { 0, 1, 3 }.Select(rear => new object[] { heater, rear }));

        [Theory]
        [MemberData(nameof(Combinations))]
        public void RearElementAppendsOneIndependentByteWithoutChangingHeaterFields(byte heater, byte rear)
        {
            var state = State(heater, rear, uint.MaxValue);
            var bytes = PacketCodec.Encode(state);
            Assert.Equal(12, bytes.Length);
            Assert.Equal(rear, bytes[11]);
            var decoded = Assert.IsType<HeaterState>(PacketCodec.Decode(bytes));
            Assert.Equal(state.Flags, decoded.Flags);
            Assert.Equal(state.Wear, decoded.Wear);
            Assert.Equal(state.Revision, decoded.Revision);
            Assert.Equal(rear, decoded.RearWindowFlags);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(11).ToArray()));
            Assert.True(new HeaterReplica().Receive(decoded));
        }

        [Theory]
        [InlineData(2)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(128)] [InlineData(255)]
        public void UnknownOrUnavailableInstalledRearElementFlagsFailEveryBoundary(byte rear)
        {
            var state = State(3, rear);
            Assert.False(state.Valid);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var bytes = PacketCodec.Encode(State(3, 0)); bytes[11] = rear;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            Assert.False(new HeaterReplica().Receive(state));
            Assert.Throws<ArgumentException>(() => new HeaterPublication().Observe(3, 80, rear));
        }

        [Fact]
        public void RearOnlyChangesPublishAndSnapshotsCannotConsumePendingState()
        {
            var publication = new HeaterPublication();
            var first = publication.Observe(3, 80, 1); publication.MarkBroadcast(first.Revision);
            var update = publication.Observe(3, 80, 3);
            Assert.Equal(first.Revision + 1, update.Revision);
            update.RearWindowFlags = 0;
            var snapshot = publication.Observe(3, 80, 3);
            Assert.Equal(update.Revision, snapshot.Revision);
            Assert.Equal((byte)3, snapshot.RearWindowFlags);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(snapshot.Revision); Assert.False(publication.NeedsBroadcast);
            Assert.Equal(snapshot.Revision + 1, publication.Observe(3, 80, 0).Revision);
        }

        [Fact]
        public void RearStateCopiesConflictChecksWrapAndClearFollowHeaterRevision()
        {
            var replica = new HeaterReplica(); var incoming = State(3, 3, uint.MaxValue);
            Assert.True(replica.Receive(incoming)); incoming.RearWindowFlags = 0;
            var copy = replica.Get()!; copy.RearWindowFlags = 1;
            Assert.Equal((byte)3, replica.Get()!.RearWindowFlags);
            Assert.False(replica.Receive(State(3, 1, uint.MaxValue)));
            Assert.True(replica.Receive(State(3, 1, 0)));
            Assert.False(replica.Receive(State(3, 3, uint.MaxValue)));
            Assert.False(replica.Receive(State(3, 3, 0x80000000)));
            replica.Clear(); Assert.Null(replica.Get()); Assert.True(replica.Receive(State(0, 3, 0)));
        }

        [Fact]
        public void CatalogPinsBodyOptionAndSettledStatesWithoutPausingBodySave()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            var heater = Assert.IsType<EngineHeaterData>(parsed.GuestEngineInputs!.Heater);
            Assert.Equal("CORRIS/BODY", heater.RearWindow.Path);
            Assert.Equal("Save", heater.RearWindow.Fsm);
            Assert.Equal("HeatingSprites", heater.RearWindow.Variable);
            Assert.Equal(new[] { "State 1", "State 4", "Save" }, heater.RearWindow.ReadyStates);
            Assert.DoesNotContain(parsed.GuestEngineProtection!.PausedFsms, fsm => fsm.Path == "CORRIS/BODY");
            Assert.Equal(23, parsed.GuestEngineProtection.PausedFsms.Count);
        }

        [Theory]
        [InlineData("rearWindow", null)]
        [InlineData("path", "\"CORRIS/Other\"")]
        [InlineData("fsm", "\"Data\"")]
        [InlineData("variable", "\"Installed\"")]
        [InlineData("readyStates", "[\"State 1\",\"State 4\"]")]
        [InlineData("readyStates", "[\"State 1\",\"State 4\",\"Load 2\"]")]
        [InlineData("readyStates", "[\"Save\",\"Save\",\"State 4\"]")]
        public void ChangedOrIncompleteRearSourceRejectsInputAdmission(string field, string? value)
        {
            var json = Catalog(); var heater = json["guestEngineInputs"]!["heater"]!;
            if (field == "rearWindow") heater.AsObject().Remove(field);
            else heater["rearWindow"]![field] = JsonNode.Parse(value!);
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs);
            Assert.NotNull(parsed.GuestEngineProtection);
            Assert.NotEmpty(parsed.Doors);
        }
    }
}
