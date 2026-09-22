using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class HeaterReplica
    {
        private HeaterState? _state;
        public bool Receive(HeaterState state)
        {
            if (state == null || !state.Valid) return false;
            if (_state != null)
            {
                if (_state.Revision == state.Revision) return _state.Flags == state.Flags && _state.Wear == state.Wear
                    && _state.RearWindowFlags == state.RearWindowFlags;
                if (unchecked((int)(state.Revision - _state.Revision)) <= 0) return false;
            }
            _state = state.Copy(); return true;
        }
        public HeaterState? Get() => _state?.Copy();
        public void Clear() => _state = null;
    }

    public sealed class HeaterPublication
    {
        private HeaterState? _state;
        private uint _sent;
        private bool _hasSent;
        public bool NeedsBroadcast => _state != null && (!_hasSent || _sent != _state.Revision);
        public HeaterState Observe(byte flags, float wear, byte rearWindowFlags = 0)
        {
            var next = new HeaterState { Flags = flags, Wear = wear, RearWindowFlags = rearWindowFlags };
            if (!next.Valid) throw new ArgumentException("Invalid heater publication.");
            next.Revision = _state == null ? 1 : _state.Flags == flags && _state.Wear == wear && _state.RearWindowFlags == rearWindowFlags
                ? _state.Revision : unchecked(_state.Revision + 1);
            _state = next; return next.Copy();
        }
        public void MarkBroadcast(uint revision) { if (_state == null || _state.Revision != revision) return; _sent = revision; _hasSent = true; }
    }
}
