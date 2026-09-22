using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;
using Xunit;

// Dependency-only doubles. Lifecycle methods and their fields are extracted
// without rewriting from SessionManager.cs; no cleanup implementation lives here.
namespace S09.ShutdownFixture
{
    internal sealed class RemotePlayer { internal byte PlayerId; }
    internal sealed class DevLoopbackClient : IDisposable
    {
        internal int Disposals;
        internal Action OnDispose = () => { };
        public void Dispose() { Disposals++; OnDispose(); }
    }
    internal sealed class NetTrafficMeter
    {
        internal static readonly NetTrafficMeter Instance = new NetTrafficMeter();
        internal int Resets, Sent;
        internal void RecordSent(Channel channel, int bytes) { Sent++; }
        internal void Reset() { Resets++; Sent = 0; }
    }
    internal sealed partial class SessionManager
    {
        internal PeerId[] PeerOrder => _playersByPeer.Keys.ToArray();
        internal bool HasTransport => _transport != null;
        internal bool HasDevClient => _devClient != null;
        internal bool CleanupPending => _failedSessionCleanupPending;
        internal DevLoopbackClient Seed(SessionState state, params PeerId[] peers)
        {
            State = state; IsHost = state == SessionState.Hosting; LocalPlayerId = 7;
            StatusText = "seeded session"; PermanentDeathEnabled = true;
            LocalClothingAdmission = 123; _clothingSequence = 456;
            foreach (var peer in peers) _playersByPeer.Add(peer, new RemotePlayer { PlayerId = 1 });
            _hostPeer = new PeerId(900);
            _pendingPings.Add(7, 1);
            _nextSnapshotRequestAt.Add(_hostPeer.Value, 20);
            _nextResyncRequestAt.Add(_hostPeer.Value, 20);
            _nextObjectStateRequestAt.Add(_hostPeer.Value, 20);
            _joinAttempt.Begin(Time.unscaledTime);
            _joinAttempt.TransportConnected(Time.unscaledTime);
            _passengerSeats.Apply(new PassengerState { PlayerId = 1, VehicleId = 42, SeatIndex = 0, Sequence = 99 }, (_, _) => true);
            _deathSession.TryWipe(true);
            _guestSlotsBySteam.Add(1, new GuestSlot { PlayerId = 1, Disconnected = true });
            _nextPlayerId = 9; _nextPingAt = 99; _pingNonce = 88; _steamOpStartedAt = 10;
            _steamLobbyAttemptActive = true; _bypassHostPlayerGate = true; _joinBrowseActive = true;
            _failedAt = 10; _failedSessionCleanupPending = true;
            ConnectionQuality.Instance.TransportName = "failed transport";
            ConnectionQuality.Instance.SetRelay(true);
            ConnectionQuality.Instance.NoteUnreliableDropped();
            for (int i = 0; i < 5; i++) ConnectionQuality.Instance.NoteSendFailure();
            NetTrafficMeter.Instance.RecordSent(Channel.ReliableOrdered, 10);
            return _devClient = new DevLoopbackClient();
        }
        internal void AttachFixture(ITransport transport) => AttachTransport(transport);
        internal void SendFixture(PeerId peer, IMessage message) => SendTo(peer, message, Channel.ReliableOrdered);
        internal void FailFixture(string status, LaunchMode pending = LaunchMode.None)
        {
            _pendingMode = pending;
            FailSession(status);
        }
        internal void FlushFixture() => FlushFailedSessionCleanup();
        internal void AssertPendingMode(LaunchMode mode) => Assert.Equal(mode, _pendingMode);
        // Production transport attachment is exercised; ingress/handshake is not this fixture's scope.
        private void OnPeerConnected(PeerId peer) { }
        private void OnPeerDisconnected(PeerId peer, string reason) { }
        private void OnPacketReceived(PeerId peer, byte[] packet, Channel channel) { }

        internal void AssertRuntimeCleared(SessionState expectedState = SessionState.Idle,
            string expectedStatus = "Idle", bool expectedBrowse = false, float expectedFailedAt = -1f)
        {
            Assert.Null(_transport); Assert.Null(_devClient); Assert.Null(_hostPeer);
            Assert.Empty(_playersByPeer); Assert.Empty(_pendingPings);
            Assert.Empty(_nextSnapshotRequestAt); Assert.Empty(_nextResyncRequestAt); Assert.Empty(_nextObjectStateRequestAt);
            Assert.Equal(JoinAttemptStage.None, _joinAttempt.Stage); Assert.False(_joinAttempt.HasTimedOut(double.MaxValue));
            Assert.Empty(_passengerSeats.Occupants); Assert.False(_deathSession.Wiped); Assert.Empty(_guestSlotsBySteam);
            Assert.Equal((byte)1, _nextPlayerId); Assert.Equal((byte)0, LocalPlayerId);
            Assert.Equal(0f, _nextPingAt); Assert.Equal(0u, _pingNonce); Assert.Equal(-1f, _steamOpStartedAt);
            Assert.False(_steamLobbyAttemptActive); Assert.False(_bypassHostPlayerGate); Assert.Equal(expectedBrowse, _joinBrowseActive);
            Assert.False(_failedSessionCleanupPending); Assert.False(IsHost); Assert.False(PermanentDeathEnabled);
            Assert.Equal(0ul, LocalClothingAdmission); Assert.Equal(0u, _clothingSequence);
            Assert.Equal(expectedState, State); Assert.Equal(expectedStatus, StatusText); Assert.Equal(expectedFailedAt, _failedAt);
            Assert.Equal("?", ConnectionQuality.Instance.TransportName); Assert.Null(ConnectionQuality.Instance.UsingRelay);
            Assert.Equal(0, ConnectionQuality.Instance.LossPercent); Assert.Equal(0, ConnectionQuality.Instance.SendFailures);
            Assert.False(ConnectionQuality.Instance.ShouldPauseOwnershipTransfers); Assert.Equal(0, NetTrafficMeter.Instance.Sent);
        }
        internal void AssertSeatSequenceReset()
        {
            var result = _passengerSeats.Apply(new PassengerState { PlayerId = 1, VehicleId = 42, SeatIndex = 0, Sequence = 1 }, (_, _) => true);
            Assert.NotNull(result); Assert.True(result!.Accepted);
        }
    }
    internal sealed class ShutdownTransport : ITransport
    {
        internal sealed class Attempt
        {
            internal PeerId Peer;
            internal Channel Channel;
            internal IMessage Message = null!;
            internal byte[] Payload = null!;
        }
        public bool IsHost { get; set; }
        public event Action<PeerId>? PeerConnected { add { Subscriptions++; } remove { Subscriptions--; } }
        public event Action<PeerId, string>? PeerDisconnected { add { Subscriptions++; } remove { Subscriptions--; } }
        public event Action<PeerId, byte[], Channel>? PacketReceived { add { Subscriptions++; } remove { Subscriptions--; } }
        internal readonly List<Attempt> Attempts = new List<Attempt>(), Delivered = new List<Attempt>();
        internal Action<Attempt> BeforeDeliver = _ => { }, AfterDeliver = _ => { };
        internal Action OnDispose = () => { };
        internal int Disposals, Subscriptions;
        public void Send(PeerId peer, byte[] payload, Channel channel)
        {
            if (Disposals != 0) throw new ObjectDisposedException(nameof(ShutdownTransport));
            var attempt = new Attempt { Peer = peer, Channel = channel, Payload = payload, Message = PacketCodec.Decode(payload) };
            Attempts.Add(attempt); BeforeDeliver(attempt); Delivered.Add(attempt); AfterDeliver(attempt);
        }
        public void Send(PeerId peer, byte[] payload, int length, Channel channel)
        {
            var copy = new byte[length]; Array.Copy(payload, copy, length); Send(peer, copy, channel);
        }
        public void Update() { }
        public void Dispose() { Disposals++; OnDispose(); }
    }
}
namespace S09.ShutdownFixture.Sync
{
    internal sealed class PaneScrapeSync
    {
        internal static readonly PaneScrapeSync Instance = new PaneScrapeSync();
        internal int Resets;
        internal Action OnReset = () => { };
        internal void ResetSession() { Resets++; OnReset(); }
    }
    internal sealed class PlayerSyncManager
    {
        internal static readonly PlayerSyncManager Instance = new PlayerSyncManager();
        internal int Resets, ClothingResets;
        internal void ResetGuestSpawn() { Resets++; }
        internal void ResetClothingSession() { ClothingResets++; }
    }
    internal sealed class DeathSyncManager
    {
        internal static readonly DeathSyncManager Instance = new DeathSyncManager();
        internal int Resets;
        internal void ResetSession() { Resets++; }
    }
}
