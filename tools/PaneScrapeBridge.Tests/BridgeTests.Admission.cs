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
        private static PaneScrapeUpdate CopyUpdate(PaneScrapeUpdate update)
            => Assert.IsType<PaneScrapeUpdate>(PacketCodec.Decode(PacketCodec.Encode(update)));

        private static void Deliver(Rig guest, PaneScrapeUpdate update)
        {
            SessionManager.Instance = guest.Session;
            guest.Bridge.OnUpdate(CopyUpdate(update));
        }

        private static void AdmitAndEquipGuest(Rig host, Rig guest)
        {
            Deliver(guest, host.Session.Sent.OfType<PaneScrapeUpdate>().Single(x => x.Actor == 2 && x.Sequence == 0));
            Assert.True(Get<bool>(guest.Bridge, "_admitted"));
            Assert.Equal(host.Epoch, guest.Epoch);
            guest.Picked.Value = guest.Tool.Body!.gameObject;
            guest.Enter("Set pivot 2");
            var pickup = Assert.IsType<ScraperAction>(guest.Session.Sent.Last());
            Assert.Equal(ScraperOperation.Pickup, pickup.Operation);
            Physics.Contact = host.ToolContact;
            var approval = host.Act(pickup);
            Assert.Equal(PaneScrapeStatus.Accepted, approval.Status);
            Deliver(guest, approval);
            Call(guest.Bridge, "Update"); guest.Enter("Ice Scraper");
            foreach (var action in guest.Session.Sent.OfType<ScraperAction>().Skip(1).ToArray())
            {
                var result = host.Act(action);
                Assert.Equal(PaneScrapeStatus.Accepted, result.Status);
                Deliver(guest, result);
            }
            Physics.Contact = host.Pane;
            AssertParkedNonOwner(host); AssertParkedNonOwner(guest);
        }

        private static ScraperAction GuestStroke(Rig guest)
        {
            SessionManager.Instance = guest.Session;
            int sent = guest.Session.Sent.Count;
            guest.Scrape.Fsm.States[0].Enter();
            Assert.Equal(sent + 1, guest.Session.Sent.Count);
            var action = Assert.IsType<ScraperAction>(PacketCodec.Decode(PacketCodec.Encode(guest.Session.Sent.Last())));
            Assert.Equal(ScraperOperation.Stroke, action.Operation);
            Assert.Equal(0, guest.Glass.Calls);
            return action;
        }

        private void TraceContract(string label, ScraperAction action, PaneScrapeUpdate result)
        {
            _output.WriteLine("portable {0}: request={1} result={2}", label,
                Convert.ToBase64String(PacketCodec.Encode(action)), Convert.ToBase64String(PacketCodec.Encode(result)));
            _output.WriteLine("portable {0}: actor={1} epoch={2} sequence={3} highWater={4} status={5} revision={6} cutoff={7:R}",
                label, result.Actor, result.Epoch, result.Sequence, result.HighWater, result.Status, result.Revision, result.Cutoff);
        }

        [Theory]
        [InlineData(false)][InlineData(true)]
        public void InvalidInitialUpdateCannotPinEpochOrBlockValidGuestAdmissionAndStroke(bool decision)
        {
            using var host = new Rig();
            using var guest = new Rig(false);
            var malformed = CopyUpdate(host.Session.Sent.OfType<PaneScrapeUpdate>().Single(x => x.Actor == 2));
            malformed.Epoch = host.Epoch == uint.MaxValue ? 1 : host.Epoch + 1;
            malformed.Revision = 0;
            malformed.Cutoff = .99f;
            malformed.IsDecision = decision;
            malformed.Sequence = decision ? 999u : 0u;
            malformed.HighWater = 999;
            Deliver(guest, malformed);
            _output.WriteLine("rejected initial {0}: bytes={1}; bridgeEpoch={2}, candidateEpoch={3}, hostEpoch={4}",
                decision ? "decision" : "snapshot", Convert.ToBase64String(PacketCodec.Encode(malformed)), guest.Epoch, malformed.Epoch, host.Epoch);
            Assert.Equal(0u, guest.Epoch);
            Assert.Null(Get<PaneScrapeReplica?>(guest.Bridge, "_replica"));
            Assert.Null(Get<PaneScrapeUpdate?>(guest.Bridge, "_received"));
            Assert.False(Get<bool>(guest.Bridge, "_admitted"));
            Assert.Equal(0u, Get<uint>(guest.Bridge, "_sequence"));
            Assert.Equal(.25f, guest.Cutoff.Value); Assert.Equal(0, guest.Material.Writes);
            Assert.Equal(0, guest.Glass.Calls); Assert.Equal(0, guest.Effect.Calls);
            guest.Picked.Value = guest.Tool.Body!.gameObject;
            guest.Scrape.Fsm.States[0].Enter();
            Assert.Empty(guest.Session.Sent); // rejected admission never enables speculative input
            Assert.Equal(.25f, host.Cutoff.Value); Assert.Equal(0, host.Glass.Calls);

            AdmitAndEquipGuest(host, guest);
            var stroke = GuestStroke(guest);
            AssertGlassAndEffectsBeforeResult(host, guest);
            var accepted = host.Act(stroke);
            Assert.Equal(PaneScrapeStatus.Accepted, accepted.Status);
            Assert.Equal(host.Epoch, accepted.Epoch); Assert.Equal(2u, accepted.Revision);
            Assert.Equal(stroke.Sequence, accepted.HighWater);
            Deliver(guest, accepted); Deliver(guest, accepted);
            AssertGlassAndEffects(host, guest, .255f, 1);
            TraceContract("admission-recovery", stroke, accepted);

            // Once a VALID epoch is installed, another epoch remains barred.
            malformed.Revision = 100;
            Deliver(guest, malformed);
            Assert.Equal(host.Epoch, guest.Epoch);
            Assert.Equal(accepted.HighWater, Get<uint>(guest.Bridge, "_sequence"));
            AssertGlassAndEffects(host, guest, .255f, 1);
            var replay = host.Act(stroke);
            Assert.Equal(PaneScrapeStatus.ReplayedSequence, replay.Status);
            AssertSameAbsoluteResult(accepted, replay);
            Deliver(guest, replay);
            AssertGlassAndEffects(host, guest, .255f, 1);
            AssertParkedNonOwner(host); AssertParkedNonOwner(guest);
            TraceContract("admission-recovery-duplicate", stroke, replay);
        }

        private static void AssertGlassAndEffectsBeforeResult(Rig host, Rig guest)
        {
            Assert.Equal(.25f, host.Cutoff.Value); Assert.Equal(.25f, guest.Cutoff.Value);
            Assert.Equal(0, host.Glass.Calls); Assert.Equal(0, guest.Glass.Calls);
            Assert.Equal(0, host.Effect.Calls); Assert.Equal(0, guest.Effect.Calls);
        }

        [Theory]
        [InlineData("pane", PaneScrapeStatus.WrongPane)]
        [InlineData("area", PaneScrapeStatus.WrongPane)]
        [InlineData("epoch", PaneScrapeStatus.StaleEpoch)]
        [InlineData("actor", PaneScrapeStatus.InvalidActor)]
        [InlineData("tool", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("other-holder", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("absent", PaneScrapeStatus.ActorUnavailable)]
        [InlineData("lease-stale", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("pose-stale", PaneScrapeStatus.ActorUnavailable)]
        [InlineData("contact-range", PaneScrapeStatus.InvalidContact)]
        [InlineData("contact-negative", PaneScrapeStatus.InvalidContact)]
        [InlineData("contact-nan", PaneScrapeStatus.InvalidContact)]
        [InlineData("contact-infinity", PaneScrapeStatus.InvalidContact)]
        [InlineData("eye-nan", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("eye-infinity", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("eye-range", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("direction-nan", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("direction-infinity", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("direction-range", PaneScrapeStatus.InvalidEquipment)]
        [InlineData("duplicate", PaneScrapeStatus.ReplayedSequence)]
        public void InvalidGuestStrokeLeavesBothProductionReplicasAndGlassUnchanged(string invalid, PaneScrapeStatus expected)
        {
            using var host = new Rig();
            var hostFrost = new FrostBoundary(host);
            using var guest = new Rig(false);
            var guestFrost = new FrostBoundary(guest);
            AdmitAndEquipGuest(host, guest);
            var first = GuestStroke(guest);
            var positive = host.Act(first);
            Assert.Equal(PaneScrapeStatus.Accepted, positive.Status);
            Deliver(guest, positive);
            AssertGlassAndEffects(host, guest, .255f, 1);
            var action = GuestStroke(guest);
            byte authenticatedActor = 2;
            switch (invalid)
            {
                case "pane": action.Pane = 2; break;
                case "area": action.VehicleId++; break;
                case "epoch": action.Epoch = host.Epoch == uint.MaxValue ? 1 : host.Epoch + 1; break;
                case "actor": action.Actor = 3; break;
                case "tool": action.ToolId++; break;
                case "other-holder":
                    host.Session.Players.Add(new RemotePlayer { PlayerId = 3 });
                    action.Actor = authenticatedActor = 3; break;
                case "absent": host.Session.Leave(host.Session.Players.Single()); break;
                case "lease-stale": Time.unscaledTime = host.Session.Players.Single().LastTransformTime = 11; break;
                case "pose-stale": host.Session.Players.Single().LastTransformTime = 9; break;
                case "contact-range": Physics.Distance = .801f; break;
                case "contact-negative": Physics.Distance = -.01f; break;
                case "contact-nan": Physics.Distance = float.NaN; break;
                case "contact-infinity": Physics.Distance = float.PositiveInfinity; break;
                case "eye-nan": action.Eye = new NetVector3(float.NaN, 1, 0); break;
                case "eye-infinity": action.Eye = new NetVector3(0, float.PositiveInfinity, 0); break;
                case "eye-range": action.Eye = new NetVector3(9, 1, 0); break;
                case "direction-nan": action.Direction = new NetVector3(float.NaN, 0, 1); break;
                case "direction-infinity": action.Direction = new NetVector3(0, 0, float.PositiveInfinity); break;
                case "direction-range": action.Direction = new NetVector3(0, 0, 2); break;
                case "duplicate": action = first; break;
                default: throw new ArgumentException(invalid);
            }
            int sent = host.Session.Sent.Count;
            var denied = host.Act(action, authenticatedActor);
            Assert.Equal(expected, denied.Status);
            Assert.Equal(sent + 1, host.Session.Sent.Count);
            Assert.Equal(positive.Epoch, denied.Epoch); Assert.Equal(positive.Revision, denied.Revision);
            Assert.Equal(positive.Cutoff, denied.Cutoff);
            uint expectedHighWater = invalid == "epoch" || invalid == "actor" || invalid == "duplicate"
                ? positive.HighWater : action.Sequence;
            Assert.Equal(expectedHighWater, denied.HighWater);
            Deliver(guest, denied); Deliver(guest, denied); Deliver(guest, positive);
            AssertGlassAndEffects(host, guest, .255f, 1);
            AssertParkedNonOwner(host); AssertParkedNonOwner(guest);
            hostFrost.AssertUntouched(); guestFrost.AssertUntouched();
            TraceContract("denied-" + invalid, action, denied);
            _output.WriteLine("asserted: unchanged peer cutoff/material/revision; host glass=1 guest glass=0; " +
                "host effects=0 guest effects=1; no ownership transfer; unrelated pane/frost sentinels unchanged.");
        }

        [Theory]
        [InlineData(float.NaN)][InlineData(float.PositiveInfinity)][InlineData(float.NegativeInfinity)]
        [InlineData(-1f)][InlineData(0f)][InlineData(.005f)][InlineData(1f)][InlineData(float.MaxValue)]
        public void GuestCannotAppendAnyScrapeContributionOrPublishAnAbsolutePaneResult(float contribution)
        {
            using var host = new Rig();
            using var guest = new Rig(false);
            AdmitAndEquipGuest(host, guest);
            var action = GuestStroke(guest);
            var writer = new NetWriter(); writer.WriteSingle(contribution);
            var malformed = PacketCodec.Encode(action).Concat(writer.ToArray()).ToArray();
            uint seen = Get<ScraperLease>(host.Bridge, "_lease").Seen(2);
            int sent = host.Session.Sent.Count;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(malformed));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.PaneScrapeUpdate, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.PaneScrapeUpdate, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.ScraperAction, true, false, false, true));
            Assert.Equal(seen, Get<ScraperLease>(host.Bridge, "_lease").Seen(2));
            Assert.Equal(sent, host.Session.Sent.Count);
            AssertGlassAndEffectsBeforeResult(host, guest);
            var result = host.Act(action);
            Assert.Equal(PaneScrapeStatus.Accepted, result.Status);
            Deliver(guest, result);
            AssertGlassAndEffects(host, guest, .255f, 1);
            TraceContract("native-only-contribution", action, result);
            _output.WriteLine("rejected appended contribution={0:R} packet={1}; codec RequireEnd rejects ANY extra " +
                "contribution, including .005. Directional session policy bars guest results/unauthed intents. " +
                "Only the unchanged valid hook packet executes the host action double's .005; no guest delta field exists.",
                contribution, Convert.ToBase64String(malformed));
        }
    }
}
