using System;
using System.Collections;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace PaneScrapeBridge.Tests
{
    public sealed partial class BridgeTests
    {
        [Theory]
        [InlineData(true, false)][InlineData(false, false)][InlineData(false, true)]
        public void ResetCancelsOnlyUncommittedPickupAndClearsSessionState(bool host, bool approved)
        {
            using var r = new Rig(host);
            if (!host) r.Bridge.OnUpdate(new PaneScrapeUpdate { Epoch = 42, VehicleId = 9, ToolId = 77,
                Revision = 1, Cutoff = .25f, Actor = 2 });
            r.Picked.Value = r.Tool.Body!.gameObject;
            Physics.Contact = r.ToolContact;
            r.Enter("Set pivot 2");
            if (approved)
            {
                var request = r.Session.Sent.OfType<ScraperAction>().Last();
                r.Bridge.OnUpdate(new PaneScrapeUpdate { Epoch = 42, VehicleId = 9, ToolId = 77,
                    Revision = 1, Cutoff = .25f, Actor = 2, Holder = 2, Sequence = request.Sequence,
                    HighWater = request.Sequence, Status = PaneScrapeStatus.Accepted });
            }
            int sent = r.Session.Sent.Count;
            r.Bridge.ResetSession();
            Assert.Null(r.Picked.Value);
            Assert.Equal("Look for object", r.Hand.ActiveStateName);
            Assert.Equal(sent, r.Session.Sent.Count);
            Assert.Equal(0, ((Counter)r.Originals.Single(x => x.Key.Name == "Set pivot 2").Value[0]).Calls);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
            foreach (string field in new[] { "_lease", "_authority", "_replica", "_received", "_request",
                "_pickupGate", "_pane", "_tool", "_vehicle", "_hand", "_glassActions", "_soundActions" })
                Assert.Null(Get<object?>(r.Bridge, field));
            foreach (string field in new[] { "_epoch", "_sequence", "_pickupSequence" })
                Assert.Equal(0u, Get<uint>(r.Bridge, field));
            foreach (string field in new[] { "_resumePickup", "_admitted", "_failed" })
                Assert.False(Get<bool>(r.Bridge, field));
            foreach (string field in new[] { "_originals", "_installed", "_replaced", "_restoreEntries" })
                Assert.Empty((IEnumerable)Get<object>(r.Bridge, field));
            foreach (var pair in r.Originals) Assert.Same(pair.Value, pair.Key.Actions);
            Assert.True(r.Originals.Single(x => x.Key.Name == "Set pivot 2").Value[0].Enabled);
            Assert.False(r.Originals.Single(x => x.Key.Name == "Set pivot 2").Value[1].Enabled);
            Assert.Same(r.Globals, r.Hand.Fsm.GlobalTransitions); Assert.Same(r.Events, r.Hand.Fsm.Events);
            r.Bridge.ResetSession(); // idempotent; never resumes native pickup on teardown
            Assert.Equal(sent, r.Session.Sent.Count);
        }

        [Theory]
        [InlineData(false)][InlineData(true)]
        public void ResetDoesNotDropAResumedToolOrReplaceAnotherNativeSelection(bool otherSelection)
        {
            using var r = new Rig();
            r.Picked.Value = r.Tool.Body!.gameObject; Physics.Contact = r.ToolContact;
            r.Enter("Set pivot 2");
            if (otherSelection) { r.Picked.Value = new GameObject("other"); r.Enter("Look for object"); }
            else { Call(r.Bridge, "Update"); r.Enter("Ice Scraper"); }
            var selected = r.Picked.Value; string state = r.Hand.ActiveStateName;
            r.Bridge.ResetSession();
            Assert.Same(selected, r.Picked.Value); Assert.Equal(state, r.Hand.ActiveStateName);
        }

        [Fact]
        public void NewSessionDoesNotReuseLeaseSequenceOrReplicaAndDoesNotUndoAcceptedGlass()
        {
            using var r = new Rig(); r.Equip();
            var oldStroke = r.Request(3, ScraperOperation.Stroke);
            var oldResult = r.Act(oldStroke);
            uint oldEpoch = r.Epoch;
            r.Bridge.ResetSession();
            Assert.False(r.Bridge.AllowsToolMotion(77, 2)); Assert.False(r.Bridge.OwnsPane(9));
            Assert.Equal(oldResult.Cutoff, r.Cutoff.Value); Assert.Equal(oldResult.Cutoff, r.Material.Cutoff);
            Call(r.Bridge, "Update");
            Assert.NotEqual(oldEpoch, r.Epoch);
            var fresh = r.Session.Sent.OfType<PaneScrapeUpdate>().Last();
            Assert.Equal((byte)255, fresh.Holder); Assert.False(fresh.Equipped);
            Assert.Equal(0u, fresh.HighWater); Assert.Equal(1u, fresh.Revision);
            Assert.Equal(oldResult.Cutoff, fresh.Cutoff);
            Assert.Equal(PaneScrapeStatus.StaleEpoch, r.Act(oldStroke).Status);
            Assert.Equal(1, r.Glass.Calls);
            r.Equip();
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(3, ScraperOperation.Stroke)).Status);
            Assert.Equal(2, r.Glass.Calls); Assert.Equal(.26f, r.Cutoff.Value, 6);
        }

        private sealed class FailingCancelAction : FsmStateAction
        {
            public override void OnEnter() { throw new InvalidOperationException("portable cancellation callback failure"); }
        }

        [Fact]
        public void NativeCancellationFailureCannotRetainSessionAuthorityOrInstalledHooks()
        {
            using var r = new Rig();
            r.Picked.Value = r.Tool.Body!.gameObject; Physics.Contact = r.ToolContact;
            r.Enter("Set pivot 2");
            var look = r.Hand.Fsm.States.Single(s => s.Name == "Look for object");
            var failure = new FailingCancelAction(); failure.Init(look);
            look.Actions = new FsmStateAction[] { failure };
            r.Bridge.ResetSession();
            Assert.Null(r.Picked.Value);
            Assert.Null(Get<object?>(r.Bridge, "_authority")); Assert.Null(Get<object?>(r.Bridge, "_lease"));
            Assert.Null(Get<object?>(r.Bridge, "_replica")); Assert.Null(Get<object?>(r.Bridge, "_request"));
            Assert.False(Get<bool>(r.Bridge, "_admitted")); Assert.Equal(0u, r.Epoch);
            foreach (var pair in r.Originals.Where(p => p.Key != look)) Assert.Same(pair.Value, pair.Key.Actions);
            Assert.Same(failure, look.Actions.Single()); // foreign action is not removed by our cleanup
            Assert.Same(r.Globals, r.Hand.Fsm.GlobalTransitions); Assert.Same(r.Events, r.Hand.Fsm.Events);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
        }
    }
}