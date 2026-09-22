using System;
using System.Collections.Generic;
using System.Globalization;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class FleaListingPolicy
    {
        public const int Capacity = 64;
        // Native Sell strips eight characters for the object name, then seven for
        // its price guide. Letters distinguish our saved identities from vanilla RNG.
        public static string Key(string itemName, uint nativeNumber)
        {
            if (nativeNumber < 1 || nativeNumber > 999999) throw new ArgumentOutOfRangeException("nativeNumber");
            return itemName + "OW" + nativeNumber.ToString("D6", CultureInfo.InvariantCulture);
        }
        public static bool TryNumber(string nativeId, string prefix, out uint number)
        {
            number = 0;
            if (nativeId == null || prefix == null || prefix.Length == 0 || !nativeId.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string digits = nativeId.Substring(prefix.Length);
            if (digits.Length < 1 || digits.Length > 6 || digits[0] == '0') return false;
            foreach (char c in digits) { if (c < '0' || c > '9') return false; number = number * 10 + (uint)(c - '0'); }
            return number > 0;
        }
        public static bool Valid(FleaListingIntent r) => r != null && r.PlayerId != 255 && r.ItemId != 0 && r.Price <= 999;
        public static bool Valid(FleaListingState s)
        {
            if (s == null || s.Items.Count > Capacity) return false;
            var ids = new HashSet<uint>(); var native = new HashSet<uint>();
            foreach (var e in s.Items)
            {
                if (e == null || e.ItemId == 0 || e.NativeNumber < 1 || e.NativeNumber > 999999 || e.Price > 999
                    || !ids.Add(e.ItemId) || !native.Add(e.NativeNumber)
                    || !Finite(e.Position.X) || !Finite(e.Position.Y) || !Finite(e.Position.Z)) return false;
                var q = e.Rotation; float n = q.X*q.X + q.Y*q.Y + q.Z*q.Z + q.W*q.W;
                if (!Finite(n) || n < .9f || n > 1.1f) return false;
            }
            return true;
        }
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        public static FleaListingState Copy(FleaListingState s)
        {
            var copy = new FleaListingState { Revision = s.Revision };
            foreach (var e in s.Items) copy.Items.Add(new FleaListingState.Entry { ItemId = e.ItemId,
                NativeNumber = e.NativeNumber, Price = e.Price, Position = e.Position, Rotation = e.Rotation });
            return copy;
        }
        public static bool Same(FleaListingState a, FleaListingState b)
        {
            if (a.Items.Count != b.Items.Count) return false;
            for (int i = 0; i < a.Items.Count; i++)
            {
                var x = a.Items[i]; var y = b.Items[i];
                if (x.ItemId != y.ItemId || x.NativeNumber != y.NativeNumber || x.Price != y.Price
                    || x.Position.X != y.Position.X || x.Position.Y != y.Position.Y || x.Position.Z != y.Position.Z
                    || x.Rotation.X != y.Rotation.X || x.Rotation.Y != y.Rotation.Y || x.Rotation.Z != y.Rotation.Z || x.Rotation.W != y.Rotation.W) return false;
            }
            return true;
        }
        public static bool CanApply(FleaListingState? old, FleaListingState next) => Valid(next)
            && (old == null || (old.Revision == next.Revision ? Same(old, next) : unchecked(next.Revision - old.Revision) < 0x80000000u));
    }

    public sealed class FleaListingReceipts
    {
        private sealed class Receipt { internal FleaListingIntent Request = null!; internal byte Result; }
        private readonly Dictionary<byte, Receipt> _last = new Dictionary<byte, Receipt>();
        public bool TryGet(FleaListingIntent r, out byte result)
        {
            result = FleaListingResult.Stale;
            if (!_last.TryGetValue(r.PlayerId, out var old)) return false;
            ushort delta = unchecked((ushort)(r.Sequence - old.Request.Sequence));
            if (delta != 0) return delta > short.MaxValue;
            if (r.Revision == old.Request.Revision && r.ItemId == old.Request.ItemId && r.Price == old.Request.Price) result = old.Result;
            return true;
        }
        public void Record(FleaListingIntent r, byte result)
        {
            _last[r.PlayerId] = new Receipt { Result = result, Request = new FleaListingIntent {
                PlayerId = r.PlayerId, Sequence = r.Sequence, ItemId = r.ItemId, Revision = r.Revision, Price = r.Price } };
        }
        public void ForgetPlayer(byte player) { _last.Remove(player); }
        public void Clear() { _last.Clear(); }
    }
}
