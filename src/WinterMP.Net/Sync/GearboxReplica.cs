using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class GearboxReplica
    {
        private GearboxState? _state;
        public bool Receive(GearboxState state)
        {
            if (state == null || !state.Valid) return false;
            if (_state != null)
            {
                if (_state.Revision == state.Revision) return _state.Flags == state.Flags && _state.Type == state.Type;
                if (unchecked((int)(state.Revision - _state.Revision)) <= 0) return false;
            }
            _state = state.Copy(); return true;
        }
        public GearboxState? Get() => _state?.Copy();
        public void Clear() => _state = null;
    }

    public sealed class GearboxPublication
    {
        private GearboxState? _state;
        private uint _sent;
        private bool _hasSent;
        public bool NeedsBroadcast => _state != null && (!_hasSent || _sent != _state.Revision);
        public GearboxState Observe(byte flags, int type)
        {
            var next = new GearboxState { Flags = flags, Type = type };
            if (!next.Valid) throw new ArgumentException("Invalid gearbox publication.");
            next.Revision = _state == null ? 1 : _state.Flags == flags && _state.Type == type ? _state.Revision : unchecked(_state.Revision + 1);
            _state = next; return next.Copy();
        }
        public void MarkBroadcast(uint revision) { if (_state == null || _state.Revision != revision) return; _sent = revision; _hasSent = true; }
    }
}
