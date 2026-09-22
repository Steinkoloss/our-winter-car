using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class JoinAttemptTests
    {
        [Fact]
        public void WaitingForTransportHasABoundedDeadline()
        {
            var attempt = new JoinAttempt();
            Assert.False(attempt.HasTimedOut(1000));
            attempt.Begin(10);
            Assert.Equal(JoinAttemptStage.Connecting, attempt.Stage);
            Assert.False(attempt.HasTimedOut(69.99));
            Assert.True(attempt.HasTimedOut(70));
        }

        [Fact]
        public void SlowTransportGetsAFullHandshakeWindow()
        {
            var attempt = new JoinAttempt(); attempt.Begin(0); attempt.TransportConnected(59);
            Assert.Equal(JoinAttemptStage.AwaitingHandshake, attempt.Stage);
            Assert.False(attempt.HasTimedOut(60));
            Assert.False(attempt.HasTimedOut(118.99));
            Assert.True(attempt.HasTimedOut(119));
        }

        [Theory]
        [InlineData(11)]
        [InlineData(69)]
        [InlineData(1000)]
        public void DuplicateConnectionsCannotKeepASilentHostAlive(double duplicateAt)
        {
            var attempt = new JoinAttempt(); attempt.Begin(0); attempt.TransportConnected(10);
            attempt.TransportConnected(duplicateAt);
            Assert.True(attempt.HasTimedOut(70));
        }

        [Fact]
        public void CompletionOrCancellationDisarmsBothDeadlines()
        {
            var attempt = new JoinAttempt(); attempt.Begin(0); attempt.TransportConnected(1);
            attempt.Clear(); attempt.TransportConnected(2);
            Assert.Equal(JoinAttemptStage.None, attempt.Stage);
            Assert.False(attempt.HasTimedOut(double.MaxValue));
        }

        [Fact]
        public void RetryHasAnIndependentDeadline()
        {
            var attempt = new JoinAttempt(); attempt.Begin(0); attempt.TransportConnected(1);
            Assert.True(attempt.HasTimedOut(61));
            attempt.Clear(); attempt.Begin(500);
            Assert.Equal(JoinAttemptStage.Connecting, attempt.Stage);
            Assert.False(attempt.HasTimedOut(559.99));
            Assert.True(attempt.HasTimedOut(560));
            attempt.TransportConnected(560);
            Assert.False(attempt.HasTimedOut(619.99));
            Assert.True(attempt.HasTimedOut(620));
        }
    }
}
