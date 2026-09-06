using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public enum NativePartPhase { Unavailable, Loose, Fitted, Retired }

    /// <summary>Native part scalars are not normalized units. Never clamp host wear to 0..1.</summary>
    public static class PartStatePolicy
    {
        // Native mounts destroy/recreate Rigidbody while Data and its save identity
        // survive. Data.Installed is an occupancy query scratch variable, not this phase.
        public static NativePartPhase NativePhase(bool dataExists, int assemblyId, bool consumed, bool hasBody)
        {
            if (!dataExists || consumed) return NativePartPhase.Retired;
            if (assemblyId < 0) return NativePartPhase.Unavailable;
            if (assemblyId > 0) return NativePartPhase.Fitted;
            return hasBody ? NativePartPhase.Loose : NativePartPhase.Unavailable;
        }

        public static bool Valid(byte flags, float tightness, float wear) =>
            (flags & ~PartState.FlagInstalled) == 0 && Finite(tightness) && Finite(wear);

        public static bool Valid(PartState state) => Valid(state.Flags, state.TightnessValue, state.WearValue);

        public static PartState? Capture(uint id, bool installed, float tightness, float wear)
        {
            byte flags = installed ? PartState.FlagInstalled : (byte)0;
            if (!Valid(flags, tightness, wear)) return null;
            return new PartState { NetId = id, Flags = flags, TightnessValue = tightness, WearValue = wear,
                Tightness = LegacyHint(tightness), Wear = LegacyHint(wear) };
        }

        public static WorldPartSnapshot.Entry SnapshotEntry(PartState state) => new WorldPartSnapshot.Entry
        {
            NetId = state.NetId, Flags = state.Flags, Tightness = state.Tightness, Wear = state.Wear,
            TightnessValue = state.TightnessValue, WearValue = state.WearValue,
        };

        public static bool Valid(WorldPartSnapshot snapshot)
        {
            if (snapshot.Entries == null || snapshot.Entries.Count > WorldPartSnapshot.MaxEntries) return false;
            var ids = new HashSet<uint>();
            foreach (var entry in snapshot.Entries)
                if (!ids.Add(entry.NetId) || !Valid(entry.Flags, entry.TightnessValue, entry.WearValue)) return false;
            return true;
        }

        /// <summary>Reports request a fresh host observation after native FSM events settle.
        /// Guest random initialization, wear drift or forged scalars cannot alter the host part.</summary>
        public static PartState? HostReply(PartState report, PartState current)
        {
            if (report.NetId != current.NetId || !Valid(report) || !Valid(current)) return null;
            return Capture(current.NetId, (current.Flags & PartState.FlagInstalled) != 0,
                current.TightnessValue, current.WearValue);
        }

        public static uint MixChecksum(uint crc, PartState state)
        {
            if (!Valid(state)) return crc;
            crc = StableHash.Combine(crc, state.NetId);
            crc = StableHash.Combine(crc, state.Flags);
            crc = StableHash.Combine(crc, ScalarBits(state.TightnessValue));
            return StableHash.Combine(crc, ScalarBits(state.WearValue));
        }

        private static uint ScalarBits(float value)
        {
            // Suppress float noise without collapsing all healthy parts to the same
            // checksum. Use double for rounding so even finite float extrema cannot overflow.
            float rounded = (float)(Math.Round((double)value * 1000d) / 1000d);
            if (rounded == 0f) rounded = 0f;
            return BitConverter.ToUInt32(BitConverter.GetBytes(rounded), 0);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static byte LegacyHint(float value) => (byte)Math.Round(Math.Max(0d, Math.Min(1d, value)) * 255d);
    }
}
