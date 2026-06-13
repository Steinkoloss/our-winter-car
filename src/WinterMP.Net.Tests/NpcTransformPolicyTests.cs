using WinterMP.Net;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class NpcTransformPolicyTests
    {
        [Theory]
        [InlineData(0f, 8f)]
        [InlineData(80f, 8f)]
        [InlineData(81f, 3f)]
        [InlineData(200f, 3f)]
        [InlineData(201f, 0f)]
        public void GetSendRateHz_UsesDistanceTiers(float distance, float expectedHz)
        {
            Assert.Equal(expectedHz, NpcTransformPolicy.GetSendRateHz(distance));
        }

        [Fact]
        public void GetEffectiveSendRateHz_FarMovingUsesFallback()
        {
            Assert.Equal(1f, NpcTransformPolicy.GetEffectiveSendRateHz(500f, moving: true));
            Assert.Equal(0f, NpcTransformPolicy.GetEffectiveSendRateHz(500f, moving: false));
        }

        [Fact]
        public void ShouldStream_IsTrueWhenMovingEvenBeyondMidRange()
        {
            Assert.True(NpcTransformPolicy.ShouldStream(500f, moving: true));
            Assert.False(NpcTransformPolicy.ShouldStream(500f, moving: false));
        }

        [Fact]
        public void SelectSendChannel_FinalIsReliable()
        {
            Assert.Equal(Channel.ReliableOrdered, NpcTransformPolicy.SelectSendChannel(isFinal: true));
            Assert.Equal(Channel.UnreliableSequenced, NpcTransformPolicy.SelectSendChannel(isFinal: false));
        }
    }
}
