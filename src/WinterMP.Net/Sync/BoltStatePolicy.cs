using System;
using System.Collections;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    // Host messages share reliable order, but different bolt FSMs can bind later.
    // Applying an older bolt's array entry must not roll back its parent's total.
    public sealed class PartTightnessReceipts
    {
        private sealed class Entry { public ulong Order; public float Value; }
        private readonly Dictionary<uint, Entry> _values = new Dictionary<uint, Entry>();
        public float Resolve(uint partId, ulong order, float value)
        {
            if (order == 0 || float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Invalid part receipt.");
            if (!_values.TryGetValue(partId, out var entry))
                _values.Add(partId, entry = new Entry { Order = order, Value = value });
            else if (order > entry.Order) { entry.Order = order; entry.Value = value; }
            return entry.Value;
        }
        public void Clear() => _values.Clear();
        public float Latest(uint partId, float fallback) => _values.TryGetValue(partId, out var entry) ? entry.Value : fallback;
    }

    /// <summary>A replacement needs a fresh absolute bolt observation after each attachment.</summary>
    public sealed class ReplicaBoltGate
    {
        private ulong _afterOrder;
        public bool Attached { get; private set; }
        public bool Seeded { get; private set; }
        public void SetAttachment(bool attached, ulong afterOrder)
        {
            Attached = attached; Seeded = false; _afterOrder = afterOrder;
        }
        public bool CanReceive(ulong order) => Attached && order > _afterOrder;
        public void Received(ulong order) { if (CanReceive(order)) Seeded = true; }
        public bool CanTurn(int tightness, int direction) => Attached && Seeded
            && tightness >= 0 && tightness <= BoltStatePolicy.MaximumTightness
            && ((direction == -1 && tightness > 0) || (direction == 1 && tightness < BoltStatePolicy.MaximumTightness));

        public static bool TryIndex(string name, int start, int length, int count, out int index)
        {
            index = 0;
            if (name == null || start < 0 || length <= 0 || length > 9 || start > name.Length - length || count <= 0) return false;
            for (int i = start; i < start + length; i++)
            {
                if (name[i] < '0' || name[i] > '9') return false;
                index = index * 10 + name[i] - '0';
            }
            return index < count;
        }
    }

    public static class BoltStatePolicy
    {
        public const int MaximumTightness = 8;

        public static bool Valid(BoltState state) => state != null && state.BoltTightness <= MaximumTightness
            && state.ScrewInt == 0 && Finite(state.PartTightness);

        public static BoltState? Capture(uint id, int tightness, float partTightness)
        {
            if (tightness < 0 || tightness > MaximumTightness || !Finite(partTightness)) return null;
            return new BoltState { NetId = id, BoltTightness = (ushort)tightness, PartTightness = partTightness };
        }

        public static BoltState? HostReply(BoltState report, BoltState current) =>
            Valid(report) && Valid(current) && report.NetId == current.NetId
                ? Capture(current.NetId, current.BoltTightness, current.PartTightness) : null;

        public static bool Valid(WorldBoltSnapshot snapshot)
        {
            if (snapshot.Entries == null || snapshot.Entries.Count > WorldBoltSnapshot.MaxEntries) return false;
            var ids = new HashSet<uint>();
            foreach (var entry in snapshot.Entries)
                if (!ids.Add(entry.NetId) || !Valid(FromSnapshot(entry))) return false;
            return true;
        }

        public static WorldBoltSnapshot.Entry SnapshotEntry(BoltState state) => new WorldBoltSnapshot.Entry {
            NetId = state.NetId, BoltTightness = state.BoltTightness, ScrewInt = state.ScrewInt, PartTightness = state.PartTightness };
        public static BoltState FromSnapshot(WorldBoltSnapshot.Entry entry) => new BoltState {
            NetId = entry.NetId, BoltTightness = entry.BoltTightness, ScrewInt = entry.ScrewInt, PartTightness = entry.PartTightness };

        // Absolute array assignment repairs predicted turns and missed updates without
        // adding another turn to the part's aggregate. Native Calc pos supplies divisor.
        public static bool ApplyArray(BoltState state, IList values, int index, float divisor, out float position)
        {
            position = 0;
            if (!Valid(state) || values == null || values.IsReadOnly || index < 0 || index >= values.Count
                || !(values[index] is int) || !Finite(divisor) || divisor == 0) return false;
            float result = state.BoltTightness / divisor;
            if (!Finite(result)) return false;
            values[index] = (int)state.BoltTightness;
            position = result;
            return true;
        }

        public static uint MixChecksum(uint crc, BoltState state)
        {
            if (!Valid(state)) return crc;
            crc = StableHash.Combine(crc, state.NetId);
            crc = StableHash.Combine(crc, state.BoltTightness);
            float rounded = (float)(Math.Round((double)state.PartTightness * 1000d) / 1000d);
            if (rounded == 0f) rounded = 0f;
            return StableHash.Combine(crc, BitConverter.ToUInt32(BitConverter.GetBytes(rounded), 0));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
