using System;
using System.Collections.Generic;
using System.Linq;
using S09.ShutdownFixture;
using UnityEngine;
using WinterMP.Core;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;
using Xunit;
using Xunit.Abstractions;
using Sync = S09.ShutdownFixture.Sync;
using LaunchMode = S09.ShutdownFixture.LaunchMode;

namespace TrainSend.Tests
{
    public sealed class ShutdownTests : IDisposable
    {
        private static readonly PeerId[] Peers = { new PeerId(101), new PeerId(202), new PeerId(303) };
        private readonly List<ShutdownTransport> _owned = new List<ShutdownTransport>();
        private readonly ITestOutputHelper _output;
        public ShutdownTests(ITestOutputHelper output)
        {
            _output = output; Time.unscaledTime = 10; WinterMPPlugin.Log.Lines.Clear();
            Sync.PaneScrapeSync.Instance.Resets = Sync.PlayerSyncManager.Instance.Resets = Sync.DeathSyncManager.Instance.Resets = 0;
            Sync.PlayerSyncManager.Instance.ClothingResets = 0;
            Sync.PaneScrapeSync.Instance.OnReset = () => { };
            NetTrafficMeter.Instance.Resets = NetTrafficMeter.Instance.Sent = 0;
        }
        private ShutdownTransport Attach(SessionManager session)
        {
            var transport = new ShutdownTransport { IsHost = session.IsHost };
            _owned.Add(transport); session.AttachFixture(transport);
            Assert.Equal(3, transport.Subscriptions);
            return transport;
        }
        public void Dispose()
        {
            // Test-run resource cleanup also runs on red. It is not the production assertion.
            foreach (var transport in _owned)
            {
                transport.OnDispose = () => { };
                if (transport.Disposals == 0) transport.Dispose();
            }
            _owned.Clear();
            Sync.PaneScrapeSync.Instance.OnReset = () => { };
            WinterMP.Core.Session.ConnectionQuality.Instance.Reset();
        }
        private void Observe(SessionManager session, ShutdownTransport transport, Exception? failure)
        {
            _output.WriteLine($"observed: dispose={transport.Disposals}, hasTransport={session.HasTransport}, peers={session.PeerOrder.Length}, pending={session.CleanupPending}, state={session.State}");
            _output.WriteLine("attempted=" + string.Join(",", transport.Attempts.Select(a => a.Peer.Value))
                + "; delivered=" + string.Join(",", transport.Delivered.Select(a => a.Peer.Value)));
            _output.WriteLine(failure?.ToString() ?? "no exception");
        }
        private static void AssertCleanup(SessionManager session, ShutdownTransport transport, DevLoopbackClient dev, int resets = 1)
        {
            Assert.Equal(1, transport.Disposals); Assert.Equal(1, dev.Disposals);
            session.AssertRuntimeCleared();
            Assert.Equal(resets, Sync.PaneScrapeSync.Instance.Resets);
            Assert.Equal(resets, Sync.PlayerSyncManager.Instance.Resets);
            Assert.Equal(resets, Sync.PlayerSyncManager.Instance.ClothingResets);
            Assert.Equal(resets, Sync.DeathSyncManager.Instance.Resets);
            Assert.Equal(resets, NetTrafficMeter.Instance.Resets);
            Assert.Equal(resets, WinterMPPlugin.Log.Lines.Count(s => s == "Session: Idle — Idle"));
        }
        private static void AssertDisconnects(ShutdownTransport transport, PeerId[] attempted, PeerId[] delivered, string reason)
        {
            Assert.Equal(attempted, transport.Attempts.Select(a => a.Peer));
            Assert.Equal(delivered, transport.Delivered.Select(a => a.Peer));
            Assert.Equal(transport.Attempts.Count, transport.Attempts.Select(a => a.Peer).Distinct().Count());
            Assert.All(transport.Attempts, a =>
            {
                Assert.Equal(Channel.ReliableOrdered, a.Channel);
                Assert.Equal(reason, Assert.IsType<DisconnectMessage>(a.Message).Reason);
            });
        }

        [Theory]
        [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
        [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
        public void DisconnectFailureStillDisposesResetsAndTransitionsIdleExactlyOnce(int failureIndex, bool afterDelivery)
        {
            var session = new SessionManager(); var dev = session.Seed(SessionState.Hosting, Peers);
            var transport = Attach(session); var order = session.PeerOrder;
            var failure = new InvalidOperationException("selected disconnect send failure", new ArgumentException("original transport cause"));
            Action<ShutdownTransport.Attempt> fail = a => { if (a.Peer == order[failureIndex]) throw failure; };
            if (afterDelivery) transport.AfterDeliver = fail; else transport.BeforeDeliver = fail;
            var escaped = Record.Exception(() => session.Shutdown("leaving session"));
            Observe(session, transport, escaped);
            // Preserve original fallback diagnostics, including exception identity, inner cause and stack.
            Assert.Same(failure, escaped); Assert.Contains("SessionManager.SendTo", escaped!.StackTrace);
            Assert.Contains("SessionManager.Shutdown", escaped.StackTrace);
            Assert.Contains("original transport cause", escaped.ToString());
            AssertDisconnects(transport, order.Take(failureIndex + 1).ToArray(),
                order.Take(failureIndex + (afterDelivery ? 1 : 0)).ToArray(), "leaving session");
            AssertCleanup(session, transport, dev);
            session.AssertSeatSequenceReset();
            int attempts = transport.Attempts.Count;
            session.SendFixture(Peers[0], new PingMessage { Nonce = 11 }); transport.Update();
            Assert.Equal(attempts, transport.Attempts.Count);
            _output.WriteLine("PASS: original exception preserved; ordered prefix only; no retry; transport/runtime/Idle cleanup once; detached sends inert.");
        }

        [Theory]
        [InlineData(SessionState.Hosting, false)] [InlineData(SessionState.Hosting, true)]
        [InlineData(SessionState.Connected, false)] [InlineData(SessionState.Connected, true)]
        public void PaneResetFailureAfterDisconnectFailurePreservesPrimaryAndClearsSession(SessionState role, bool afterDelivery)
        {
            var session = new SessionManager();
            var dev = session.Seed(role, role == SessionState.Hosting ? Peers : new[] { new PeerId(900) });
            var transport = Attach(session); var order = session.PeerOrder;
            int failureIndex = order.Length - 1;
            var primary = new InvalidOperationException("primary disconnect failure", new ArgumentException("primary inner cause"));
            Action<ShutdownTransport.Attempt> fail = a => { if (a.Peer == order[failureIndex]) throw primary; };
            if (afterDelivery) transport.AfterDeliver = fail; else transport.BeforeDeliver = fail;
            var cleanup = new ApplicationException("selected Pane reset failure", new ArgumentException("cleanup inner cause"));
            Sync.PaneScrapeSync.Instance.OnReset = () => throw cleanup;

            var escaped = Record.Exception(() => session.Shutdown("callback failure shutdown"));
            Observe(session, transport, escaped);
            _output.WriteLine($"callback observations: pane={Sync.PaneScrapeSync.Instance.Resets}, player={Sync.PlayerSyncManager.Instance.Resets}, clothing={Sync.PlayerSyncManager.Instance.ClothingResets}, death={Sync.DeathSyncManager.Instance.Resets}");
            Assert.Same(primary, escaped); Assert.Same(primary.InnerException, escaped!.InnerException);
            Assert.Contains("SessionManager.SendTo", escaped.StackTrace);
            Assert.Contains("SessionManager.Shutdown", escaped.StackTrace);
            Assert.Contains("primary inner cause", escaped.ToString());
            AssertDisconnects(transport, order, order.Take(failureIndex + (afterDelivery ? 1 : 0)).ToArray(), "callback failure shutdown");
            AssertCleanup(session, transport, dev);
            AssertPaneFailureLogged(cleanup);
            int attempts = transport.Attempts.Count;
            session.SendFixture(order[0], new PingMessage { Nonce = 11 });
            Assert.Equal(attempts, transport.Attempts.Count);

            // Role/admission seeding is not a native rejoin; exercise the real codec on a new transport.
            Sync.PaneScrapeSync.Instance.OnReset = () => { };
            var freshPeer = new PeerId(404); var freshDev = session.Seed(role, freshPeer); var fresh = Attach(session);
            session.SendFixture(freshPeer, new TrainState { Sequence = 1 });
            Assert.Equal(1u, Assert.IsType<TrainState>(Assert.Single(fresh.Delivered).Message).Sequence);
            Assert.Null(Record.Exception(() => session.Shutdown("fresh session")));
            AssertCleanup(session, fresh, freshDev, 2);
            Assert.Equal(attempts, transport.Attempts.Count); Assert.Equal(1, transport.Disposals);
            AssertPaneFailureLogged(cleanup);
            _output.WriteLine("PASS: one Pane callback fault logged; original disconnect identity/stack/inner cause preserved; remaining cleanup and Idle once; fresh transport works; no delivery or callback retry.");
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)] [InlineData(SessionState.Connecting)]
        public void PaneResetFailureWithoutDisconnectFailureStillClearsAndPropagatesCleanup(SessionState role)
        {
            var session = new SessionManager();
            var peers = role == SessionState.Hosting ? Peers : role == SessionState.Connected ? new[] { new PeerId(900) } : new PeerId[0];
            var dev = session.Seed(role, peers); var transport = Attach(session); var order = session.PeerOrder;
            var cleanup = new ApplicationException("selected Pane reset failure", new ArgumentException("cleanup inner cause"));
            Sync.PaneScrapeSync.Instance.OnReset = () => throw cleanup;
            var escaped = Record.Exception(() => session.Shutdown("cleanup only failure"));
            Observe(session, transport, escaped);
            Assert.Same(cleanup, escaped); Assert.Same(cleanup.InnerException, escaped!.InnerException);
            Assert.Contains("PaneScrapeSync.ResetSession", escaped.StackTrace);
            Assert.Contains("SessionManager.ResetSessionRuntimeState", escaped.StackTrace);
            AssertDisconnects(transport, order, order, "cleanup only failure");
            AssertCleanup(session, transport, dev); AssertPaneFailureLogged(cleanup);
            Sync.PaneScrapeSync.Instance.OnReset = () => { };
            Assert.Null(Record.Exception(() => session.Shutdown("already idle")));
            AssertCleanup(session, transport, dev, 2);
            AssertDisconnects(transport, order, order, "cleanup only failure");
            AssertPaneFailureLogged(cleanup);
            _output.WriteLine("PASS: cleanup-only fault remains observable with original stack after runtime reset and Idle once; repeated shutdown never redelivers or redisposes.");
        }

        private void AssertPaneFailureLogged(Exception failure)
        {
            var line = Assert.Single(WinterMPPlugin.Log.Lines, s => s.StartsWith("Session PaneScrape reset failed: ", StringComparison.Ordinal));
            Assert.Contains(failure.GetType().FullName + ": " + failure.Message, line);
            Assert.Contains(failure.InnerException!.ToString(), line);
            Assert.Contains("PaneScrapeSync.ResetSession", line);
            _output.WriteLine(line);
        }

        [Theory]
        [InlineData(true, SessionState.Hosting, 0)] [InlineData(false, SessionState.Hosting, 0)]
        [InlineData(true, SessionState.Connected, 0)] [InlineData(false, SessionState.Connected, 0)]
        [InlineData(true, SessionState.Hosting, 1)] [InlineData(false, SessionState.Hosting, 1)]
        [InlineData(true, SessionState.Connected, 1)] [InlineData(false, SessionState.Connected, 1)]
        [InlineData(true, SessionState.Hosting, 2)] [InlineData(false, SessionState.Hosting, 2)]
        [InlineData(true, SessionState.Connected, 2)] [InlineData(false, SessionState.Connected, 2)]
        public void OneDisposeFaultStillClearsShutdownAndPreservesPrimary(bool devFault, SessionState role, int sendFault)
        {
            var session = new SessionManager();
            var dev = session.Seed(role, role == SessionState.Hosting ? Peers : new[] { new PeerId(900) });
            var transport = Attach(session); var order = session.PeerOrder;
            int failureIndex = role == SessionState.Hosting ? 1 : 0;
            var primary = new InvalidOperationException("primary disconnect failure", new ArgumentException("primary inner cause"));
            Action<ShutdownTransport.Attempt> fail = a => { if (a.Peer == order[failureIndex]) throw primary; };
            if (sendFault == 1) transport.BeforeDeliver = fail;
            if (sendFault == 2) transport.AfterDeliver = fail;
            var disposal = new ApplicationException("selected disposal failure", new ArgumentException("disposal inner cause"));
            bool detachedAtDispose = false;
            Action failDispose = () =>
            {
                detachedAtDispose = !session.HasDevClient && !session.HasTransport;
                throw disposal;
            };
            if (devFault) dev.OnDispose = failDispose; else transport.OnDispose = failDispose;

            var escaped = Record.Exception(() => session.Shutdown("disposal shutdown"));
            ObserveDisposal(session, transport, dev, escaped);
            if (sendFault == 0) Assert.Null(escaped);
            else
            {
                Assert.Same(primary, escaped); Assert.Same(primary.InnerException, escaped!.InnerException);
                Assert.Contains("SessionManager.SendTo", escaped.StackTrace);
                Assert.Contains("SessionManager.Shutdown", escaped.StackTrace);
                Assert.Contains("primary inner cause", escaped.ToString());
            }
            Assert.True(detachedAtDispose);
            AssertDisconnects(transport, sendFault == 0 ? order : order.Take(failureIndex + 1).ToArray(),
                sendFault == 0 ? order : order.Take(failureIndex + (sendFault == 2 ? 1 : 0)).ToArray(), "disposal shutdown");
            AssertCleanup(session, transport, dev); AssertDisposeFailureLogged(disposal, devFault);
            session.AssertSeatSequenceReset();
            int attempts = transport.Attempts.Count;
            session.SendFixture(order[0], new PingMessage { Nonce = 11 });
            Assert.Equal(attempts, transport.Attempts.Count);

            // The failed disposer remains armed: neither idle shutdown nor reuse may retry it.
            Assert.Null(Record.Exception(() => session.Shutdown("already idle")));
            AssertCleanup(session, transport, dev, 2);
            var freshPeer = new PeerId(404); var freshDev = session.Seed(role, freshPeer); var fresh = Attach(session);
            session.SendFixture(freshPeer, new TrainState { Sequence = 1 });
            Assert.Equal(1u, Assert.IsType<TrainState>(Assert.Single(fresh.Delivered).Message).Sequence);
            Assert.Null(Record.Exception(() => session.Shutdown("fresh session")));
            AssertCleanup(session, fresh, freshDev, 3);
            Assert.Equal(attempts, transport.Attempts.Count); Assert.Equal(1, transport.Disposals); Assert.Equal(1, dev.Disposals);
            AssertDisposeFailureLogged(disposal, devFault);
            _output.WriteLine("PASS: one disposal fault logged; both references detached before Dispose; runtime and Idle once; primary preserved; no send/dispose retry; fresh transport works.");
        }

        [Theory]
        [InlineData(true, SessionState.Hosting, LaunchMode.None)] [InlineData(false, SessionState.Hosting, LaunchMode.None)]
        [InlineData(true, SessionState.Connected, LaunchMode.None)] [InlineData(false, SessionState.Connected, LaunchMode.None)]
        [InlineData(true, SessionState.Connected, LaunchMode.Host)] [InlineData(false, SessionState.Connected, LaunchMode.Host)]
        [InlineData(true, SessionState.Connecting, LaunchMode.JoinBrowse)] [InlineData(false, SessionState.Connecting, LaunchMode.JoinBrowse)]
        public void OneDisposeFaultDuringDeferredCleanupKeepsFailureOrPendingLaunch(bool devFault, SessionState role, LaunchMode pending)
        {
            var session = new SessionManager();
            var dev = session.Seed(role, role == SessionState.Hosting ? Peers : role == SessionState.Connected ? new[] { new PeerId(900) } : new PeerId[0]);
            var transport = Attach(session);
            var disposal = new ApplicationException("selected disposal failure", new ArgumentException("disposal inner cause"));
            if (devFault) dev.OnDispose = () => throw disposal; else transport.OnDispose = () => throw disposal;
            session.FailFixture("precise initiating failure", pending);
            Assert.True(session.CleanupPending); Assert.Equal(SessionState.Failed, session.State);
            Assert.Equal(0, transport.Disposals); Assert.Equal(0, dev.Disposals);
            var escaped = Record.Exception(() => session.FlushFixture());
            ObserveDisposal(session, transport, dev, escaped);
            Assert.Null(escaped);
            string status = pending == LaunchMode.None ? "precise initiating failure"
                : pending == LaunchMode.JoinBrowse ? "Waiting for main menu — pick a friend to join"
                : "Waiting for game to boot before 'Host'...";
            Action assertCleared = () => session.AssertRuntimeCleared(
                pending == LaunchMode.None ? SessionState.Failed : SessionState.Idle,
                status, role != SessionState.Hosting, pending == LaunchMode.None ? 10f : -1f);
            assertCleared(); session.AssertPendingMode(pending);
            Assert.Equal(1, dev.Disposals); Assert.Equal(1, transport.Disposals);
            AssertResetCounts(1); AssertDisposeFailureLogged(disposal, devFault);
            Assert.Equal(pending == LaunchMode.None ? 1 : 2, WinterMPPlugin.Log.Lines.Count(s => s.StartsWith("Session: ", StringComparison.Ordinal)));
            Assert.Empty(transport.Attempts);
            Assert.Null(Record.Exception(() => session.FlushFixture()));
            assertCleared(); session.AssertPendingMode(pending); AssertResetCounts(1);
            Assert.Equal(1, dev.Disposals); Assert.Equal(1, transport.Disposals);
            Assert.Empty(transport.Attempts); AssertDisposeFailureLogged(disposal, devFault);
            _output.WriteLine("PASS: deferred disposal fault logged once; runtime reset once; precise failure/recovery time or pending launch transition retained; browser restored; no sends or cleanup retry.");
        }

        [Fact]
        public void DevDisposeFaultWithoutTransportStillResetsEmptyConnectingSession()
        {
            var session = new SessionManager(); var dev = session.Seed(SessionState.Connecting);
            var disposal = new ApplicationException("selected disposal failure", new ArgumentException("disposal inner cause"));
            dev.OnDispose = () => throw disposal;
            var escaped = Record.Exception(() => session.Shutdown("no transport"));
            _output.WriteLine($"dev-only observations: devDispose={dev.Disposals}, hasDev={session.HasDevClient}, state={session.State}, pane={Sync.PaneScrapeSync.Instance.Resets}");
            _output.WriteLine(escaped?.ToString() ?? "no exception");
            Assert.Null(escaped); session.AssertRuntimeCleared(); AssertResetCounts(1);
            Assert.Equal(1, dev.Disposals); AssertDisposeFailureLogged(disposal, true);
            _output.WriteLine("PASS: dev-only disposal fault contained without transport; empty connecting session reset once.");
        }

        private void ObserveDisposal(SessionManager session, ShutdownTransport transport, DevLoopbackClient dev, Exception? failure)
        {
            Observe(session, transport, failure);
            _output.WriteLine($"disposal observations: devDispose={dev.Disposals}, hasDev={session.HasDevClient}, transportDispose={transport.Disposals}, hasTransport={session.HasTransport}, pane={Sync.PaneScrapeSync.Instance.Resets}, player={Sync.PlayerSyncManager.Instance.Resets}, clothing={Sync.PlayerSyncManager.Instance.ClothingResets}, death={Sync.DeathSyncManager.Instance.Resets}, meter={NetTrafficMeter.Instance.Resets}, status={session.StatusText}");
        }

        private static void AssertResetCounts(int count)
        {
            Assert.Equal(count, Sync.PaneScrapeSync.Instance.Resets);
            Assert.Equal(count, Sync.PlayerSyncManager.Instance.Resets);
            Assert.Equal(count, Sync.PlayerSyncManager.Instance.ClothingResets);
            Assert.Equal(count, Sync.DeathSyncManager.Instance.Resets);
            Assert.Equal(count, NetTrafficMeter.Instance.Resets);
        }

        private void AssertDisposeFailureLogged(Exception failure, bool devFault)
        {
            var line = Assert.Single(WinterMPPlugin.Log.Lines, s => s.StartsWith("Session ", StringComparison.Ordinal) && s.Contains(" dispose failed: "));
            Assert.StartsWith(devFault ? "Session dev client dispose failed: " : "Session transport dispose failed: ", line);
            Assert.Contains(failure.GetType().FullName + ": " + failure.Message, line);
            Assert.Contains(failure.InnerException!.ToString(), line);
            Assert.Contains(devFault ? "DevLoopbackClient.Dispose" : "ShutdownTransport.Dispose", line);
            _output.WriteLine(line);
        }

        [Fact]
        public void HealthyMultiPeerShutdownKeepsOriginalOrderingAndOneDisconnectEach()
        {
            var session = new SessionManager(); var dev = session.Seed(SessionState.Hosting, Peers);
            var transport = Attach(session); var order = session.PeerOrder;
            Assert.Equal(Peers.Length, order.Length);
            Assert.Null(Record.Exception(() => session.Shutdown("normal shutdown")));
            Observe(session, transport, null);
            AssertDisconnects(transport, order, order, "normal shutdown");
            AssertCleanup(session, transport, dev);
            session.Shutdown("already idle");
            Assert.Equal(1, transport.Disposals); Assert.Equal(1, dev.Disposals);
            AssertDisconnects(transport, order, order, "normal shutdown");
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void GuestDisconnectFailureClearsSingleHostAndCurrentClothingState(bool afterDelivery)
        {
            var session = new SessionManager(); var host = new PeerId(900);
            var dev = session.Seed(SessionState.Connected, host); var transport = Attach(session);
            Assert.Equal(123ul, session.LocalClothingAdmission);
            var failure = new InvalidOperationException("guest disconnect failure");
            if (afterDelivery) transport.AfterDeliver = _ => throw failure;
            else transport.BeforeDeliver = _ => throw failure;
            var escaped = Record.Exception(() => session.Shutdown("guest leaving"));
            Observe(session, transport, escaped);
            Assert.Same(failure, escaped);
            AssertDisconnects(transport, new[] { host }, afterDelivery ? new[] { host } : new PeerId[0], "guest leaving");
            AssertCleanup(session, transport, dev);
            Assert.Equal(0ul, session.LocalClothingAdmission);
            _output.WriteLine("PASS: guest single-host disconnect not retried; current clothing admission/sequence and all runtime state cleared once.");
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void SameManagerCanAttachFreshSessionAndSendAfterFailedShutdown(SessionState role)
        {
            var session = new SessionManager(); var dev = session.Seed(role, Peers);
            var old = Attach(session); var failure = new InvalidOperationException("old transport disconnect failure");
            old.BeforeDeliver = _ => throw failure;
            var escaped = Record.Exception(() => session.Shutdown("old session"));
            Observe(session, old, escaped); Assert.Same(failure, escaped);
            AssertCleanup(session, old, dev);
            Assert.Single(old.Attempts); Assert.Empty(old.Delivered);
            var freshPeer = new PeerId(404);
            var freshDev = session.Seed(role, freshPeer); var fresh = Attach(session);
            session.SendFixture(freshPeer, new TrainState { Sequence = 1 });
            session.SendFixture(freshPeer, new PingMessage { Nonce = 37 });
            Assert.Equal(2, fresh.Delivered.Count); Assert.Equal(2, fresh.Attempts.Count);
            Assert.Equal((uint)1, Assert.IsType<TrainState>(fresh.Delivered[0].Message).Sequence);
            Assert.Equal((uint)37, Assert.IsType<PingMessage>(fresh.Delivered[1].Message).Nonce);
            Assert.All(fresh.Delivered, a => { Assert.Equal(freshPeer, a.Peer); Assert.Equal(Channel.ReliableOrdered, a.Channel); });
            Assert.Single(old.Attempts); Assert.Equal(1, old.Disposals);
            session.Shutdown("fresh session");
            AssertCleanup(session, fresh, freshDev, 2);
            Assert.Equal("fresh session", Assert.IsType<DisconnectMessage>(fresh.Delivered[2].Message).Reason);
            Assert.Equal(3, fresh.Attempts.Count); Assert.Equal(3, fresh.Delivered.Count);
            Assert.Single(old.Attempts); Assert.Empty(old.Delivered);
            _output.WriteLine("PASS: same manager, fresh transport and recipient; real Train/Ping codec sends; no stale disconnect retry; fresh shutdown clean.");
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void EmptyPeerShutdownCleansRuntimeWithOrWithoutTransport(bool attached)
        {
            var session = new SessionManager(); var dev = session.Seed(SessionState.Connecting);
            var transport = attached ? Attach(session) : null;
            Assert.Null(Record.Exception(() => session.Shutdown("empty session")));
            session.AssertRuntimeCleared(); Assert.Equal(1, dev.Disposals);
            Assert.Equal(1, Sync.PaneScrapeSync.Instance.Resets); Assert.Equal(1, NetTrafficMeter.Instance.Resets);
            Assert.Equal(1, Sync.PlayerSyncManager.Instance.ClothingResets);
            if (transport != null) { Assert.Equal(1, transport.Disposals); Assert.Empty(transport.Attempts); }
        }
    }
}
