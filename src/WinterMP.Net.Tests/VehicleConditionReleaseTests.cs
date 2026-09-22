using System;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleConditionReleaseTests
    {
        private static VehicleCondition State() => new VehicleCondition { VehicleId = 42, OwnerPlayerId = 3, Sequence = 18,
            Availability = 63, TirePressure = 190, DrivetrainDamage = 2, HealthFL = 0, HealthFR = 30, HealthRL = 100, HealthRR = 255, Flags = 1 };
        private static ItemTransform Release(ushort sequence = 19) => new ItemTransform {
            ItemId = 42, OwnerPlayerId = 3, Sequence = sequence, Flags = ItemTransform.FlagFinal };
        private static VehicleConditionReleaseAck Ack() => VehicleConditionReleasePolicy.Create(State(), 3, Release())!;

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(4)] [InlineData(63)]
        public void RoundTripPreservesNineteenByteFramingAndAvailability(byte availability)
        {
            var ack = Ack(); ack.Condition.Availability = availability; var bytes = PacketCodec.Encode(ack);
            Assert.Equal(19, bytes.Length); var decoded = Assert.IsType<VehicleConditionReleaseAck>(PacketCodec.Decode(bytes));
            Assert.Equal(bytes, PacketCodec.Encode(decoded)); Assert.Equal(availability, decoded.Condition.Availability);
            Assert.Equal(19, decoded.ReleaseSequence); Assert.Equal(18, decoded.Condition.Sequence);
            Assert.Equal(0, decoded.Condition.HealthFL); Assert.Equal(255, decoded.Condition.HealthRR);
        }

        [Fact]
        public void EveryTruncationAndTrailingByteIsRejected()
        {
            var full = PacketCodec.Encode(Ack());
            for (int length = 0; length < full.Length; length++)
            { var cut = new byte[length]; Array.Copy(full, cut, length); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(cut)); }
            var extra = new byte[full.Length + 1]; Array.Copy(full, extra, full.Length);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(extra));
        }

        [Theory]
        [InlineData("host")] [InlineData("unowned")] [InlineData("snapshot")] [InlineData("vehicle")] [InlineData("flags")] [InlineData("availability")]
        public void InvalidConfirmationCannotEncodeOrDecode(string change)
        {
            var ack = Ack(); var bytes = PacketCodec.Encode(ack);
            switch (change)
            {
                case "host": ack.Condition.OwnerPlayerId = bytes[8] = 0; break;
                case "unowned": ack.Condition.OwnerPlayerId = bytes[8] = 255; break;
                case "snapshot": ack.Condition.Sequence = 65535; bytes[9] = bytes[10] = 255; break;
                case "vehicle": ack.Condition.VehicleId = 0; Array.Clear(bytes, 4, 4); break;
                case "flags": ack.Condition.Flags = bytes[17] = 17; break;
                case "availability": ack.Condition.Availability = bytes[18] = 128; break;
            }
            Assert.False(ack.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(ack));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Theory]
        [InlineData(0)] [InlineData(19)] [InlineData(65535)]
        public void ExactReleaseSequenceMayWrapWithoutBeingASnapshot(ushort sequence)
        {
            var condition = State(); var ack = VehicleConditionReleasePolicy.Create(condition, 3, Release(sequence));
            Assert.NotNull(ack); Assert.Equal(sequence, ack!.ReleaseSequence); Assert.NotSame(condition, ack.Condition);
            condition.HealthFR = 1; Assert.Equal(30, ack.Condition.HealthFR);
            Assert.True(VehicleConditionReleasePolicy.Matches(ack, ack.Copy(), 3));
        }

        [Theory]
        [InlineData("missing")] [InlineData("oldowner")] [InlineData("unowned")]
        [InlineData("live")] [InlineData("driver")] [InlineData("wrongcar")]
        public void OnlyEstablishedGuestFinalsCanBeConfirmed(string change)
        {
            VehicleCondition? state = State(); var final = Release(); byte previous = 3;
            switch (change)
            {
                case "missing": state = null; break;
                case "oldowner": previous = 2; break;
                case "unowned": previous = 255; break;
                case "live": final.Flags = ItemTransform.FlagVehicle; break;
                case "driver": final.Flags |= ItemTransform.FlagDriver; break;
                case "wrongcar": final.ItemId = 43; break;
            }
            Assert.Null(VehicleConditionReleasePolicy.Create(state, previous, final));
        }

        [Theory]
        [InlineData("release")] [InlineData("condition")] [InlineData("vehicle")] [InlineData("owner")]
        public void ConfirmationMustMatchBothSequencesVehicleAndClaimant(string change)
        {
            var pending = Ack(); var received = pending.Copy();
            switch (change)
            {
                case "release": received.ReleaseSequence++; break;
                case "condition": received.Condition.Sequence++; break;
                case "vehicle": received.Condition.VehicleId++; break;
                case "owner": received.Condition.OwnerPlayerId = 2; break;
            }
            Assert.False(VehicleConditionReleasePolicy.Matches(pending, received, 3));
            Assert.False(VehicleConditionReleasePolicy.Matches(pending, Ack(), 2));
            Assert.False(VehicleConditionReleasePolicy.Matches(null, Ack(), 3));
        }

        [Fact]
        public void HostConfirmedValuesAreCopiedAndMayCorrectTheSubmittedValues()
        {
            var pending = Ack(); var received = pending.Copy(); received.Condition.HealthFR = 22;
            Assert.True(VehicleConditionReleasePolicy.Matches(pending, received, 3));
            Assert.Equal(30, pending.Condition.HealthFR);
        }

        [Fact]
        public void AdmissionRequiresSelectedHostCompletedHandshakeAndReliableOrdered()
        {
            var id = MessageId.VehicleConditionReleaseAck;
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
