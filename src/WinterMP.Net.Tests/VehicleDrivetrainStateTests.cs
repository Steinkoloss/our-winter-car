using System;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleDrivetrainStateTests
    {
        private static VehicleDrivetrainWearState State(uint revision = 1, float wear = 89.12345f) => new VehicleDrivetrainWearState {
            VehicleId = 71, Revision = revision, Flags = 1, DriveshaftWear = wear, GearboxWear = 15.0001f, RearAxleWear = -.125f };

        [Theory]
        [InlineData(-1f)] [InlineData(0f)] [InlineData(.99999f)] [InlineData(1f)] [InlineData(7f)]
        [InlineData(15f)] [InlineData(89.12345f)] [InlineData(100f)] [InlineData(float.MinValue)] [InlineData(float.MaxValue)]
        public void ExactNativeWearSurvivesTheWire(float value)
        {
            var state = State(0x12345678, value); byte[] packet = PacketCodec.Encode(state);
            Assert.Equal(28, packet.Length); Assert.Equal(202, BitConverter.ToUInt16(packet, 0));
            Assert.Equal(71u, BitConverter.ToUInt32(packet, 2)); Assert.Equal(state.Revision, BitConverter.ToUInt32(packet, 6));
            Assert.Equal(1, packet[10]); Assert.Equal(value, BitConverter.ToSingle(packet, 11));
            Assert.Equal(state.GearboxWear, BitConverter.ToSingle(packet, 15)); Assert.Equal(state.RearAxleWear, BitConverter.ToSingle(packet, 19));
            var read = Assert.IsType<VehicleDrivetrainWearState>(PacketCodec.Decode(packet));
            Assert.True(state.SameWear(read)); Assert.Equal(packet, PacketCodec.Encode(read));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void EachSlotRejectsNonfiniteOrHiddenUnavailableWear(int slot)
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                byte[] packet = PacketCodec.Encode(State()); Array.Copy(BitConverter.GetBytes(invalid), 0, packet, 11 + 4 * slot, 4);
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
                var state = new VehicleDrivetrainWearState { VehicleId = 71, Flags = 1 }; Set(state, slot, invalid);
                Assert.False(state.Valid); Assert.False(new VehicleDrivetrainWearReplica().Receive(state));
                Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
                Assert.Throws<ArgumentException>(() => new VehicleDrivetrainWearPublication().Observe(state));
            }
            var hidden = new VehicleDrivetrainWearState { VehicleId = 71 }; Set(hidden, slot, 1);
            Assert.False(hidden.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(hidden));
            var bytes = PacketCodec.Encode(new VehicleDrivetrainWearState { VehicleId = 71, Flags = 1 });
            bytes[10] = 0; Array.Copy(BitConverter.GetBytes(1f), 0, bytes, 11 + 4 * slot, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Theory]
        [InlineData(2)] [InlineData(3)] [InlineData(128)] [InlineData(255)]
        public void UnknownFlagsAreRejected(byte flags)
        {
            var state = State(); state.Flags = flags; Assert.False(state.Valid);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var bytes = PacketCodec.Encode(State()); bytes[10] = flags; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Fact]
        public void UnknownIsDifferentFromKnownZeroAndVehicleZeroIsInvalid()
        {
            var value = new VehicleDrivetrainWearState { VehicleId = 71 };
            var unknown = PacketCodec.Encode(value); value.Flags = 1; var known = PacketCodec.Encode(value);
            Assert.NotEqual(unknown, known); value.VehicleId = 0;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(value));
            Array.Clear(known, 2, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(known));
        }

        [Theory]
        [InlineData(0)] [InlineData(2)] [InlineData(6)] [InlineData(10)] [InlineData(11)] [InlineData(15)] [InlineData(19)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(27)] [InlineData(29)]
        public void PartialAndTrailingPayloadsAreRejected(int size)
        {
            var packet = PacketCodec.Encode(State()); Array.Resize(ref packet, size);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
        }

        [Fact]
        public void RevisionsRejectStaleConflictingAndForeignStateWithoutConsumingValidUpdates()
        {
            var replica = new VehicleDrivetrainWearReplica(); Assert.True(replica.Receive(State(9)));
            Assert.True(replica.Receive(State(9))); Assert.False(replica.Receive(State(9, 0))); Assert.False(replica.Receive(State(8)));
            var foreign = State(10); foreign.VehicleId++; Assert.False(replica.Receive(foreign));
            var invalid = State(10, float.NaN); Assert.False(replica.Receive(invalid));
            Assert.True(replica.Receive(State(10, 0))); Assert.Equal(0f, replica.Get()!.DriveshaftWear);
        }

        [Fact]
        public void WithdrawalsAndRepairsReplaceTheWholeResult()
        {
            var replica = new VehicleDrivetrainWearReplica(); Assert.True(replica.Receive(State(9, .1f)));
            Assert.True(replica.Receive(new VehicleDrivetrainWearState { VehicleId = 71, Revision = 10 }));
            Assert.Equal(0, replica.Get()!.Flags); Assert.False(replica.Receive(State(9)));
            Assert.True(replica.Receive(State(11, 100))); Assert.Equal(100f, replica.Get()!.DriveshaftWear);
        }

        [Fact]
        public void SerialWrapHalfRangeAndNewSessionAreHandled()
        {
            var replica = new VehicleDrivetrainWearReplica(); Assert.True(replica.Receive(State(uint.MaxValue)));
            Assert.True(replica.Receive(State(0, 80))); Assert.False(replica.Receive(State(uint.MaxValue)));
            Assert.False(replica.Receive(State(0x80000000))); Assert.True(new VehicleDrivetrainWearReplica().Receive(State(0, 90)));
        }

        [Fact]
        public void CallerAndReaderStorageCannotMutateAcceptedOrPublishedState()
        {
            var value = State(); var replica = new VehicleDrivetrainWearReplica(); Assert.True(replica.Receive(value));
            var publication = new VehicleDrivetrainWearPublication(); var published = publication.Observe(value);
            value.DriveshaftWear = 999; replica.Get()!.GearboxWear = 999; published.RearAxleWear = 999;
            Assert.True(State().SameWear(replica.Get()!)); Assert.True(State().SameWear(publication.Observe(State())));
        }

        [Fact]
        public void SnapshotCaptureDoesNotConsumePendingPublicationOrDriverHistory()
        {
            var publication = new VehicleDrivetrainWearPublication(); var state = publication.Observe(State(65535));
            Assert.Equal(1u, state.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(1); Assert.False(publication.NeedsBroadcast);
            var snapshot = publication.Observe(State(0, 88)); Assert.Equal(2u, snapshot.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(1); Assert.True(publication.NeedsBroadcast);
            Assert.Equal(2u, publication.Observe(State(42, 88)).Revision); publication.MarkBroadcast(2); Assert.False(publication.NeedsBroadcast);
            var foreign = State(); foreign.VehicleId++; Assert.Throws<ArgumentException>(() => publication.Observe(foreign));
        }

        [Fact]
        public void OnlyTheSelectedAuthenticatedHostMaySendWearResultsReliably()
        {
            var id = MessageId.VehicleDrivetrainWearState;
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, false, false, false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
        }

        private static void Set(VehicleDrivetrainWearState state, int slot, float value)
        { if (slot == 0) state.DriveshaftWear = value; else if (slot == 1) state.GearboxWear = value; else state.RearAxleWear = value; }
    }
}
