using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class HeatSourceSync
    {
        private static GameObject? FindCabinLogPrefab(WoodstoveFuelData c)
        {
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || fsm.FsmName != c["loggingFsm"] || ScenePath.Of(fsm.transform) != c["loggingPath"]) continue;
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                var create = FsmHook.FindState(fsm, c["createState"]);
                if (create == null || !create.IsInitialized || create.Actions.Length <= 3) continue;
                var action = FsmHook.NativeAction(create, 3);
                if (action == null || action.GetType().Name != "CreateObject"
                    || CabinField<FsmGameObject>(action, "storeObject").Name != "Log")
                    throw new InvalidOperationException("Cabin log factory signature changed.");
                var prefab = CabinField<FsmGameObject>(action, "gameObject").Value;
                if (prefab == null || prefab.name != "log" || prefab.GetComponent<Rigidbody>() == null
                    || prefab.transform.Find("log(Clone)") == null
                    || prefab.transform.Find("log(Clone)").GetComponent<Rigidbody>() == null)
                    throw new InvalidOperationException("Cabin split-log prefab changed.");
                return prefab;
            }
            return null;
        }
        private GameObject Template(byte shape) => shape == 1 ? _logPrefab!
            : _logPrefab!.transform.Find("log(Clone)").gameObject;
        private byte ShapeOf(GameObject piece)
        {
            byte found = 0;
            for (byte shape = 1; shape <= 2; shape++)
            {
                var template = Template(shape);
                var mesh = piece.GetComponent<MeshFilter>(); var expected = template.GetComponent<MeshFilter>();
                var collider = piece.GetComponent<BoxCollider>(); var expectedCollider = template.GetComponent<BoxCollider>();
                if (mesh == null || expected == null || mesh.sharedMesh != expected.sharedMesh
                    || collider == null || expectedCollider == null || collider.size != expectedCollider.size
                    || collider.center != expectedCollider.center) continue;
                if (found != 0) return 0; // Never choose an arbitrary physical half when signatures collide.
                found = shape;
            }
            return found;
        }
        private bool KnownCabinPiece(GameObject piece)
        {
            foreach (var wood in _cabinWood.Values) if (wood.Piece == piece) return true;
            return false;
        }
        private void ScanCabinWood()
        {
            if (_cabinWood.Count >= WoodstoveFuelUpdate.MaxResources) return;
            var items = WorldSyncManager.Instance!.ItemSync;
            foreach (var obj in ScenePath.ScanRigidbodies())
            {
                var body = obj as Rigidbody;
                if (body == null || !body.gameObject.activeInHierarchy || body.name != "firewood(Clone)"
                    || body.transform.parent != null || body.tag != "PART" || body.GetComponent<FixedJoint>() != null
                    || (body.position - _cabin!.WoodTrigger!.transform.position).sqrMagnitude > 400f
                    || KnownCabinPiece(body.gameObject)) continue;
                byte shape = ShapeOf(body.gameObject); if (shape == 0) continue;
                uint id;
                do { id = NewCabinId(); } while (_cabinWood.ContainsKey(id) || items.Items.ContainsKey(id));
                var item = items.BindCabinWood(id, body);
                _cabinWood.Add(id, new CabinWood { Id = id, Shape = shape, Piece = body.gameObject, Item = item });
                _nextCabinState = 0;
                if (_cabinWood.Count >= WoodstoveFuelUpdate.MaxResources) break;
            }
        }
        private void MaterializeCabinWood()
        {
            if (_fuelClient?.Current == null || _logPrefab == null) return;
            var retired = _fuelClient.Current.ConsumedResources;
            foreach (var pair in new List<KeyValuePair<uint, WoodstoveFuelUpdate>>(_pendingWood))
            {
                uint id = pair.Key; var update = pair.Value;
                if (Array.BinarySearch(retired, id) >= 0) { _pendingWood.Remove(id); continue; }
                if (_cabinWood.TryGetValue(id, out var previous))
                {
                    if (previous.Shape != update.Shape) throw new InvalidOperationException("Cabin resource shape changed.");
                    _pendingWood.Remove(id); continue;
                }
                // Same-world discovery can bind an existing exact piece, but ambiguity
                // never becomes a path/ordinal identity. Otherwise use the native prefab.
                GameObject? piece = null;
                foreach (var obj in ScenePath.ScanRigidbodies())
                {
                    var body = obj as Rigidbody;
                    if (body == null || !body.gameObject.activeInHierarchy || body.name != "firewood(Clone)"
                        || body.transform.parent != null || body.tag != "PART" || KnownCabinPiece(body.gameObject)
                        || (body.position - update.Position.ToUnity()).sqrMagnitude > .0001f || ShapeOf(body.gameObject) != update.Shape) continue;
                    if (piece != null) throw new InvalidOperationException("Ambiguous cabin resource admission.");
                    piece = body.gameObject;
                }
                bool created = piece == null;
                if (piece == null) piece = CloneCabinPiece(update);
                var item = WorldSyncManager.Instance!.ItemSync.BindCabinWood(id, piece.GetComponent<Rigidbody>());
                _cabinWood.Add(id, new CabinWood { Id = id, Shape = update.Shape, Piece = piece, Item = item, Replica = created });
                _pendingWood.Remove(id);
            }
        }
        private GameObject CloneCabinPiece(WoodstoveFuelUpdate update)
        {
            var template = Template(update.Shape);
            var fsms = template.GetComponentsInChildren<PlayMakerFSM>(true);
            var enabled = new bool[fsms.Length]; GameObject clone;
            try
            {
                // The replica is already a split piece. Never replay split statistics,
                // stress or local creation prerequisites while materializing it.
                for (int i = 0; i < fsms.Length; i++) { enabled[i] = fsms[i].enabled; fsms[i].enabled = false; }
                clone = (GameObject)UnityEngine.Object.Instantiate(template, update.Position.ToUnity(), update.Rotation.ToUnity());
            }
            finally { for (int i = 0; i < fsms.Length; i++) fsms[i].enabled = enabled[i]; }
            try
            {
                clone.SetActive(false);
                foreach (var fsm in clone.GetComponentsInChildren<PlayMakerFSM>(true))
                { fsm.enabled = false; UnityEngine.Object.Destroy(fsm); }
                foreach (var joint in clone.GetComponentsInChildren<FixedJoint>(true)) UnityEngine.Object.Destroy(joint);
                foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true))
                    if (body.gameObject != clone) { body.gameObject.SetActive(false); UnityEngine.Object.Destroy(body.gameObject); }
                clone.name = "firewood(Clone)"; clone.tag = "PART"; clone.transform.parent = null;
                clone.GetComponent<Rigidbody>().isKinematic = false; clone.SetActive(true);
                return clone;
            }
            catch { UnityEngine.Object.Destroy(clone); throw; }
        }
    }
}
