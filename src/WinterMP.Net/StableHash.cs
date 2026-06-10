namespace WinterMP.Net
{
    /// <summary>
    /// FNV-1a 32-bit. Used for deterministic network ids derived from scene paths and
    /// for the sync catalog hash. Must stay byte-for-byte stable across versions —
    /// never change this algorithm without bumping the protocol version.
    /// </summary>
    public static class StableHash
    {
        public const uint OffsetBasis = 2166136261;
        private const uint Prime = 16777619;

        public static uint Fnv1a32(string text)
        {
            uint hash = OffsetBasis;
            foreach (char c in text)
            {
                hash ^= (byte)c;
                hash *= Prime;
                hash ^= (byte)(c >> 8);
                hash *= Prime;
            }

            return hash;
        }

        /// <summary>Combine two hashes (e.g. fold per-object hashes into a catalog hash).</summary>
        public static uint Combine(uint a, uint b)
        {
            unchecked
            {
                uint hash = a;
                hash ^= b & 0xFF;
                hash *= Prime;
                hash ^= (b >> 8) & 0xFF;
                hash *= Prime;
                hash ^= (b >> 16) & 0xFF;
                hash *= Prime;
                hash ^= (b >> 24) & 0xFF;
                hash *= Prime;
                return hash;
            }
        }
    }
}
