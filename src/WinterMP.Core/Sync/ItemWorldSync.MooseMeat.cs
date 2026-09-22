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
        private sealed class MeatBinding
        {
            internal Rigidbody Body = null!;
            internal PlayMakerFSM Use = null!;
            internal string NativeId = "";
            internal bool Replica;
            internal MooseMeatState? Observed, Received;
            internal uint SentRevision;
            internal bool Sent;
            internal float NextSend;
        }
        private sealed class LocalMeat
        {
            internal bool Active;
            internal readonly List<FsmSuppressor> FsMs = new List<FsmSuppressor>();
        }
        private PlayMakerFSM? _meatFactory;
        private GameObject? _meatPrefab;
        private bool _meatFailed;
        private float _nextMeat;
        private readonly FsmSuppressor _meatFactoryPause = new FsmSuppressor();
        private readonly Dictionary<FsmState, FsmStateAction> _meatHooks = new Dictionary<FsmState, FsmStateAction>();
        private readonly Dictionary<Rigidbody, string> _meatOutputs = new Dictionary<Rigidbody, string>();
        private readonly Dictionary<uint, MeatBinding> _meat = new Dictionary<uint, MeatBinding>();
        private readonly Dictionary<uint, MooseMeatState> _pendingMeat = new Dictionary<uint, MooseMeatState>();
        private readonly Dictionary<GameObject, LocalMeat> _localMeat = new Dictionary<GameObject, LocalMeat>();
        private readonly string[] _meatNames = new string[5];
        private readonly Material?[] _meatMaterials = new Material?[5];

        private void RefreshMeatFactory()
        {
            var c = SyncCatalog.MooseMeat;
            if (c == null || _meatFailed || _meatFactory != null) return;
            try
            {
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != c["factoryFsm"] || ScenePath.Of(fsm.transform) != c["path"]) continue;
                    var prefab = fsm.FsmVariables.FindFsmGameObject(c["prefab"])?.Value;
                    if (prefab == null || prefab.name != c["prefabName"] || prefab.GetComponent<Rigidbody>() == null
                        || fsm.FsmVariables.FindFsmGameObject(c["output"]) == null || fsm.FsmVariables.FindFsmString(c["factoryId"]) == null)
                        throw new InvalidOperationException("Native meat factory references changed.");
                    var fsms = prefab.GetComponentsInChildren<PlayMakerFSM>(true);
                    if (fsms.Length != 2) throw new InvalidOperationException("Native meat prefab FSMs changed.");
                    PlayMakerFSM? use = null;
                    foreach (var member in fsms)
                    {
                        if (member.gameObject != prefab || (member.FsmName != c["use"] && member.FsmName != c["fire"]))
                            throw new InvalidOperationException("Native meat prefab topology changed.");
                        if (member.FsmName == c["use"]) use = member;
                    }
                    if (use == null) throw new InvalidOperationException("Native meat Use missing.");
                    if (!use.Fsm.Initialized) use.Fsm.Init(use);
                    if (use.FsmVariables.FindFsmString(c["id"]) == null || use.FsmVariables.FindFsmFloat(c["condition"]) == null
                        || use.FsmVariables.FindFsmInt(c["type"]) == null || use.FsmVariables.FindFsmGameObject(c["owner"]) == null)
                        throw new InvalidOperationException("Native meat variables changed.");
                    var renderer = prefab.GetComponent<Renderer>();
                    if (renderer == null) throw new InvalidOperationException("Native meat renderer missing.");
                    string[] kinds = { "raw", "rotten", "grilled", "charred", "spoiled" };
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        var state = FsmHook.FindState(use, c[kinds[i]]) ?? throw new InvalidOperationException("Native meat presentation missing.");
                        _meatMaterials[i] = renderer.sharedMaterial;
                        foreach (var action in state.Actions)
                        {
                            if (!action.Enabled) continue;
                            if (action.GetType().Name == "SetName") _meatNames[i] = PackageField<FsmString>(action, "name")?.Value ?? "";
                            if (action.GetType().Name == "SetMaterial") _meatMaterials[i] = PackageField<FsmMaterial>(action, "material")?.Value;
                        }
                        if (string.IsNullOrEmpty(_meatNames[i]) || _meatMaterials[i] == null)
                            throw new InvalidOperationException("Native meat display binding changed.");
                    }
                    _meatMaterials[4] = _meatMaterials[2]; // Spoiled grilled meat retains its cooked material.
                    TrophyState(use, c["waitPlayer"], "SetBoolValue", "MousePickEvent", "Wait");
                    TrophyState(use, c["waitButton"], "MousePickEvent", "SetBoolValue", "GetButtonDown");
                    TrophyState(use, c["eat"], "BoolTest", "DestroyComponent", "SetIntValue", "SetBoolValue", "MasterAudioPlaySound", "FloatAdd", "FloatAdd", "FloatSubtract", "FloatSubtract");
                    TrophyState(use, c["destroy"], "SetIntValue", "SetParent", "DestroyComponent", "DestroyComponent", "DestroyComponent", "SetPosition");
                    TrophyState(fsm, c["factoryIdle"]);
                    foreach (string key in new[] { "create", "loadCreate" })
                    {
                        var state = FsmHook.FindState(fsm, c[key]) ?? throw new InvalidOperationException("Native meat creation missing.");
                        string[] types = key == "create" ? new[] { "IntAdd", "CreateObject", "SetParent", "SetRandomRotation", "ConvertIntToString", "BuildString", "SetName" }
                            : new[] { "CreateObject", "SetName" };
                        if (state.Actions.Length != types.Length) throw new InvalidOperationException("Native meat creation changed.");
                        for (int i = 0; i < types.Length; i++)
                            if (state.Actions[i].GetType().Name != types[i] || state.Actions[i].Enabled != !(key == "create" && i == 2))
                                throw new InvalidOperationException("Native meat creation actions changed.");
                        var create = state.Actions[key == "create" ? 1 : 0];
                        if (PackageField<FsmGameObject>(create, "gameObject")?.Name != c["prefab"]
                            || PackageField<FsmGameObject>(create, "storeObject")?.Name != c["output"])
                            throw new InvalidOperationException("Native meat direct output changed.");
                        var hook = new FsmHookAction(CaptureMeatOutput);
                        _meatHooks.Add(state, hook);
                        var actions = new List<FsmStateAction>(state.Actions); actions.Add(hook); state.Actions = actions.ToArray();
                    }
                    _meatFactory = fsm; _meatPrefab = prefab;
                    SyncEventLog.Record("meat-bind", c["path"]); return;
                }
            }
            catch (Exception e) { FailMeat(e); }
        }

        private void CaptureMeatOutput()
        {
            if (_meatFailed || _meatFactory == null) return;
            try
            {
                var c = SyncCatalog.MooseMeat!;
                var body = _meatFactory.FsmVariables.FindFsmGameObject(c["output"]).Value?.GetComponent<Rigidbody>();
                string id = _meatFactory.FsmVariables.FindFsmString(c["factoryId"]).Value;
                if (body == null || !FactoryItemIdentity.IsNativeId(id, c["prefix"])) throw new InvalidOperationException("Invalid native meat output.");
                _meatOutputs[body] = id; _trackedBodies[body] = true;
            }
            catch (Exception e) { FailMeat(e); }
        }

        private bool TryScanMeat(Rigidbody body)
        {
            var c = SyncCatalog.MooseMeat; var session = SessionManager.Instance;
            if (c == null || session == null) return false;
            string name = body.name;
            bool candidate = name == c["prefabName"] + "(Clone)" || FactoryItemIdentity.IsNativeId(name, c["prefix"])
                || Array.IndexOf(_meatNames, name) >= 0 || _meatOutputs.ContainsKey(body);
            if (!candidate) return false;
            if (_meatFailed || _meatFactory == null) return true;
            try
            {
                PlayMakerFSM? use = null;
                foreach (var fsm in body.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == c["use"]) use = fsm;
                if (use == null || !use.Fsm.Started || Array.IndexOf(_meatNames, name) < 0) return true;
                string id = use.FsmVariables.FindFsmString(c["id"]).Value;
                if (!FactoryItemIdentity.IsNativeId(id, c["prefix"])) return true;
                if (_meatOutputs.TryGetValue(body, out var captured) && captured != id) throw new InvalidOperationException("Native meat changed identity during initialization.");
                uint netId = FactoryItemIdentity.ItemId(c.FactoryId, id);
                if (_meat.TryGetValue(netId, out var existing) && existing.Body == body) return true;
                if (!session.IsHost)
                {
                    if (!_localMeat.ContainsKey(body.gameObject))
                    {
                        var local = new LocalMeat { Active = body.gameObject.activeSelf };
                        _localMeat.Add(body.gameObject, local);
                        foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                        {
                            var pause = new FsmSuppressor(); local.FsMs.Add(pause);
                            if (!pause.Suppress(fsm)) throw new InvalidOperationException("Could not pause guest saved meat.");
                        }
                        body.gameObject.SetActive(false); _trackedBodies[body] = true;
                    }
                }
                else if (!_spawnLifecycle.IsRetired(netId))
                {
                    if (_items.ContainsKey(netId) || existing != null) throw new InvalidOperationException("Native meat identity collision.");
                    BindFactoryBodyAsOwner(body, netId, Time.unscaledTime); _trackedBodies[body] = true;
                    _meat.Add(netId, new MeatBinding { Body = body, Use = use, NativeId = id });
                    SyncEventLog.Record("meat-spawn", id + " " + netId.ToString("X8"));
                }
                _meatOutputs.Remove(body);
            }
            catch (Exception e) { FailMeat(e); }
            return true;
        }

        internal MooseMeatState? BuildMeatState(uint id)
        {
            if (_meatFailed || SessionManager.Instance?.IsHost != true || _spawnLifecycle.IsRetired(id)
                || !_meat.TryGetValue(id, out var binding) || binding.Body == null || binding.Replica || !_items.ContainsKey(id)) return null;
            try
            {
                var c = SyncCatalog.MooseMeat!;
                int kind = Array.IndexOf(_meatNames, binding.Body.name);
                if (kind < 0 || binding.Use.ActiveStateName == c["destroy"] || binding.Use.ActiveStateName == c["deleted"]) return null;
                var state = new MooseMeatState { FactoryId = c.FactoryId, NativeId = binding.NativeId, Kind = (byte)kind,
                    Condition = Mathf.Clamp(binding.Use.FsmVariables.FindFsmFloat(c["condition"]).Value, 0, 100),
                    Position = binding.Body.position.ToNet(), Rotation = binding.Body.rotation.ToNet() };
                if (!MooseMeatPolicy.Valid(state)) throw new InvalidOperationException("Invalid native meat food state.");
                var old = binding.Observed;
                state.Revision = old == null ? 1 : (MooseMeatPolicy.SameFood(old, state) ? old.Revision : unchecked(old.Revision + 1));
                binding.Observed = state; return state;
            }
            catch (Exception e) { FailMeat(e); return null; }
        }
        internal IEnumerable<MooseMeatState> BuildMeatStates()
        {
            foreach (uint id in _meat.Keys) { var state = BuildMeatState(id); if (state != null) yield return state; }
        }
        private void FailMeat(Exception e)
        {
            if (_meatFailed) return;
            _meatFailed = true;
            WinterMPPlugin.Log.LogWarning("Moose meat sync disabled: " + e.Message);
            SyncEventLog.Record("meat-disabled", e.Message);
        }
    }
}
