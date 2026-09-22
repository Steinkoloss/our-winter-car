using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Retains host condition without applying damage to a guest's native engine graph.</summary>
    public sealed class VehicleDamageReplica
    {
        public const byte HostPlayerId = 0;
        private readonly Dictionary<uint, VehicleDamage> _states = new Dictionary<uint, VehicleDamage>();

        public bool Receive(VehicleDamage? state)
        {
            if (state == null || state.VehicleId == 0 || state.OwnerPlayerId != HostPlayerId || !VehicleDamagePolicy.IsValid(state))
                return false;
            if (_states.TryGetValue(state.VehicleId, out var previous))
            {
                ushort difference = unchecked((ushort)(state.Sequence - previous.Sequence));
                if (difference == 0 || difference > short.MaxValue) return false;
            }
            _states[state.VehicleId] = VehicleDamagePolicy.Copy(state);
            return true;
        }

        public VehicleDamage? Get(uint vehicleId) => _states.TryGetValue(vehicleId, out var state) ? VehicleDamagePolicy.Copy(state) : null;

        public void Clear() => _states.Clear();
    }
}
