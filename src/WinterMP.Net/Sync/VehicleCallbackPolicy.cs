using System;
using System.Collections.Generic;

namespace WinterMP.Net.Sync
{
    /// <summary>Vehicle-wide callback budget with connection-specific serial ordering.</summary>
    public sealed class VehicleCallbackPolicy
    {
        private readonly float _maximumBurst, _stepsPerSecond;
        public VehicleCallbackPolicy(float maximumBurst, float stepsPerSecond)
        {
            if (!(maximumBurst >= 1) || float.IsInfinity(maximumBurst)
                || !(stepsPerSecond > 0) || float.IsInfinity(stepsPerSecond)) throw new ArgumentOutOfRangeException();
            _maximumBurst = maximumBurst; _stepsPerSecond = stepsPerSecond;
        }
        private sealed class Sender { internal ushort Sequence; }
        private sealed class Vehicle
        {
            internal readonly Dictionary<byte, Sender> Senders = new Dictionary<byte, Sender>();
            internal float At, Remaining;
        }
        private readonly Dictionary<uint, Vehicle> _vehicles = new Dictionary<uint, Vehicle>();
        public bool Receive(uint vehicleId, byte playerId, ushort sequence, byte authenticatedPlayer, byte owner, bool localDriver, float now)
        {
            if (vehicleId == 0 || playerId == 0 || playerId == byte.MaxValue || playerId != authenticatedPlayer || owner != authenticatedPlayer
                || localDriver || float.IsNaN(now) || float.IsInfinity(now) || now < 0) return false;
            if (!_vehicles.TryGetValue(vehicleId, out var vehicle))
            { vehicle = new Vehicle { At = now, Remaining = _maximumBurst }; _vehicles.Add(vehicleId, vehicle); }
            if (vehicle.Senders.TryGetValue(playerId, out var sender))
            {
                ushort delta = unchecked((ushort)(sequence - sender.Sequence));
                if (delta == 0 || delta > short.MaxValue) return false;
            }
            else { sender = new Sender(); vehicle.Senders.Add(playerId, sender); }
            // Consume ordering before native validation; a rejected event cannot
            // become a deferred saved write after a repair. Handoffs share the budget.
            sender.Sequence = sequence;
            vehicle.Remaining = Math.Min(_maximumBurst, vehicle.Remaining + Math.Max(0, now - vehicle.At) * _stepsPerSecond);
            vehicle.At = Math.Max(now, vehicle.At);
            if (vehicle.Remaining < 1) return false;
            vehicle.Remaining -= 1;
            return true;
        }
        public void ForgetPlayer(byte playerId)
        { foreach (var vehicle in _vehicles.Values) vehicle.Senders.Remove(playerId); }
        public void Clear() => _vehicles.Clear();
    }
}
