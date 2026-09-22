using System;
using WinterMP.Net.Messages;
namespace WinterMP.Net.Sync
{
    public static class AdvertPolicy
    {
        public static bool Pose(NetVector3 p, NetQuaternion q)
        {
            float n = q.X*q.X+q.Y*q.Y+q.Z*q.Z+q.W*q.W;
            return Math.Abs(p.X)<=100000 && Math.Abs(p.Y)<=100000 && Math.Abs(p.Z)<=100000 && n>=.9f && n<=1.1f;
        }
        public static bool Valid(AdvertJobState s) => s.Revision != 0 && s.CompletedMask < (1u << 28)
            && s.Delivered >= 0 && s.Delivered <= 1000000 && s.Sheets <= 30 && (s.Stage == 0 || s.Stage == 1 || s.Stage == 2 || s.Stage == 5)
            && s.NextDay <= 7 && s.Flags <= 7 && s.Scale >= 0 && s.Scale <= 1 && s.Salary >= 0 && s.Salary <= 100000000 && Pose(s.Position,s.Rotation);
        public static bool Valid(AdvertIntent r) => r.Sequence != 0 && r.ExpectedRevision != 0 && r.ItemId != 0 && r.PlayerId > 0 && r.PlayerId < 255 && (r.Box < 28 || r.Box == 255);
        public static bool Same(AdvertJobState a, AdvertJobState b) => a.CompletedMask == b.CompletedMask && a.Delivered == b.Delivered
            && a.Sheets == b.Sheets && a.Stage == b.Stage && a.NextDay == b.NextDay && a.Flags == b.Flags && a.Scale == b.Scale && a.Salary == b.Salary;
        public static bool Newer(uint value, uint previous) => value != 0 && unchecked((int)(value - previous)) > 0;
        public static bool CanTake(AdvertJobState s) => (s.Flags & 2) != 0 && s.Sheets > 0 && (s.Stage == 2 || s.Stage == 5);
        public static bool CanDeliver(AdvertJobState s, byte box) => box < 28 && (s.Stage == 2 || s.Stage == 5) && (s.CompletedMask & (1u << box)) == 0;
    }
}
