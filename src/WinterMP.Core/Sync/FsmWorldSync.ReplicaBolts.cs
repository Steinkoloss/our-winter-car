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
    internal sealed partial class FsmWorldSync
    {
        internal void ObserveReplacementTightness(uint id, float value) =>
            _partTightnessReceipts.Resolve(id, ++_partReceiptOrder, value);
        internal float ReplacementTightness(uint id, float fallback) => _partTightnessReceipts.Latest(id, fallback);

        internal bool ReplacementBoltLoose(PlayMakerFSM fsm)
        {
            foreach (var bolt in _bolts.Values)
                if (bolt.Fsm == fsm && bolt.ReplicaGate?.Seeded == true && BoltReady(bolt))
                    return bolt.BoltTightnessVar!.Value >= 0 && bolt.BoltTightnessVar.Value < BoltStatePolicy.MaximumTightness;
            return false;
        }

        internal void PrepareReplacementBolts(PlayMakerFSM data)
        {
            if (!_bridge.PartIdentities.IsReplica(data) || !_bridge.PartIdentities.TryRootId(data, out uint partId)) return;
            foreach (var fsm in data.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (!SyncCatalog.TryMatchBolt(fsm)) continue;
                var bolt = new SyncedBolt { Fsm = fsm, Path = ScenePath.Of(fsm.transform), ReplicaGate = new ReplicaBoltGate() };
                try
                {
                    string? path = ScenePath.RelativeTo(fsm.transform, data.transform);
                    if (path == null || !PartIdentity.TryFsmId(partId, path, fsm.FsmName, out uint id))
                        throw new InvalidOperationException("Invalid replica bolt identity.");
                    if (_doors.ContainsKey(id) || _buys.ContainsKey(id) || _parts.ContainsKey(id))
                        throw new InvalidOperationException("Replica bolt overlaps another synchronized object.");
                    if (_bolts.TryGetValue(id, out var previous))
                    {
                        if (previous.Fsm != null) throw new InvalidOperationException("Replica bolt identity already occupied.");
                        _bolts.Remove(id);
                    }
                    BindNativeBolt(bolt);
                    if (bolt.PartData != data) throw new InvalidOperationException("Replica bolt targets another part.");
                    BindReplicaBoltVisual(bolt);
                    ReplaceReplicaBoltGraph(id, bolt);
                    _bolts.Add(id, bolt);
                    MarkRegistered(fsm);
                    SyncEventLog.Record("replica-bolt-bound", id.ToString("X8") + " " + path);
                }
                catch (Exception e)
                {
                    fsm.enabled = false;
                    if (bolt.ReplicaCollider != null) bolt.ReplicaCollider.enabled = false;
                    FailBolt(bolt, e);
                }
            }
        }

        private static void BindReplicaBoltVisual(SyncedBolt bolt)
        {
            var fsm = bolt.Fsm;
            var init = BoltActions(fsm, "Init", new[] { "GetChild", "GetScale", "GetName", "GetSubstring",
                "ConvertStringToInt", "ArrayListGet", "ConvertIntToFloat", "NextFrameEvent" });
            if (fsm.Fsm.GetOwnerDefaultTarget(BoltField<FsmOwnerDefault>(init[0], "gameObject")) != fsm.gameObject
                || BoltField<FsmString>(init[0], "withTag")?.Value != "Untagged"
                || !string.IsNullOrEmpty(BoltField<FsmString>(init[0], "childName")?.Value)
                || BoltField<FsmGameObject>(init[0], "storeResult")?.Name != "ThisBolt"
                || BoltField<FsmOwnerDefault>(init[1], "gameObject")?.GameObject.Name != "ThisBolt"
                || BoltField<FsmFloat>(init[1], "xScale")?.Name != "Boltsize"
                || Convert.ToInt32(init[1].GetType().GetField("space")?.GetValue(init[1])) != 1
                || BoltField<FsmGameObject>(init[2], "gameObject")?.Name != "ThisBolt"
                || BoltField<FsmString>(init[2], "storeName")?.Name != "Name"
                || BoltField<FsmString>(init[3], "stringVariable")?.Name != "Name"
                || BoltField<FsmString>(init[3], "storeResult")?.Name != "Nmbr"
                || BoltField<FsmString>(init[4], "stringVariable")?.Name != "Nmbr"
                || BoltField<FsmInt>(init[4], "intVariable")?.Name != "Index")
                throw new InvalidOperationException("Native bolt visual/index binding changed.");
            Transform? visual = null;
            for (int i = 0; i < fsm.transform.childCount; i++)
            {
                var child = fsm.transform.GetChild(i);
                if (child.tag != "Untagged") continue;
                if (visual != null) throw new InvalidOperationException("Ambiguous bolt visual.");
                visual = child;
            }
            var start = BoltField<FsmInt>(init[3], "startIndex");
            var length = BoltField<FsmInt>(init[3], "length");
            var values = BoltArray(bolt);
            if (visual == null || start == null || start.UseVariable || length == null || length.UseVariable || values == null
                || !ReplicaBoltGate.TryIndex(visual.name, start.Value, length.Value, values.Count, out int index)
                || !(values[index] is int) || !IsFinite(visual.localScale.x) || visual.localScale.x <= 0)
                throw new InvalidOperationException("Replica bolt array slot or size is unavailable.");
            var collider = fsm.FsmVariables.FindFsmObject("Collider")?.Value as SphereCollider;
            if (collider == null || collider.gameObject != fsm.gameObject || !collider.isTrigger || collider.gameObject.layer != 12)
                throw new InvalidOperationException("Native bolt pick collider changed.");
            var pose = FsmHook.FindState(fsm, "Set pos")!.Actions[0];
            if (Convert.ToInt32(pose.GetType().GetField("space")?.GetValue(pose)) != 1)
                throw new InvalidOperationException("Native bolt pose is no longer local.");
            for (var parent = fsm.transform; parent != bolt.PartData.transform; parent = parent.parent)
                if (parent == null || parent.GetComponent<Rigidbody>() != null)
                    throw new InvalidOperationException("Replica bolt has an independent physics body.");
            bolt.ReplicaCollider = collider; collider.enabled = false;
            bolt.ReplicaVisual = visual;
            bolt.IndexVar.Value = index;
            fsm.FsmVariables.FindFsmGameObject("ThisBolt").Value = visual.gameObject;
            fsm.FsmVariables.FindFsmFloat("Boltsize").Value = visual.localScale.x;
            if (FsmVariables.GlobalVariables.FindFsmBool(SyncCatalog.ReplacementParts!["replicaRepairVariable"]) == null)
                throw new InvalidOperationException("Native tool mode is unavailable.");
        }

        private void ReplaceReplicaBoltGraph(uint id, SyncedBolt bolt)
        {
            var fsm = bolt.Fsm;
            // Keep the native tool's FSM name, variables and event names. No
            // guest array deltas, parent BOLTING, alternator or timing actions run.
            fsm.Fsm.States = new[] {
                ReplicaBoltState(fsm, "Set pos", () => UpdateReplicaBoltVisual(bolt)),
                ReplicaBoltState(fsm, "Tight?", () => OnReplicaBoltTurn(id, bolt, 1)),
                ReplicaBoltState(fsm, "Loose?", () => OnReplicaBoltTurn(id, bolt, -1)),
                ReplicaBoltState(fsm, "On", () => UpdateReplicaBoltCollider(bolt)),
                ReplicaBoltState(fsm, "Off", () => { if (bolt.ReplicaCollider != null) bolt.ReplicaCollider.enabled = false; }),
            };
            var transitions = new List<FsmTransition>();
            foreach (var pair in new[] { new[] { "TIGHTEN", "Tight?" }, new[] { "UNTIGHTEN", "Loose?" },
                new[] { "REPAIRMODE_ON", "On" }, new[] { "REPAIRMODE_OFF", "Off" } })
                transitions.Add(new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent(pair[0]), ToState = pair[1] });
            fsm.Fsm.GlobalTransitions = transitions.ToArray();
            fsm.Fsm.StartState = "Set pos"; fsm.Fsm.RestartOnEnable = false;
            if (!FsmHook.EnsureRemoteEntry(fsm, "Set pos")) throw new InvalidOperationException("Cannot pose replica bolt.");
        }

        private static FsmState ReplicaBoltState(PlayMakerFSM fsm, string name, Action callback) =>
            new FsmState(fsm.Fsm) { Name = name, Actions = new FsmStateAction[] { new FsmHookAction(callback) }, Transitions = new FsmTransition[0] };

        internal void SetReplacementBolts(PlayMakerFSM data, bool fitted, bool changed)
        {
            ulong barrier = changed ? ++_partReceiptOrder : 0;
            bool hasBolts = false;
            foreach (var bolt in _bolts.Values)
            {
                if (bolt.ReplicaGate == null || bolt.PartData != data || bolt.Fsm == null || bolt.Failed) continue;
                hasBolts = true;
                if (changed || fitted != bolt.ReplicaGate.Attached) bolt.ReplicaGate.SetAttachment(fitted, barrier);
                bolt.Fsm.enabled = fitted;
                if (fitted && bolt.Fsm.gameObject.activeInHierarchy && !bolt.Fsm.Fsm.Started) bolt.Fsm.Fsm.Start();
                UpdateReplicaBoltCollider(bolt);
            }
            if (fitted && hasBolts && data.GetComponent<Rigidbody>() != null)
                data.GetComponent<Rigidbody>().detectCollisions = true; // Only the layer-12 trigger picks are enabled.
        }

        private void UpdateReplicaBoltVisual(SyncedBolt bolt)
        {
            if (bolt.ReplicaGate?.Seeded == true && bolt.ReplicaVisual != null)
            {
                var position = bolt.ReplicaVisual.localPosition;
                position.z = bolt.TightnessFVar!.Value;
                bolt.ReplicaVisual.localPosition = position;
            }
            UpdateReplicaBoltCollider(bolt);
        }

        private void UpdateReplicaBoltCollider(SyncedBolt bolt)
        {
            if (bolt.ReplicaCollider == null) return;
            bolt.ReplicaCollider.enabled = !bolt.Failed && bolt.ReplicaGate?.Attached == true && bolt.ReplicaGate.Seeded
                && bolt.Fsm != null && bolt.Fsm.enabled && bolt.Fsm.gameObject.activeInHierarchy
                && NativePartIdentity.Phase(bolt.PartData) == NativePartPhase.Fitted
                && _bridge.ReplacementBoltsReady(bolt.PartData)
                && FsmVariables.GlobalVariables.FindFsmBool(SyncCatalog.ReplacementParts!["replicaRepairVariable"])?.Value == true;
        }

        private void OnReplicaBoltTurn(uint id, SyncedBolt bolt, int direction)
        {
            try
            {
                UpdateReplicaBoltCollider(bolt);
                var session = SessionManager.Instance;
                if (_bridge.ApplyingRemote || session == null || session.IsHost || session.State != SessionState.Connected
                    || bolt.Failed || bolt.ReplicaCollider == null || !bolt.ReplicaCollider.enabled
                    || !bolt.ReplicaGate!.CanTurn(bolt.BoltTightnessVar!.Value, direction)
                    || Time.unscaledTime < bolt.NextReplicaRequestAt) return;
                bolt.NextReplicaRequestAt = Time.unscaledTime + .1f;
                session.SendWorldMessage(new FsmRawEvent { NetId = id, EventName = direction > 0 ? "TIGHTEN" : "UNTIGHTEN" }, Channel.ReliableOrdered);
                SyncEventLog.Record("replica-bolt-intent", id.ToString("X8") + " " + direction);
            }
            catch (Exception e) { FailBolt(bolt, e); }
            finally { if (bolt.Fsm != null && bolt.Fsm.enabled) FsmHook.FireRemoteEntry(bolt.Fsm, "Set pos"); }
        }

        private void ProcessReplicaBolts()
        {
            foreach (var pair in _bolts)
            {
                var bolt = pair.Value;
                if (bolt.ReplicaGate == null || bolt.Fsm == null || bolt.Failed) continue;
                try
                {
                    UpdateReplicaBoltCollider(bolt);
                    if (bolt.ReplicaGate.Attached && !bolt.ReplicaGate.Seeded && BoltReady(bolt)
                        && _bridge.ReplacementBoltsReady(bolt.PartData))
                        _bridge.RequestObjectState(pair.Key);
                }
                catch (Exception e) { FailBolt(bolt, e); }
            }
        }

        internal void ForgetReplacementBolts(PlayMakerFSM? data)
        {
            if (data is null) return;
            _bridge.HookedFsms.Remove(data); _registeredFsms.Remove(data);
            foreach (uint id in new List<uint>(_parts.Keys))
                if (_parts[id].ReplicaCopy && ReferenceEquals(_parts[id].Fsm, data))
                { _parts.Remove(id); _pendingPartStates.Remove(id); }
            foreach (uint id in new List<uint>(_bolts.Keys))
            {
                var bolt = _bolts[id];
                if (bolt.ReplicaGate == null || !ReferenceEquals(bolt.PartData, data)) continue;
                if (bolt.Fsm != null) bolt.Fsm.enabled = false;
                if (bolt.ReplicaCollider != null) bolt.ReplicaCollider.enabled = false;
                if (!(bolt.Fsm is null)) { _bridge.HookedFsms.Remove(bolt.Fsm); _registeredFsms.Remove(bolt.Fsm); }
                _bolts.Remove(id); _pendingBoltStates.Remove(id);
            }
        }
    }
}
