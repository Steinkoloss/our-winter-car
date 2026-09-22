using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class WoodstoveAdmissionTests
    {
        private static WoodstoveFuelUpdate Admit(uint high = 8) => new WoodstoveFuelUpdate {
            Actor = 2, HighWater = high, Snapshot = new WoodstoveFuelSnapshot(WoodstoveFuelAuthority.CabinSourceId,
                7, 3, new WoodstoveFuelValues(1, .18f, false), new uint[] { 51 }),
            ResourceId = 52, Shape = 1, Rotation = NetQuaternion.Identity };
        [Fact]
        public void RealClientAdmissionRetainsHighWaterLiveIdentityAndRetirementAcrossRejoin()
        {
            var client = new WoodstoveFuelClient(2);
            Assert.False(client.Receive(false, Admit())); Assert.Null(client.Create(52));
            Assert.True(client.Receive(true, Admit()));
            Assert.Null(client.Create(51)); // retired; neither a new identity nor permission to spend
            Assert.Null(client.Create(53)); // unadmitted
            var request = client.Create(52)!; Assert.Equal(9u, request.Sequence);
            Assert.Equal(7u, request.Epoch); Assert.Equal(52u, request.ResourceId);
            Assert.True(client.Receive(true, Admit(3))); Assert.Equal(9u, client.HighWater);
            var rejoin = new WoodstoveFuelClient(2); Assert.True(rejoin.Receive(true, Admit(9)));
            Assert.Equal(10u, rejoin.Create(52)!.Sequence);
            var stale = Admit(); stale.Snapshot = new WoodstoveFuelSnapshot(WoodstoveFuelAuthority.CabinSourceId,
                6, 99, new WoodstoveFuelValues(4, .5f, true), new uint[0]);
            Assert.False(rejoin.Receive(true, stale)); Assert.Equal(7u, rejoin.Epoch);
        }
        [Fact]
        public void SnapshotOvertakesMatchingResultWithoutLosingAcknowledgementOrResurrectingFuel()
        {
            var client = new WoodstoveFuelClient(2); client.Receive(true, Admit());
            var request = client.Create(52)!;
            var final = new WoodstoveFuelUpdate { Actor = 2, Sequence = request.Sequence, HighWater = request.Sequence,
                IsDecision = true, Status = WoodstoveFeedStatus.Accepted,
                Snapshot = new WoodstoveFuelSnapshot(WoodstoveFuelAuthority.CabinSourceId, 7, 4,
                    new WoodstoveFuelValues(2, .18f, true), new uint[] { 51, 52 }) };
            var later = new WoodstoveFuelUpdate { Snapshot = new WoodstoveFuelSnapshot(WoodstoveFuelAuthority.CabinSourceId,
                7, 5, new WoodstoveFuelValues(1, .18f, true), new uint[] { 51, 52 }) };
            Assert.True(client.Receive(true, later));
            Assert.False(client.Receive(true, final));
            Assert.Equal(request.Sequence, client.LastDecision!.Sequence);
            Assert.Equal(1, client.Current!.Values.Fuel); Assert.Null(client.Create(52));
            Assert.False(client.Receive(true, final)); // duplicate acknowledgement grants no new side effects
            Assert.Equal(request.Sequence, client.LastDecision.Sequence);
            Assert.False(client.Receive(true, Admit())); Assert.Null(client.Create(51));
        }
        [Fact]
        public void IdentityShapeCannotBeReboundAndSequenceCannotWrap()
        {
            var client = new WoodstoveFuelClient(2); client.Receive(true, Admit(uint.MaxValue));
            Assert.Null(client.Create(52));
            var changed = Admit(); changed.Shape = 2;
            Assert.False(client.Receive(true, changed));
        }

        [Theory]
        [InlineData(WoodstoveFeedStatus.Busy)] [InlineData(WoodstoveFeedStatus.ReplayedSequence)]
        public void DuplicateDenialDoesNotReleaseConfirmedPendingFeed(WoodstoveFeedStatus denial)
        {
            var client = new WoodstoveFuelClient(2); client.Receive(true, Admit());
            var request = client.Create(52)!;
            var pending = new WoodstoveFuelUpdate { Epoch = 7, Actor = 2, IsDecision = true,
                Sequence = request.Sequence, HighWater = request.Sequence, Status = WoodstoveFeedStatus.Pending };
            Assert.False(client.Receive(true, pending)); // acknowledgment only; no snapshot to apply
            pending.Status = denial;
            Assert.False(client.Receive(true, pending));
            Assert.Null(client.Create(52)); Assert.Null(client.LastDecision);
            var final = new WoodstoveFuelUpdate { Epoch = 7, Actor = 2, IsDecision = true,
                Sequence = request.Sequence, HighWater = request.Sequence, Status = WoodstoveFeedStatus.Accepted,
                Snapshot = new WoodstoveFuelSnapshot(WoodstoveFuelAuthority.CabinSourceId, 7, 4,
                    new WoodstoveFuelValues(2, .18f, true), new uint[] { 51, 52 }) };
            Assert.True(client.Receive(true, final)); Assert.Same(final, client.LastDecision);
            Assert.Equal(2, client.Current!.Values.Fuel); Assert.Null(client.Create(52));
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void FinalNativeFailureAndUnreservedBusyStillReleaseOnlyMatchingAttempt(bool wasPending)
        {
            var client = new WoodstoveFuelClient(2); client.Receive(true, Admit());
            var request = client.Create(52)!;
            var update = new WoodstoveFuelUpdate { Epoch = 7, Actor = 2, IsDecision = true,
                Sequence = request.Sequence, HighWater = request.Sequence, Status = WoodstoveFeedStatus.Pending };
            if (wasPending) client.Receive(true, update);
            update.Status = wasPending ? WoodstoveFeedStatus.NativeFailure : WoodstoveFeedStatus.Busy;
            Assert.False(client.Receive(true, update)); Assert.Same(update, client.LastDecision);
            Assert.Equal(request.Sequence + 1, client.Create(52)!.Sequence);
            Assert.Equal(1, client.Current!.Values.Fuel); // failures do not invent state
        }
    }
}
