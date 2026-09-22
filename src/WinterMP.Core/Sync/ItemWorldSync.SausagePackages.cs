using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly Dictionary<uint, GameObject> _sausagePackageReplicas = new Dictionary<uint, GameObject>();
        private readonly Dictionary<GameObject, LocalMeat> _localSausagePackages = new Dictionary<GameObject, LocalMeat>();
        private static bool TrySausagePackageId(Rigidbody body, out uint id)
        {
            id = 0; var c = SyncCatalog.Sausages;
            if (body == null || c == null || (body.name != c["packageName"] && body.name != "spoiled " + c["packageName"]
                && !FactoryItemIdentity.IsNativeId(body.name, c["packagePrefix"]))) return false;
            var use = SausageUse(body.gameObject, c["use"]);
            string nativeId = use?.FsmVariables.FindFsmString(c["packageId"])?.Value ?? "";
            if (!FactoryItemIdentity.IsNativeId(nativeId, c["packagePrefix"])) return false;
            id = SausagePolicy.PackageId(nativeId); return true;
        }
        private bool TryScanSausagePackage(Rigidbody body)
        {
            var c = SyncCatalog.Sausages; var session = SessionManager.Instance;
            if (c == null || session == null) return false;
            bool candidate = body.name == c["packageName"] || body.name == "spoiled " + c["packageName"]
                || FactoryItemIdentity.IsNativeId(body.name, c["packagePrefix"]);
            if (!candidate) return false;
            if (_trackedBodies.ContainsKey(body)) return true;
            var use = SausageUse(body.gameObject, c["use"]);
            if (use == null || !use.Fsm.Started || !TrySausagePackageId(body, out uint id)) return true;
            try
            {
                if (session.IsHost)
                {
                    if (_spawnLifecycle.IsRetired(id)) { use.SendEvent("GARBAGE"); return true; }
                    if (_items.TryGetValue(id, out var old))
                    { if (old.Body != body) throw new InvalidOperationException("Sausage package native ID collision."); return true; }
                    BindFactoryBodyAsOwner(body, id, Time.unscaledTime); _trackedBodies[body] = true;
                }
                else if (!_localSausagePackages.ContainsKey(body.gameObject))
                {
                    var local = new LocalMeat { Active = body.gameObject.activeSelf }; _localSausagePackages.Add(body.gameObject, local);
                    foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                    {
                        var pause = new FsmSuppressor(); local.FsMs.Add(pause);
                        if (!pause.Suppress(fsm)) throw new InvalidOperationException("Cannot preserve guest sausage package.");
                    }
                    body.gameObject.SetActive(false); _trackedBodies[body] = true;
                }
            }
            catch (Exception e) { FailSausages(e); }
            return true;
        }
        private bool TryRetireSausagePackage(Rigidbody body)
        {
            if (SessionManager.Instance?.IsHost != true || !TrySausagePackageId(body, out _)) return false;
            var use = SausageUse(body.gameObject, SyncCatalog.Sausages!["use"]);
            if (use == null || !use.Fsm.Started || !use.enabled) throw new InvalidOperationException("Sausage package cannot retain its native save tombstone.");
            use.SendEvent("GARBAGE"); return true;
        }
        private void TrackSausagePackageReplica(uint id, Rigidbody body, string template)
        {
            var c = SyncCatalog.Sausages;
            if (SessionManager.Instance?.IsHost == false && c != null
                && (template == c["packageName"] || template == "spoiled " + c["packageName"])) _sausagePackageReplicas[id] = body.gameObject;
        }
        private void ClearSausagePackages()
        {
            foreach (var pair in _localSausagePackages)
            {
                if (pair.Key == null) continue;
                _trackedBodies.Remove(pair.Key.GetComponent<Rigidbody>()); pair.Key.SetActive(pair.Value.Active);
                foreach (var pause in pair.Value.FsMs) pause.Restore();
            }
            foreach (var pair in _sausagePackageReplicas)
            {
                if (pair.Value == null) continue;
                var body = pair.Value.GetComponent<Rigidbody>();
                if (_items.TryGetValue(pair.Key, out var item) && item.Body == body) RemoveTrackedItem(pair.Key, body);
                foreach (var fsm in pair.Value.GetComponents<PlayMakerFSM>()) fsm.enabled = false;
                UnityEngine.Object.Destroy(pair.Value);
            }
            _sausagePackageReplicas.Clear(); _localSausagePackages.Clear();
        }
    }
}
