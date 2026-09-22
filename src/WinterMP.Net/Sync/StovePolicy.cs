using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class StoveIntentLedger
    {
        private readonly Dictionary<byte, ushort> _sequences = new Dictionary<byte, ushort>();
        public bool Accept(StoveKnobIntent request, bool nearby)
        {
            if (!StovePolicy.Valid(request)) return false;
            if (_sequences.TryGetValue(request.PlayerId, out ushort old)
                && (ushort)(request.Sequence - old) >= 0x8000) return false;
            if (_sequences.TryGetValue(request.PlayerId, out old) && old == request.Sequence) return false;
            _sequences[request.PlayerId] = request.Sequence;
            return nearby;
        }
        public void Forget(byte player) { _sequences.Remove(player); }
    }

    public static class StovePolicy
    {
        public static bool Valid(StoveKnobIntent s) => s != null && s.ApplianceId != 0 && s.PlayerId > 0
            && s.PlayerId < 255 && s.Plate < 4 && s.Direction < 2;
        public static bool Valid(ApplianceState s)
        {
            if (s.StoveRevision != 0 && (s.ApplianceId == 0 || s.Kind != 0 || (s.Flags & ~15) != 0 || s.FirePlate > 4)) return false;
            if (s.StoveHeat == null || s.StoveHeat.Length != 4 || s.StoveRotation == null || s.StoveRotation.Length != 4
                || s.GrillMask > 15 || s.BurnMask > 15 || (s.GrillMask & s.BurnMask) != 0) return false;
            for (int i = 0; i < 4; i++)
                if (!Finite(s.StoveHeat[i]) || s.StoveHeat[i] < 0 || s.StoveHeat[i] > 750
                    || !Finite(s.StoveRotation[i]) || s.StoveRotation[i] < -360 || s.StoveRotation[i] > 330) return false;
            return true;
        }
        public static bool Same(ApplianceState a, ApplianceState b)
        {
            if (a.ApplianceId != b.ApplianceId || a.Flags != b.Flags || a.FireCount != b.FireCount || a.FirePlate != b.FirePlate
                || a.GrillMask != b.GrillMask || a.BurnMask != b.BurnMask) return false;
            for (int i = 0; i < 4; i++) if (a.StoveHeat[i] != b.StoveHeat[i] || a.StoveRotation[i] != b.StoveRotation[i]) return false;
            return true;
        }
        public static bool CanReceive(ApplianceState? old, ApplianceState next)
        {
            if (!Valid(next) || next.StoveRevision == 0) return false;
            if (old == null) return true;
            uint delta = unchecked(next.StoveRevision - old.StoveRevision);
            return old.ApplianceId == next.ApplianceId && (delta == 0 ? Same(old, next) : delta < 0x80000000u);
        }
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
