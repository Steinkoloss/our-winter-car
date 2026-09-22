using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class StarterDrawPolicy
    {
        // Bound work from a peer without tying legitimate native callback counts
        // to the host's frame rate. Excess requests are consumed, never deferred.
        public const float CountsPerSecond = 2048;
        private sealed class Sender
        {
            internal ushort Sequence;
            internal float At, Remaining = StarterDrawRequest.MaximumCount;
        }
        private readonly Dictionary<uint, Dictionary<byte, Sender>> _senders = new Dictionary<uint, Dictionary<byte, Sender>>();
        public bool Receive(StarterDrawRequest request, byte authenticatedPlayer, byte owner, bool localDriver, float now)
        {
            if (request == null || !request.Valid || request.PlayerId != authenticatedPlayer || owner != authenticatedPlayer
                || localDriver || float.IsNaN(now) || float.IsInfinity(now) || now < 0) return false;
            if (!_senders.TryGetValue(request.VehicleId, out var players))
            { players = new Dictionary<byte, Sender>(); _senders.Add(request.VehicleId, players); }
            if (players.TryGetValue(request.PlayerId, out var sender))
            {
                ushort delta = unchecked((ushort)(request.Sequence - sender.Sequence));
                if (delta == 0 || delta > short.MaxValue) return false;
                float elapsed = now > sender.At ? now - sender.At : 0;
                sender.Remaining = System.Math.Min(StarterDrawRequest.MaximumCount, sender.Remaining + elapsed * CountsPerSecond);
            }
            else { sender = new Sender(); players.Add(request.PlayerId, sender); }
            sender.Sequence = request.Sequence; sender.At = System.Math.Max(now, sender.At);
            if (sender.Remaining < request.Count) return false;
            sender.Remaining -= request.Count;
            return true;
        }
        public void ForgetPlayer(byte playerId) { foreach (var players in _senders.Values) players.Remove(playerId); }
        public void Clear() => _senders.Clear();
    }
}
