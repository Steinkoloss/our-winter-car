using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleStateStreamPolicyTests
    {
        private static VehicleState State(byte owner = 1, ushort sequence = 1, uint vehicle = 91) => new VehicleState {
            VehicleId = vehicle, OwnerPlayerId = owner, Sequence = sequence,
            Flags = VehicleState.FlagEngineOn | VehicleState.FlagAccOn, Rpm = 2500,
            SpeedTenthsKmh = 550, FuelLevel = 180, CoolantTemp = 170, Gear = 4 };

        [Theory]
        [InlineData(false, false, 255, false)]
        [InlineData(false, false, 0, false)]
        [InlineData(false, false, 2, false)]
        [InlineData(false, true, 255, true)]
        [InlineData(true, false, 255, true)]
        [InlineData(true, false, 2, false)]
        [InlineData(true, true, 255, true)]
        public void OnlyEstablishedLocalOwnershipOrUnownedHostMayPublish(bool host, bool local, byte remote, bool expected)
            => Assert.Equal(expected, VehicleStateStreamPolicy.CanPublish(host, local, remote));

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(40000)]
        [InlineData(65534)]
        public void FirstPacketHasNoInventedZeroBaseline(ushort sequence)
        {
            var policy = new VehicleStateStreamPolicy();
            Assert.True(policy.Receive(State(sequence: sequence), true, false, 1));
            Assert.False(policy.Receive(State(sequence: sequence), true, false, 1));
        }

        [Theory]
        [InlineData(0, 1, true)]
        [InlineData(0, 32767, true)]
        [InlineData(0, 32768, false)]
        [InlineData(1, 1, false)]
        [InlineData(5, 4, false)]
        [InlineData(65534, 0, true)]
        [InlineData(65534, 1, true)]
        public void LiveSequencesRejectDuplicatesOldPacketsAndAmbiguousHalfRange(ushort previous, ushort next, bool expected)
        {
            var policy = new VehicleStateStreamPolicy();
            Assert.True(policy.Receive(State(sequence: previous), true, false, 1));
            Assert.Equal(expected, policy.Receive(State(sequence: next), true, false, 1));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(65533, 65534)]
        [InlineData(65534, 0)]
        [InlineData(65535, 0)]
        public void PublisherSkipsReservedSnapshotSequence(ushort previous, ushort expected)
            => Assert.Equal(expected, VehicleStateStreamPolicy.NextSequence(previous));

        [Fact]
        public void OwnershipHandoffsKeepIndependentSenderHistory()
        {
            var policy = new VehicleStateStreamPolicy();
            Assert.True(policy.Receive(State(0, 2000), false, false, 0));
            Assert.True(policy.Receive(State(1, 1), false, false, 1));
            Assert.True(policy.Receive(State(2, 1), false, false, 2));
            Assert.True(policy.Receive(State(0, 2001), false, false, 0));
            Assert.False(policy.Receive(State(0, 1999), false, false, 0));
            Assert.False(policy.Receive(State(1, 1), false, false, 1));
            Assert.True(policy.Receive(State(1, 2), false, false, 1));
        }

        [Fact]
        public void DepartedOwnerCannotAdvanceHistoryOrReplaceCurrentSimulator()
        {
            var policy = new VehicleStateStreamPolicy();
            Assert.True(policy.Receive(State(1, 1), false, false, 1));
            Assert.True(policy.Receive(State(2, 1), false, false, 2));
            Assert.False(policy.Receive(State(1, 9000), false, false, 2));
            Assert.False(policy.Receive(State(1, 9000), false, false, 255));
            Assert.False(policy.Receive(State(0, 9000), false, false, 2));
            Assert.True(policy.Receive(State(1, 2), false, false, 1));
            Assert.True(policy.Receive(State(0, 1), false, false, 255));
        }

        [Theory]
        [InlineData(true, false, 1, 1, true)]
        [InlineData(true, false, 1, 2, false)]
        [InlineData(true, false, 255, 1, false)]
        [InlineData(true, false, 0, 0, false)]
        [InlineData(true, true, 1, 1, false)]
        [InlineData(false, false, 255, 0, true)]
        [InlineData(false, false, 0, 0, true)]
        [InlineData(false, false, 1, 1, true)]
        [InlineData(false, false, 1, 0, false)]
        [InlineData(false, true, 0, 0, false)]
        [InlineData(false, true, 1, 1, false)]
        public void SourceGateUsesEstablishedOwnershipInsteadOfNearbyObservers(bool host, bool local, byte remote, byte sender, bool expected)
            => Assert.Equal(expected, new VehicleStateStreamPolicy().Receive(State(sender), host, local, remote));

        [Theory]
        [InlineData(false, false, 255, 0, true)]
        [InlineData(false, false, 0, 0, true)]
        [InlineData(false, false, 1, 0, false)]
        [InlineData(false, true, 255, 0, false)]
        [InlineData(false, true, 0, 0, false)]
        [InlineData(false, false, 1, 1, false)]
        [InlineData(false, false, 255, 1, false)]
        [InlineData(true, false, 1, 1, false)]
        [InlineData(true, false, 255, 0, false)]
        public void SnapshotSentinelIsHostOnlyAndCannotOverrideASimulator(bool host, bool local, byte remote, byte sender, bool expected)
            => Assert.Equal(expected, new VehicleStateStreamPolicy().Receive(State(sender, VehicleState.SnapshotSequence), host, local, remote));

        [Fact]
        public void HostSnapshotDoesNotCreateOrChangeAnyLiveBaseline()
        {
            var policy = new VehicleStateStreamPolicy();
            Assert.True(policy.Receive(State(0, VehicleState.SnapshotSequence), false, false, 255));
            Assert.True(policy.Receive(State(0, 40000), false, false, 255));
            Assert.True(policy.Receive(State(0, VehicleState.SnapshotSequence), false, false, 0));
            Assert.False(policy.Receive(State(0, 39999), false, false, 0));
            Assert.True(policy.Receive(State(0, 40001), false, false, 0));
            Assert.True(policy.Receive(State(1, 0), false, false, 1));
        }

        [Fact]
        public void AdmissionForgetsOnlyReturningPlayerAndTeardownClearsEverything()
        {
            var policy = new VehicleStateStreamPolicy();
            foreach (uint vehicle in new uint[] { 1, 2 })
            {
                Assert.True(policy.Receive(State(1, 100, vehicle), false, false, 1));
                Assert.True(policy.Receive(State(0, 100, vehicle), false, false, 0));
            }
            policy.ForgetPlayer(1);
            foreach (uint vehicle in new uint[] { 1, 2 })
            {
                Assert.True(policy.Receive(State(1, 0, vehicle), false, false, 1));
                Assert.False(policy.Receive(State(0, 1, vehicle), false, false, 0));
            }
            Assert.True(policy.Receive(State(0, 0, 3), false, false, 0));
            policy.Clear();
            Assert.True(policy.Receive(State(0, 0), false, false, 0));
            Assert.True(policy.Receive(State(1, 0, 1), false, false, 1));
        }

        [Fact]
        public void MalformedValuesCannotConsumeTheSendersSequence()
        {
            var policy = new VehicleStateStreamPolicy();
            Assert.False(VehicleStateStreamPolicy.IsValid(null));
            Assert.False(policy.Receive(null, true, false, 1));
            var zero = State(vehicle: 0); Assert.False(VehicleStateStreamPolicy.IsValid(zero));
            Assert.False(policy.Receive(zero, true, false, 1));
            var unknownOwner = State(255); Assert.False(VehicleStateStreamPolicy.IsValid(unknownOwner));
            Assert.False(policy.Receive(unknownOwner, true, false, 255));
            foreach (byte flags in new byte[] { 32, 64, 128, 255 })
            {
                var invalid = State(sequence: 10000); invalid.Flags = flags;
                Assert.False(VehicleStateStreamPolicy.IsValid(invalid));
                Assert.False(policy.Receive(invalid, true, false, 1));
            }
            Assert.True(policy.Receive(State(sequence: 0), true, false, 1));
            for (byte flags = 0; flags <= 31; flags++)
            {
                var valid = State(sequence: (ushort)(flags + 1)); valid.Flags = flags;
                Assert.True(VehicleStateStreamPolicy.IsValid(valid));
                Assert.True(policy.Receive(valid, true, false, 1));
            }
        }

        [Fact]
        public void CopiesAndUnchangedWirePreserveEveryFieldIncludingGear()
        {
            var original = State(2, 12345); original.Flags = 31;
            var copy = VehicleStateStreamPolicy.Copy(original);
            Assert.NotSame(original, copy);
            byte[] wire = PacketCodec.Encode(original);
            Assert.Equal(35, wire.Length); Assert.Equal(wire, PacketCodec.Encode(copy));
            Assert.Equal(wire, PacketCodec.Encode(PacketCodec.Decode(wire)));
            original.Gear = 0; original.FuelLevel = 0; original.Rpm = 0;
            Assert.Equal(4, copy.Gear); Assert.Equal(180, copy.FuelLevel); Assert.Equal(2500, copy.Rpm);
        }
    }
}
