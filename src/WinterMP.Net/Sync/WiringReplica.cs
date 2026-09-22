using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class WiringPolicy
    {
        public const uint SourceCount = 11;
        public static string Name(uint id)
        {
            switch (id)
            {
                case 1: return "WiringCoilHarness";
                case 2: return "WiringBatteryStarter";
                case 3: return "WiringBatteryHarness";
                case 4: return "WiringBatteryGround";
                case 5: return "WiringIgnitionFusebox";
                case 6: return "WiringAlternatorRegulator";
                case 7: return "WiringRegulatorHarness";
                case 8: return "WiringFueltank";
                case 9: return "WiringHeatercontrolFusebox";
                case 10: return "WiringHeater";
                case 11: return "WiringFuseboxWindow";
                default: return string.Empty;
            }
        }

        public static bool ProjectsNativeReads(uint id) => id >= 9 && id <= 11;
        public static bool SupportsBolted(uint id) => id >= 2 && id <= 4;
        public static bool Valid(WiringState state) => state != null && Name(state.SourceId).Length != 0
            && (state.SourceId == 5 || (state.Flags & WiringState.Connectable) == 0)
            && WiringState.ValidFlags(state.Flags) && (SupportsBolted(state.SourceId) || (state.Flags & WiringState.Bolted) == 0);
        internal static WiringState Copy(WiringState state) => new WiringState { SourceId = state.SourceId, Revision = state.Revision, Flags = state.Flags };
    }

    public sealed class WiringReplica
    {
        private readonly Dictionary<uint, WiringState> _states = new Dictionary<uint, WiringState>();
        public bool Receive(WiringState state)
        {
            if (!WiringPolicy.Valid(state)) return false;
            if (_states.TryGetValue(state.SourceId, out var old))
            {
                if (old.Revision == state.Revision) return old.Flags == state.Flags;
                if (unchecked((int)(state.Revision - old.Revision)) <= 0) return false;
            }
            _states[state.SourceId] = WiringPolicy.Copy(state);
            return true;
        }
        public WiringState? Get(uint id) => _states.TryGetValue(id, out var state) ? WiringPolicy.Copy(state) : null;
        public void Clear() => _states.Clear();
    }

    public sealed class WiringPublication
    {
        private WiringState? _state;
        private uint _broadcastRevision;
        private bool _broadcast;
        public bool NeedsBroadcast => _state != null && (!_broadcast || _state.Revision != _broadcastRevision);
        public WiringState Observe(uint sourceId, byte flags)
        {
            var next = new WiringState { SourceId = sourceId, Flags = flags };
            if (!WiringPolicy.Valid(next) || _state != null && _state.SourceId != sourceId)
                throw new ArgumentException("Invalid wiring publication.");
            next.Revision = _state == null ? 1 : _state.Flags == flags ? _state.Revision : unchecked(_state.Revision + 1);
            _state = next;
            return WiringPolicy.Copy(next);
        }
        public void MarkBroadcast(uint revision)
        {
            if (_state == null || _state.Revision != revision) return;
            _broadcast = true; _broadcastRevision = revision;
        }
    }
}
