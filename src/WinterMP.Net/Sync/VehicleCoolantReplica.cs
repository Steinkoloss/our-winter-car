using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class VehicleCoolantReplica
    {
        private VehicleCoolantState? _state;
        public bool Receive(VehicleCoolantState state)
        {
            if (state == null || !state.Valid) return false;
            if (_state != null)
            {
                if (_state.VehicleId != state.VehicleId) return false;
                if (_state.Revision == state.Revision) return _state.Flags == state.Flags && _state.Celsius == state.Celsius
                    && _state.EngineCelsius == state.EngineCelsius;
                if (unchecked((int)(state.Revision - _state.Revision)) <= 0) return false;
            }
            _state = state.Copy(); return true;
        }
        public VehicleCoolantState? Get() => _state?.Copy();
    }

    public sealed class VehicleCoolantPublication
    {
        private VehicleCoolantState? _state;
        private uint _sent;
        private bool _hasSent;
        public bool NeedsBroadcast => _state != null && (!_hasSent || _sent != _state.Revision);
        public VehicleCoolantState Observe(uint vehicleId, byte flags, float celsius, float engineCelsius = 0)
        {
            var next = new VehicleCoolantState { VehicleId = vehicleId, Flags = flags, Celsius = celsius, EngineCelsius = engineCelsius };
            if (!next.Valid || _state != null && _state.VehicleId != vehicleId)
                throw new ArgumentException("Invalid vehicle coolant publication.");
            next.Revision = _state == null ? 1 : _state.Flags == flags && _state.Celsius == celsius && _state.EngineCelsius == engineCelsius
                ? _state.Revision : unchecked(_state.Revision + 1);
            _state = next; return next.Copy();
        }
        public void MarkBroadcast(uint revision)
        {
            if (_state == null || _state.Revision != revision) return;
            _sent = revision; _hasSent = true;
        }
    }
}
