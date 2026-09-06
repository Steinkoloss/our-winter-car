using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class GuestSavePolicyTests
    {
        [Fact]
        public void AHostOrSoloBootKeepsPersistence()
        {
            var policy = new GuestSavePolicy();
            Assert.True(policy.CanHost);
            Assert.True(policy.CanPersist(false));
            Assert.True(policy.CanPersist(true));
        }

        [Fact]
        public void MissingPersistenceHooksRefuseGuestAdmissionWithoutDisablingHostSaves()
        {
            var policy = new GuestSavePolicy();
            Assert.False(policy.TryBeginGuest(false));
            Assert.True(policy.CanHost);
            Assert.True(policy.CanPersist(false));
        }

        [Fact]
        public void GuestProtectionSurvivesRetryAndDoesNotBlockMemorySerialization()
        {
            var policy = new GuestSavePolicy();
            Assert.True(policy.TryBeginGuest(true));
            Assert.False(policy.CanPersist(false));
            Assert.True(policy.CanPersist(true));
            Assert.False(policy.CanHost);
            Assert.False(policy.TryBeginGuest(false));
            Assert.True(policy.ProtectWorld);
            Assert.True(policy.TryBeginGuest(true));
            Assert.False(policy.CanPersist(false));
            Assert.False(policy.CanHost);
        }
    }
}
