using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ParkedVehicleConditionTests
    {
        private static VehicleCondition Condition(byte owner = 1) => new VehicleCondition { Availability = VehicleCondition.AvailableAll,
            VehicleId = 12, OwnerPlayerId = owner, Sequence = 7, TirePressure = 190, DrivetrainDamage = 2,
            HealthFL = 0, HealthFR = 70, HealthRL = 100, HealthRR = 255, Flags = 1 };
        private static ItemTransform Release(byte owner = 1) => new ItemTransform {
            ItemId = 12, OwnerPlayerId = owner, Sequence = 99, Flags = ItemTransform.FlagFinal };

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(254)]
        public void EstablishedOwnerFinalCopiesWholeAcceptedConditionWithoutPromotingItsSender(byte owner)
        {
            var state = Condition(owner); var bytes = PacketCodec.Encode(state);
            var parked = Assert.IsType<VehicleCondition>(VehicleConditionStreamPolicy.CaptureReleasedCondition(state, owner, Release(owner)));
            Assert.NotSame(state, parked); Assert.Equal(bytes, PacketCodec.Encode(parked));
            state.HealthFR = 1; Assert.Equal(70, parked.HealthFR);
            Assert.Equal(owner, parked.OwnerPlayerId); Assert.Equal(7, parked.Sequence);
        }

        [Theory]
        [InlineData(255, 1)] [InlineData(2, 1)] [InlineData(1, 2)] [InlineData(0, 1)] [InlineData(1, 255)]
        public void UnownedAndOtherActorFinalsCannotApproveAParkedRecord(byte previous, byte releaseOwner) =>
            Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(Condition(), previous, Release(releaseOwner)));

        [Theory]
        [InlineData(0)] [InlineData(ItemTransform.FlagVehicle)] [InlineData(ItemTransform.FlagFinal | ItemTransform.FlagDriver)]
        [InlineData(ItemTransform.FlagFinal | ItemTransform.FlagVehicle | ItemTransform.FlagDriver)]
        public void OnlyNonDriverFinalPosesCanApproveParking(byte flags)
        {
            var release = Release(); release.Flags = flags;
            Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(Condition(), 1, release));
        }

        [Theory]
        [InlineData(ItemTransform.FlagFinal)] [InlineData(ItemTransform.FlagFinal | ItemTransform.FlagVehicle)]
        public void ApprovedReleaseDoesNotRequireTheLiveVehicleStreamFlag(byte flags)
        {
            var release = Release(); release.Flags = flags;
            Assert.NotNull(VehicleConditionStreamPolicy.CaptureReleasedCondition(Condition(), 1, release));
        }

        [Fact]
        public void MissingWrongVehicleAndInvalidConditionNeverCreateAParkedRecord()
        {
            Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(null, 1, Release()));
            Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(Condition(), 1, null));
            var state = Condition(); state.VehicleId = 13;
            Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1, Release()));
            state = Condition(2); Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1, Release()));
            state = Condition(); state.Flags = 17; Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1, Release()));
            state = Condition(); state.VehicleId = 0; var release = Release(); release.ItemId = 0;
            Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1, release));
        }

        [Fact]
        public void OnlyHostSnapshotMayServeAsTheLastAcceptedCondition()
        {
            var state = Condition(); state.Sequence = VehicleCondition.SnapshotSequence;
            Assert.Null(VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1, Release()));
            state.OwnerPlayerId = 0;
            Assert.NotNull(VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 0, Release(0)));
        }

        [Theory]
        [InlineData(0, 0)] [InlineData(1, 70)] [InlineData(2, 100)] [InlineData(3, 255)]
        public void ParkedObserversReadEachRetainedWheelIncludingKnownZero(int wheel, byte expected)
        {
            var parked = VehicleConditionStreamPolicy.CaptureReleasedCondition(Condition(), 1, Release());
            Assert.True(VehicleConditionStreamPolicy.TryGetParkedObserverHealth(parked, 12, false, 255, wheel, out byte health));
            Assert.Equal(expected, health);
        }

        [Theory]
        [InlineData(true, 255)] [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
        public void OwnershipOrLocalDriverStopsParkedReads(bool local, byte owner)
        {
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverHealth(Condition(), 12, local, owner, 0, out byte health));
            Assert.Equal(0, health);
        }

        [Theory]
        [InlineData(-1)] [InlineData(4)]
        public void InvalidWheelCannotBorrowAnotherSlot(int wheel) =>
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverHealth(Condition(), 12, false, 255, wheel, out _));

        [Fact]
        public void MissingOrWrongCarParkedStateIsUnavailable()
        {
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverHealth(null, 12, false, 255, 0, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverHealth(Condition(), 13, false, 255, 0, out _));
        }

        [Fact]
        public void RetainingAReleaseDoesNotAllowUnownedReportsOrResetLiveHistory()
        {
            var stream = new VehicleConditionStreamPolicy(); var state = Condition();
            Assert.True(stream.Receive(state, false, false, 1));
            Assert.NotNull(VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1, Release()));
            var newer = Condition(); newer.Sequence++;
            Assert.False(stream.Receive(newer, false, false, 255));
            Assert.False(stream.Receive(state, false, false, 1));
            Assert.True(stream.Receive(newer, false, false, 1));
        }
    }
}
