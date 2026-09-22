using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class BatteryReplica
    {
        private BatteryState? _state;
        public bool Receive(BatteryState state)
        {
            if (state == null || !state.Valid) return false;
            if (_state != null)
            {
                if (_state.Revision == state.Revision) return _state.Flags == state.Flags && _state.Charge == state.Charge && _state.ChargeMax == state.ChargeMax;
                if (unchecked((int)(state.Revision - _state.Revision)) <= 0) return false;
            }
            _state = state.Copy(); return true;
        }
        public BatteryState? Get() => _state?.Copy();
        public void Clear() => _state = null;
    }

    public sealed class BatteryPublication
    {
        private BatteryState? _state;
        private uint _sent;
        private bool _hasSent;
        public bool NeedsBroadcast => _state != null && (!_hasSent || _sent != _state.Revision);
        public BatteryState Observe(byte flags, float charge, float chargeMax = 0)
        {
            var next = new BatteryState { Flags = flags, Charge = charge, ChargeMax = chargeMax };
            if (!next.Valid) throw new ArgumentException("Invalid battery publication.");
            next.Revision = _state == null ? 1 : _state.Flags == flags && _state.Charge == charge && _state.ChargeMax == chargeMax ? _state.Revision : unchecked(_state.Revision + 1);
            _state = next; return next.Copy();
        }
        public void MarkBroadcast(uint revision) { if (_state == null || _state.Revision != revision) return; _sent = revision; _hasSent = true; }
    }
}
