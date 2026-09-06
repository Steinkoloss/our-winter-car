using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class VenttiSceneReplica
    {
        public VenttiSceneState? Current { get; private set; }
        public VenttiSceneState? Previous { get; private set; }
        public float ReceivedAt { get; private set; }

        public bool Receive(uint table, uint layout, int count, VenttiSceneState state, float now)
        {
            if (!BankTransferPolicy.IsFinite(now) || now < 0 || state.TableId != table || state.LayoutId != layout
                || state.Poses == null || state.Poses.Length != count || !IsValid(state)) return false;
            if (Current != null && !Forward(Current.Sequence, state.Sequence)) return false;
            Previous = Current;
            Current = Copy(state); ReceivedAt = now;
            return true;
        }

        public float Blend(float now)
        {
            if (Previous == null || !BankTransferPolicy.IsFinite(now)) return 1;
            return Math.Max(0, Math.Min(1, (now - ReceivedAt) / .1f));
        }

        public static bool Forward(uint previous, uint next)
        {
            uint delta = unchecked(next - previous);
            return delta > 0 && delta <= int.MaxValue;
        }

        public static bool IsValid(VenttiSceneState state)
        {
            if (state.Poses == null || state.Poses.Length < VenttiSceneState.WorldPoseCount || state.Poses.Length > VenttiSceneState.MaxPoses) return false;
            for (int i = 0; i < state.Poses.Length; i++)
            {
                var pose = state.Poses[i];
                if (pose.Active > 1 || !PositionValid(pose.Position, i < VenttiSceneState.WorldPoseCount ? 100000 : 1000)) return false;
                var q = pose.Rotation;
                if (!BankTransferPolicy.IsFinite(q.X) || !BankTransferPolicy.IsFinite(q.Y)
                    || !BankTransferPolicy.IsFinite(q.Z) || !BankTransferPolicy.IsFinite(q.W)) return false;
                double norm = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
                if (norm < .999 || norm > 1.001) return false;
            }
            return true;
        }

        public static bool PositionValid(NetVector3 p, float limit) =>
            BankTransferPolicy.IsFinite(p.X) && BankTransferPolicy.IsFinite(p.Y) && BankTransferPolicy.IsFinite(p.Z)
            && Math.Abs(p.X) <= limit && Math.Abs(p.Y) <= limit && Math.Abs(p.Z) <= limit;

        public static bool Same(VenttiSceneState a, VenttiSceneState b)
        {
            if (a.TableId != b.TableId || a.LayoutId != b.LayoutId || a.Poses.Length != b.Poses.Length) return false;
            for (int i = 0; i < a.Poses.Length; i++)
            {
                var x = a.Poses[i]; var y = b.Poses[i];
                if (x.Active != y.Active || Math.Abs(x.Position.X - y.Position.X) > .001f
                    || Math.Abs(x.Position.Y - y.Position.Y) > .001f || Math.Abs(x.Position.Z - y.Position.Z) > .001f) return false;
                float dot = x.Rotation.X * y.Rotation.X + x.Rotation.Y * y.Rotation.Y + x.Rotation.Z * y.Rotation.Z + x.Rotation.W * y.Rotation.W;
                if (Math.Abs(dot) < .99999f) return false;
            }
            return true;
        }

        public static uint Layout(string rootPath, string[] poses, string[] sounds) =>
            StableHash.Fnv1a32("VenttiReaction/v1\n" + rootPath + "\n" + string.Join("\n", poses) + "\0" + string.Join("\n", sounds));
        public static VenttiSceneState Copy(VenttiSceneState s) => new VenttiSceneState
        {
            TableId = s.TableId, LayoutId = s.LayoutId, Sequence = s.Sequence, Poses = (VenttiPose[])s.Poses.Clone(),
        };
        public void Clear() { Current = Previous = null; ReceivedAt = 0; }
    }
}
