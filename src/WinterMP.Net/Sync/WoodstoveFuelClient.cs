using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Production admission/request/ack gate. No prediction, world writes or persistence.</summary>
    public sealed class WoodstoveFuelClient
    {
        private readonly byte _actor;
        private WoodstoveFuelReplica? _replica;
        private readonly Dictionary<uint, byte> _resources = new Dictionary<uint, byte>();
        private readonly Dictionary<uint, uint> _requests = new Dictionary<uint, uint>();
        private readonly HashSet<uint> _pending = new HashSet<uint>();
        public uint Epoch { get; private set; }
        public uint HighWater { get; private set; }
        public WoodstoveFuelSnapshot? Current => _replica?.Current;
        public WoodstoveFuelUpdate? LastDecision { get; private set; }
        public WoodstoveFuelClient(byte actor)
        {
            if (actor == 255) throw new ArgumentOutOfRangeException(nameof(actor));
            _actor = actor;
        }
        public bool Receive(bool authenticatedHost, WoodstoveFuelUpdate update)
        {
            if (!authenticatedHost || update == null || update.Epoch == 0) return false;
            if (Epoch == 0)
            {
                if (update.IsDecision || update.Actor != _actor || update.Snapshot == null) return false;
                var replica = new WoodstoveFuelReplica(update.Epoch);
                if (!replica.Receive(true, update.Snapshot)) return false;
                Epoch = update.Epoch; _replica = replica;
            }
            if (Epoch != update.Epoch) return false;
            if (update.ResourceId != 0 && (update.Shape == 0 || update.Shape > 2
                || (_resources.TryGetValue(update.ResourceId, out byte shape) && shape != update.Shape))) return false;
            if (update.Actor == _actor)
            {
                HighWater = Math.Max(HighWater, update.HighWater);
                if (update.IsDecision && _requests.TryGetValue(update.Sequence, out uint resource))
                {
                    if (update.Status == WoodstoveFeedStatus.Pending) _pending.Add(update.Sequence);
                    // A duplicate can be denied while the original native feed is
                    // still pending. Only its final outcome releases that reservation.
                    else if (!_pending.Contains(update.Sequence) || (update.Status != WoodstoveFeedStatus.Busy
                        && update.Status != WoodstoveFeedStatus.ReplayedSequence))
                    {
                        if (update.Status == WoodstoveFeedStatus.Accepted && (update.Snapshot == null
                            || Array.BinarySearch(update.Snapshot.ConsumedResources, resource) < 0)) return false;
                        _requests.Remove(update.Sequence); _pending.Remove(update.Sequence); LastDecision = update;
                    }
                }
            }
            if (update.Snapshot == null || !_replica!.Receive(true, update.Snapshot)) return false;
            if (update.ResourceId != 0 && Array.BinarySearch(Current!.ConsumedResources, update.ResourceId) < 0)
                _resources[update.ResourceId] = update.Shape;
            foreach (uint retired in Current!.ConsumedResources) _resources.Remove(retired);
            return true;
        }
        public WoodstoveFeedIntent? Create(uint resource)
        {
            if (Epoch == 0 || HighWater == uint.MaxValue || !_resources.ContainsKey(resource)
                || Current == null || Array.BinarySearch(Current.ConsumedResources, resource) >= 0) return null;
            // One in-flight attempt per piece; don't retry a pending native mutation.
            foreach (uint pending in _requests.Values) if (pending == resource) return null;
            uint sequence = ++HighWater; _requests.Add(sequence, resource);
            return new WoodstoveFeedIntent { SourceId = WoodstoveFuelAuthority.CabinSourceId,
                Epoch = Epoch, Actor = _actor, Sequence = sequence, ResourceId = resource };
        }
    }
}
