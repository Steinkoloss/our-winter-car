using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class SausageBinding
        {
            internal Rigidbody Body = null!;
            internal PlayMakerFSM Use = null!;
            internal bool Replica;
            internal SausageState? Last, Received;
            internal float NextSend;
            internal uint Sent;
            internal FsmState? Retirement;
            internal FsmStateAction? RetirementHook;
        }
        private GameObject? _sausagePrefab;
        private bool _sausageFailed;
        private float _sausageTick;
        private uint _looseSausageOrdinal;
        private readonly Dictionary<uint, SausageBinding> _sausages = new Dictionary<uint, SausageBinding>();
        private readonly Dictionary<uint, SausageState> _pendingSausages = new Dictionary<uint, SausageState>();
        private readonly Dictionary<GameObject, LocalMeat> _localSausages = new Dictionary<GameObject, LocalMeat>();
        private readonly string[] _sausageNames = new string[4];
        private Material? _sausageFreshMaterial, _sausageBurntMaterial;

        private static PlayMakerFSM? SausageUse(GameObject obj, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var fsm in obj.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == name) { if (found != null) throw new InvalidOperationException("Duplicate sausage FSM."); found = fsm; }
            return found;
        }
        private void ValidateSausagePrefab(GameObject prefab, SausagesData c)
        {
            if (_sausagePrefab == prefab) return;
            var use = SausageUse(prefab, c["use"]); var fire = SausageUse(prefab, c["fire"]);
            if (use == null || fire == null || prefab.GetComponentsInChildren<PlayMakerFSM>(true).Length != 2)
                throw new InvalidOperationException("Native sausage prefab FSMs changed.");
            if (!use.Fsm.Initialized) use.Fsm.Init(use);
            if (use.FsmVariables.FindFsmFloat(c["condition"]) == null || use.FsmVariables.FindFsmBool(c["grilled"]) == null
                || use.FsmVariables.FindFsmGameObject(c["owner"]) == null)
                throw new InvalidOperationException("Native sausage food variables changed.");
            PackageStateActions(use, c["wait"], "SetBoolValue", "MousePickEvent", "Wait");
            PackageStateActions(use, c["button"], "MousePickEvent", "SetBoolValue", "GetButtonDown");
            PackageStateActions(use, c["eat"], "BoolTest", "MasterAudioPlaySound", "DestroyComponent", "FloatAdd", "AddToFsmInt", "SetBoolValue", "BoolTest");
            PackageStateActions(use, c["freshEat"], "FloatAdd", "FloatAdd");
            PackageStateActions(use, c["grilledEat"], "FloatAdd", "FloatAdd", "FloatAdd");
            PackageStateActions(use, c["destroy"], "DestroySelf");
            string[] kinds = { "raw", "cooked", "charred", "spoiled" };
            for (int n = 0; n < kinds.Length; n++)
            {
                var state = FsmHook.FindState(use, c[kinds[n]]) ?? throw new InvalidOperationException("Native sausage appearance missing.");
                foreach (var action in state.Actions)
                {
                    if (!action.Enabled) throw new InvalidOperationException("Disabled sausage appearance action.");
                    if (action.GetType().Name == "SetName") _sausageNames[n] = PackageField<FsmString>(action, "name")?.Value ?? "";
                    if (n == 2 && action.GetType().Name == "SetMaterial") _sausageBurntMaterial = PackageField<FsmMaterial>(action, "material")?.Value;
                }
                if (string.IsNullOrEmpty(_sausageNames[n])) throw new InvalidOperationException("Native sausage name missing.");
            }
            var fresh = prefab.transform.Find(c["freshMesh"]); var grilled = prefab.transform.Find(c["grilledMesh"]);
            if (fresh == null || grilled == null || fresh.GetComponent<Renderer>() == null || _sausageBurntMaterial == null)
                throw new InvalidOperationException("Native sausage meshes changed.");
            _sausageFreshMaterial = fresh.GetComponent<Renderer>().sharedMaterial;
        }
        private bool TryScanSausage(Rigidbody body)
        {
            var c = SyncCatalog.Sausages; var session = SessionManager.Instance;
            if (c == null || session == null || (Array.IndexOf(_sausageNames, body.name) < 0 && body.name != c["prefabName"] + "(Clone)")) return false;
            // The island NPC has a different prefab with the same display name.
            if (SausageUse(body.gameObject, c["fire"]) == null) return false;
            foreach (var binding in _sausages.Values) if (binding.Body == body) return true;
            if (_sausageFailed || _sausagePrefab == null || _trackedBodies.ContainsKey(body)) return true;
            try
            {
                if (session.IsHost)
                {
                    uint id;
                    do { id = StableHash.Fnv1a32("sausage:existing:" + ++_looseSausageOrdinal); }
                    while (id == 0 || _items.ContainsKey(id) || _spawnLifecycle.IsRetired(id));
                    RegisterSausage(body, id);
                }
                else if (!_localSausages.ContainsKey(body.gameObject))
                {
                    var local = new LocalMeat { Active = body.gameObject.activeSelf }; _localSausages.Add(body.gameObject, local);
                    foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                    {
                        var pause = new FsmSuppressor(); local.FsMs.Add(pause);
                        if (!pause.Suppress(fsm)) throw new InvalidOperationException("Cannot preserve guest loose sausage.");
                    }
                    body.gameObject.SetActive(false); _trackedBodies[body] = true;
                }
            }
            catch (Exception e) { FailSausages(e); }
            return true;
        }
        private void RegisterSausage(Rigidbody body, uint id)
        {
            if (id == 0 || _items.ContainsKey(id) || _sausages.ContainsKey(id) || _spawnLifecycle.IsRetired(id)) throw new InvalidOperationException("Sausage identity collision.");
            var use = SausageUse(body.gameObject, SyncCatalog.Sausages!["use"]) ?? throw new InvalidOperationException("Sausage Use missing.");
            if (!use.Fsm.Initialized) use.Fsm.Init(use);
            use.FsmVariables.FindFsmGameObject(SyncCatalog.Sausages["owner"]).Value = body.gameObject;
            var binding = new SausageBinding { Body = body, Use = use }; _sausages.Add(id, binding);
            BindFactoryBodyAsOwner(body, id, Time.unscaledTime); _trackedBodies[body] = true;
            HookSausageRetirement(id, binding);
        }
        private void HookSausageRetirement(uint id, SausageBinding binding)
        {
            var state = FsmHook.FindState(binding.Use, SyncCatalog.Sausages!["destroy"]) ?? throw new InvalidOperationException("Sausage retirement state missing.");
            var hook = new FsmHookAction(() => { if (!_bridge.ApplyingRemote) AnnounceItemDespawn(id, "sausage consumed"); });
            var actions = new List<FsmStateAction>(state.Actions); actions.Insert(0, hook); state.Actions = actions.ToArray();
            binding.Retirement = state; binding.RetirementHook = hook;
        }
        internal SausageState? BuildSausageState(uint id)
        {
            if (_sausageFailed || SessionManager.Instance?.IsHost != true || _spawnLifecycle.IsRetired(id)
                || !_sausages.TryGetValue(id, out var b) || b.Body == null || b.Replica || !_items.ContainsKey(id)) return null;
            var c = SyncCatalog.Sausages!;
            int kind = Array.IndexOf(_sausageNames, b.Body.name);
            if (kind < 0 || !b.Use.Fsm.Started || b.Use.ActiveStateName == c["destroy"]) return null;
            var s = new SausageState { ItemId = id, Revision = b.Last?.Revision ?? 1, Kind = (byte)kind,
                Grilled = b.Use.FsmVariables.FindFsmBool(c["grilled"]).Value,
                Condition = Mathf.Clamp(b.Use.FsmVariables.FindFsmFloat(c["condition"]).Value, 0, 100),
                Position = b.Body.position.ToNet(), Rotation = b.Body.rotation.ToNet() };
            if (b.Last != null && !SausagePolicy.SameFood(b.Last, s)) { if (++s.Revision == 0) ++s.Revision; }
            if (!SausagePolicy.Valid(s)) { FailSausages(new InvalidOperationException("Invalid host sausage state.")); return null; }
            b.Last = s; return s;
        }
        internal IEnumerable<SausageState> BuildSausageStates()
        {
            foreach (uint id in _sausages.Keys) { var state = BuildSausageState(id); if (state != null) yield return state; }
        }
        internal void OnSausageState(SausageState state)
        {
            if (_sausageFailed || SessionManager.Instance?.IsHost != false || !SausagePolicy.Valid(state) || _spawnLifecycle.IsRetired(state.ItemId)) return;
            var old = _pendingSausages.TryGetValue(state.ItemId, out var pending) ? pending : (_sausages.TryGetValue(state.ItemId, out var b) ? b.Received : null);
            if (old != null && state.Revision != old.Revision && !TractorTrailerPolicy.Newer(state.Revision, old.Revision)) return;
            if (old != null && state.Revision == old.Revision && !SausagePolicy.SameFood(state, old)) return;
            if (!_pendingSausages.ContainsKey(state.ItemId) && _pendingSausages.Count >= 1024) return;
            _snapshotSeenIds.Add(state.ItemId); _pendingSausages[state.ItemId] = state;
        }
        private void ProcessSausages(SessionManager session)
        {
            RefreshSausages();
            if (_sausageFailed || _sausagePrefab == null || Time.unscaledTime < _sausageTick) return;
            _sausageTick = Time.unscaledTime + .25f;
            try
            {
                if (session.IsHost)
                {
                    foreach (var s in BuildSausageStates())
                    {
                        var b = _sausages[s.ItemId];
                        if (b.Sent == s.Revision && Time.unscaledTime < b.NextSend) continue;
                        session.SendWorldMessage(s, Channel.ReliableOrdered); b.Sent = s.Revision; b.NextSend = Time.unscaledTime + 5;
                    }
                }
                else foreach (uint id in new List<uint>(_pendingSausages.Keys))
                {
                    var state = _pendingSausages[id];
                    if (!_spawnLifecycle.IsRetired(id))
                    {
                        if (_sausages.TryGetValue(id, out var b) && b.Body != null) ApplySausageFood(b, state);
                        else MaterializeSausage(state);
                    }
                    _pendingSausages.Remove(id);
                }
            }
            catch (Exception e) { FailSausages(e); }
        }
        private void FailSausages(Exception e)
        {
            if (_sausageFailed) return;
            _sausageFailed = true;
            WinterMPPlugin.Log.LogWarning("Sausage sync disabled: " + e.Message); SyncEventLog.Record("sausage-disabled", e.Message);
        }
        private void ClearSausages()
        {
            foreach (var pair in _sausages)
            {
                var b = pair.Value;
                if (b.Retirement != null && b.RetirementHook != null) RemoveReplacementHook(b.Retirement, b.RetirementHook);
                if (_items.TryGetValue(pair.Key, out var item) && item.Body == b.Body)
                { if (b.Body != null && item.KinematicSaved) b.Body.isKinematic = item.OriginalKinematic; RemoveTrackedItem(pair.Key, b.Body); }
                if (b.Replica && b.Use != null) { b.Use.enabled = false; UnityEngine.Object.Destroy(b.Use.gameObject); }
            }
            ClearSausagePackages();
            foreach (var pair in _localSausages)
            {
                if (pair.Key == null) continue;
                _trackedBodies.Remove(pair.Key.GetComponent<Rigidbody>()); pair.Key.SetActive(pair.Value.Active);
                foreach (var pause in pair.Value.FsMs) pause.Restore();
            }
            foreach (var source in _sausageSources.Values) foreach (var body in source.Outputs) _trackedBodies.Remove(body);
            for (int n = _sausageRestore.Count - 1; n >= 0; n--) _sausageRestore[n]();
            _sausages.Clear(); _pendingSausages.Clear(); _localSausages.Clear(); _sausageSources.Clear(); _sausageRestore.Clear();
            _openedSausagePackages.Clear(); _sausageSequences.Clear(); _sausageSequence = 0; _looseSausageOrdinal = 0;
            _sausagePrefab = null; _sausageFailed = false; _sausageDiscovery = 0; _sausageTick = 0;
        }
    }
}
