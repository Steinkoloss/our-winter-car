using System.Reflection;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleClimateStreamPolicyTests
    {
        private static VehicleClimate State(byte owner = 1, ushort sequence = 1, uint id = 91) => new VehicleClimate {
            VehicleId = id, OwnerPlayerId = owner, Sequence = sequence, Flags = 7, Frost = 11, Fog = 22,
            CabinTemp = 33, HeaterTemp = 44, HeaterBlower = 55, HeaterDirection = 66,
            Ice = 77, IceSideLeft = 88, IceSideRight = 99, IceDoorLeft = 110, IceDoorRight = 121, IceRear = 132, IceMask = 63 };

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(40000)] [InlineData(65534)]
        public void FirstSequenceHasNoInventedBaseline(ushort sequence)
        {
            var p = new VehicleClimateStreamPolicy();
            Assert.True(p.Receive(State(sequence: sequence), true, false, 1));
            Assert.False(p.Receive(State(sequence: sequence), true, false, 1));
        }

        [Theory]
        [InlineData(0, 1, true)] [InlineData(0, 32767, true)] [InlineData(0, 32768, false)]
        [InlineData(1, 1, false)] [InlineData(5, 4, false)] [InlineData(65534, 0, true)] [InlineData(65534, 1, true)]
        public void DuplicateStaleAndHalfRangePacketsCannotAdvanceTheOwner(ushort previous, ushort next, bool expected)
        {
            var p = new VehicleClimateStreamPolicy(); Assert.True(p.Receive(State(sequence: previous), true, false, 1));
            Assert.Equal(expected, p.Receive(State(sequence: next), true, false, 1));
        }

        [Theory]
        [InlineData(true, false, 1, 1, true)] [InlineData(true, false, 1, 2, false)]
        [InlineData(true, false, 255, 1, false)] [InlineData(true, false, 255, 0, false)]
        [InlineData(true, false, 0, 0, false)] [InlineData(true, true, 1, 1, false)]
        [InlineData(false, false, 255, 0, true)] [InlineData(false, false, 0, 0, true)]
        [InlineData(false, false, 1, 1, true)] [InlineData(false, false, 1, 0, false)]
        [InlineData(false, false, 1, 2, false)] [InlineData(false, false, 255, 1, false)]
        [InlineData(false, true, 0, 0, false)] [InlineData(false, true, 1, 1, false)]
        [InlineData(false, false, 255, 255, false)]
        public void CurrentVehicleOwnershipGatesBothArrivalAndPresentation(bool host, bool local, byte owner, byte sender, bool expected)
        {
            Assert.Equal(expected, VehicleClimateStreamPolicy.CanPresent(sender, host, local, owner));
            Assert.Equal(expected, new VehicleClimateStreamPolicy().Receive(State(sender), host, local, owner));
        }

        [Theory]
        [InlineData(true, false, 1, 1, false)] [InlineData(true, false, 255, 0, false)]
        [InlineData(false, false, 255, 0, true)] [InlineData(false, false, 0, 0, true)]
        [InlineData(false, false, 1, 0, false)] [InlineData(false, false, 1, 1, false)]
        [InlineData(false, true, 255, 0, false)] [InlineData(false, false, 255, 1, false)]
        public void OnlyHostSnapshotsForUnownedOrHostOwnedCarsAreAccepted(bool host, bool local, byte owner, byte sender, bool expected)
            => Assert.Equal(expected, new VehicleClimateStreamPolicy().Receive(State(sender, VehicleClimate.SnapshotSequence), host, local, owner));

        [Fact]
        public void SnapshotsDoNotAdvanceLiveHistory()
        {
            var p = new VehicleClimateStreamPolicy();
            Assert.True(p.Receive(State(0, VehicleClimate.SnapshotSequence), false, false, 255));
            Assert.True(p.Receive(State(0, 0), false, false, 255));
            Assert.True(p.Receive(State(0, VehicleClimate.SnapshotSequence), false, false, 0));
            Assert.False(p.Receive(State(0, 0), false, false, 0)); Assert.True(p.Receive(State(0, 1), false, false, 0));
        }

        [Fact]
        public void HandoffsRetainEachReturningOwnersHistory()
        {
            var p = new VehicleClimateStreamPolicy();
            Assert.True(p.Receive(State(1, 100), true, false, 1)); Assert.True(p.Receive(State(2, 0), true, false, 2));
            Assert.False(p.Receive(State(1, 6000), true, false, 2));
            Assert.False(p.Receive(State(1, 99), true, false, 1)); Assert.True(p.Receive(State(1, 101), true, false, 1));
            Assert.False(p.Receive(State(2, 0), true, false, 2)); Assert.True(p.Receive(State(2, 1), true, false, 2));
        }

        [Fact]
        public void RejectedUnownedAndLocalDriverPacketsDoNotConsumeHistory()
        {
            var p = new VehicleClimateStreamPolicy();
            Assert.False(p.Receive(State(1, 1), true, false, 255)); Assert.False(p.Receive(State(1, 1), true, true, 1));
            Assert.True(p.Receive(State(1, 1), true, false, 1));
        }

        [Fact]
        public void ForgetAndClearRestartOnlyTheIntendedBaselines()
        {
            var p = new VehicleClimateStreamPolicy();
            Assert.True(p.Receive(State(1, 100), true, false, 1)); Assert.True(p.Receive(State(1, 100, 92), true, false, 1));
            Assert.True(p.Receive(State(2, 100), true, false, 2)); p.ForgetPlayer(1);
            Assert.True(p.Receive(State(1, 0), true, false, 1)); Assert.True(p.Receive(State(1, 0, 92), true, false, 1));
            Assert.False(p.Receive(State(2, 0), true, false, 2)); p.Clear(); Assert.True(p.Receive(State(2, 0), true, false, 2));
        }

        [Theory]
        [InlineData("id")] [InlineData("owner")] [InlineData("flags")] [InlineData("mask")] [InlineData("unavailable pane")]
        public void InvalidReportsCannotPoisonAcceptedSequence(string defect)
        {
            var p = new VehicleClimateStreamPolicy(); var invalid = State();
            if (defect == "id") invalid.VehicleId = 0;
            else if (defect == "owner") invalid.OwnerPlayerId = 255;
            else if (defect == "flags") invalid.Flags = 16;
            else if (defect == "mask") invalid.IceMask = 128;
            else invalid.IceMask = 0;
            Assert.False(VehicleClimateStreamPolicy.IsValid(invalid)); Assert.False(p.Receive(invalid, true, false, 1));
            Assert.True(p.Receive(State(), true, false, 1));
        }

        [Fact]
        public void NullIsRejectedAndCopiesRetainEveryFieldIndependently()
        {
            Assert.False(VehicleClimateStreamPolicy.IsValid(null)); Assert.False(new VehicleClimateStreamPolicy().Receive(null, true, false, 1));
            var original = State(); var copy = VehicleClimateStreamPolicy.Copy(original);
            foreach (var field in typeof(VehicleClimate).GetFields(BindingFlags.Instance | BindingFlags.Public))
                Assert.Equal(field.GetValue(original), field.GetValue(copy));
            original.IceRear = 0; original.HeaterBlower = 0;
            Assert.Equal(132, copy.IceRear); Assert.Equal(55, copy.HeaterBlower);
        }
    }
}
