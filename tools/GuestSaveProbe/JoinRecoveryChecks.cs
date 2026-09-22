using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;

namespace WinterMP.GuestSaveProbe
{
    internal static class JoinRecoveryChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static object Get(SessionManager session, string name) => typeof(SessionManager).GetField(name, Members).GetValue(session);
        private static void Set(SessionManager session, string name, object value) => typeof(SessionManager).GetField(name, Members).SetValue(session, value);
        private static void Call(SessionManager session, string name, params object[] args) => typeof(SessionManager).GetMethod(name, Members).Invoke(session, args);
        private static void State(SessionManager session, SessionState state) => Call(session, "SetState", state, "join recovery probe");
        private static JoinAttempt Attempt(SessionManager session) => (JoinAttempt)Get(session, "_joinAttempt");
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static bool Protected => (bool)typeof(SessionManager).Assembly.GetType("WinterMP.Core.Session.GuestSaveGuard", true)
            .GetProperty("ProtectWorld", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null);

        internal static IEnumerator Run(Action<string, Action> check)
        {
            if (!File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-join-sandbox.txt")))
                throw new InvalidOperationException("Join recovery probe requires a marked isolated game copy.");
            var session = SessionManager.Instance!;
            Require(session.State == SessionState.Idle && Protected, "Run the guest-save checks before join recovery.");
            LoopbackPair? pair = null;
            UdpTransport? host = null;
            try
            {
                State(session, SessionState.Connecting); Set(session, "_joinBrowseActive", true);
                pair = LoopbackTransport.CreatePair(); Call(session, "AttachTransport", pair.Client); pair.Client.Update();
                check("join: transport connection starts the handshake deadline", () =>
                    Require(Attempt(session).Stage == JoinAttemptStage.AwaitingHandshake && !session.ShowJoinBrowseUI,
                        "Transport connection was mistaken for admission."));
                Attempt(session).Begin(0); Attempt(session).TransportConnected(1);
                Call(session, "CheckJoinTimeout", 61d);
                string reason = session.StatusText;
                check("join: silent host fails before transport disposal and hides retry until cleanup", () =>
                    Require(session.State == SessionState.Failed && Get(session, "_transport") != null
                        && !session.ShowJoinBrowseUI && reason.Contains("host did not finish"), "Missing handshake did not fail safely."));
                pair.Host.Send(pair.Client.LocalPeerId, PacketCodec.Encode(new HandshakeResponse {
                    Accepted = true, PlayerId = 1, HostPlayerName = "Late host" }), Channel.ReliableOrdered);
                pair.Client.Update();
                check("join: reply after failure cannot resurrect the session", () =>
                    Require(session.State == SessionState.Failed && session.StatusText == reason && session.PlayerCount == 0,
                        "Late host reply revived a failed attempt."));
                int closed = 0; pair.Host.PeerDisconnected += (_, __) => closed++;
                Call(session, "FlushFailedSessionCleanup"); pair.Host.Update();
                check("join: cleanup closes transport and restores the friend picker with its error", () =>
                    Require(closed == 1 && Get(session, "_transport") == null && session.ShowJoinBrowseUI
                        && session.StatusText == reason && Attempt(session).Stage == JoinAttemptStage.None && Protected,
                        "Cleanup lost failure context, save protection or retry access."));
                pair.Host.Dispose(); pair = null;

                State(session, SessionState.Idle); State(session, SessionState.Connecting);
                Attempt(session).Begin(0); Call(session, "CheckJoinTimeout", 60d);
                check("join: a missing transport also times out", () =>
                    Require(session.State == SessionState.Failed && session.StatusText.Contains("Could not reach"), "Lobby wait remained unbounded."));
                Call(session, "FlushFailedSessionCleanup");

                State(session, SessionState.Idle); State(session, SessionState.Connecting);
                pair = LoopbackTransport.CreatePair(); Call(session, "AttachTransport", pair.Client); pair.Client.Update();
                Attempt(session).Begin(-200); Attempt(session).TransportConnected(-100);
                pair.Host.Send(pair.Client.LocalPeerId, PacketCodec.Encode(new HandshakeResponse {
                    Accepted = true, PlayerId = 2, HostPlayerName = "Retry host" }), Channel.ReliableOrdered);
                Call(session, "Update");
                check("join: ready reply is processed before deadline sweep after a slow frame", () =>
                    Require(session.State == SessionState.Connected && session.LocalPlayerId == 2 && session.PlayerCount == 1
                        && Attempt(session).Stage == JoinAttemptStage.None && !session.ShowJoinBrowseUI && Protected,
                        "Retry failed or timeout ran ahead of an already queued acceptance."));
                Call(session, "CheckJoinTimeout", double.MaxValue);
                check("join: admitted session is not subject to the join deadline", () =>
                    Require(session.State == SessionState.Connected, "Join deadline disconnected an admitted player."));
                session.Shutdown("Join probe retry complete"); pair.Host.Dispose(); pair = null;

                State(session, SessionState.Connecting); pair = LoopbackTransport.CreatePair();
                Call(session, "AttachTransport", pair.Client); pair.Client.Update();
                pair.Host.Send(pair.Client.LocalPeerId, PacketCodec.Encode(new HandshakeResponse {
                    Accepted = false, Reason = "Probe catalog mismatch" }), Channel.ReliableOrdered);
                Call(session, "Update"); Call(session, "CheckJoinTimeout", double.MaxValue);
                check("join: explicit refusal survives cleanup and deadline checks", () =>
                    Require(session.State == SessionState.Failed && session.StatusText.Contains("Probe catalog mismatch")
                        && Get(session, "_transport") == null && !session.ShowJoinBrowseUI, "Refusal was replaced or an unrequested picker opened."));
                session.Shutdown("Join probe refusal complete"); pair.Host.Dispose(); pair = null;
                State(session, SessionState.Hosting); Call(session, "CheckJoinTimeout", double.MaxValue);
                check("join: waiting hosts have no guest join deadline", () =>
                    Require(session.State == SessionState.Hosting, "Empty host lobby timed out."));
                session.Shutdown("Join probe fixtures complete");

                // Real UDP keepalives continue while this host deliberately withholds
                // its mod handshake. Exercise the unmodified Unity Update clock.
                const int port = 38948;
                host = UdpTransport.CreateHost(port);
                int handshakes = 0;
                bool accept = false;
                host.PacketReceived += (peer, payload, channel) =>
                {
                    if (PacketCodec.Decode(payload) is not HandshakeRequest) return;
                    handshakes++;
                    if (accept) host.Send(peer, PacketCodec.Encode(new HandshakeResponse {
                        Accepted = true, PlayerId = 3, HostPlayerName = "UDP retry host" }), Channel.ReliableOrdered);
                };
                session.StartJoinLocal(IPAddress.Loopback.ToString(), port);
                float began = Time.realtimeSinceStartup;
                while (session.State == SessionState.Connecting && Time.realtimeSinceStartup - began < 70)
                { host.Update(); yield return null; }
                float elapsed = Time.realtimeSinceStartup - began;
                check("join: live UDP keepalives cannot hide a missing handshake", () =>
                    Require(handshakes == 1 && elapsed >= 59 && elapsed < 70 && session.State == SessionState.Failed
                        && session.StatusText.Contains("host did not finish") && Get(session, "_transport") == null,
                        "Silent UDP host result: requests=" + handshakes + ", elapsed=" + elapsed + ", status=" + session.StatusText));
                session.Shutdown("Retry timed-out UDP join"); accept = true;
                session.StartJoinLocal(IPAddress.Loopback.ToString(), port); began = Time.realtimeSinceStartup;
                while (session.State == SessionState.Connecting && Time.realtimeSinceStartup - began < 10)
                { host.Update(); yield return null; }
                check("join: a real UDP retry succeeds with save protection intact", () =>
                    Require(handshakes == 2 && session.State == SessionState.Connected && session.LocalPlayerId == 3 && Protected,
                        "Fresh UDP join failed after timeout: " + session.StatusText));
            }
            finally
            {
                session.Shutdown("Join recovery probe complete");
                pair?.Host.Dispose(); host?.Dispose();
            }
        }
    }
}
