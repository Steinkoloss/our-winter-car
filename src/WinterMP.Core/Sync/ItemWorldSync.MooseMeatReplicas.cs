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
        internal void OnMeatState(MooseMeatState state)
        {
            var c = SyncCatalog.MooseMeat;
            if (SessionManager.Instance?.IsHost != false || c == null || _meatFailed || !MooseMeatPolicy.Valid(state)
                || state.FactoryId != c.FactoryId || !FactoryItemIdentity.IsNativeId(state.NativeId, c["prefix"])) return;
            uint id = FactoryItemIdentity.ItemId(state.FactoryId, state.NativeId);
            if (_spawnLifecycle.IsRetired(id)) return;
            MooseMeatState? previous = _pendingMeat.TryGetValue(id, out var pending) ? pending
                : (_meat.TryGetValue(id, out var binding) ? binding.Received : null);
            if (!MooseMeatPolicy.CanReceive(previous, state) || (!_pendingMeat.ContainsKey(id) && _pendingMeat.Count >= 1024)) return;
            _snapshotSeenIds.Add(id); _pendingMeat[id] = state;
        }

        private void ProcessMeat(SessionManager session)
        {
            if (_meatFailed || _meatFactory == null || SyncCatalog.MooseMeat == null || Time.unscaledTime < _nextMeat) return;
            _nextMeat = Time.unscaledTime + .2f;
            try
            {
                if (!session.IsHost && !_meatFactoryPause.Active && _meatFactory.Fsm.Started
                    && _meatFactory.ActiveStateName == SyncCatalog.MooseMeat["factoryIdle"]
                    && !_meatFactoryPause.Suppress(_meatFactory)) throw new InvalidOperationException("Could not pause guest meat factory.");
                foreach (var output in new List<KeyValuePair<Rigidbody, string>>(_meatOutputs))
                {
                    if (!output.Key) _meatOutputs.Remove(output.Key);
                    else TryScanMeat(output.Key);
                }
                if (session.IsHost)
                {
                    foreach (var state in BuildMeatStates())
                    {
                        var binding = _meat[FactoryItemIdentity.ItemId(state.FactoryId, state.NativeId)];
                        if (binding.Sent && binding.SentRevision == state.Revision && Time.unscaledTime < binding.NextSend) continue;
                        session.SendWorldMessage(state, Channel.ReliableOrdered);
                        binding.Sent = true; binding.SentRevision = state.Revision; binding.NextSend = Time.unscaledTime + 5;
                    }
                    return;
                }
                if (!_meatFactoryPause.Active) return;
                foreach (uint id in new List<uint>(_pendingMeat.Keys))
                {
                    var state = _pendingMeat[id];
                    if (_spawnLifecycle.IsRetired(id)) { _pendingMeat.Remove(id); continue; }
                    if (_meat.TryGetValue(id, out var binding) && binding.Body != null)
                    {
                        if (!binding.Replica) throw new InvalidOperationException("Meat receipt overlaps a native save object.");
                        ApplyMeatFood(binding, state);
                    }
                    else MaterializeMeat(id, state);
                    _pendingMeat.Remove(id);
                }
            }
            catch (Exception e) { FailMeat(e); }
        }

        private void MaterializeMeat(uint id, MooseMeatState state)
        {
            if (_meatPrefab == null) return;
            if (_items.TryGetValue(id, out var existing))
            {
                if (existing.Body != null) throw new InvalidOperationException("Meat item ID collision.");
                RemoveTrackedItem(id, existing.Body);
            }
            if (_meat.TryGetValue(id, out var previous) && previous.Replica && previous.Use != null)
            {
                previous.Use.enabled = false;
                UnityEngine.Object.Destroy(previous.Use.gameObject);
            }
            var c = SyncCatalog.MooseMeat!;
            var templates = _meatPrefab.GetComponents<PlayMakerFSM>();
            var enabled = new bool[templates.Length];
            GameObject clone;
            try
            {
                // Native meat has no initial wait: block both FSMs on the prefab
                // before Instantiate so no guest save or random cooking timer runs.
                for (int i = 0; i < templates.Length; i++) { enabled[i] = templates[i].enabled; templates[i].enabled = false; }
                clone = (GameObject)UnityEngine.Object.Instantiate(_meatPrefab, state.Position.ToUnity(), state.Rotation.ToUnity());
            }
            finally { for (int i = 0; i < templates.Length; i++) templates[i].enabled = enabled[i]; }
            try
            {
                PlayMakerFSM? use = null;
                foreach (var fsm in clone.GetComponents<PlayMakerFSM>())
                {
                    fsm.enabled = false;
                    if (fsm.FsmName == c["use"]) use = fsm;
                }
                if (use == null) throw new InvalidOperationException("Meat replica Use missing.");
                if (!use.Fsm.Initialized) use.Fsm.Init(use);
                use.FsmVariables.FindFsmString(c["id"]).Value = "wintermp-meat-" + id.ToString("X8");
                use.FsmVariables.FindFsmGameObject(c["owner"]).Value = clone;
                // Keep native eating input/effects. The host alone runs cooking,
                // spoilage and persistence; those states cannot be entered here.
                var wait = FsmHook.FindState(use, c["waitPlayer"])!;
                wait.Actions = new[] { wait.Actions[0], wait.Actions[1] };
                var transitions = new List<FsmTransition>();
                foreach (var transition in wait.Transitions) if (transition.ToState == c["waitButton"]) transitions.Add(transition);
                wait.Transitions = transitions.ToArray();
                use.Fsm.States = new[] { wait, FsmHook.FindState(use, c["waitButton"])!, FsmHook.FindState(use, c["eat"])!, FsmHook.FindState(use, c["destroy"])! };
                var globals = new List<FsmTransition>();
                foreach (var transition in use.Fsm.GlobalTransitions) if (transition.ToState == c["destroy"]) globals.Add(transition);
                use.Fsm.GlobalTransitions = globals.ToArray();
                use.Fsm.StartState = c["waitPlayer"]; use.Fsm.RestartOnEnable = true;
                clone.transform.localScale = Vector3.one;
                var body = clone.GetComponent<Rigidbody>(); body.isKinematic = false;
                var binding = new MeatBinding { Body = body, Use = use, Replica = true, NativeId = state.NativeId };
                _meat[id] = binding; _trackedBodies[body] = true;
                BindSpawnedBody(body, new ItemSpawn.Entry { NetId = id, TemplateName = _meatNames[state.Kind],
                    Position = state.Position, Rotation = state.Rotation }, 0, Time.unscaledTime);
                _pendingItemPoses.Remove(id);
                ApplyMeatFood(binding, state); clone.SetActive(true);
                SyncEventLog.Record("meat-replica", state.NativeId + " " + id.ToString("X8"));
            }
            catch
            {
                foreach (var fsm in clone.GetComponents<PlayMakerFSM>()) fsm.enabled = false;
                RemoveTrackedItem(id, clone.GetComponent<Rigidbody>()); _meat.Remove(id);
                UnityEngine.Object.Destroy(clone); throw;
            }
        }

        private void ApplyMeatFood(MeatBinding binding, MooseMeatState state)
        {
            var c = SyncCatalog.MooseMeat!;
            bool changed = binding.Received == null || binding.Received.Kind != state.Kind;
            binding.Use.FsmVariables.FindFsmFloat(c["condition"]).Value = state.Condition;
            if (changed)
            {
                binding.Use.enabled = false;
                binding.Use.FsmVariables.FindFsmInt(c["type"]).Value = state.Kind == 4 ? 2 : state.Kind;
                binding.Body.name = _meatNames[state.Kind];
                binding.Body.GetComponent<Renderer>().sharedMaterial = _meatMaterials[state.Kind];
                if (state.Kind == 2)
                {
                    binding.Use.enabled = true;
                    if (!binding.Use.Fsm.Started) binding.Use.Fsm.Start();
                }
            }
            binding.Received = state;
        }

        private void ClearMeat()
        {
            ClearMooseChop();
            foreach (var pair in _meat)
            {
                var binding = pair.Value;
                if (_items.TryGetValue(pair.Key, out var tracked) && tracked.Body == binding.Body)
                {
                    if (binding.Body != null && tracked.KinematicSaved) binding.Body.isKinematic = tracked.OriginalKinematic;
                    RemoveTrackedItem(pair.Key, binding.Body);
                }
                // Native eating removes the Rigidbody but leaves a save tombstone
                // GameObject. Replica tombstones are disposable even without a body.
                if (binding.Replica && binding.Use != null)
                {
                    binding.Use.enabled = false;
                    UnityEngine.Object.Destroy(binding.Use.gameObject);
                }
            }
            foreach (var pair in _localMeat)
            {
                if (pair.Key == null) continue;
                _trackedBodies.Remove(pair.Key.GetComponent<Rigidbody>());
                pair.Key.SetActive(pair.Value.Active);
                foreach (var pause in pair.Value.FsMs) pause.Restore();
            }
            foreach (var body in _meatOutputs.Keys) _trackedBodies.Remove(body);
            foreach (var pair in _meatHooks)
            {
                var actions = new List<FsmStateAction>(pair.Key.Actions); actions.Remove(pair.Value); pair.Key.Actions = actions.ToArray();
            }
            _meatFactoryPause.Restore(); _meatFactory = null; _meatPrefab = null;
            _meat.Clear(); _localMeat.Clear(); _meatOutputs.Clear(); _pendingMeat.Clear(); _meatHooks.Clear();
            _meatFailed = false; _nextMeat = 0;
        }
    }
}
