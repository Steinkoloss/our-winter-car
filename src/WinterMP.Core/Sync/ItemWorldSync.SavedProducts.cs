using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private IEnumerable<ItemSpawn> BuildSavedProductManifests()
        {
            var session = SessionManager.Instance;
            var catalog = SyncCatalog.ShoppingBags;
            if (session == null || !session.IsHost || catalog == null) yield break;
            bool templatesReady = true;
            if (Time.unscaledTime >= _nextBagSpillDiscovery)
            {
                _nextBagSpillDiscovery = Time.unscaledTime + 1f;
                try { PrepareBagSpillCapture(); }
                catch (Exception e)
                {
                    templatesReady = false;
                    WinterMPPlugin.Log.LogWarning("WorldSync: saved product replay waiting for native templates: " + e.Message);
                }
            }
            if (!templatesReady) yield break;

            // Live spills already retain manifests. After a restart only the native
            // saved objects remain, so reconstruct their creation descriptors from
            // the same validated product templates used for bag openings.
            var described = new HashSet<uint>();
            foreach (var manifest in _hostSpawnManifests.Values)
                foreach (var entry in manifest.Items) described.Add(entry.NetId);

            foreach (var pair in _items)
            {
                var item = pair.Value;
                var body = item.Body;
                if (body == null || item.IsVehicle || !body.gameObject.activeInHierarchy
                    || described.Contains(pair.Key) || _spawnLifecycle.IsRetired(pair.Key)) continue;
                if (IsNativeAtf(body) || IsMotorOilBody(body) || SupplyRule(body, out _) != null) continue;
                string name = body.gameObject.name;
                var milk = SyncCatalog.MilkCondition;
                if (milk != null && name == milk.SpoiledName) name = milk.ItemName;
                if (!_bagSpillTemplates.TryGetValue(name, out var template) || template == null) continue;

                PlayMakerFSM? use = null;
                foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == catalog["itemFsm"]) { use = fsm; break; }
                if (use == null || !use.Fsm.Initialized || !use.Fsm.Started
                    || string.IsNullOrEmpty(use.FsmVariables.FindFsmString(catalog["itemIdVariable"])?.Value)
                    || use.FsmVariables.FindFsmBool(catalog["consumedVariable"])?.Value == true) continue;

                // Epoch zero is reserved for this observed saved item. Normal bag
                // spills start at one; replay deduplicates each live body by NetId.
                var replay = new ItemSpawn { ContainerNetId = pair.Key, Epoch = 0,
                    OwnerPlayerId = session.LocalPlayerId, StateName = "saved-product", Flags = ItemSpawn.FlagReplay };
                replay.Items.Add(new ItemSpawn.Entry { NetId = pair.Key, TemplateName = name,
                    Position = body.position.ToNet(), Rotation = body.rotation.ToNet() });
                yield return replay;
            }
        }
    }
}
