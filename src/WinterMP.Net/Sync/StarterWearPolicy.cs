using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class StarterWearPolicy
    {
        private sealed class Sender
        {
            internal ushort Sequence;
            internal float At, Remaining = StarterWearRequest.MaximumSeconds;
        }
        private readonly Dictionary<uint, Dictionary<byte, Sender>> _senders = new Dictionary<uint, Dictionary<byte, Sender>>();
        public bool Receive(StarterWearRequest request, byte authenticatedPlayer, byte owner, bool localDriver, float now)
        {
            if (request == null || !request.Valid || request.PlayerId != authenticatedPlayer || owner != authenticatedPlayer
                || localDriver || float.IsNaN(now) || float.IsInfinity(now) || now < 0) return false;
            if (!_senders.TryGetValue(request.VehicleId, out var players))
            { players = new Dictionary<byte, Sender>(); _senders.Add(request.VehicleId, players); }
            if (players.TryGetValue(request.PlayerId, out var sender))
            {
                ushort delta = unchecked((ushort)(request.Sequence - sender.Sequence));
                if (delta == 0 || delta > short.MaxValue) return false;
                sender.Remaining = Math.Min(StarterWearRequest.MaximumSeconds, sender.Remaining + Math.Max(0, now - sender.At));
            }
            else { sender = new Sender(); players.Add(request.PlayerId, sender); }
            // Consume excess time, including its sequence, so repair or a later
            // frame cannot turn rejected work into deferred wear.
            sender.Sequence = request.Sequence; sender.At = Math.Max(now, sender.At);
            if (sender.Remaining < request.Seconds) return false;
            sender.Remaining -= request.Seconds;
            return true;
        }
        public void ForgetPlayer(byte playerId) { foreach (var players in _senders.Values) players.Remove(playerId); }
        public void Clear() => _senders.Clear();
    }
}
