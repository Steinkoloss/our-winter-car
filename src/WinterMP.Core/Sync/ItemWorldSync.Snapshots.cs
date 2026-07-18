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
                // Remember every id the host proved it knows: spawn-manifest adoption
                // uses "tracked here but never in a host snapshot" to spot stale clones
                // left over from a previous connection (see MaterializeSpawnEntry).
                _snapshotSeenIds.Add(entry.ItemId);

                var position = entry.Position.ToUnity();
                var rotation = entry.Rotation.ToUnity();

                if (_items.TryGetValue(entry.ItemId, out var item) && item.Body != null)
                {
                    // Live streams beat the snapshot (it was built moments ago). Also skip
                    // an item that is rolling under local physics but not yet claimed: a
                    // stale snapshot pose would teleport-and-sleep it mid-roll (#28). It
                    // self-heals once it rests (the at-rest item checksum catches drift).
                    if (item.LocallyOwned
                        || Time.unscaledTime - item.LastRemoteAt < GetRemoteHoldSeconds(item)
                        || Time.unscaledTime - item.LastMovedAt < item.StillSeconds)
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
                    ReleaseCargoFollowingVehicle(itemId);
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

        private void ApplySnapshotPose(SyncedItem item, Vector3 position, Quaternion rotation)
        {
            // Validate the snapshot pose like OnRemoteItemTransform does: snapshot floats
            // reach the transform verbatim (ToUnity copies raw wire components), and Unity
            // silently drops a NaN/Infinity transform — the item would vanish for the rest
            // of the session. Re-normalize the quaternion so the landing rotation is unit.
            if (!IsFinite(position) || !TryNormalize(rotation, out rotation))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropping non-finite snapshot pose for item {item.Id}.");
                return;
            }

            var body = item.Body;
            // A snapshot teleport overrides any in-progress cargo pin or membership:
            // release (restoring the saved kinematic flag) so the pose lands on a body
            // in its normal physics state; a live cargo stream simply re-pins from here.
            ReleaseRemoteCargo(item, body, Time.unscaledTime, seedVelocity: false);
            item.LocalCargoVehicleId = 0;
            RestoreCargoPhysics(item);
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
