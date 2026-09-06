using System;
using System.Globalization;

namespace WinterMP.Net.Sync
{
    public static class FactoryItemIdentity
    {
        public static uint FactoryId(string path, string fsm) => StableHash.Fnv1a32(path + "::" + fsm);
        public static uint ItemId(uint factoryId, string nativeId) =>
            StableHash.Fnv1a32("factory:" + factoryId.ToString(CultureInfo.InvariantCulture) + ":" + nativeId);

        public static bool IsNativeId(string nativeId, string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(nativeId) || nativeId.Length > 128
                || !nativeId.StartsWith(prefix, StringComparison.Ordinal) || nativeId.Length == prefix.Length) return false;
            // Native counters are positive Int32 values, serialized without signs or padding.
            int first = prefix.Length;
            if (nativeId[first] < '1' || nativeId[first] > '9') return false;
            for (int i = first + 1; i < nativeId.Length; i++)
                if (nativeId[i] < '0' || nativeId[i] > '9') return false;
            return int.TryParse(nativeId.Substring(first), NumberStyles.None, CultureInfo.InvariantCulture, out _);
        }
    }
}
