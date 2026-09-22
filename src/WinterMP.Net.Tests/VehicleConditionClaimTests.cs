using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleConditionClaimTests
    {
        private static VehicleCondition State(byte owner = 1) => new VehicleCondition {
            VehicleId = 42, OwnerPlayerId = owner, Sequence = 12, Availability = VehicleCondition.AvailableAll,
            TirePressure = 190, DrivetrainDamage = 2, HealthFL = 0, HealthFR = 45, HealthRL = 100, HealthRR = 255 };

        [Theory]
        [InlineData(0, 0)] [InlineData(0, 255)] [InlineData(1, 1)] [InlineData(2, 2)]
        public void CaptureCopiesOnlyCurrentAcceptedState(byte source, byte owner)
        {
            var accepted = State(source); var copy = VehicleConditionClaimPolicy.Capture(accepted, null, 42, owner);
            Assert.NotNull(copy); Assert.NotSame(accepted, copy); Assert.Equal(PacketCodec.Encode(accepted), PacketCodec.Encode(copy!));
            accepted.HealthFR = 1; Assert.Equal(45, copy!.HealthFR);
        }

        [Theory]
        [InlineData(1, 0)] [InlineData(1, 255)] [InlineData(0, 1)] [InlineData(1, 2)]
        public void WrongOwnerCannotSeedAClaim(byte source, byte owner) =>
            Assert.Null(VehicleConditionClaimPolicy.Capture(State(source), null, 42, owner));

        [Fact]
        public void MissingInvalidWrongVehicleAndGuestSnapshotCannotSeed()
        {
            Assert.Null(VehicleConditionClaimPolicy.Capture(null, null, 42, 255));
            Assert.Null(VehicleConditionClaimPolicy.Capture(State(), null, 43, 1));
            var input = State(); input.Flags = 17; Assert.Null(VehicleConditionClaimPolicy.Capture(input, null, 42, 1));
            input.Flags = 0; input.Sequence = 65535; Assert.Null(VehicleConditionClaimPolicy.Capture(input, null, 42, 1));
            input.OwnerPlayerId = 0; Assert.NotNull(VehicleConditionClaimPolicy.Capture(input, null, 42, 255));
        }

        [Fact]
        public void ApprovedParkedStateSeedsOnlyUnownedVehicleAndCannotOverrideHostWithdrawal()
        {
            var parked = State(); var result = VehicleConditionClaimPolicy.Capture(null, parked, 42, 255);
            Assert.NotNull(result); Assert.NotSame(parked, result);
            Assert.Null(VehicleConditionClaimPolicy.Capture(null, parked, 42, 2));
            Assert.Null(VehicleConditionClaimPolicy.Capture(null, parked, 43, 255));
            var host = State(0); host.Sequence = 65535; host.Availability = 0;
            Assert.Equal(0, VehicleConditionClaimPolicy.Capture(host, parked, 42, 255)!.Availability);
        }

        [Theory]
        [InlineData(3, 3, true, 255, true)] [InlineData(3, 3, false, 255, false)]
        [InlineData(3, 4, true, 255, false)] [InlineData(3, 3, true, 1, false)]
        [InlineData(3, 3, true, 0, false)] [InlineData(0, 0, true, 255, false)]
        [InlineData(255, 255, true, 255, false)]
        public void OnlyTheClaimingGuestWithCurrentLocalOwnershipCanRead(byte claimant, byte local, bool owned, byte remote, bool allowed)
        {
            Assert.Equal(allowed, VehicleConditionClaimPolicy.CanRead(State(), 42, claimant, local, owned, remote));
            Assert.False(VehicleConditionClaimPolicy.CanRead(State(), 43, claimant, local, owned, remote));
            Assert.False(VehicleConditionClaimPolicy.CanRead(null, 42, claimant, local, owned, remote));
        }

        [Theory]
        [InlineData(0, 0)] [InlineData(1, 45)] [InlineData(2, 100)] [InlineData(3, 255)]
        public void ReadsPreserveKnownZeroAndByteRange(int wheel, byte expected)
        {
            var input = State(); var before = PacketCodec.Encode(input);
            Assert.True(VehicleConditionClaimPolicy.TryGetHealth(input, wheel, out var health)); Assert.Equal(expected, health);
            Assert.True(VehicleConditionClaimPolicy.TryGetDrivetrain(input, out var damage)); Assert.Equal(2, damage);
            Assert.Equal(before, PacketCodec.Encode(input));
            input.Availability = 0; Assert.False(VehicleConditionClaimPolicy.TryGetHealth(input, wheel, out _));
            Assert.False(VehicleConditionClaimPolicy.TryGetDrivetrain(input, out _));
        }

        [Theory]
        [InlineData(-1)] [InlineData(4)] [InlineData(int.MaxValue)]
        public void UnknownWheelCannotReadAnotherInput(int wheel) => Assert.False(VehicleConditionClaimPolicy.TryGetHealth(State(), wheel, out _));

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)] [InlineData(16)] [InlineData(32)] [InlineData(63)]
        public void CaptureIntersectsNativeReadinessAndRetainedAvailability(byte available)
        {
            var input = State(); input.Availability = available; var before = PacketCodec.Encode(input);
            var captured = State(3); captured.Sequence = 401; captured.TirePressure = 230;
            captured.HealthFL = captured.HealthFR = captured.HealthRL = captured.HealthRR = 90; captured.DrivetrainDamage = 3; captured.Flags = 15;
            VehicleConditionClaimPolicy.ApplyToCapture(captured, input);
            Assert.Equal((byte)(available | 1), captured.Availability); Assert.Equal(230, captured.TirePressure);
            Assert.Equal(captured.HasDrivetrain ? 2 : 0, captured.DrivetrainDamage);
            Assert.Equal(captured.HasWheel(0) ? input.HealthFL : 0, captured.HealthFL);
            Assert.Equal(captured.HasWheel(1) ? input.HealthFR : 0, captured.HealthFR);
            Assert.Equal(captured.HasWheel(2) ? input.HealthRL : 0, captured.HealthRL);
            Assert.Equal(captured.HasWheel(3) ? input.HealthRR : 0, captured.HealthRR);
            Assert.Equal((available >> 2) & 15, captured.Flags);
            Assert.Equal(3, captured.OwnerPlayerId); Assert.Equal(401, captured.Sequence); Assert.Equal(before, PacketCodec.Encode(input));
            Assert.Equal(PacketCodec.Encode(captured), PacketCodec.Encode(PacketCodec.Decode(PacketCodec.Encode(captured))));
            captured.Availability = 0; VehicleConditionClaimPolicy.ApplyToCapture(captured, input); Assert.Equal(0, captured.Availability);
        }

        [Fact]
        public void InvalidOrWrongVehicleInputCannotRewriteCapture()
        {
            var captured = State(3); var before = PacketCodec.Encode(captured); var input = State(); input.VehicleId = 43;
            VehicleConditionClaimPolicy.ApplyToCapture(captured, input); Assert.Equal(before, PacketCodec.Encode(captured));
            input.VehicleId = 42; input.Availability = 128; VehicleConditionClaimPolicy.ApplyToCapture(captured, input);
            Assert.Equal(before, PacketCodec.Encode(captured));
        }
    }
}
