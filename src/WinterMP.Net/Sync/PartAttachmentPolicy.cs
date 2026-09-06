using System;
using System.Text;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class PartAttachmentPolicy
    {
        public const int MaxPathBytes = 512;

        public static bool ValidPath(string path)
        {
            if (path == null || path.Length > MaxPathBytes || Encoding.UTF8.GetByteCount(path) > MaxPathBytes
                || path.IndexOfAny(new[] { ':', '\\' }) >= 0) return false;
            foreach (char c in path) if (char.IsControl(c)) return false;
            if (path.Length == 0) return true;
            foreach (string segment in path.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == "..") return false;
            return true;
        }

        public static bool Valid(ReplacementPartState state, uint id)
        {
            if (!ValidPath(state.ParentPath) || !Finite(state.LocalPosition) || !Rotation(state.LocalRotation)
                || !Finite(state.LocalScale) || state.LocalScale.X <= 0 || state.LocalScale.Y <= 0 || state.LocalScale.Z <= 0) return false;
            if (state.ParentKind == PartParentKind.None)
                return state.ParentId == 0 && state.ParentPath.Length == 0
                    && Equal(state.LocalPosition, new NetVector3()) && Equal(state.LocalRotation, NetQuaternion.Identity)
                    && Equal(state.LocalScale, new NetVector3(1, 1, 1));
            return state.Installed && state.AssemblyId > 0
                && (state.ParentKind == PartParentKind.Vehicle || (state.ParentKind == PartParentKind.NativePart && state.ParentId != id));
        }

        public static bool HasAttachment(ReplacementPartState state) =>
            state.Installed && state.AssemblyId > 0 && state.ParentKind != PartParentKind.None;

        public static bool Same(ReplacementPartState a, ReplacementPartState b) =>
            a.ParentKind == b.ParentKind && a.ParentId == b.ParentId && a.ParentPath == b.ParentPath
            && Equal(a.LocalPosition, b.LocalPosition) && Equal(a.LocalRotation, b.LocalRotation) && Equal(a.LocalScale, b.LocalScale);

        private static bool Equal(NetVector3 a, NetVector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        private static bool Equal(NetQuaternion a, NetQuaternion b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(NetVector3 v) => Finite(v.X) && Finite(v.Y) && Finite(v.Z);
        private static bool Rotation(NetQuaternion q)
        {
            float norm = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            return Finite(norm) && norm >= .9f && norm <= 1.1f;
        }
    }
}
