using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleDamageTests
    {
        [Fact]
        public void ReplacementClearsOnlyTheKnownRepairedPart()
        {
            uint previous = VehicleDamage.Bearing1 | VehicleDamage.Crankshaft;
            Assert.Equal(VehicleDamage.Crankshaft,
                VehicleDamagePolicy.Reconcile(previous, VehicleDamage.Bearing1, 0));
            Assert.Equal(0u, VehicleDamagePolicy.Reconcile(previous, previous, 0));
        }

        [Fact]
        public void LateBindingPreservesFailuresUntilPartConditionIsKnown()
        {
            Assert.Equal(VehicleDamage.Block,
                VehicleDamagePolicy.Reconcile(VehicleDamage.Block, 0, 0));
            Assert.Equal(VehicleDamage.Block | VehicleDamage.Piston1,
                VehicleDamagePolicy.Reconcile(VehicleDamage.Block, VehicleDamage.Piston1, VehicleDamage.Piston1));
        }

        [Fact]
        public void RandomSelectorsAndUnusedBitsNeverBecomeDurableDamage()
        {
            uint triggers = VehicleDamage.Seize | VehicleDamage.Camfail | (1u << 30);
            Assert.Equal(VehicleDamage.Headgasket,
                VehicleDamagePolicy.Reconcile(triggers, triggers | VehicleDamage.Headgasket,
                    triggers | VehicleDamage.Headgasket));
        }

        [Theory]
        [InlineData(VehicleDamage.Seize)]
        [InlineData(VehicleDamage.Camfail)]
        [InlineData(1u << 16)]
        public void ReceiversRejectRandomOrUnknownSlots(uint bit)
        {
            Assert.False(VehicleDamagePolicy.IsValid(new VehicleDamage { DamageMask = bit }));
            Assert.False(VehicleDamagePolicy.IsValid(new VehicleDamage { KnownPartsMask = bit }));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void NonFiniteKnownWearIsRejected(float value)
        {
            var state = new VehicleDamage { KnownPartsMask = VehicleDamage.Piston1 };
            state.Wear[7] = value;
            Assert.False(VehicleDamagePolicy.IsValid(state));
        }

        [Theory]
        [InlineData(80f, false, true)]
        [InlineData(0f, true, true)]
        [InlineData(-2f, true, true)]
        [InlineData(80f, true, false)]
        [InlineData(0f, false, false)]
        public void WearAndBreakageMustAgree(float wear, bool broken, bool valid)
        {
            var state = new VehicleDamage
            {
                KnownPartsMask = VehicleDamage.Bearing1,
                DamageMask = broken ? VehicleDamage.Bearing1 : 0,
            };
            state.Wear[0] = wear;
            Assert.Equal(valid, VehicleDamagePolicy.IsValid(state));
        }

        [Fact]
        public void ChangeDetectionIgnoresSequenceButIncludesRepairsAndLateBindings()
        {
            var first = new VehicleDamage { KnownPartsMask = VehicleDamage.Bearing1 };
            first.Wear[0] = 50f;
            var next = new VehicleDamage { KnownPartsMask = VehicleDamage.Bearing1, Sequence = 200 };
            next.Wear[0] = 50.005f;
            Assert.True(VehicleDamagePolicy.SameCondition(first, next));
            next.Wear[0] = 49.98f;
            Assert.False(VehicleDamagePolicy.SameCondition(first, next));
            next.Wear[0] = 50f;
            next.KnownPartsMask |= VehicleDamage.Block;
            next.Wear[14] = 100f;
            Assert.False(VehicleDamagePolicy.SameCondition(first, next));
            next.KnownPartsMask = VehicleDamage.Bearing1;
            next.DamageMask = VehicleDamage.Bearing1;
            next.Wear[0] = 0f;
            Assert.False(VehicleDamagePolicy.SameCondition(first, next));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SnapshotRoundTripIncludesHealthyPartsAndAllWearSlots(bool broken)
        {
            var state = new VehicleDamage
            {
                VehicleId = 1234, OwnerPlayerId = 2, Sequence = ushort.MaxValue,
                KnownPartsMask = VehicleDamage.ConcretePartsMask,
                DamageMask = broken ? VehicleDamage.Block : 0,
            };
            for (int i = 0; i < VehicleDamage.PartSlots; i++) state.Wear[i] = 20.125f + i;
            if (broken) state.Wear[14] = -1.5f;
            byte[] packet = PacketCodec.Encode(state);
            Assert.Equal(81, packet.Length);
            var decoded = Assert.IsType<VehicleDamage>(PacketCodec.Decode(packet));
            Assert.True(VehicleDamagePolicy.IsValid(decoded));
            Assert.Equal(state.VehicleId, decoded.VehicleId);
            Assert.Equal(state.OwnerPlayerId, decoded.OwnerPlayerId);
            Assert.Equal(state.Sequence, decoded.Sequence);
            Assert.Equal(state.KnownPartsMask, decoded.KnownPartsMask);
            Assert.Equal(state.DamageMask, decoded.DamageMask);
            Assert.Equal(state.Wear, decoded.Wear);
            Assert.True(SessionMessagePolicy.IsChannelAllowed(decoded.Id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(decoded.Id, Channel.UnreliableSequenced));
        }

        [Fact]
        public void PartialWearArraysCannotBeSerialized()
        {
            var state = new VehicleDamage { Wear = new float[2] };
            Assert.False(VehicleDamagePolicy.IsValid(state));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        }
    }
}
