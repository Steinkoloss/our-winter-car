using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        internal int ScanItems()
        {
            int initialCount = _items.Count;
            RefreshTrophyFactories();
            RefreshPackageFactories();
            RefreshReplacementFactories();
            ScanNativeParts();
            // Collect new candidates first so same-path clones (six sausages at the
            // scene root...) get deterministic ordinals from one consistent batch.
            var newcomers = new List<SyncedItem>();
            var bodies = ScenePath.ScanRigidbodies();
            foreach (var obj in bodies)
            {
                var body = obj as Rigidbody;
                if (body == null) continue;

                try
                {
                    if (!body.gameObject.activeInHierarchy) continue;
                    if (TryScanTrophy(body) || TryScanPackage(body) || TryScanNativePart(body) || _trackedBodies.ContainsKey(body)) continue;

                    bool isVehicle = SyncCatalog.IsVehicleRoot(body);
                    bool isItem = !isVehicle && SyncCatalog.IsPickableRigidbody(body);
                    if (!isItem && !isVehicle) continue;

                    newcomers.Add(new SyncedItem
                    {
                        Body = body,
                        Path = ScenePath.Of(body.transform),
                        LastPosition = body.transform.position,
                        IsVehicle = isVehicle,
                    });
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"WorldSync: skipped rigidbody during scan: {e.Message}");
                }
            }

            int nativeAdded = Math.Max(0, _items.Count - initialCount);
            if (newcomers.Count == 0) return nativeAdded;

            // Same-path groups: ordinal by initial position (same save => same order
            // on every machine). Quantized to 1 mm so float noise can't flip ties.
            var byPath = new Dictionary<string, List<SyncedItem>>();
            foreach (var item in newcomers)
            {
                if (!byPath.TryGetValue(item.Path, out var group))
                    byPath[item.Path] = group = new List<SyncedItem>();
                group.Add(item);
            }

            int added = 0;
            foreach (var pair in byPath)
            {
                var group = pair.Value;
                if (group.Count > 1)
                    group.Sort(CompareByInitialPosition);

                for (int i = 0; i < group.Count; i++)
                {
                    var item = group[i];
                    string prefix = item.IsVehicle ? "vehicle:" : "item:";
                    string idSource = group.Count > 1 ? prefix + item.Path + "#" + i : prefix + item.Path;
                    item.Id = StableHash.Fnv1a32(idSource);
                    _trackedBodies[item.Body] = true;

                    if (_spawnLifecycle.IsRetired(item.Id))
                    {
                        _trackedBodies.Remove(item.Body);
                        UnityEngine.Object.Destroy(item.Body.gameObject);
                        WinterMPPlugin.Log.LogInfo(
                            $"WorldSync: item '{item.Path}' removed by session retirement.");
                        continue;
                    }

                    if (_items.ContainsKey(item.Id))
                    {
                        // Late-discovered clone of an existing group — its ordinal may
                        // disagree across machines, so it is safer not to sync it.
                        WinterMPPlugin.Log.LogWarning($"WorldSync: item id collision, not syncing '{item.Path}'.");
                        continue;
                    }

                    _items[item.Id] = item;
                    if (item.IsVehicle)
                        WinterMPPlugin.Log.LogInfo($"WorldSync: vehicle registered: '{item.Path}' ({item.Body.mass:0} kg).");
                    else
                        TryRegisterConsumableHooks(item);
                    added++;

                    // A join snapshot may have arrived before this item was scanned.
                    if (_pendingItemPoses.TryGetValue(item.Id, out var pose))
                    {
                        _pendingItemPoses.Remove(item.Id);
                        if (Time.unscaledTime < pose.ExpiresAt)
                            ApplySnapshotPose(item, pose.Position, pose.Rotation);
                    }
                }
            }

            return added + nativeAdded;
        }

        private void TryRegisterConsumableHooks(SyncedItem item)
        {
            if (item.Body == null || item.IsVehicle) return;

            foreach (var fsm in item.Body.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Use" || _bridge.HookedFsms.ContainsKey(fsm)) continue;

                var despawnStates = new List<string>();
                SyncCatalog.CollectConsumableDespawnStates(fsm, despawnStates);
                if (despawnStates.Count == 0) continue;

                bool hooked = false;
                uint itemId = item.Id;
                foreach (string state in despawnStates)
                {
                    string captured = state;
                    if (!FsmHook.OnStateEnter(fsm, state, () => OnItemConsumed(itemId, captured))) continue;
                    hooked = true;
                }

                if (hooked)
                    _bridge.HookedFsms[fsm] = true;
            }
        }

        private void OnItemConsumed(uint itemId, string stateName)
        {
            if (_bridge.ApplyingRemote) return;
            AnnounceItemDespawn(itemId, $"consumed ({stateName})");
        }

        private static int CompareByInitialPosition(SyncedItem a, SyncedItem b)
        {
            long ax = Quantize(a.LastPosition.x), bx = Quantize(b.LastPosition.x);
            if (ax != bx) return ax.CompareTo(bx);
            long ay = Quantize(a.LastPosition.y), by = Quantize(b.LastPosition.y);
            if (ay != by) return ay.CompareTo(by);
            return Quantize(a.LastPosition.z).CompareTo(Quantize(b.LastPosition.z));
        }

        private static long Quantize(float value) => (long)Math.Round(value * 1000f);
    }
}
