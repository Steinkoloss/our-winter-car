using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class CylinderHeadPolicy
    {
        public const int BoltCount = 10;
        public static bool Valid(CylinderHeadState s)
        {
            if (s == null || s.NetId == 0 || s.ParentId == s.NetId || !Finite(s.Position.X) || !Finite(s.Position.Y)
                || !Finite(s.Position.Z) || !Finite(s.Mass) || !Finite(s.Tightness) || s.Tightness < 0 || s.Tightness > 10000
                || (!s.FastenersAvailable && s.Tightness != 0)) return false;
            foreach (byte value in s.Fasteners)
                if (value > BoltStatePolicy.MaximumTightness || (!s.FastenersAvailable && value != 0)) return false;
            float norm = s.Rotation.X * s.Rotation.X + s.Rotation.Y * s.Rotation.Y
                + s.Rotation.Z * s.Rotation.Z + s.Rotation.W * s.Rotation.W;
            return Finite(norm) && norm >= .9f && norm <= 1.1f
                && (s.ParentId == 0 ? s.Mass > 0 && s.Mass <= 10000 : s.Mass == 0);
        }
        public static PartFitStatus CheckRequest(PartFitRequest request, CylinderHeadState? state,
            bool available, bool nearby, bool inRange, bool authority, bool idle, float tightness)
        {
            if (!available || state == null || !Valid(state) || request.ItemId != state.NetId || request.SlotIndex != 0
                || (request.Operation != PartFitOperation.Install && request.Operation != PartFitOperation.Remove)) return PartFitStatus.Unavailable;
            if (request.ExpectedRevision != state.Revision) return PartFitStatus.Stale;
            bool install = request.Operation == PartFitOperation.Install;
            if (install ? state.ParentId != 0 : state.ParentId == 0) return install ? PartFitStatus.NotLoose : PartFitStatus.NotFitted;
            if (!nearby || (install && !inRange)) return PartFitStatus.TooFar;
            if (!install && !PartRemovalPolicy.Unbolted(tightness)) return PartFitStatus.Bolted;
            return !idle || (install && !authority) ? PartFitStatus.Busy : PartFitStatus.Pending;
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool SameAttachment(CylinderHeadState a, CylinderHeadState b) =>
            a.NetId == b.NetId && a.ParentId == b.ParentId && a.Mass == b.Mass;
        public static bool SameFasteners(CylinderHeadState a, CylinderHeadState b)
        {
            if (a.FastenersAvailable != b.FastenersAvailable || a.Tightness != b.Tightness) return false;
            for (int i = 0; i < BoltCount; i++) if (a.Fasteners[i] != b.Fasteners[i]) return false;
            return true;
        }
        public static PartFitStatus CheckFastener(PartFitRequest request, CylinderHeadState? state, bool ready, bool nearby, bool busy)
        {
            if (state == null || !Valid(state) || request.ItemId != state.NetId || !state.FastenersAvailable
                || request.SlotIndex < 1 || request.SlotIndex > BoltCount || !PartToolScrewPolicy.IsTurn(request.Operation))
                return PartFitStatus.Unavailable;
            if (request.ExpectedRevision != state.Revision) return PartFitStatus.Stale;
            if (state.ParentId == 0) return PartFitStatus.NotFitted;
            if (!ready) return PartFitStatus.Unavailable;
            if (!nearby) return PartFitStatus.TooFar;
            if (busy) return PartFitStatus.Busy;
            int value = state.Fasteners[request.SlotIndex - 1];
            return (request.Operation == PartFitOperation.ToolTighten ? value >= BoltStatePolicy.MaximumTightness : value == 0)
                ? PartFitStatus.Blocked : PartFitStatus.Pending;
        }
        public static bool CanReceive(CylinderHeadState? previous, CylinderHeadState next)
        {
            if (!Valid(next)) return false;
            if (previous == null) return true;
            if (next.NetId != previous.NetId) return false;
            uint distance = unchecked(next.Revision - previous.Revision);
            return distance == 0 ? SameAttachment(previous, next) && SameFasteners(previous, next) : distance < 0x80000000u;
        }
        public static CylinderHeadState Copy(CylinderHeadState s)
        {
            var copy = new CylinderHeadState { NetId = s.NetId, Revision = s.Revision, ParentId = s.ParentId,
                Position = s.Position, Rotation = s.Rotation, Mass = s.Mass, FastenersAvailable = s.FastenersAvailable, Tightness = s.Tightness };
            System.Array.Copy(s.Fasteners, copy.Fasteners, BoltCount); return copy;
        }
        public static uint MixChecksum(uint crc, CylinderHeadState s)
        {
            crc = StableHash.Combine(StableHash.Combine(crc, s.NetId), s.ParentId);
            crc = StableHash.Combine(crc, s.FastenersAvailable ? 1u : 0u);
            foreach (byte value in s.Fasteners) crc = StableHash.Combine(crc, value);
            return crc;
        }
    }
}
