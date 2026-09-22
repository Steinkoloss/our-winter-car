using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class DeathSessionPolicyTests
    {
        [Fact]
        public void NormalDeathsDoNotEndTheRunOrBlockRecovery()
        {
            var policy = new DeathSessionPolicy();
            Assert.False(policy.TryWipe(false));
            Assert.True(policy.CanJoin);
            Assert.True(policy.CanRespawn(false));
        }

        [Fact]
        public void FirstPermanentDeathEndsTheRunOnce()
        {
            var policy = new DeathSessionPolicy();
            Assert.False(policy.CanRespawn(true));
            Assert.True(policy.CanJoin);
            Assert.True(policy.TryWipe(true));
            Assert.True(policy.Wiped);
            Assert.False(policy.CanJoin);
            Assert.False(policy.CanRespawn(true));
            Assert.False(policy.TryWipe(true));
            Assert.False(policy.TryWipe(true));
        }

        [Fact]
        public void SettingsChangesCannotReviveAWipedWorld()
        {
            var policy = new DeathSessionPolicy();
            policy.TryWipe(true);
            Assert.False(policy.TryWipe(false));
            Assert.False(policy.CanRespawn(false));
            Assert.False(policy.CanJoin);
            Assert.True(policy.Wiped);
        }

        [Fact]
        public void OnlyANewTransportSessionReopensTheRun()
        {
            var policy = new DeathSessionPolicy();
            policy.TryWipe(true);
            policy.Reset();
            Assert.False(policy.Wiped);
            Assert.True(policy.CanJoin);
            Assert.True(policy.CanRespawn(false));
            Assert.False(policy.CanRespawn(true));
            Assert.True(policy.TryWipe(true));
        }
    }
}
