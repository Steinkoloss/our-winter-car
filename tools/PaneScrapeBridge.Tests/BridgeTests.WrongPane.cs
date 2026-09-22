using System;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;
using Xunit.Abstractions;

namespace PaneScrapeBridge.Tests
{
    public sealed partial class BridgeTests
    {
        private readonly ITestOutputHelper _output;
        public BridgeTests(ITestOutputHelper output) { _output = output; }

        // Unrelated native-variable boundary sentinels, not a cabin/frost simulator.
        private sealed class FrostBoundary
        {
            public readonly FsmFloat Frost = new FsmFloat { Name = "Frost", Value = .73f };
            public readonly FsmFloat Side = new FsmFloat { Name = "CutoffSideLeft", Value = .61f };
            public readonly Material Material = new Material { Cutoff = .73f };
            public FrostBoundary(Rig rig)
            {
                var freezing = Get<PlayMakerFSM>(rig.Bridge, "_freezing");
                freezing.FsmVariables.Vars[Side.Name] = Side;
                var frosting = freezing.gameObject.Add(new PlayMakerFSM { FsmName = "GlassFrosting" });
                frosting.FsmVariables.Vars[Frost.Name] = Frost;
                frosting.FsmVariables.Vars["FrostGlass"] = new FsmMaterial { Value = Material };
            }
            public void AssertUntouched()
            {
                Assert.Equal(.73f, Frost.Value); Assert.Equal(.61f, Side.Value);
                Assert.Equal(.73f, Material.Cutoff); Assert.Equal(0, Material.Writes);
            }
        }

        private static void Select(Rig rig, PlayerSyncManager pose)
        {
            SessionManager.Instance = rig.Session;
            PlayerSyncManager.Instance = pose;
        }

        private static void AssertParkedNonOwner(Rig rig)
        {
            Assert.Equal("CORRIS", rig.Car.Path); Assert.True(rig.Car.IsVehicle);
            Assert.Equal(0f, rig.Car.Body!.velocity.sqrMagnitude);
            Assert.Equal(0f, rig.Car.Body.angularVelocity.sqrMagnitude);
            Assert.False(rig.Car.LocallyOwned); Assert.Equal((byte)255, rig.Car.RemoteOwner);
            Assert.Equal("ice scraper(itemx)", rig.Tool.Body!.name);
        }

        private static void AssertSameAbsoluteResult(PaneScrapeUpdate previous, PaneScrapeUpdate current)
        {
            Assert.Equal(previous.Epoch, current.Epoch); Assert.Equal(previous.VehicleId, current.VehicleId);
            Assert.Equal(previous.ToolId, current.ToolId); Assert.Equal(previous.Revision, current.Revision);
            Assert.Equal(previous.Cutoff, current.Cutoff); Assert.Equal(previous.Holder, current.Holder);
            Assert.Equal(previous.Equipped, current.Equipped);
        }

        private static void AssertGlassAndEffects(Rig host, Rig guest, float cutoff, int strokes)
        {
            Assert.Equal((uint)(strokes + 1), Get<PaneScrapeReplica>(host.Bridge, "_replica").Current!.Revision);
            Assert.Equal((uint)(strokes + 1), Get<PaneScrapeReplica>(guest.Bridge, "_replica").Current!.Revision);
            Assert.Equal(cutoff, host.Cutoff.Value, 6); Assert.Equal(host.Cutoff.Value, guest.Cutoff.Value);
            Assert.Equal(host.Cutoff.Value, host.Material.Cutoff); Assert.Equal(host.Cutoff.Value, guest.Material.Cutoff);
            Assert.Equal(strokes, host.Glass.Calls); Assert.Equal(strokes, host.Material.Writes);
            Assert.Equal(0, guest.Glass.Calls); Assert.Equal(0, host.Effect.Calls);
            Assert.Equal(strokes, guest.Effect.Calls); Assert.Equal(0f, host.Heat.Value);
            Assert.Equal(strokes * .47f, guest.Heat.Value, 6);
            Assert.Equal(0, ((SendEventByName)guest.Originals[guest.Scrape.Fsm.States[0]][0]).Calls);
            Assert.False(Get<bool>(host.Bridge, "_failed")); Assert.False(Get<bool>(guest.Bridge, "_failed"));
        }

        private void TracePacket(string label, GuestStrokeWire.Packet packet, PaneScrapeUpdate result)
        {
            _output.WriteLine("{0}: original={1} transmitted={2} result={3}", label,
                Convert.ToBase64String(packet.Original), Convert.ToBase64String(packet.Transmitted),
                Convert.ToBase64String(PacketCodec.Encode(result)));
            _output.WriteLine("{0}: actor={1} sequence={2} highWater={3} status={4} revision={5} cutoff={6:R}",
                label, result.Actor, result.Sequence, result.HighWater, result.Status, result.Revision, result.Cutoff);
        }

        [Fact]
        public void FreshWrongPaneFromGuestHookConsumesSequenceWithoutMutationAndFreshStrokeRecovers()
        {
            var oldPose = PlayerSyncManager.Instance;
            using var host = new Rig();
            var hostFrost = new FrostBoundary(host);
            var hostPose = new PlayerSyncManager { Position = new Vector3(100, 0, 0) };
            Get<Transform>(host.Bridge, "_eye").position = hostPose.Position + Vector3.up;
            using var guest = new Rig(false);
            var guestFrost = new FrostBoundary(guest);
            var guestPose = new PlayerSyncManager();
            using var wire = new GuestStrokeWire(guest.Session);
            try
            {
                AssertParkedNonOwner(host); AssertParkedNonOwner(guest);
                Assert.True(host.Session.IsHost); Assert.False(guest.Session.IsHost);
                Assert.Equal((byte)0, host.Session.LocalPlayerId); Assert.Equal((byte)2, guest.Session.LocalPlayerId);
                Assert.True(hostPose.TryReadLocalPose(out var hostFeet, out _));
                Assert.True(guestPose.TryReadLocalPose(out var guestFeet, out _));
                Assert.True((hostFeet - guestFeet).sqrMagnitude > 100);
                Assert.Equal(guestFeet, host.Session.Players.Single().Position);
                Assert.False(host.Session.Players.Single().IsDead);
                Assert.Equal(0, host.Session.Players.Single().MoveState);
                Assert.Equal(Time.unscaledTime, host.Session.Players.Single().LastTransformTime);
                Assert.Null(host.Picked.Value);
                TracePreconditions("before-pickup", host, guest, hostPose, guestPose);
                _output.WriteLine("preconditions: portable doubles; parked CORRIS linear/angular=0; guest=2 non-owner; " +
                    "both LocallyOwned=false RemoteOwner=255; host=0 feet=(100,0,0), guest feet=(0,0,0); " +
                    "host away, no held tool; guest alive/outside/fresh; first-hit physics is a double, NOT native contact.");

                Select(guest, guestPose);
                guest.Bridge.OnUpdate((PaneScrapeUpdate)PacketCodec.Decode(PacketCodec.Encode(
                    host.Session.Sent.OfType<PaneScrapeUpdate>().Single(x => x.Actor == 2))));
                guest.Picked.Value = guest.Tool.Body!.gameObject;
                guest.Enter("Set pivot 2");
                Assert.Single(wire.Packets);
                Assert.Equal(ScraperOperation.Pickup, wire.Packets[0].Request.Operation);
                Physics.Contact = host.ToolContact;
                Select(host, hostPose);
                var pickup = host.Act(wire.Packets[0].Request);
                Assert.Equal(PaneScrapeStatus.Accepted, pickup.Status);
                TracePacket("pickup", wire.Packets[0], pickup);
                Select(guest, guestPose); guest.Bridge.OnUpdate(pickup);
                Call(guest.Bridge, "Update"); guest.Enter("Ice Scraper");
                foreach (var packet in wire.Packets.Skip(1).ToArray())
                {
                    Select(host, hostPose);
                    var approval = host.Act(packet.Request);
                    Assert.Equal(PaneScrapeStatus.Accepted, approval.Status);
                    TracePacket("admission-" + packet.Request.Operation, packet, approval);
                    Select(guest, guestPose); guest.Bridge.OnUpdate(approval);
                }
                var lease = Get<ScraperLease>(host.Bridge, "_lease");
                Assert.Equal((byte)2, lease.Holder); Assert.True(lease.Equipped);

                Physics.Contact = host.Pane;
                Assert.Equal(.6f, Physics.Distance);
                Assert.True(Physics.Distance <= PaneScrapePolicy.ContactMetres);
                Select(guest, guestPose); guest.Scrape.Fsm.States[0].Enter();
                var positivePacket = wire.Packets.Last();
                Assert.Equal(ScraperOperation.Stroke, positivePacket.Request.Operation);
                Assert.Equal(positivePacket.Original, positivePacket.Transmitted);
                Assert.Equal(0, guest.Glass.Calls); Assert.Equal(0, guest.Effect.Calls);
                Select(host, hostPose);
                var positive = host.Act(positivePacket.Request);
                Assert.Equal(PaneScrapeStatus.Accepted, positive.Status);
                Assert.Equal(positivePacket.Request.Sequence, positive.HighWater);
                Assert.Equal(2u, positive.Revision);
                Select(guest, guestPose); guest.Bridge.OnUpdate(positive); guest.Bridge.OnUpdate(positive);
                AssertGlassAndEffects(host, guest, .255f, 1);
                hostFrost.AssertUntouched(); guestFrost.AssertUntouched();
                TracePacket("positive", positivePacket, positive);
                TraceFixtureDecision("positive", host, guest, positive);

                wire.ArmWrongPane();
                Assert.Throws<InvalidOperationException>(() => wire.ArmWrongPane());
                // A genuine keepalive must not use up the armed stroke mutation.
                Time.unscaledTime += .2f;
                host.Session.Players.Single().LastTransformTime = Time.unscaledTime;
                Select(guest, guestPose); Call(guest.Bridge, "Update");
                var keepalive = wire.Packets.Last();
                Assert.Equal(ScraperOperation.KeepAlive, keepalive.Request.Operation);
                Assert.Equal(keepalive.Original, keepalive.Transmitted); Assert.True(wire.Armed);
                Select(host, hostPose); var kept = host.Act(keepalive.Request);
                Assert.Equal(PaneScrapeStatus.Accepted, kept.Status);
                TracePacket("armed-keepalive", keepalive, kept);
                Select(guest, guestPose); guest.Bridge.OnUpdate(kept);
                uint highWaterBefore = lease.Seen(2);
                float leaseAgeBefore = lease.Age(Time.unscaledTime);
                int hostMessagesBefore = host.Session.Sent.Count;

                TracePreconditions("before-wrong", host, guest, hostPose, guestPose);
                guest.Scrape.Fsm.States[0].Enter();
                var wrongPacket = wire.Packets.Last();
                var original = (ScraperAction)PacketCodec.Decode(wrongPacket.Original);
                string probePackets = "pane-mutation-packets|" + Convert.ToBase64String(wrongPacket.Original)
                    + "|" + Convert.ToBase64String(wrongPacket.Transmitted);
                Assert.Contains(probePackets, wire.ProbeSnapshot());
                _output.WriteLine("source-linked diagnostic " + probePackets);
                Assert.Equal(highWaterBefore + 1, original.Sequence);
                Assert.Equal(PaneScrapeIntent.Windshield, original.Pane);
                Assert.Same(host.Pane, Physics.Contact); Assert.Equal(.6f, Physics.Distance);
                AssertParkedNonOwner(host); AssertParkedNonOwner(guest);
                AssertGlassAndEffects(host, guest, .255f, 1); // no guest speculation
                Select(host, hostPose); var denied = host.Act(wrongPacket.Request);
                Assert.Equal(PaneScrapeStatus.WrongPane, denied.Status);
                Assert.False(wire.Armed); Assert.Equal(1, wire.Mutations);
                Assert.Equal((byte)2, wrongPacket.Request.Pane);
                var corrected = wrongPacket.Request; corrected.Pane = original.Pane;
                Assert.Equal(wrongPacket.Original, PacketCodec.Encode(corrected)); // all other bytes unchanged
                Assert.Equal(wrongPacket.Original.Length, wrongPacket.Transmitted.Length);
                Assert.Single(wrongPacket.Original.Zip(wrongPacket.Transmitted), p => p.First != p.Second);
                Assert.Equal(wrongPacket.Original, PacketCodec.Encode(guest.Session.Sent.OfType<ScraperAction>().Last()));
                Assert.Equal(hostMessagesBefore + 1, host.Session.Sent.Count); // denial, not an accepted result
                Assert.True(denied.IsDecision); Assert.Equal((byte)2, denied.Actor);
                Assert.Equal(original.Sequence, denied.Sequence); Assert.Equal(original.Sequence, denied.HighWater);
                Assert.Equal(original.Sequence, lease.Seen(2)); Assert.Equal(leaseAgeBefore, lease.Age(Time.unscaledTime));
                AssertSameAbsoluteResult(positive, denied);
                Select(guest, guestPose); guest.Bridge.OnUpdate(denied); guest.Bridge.OnUpdate(denied);
                Assert.Equal(original.Sequence, Get<uint>(guest.Bridge, "_sequence"));
                AssertGlassAndEffects(host, guest, .255f, 1);
                hostFrost.AssertUntouched(); guestFrost.AssertUntouched();
                TracePacket("wrong-pane", wrongPacket, denied);
                TraceFixtureDecision("wrong-pane", host, guest, denied);

                Select(host, hostPose); var replay = host.Act(corrected);
                Assert.Equal(PaneScrapeStatus.ReplayedSequence, replay.Status);
                Assert.Equal(denied.HighWater, replay.HighWater); Assert.Equal(denied.Sequence, replay.Sequence);
                AssertSameAbsoluteResult(denied, replay);
                Select(guest, guestPose); guest.Bridge.OnUpdate(replay); guest.Bridge.OnUpdate(positive);
                AssertGlassAndEffects(host, guest, .255f, 1);
                hostFrost.AssertUntouched(); guestFrost.AssertUntouched();
                TracePacket("corrected-replay", new GuestStrokeWire.Packet(wrongPacket.Original, PacketCodec.Encode(corrected)), replay);

                TraceFixtureDecision("corrected-replay", host, guest, replay);
                TracePreconditions("before-recovery", host, guest, hostPose, guestPose);
                guest.Scrape.Fsm.States[0].Enter();
                var recoveredPacket = wire.Packets.Last();
                Assert.Equal(original.Sequence + 1, recoveredPacket.Request.Sequence);
                Assert.Equal(PaneScrapeIntent.Windshield, recoveredPacket.Request.Pane);
                Assert.Equal(recoveredPacket.Original, recoveredPacket.Transmitted); Assert.Equal(1, wire.Mutations);
                Select(host, hostPose); var recovered = host.Act(recoveredPacket.Request);
                Assert.Equal(PaneScrapeStatus.Accepted, recovered.Status);
                Assert.Equal(positive.Revision + 1, recovered.Revision);
                Assert.Equal(recoveredPacket.Request.Sequence, recovered.HighWater);
                Select(guest, guestPose); guest.Bridge.OnUpdate(recovered); guest.Bridge.OnUpdate(recovered);
                AssertGlassAndEffects(host, guest, .26f, 2);
                hostFrost.AssertUntouched(); guestFrost.AssertUntouched();
                TracePacket("fresh-recovery", recoveredPacket, recovered);
                TraceFixtureDecision("fresh-recovery", host, guest, recovered);
                Select(host, hostPose); var duplicate = host.Act(recoveredPacket.Request);
                Assert.Equal(PaneScrapeStatus.ReplayedSequence, duplicate.Status);
                AssertSameAbsoluteResult(recovered, duplicate);
                TracePacket("recovery-duplicate", recoveredPacket, duplicate);
                Select(guest, guestPose); guest.Bridge.OnUpdate(duplicate);
                AssertGlassAndEffects(host, guest, .26f, 2);
                AssertParkedNonOwner(host); AssertParkedNonOwner(guest);
                TraceFixtureDecision("recovery-duplicate", host, guest, duplicate);
                Assert.Equal(new[] { PaneScrapeStatus.Accepted, PaneScrapeStatus.WrongPane,
                    PaneScrapeStatus.ReplayedSequence, PaneScrapeStatus.Accepted, PaneScrapeStatus.ReplayedSequence },
                    host.Session.Sent.OfType<PaneScrapeUpdate>().Where(x => x.IsDecision).Select(x => x.Status));
                _output.WriteLine("assertions: one Pane byte changed; WrongPane consumed both lease high-water and stroke sequence; " +
                    "corrected replay ReplayedSequence; fresh hook Stroke accepted once; duplicate denied. " +
                    "host glass/material writes=2, guest glass=0; host effects=0, guest effects=2; " +
                    "cutoff=.26 on both; separate Frost=.73/side=.61 and frost material writes=0 throughout. " +
                    "Guest may reapply identical absolute material values on denial; no value/revision/effect advance.");
            }
            finally
            {
                wire.Dispose(); Assert.False(wire.Armed); Assert.Null(guest.Session.Sending);
                Select(guest, guestPose); guest.Bridge.ResetSession();
                Select(host, hostPose); host.Bridge.ResetSession();
                foreach (var rig in new[] { host, guest })
                {
                    foreach (var pair in rig.Originals) Assert.Same(pair.Value, pair.Key.Actions);
                    Assert.Same(rig.Globals, rig.Hand.Fsm.GlobalTransitions); Assert.Same(rig.Events, rig.Hand.Fsm.Events);
                    Assert.Null(Get<object?>(rig.Bridge, "_lease")); Assert.Null(Get<object?>(rig.Bridge, "_authority"));
                }
                PlayerSyncManager.Instance = oldPose;
                FixtureRecord("cleanup", "finally", new { mutation_disarmed = !wire.Armed,
                    observer_detached = guest.Session.Sending == null,
                    bridges_reset = Get<object?>(host.Bridge, "_authority") == null && Get<object?>(guest.Bridge, "_authority") == null });
                _output.WriteLine("cleanup: mutation disarmed/detached; both bridges reset, original actions/hand routing restored; " +
                    "portable only, no native process/deployment/save/rig resource created.");
            }
        }

        [Fact]
        public void WrongPaneBoundaryDisposalCancelsPendingMutationAndPreservesOtherObservers()
        {
            using var rig = new Rig(false);
            var session = rig.Session;
            Action<IMessage> observer = _ => { };
            session.Sending = observer;
            var wire = new GuestStrokeWire(session); wire.ArmWrongPane(); wire.Dispose(); wire.Dispose();
            Assert.False(wire.Armed); Assert.Same(observer, session.Sending); Assert.Empty(wire.Packets);
            Assert.Throws<ObjectDisposedException>(() => wire.ArmWrongPane());
            Assert.Throws<ArgumentException>(() => new GuestStrokeWire(new SessionManager()));
        }

        private sealed class UnencodablePacket : IMessage
        {
            public MessageId Id => MessageId.ScraperAction;
            public void Write(NetWriter writer) { throw new InvalidOperationException("portable encode failure"); }
            public void Read(NetReader reader) { throw new NotSupportedException(); }
        }

        [Fact]
        public void WrongPaneBoundaryFailureClearsPendingMutationAndDetaches()
        {
            using var rig = new Rig(false);
            var session = rig.Session;
            using var wire = new GuestStrokeWire(session); wire.ArmWrongPane();
            Assert.Throws<InvalidOperationException>(() => session.SendWorldMessage(new UnencodablePacket(), Channel.ReliableOrdered));
            Assert.False(wire.Armed); Assert.Null(session.Sending); Assert.Empty(wire.Packets);
            Assert.Equal(0, wire.Mutations);
        }
    }
}
