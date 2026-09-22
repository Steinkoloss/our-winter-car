using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly HashSet<uint> _cabinItemIds = new HashSet<uint>();
        private readonly HashSet<Rigidbody> _cabinBodies = new HashSet<Rigidbody>();
        internal SyncedItem BindCabinWood(uint id, Rigidbody body)
        {
            if (id == 0 || body == null || _cabinItemIds.Contains(id) || _items.ContainsKey(id))
                throw new InvalidOperationException("Cabin resource identity collision.");
            SyncedItem? item = null;
            foreach (var candidate in _items.Values) if (candidate.Body == body) { item = candidate; break; }
            if (item != null) _items.Remove(item.Id);
            else item = new SyncedItem { Body = body, Path = ScenePath.Of(body.transform), LastPosition = body.position };
            item.Id = id; item.OutSequence = 0; item.LastRemoteSequence = 0;
            item.LastRemoteSequenceOwner = WorldSyncIds.NoOwner;
            item.RemoteOwner = WorldSyncIds.NoOwner; item.LocallyOwned = false;
            _items.Add(id, item); _trackedBodies[body] = true;
            _cabinItemIds.Add(id); _cabinBodies.Add(body);
            return item;
        }
        internal void UnbindCabinWood(uint id)
        {
            if (!_cabinItemIds.Contains(id) || !_items.TryGetValue(id, out var item)) return;
            if (item.Body != null)
            {
                ReleaseHeldBag(item.Body);
                if (item.KinematicSaved) item.Body.isKinematic = item.OriginalKinematic;
            }
            // Keep the reserved body/ID until world reset: a possibly partial native
            // feed must never be rediscovered through generic clone hashing/despawn.
            _items.Remove(id); _pendingItemPoses.Remove(id);
        }
    }
}
