using System;
using System.Collections.Generic;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace PaneScrapeBridge.Tests
{
    // Portable transport boundary only. Captures production SendWorldMessage calls;
    // it neither constructs intents nor supplies authority/lease/result values.
    internal sealed class GuestStrokeWire : IDisposable
    {
        internal sealed class Packet
        {
            public readonly byte[] Original, Transmitted;
            public Packet(byte[] original, byte[] transmitted) { Original = original; Transmitted = transmitted; }
            public ScraperAction Request => (ScraperAction)PacketCodec.Decode(Transmitted);
        }

        private readonly SessionManager _guest;
        private readonly ProbeBoundary _probe;
        private bool _disposed;
        public bool Armed => _probe.Armed;
        public int Mutations { get; private set; }
        public readonly List<Packet> Packets = new List<Packet>();
        public string[] ProbeSnapshot() => _probe.Snapshot();

        public GuestStrokeWire(SessionManager guest)
        {
            if (guest.IsHost) throw new ArgumentException("Guest send boundary required.", nameof(guest));
            _guest = guest;
            _probe = new ProbeBoundary(guest);
            _guest.Sending += Capture;
        }

        public void ArmWrongPane()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GuestStrokeWire));
            _probe.Arm();
        }

        private void Capture(IMessage message)
        {
            try
            {
                byte[] original = PacketCodec.Encode(message);
                IMessage transmitted = _probe.Observe(message);
                if (!ReferenceEquals(message, transmitted)) Mutations++;
                Packets.Add(new Packet(original, PacketCodec.Encode(transmitted)));
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _guest.Sending -= Capture;
            _probe.Dispose();
        }
    }
}
