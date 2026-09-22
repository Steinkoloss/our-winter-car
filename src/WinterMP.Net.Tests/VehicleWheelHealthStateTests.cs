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
    public sealed class VehicleWheelHealthStateTests
    {
        private static VehicleWheelHealthState State(uint revision = 1, float health = 89.12345f) => new VehicleWheelHealthState {
            VehicleId = 71, Revision = revision, Availability = 15, HealthFL = health, HealthFR = 15.0001f, HealthRL = -.125f, HealthRR = 73.625f };

        [Theory]
        [InlineData(-1f)] [InlineData(0f)] [InlineData(.99999f)] [InlineData(1f)] [InlineData(7f)]
        [InlineData(15f)] [InlineData(89.12345f)] [InlineData(100f)] [InlineData(float.MinValue)] [InlineData(float.MaxValue)]
        public void ExactNativeHealthSurvivesTheWire(float value)
        {
            var state = State(0x12345678, value); byte[] packet = PacketCodec.Encode(state);
            Assert.Equal(43, packet.Length); Assert.Equal(205, BitConverter.ToUInt16(packet, 0));
            Assert.Equal(71u, BitConverter.ToUInt32(packet, 2)); Assert.Equal(state.Revision, BitConverter.ToUInt32(packet, 6));
            Assert.Equal(15, packet[10]); Assert.Equal(value, BitConverter.ToSingle(packet, 11));
            Assert.Equal(state.HealthFR, BitConverter.ToSingle(packet, 15)); Assert.Equal(state.HealthRL, BitConverter.ToSingle(packet, 19));
            Assert.Equal(state.HealthRR, BitConverter.ToSingle(packet, 23));
            var read = Assert.IsType<VehicleWheelHealthState>(PacketCodec.Decode(packet));
            Assert.True(state.SameHealth(read)); Assert.Equal(packet, PacketCodec.Encode(read));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void EachSlotRejectsNonfiniteOrHiddenUnavailableHealth(int slot)
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                byte[] packet = PacketCodec.Encode(State()); Array.Copy(BitConverter.GetBytes(invalid), 0, packet, 11 + 4 * slot, 4);
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
                var state = new VehicleWheelHealthState { VehicleId = 71, Availability = 15 }; Set(state, slot, invalid);
                Assert.False(state.Valid); Assert.False(new VehicleWheelHealthReplica().Receive(state));
                Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
                Assert.Throws<ArgumentException>(() => new VehicleWheelHealthPublication().Observe(state));
            }
            var hidden = new VehicleWheelHealthState { VehicleId = 71 }; Set(hidden, slot, 1);
            Assert.False(hidden.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(hidden));
            var bytes = PacketCodec.Encode(new VehicleWheelHealthState { VehicleId = 71, Availability = 15 });
            bytes[10] = 0; Array.Copy(BitConverter.GetBytes(1f), 0, bytes, 11 + 4 * slot, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Theory]
        [InlineData(16)] [InlineData(32)] [InlineData(128)] [InlineData(255)]
        public void UnknownAvailabilityAreRejected(byte flags)
        {
            var state = State(); state.Availability = flags; Assert.False(state.Valid);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var bytes = PacketCodec.Encode(State()); bytes[10] = flags; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Fact]
        public void UnknownIsDifferentFromKnownZeroAndVehicleZeroIsInvalid()
        {
            var value = new VehicleWheelHealthState { VehicleId = 71 };
            var unknown = PacketCodec.Encode(value); value.Availability = 15; var known = PacketCodec.Encode(value);
            Assert.NotEqual(unknown, known); value.VehicleId = 0;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(value));
            Array.Clear(known, 2, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(known));
        }

        [Theory]
        [InlineData(0)] [InlineData(2)] [InlineData(6)] [InlineData(10)] [InlineData(11)] [InlineData(15)] [InlineData(19)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(26)] [InlineData(28)]
        public void PartialAndTrailingPayloadsAreRejected(int size)
        {
            var packet = PacketCodec.Encode(State()); Array.Resize(ref packet, size);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
        }

        [Fact]
        public void RevisionsRejectStaleConflictingAndForeignStateWithoutConsumingValidUpdates()
        {
            var replica = new VehicleWheelHealthReplica(); Assert.True(replica.Receive(State(9)));
            Assert.True(replica.Receive(State(9))); Assert.False(replica.Receive(State(9, 0))); Assert.False(replica.Receive(State(8)));
            var foreign = State(10); foreign.VehicleId++; Assert.False(replica.Receive(foreign));
            var invalid = State(10, float.NaN); Assert.False(replica.Receive(invalid));
            Assert.True(replica.Receive(State(10, 0))); Assert.Equal(0f, replica.Get()!.HealthFL);
        }

        [Fact]
        public void WithdrawalsAndRepairsReplaceTheWholeResult()
        {
            var replica = new VehicleWheelHealthReplica(); Assert.True(replica.Receive(State(9, .1f)));
            Assert.True(replica.Receive(new VehicleWheelHealthState { VehicleId = 71, Revision = 10 }));
            Assert.Equal(0, replica.Get()!.Availability); Assert.False(replica.Receive(State(9)));
            Assert.True(replica.Receive(State(11, 100))); Assert.Equal(100f, replica.Get()!.HealthFL);
        }

        [Fact]
        public void SerialWrapHalfRangeAndNewSessionAreHandled()
        {
            var replica = new VehicleWheelHealthReplica(); Assert.True(replica.Receive(State(uint.MaxValue)));
            Assert.True(replica.Receive(State(0, 80))); Assert.False(replica.Receive(State(uint.MaxValue)));
            Assert.False(replica.Receive(State(0x80000000))); Assert.True(new VehicleWheelHealthReplica().Receive(State(0, 90)));
        }

        [Fact]
        public void CallerAndReaderStorageCannotMutateAcceptedOrPublishedState()
        {
            var value = State(); var replica = new VehicleWheelHealthReplica(); Assert.True(replica.Receive(value));
            var publication = new VehicleWheelHealthPublication(); var published = publication.Observe(value);
            value.HealthFL = 999; replica.Get()!.HealthFR = 999; published.HealthRL = 999;
            Assert.True(State().SameHealth(replica.Get()!)); Assert.True(State().SameHealth(publication.Observe(State())));
        }

        [Fact]
        public void SnapshotCaptureDoesNotConsumePendingPublicationOrDriverHistory()
        {
            var publication = new VehicleWheelHealthPublication(); var state = publication.Observe(State(65535));
            Assert.Equal(1u, state.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(1); Assert.False(publication.NeedsBroadcast);
            var snapshot = publication.Observe(State(0, 88)); Assert.Equal(2u, snapshot.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(1); Assert.True(publication.NeedsBroadcast);
            Assert.Equal(2u, publication.Observe(State(42, 88)).Revision); publication.MarkBroadcast(2); Assert.False(publication.NeedsBroadcast);
            var foreign = State(); foreign.VehicleId++; Assert.Throws<ArgumentException>(() => publication.Observe(foreign));
        }

        [Fact]
        public void OnlyTheSelectedAuthenticatedHostMaySendHealthResultsReliably()
        {
            var id = MessageId.VehicleWheelHealthState;
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, false, false, false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
        }

        private static void Set(VehicleWheelHealthState state, int slot, float value)
        { if (slot == 0) state.HealthFL = value; else if (slot == 1) state.HealthFR = value; else if (slot == 2) state.HealthRL = value; else state.HealthRR = value; }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
        [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
        public void EachWheelCanWithdrawIndependently(byte mask)
        {
            var state = State(); state.Availability = mask;
            for (int i = 0; i < 4; i++) if (!state.HasWheel(i)) Set(state, i, 0);
            var copy = Assert.IsType<VehicleWheelHealthState>(PacketCodec.Decode(PacketCodec.Encode(state)));
            for (int i = 0; i < 4; i++) { Assert.Equal((mask & (1 << i)) != 0, copy.HasWheel(i)); Assert.Equal(state.Health(i), copy.Health(i)); }
            Assert.False(copy.HasWheel(-1)); Assert.False(copy.HasWheel(4));
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        [Fact]
        public void CatalogSourcesMatchTheFourGuardedNativeMountReaders()
        {
            var catalog = SyncCatalogJson.Parse(Catalog().ToJsonString()); var rule = Assert.IsType<VehicleWheelHealthData>(catalog.VehicleWheelHealth);
            Assert.Equal("CORRIS", rule.RootPath); Assert.Equal(4, rule.Sources.Count);
            foreach (var source in rule.Sources)
            {
                var guard = catalog.GuestEngineProtection!.Writers.Single(w => w.WheelHealthIndex == source.Wheel);
                Assert.Equal(guard.Path, source.Path); Assert.Equal(2, guard.WheelHealthReads.Count);
                Assert.Equal(source.Path + "/tire/VINP_Wheel" + new[] { "FL", "FR", "RL", "RR" }[source.Wheel], source.TargetPath);
            }
        }

        [Theory]
        [InlineData("missing")] [InlineData("wheel")] [InlineData("duplicate wheel")] [InlineData("duplicate path")]
        [InlineData("foreign path")] [InlineData("foreign target")] [InlineData("null")] [InlineData("empty root")]
        public void MalformedSourcesDisableOnlyWheelHealthPublication(string change)
        {
            var root = Catalog(); var profile = root["vehicleWheelHealth"]!; var sources = profile["sources"]!.AsArray();
            switch (change)
            {
                case "missing": sources.RemoveAt(3); break;
                case "wheel": sources[0]!["wheel"] = 4; break;
                case "duplicate wheel": sources[1]!["wheel"] = 0; break;
                case "duplicate path": sources[1]!["path"] = sources[0]!["path"]!.DeepClone(); break;
                case "foreign path": sources[0]!["path"] = "OTHER/WHEELc_FL"; break;
                case "foreign target": sources[0]!["targetPath"] = sources[1]!["targetPath"]!.DeepClone(); break;
                case "null": root["vehicleWheelHealth"] = null; break;
                case "empty root": profile["rootPath"] = ""; break;
            }
            var parsed = SyncCatalogJson.Parse(root.ToJsonString()); Assert.Null(parsed.VehicleWheelHealth);
            Assert.NotNull(parsed.VehicleWheelHealthError); Assert.NotNull(parsed.GuestEngineProtection); Assert.NotNull(parsed.VehicleTirePressure);
        }
    }
}
