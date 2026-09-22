using System;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    // Portable adapter doubles, not Unity/native execution evidence.
    public class WoodstoveFuelTests
    {
        private const uint Epoch = 71, Log = 101;
        private static uint Source => WoodstoveFuelAuthority.CabinSourceId;
        private static WoodstoveFeedRequest Request(byte actor = 2, uint seq = 1, uint resource = Log)
            => new WoodstoveFeedRequest(Source, Epoch, actor, seq, resource);

        private sealed class Host : IWoodstoveFuelHost
        {
            public int Fuel = 1, Calls;
            public float Heat = .4f;
            public bool Lit = true, Available = true, Contact = true, Equipped = true, Alive = true, Ready = true, Firewood = true;
            public float Age = .1f, Distance = 1;
            public byte Holder = 2;
            public uint Resource = Log;
            public bool Throw, WrongDelta;
            public Action? DuringFeed;
            public WoodstoveFuelValues ReadFuel() => new WoodstoveFuelValues(Fuel, Heat, Lit);
            public WoodstoveFeedContext Observe(byte actor, uint resource)
                => new WoodstoveFeedContext { ActorPresent = true, ActorAlive = Alive, PoseAgeSeconds = Age,
                    DistanceSquared = Distance, SourceReady = Ready, ResourceId = Resource,
                    ResourceAvailable = Available, ResourceAtSource = Contact, ResourceAvailableToActor = Holder == actor, EquipmentReady = Equipped,
                    ResourceIsFirewood = Firewood };
            public void FeedOne(uint resource)
            {
                Calls++; DuringFeed?.Invoke();
                if (Throw) throw new InvalidOperationException("portable native-adapter fault");
                Assert.Equal(Resource, resource);
                Available = false; Fuel += WrongDelta ? 2 : 1;
            }
            public bool IsConsumed(uint resource) => !Available;
        }

        [Fact]
        public void AcceptedGuestConsumesOneAndAbsoluteResultConvergesWithoutPrediction()
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            var replica = new WoodstoveFuelReplica(Epoch);
            Assert.True(replica.Receive(true, authority.Capture()));
            var request = Request(); // Sending a request does not write either canonical state.
            Assert.Equal(1, replica.Current!.Values.Fuel); Assert.Equal(0, host.Calls);
            var result = authority.Decide(2, request);
            Assert.Equal(WoodstoveFeedStatus.Accepted, result.Status);
            Assert.Equal(1, host.Calls); Assert.Equal(2, host.Fuel);
            Assert.True(replica.Receive(true, result.Snapshot!));
            Assert.Equal(host.Fuel, replica.Current!.Values.Fuel);
            Assert.Equal(host.Heat, replica.Current.Values.Heat);
            Assert.Equal(new uint[] { Log }, replica.Current.ConsumedResources);
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, authority.Decide(2, request).Status);
            Assert.Equal(1, host.Calls);
        }

        [Fact]
        public void CompetingActorsCannotSpendTheSameResourceEvenIfAdapterOffersItAgain()
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Decide(2, Request()).Status);
            host.Available = true; host.Holder = 3;
            var result = authority.Decide(3, Request(3));
            Assert.Equal(WoodstoveFeedStatus.ResourceConsumed, result.Status);
            Assert.Equal(1, host.Calls); Assert.Equal(2, result.Snapshot!.Values.Fuel);
        }

        [Theory]
        [InlineData(0, WoodstoveFeedStatus.InvalidActor)]
        [InlineData(1, WoodstoveFeedStatus.StaleEpoch)]
        [InlineData(2, WoodstoveFeedStatus.WrongSource)]
        [InlineData(3, WoodstoveFeedStatus.InvalidResource)]
        [InlineData(4, WoodstoveFeedStatus.InvalidResource)]
        [InlineData(5, WoodstoveFeedStatus.OutOfRange)]
        [InlineData(6, WoodstoveFeedStatus.InvalidEquipment)]
        [InlineData(7, WoodstoveFeedStatus.InvalidContact)]
        [InlineData(8, WoodstoveFeedStatus.ActorUnavailable)]
        [InlineData(9, WoodstoveFeedStatus.ActorUnavailable)]
        [InlineData(10, WoodstoveFeedStatus.SourceUnavailable)]
        [InlineData(11, WoodstoveFeedStatus.ReplayedSequence)]
        public void InvalidRequestsHaveNoFuelResourceOrHeatEffects(int kind, WoodstoveFeedStatus expected)
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            var before = authority.Capture(); var request = Request(); byte actor = 2;
            switch (kind)
            {
                case 0: actor = 3; break;
                case 1: request = new WoodstoveFeedRequest(Source, Epoch - 1, 2, 1, Log); break;
                case 2: request = new WoodstoveFeedRequest(Source + 1, Epoch, 2, 1, Log); break;
                case 3: request = Request(resource: 0); break;
                case 4: host.Available = false; break;
                case 5: host.Distance = 9.01f; break;
                case 6: host.Equipped = false; break;
                case 7: host.Contact = false; break;
                case 8: host.Alive = false; break;
                case 9: host.Age = 2.01f; break;
                case 10: host.Ready = false; break;
                case 11: request = Request(seq: 0); break;
            }
            var result = authority.Decide(actor, request);
            Assert.Equal(expected, result.Status); Assert.Equal(0, host.Calls);
            Assert.Equal(1, host.Fuel); Assert.Equal(.4f, host.Heat);
            Assert.Equal(before.Revision, result.Snapshot!.Revision);
            Assert.Empty(result.Snapshot.ConsumedResources);
        }

        [Fact]
        public void DeniedAttemptCannotBecomeValidWhenReplayedAndRejoinResumesAboveHighWater()
        {
            var host = new Host { Contact = false }; var authority = new WoodstoveFuelAuthority(Epoch, host);
            Assert.Equal(WoodstoveFeedStatus.InvalidContact, authority.Decide(2, Request(seq: 8)).Status);
            host.Contact = true;
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, authority.Decide(2, Request(seq: 8)).Status);
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, authority.Decide(2, Request(seq: 7)).Status);
            Assert.Equal(8u, authority.Seen(2)); // Retained authority across same-player rejoin.
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Decide(2, Request(seq: authority.Seen(2) + 1)).Status);
            Assert.Equal(1, host.Calls);
        }

        [Fact]
        public void SnapshotOvertakingResultCannotRestoreFuelOrResurrectConsumedResource()
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            var old = authority.Capture(); var result = authority.Decide(2, Request());
            host.Fuel = 1; host.Heat = .2f; // Host vanilla burn, not invented persistence.
            var fresh = authority.Capture(); var replica = new WoodstoveFuelReplica(Epoch);
            Assert.True(replica.Receive(true, fresh));
            Assert.False(replica.Receive(true, result.Snapshot!));
            Assert.False(replica.Receive(true, old));
            Assert.Equal(1, replica.Current!.Values.Fuel); Assert.Equal(.2f, replica.Current.Values.Heat);
            Assert.Equal(new uint[] { Log }, replica.Current.ConsumedResources);
            var rejoin = new WoodstoveFuelReplica(Epoch);
            Assert.True(rejoin.Receive(true, authority.Capture()));
            Assert.Equal(replica.Current.Revision, rejoin.Current!.Revision);
            Assert.False(rejoin.Receive(false, fresh));
            Assert.False(rejoin.Receive(true, new WoodstoveFuelAuthority(Epoch + 1, host).Capture()));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1f)]
        public void InvalidHostFuelHeatAndPoseFailClosed(float invalid)
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            host.Distance = invalid;
            Assert.Equal(WoodstoveFeedStatus.OutOfRange, authority.Decide(2, Request()).Status);
            host.Distance = 0; host.Age = invalid;
            Assert.Equal(WoodstoveFeedStatus.ActorUnavailable, authority.Decide(2, Request(seq: 2)).Status);
            host.Age = 0; host.Heat = invalid;
            Assert.Equal(WoodstoveFeedStatus.NativeFailure, authority.Decide(2, Request(seq: 3)).Status);
            Assert.Equal(0, host.Calls); Assert.True(authority.Faulted);
        }

        [Theory]
        [InlineData(-1)] [InlineData(int.MaxValue)]
        public void InvalidFuelNeverInvokesMutator(int fuel)
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host); host.Fuel = fuel;
            Assert.NotEqual(WoodstoveFeedStatus.Accepted, authority.Decide(2, Request()).Status);
            Assert.Equal(0, host.Calls);
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void PartialNativeFailureIsNotAcceptedOrRetried(bool wrongDelta)
        {
            var host = new Host { Throw = !wrongDelta, WrongDelta = wrongDelta };
            var authority = new WoodstoveFuelAuthority(Epoch, host);
            Assert.Equal(WoodstoveFeedStatus.NativeFailure, authority.Decide(2, Request()).Status);
            Assert.True(authority.Faulted);
            Assert.Equal(WoodstoveFeedStatus.NativeFailure, authority.Decide(2, Request(seq: 2)).Status);
            Assert.Equal(1, host.Calls);
        }

        [Fact]
        public void ReentrantFeedIsRejectedAndSnapshotsDoNotAliasMutableTombstones()
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            host.DuringFeed = () => Assert.Equal(WoodstoveFeedStatus.Busy, authority.Decide(2, Request(seq: 2)).Status);
            var result = authority.Decide(2, Request());
            Assert.Equal(WoodstoveFeedStatus.Accepted, result.Status);
            result.Snapshot!.ConsumedResources[0] = 999;
            Assert.Equal(Log, authority.Capture().ConsumedResources[0]); Assert.Equal(1, host.Calls);
        }

        [Fact]
        public void ResourceIdentityAloneDoesNotAuthorizeBurningANonFirewoodItem()
        {
            var host = new Host { Firewood = false }; var authority = new WoodstoveFuelAuthority(Epoch, host);
            var result = authority.Decide(2, Request());
            Assert.Equal(WoodstoveFeedStatus.InvalidResource, result.Status);
            Assert.Equal(0, host.Calls); Assert.True(host.Available);
            Assert.Equal(1, host.Fuel); Assert.Empty(result.Snapshot!.ConsumedResources);
        }

        [Fact]
        public void InvalidReplicaSnapshotsCannotReplaceCanonicalValuesOrResurrectWood()
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            var accepted = authority.Decide(2, Request()).Snapshot!;
            var replica = new WoodstoveFuelReplica(Epoch);
            Assert.True(replica.Receive(true, accepted));
            var candidates = new[] {
                new WoodstoveFuelSnapshot(Source + 1, Epoch, 8, accepted.Values, new uint[] { Log }),
                new WoodstoveFuelSnapshot(Source, Epoch, 0, accepted.Values, new uint[] { Log }),
                new WoodstoveFuelSnapshot(Source, Epoch, 8, new WoodstoveFuelValues(-1, .2f, true), new uint[] { Log }),
                new WoodstoveFuelSnapshot(Source, Epoch, 8, new WoodstoveFuelValues(2, float.NaN, true), new uint[] { Log }),
                new WoodstoveFuelSnapshot(Source, Epoch, 8, accepted.Values, new uint[0]),
                new WoodstoveFuelSnapshot(Source, Epoch, 8, accepted.Values, new uint[] { Log, Log }),
                new WoodstoveFuelSnapshot(Source, Epoch, 8, accepted.Values, new uint[] { 0, Log }),
                new WoodstoveFuelSnapshot(Source, Epoch, accepted.Revision, new WoodstoveFuelValues(3, .4f, true), new uint[] { Log }),
                new WoodstoveFuelSnapshot(Source, Epoch, accepted.Revision, accepted.Values, new uint[] { Log, Log + 1 })
            };
            foreach (var candidate in candidates)
            {
                Assert.False(replica.Receive(true, candidate));
                Assert.Same(accepted, replica.Current);
            }
        }

        [Fact]
        public void AuthenticatedButWrongResourceAndCompetingHolderRemainSideEffectFree()
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            Assert.Equal(WoodstoveFeedStatus.InvalidResource, authority.Decide(2, Request(resource: Log + 1)).Status);
            Assert.Equal(WoodstoveFeedStatus.InvalidResource, authority.Decide(3, Request(3)).Status);
            Assert.Equal(0, host.Calls); Assert.True(host.Available);
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Decide(2, Request(seq: 2)).Status);
            Assert.Equal(1, host.Calls);
        }

        [Fact]
        public void SpoofedAndOldEpochRequestsCannotAdvanceLegitimateActorsHighWater()
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            Assert.Equal(WoodstoveFeedStatus.InvalidActor, authority.Decide(3, Request(seq: uint.MaxValue)).Status);
            Assert.Equal(WoodstoveFeedStatus.StaleEpoch, authority.Decide(2,
                new WoodstoveFeedRequest(Source, Epoch - 1, 2, uint.MaxValue, Log)).Status);
            Assert.Equal(0u, authority.Seen(2));
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Decide(2, Request()).Status);
            Assert.Equal(1u, authority.Seen(2)); Assert.Equal(1, host.Calls);
        }

        [Fact]
        public void CountersDoNotWrapAndNewEpochUsesNativeInitializationNotOldFuel()
        {
            var host = new Host(); var authority = new WoodstoveFuelAuthority(Epoch, host);
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Decide(2, Request(seq: uint.MaxValue)).Status);
            host.Available = true; host.Resource = 102;
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, authority.Decide(2, Request(seq: 1, resource: 102)).Status);
            Assert.Equal(1, host.Calls);
            host.Fuel = 0; host.Heat = 0; host.Lit = false;
            var newWorld = new WoodstoveFuelAuthority(Epoch + 1, host);
            var fresh = newWorld.Capture();
            Assert.Equal(0, fresh.Values.Fuel); Assert.False(fresh.Values.Lit);
            Assert.Empty(fresh.ConsumedResources); Assert.Equal(0u, newWorld.Seen(2));
            Assert.Equal(WoodstoveFeedStatus.StaleEpoch, newWorld.Decide(2, Request()).Status);
        }

        [Fact]
        public void HostUsesSameSeamAndIndependentLogsRemainFinite()
        {
            var host = new Host { Holder = 0 }; var authority = new WoodstoveFuelAuthority(Epoch, host);
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Decide(0, Request(0)).Status);
            host.Resource = 102; host.Available = true; host.Holder = 2;
            Assert.Equal(WoodstoveFeedStatus.Accepted, authority.Decide(2, Request(resource: 102)).Status);
            Assert.Equal(3, host.Fuel); Assert.Equal(2, host.Calls);
            Assert.Equal(new uint[] { 101, 102 }, authority.Capture().ConsumedResources);
        }
    }
}
