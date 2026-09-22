using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class VehicleWheelHealthReplica
    {
        private VehicleWheelHealthState? _state;
        public bool Receive(VehicleWheelHealthState state)
        {
            if (state == null || !state.Valid) return false;
            if (_state != null)
            {
                if (_state.VehicleId != state.VehicleId) return false;
                if (_state.Revision == state.Revision) return _state.SameState(state);
                if (unchecked((int)(state.Revision - _state.Revision)) <= 0) return false;
            }
            _state = state.Copy(); return true;
        }
        public VehicleWheelHealthState? Get() => _state?.Copy();
    }

    public sealed class VehicleWheelHealthPublication
    {
        private VehicleWheelHealthState? _state;
        private uint _sent;
        private bool _hasSent;
        public bool NeedsBroadcast => _state != null && (!_hasSent || _sent != _state.Revision);
        public VehicleWheelHealthState Observe(VehicleWheelHealthState value, byte changedTargets = 0)
        {
            if (value == null || !value.Valid || _state != null && _state.VehicleId != value.VehicleId)
                throw new ArgumentException("Invalid wheel health publication.");
            var next = value.Copy();
            for (int i = 0; i < 4; i++)
            {
                uint epoch = _state?.Epoch(i) ?? 0;
                // Ordinary wear must not invalidate an in-flight puncture. Repair,
                // replacement, withdrawal and terminal damage retire that tyre life.
                if (_state == null || (changedTargets & (1 << i)) != 0 || next.HasWheel(i) != _state.HasWheel(i)
                    || next.Health(i) > _state.Health(i) || next.Health(i) <= 0 && _state.Health(i) > 0)
                { epoch = unchecked(epoch + 1); if (epoch == 0) epoch = 1; }
                next.SetEpoch(i, epoch);
            }
            next.Revision = _state == null ? 1 : _state.SameState(next) ? _state.Revision : unchecked(_state.Revision + 1);
            _state = next; return next.Copy();
        }
        public void MarkBroadcast(uint revision)
        {
            if (_state == null || _state.Revision != revision) return;
            _sent = revision; _hasSent = true;
        }
    }
}
