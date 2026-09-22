using System;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleCoolantTests
    {
        private static VehicleCoolantState State(uint revision = 1, float celsius = 87.125f, byte flags = 1)
            => new VehicleCoolantState { VehicleId = 42, Revision = revision, Flags = flags, Celsius = celsius };

        [Theory]
        [InlineData(-99f)] [InlineData(-25.375f)] [InlineData(0f)] [InlineData(87.125f)]
        [InlineData(120f)] [InlineData(140f)] [InlineData(float.MaxValue)] [InlineData(-float.MaxValue)]
        public void DegreesRetainNativePrecisionBeyondTheOldGaugeByte(float celsius)
        {
            var bytes = PacketCodec.Encode(State(123, celsius));
            Assert.Equal(19, bytes.Length); Assert.Equal(197, BitConverter.ToUInt16(bytes, 0));
            var copy = Assert.IsType<VehicleCoolantState>(PacketCodec.Decode(bytes));
            Assert.Equal(42u, copy.VehicleId); Assert.Equal(123u, copy.Revision); Assert.Equal(celsius, copy.Celsius);
            Assert.Equal((byte)1, copy.Flags);
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonfiniteDegreesAreRejectedInBothDirections(float invalid)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(1, invalid)));
            var bytes = PacketCodec.Encode(State()); Array.Copy(BitConverter.GetBytes(invalid), 0, bytes, 11, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            Assert.False(new VehicleCoolantReplica().Receive(State(1, invalid)));
        }

        [Theory]
        [InlineData(2)] [InlineData(3)] [InlineData(128)] [InlineData(255)]
        public void UnknownFlagsAreRejected(byte flags)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(1, 0, flags)));
            var bytes = PacketCodec.Encode(State()); bytes[10] = flags;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Fact]
        public void UnavailableRequiresZeroAndARealVehicle()
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(1, 80, 0)));
            var bytes = PacketCodec.Encode(State()); bytes[10] = 0;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            var absent = State(2, 0, 0); Assert.True(absent.Valid);
            Assert.Equal((byte)0, Assert.IsType<VehicleCoolantState>(PacketCodec.Decode(PacketCodec.Encode(absent))).Flags);
            absent.VehicleId = 0; Assert.False(absent.Valid);
            bytes = PacketCodec.Encode(State()); Array.Clear(bytes, 2, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Fact]
        public void AllTruncationsAreRejected()
        {
            var bytes = PacketCodec.Encode(State());
            for (int length = 0; length < bytes.Length; length++)
            {
                var cut = new byte[length]; Array.Copy(bytes, cut, length);
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(cut));
            }
        }

        [Fact]
        public void RevisionsRejectStaleConflictingAndForeignStateWithoutRetainingCallerObjects()
        {
            var replica = new VehicleCoolantReplica(); var first = State(50);
            Assert.True(replica.Receive(first)); first.Celsius = 999;
            Assert.Equal(87.125f, replica.Get()!.Celsius);
            var copy = replica.Get()!; copy.Celsius = 888;
            Assert.True(replica.Receive(State(50))); Assert.False(replica.Receive(State(49)));
            Assert.False(replica.Receive(State(50, 88))); Assert.False(replica.Receive(State(50, 0, 0)));
            var foreign = State(51); foreign.VehicleId = 43; Assert.False(replica.Receive(foreign));
            Assert.True(replica.Receive(State(51, 0, 0))); Assert.Equal((byte)0, replica.Get()!.Flags);
            Assert.False(replica.Receive(State(50))); Assert.True(replica.Receive(State(52, -12.5f)));
            Assert.Equal(-12.5f, replica.Get()!.Celsius);
        }

        [Fact]
        public void RevisionWrapAndSessionResetAreIndependentOfDriverSequences()
        {
            var replica = new VehicleCoolantReplica(); Assert.True(replica.Receive(State(uint.MaxValue)));
            Assert.True(replica.Receive(State(0, 90))); Assert.False(replica.Receive(State(uint.MaxValue)));
            Assert.False(replica.Receive(State(0x80000000u))); Assert.True(new VehicleCoolantReplica().Receive(State(0, -25)));
        }

        [Fact]
        public void SnapshotObservationCannotAdvanceTheBroadcastBaseline()
        {
            var publication = new VehicleCoolantPublication(); var first = publication.Observe(42, 1, 80);
            Assert.Equal(1u, first.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(first.Revision); Assert.False(publication.NeedsBroadcast);
            var snapshot = publication.Observe(42, 1, 81); Assert.Equal(2u, snapshot.Revision);
            snapshot.Celsius = 999; Assert.True(publication.NeedsBroadcast);
            var live = publication.Observe(42, 1, 81); Assert.Equal(81, live.Celsius); Assert.Equal(2u, live.Revision);
            publication.MarkBroadcast(1); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(live.Revision); Assert.False(publication.NeedsBroadcast);
            Assert.Equal(3u, publication.Observe(42, 0, 0).Revision); Assert.True(publication.NeedsBroadcast);
            Assert.Throws<ArgumentException>(() => publication.Observe(43, 1, 81));
            Assert.Throws<ArgumentException>(() => publication.Observe(42, 0, 81));
        }

        [Fact]
        public void OnlyAnAuthenticatedSelectedHostCanPublishOverReliableOrdered()
        {
            var id = MessageId.VehicleCoolantState;
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
        }
    }
}
