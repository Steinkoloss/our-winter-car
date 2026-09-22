using System;

namespace WinterMP.Net.Sync
{
    /// <summary>
    /// Absolute state acceptance only, never prediction or native event replay.
    /// Session authentication and a fresh known epoch must precede Receive.
    /// A future native adapter applies Current.Values and retires the listed
    /// resources without changing ownership. A fresh join starts from a snapshot.
    /// </summary>
    public sealed class WoodstoveFuelReplica
    {
        private readonly uint _epoch;
        public WoodstoveFuelSnapshot? Current { get; private set; }
        public WoodstoveFuelReplica(uint epoch)
        {
            if (epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            _epoch = epoch;
        }
        public bool Receive(bool authenticatedHost, WoodstoveFuelSnapshot snapshot)
        {
            if (!authenticatedHost || snapshot == null || snapshot.SourceId != WoodstoveFuelAuthority.CabinSourceId
                || snapshot.Epoch != _epoch || snapshot.Revision == 0 || !snapshot.Values.Valid) return false;
            var resources = snapshot.ConsumedResources;
            for (int i = 0; i < resources.Length; i++)
                if (resources[i] == 0 || (i > 0 && resources[i] <= resources[i - 1])) return false;
            if (Current != null)
            {
                if (snapshot.Revision < Current.Revision) return false;
                var previous = Current.ConsumedResources;
                if (snapshot.Revision == Current.Revision && (!snapshot.Values.Same(Current.Values)
                    || resources.Length != previous.Length)) return false;
                // Within one epoch, a resource tombstone can never disappear.
                foreach (uint id in previous) if (Array.BinarySearch(resources, id) < 0) return false;
            }
            Current = snapshot;
            return true;
        }
    }
}
