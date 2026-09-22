using System;
using System.Globalization;
using WinterMP.Net.Messages;
namespace WinterMP.Net.Sync
{
    public static class BulbPolicy
    {
        public static uint BoxOutput(uint boxId)
        {
            if (boxId == 0) throw new ArgumentException("Missing bulb box.");
            return StableHash.Fnv1a32("bulb:box:" + boxId.ToString(CultureInfo.InvariantCulture));
        }
        public static bool Valid(BulbState s)
        {
            var p = s.Position; var q = s.Rotation;
            float norm = q.X*q.X + q.Y*q.Y + q.Z*q.Z + q.W*q.W;
            return s.ItemId != 0 && s.Revision != 0 && s.Wear >= 0 && s.Wear <= 100
                && Math.Abs(p.X) <= 100000 && Math.Abs(p.Y) <= 100000 && Math.Abs(p.Z) <= 100000 && norm >= .9f && norm <= 1.1f;
        }
        public static bool Accept(BulbState incoming, BulbState? previous) => Valid(incoming) && (previous == null
            || incoming.ItemId == previous.ItemId && (TrainPolicy.Newer(incoming.Revision, previous.Revision)
                || incoming.Revision == previous.Revision && incoming.Wear == previous.Wear));
    }
}
