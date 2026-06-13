using System.Collections.Generic;
using UnityEngine;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        internal IEnumerable<WorldItemSnapshot> BuildItemSnapshotChunks()
        {
            var items = new WorldItemSnapshot();
            foreach (var pair in _items)
            {
                var item = pair.Value;
                if (item.Body == null) continue;

                items.Entries.Add(new WorldItemSnapshot.Entry
                {
                    ItemId = pair.Key,
                    Position = item.Body.transform.position.ToNet(),
                    Rotation = item.Body.transform.rotation.ToNet(),
                });
                if (items.Entries.Count >= ItemSnapshotChunk)
                {
                    yield return items;
                    items = new WorldItemSnapshot();
                }
            }

            if (items.Entries.Count > 0)
                yield return items;
        }

        public void OnRemoteItemSnapshot(WorldItemSnapshot message)
        {
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                var position = entry.Position.ToUnity();
                var rotation = entry.Rotation.ToUnity();

                if (_items.TryGetValue(entry.ItemId, out var item) && item.Body != null)
                {
                    // Live streams beat the snapshot (it was built moments ago).
                    if (item.LocallyOwned || Time.unscaledTime - item.LastRemoteAt < GetRemoteHoldSeconds(item))
                        continue;
                    ApplySnapshotPose(item, position, rotation);
                    applied++;
                }
                else
                {
                    _pendingItemPoses[entry.ItemId] = new PendingPose
                    {
                        Position = position,
                        Rotation = rotation,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: item snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        public void OnRemoteItemDespawnSnapshot(WorldItemDespawnSnapshot message)
        {
            int removed = 0, parked = 0;
            foreach (uint itemId in message.ItemIds)
            {
                if (_items.TryGetValue(itemId, out var item) && item.Body != null)
                {
                    item.DespawnSent = true;
                    _bridge.ApplyingRemote = true;
                    try
                    {
                        UnityEngine.Object.Destroy(item.Body.gameObject);
                    }
                    finally
                    {
                        _bridge.ApplyingRemote = false;
                    }

                    RemoveTrackedItem(itemId, item.Body);
                    removed++;
                }
                else
                {
                    _pendingDespawnedItems.Add(itemId);
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: despawn snapshot — {message.ItemIds.Count} ids, {removed} removed, {parked} parked.");
        }

        private static void ApplySnapshotPose(SyncedItem item, Vector3 position, Quaternion rotation)
        {
            var body = item.Body;
            body.transform.position = position;
            body.transform.rotation = rotation;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep();
            }

            // The teleport must not register as local motion (no claim, no stream).
            item.LastPosition = position;
            item.LastMovedAt = -999f;
        }
    }
}
