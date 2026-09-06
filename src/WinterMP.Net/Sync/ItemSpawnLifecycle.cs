using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Session-wide removals outlive deferred spawning; replay can repair missing live items.</summary>
    public sealed class ItemSpawnLifecycle
    {
        private readonly HashSet<long> _manifests = new HashSet<long>();
        private readonly HashSet<uint> _retired = new HashSet<uint>();
        public IEnumerable<uint> RetiredIds => _retired;
        public void Retire(uint id) { _retired.Add(id); }
        public bool IsRetired(uint id) => _retired.Contains(id);
        public bool ShouldMaterialize(uint id, bool hasLiveBody) => !hasLiveBody && !_retired.Contains(id);
        public void Clear() { _manifests.Clear(); _retired.Clear(); }

        public bool AcceptManifest(ItemSpawn message)
        {
            if (!Valid(message)) return false;
            // Persistent factory identities are sufficient for per-item deduplication.
            // Their native counters outlive a session's wrapping ushort spill epochs.
            if (message.IsFactory) return true;
            bool first = _manifests.Add(((long)message.ContainerNetId << 16) | message.Epoch);
            // A replay is a fresh observation of which items still exist on the host.
            // Deduplicate at the item body, not the spill, so a lost replica can heal.
            return first || message.IsReplay;
        }

        private static bool Valid(ItemSpawn message)
        {
            if ((message.Flags & ~(ItemSpawn.FlagReplay | ItemSpawn.FlagFactory)) != 0 || message.Items == null || message.Items.Count > ItemSpawn.MaxItems
                || message.StateName == null || message.StateName.Length > 128) return false;
            if (message.IsFactory && (message.OfferSequence != 0 || message.Items.Count == 0)) return false;
            var ids = new HashSet<uint>();
            foreach (var entry in message.Items)
            {
                if (!ids.Add(entry.NetId) || string.IsNullOrEmpty(entry.TemplateName) || entry.TemplateName.Length > 128) return false;
                if (message.IsFactory && entry.NetId != FactoryItemIdentity.ItemId(message.ContainerNetId, entry.TemplateName)) return false;
                foreach (char c in entry.TemplateName) if (char.IsControl(c)) return false;
                var p = entry.Position; var q = entry.Rotation;
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(q.X) || !Finite(q.Y) || !Finite(q.Z) || !Finite(q.W)) return false;
                double norm = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
                if (norm < .9 || norm > 1.1) return false;
            }
            return true;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
