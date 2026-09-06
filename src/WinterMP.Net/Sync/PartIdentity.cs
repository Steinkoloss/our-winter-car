using System;
using System.Globalization;

namespace WinterMP.Net.Sync
{
    /// <summary>Native save identity survives installation, renaming and scan order.</summary>
    public static class PartIdentity
    {
        public static bool TryPersistentId(string nativeId, string assemblyKey, string positionKey,
            string assemblySuffix, string positionSuffix, out uint id)
        {
            id = 0;
            if (string.IsNullOrEmpty(assemblySuffix) || string.IsNullOrEmpty(positionSuffix) || assemblySuffix == positionSuffix
                || assemblyKey != nativeId + assemblySuffix || positionKey != nativeId + positionSuffix) return false;
            return TryItemId(nativeId, out id);
        }

        public static bool TryItemId(string nativeId, out uint id)
        {
            id = 0;
            if (string.IsNullOrEmpty(nativeId) || nativeId.Length < 2 || nativeId.Length > 128
                || !Letter(nativeId[0]) || nativeId[nativeId.Length - 1] < '0' || nativeId[nativeId.Length - 1] > '9') return false;
            foreach (char c in nativeId)
                if (!Letter(c) && (c < '0' || c > '9')) return false;
            // The whole ID is opaque. VIN1330 is a valid original part, and the
            // trailing zero in ALTERNATOR0 belongs to its factory prefix.
            id = StableHash.Fnv1a32("part:" + nativeId);
            return true;
        }

        public static bool TryFsmId(uint itemId, string relativePath, string fsmName, out uint id)
        {
            id = 0;
            if (!ValidRelativePath(relativePath) || string.IsNullOrEmpty(fsmName) || fsmName.Length > 128
                || fsmName.IndexOfAny(new[] { ':', '/', '\\' }) >= 0 || HasControl(fsmName)) return false;
            id = StableHash.Fnv1a32("part-fsm:" + itemId.ToString(CultureInfo.InvariantCulture) + ":"
                + relativePath + "::" + fsmName);
            return true;
        }

        private static bool ValidRelativePath(string path)
        {
            if (path == null || path.Length > 1024 || HasControl(path)) return false;
            if (path.Length == 0) return true;
            if (path[0] == '/' || path[path.Length - 1] == '/' || path.IndexOf("//", StringComparison.Ordinal) >= 0
                || path.IndexOfAny(new[] { ':', '\\', '\r', '\n', '\0' }) >= 0) return false;
            foreach (string segment in path.Split('/'))
                if (segment == "." || segment == "..") return false;
            return true;
        }
        private static bool HasControl(string value)
        {
            foreach (char c in value) if (char.IsControl(c)) return true;
            return false;
        }
        private static bool Letter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}
