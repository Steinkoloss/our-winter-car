using System;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    // Portable host adapter double, never native/transport evidence. In production
    // these facts MUST come from authenticated host equipment and collision state.
    public sealed class PaneScrapeAuthorityTests
    {
        private const uint Vehicle = 2784793521, Epoch = 23, Tool = 77;
        private sealed class Host : IPaneScrapeHost
        {
            public float Cutoff = .25f, OtherPane = .6f;
            public int Strokes, ContextReads;
            public bool ThrowAfterMutation, NonfiniteAfterMutation, Reenter;
            public PaneScrapeAuthority? Authority;
            public PaneScrapeDecision? Nested;
            public PaneScrapeHostContext Context = new PaneScrapeHostContext
            {
                ActorPresent = true, ActorAlive = true, ActorOutside = true,
                PoseAgeSeconds = .05f, PaneAvailable = true, VehicleParked = true,
                EquipmentActor = 2, EquipmentEpoch = Epoch, EquippedToolId = Tool,
                IsIceScraper = true, EquipmentAgeSeconds = .05f,
                ContactVehicleId = Vehicle, ContactPane = PaneScrapeIntent.Windshield,
                ContactDistance = .6f, ContactAgeSeconds = .05f, Unobstructed = true
            };
            public float ReadWindshieldCutoff() => Cutoff;
            public PaneScrapeHostContext ReadContext(byte actor)
            { ContextReads++; return Context; }
            public void ApplyWindshieldDelta()
            {
                Strokes++;
                if (Reenter) Nested = Authority!.Decide(2, Intent(2));
                Cutoff += .005f;
                if (NonfiniteAfterMutation) Cutoff = float.NaN;
                if (ThrowAfterMutation) throw new InvalidOperationException("Native callback failed after mutation");
            }
        }
        private static PaneScrapeIntent Intent(uint sequence = 1, byte actor = 2, uint epoch = Epoch,
            uint vehicle = Vehicle, byte pane = PaneScrapeIntent.Windshield, uint tool = Tool)
            => new PaneScrapeIntent(epoch, actor, sequence, vehicle, pane, tool);
        private static PaneScrapeAuthority Start(Host host) => new PaneScrapeAuthority(Vehicle, Epoch, host);

        [Fact]
        public void LegitimateOutsideGuestExecutesOnceAndReplicatesWithoutVehicleOwnership()
        {
            var host = new Host(); var authority = Start(host);
            var replica = new PaneScrapeReplica(Vehicle, Epoch, 2);
            Assert.True(replica.ReceiveSnapshot(true, authority.Capture()));
            var result = authority.Decide(2, Intent());
            Assert.Equal(PaneScrapeStatus.Accepted, result.Status);
            Assert.Equal(1, host.Strokes);
            Assert.Equal(.255f, host.Cutoff, 6);
            Assert.Equal(.6f, host.OtherPane);
            Assert.Equal(.25f, replica.Current!.Cutoff); // No guest prediction while waiting for the result.
            Assert.True(replica.ReceiveDecision(true, result, out bool personalEffect));
            Assert.True(personalEffect);
            Assert.Equal(host.Cutoff, replica.Current!.Cutoff);
            var replay = authority.Decide(2, Intent());
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, replay.Status);
            Assert.Equal(1, host.Strokes);
            Assert.Equal(result.Snapshot!.Revision, replay.Snapshot!.Revision);
            Assert.True(replica.ReceiveDecision(true, result, out personalEffect));
            Assert.False(personalEffect);
        }

        [Theory]
        [InlineData("absent", PaneScrapeStatus.ActorUnavailable)]
        [InlineData("dead", PaneScrapeStatus.ActorUnavailable)]
        [InlineData("inside", PaneScrapeStatus.InvalidContact)]
        [InlineData("pose-stale", PaneScrapeStatus.InvalidContact)]
        [InlineData("pose-future", PaneScrapeStatus.InvalidContact)]
        [InlineData("pose-nan", PaneScrapeStatus.InvalidContact)]
        [InlineData("no-tool", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("different-tool", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("not-scraper", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("other-holder", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("tool-epoch", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("tool-stale", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("tool-infinite", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("no-pane", PaneScrapeStatus.PaneUnavailable)]
        [InlineData("moving", PaneScrapeStatus.PaneUnavailable)]
        [InlineData("different-vehicle", PaneScrapeStatus.InvalidContact)]
        [InlineData("different-pane", PaneScrapeStatus.InvalidContact)]
        [InlineData("too-far", PaneScrapeStatus.InvalidContact)]
        [InlineData("behind-ray", PaneScrapeStatus.InvalidContact)]
        [InlineData("no-contact", PaneScrapeStatus.InvalidContact)]
        [InlineData("contact-stale", PaneScrapeStatus.InvalidContact)]
        [InlineData("contact-nan", PaneScrapeStatus.InvalidContact)]
        [InlineData("occluded", PaneScrapeStatus.InvalidContact)]
        public void HostFactsDenyInvalidActionWithoutMutatingEitherCanonicalPane(string invalid, PaneScrapeStatus expected)
        {
            var host = new Host(); var authority = Start(host);
            var good = host.Context;
            var bad = good;
            switch (invalid)
            {
                case "absent": bad.ActorPresent = false; break;
                case "dead": bad.ActorAlive = false; break;
                case "inside": bad.ActorOutside = false; break;
                case "pose-stale": bad.PoseAgeSeconds = .601f; break;
                case "pose-future": bad.PoseAgeSeconds = -.01f; break;
                case "pose-nan": bad.PoseAgeSeconds = float.NaN; break;
                case "no-tool": bad.EquippedToolId = 0; break;
                case "different-tool": bad.EquippedToolId++; break;
                case "not-scraper": bad.IsIceScraper = false; break;
                case "other-holder": bad.EquipmentActor = 3; break;
                case "tool-epoch": bad.EquipmentEpoch++; break;
                case "tool-stale": bad.EquipmentAgeSeconds = .601f; break;
                case "tool-infinite": bad.EquipmentAgeSeconds = float.PositiveInfinity; break;
                case "no-pane": bad.PaneAvailable = false; break;
                case "moving": bad.VehicleParked = false; break;
                case "different-vehicle": bad.ContactVehicleId++; break;
                case "different-pane": bad.ContactPane++; break;
                case "too-far": bad.ContactDistance = .8001f; break;
                case "behind-ray": bad.ContactDistance = -.001f; break;
                case "no-contact": bad.ContactDistance = float.PositiveInfinity; break;
                case "contact-stale": bad.ContactAgeSeconds = .601f; break;
                case "contact-nan": bad.ContactAgeSeconds = float.NaN; break;
                case "occluded": bad.Unobstructed = false; break;
                default: throw new ArgumentException(invalid);
            }
            host.Context = bad;
            var guest = new PaneScrapeReplica(Vehicle, Epoch, 2);
            Assert.True(guest.ReceiveSnapshot(true, authority.Capture()));
            var result = authority.Decide(2, Intent());
            Assert.Equal(expected, result.Status);
            Assert.True(guest.ReceiveDecision(true, result, out bool effect));
            Assert.False(effect);
            Assert.Equal(.25f, host.Cutoff); Assert.Equal(.25f, guest.Current!.Cutoff);
            Assert.Equal(.6f, host.OtherPane); Assert.Equal(0, host.Strokes);
            host.Context = good;
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, authority.Decide(2, Intent()).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, authority.Decide(2, Intent(2)).Status);
        }

        [Fact]
        public void AuthenticationAndEpochFailuresCannotPoisonAnActorsSequence()
        {
            var h = new Host(); var a = Start(h);
            Assert.Equal(PaneScrapeStatus.InvalidActor, a.Decide(3, Intent(999)).Status);
            Assert.Equal(PaneScrapeStatus.InvalidActor, a.Decide(255, Intent(999, 255)).Status);
            Assert.Equal(PaneScrapeStatus.StaleEpoch, a.Decide(2, Intent(999, epoch: Epoch - 1)).Status);
            Assert.Equal(PaneScrapeStatus.StaleEpoch, a.Decide(2, Intent(999, epoch: 0)).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, a.Decide(2, Intent()).Status);
            Assert.Equal(1, h.Strokes); Assert.Equal(1, h.ContextReads);
        }

        [Fact]
        public void WrongTargetAndToolCannotBeCorrectedByReplayingSameStroke()
        {
            var h = new Host(); var a = Start(h);
            Assert.Equal(PaneScrapeStatus.WrongPane, a.Decide(2, Intent(vehicle: Vehicle + 1)).Status);
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, a.Decide(2, Intent()).Status);
            Assert.Equal(PaneScrapeStatus.WrongPane, a.Decide(2, Intent(2, pane: 2)).Status);
            Assert.Equal(PaneScrapeStatus.InvalidEquipment, a.Decide(2, Intent(3, tool: 0)).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, a.Decide(2, Intent(4)).Status);
            Assert.Equal(1, h.Strokes);
        }

        [Fact]
        public void SequenceIsMonotonicDoesNotWrapAndBelongsToAuthenticatedActor()
        {
            var h = new Host(); var a = Start(h);
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, a.Decide(2, Intent(0)).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, a.Decide(2, Intent(5)).Status);
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, a.Decide(2, Intent(4)).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, a.Decide(2, Intent(uint.MaxValue)).Status);
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, a.Decide(2, Intent(1)).Status);
            h.Context.EquipmentActor = 0;
            Assert.Equal(PaneScrapeStatus.Accepted, a.Decide(0, Intent(1, 0)).Status);
            Assert.Equal(3, h.Strokes);
        }

        [Fact]
        public void ExactNativeDistanceAndFreshnessBoundaryAreAccepted()
        {
            var h = new Host(); h.Context.ContactDistance = .8f;
            h.Context.ContactAgeSeconds = h.Context.EquipmentAgeSeconds = h.Context.PoseAgeSeconds = .6f;
            Assert.Equal(PaneScrapeStatus.Accepted, Start(h).Decide(2, Intent()).Status);
        }

        [Fact]
        public void NativeFailureAfterMutationIsFailStopAndNeverRetried()
        {
            var h = new Host { ThrowAfterMutation = true }; var a = Start(h);
            Assert.Equal(PaneScrapeStatus.NativeFailure, a.Decide(2, Intent()).Status);
            Assert.True(a.Faulted);
            Assert.Equal(.255f, a.Capture().Cutoff, 6);
            Assert.Equal(PaneScrapeStatus.NativeFailure, a.Decide(2, Intent(2)).Status);
            Assert.Equal(1, h.Strokes);
        }

        [Fact]
        public void NonfiniteNativeReadbackCannotBecomeAnAcceptedResult()
        {
            var h = new Host { NonfiniteAfterMutation = true }; var a = Start(h);
            var result = a.Decide(2, Intent());
            Assert.Equal(PaneScrapeStatus.NativeFailure, result.Status);
            Assert.Null(result.Snapshot); Assert.True(a.Faulted);
            Assert.Equal(1, h.Strokes);
        }

        [Fact]
        public void ReentrantNativeCallbackCannotApplyAnotherStroke()
        {
            var h = new Host { Reenter = true }; var a = Start(h); h.Authority = a;
            Assert.Equal(PaneScrapeStatus.Accepted, a.Decide(2, Intent()).Status);
            Assert.Equal(PaneScrapeStatus.Busy, h.Nested!.Status); Assert.Equal(1, h.Strokes);
            h.Reenter = false;
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, a.Decide(2, Intent(2)).Status);
            Assert.Equal(1, h.Strokes);
        }

        [Fact]
        public void SamePeerRejoinAndFreshJoinCaptureNativeLifecycleRatherThanPersistingScrapes()
        {
            var h = new Host(); var a = Start(h);
            var old = a.Decide(2, Intent());
            h.Cutoff = 0; // Native FREEZE, not an additive action or custom persistence.
            var frozen = a.Capture(); Assert.True(frozen.Revision > old.Snapshot!.Revision);
            var returning = new PaneScrapeReplica(Vehicle, Epoch, 2);
            Assert.True(returning.ReceiveSnapshot(true, frozen));
            Assert.False(returning.ReceiveSnapshot(true, old.Snapshot));
            Assert.Equal(0, returning.Current!.Cutoff);
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, a.Decide(2, Intent()).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, a.Decide(2, Intent(2)).Status);
            var newcomer = new PaneScrapeReplica(Vehicle, Epoch, 3);
            Assert.True(newcomer.ReceiveSnapshot(true, a.Capture()));
            Assert.Equal(h.Cutoff, newcomer.Current!.Cutoff);
            h.Cutoff = 1; // Native roof initialization on a NEW cold-loaded authority.
            var cold = new PaneScrapeAuthority(Vehicle, Epoch + 1, h);
            Assert.Equal(1, cold.Capture().Cutoff);
            Assert.Equal(PaneScrapeStatus.StaleEpoch, cold.Decide(2, Intent(3)).Status);
        }

        [Fact]
        public void NewerSnapshotDoesNotSwallowAnAcceptedActorsPersonalEffect()
        {
            var h = new Host(); var a = Start(h);
            var stroke = a.Decide(2, Intent());
            h.Cutoff = 0; // Bulk resync / native FREEZE overtakes the ordered action result.
            var frozen = a.Capture();
            var replica = new PaneScrapeReplica(Vehicle, Epoch, 2);
            Assert.True(replica.ReceiveSnapshot(true, frozen));
            Assert.True(replica.ReceiveDecision(true, stroke, out bool effect));
            Assert.True(effect);
            Assert.Equal(0, replica.Current!.Cutoff);
            Assert.True(replica.ReceiveDecision(true, stroke, out effect));
            Assert.False(effect);
            Assert.Equal(frozen.Revision, replica.Current.Revision);
        }

        [Fact]
        public void ReplicaAuthenticatesHostEpochPaneRevisionAndEffectRecipient()
        {
            var h = new Host(); var a = Start(h); var result = a.Decide(2, Intent());
            var guest = new PaneScrapeReplica(Vehicle, Epoch, 2);
            Assert.False(guest.ReceiveDecision(false, result, out _)); Assert.Null(guest.Current);
            Assert.False(guest.ReceiveSnapshot(true, new PaneScrapeSnapshot(Vehicle, Epoch - 1, 1, .9f)));
            Assert.False(guest.ReceiveSnapshot(true, new PaneScrapeSnapshot(Vehicle + 1, Epoch, 1, .9f)));
            Assert.False(guest.ReceiveSnapshot(true, new PaneScrapeSnapshot(Vehicle, Epoch, 0, .9f)));
            Assert.False(guest.ReceiveSnapshot(true, new PaneScrapeSnapshot(Vehicle, Epoch, 1, float.NaN)));
            Assert.True(guest.ReceiveDecision(true, result, out bool guestHeat)); Assert.True(guestHeat);
            Assert.False(guest.ReceiveSnapshot(true, new PaneScrapeSnapshot(Vehicle, Epoch, result.Snapshot!.Revision, .99f)));
            var hostPlayer = new PaneScrapeReplica(Vehicle, Epoch, 0);
            Assert.True(hostPlayer.ReceiveDecision(true, result, out bool hostHeat)); Assert.False(hostHeat);
        }
    }
}
