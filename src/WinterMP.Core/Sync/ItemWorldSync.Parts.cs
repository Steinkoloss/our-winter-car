using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly Dictionary<uint, PlayMakerFSM> _nativeParts = new Dictionary<uint, PlayMakerFSM>();

        private void ScanNativeParts()
        {
            // Saved fitted parts have no Rigidbody and cannot be found by the item scan.
            foreach (var obj in ScenePath.ScanFsms())
            {
                var data = obj as PlayMakerFSM;
                if (data == null || !data.Fsm.Initialized || !data.Fsm.Started || !NativePartIdentity.IsData(data)) continue;
                try
                {
                    if (_bridge.PartIdentities.TryRootId(data, out uint id)) TrackNativePart(data, id);
                }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: native part discovery: " + e.Message); }
            }
        }

        private void TrackNativePart(PlayMakerFSM data, uint id)
        {
            if (!_nativeParts.TryGetValue(id, out var previous) || previous != data) _nativeParts[id] = data;
            TrackReplacementPart(data, id);
        }

        internal bool CanSyncItemMotion(SyncedItem item)
        {
            if (!_nativeParts.TryGetValue(item.Id, out var data)) return true;
            if (IsPendingGuestPartIsolation(data)) return false;
            return NativePartIdentity.Phase(data) == NativePartPhase.Loose && data.gameObject.activeInHierarchy
                && data.GetComponent<Rigidbody>() == item.Body
                && (_replacementReplica == null || _replacementReplica.AllowsLooseMotion(item.Id));
        }

        private bool NativePartSurvivesBodyRemoval(uint id) => _nativeParts.TryGetValue(id, out var data)
            && NativePartIdentity.Phase(data) != NativePartPhase.Retired;

        private void ProcessNativeParts(SessionManager session)
        {
            foreach (var pair in new List<KeyValuePair<uint, PlayMakerFSM>>(_nativeParts))
            {
                try
                {
                    if (pair.Value == null && _replacementParts.TryGetValue(pair.Key, out var replica) && replica.Replica)
                    {
                        _bridge.ForgetReplacementBolts(pair.Value);
                        // A local parent graph can disappear before its host update.
                        // Losing a temporary child is not authority to dispose of the host part.
                        _bridge.PartIdentities.Forget(pair.Value);
                        _nativeParts.Remove(pair.Key);
                        RemoveNativeItemMotion(pair.Key);
                        _pendingReplacements.Add(pair.Key);
                        continue;
                    }
                    var phase = NativePartIdentity.Phase(pair.Value);
                    if (phase == NativePartPhase.Retired)
                    {
                        if (session.IsHost) AnnounceNativePartDespawn(pair.Key, "native part disposed");
                        else if (!_spawnLifecycle.IsRetired(pair.Key)) AnnounceItemDespawn(pair.Key, "native part disposed");
                    }
                    if (phase == NativePartPhase.Loose && pair.Value != null && pair.Value.gameObject.activeInHierarchy
                        && !_spawnLifecycle.IsRetired(pair.Key)
                        && (_replacementReplica == null || _replacementReplica.AllowsLooseMotion(pair.Key)))
                    {
                        // Removal creates a new body on the same Data object. Rebind it
                        // immediately, without resurrecting an old physics ownership lease.
                        TryScanNativePart(pair.Value.GetComponent<Rigidbody>());
                    }
                    else RemoveNativeItemMotion(pair.Key);
                }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: native part lifecycle: " + e.Message); }
            }
        }

        private void RemoveNativeItemMotion(uint id)
        {
            _pendingItemPoses.Remove(id);
            if (!_items.TryGetValue(id, out var item)) return;
            ReleaseRemoteCargo(item, item.Body, Time.unscaledTime, seedVelocity: false);
            RestoreCargoPhysics(item);
            if (item.Body != null && item.KinematicSaved) item.Body.isKinematic = item.OriginalKinematic;
            RemoveTrackedItem(id, item.Body);
        }

        private void AnnounceNativePartDespawn(uint id, string reason)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !SessionSyncActive(session) || _spawnLifecycle.IsRetired(id)) return;
            if (_items.TryGetValue(id, out var item)) item.DespawnSent = true;
            RecordItemRetirement(id);
            SyncEventLog.Record("despawn", id.ToString("X8") + " " + reason);
            session.SendWorldMessage(new ItemDespawn { ItemId = id }, Channel.ReliableOrdered);
        }

        private bool TryScanNativePart(Rigidbody body)
        {
            var data = NativePartIdentity.FindData(body.transform);
            if (data == null) return false;
            // A part's helper rigidbodies belong to its native assembly graph.
            // Only the part root participates in loose-item movement.
            if (data.transform != body.transform) return true;
            if (!_bridge.PartIdentities.TryRootId(data, out uint id))
            {
                var c = Catalog.SyncCatalog.PartIdentity;
                if (!data.Fsm.Started || (c != null && Array.IndexOf(c.InitializingStates, data.ActiveStateName) >= 0))
                    _unreadyNativeParts.Add(body);
                return true;
            }
            try
            {
                TrackNativePart(data, id);
                if (IsPendingGuestPartIsolation(data)) return true;
                if (NativePartIdentity.Phase(data) != NativePartPhase.Loose
                    || (_replacementReplica != null && !_replacementReplica.AllowsLooseMotion(id))) return true;
                if (_items.TryGetValue(id, out var previous))
                {
                    if (previous.Body == body) return true;
                    if (previous.Body != null) throw new InvalidOperationException("Native part item ID collision.");
                    RemoveTrackedItem(id, previous.Body);
                }
                if (_trackedBodies.ContainsKey(body)) throw new InvalidOperationException("Part already has a transient item identity.");
                if (_spawnLifecycle.IsRetired(id)) return true;
                var item = new SyncedItem { Id = id, Body = body, Path = ScenePath.Of(body.transform),
                    LastPosition = body.transform.position };
                _items.Add(id, item); _trackedBodies[body] = true;
                if (_pendingItemPoses.TryGetValue(id, out var pose))
                {
                    _pendingItemPoses.Remove(id);
                    if (Time.unscaledTime < pose.ExpiresAt) ApplySnapshotPose(item, pose.Position, pose.Rotation);
                }
                SyncEventLog.Record("part-item", id.ToString("X8") + " " + item.Path);
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: native part registration failed: " + e.Message); }
            return true;
        }
    }
}
