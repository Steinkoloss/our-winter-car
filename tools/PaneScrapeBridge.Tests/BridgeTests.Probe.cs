using System;
using System.IO;
using System.Linq;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace PaneScrapeBridge.Tests
{
    public sealed partial class BridgeTests
    {
        [Theory]
        [InlineData("bag-env")][InlineData("pane-env")][InlineData("bag-marker")][InlineData("pane-marker")]
        [InlineData("host")][InlineData("idle")][InlineData("missing-session")]
        public void DiagnosticRequiresEveryOptInAndConnectedGuestAtArmAndSend(string missing)
        {
            using var rig = new Rig(false);
            using var probe = new ProbeBoundary(rig.Session);
            probe.Arm();
            switch (missing)
            {
                case "bag-env": Environment.SetEnvironmentVariable("WINTERMP_LOCAL2P_BAG_TEST", "0"); break;
                case "pane-env": Environment.SetEnvironmentVariable("WINTERMP_LOCAL2P_V11_PANE_TEST", "0"); break;
                case "bag-marker": File.Delete(Path.Combine(probe.Root, "wintermp-live-bag-sandbox.txt")); break;
                case "pane-marker": File.Delete(Path.Combine(probe.Root, "wintermp-v11-pane-sandbox.txt")); break;
                case "host": rig.Session.IsHost = true; break;
                case "idle": rig.Session.State = SessionState.Idle; break;
                case "missing-session": SessionManager.Instance = null; break;
            }
            var request = rig.Request(20, ScraperOperation.Stroke);
            byte[] before = PacketCodec.Encode(request);
            Assert.Same(request, probe.Observe(request)); Assert.False(probe.Armed);
            Assert.Equal(before, PacketCodec.Encode(request));
            Assert.Throws<InvalidOperationException>(() => probe.Arm()); Assert.False(probe.Armed);
        }

        [Theory]
        [InlineData("ResetSession")][InlineData("Fail")][InlineData("OnDestroy")]
        public void DiagnosticRegistersTeardownAndFailureCleanup(string boundary)
        {
            using var rig = new Rig(false);
            using var probe = new ProbeBoundary(rig.Session);
            probe.Arm();
            probe.Lifecycle(boundary); // registered source prefix; detour engine is a double
            Assert.False(probe.Armed);
            var request = rig.Request(20, ScraperOperation.Stroke);
            Assert.Same(request, probe.Observe(request));
            Assert.Contains("pane-mutation-packets|none", probe.Snapshot());
        }

        [Theory]
        [InlineData("SendWorldMessage")][InlineData("Tick")][InlineData("Execute")][InlineData("Snapshot")]
        public void DiagnosticRegisteredFinalizerClearsOnlyOnFailure(string boundary)
        {
            using var rig = new Rig(false);
            using var probe = new ProbeBoundary(rig.Session);
            probe.Arm(); probe.Failure(boundary, null); Assert.True(probe.Armed);
            probe.Failure(boundary, new InvalidOperationException("portable boundary failure"));
            Assert.False(probe.Armed);
            Assert.Contains("pane-mutation-packets|none", probe.Snapshot());
        }

        [Fact]
        public void DiagnosticUnexpectedPaneFailureCannotMutateTheNextStroke()
        {
            using var rig = new Rig(false);
            using var probe = new ProbeBoundary(rig.Session);
            probe.Arm();
            var unexpected = rig.Request(20, ScraperOperation.Stroke); unexpected.Pane = 2;
            byte[] before = PacketCodec.Encode(unexpected);
            Assert.Throws<InvalidOperationException>(() => probe.Observe(unexpected));
            Assert.False(probe.Armed); Assert.Equal(before, PacketCodec.Encode(unexpected));
            var fresh = rig.Request(21, ScraperOperation.Stroke);
            Assert.Same(fresh, probe.Observe(fresh));
            Assert.Contains("pane-mutation-packets|none", probe.Snapshot());
        }

        [Fact]
        public void DiagnosticNeverMutatesWrongActorEquipmentResultsOrPreviouslyObservedStroke()
        {
            using var rig = new Rig(false);
            using var probe = new ProbeBoundary(rig.Session);
            var first = rig.Request(20, ScraperOperation.Stroke);
            Assert.Same(first, probe.Observe(first)); probe.Arm();
            foreach (var op in new[] { ScraperOperation.Pickup, ScraperOperation.Equip, ScraperOperation.KeepAlive,
                ScraperOperation.Drop, ScraperOperation.Off })
            {
                var other = rig.Request(21, op);
                Assert.Same(other, probe.Observe(other)); Assert.True(probe.Armed);
            }
            var wrongActor = rig.Request(21, ScraperOperation.Stroke, 3);
            Assert.Same(wrongActor, probe.Observe(wrongActor)); Assert.True(probe.Armed);
            var result = new PaneScrapeUpdate();
            Assert.Same(result, probe.Observe(result)); Assert.True(probe.Armed);
            Assert.Same(first, probe.Observe(first)); Assert.True(probe.Armed);
            var older = rig.Request(19, ScraperOperation.Stroke);
            Assert.Same(older, probe.Observe(older)); Assert.True(probe.Armed);
            Assert.Same(first, probe.Observe(first)); Assert.True(probe.Armed);
            var next = rig.Request(21, ScraperOperation.Stroke);
            var sent = Assert.IsType<ScraperAction>(probe.Observe(next));
            Assert.Equal((byte)2, sent.Pane); Assert.Equal((byte)1, next.Pane); Assert.False(probe.Armed);
            var original = PacketCodec.Encode(next); var transmitted = PacketCodec.Encode(sent);
            Assert.Single(original.Zip(transmitted), pair => pair.First != pair.Second);
            string provenance = Convert.ToBase64String(original) + "|" + Convert.ToBase64String(transmitted);
            Assert.Contains("pane-stroke-packets|" + provenance, probe.Snapshot());
            Assert.Contains("pane-mutation-packets|" + provenance, probe.Snapshot());
            Assert.Contains("pane-stroke-packets|" + provenance, BepInEx.Logging.Logger.ProbeLog.Lines);
            // A replay of the altered packet must not replace the retained original.
            Assert.Same(sent, probe.Observe(sent));
            var retained = (ScraperAction)ProbeBoundary.Read("_paneOriginalStroke")!;
            Assert.Equal(original, PacketCodec.Encode(retained));
            _output.WriteLine("source-linked probe packet original|transmitted: " + provenance);
        }

        [Fact]
        public void DiagnosticSessionReplacementCancelsPendingMutation()
        {
            using var rig = new Rig(false);
            using var probe = new ProbeBoundary(rig.Session);
            probe.Arm();
            SessionManager.Instance = new SessionManager { IsHost = false, State = SessionState.Connected, LocalPlayerId = 2 };
            var request = rig.Request(20, ScraperOperation.Stroke);
            Assert.Same(request, probe.Observe(request)); Assert.False(probe.Armed);
        }

        [Fact]
        public void DiagnosticEpochChangeCancelsPendingMutationWithoutWritingSequence()
        {
            using var rig = new Rig(false);
            using var probe = new ProbeBoundary(rig.Session);
            var previous = rig.Request(20, ScraperOperation.Stroke); previous.Epoch = 42;
            probe.Observe(previous); probe.Arm();
            var changed = rig.Request(21, ScraperOperation.Stroke); changed.Epoch = 43;
            byte[] original = PacketCodec.Encode(changed);
            Assert.Same(changed, probe.Observe(changed)); Assert.False(probe.Armed);
            Assert.Equal(original, PacketCodec.Encode(changed)); Assert.Equal(0u, Get<uint>(rig.Bridge, "_sequence"));
        }

        [Fact]
        public void DiagnosticLoggingFailureClearsMutationBeforeTransmission()
        {
            using var rig = new Rig(false);
            using var probe = new ProbeBoundary(rig.Session);
            probe.Arm(); BepInEx.Logging.Logger.ProbeLog.Fail = true;
            var request = rig.Request(20, ScraperOperation.Stroke);
            byte[] original = PacketCodec.Encode(request);
            Assert.Throws<InvalidOperationException>(() => probe.Observe(request));
            Assert.False(probe.Armed); Assert.Equal(original, PacketCodec.Encode(request));
            BepInEx.Logging.Logger.ProbeLog.Fail = false;
            Assert.Contains("pane-mutation-packets|none", probe.Snapshot());
            Assert.Same(request, probe.Observe(request));
        }
    }
}
