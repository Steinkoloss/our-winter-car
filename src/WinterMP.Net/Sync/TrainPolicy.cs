using System;
using WinterMP.Net.Messages;
namespace WinterMP.Net.Sync
{
    public static class TrainPolicy
    {
        public const ushort AllColliders = 2047;
        public static bool Valid(TrainState s)
        {
            var p = s.Position; var v = s.Velocity; var q = s.Rotation;
            float speed = v.X*v.X + v.Y*v.Y + v.Z*v.Z, norm = q.X*q.X+q.Y*q.Y+q.Z*q.Z+q.W*q.W;
            return s.Sequence != 0 && s.Phase <= 3 && s.Flags <= 7 && s.ColliderMask <= AllColliders
                && s.Volume >= 0 && s.Volume <= 1 && Math.Abs(p.X) <= 100000 && Math.Abs(p.Y) <= 100000 && Math.Abs(p.Z) <= 100000
                && speed <= 961 && speed >= 0 && ((s.Phase & 1) == 0 || speed == 0) && norm >= .9f && norm <= 1.1f;
        }
        public static bool Newer(uint value, uint previous) => value != 0 && unchecked((int)(value - previous)) > 0;
        public static bool Fresh(float age) => age >= 0 && age <= 1;
        public static NetVector3 Predict(TrainState state, float age)
        {
            float time = age >= 0 ? Math.Min(age, .2f) : 0;
            return new NetVector3(state.Position.X + state.Velocity.X*time, state.Position.Y + state.Velocity.Y*time, state.Position.Z + state.Velocity.Z*time);
        }
        public static bool SameLifecycle(TrainState a, TrainState b) => a.Phase == b.Phase && a.Flags == b.Flags && a.ColliderMask == b.ColliderMask && a.HornSequence == b.HornSequence;
    }
}
