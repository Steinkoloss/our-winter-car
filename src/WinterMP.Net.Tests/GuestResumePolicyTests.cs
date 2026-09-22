using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class GuestResumePolicyTests
    {
        [Fact]
        public void LoadingAndUnansweredReturningOfferCannotPublish()
        {
            var gate = new GuestResumePolicy();
            Assert.False(gate.CanPublish(true));
            gate.Choose(); gate.CompleteRelocation();
            Assert.False(gate.CanPublish(true));
            Assert.True(gate.ReceiveOffer(true));
            gate.CompleteRelocation();
            Assert.False(gate.CanPublish(true));
            gate.Choose();
            Assert.False(gate.CanPublish(true));
            gate.CompleteRelocation();
            Assert.True(gate.CanPublish(true));
            Assert.False(gate.CanPublish(false));
        }

        [Fact]
        public void NewGuestWaitsForHostRelocationWithoutNeedingAChoice()
        {
            var gate = new GuestResumePolicy();
            Assert.True(gate.ReceiveOffer(false));
            Assert.False(gate.CanPublish(true));
            gate.CompleteRelocation();
            Assert.True(gate.CanPublish(true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RepairSnapshotsCannotReplaceTheOfferOrRespawnAPlayingGuest(bool returning)
        {
            var gate = new GuestResumePolicy();
            Assert.True(gate.ReceiveOffer(returning));
            Assert.False(gate.ReceiveOffer(!returning));
            gate.Choose(); gate.CompleteRelocation();
            Assert.False(gate.ReceiveOffer(true));
            Assert.True(gate.CanPublish(true));
            gate.Reset();
            Assert.False(gate.CanPublish(true));
            Assert.True(gate.ReceiveOffer(true));
            Assert.False(gate.CanPublish(true));
        }

        [Fact]
        public void NativeSaveBlocksTeardownPosesAndLateSnapshotOffersUntilReset()
        {
            var gate = new GuestResumePolicy();
            gate.ReceiveOffer(false); gate.CompleteRelocation();
            gate.LeaveWorld();
            Assert.False(gate.CanPublish(true));
            Assert.False(gate.ReceiveOffer(true));
            gate.Choose(); gate.CompleteRelocation();
            Assert.False(gate.CanPublish(true));
            gate.Reset();
            Assert.True(gate.ReceiveOffer(true));
        }
    }
}
