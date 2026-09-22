using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class VehicleDrivetrainWearReplica
    {
        private VehicleDrivetrainWearState? _state;
        public bool Receive(VehicleDrivetrainWearState state)
        {
            if (state == null || !state.Valid) return false;
            if (_state != null)
            {
                if (_state.VehicleId != state.VehicleId) return false;
                if (_state.Revision == state.Revision) return _state.SameWear(state);
                if (unchecked((int)(state.Revision - _state.Revision)) <= 0) return false;
            }
            _state = state.Copy(); return true;
        }
        public VehicleDrivetrainWearState? Get() => _state?.Copy();
    }

    public sealed class VehicleDrivetrainWearPublication
    {
        private VehicleDrivetrainWearState? _state;
        private uint _sent;
        private bool _hasSent;
        public bool NeedsBroadcast => _state != null && (!_hasSent || _sent != _state.Revision);
        public VehicleDrivetrainWearState Observe(VehicleDrivetrainWearState value)
        {
            if (value == null || !value.Valid || _state != null && _state.VehicleId != value.VehicleId)
                throw new ArgumentException("Invalid drivetrain wear publication.");
            var next = value.Copy();
            next.Revision = _state == null ? 1 : _state.SameWear(next) ? _state.Revision : unchecked(_state.Revision + 1);
            _state = next; return next.Copy();
        }
        public void MarkBroadcast(uint revision)
        {
            if (_state == null || _state.Revision != revision) return;
            _sent = revision; _hasSent = true;
        }
    }
}
