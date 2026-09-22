using System;
using System.Linq;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace PaneScrapeBridge.Tests
{
    public sealed partial class BridgeTests
    {
        [Theory]
        [InlineData("vehicle")][InlineData("pane")][InlineData("tool")]
        [InlineData("occluded")][InlineData("miss")][InlineData("range")][InlineData("behind-ray")]
        [InlineData("contact-nan")][InlineData("contact-infinity")]
        [InlineData("pose-stale")][InlineData("pose-future")][InlineData("pose-nan")][InlineData("feet-nan")]
        [InlineData("dead")][InlineData("inside")][InlineData("passenger")]
        [InlineData("lease-stale")][InlineData("off")]
        [InlineData("moving")][InlineData("rotating")][InlineData("velocity-nan")]
        [InlineData("pane-disabled")][InlineData("pane-inactive")]
        [InlineData("eye-nan")][InlineData("eye-infinity")][InlineData("eye-height")][InlineData("eye-offset")]
        [InlineData("direction-nan")][InlineData("direction-infinity")][InlineData("direction-zero")][InlineData("wrong-yaw")]
        public void AuthenticatedDenialCannotBecomeValidOnReplayOrAdvanceThePane(string invalid)
        {
            using var r = new Rig(); r.Equip();
            var actor = r.Session.Players[0];
            var a = r.Request(4, ScraperOperation.Stroke);
            switch (invalid)
            {
                case "vehicle": a.VehicleId++; break;
                case "pane": a.Pane++; break;
                case "tool": a.ToolId++; break;
                case "occluded": Physics.Contact = r.ToolContact; break;
                case "miss": Physics.Contact = null; break;
                case "range": Physics.Distance = .801f; break;
                case "behind-ray": Physics.Distance = -.01f; break;
                case "contact-nan": Physics.Distance = float.NaN; break;
                case "contact-infinity": Physics.Distance = float.PositiveInfinity; break;
                case "pose-stale": actor.LastTransformTime = 9; break;
                case "pose-future": actor.LastTransformTime = 11; break;
                case "pose-nan": actor.LastTransformTime = float.NaN; break;
                case "feet-nan": actor.Position = new Vector3(float.NaN, 0, 0); break;
                case "dead": actor.IsDead = true; break;
                case "inside": actor.MoveState = WinterMP.Core.Sync.PlayerMoveState.Driving; break;
                case "passenger": actor.MoveState = WinterMP.Core.Sync.PlayerMoveState.Passenger; break;
                case "lease-stale": Time.unscaledTime = actor.LastTransformTime = 11; break;
                case "off": r.Act(r.Request(3, ScraperOperation.Off)); break;
                case "moving": r.Car.Body!.velocity = new Vector3(1, 0, 0); break;
                case "rotating": r.Car.Body!.angularVelocity = new Vector3(0, 1, 0); break;
                case "velocity-nan": r.Car.Body!.velocity = new Vector3(float.NaN, 0, 0); break;
                case "pane-disabled": r.Pane.enabled = false; break;
                case "pane-inactive": r.Pane.gameObject.activeInHierarchy = false; break;
                case "eye-nan": a.Eye = new NetVector3(float.NaN, 1, 0); break;
                case "eye-infinity": a.Eye = new NetVector3(0, float.PositiveInfinity, 0); break;
                case "eye-height": a.Eye = new NetVector3(0, 2.2f, 0); break;
                case "eye-offset": a.Eye = new NetVector3(.61f, 1, 0); break;
                case "direction-nan": a.Direction = new NetVector3(0, float.NaN, 1); break;
                case "direction-infinity": a.Direction = new NetVector3(0, 0, float.PositiveInfinity); break;
                case "direction-zero": a.Direction = new NetVector3(); break;
                case "wrong-yaw": a.Direction = new NetVector3(1, 0, 0); break;
                default: throw new ArgumentException(invalid);
            }
            int materialWrites = r.Material.Writes;
            var denied = r.Act(a);
            Assert.NotEqual(PaneScrapeStatus.Accepted, denied.Status);
            Assert.Equal(4u, denied.HighWater); Assert.Equal(1u, denied.Revision);
            var replica = new PaneScrapeReplica(9, r.Epoch, 2);
            Assert.True(replica.ReceiveDecision(true, denied.Decision(), out bool effect)); Assert.False(effect);
            Assert.Equal(.25f, replica.Current!.Cutoff); Assert.Equal(.25f, r.Cutoff.Value);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
            Assert.Equal(materialWrites, r.Material.Writes);

            actor.LastTransformTime = Time.unscaledTime; actor.Position = Vector3.zero;
            actor.IsDead = false; actor.MoveState = 0;
            r.Car.Body!.velocity = r.Car.Body.angularVelocity = Vector3.zero;
            r.Pane.enabled = r.Pane.gameObject.activeInHierarchy = true;
            Physics.Contact = r.Pane; Physics.Distance = .6f;
            var replay = r.Act(r.Request(4, ScraperOperation.Stroke));
            Assert.NotEqual(PaneScrapeStatus.Accepted, replay.Status);
            Assert.Equal(denied.Revision, replay.Revision); Assert.Equal(4u, replay.HighWater);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(materialWrites, r.Material.Writes);

            // Repair through fresh real bridge operations, never by assigning a lease or result.
            r.Act(r.Request(5, ScraperOperation.Drop)); Physics.Contact = r.ToolContact;
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(6, ScraperOperation.Pickup)).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(7, ScraperOperation.Equip)).Status);
            Physics.Contact = r.Pane;
            var accepted = r.Act(r.Request(8, ScraperOperation.Stroke));
            Assert.Equal(PaneScrapeStatus.Accepted, accepted.Status); Assert.Equal(2u, accepted.Revision);
            Assert.Equal(1, r.Glass.Calls); Assert.Equal(.255f, accepted.Cutoff, 6);
            Assert.True(replica.ReceiveDecision(true, accepted.Decision(), out effect)); Assert.True(effect);
            Assert.Equal(r.Cutoff.Value, replica.Current.Cutoff); Assert.Equal(r.Cutoff.Value, r.Material.Cutoff);
        }

        [Theory]
        [InlineData("actor")][InlineData("epoch")]
        public void UnauthenticatedOrStaleAttemptCannotPoisonValidGuestSequence(string invalid)
        {
            using var r = new Rig(); r.Equip();
            var a = r.Request(uint.MaxValue, ScraperOperation.Stroke);
            if (invalid == "actor") a.Actor = 3; else a.Epoch++;
            var denied = r.Act(a);
            Assert.Equal(invalid == "actor" ? PaneScrapeStatus.InvalidActor : PaneScrapeStatus.StaleEpoch, denied.Status);
            Assert.Equal(2u, denied.HighWater); Assert.Equal(1u, denied.Revision);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Material.Writes);
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(3, ScraperOperation.Stroke)).Status);
            Assert.Equal(1, r.Glass.Calls);
        }

        [Theory]
        [InlineData((byte)0)][InlineData((byte)2)]
        public void BothActorsMustPickupBeforeEquipAndEquipBeforeStroke(byte actor)
        {
            using var r = new Rig(); Physics.Contact = r.ToolContact;
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(r.Request(1, ScraperOperation.Equip, actor), actor).Status);
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(r.Request(1, ScraperOperation.Pickup, actor), actor).Status);
            var pickup = r.Act(r.Request(2, ScraperOperation.Pickup, actor), actor);
            Assert.Equal(PaneScrapeStatus.Accepted, pickup.Status); Assert.False(pickup.Equipped);
            Physics.Contact = r.Pane;
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(r.Request(3, ScraperOperation.Stroke, actor), actor).Status);
            var heartbeat = r.Act(r.Request(4, ScraperOperation.KeepAlive, actor), actor);
            Assert.Equal(PaneScrapeStatus.Accepted, heartbeat.Status); Assert.False(heartbeat.Equipped);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(5, ScraperOperation.Equip, actor), actor).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(6, ScraperOperation.Stroke, actor), actor).Status);
            Assert.Equal(1, r.Glass.Calls); Assert.Equal(actor == 0 ? 1 : 0, r.Effect.Calls);
            Assert.False(r.Car.LocallyOwned); Assert.Equal((byte)255, r.Car.RemoteOwner);
        }

        [Fact]
        public void AnotherAuthenticatedGuestCannotStealTheLeaseOrStrokeItsPane()
        {
            using var r = new Rig(); r.Equip();
            r.Session.Players.Add(new RemotePlayer { PlayerId = 3 });
            var denied = r.Act(r.Request(1, ScraperOperation.Stroke, 3), 3);
            Assert.NotEqual(PaneScrapeStatus.Accepted, denied.Status);
            Physics.Contact = r.ToolContact;
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(r.Request(2, ScraperOperation.Pickup, 3), 3).Status);
            Assert.True(r.Bridge.AllowsToolMotion(77, 2)); Assert.False(r.Bridge.AllowsToolMotion(77, 3));
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
            Physics.Contact = r.Pane;
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(3, ScraperOperation.Stroke)).Status);
            Assert.Equal(1, r.Glass.Calls);
        }

        [Theory]
        [InlineData(ScraperOperation.Pickup)][InlineData(ScraperOperation.Equip)][InlineData(ScraperOperation.Off)]
        [InlineData(ScraperOperation.Drop)][InlineData(ScraperOperation.KeepAlive)][InlineData(ScraperOperation.Stroke)]
        public void BusyAttemptPreservesOuterRequestLeaseAndResultAndConsumesSequence(ScraperOperation operation)
        {
            using var r = new Rig(); r.Equip();
            int sent = r.Session.Sent.Count;
            r.Glass.Callback = () => {
                var outer = Get<ScraperAction>(r.Bridge, "_request");
                var lease = Get<ScraperLease>(r.Bridge, "_lease");
                float age = lease.Age(Time.unscaledTime);
                r.Bridge.OnAction(r.Request(4, operation), 2);
                Assert.Same(outer, Get<ScraperAction>(r.Bridge, "_request"));
                Assert.Equal((byte)2, lease.Holder); Assert.True(lease.Equipped);
                Assert.Equal(age, lease.Age(Time.unscaledTime)); Assert.Equal(4u, lease.Seen(2));
                Assert.Equal(sent, r.Session.Sent.Count); // no half-applied snapshot or decision
            };
            var result = r.Act(r.Request(3, ScraperOperation.Stroke));
            r.Glass.Callback = null;
            Assert.Equal(PaneScrapeStatus.Accepted, result.Status); Assert.Equal(2u, result.Revision);
            Assert.Equal(3u, result.Sequence); Assert.Equal(4u, result.HighWater);
            Assert.Equal(sent + 1, r.Session.Sent.Count); Assert.Null(Get<ScraperAction?>(r.Bridge, "_request"));
            Assert.Equal(1, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls); Assert.Equal(1, r.Material.Writes);
            var replay = r.Act(r.Request(4, ScraperOperation.Stroke));
            Assert.NotEqual(PaneScrapeStatus.Accepted, replay.Status); Assert.Equal(result.Revision, replay.Revision);
            Assert.Equal(result.Cutoff, replay.Cutoff); Assert.Equal(1, r.Glass.Calls);
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(5, ScraperOperation.Stroke)).Status);
            Assert.Equal(2, r.Glass.Calls);
        }
    }
}
