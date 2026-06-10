using System;
using System.Collections.Generic;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;

namespace WinterMP.Core.Session
{
    /// <summary>
    /// Minimal in-process guest used by the F8 loopback dev session: performs the
    /// handshake and echoes chat, so the full host code path can be exercised in a
    /// single game instance without Steam or a second machine.
    ///
    /// Ghost mode: every player pose the host streams to us is replayed back
    /// <see cref="GhostDelaySeconds"/> later as this guest's own pose. The host
    /// therefore sees a "ghost" avatar retracing their movements — a complete
    /// one-person test of the avatar pipeline (send → receive → interpolate → render).
    /// </summary>
    internal sealed class DevLoopbackClient : IDisposable
    {
        private const float GhostDelaySeconds = 3f;

        private readonly ITransport _transport;
        private readonly string _name;
        private readonly Queue<GhostSample> _ghostTrail = new Queue<GhostSample>();
        private PeerId? _hostPeer;
        private byte _playerId = 255;
        private ushort _ghostSequence;

        private struct GhostSample
        {
            public float DueAt;
            public PlayerTransform Pose;
        }

        public DevLoopbackClient(ITransport clientEnd, string name)
        {
            _transport = clientEnd;
            _name = name;
            _transport.PeerConnected += OnConnected;
            _transport.PacketReceived += OnPacket;
        }

        public void Update()
        {
            _transport.Update();
            PumpGhostTrail();
        }

        private void PumpGhostTrail()
        {
            if (!_hostPeer.HasValue) return;

            while (_ghostTrail.Count > 0 && UnityEngine.Time.unscaledTime >= _ghostTrail.Peek().DueAt)
            {
                var sample = _ghostTrail.Dequeue();
                sample.Pose.PlayerId = _playerId;
                sample.Pose.Sequence = ++_ghostSequence;
                _transport.Send(_hostPeer.Value, PacketCodec.Encode(sample.Pose), Channel.UnreliableSequenced);
            }
        }

        private void OnConnected(PeerId hostPeer)
        {
            _hostPeer = hostPeer;
            var request = new HandshakeRequest
            {
                ProtocolVersion = ProtocolInfo.Version,
                ModVersion = MyPluginInfo.PLUGIN_VERSION,
                GameVersion = Util.SafeApp.GameVersion,
                CatalogHash = 0,
                PlayerName = _name,
            };
            _transport.Send(hostPeer, PacketCodec.Encode(request), Channel.ReliableOrdered);
        }

        private void OnPacket(PeerId from, byte[] payload, Channel channel)
        {
            IMessage message;
            try
            {
                message = PacketCodec.Decode(payload);
            }
            catch (ProtocolException)
            {
                return;
            }

            switch (message)
            {
                case HandshakeResponse response:
                    if (response.Accepted)
                    {
                        _playerId = response.PlayerId;
                        WinterMPPlugin.Log.LogInfo(
                            $"[loopback guest] accepted as player {response.PlayerId} — " +
                            $"ghost mode on, replaying your movements {GhostDelaySeconds:0}s behind you.");
                    }
                    else
                    {
                        WinterMPPlugin.Log.LogInfo($"[loopback guest] refused: {response.Reason}");
                    }
                    break;

                case PlayerTransform pose:
                    // Host's own pose stream → becomes our ghost trail.
                    _ghostTrail.Enqueue(new GhostSample
                    {
                        DueAt = UnityEngine.Time.unscaledTime + GhostDelaySeconds,
                        Pose = pose,
                    });
                    break;

                case ChatMessage chat when chat.Text.StartsWith("!echo ", StringComparison.Ordinal):
                    var reply = new ChatMessage { SenderPlayerId = 255, Text = chat.Text.Substring(6) };
                    _transport.Send(from, PacketCodec.Encode(reply), Channel.ReliableOrdered);
                    break;

                case PingMessage ping:
                    var pong = new PongMessage { Nonce = ping.Nonce, SenderTimeMs = ping.SenderTimeMs };
                    _transport.Send(from, PacketCodec.Encode(pong), Channel.ReliableOrdered);
                    break;
            }
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }
}
