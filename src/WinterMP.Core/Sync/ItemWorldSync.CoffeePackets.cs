using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly Dictionary<GameObject, LocalMeat> _localCoffeePackets = new Dictionary<GameObject, LocalMeat>();
        private static bool CoffeePacketIdentity(Rigidbody body, out uint id)
        {
            id = 0; var c = SyncCatalog.Coffee;
            if (body == null || c == null || (body.name != c["packetName"] && body.name != "empty(itemx)"
                && !FactoryItemIdentity.IsNativeId(body.name, c["packetPrefix"]))) return false;
            var use = SausageUse(body.gameObject, c["use"]);
            string native = use?.FsmVariables.FindFsmString(c["packetId"])?.Value ?? "";
            if (!FactoryItemIdentity.IsNativeId(native, c["packetPrefix"])) return false;
            id = CoffeePolicy.PackageId(native); return true;
        }
        private bool TryScanCoffeePacket(Rigidbody body)
        {
            var c = SyncCatalog.Coffee; var session = SessionManager.Instance;
            if (c == null || session == null) return false;
            if (_localCoffeePackets.ContainsKey(body.gameObject)) return true;
            foreach (var b in _coffee.Values) if (b.Body == body) return true;
            if (!CoffeePacketIdentity(body, out uint id)) return body.name == c["packetName"] || FactoryItemIdentity.IsNativeId(body.name, c["packetPrefix"]);
            if (_coffeeFailed) return true;
            var use = SausageUse(body.gameObject, c["use"])!;
            if (!use.Fsm.Started || use.ActiveStateName != c["packetReady"] && use.ActiveStateName != c["packetEmpty"]) return true;
            try
            {
                if (session.IsHost)
                {
                    if (_spawnLifecycle.IsRetired(id)) { use.SendEvent("GARBAGE"); return true; }
                    var b = BindCoffee(use, id, 2, false); TryRegisterConsumableHooks(_items[id]);
                    b.Body.name = b.Ground!.Value < 1 ? "empty(itemx)" : c["packetName"];
                }
                else
                {
                    var local = new LocalMeat { Active = body.gameObject.activeSelf }; _localCoffeePackets.Add(body.gameObject, local);
                    foreach (var f in body.GetComponents<PlayMakerFSM>())
                    { var pause = new FsmSuppressor(); local.FsMs.Add(pause); if (!pause.Suppress(f)) throw new InvalidOperationException("Cannot preserve guest coffee packet."); }
                    body.gameObject.SetActive(false); _trackedBodies[body] = true;
                }
            }
            catch (Exception e) { FailCoffee(e); }
            return true;
        }
        private bool MaterializeCoffeePacket(CoffeeState state)
        {
            var c = SyncCatalog.Coffee!;
            if (_items.TryGetValue(state.ItemId, out var existing) && existing.Body != null)
            {
                var f = CoffeeFsm(existing.Body.transform, c["use"]); var b = BindCoffee(f, state.ItemId, 2, true); PauseCoffee(f); return true;
            }
            if (existing != null) RemoveTrackedItem(state.ItemId, existing.Body);
            var prefab = FindBagSpillTemplate(c["packetName"]);
            if (prefab == null) return false;
            if (prefab.name != c["packetPrefab"]) throw new InvalidOperationException("Wrong coffee packet template.");
            var source = CoffeeFsm(prefab.transform, c["use"]); bool enabled = source.enabled; GameObject clone;
            try { source.enabled = false; clone = (GameObject)UnityEngine.Object.Instantiate(prefab.gameObject, state.Position.ToUnity(), state.Rotation.ToUnity()); }
            finally { source.enabled = enabled; }
            try
            {
                var f = CoffeeFsm(clone.transform, c["use"]); f.enabled = false; clone.name = c["packetName"];
                var body = clone.GetComponent<Rigidbody>(); body.isKinematic = false;
                BindSpawnedBody(body, new ItemSpawn.Entry { NetId = state.ItemId, TemplateName = c["packetName"], Position = state.Position, Rotation = state.Rotation }, 0, Time.unscaledTime);
                BindCoffee(f, state.ItemId, 2, true); clone.SetActive(true); return true;
            }
            catch { foreach (var f in clone.GetComponents<PlayMakerFSM>()) f.enabled = false; UnityEngine.Object.Destroy(clone); throw; }
        }
        private bool TryRetireCoffeePacket(Rigidbody body)
        {
            if (SessionManager.Instance?.IsHost != true || !CoffeePacketIdentity(body, out _)) return false;
            SausageUse(body.gameObject, SyncCatalog.Coffee!["use"])!.SendEvent("GARBAGE"); return true;
        }
        private void ClearCoffeePackets()
        {
            foreach (var b in _coffee.Values)
            {
                if (!b.Replica || b.Body == null) continue;
                b.Fsm.enabled = false; RemoveTrackedItem(b.Id, b.Body); UnityEngine.Object.Destroy(b.Body.gameObject);
            }
            foreach (var pair in _localCoffeePackets)
            {
                if (pair.Key == null) continue;
                _trackedBodies.Remove(pair.Key.GetComponent<Rigidbody>()); pair.Key.SetActive(pair.Value.Active);
                foreach (var pause in pair.Value.FsMs) pause.Restore();
            }
            _localCoffeePackets.Clear();
        }
    }
}
