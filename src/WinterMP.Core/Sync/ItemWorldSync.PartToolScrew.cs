using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class PartToolScrew
        {
            public PlayMakerFSM Screw = null!;
            public BoxCollider Pick = null!;
            public FsmFloat Scalar = null!;
            public int ScalarIndex, LooseLayer;
            public float NextRequestAt;
        }
        private PlayMakerFSM? _sparkplugTool;
        private FsmState? _sparkplugNameState;
        private FsmStateAction? _sparkplugNameHook;
        private bool _sparkplugToolFailed;

        private PartToolScrew? GetPartToolScrew(ReplacementBinding part)
        {
            var rule = part.Factory.Rule.ToolScrew;
            if (rule == null || part.ToolScrewFailed || part.Data == null) return null;
            if (part.ToolScrew != null) return part.ToolScrew;
            try
            {
                var screw = FindPartAdjustmentFsm(part.Data.transform, rule.Fsm);
                var pick = part.Data.FsmVariables.FindFsmObject(SyncCatalog.ReplacementParts!["removeColliderVariable"])?.Value as BoxCollider;
                var scalar = part.Data.FsmVariables.FindFsmFloat(rule.Scalar);
                RequireFit(pick != null && pick.gameObject == part.Data.gameObject && scalar != null
                    && pick.size.x > 0 && pick.size.y > 0 && pick.size.z > 0);
                ValidatePartToolScrew(screw, part.Data, rule);
                var control = new PartToolScrew { Screw = screw, Pick = pick!, Scalar = scalar!,
                    ScalarIndex = Array.IndexOf(part.Factory.Rule.Scalars, rule.Scalar), LooseLayer = part.Data.gameObject.layer };
                part.ToolScrew = control;
                if (part.Replica) PrepareReplicaToolScrew(part, control);
                return control;
            }
            catch (Exception e) { DisablePartToolScrew(part, e.Message); return null; }
        }

        private static void DisablePartToolScrew(ReplacementBinding part, string reason)
        {
            if (part.ToolScrewFailed) return;
            part.ToolScrewFailed = true;
            if (part.Replica && part.ToolScrew != null) { part.ToolScrew.Screw.enabled = false; part.ToolScrew.Pick.enabled = false; }
            WinterMPPlugin.Log.LogWarning("WorldSync: tool tightening disabled for " + part.NativeId + ": " + reason);
            SyncEventLog.Record("part-tool-screw-disabled", part.NativeId + " " + reason);
        }

        private void OnHostPartToolScrew(PartFitRequest request, byte actor, SessionManager session)
        {
            ReplacementBinding? part = null; bool applying = _bridge.ApplyingRemote;
            try
            {
                _replacementParts.TryGetValue(request.ItemId, out part);
                var control = part == null || part.Replica || part.Factory.Failed ? null : GetPartToolScrew(part);
                var state = BuildReplacementPartState(request.ItemId); var mount = part == null ? null : FindRemovalMount(part);
                var rule = part?.Factory.Rule.ToolScrew;
                bool available = control != null && part != null && FitFsmReady(part.Data);
                bool near = available && GuestNearPackage(session, actor, part!.Data.transform.position);
                bool ready = available && NativeToolScrewMountReady(part!, control!, mount);
                bool busy = control == null || rule == null || !FitFsmReady(control.Screw) || Time.unscaledTime < control.NextRequestAt
                    || (control.Screw.ActiveStateName != rule.IdleState && control.Screw.ActiveStateName != "State 1" && control.Screw.ActiveStateName != "State 2");
                var status = PartToolScrewPolicy.Check(request, state, control?.ScalarIndex ?? -1, available, near, ready, busy);
                var receipt = _partFitLedger.Begin(request, actor, status); if (receipt == null) return;
                if (status == PartFitStatus.Pending)
                {
                    control!.NextRequestAt = Time.unscaledTime + PartToolScrewPolicy.Cooldown; _bridge.ApplyingRemote = true;
                    ExecutePartToolScrew(control.Screw, part!.Data, rule!, request.Operation);
                    RequireFit(NativeToolScrewMountReady(part, control, mount)); receipt = _partFitLedger.Complete(request, true)!;
                }
                SendPartFitReceipt(session, receipt);
                SyncEventLog.Record("part-tool-screw", actor + ":" + request.Sequence + " " + request.ItemId.ToString("X8") + " " + receipt.Status);
            }
            catch (Exception e)
            {
                if (part != null) DisablePartToolScrew(part, e.Message);
                var receipt = _partFitLedger.Complete(request, false) ?? _partFitLedger.Begin(request, actor, PartFitStatus.Failed);
                if (receipt != null) SendPartFitReceipt(session, receipt);
            }
            finally { _bridge.ApplyingRemote = applying; }
        }

        private static float ExecutePartToolScrew(PlayMakerFSM screw, PlayMakerFSM data, PartToolScrewData rule, PartFitOperation operation)
        {
            float current = data.FsmVariables.FindFsmFloat(rule.Scalar).Value;
            RequireFit(PartToolScrewPolicy.TryTurn(current, operation, out float expected));
            string entry = operation == PartFitOperation.ToolTighten ? rule.TightenState : rule.LoosenState;
            RequireFit(FsmHook.EnsureRemoteEntry(screw, entry));
            // Repair mode can interrupt Idle before its scalar refresh. Never
            // use the negative pose offset as the next native turn's bound check.
            screw.FsmVariables.FindFsmFloat(rule.ScratchVariable).Value = current;
            FsmHook.FireRemoteEntry(screw, entry);
            RequireFit(data.FsmVariables.FindFsmFloat(rule.Scalar).Value == expected && screw.ActiveStateName == rule.IdleState
                && Vector3.Distance(data.transform.localPosition, new Vector3(0, 0, -expected / 400f)) < .00001f);
            return expected;
        }

        private static bool NativeToolScrewMountReady(ReplacementBinding part, PartToolScrew control, PlayMakerFSM? mount)
        {
            float tightness = control.Scalar.Value;
            // Host collider visibility follows the host's own repair mode. It
            // cannot decide whether a nearby guest may use their wrench.
            return mount != null && FitFsmReady(mount) && part.Body == null && part.Data.transform.parent == mount.transform
                && NativePartIdentity.Phase(part.Data) == NativePartPhase.Fitted && PartHandScrewPolicy.ValidTightness(tightness)
                && control.Pick.isTrigger && part.Data.gameObject.layer == part.Factory.Rule.RemovalLayer
                && part.Data.FsmVariables.FindFsmGameObject("Owner")?.Value == part.Data.gameObject
                && mount.FsmVariables.FindFsmGameObject("ActivePart")?.Value == part.Data.gameObject
                && mount.FsmVariables.FindFsmGameObject("AssemblyPoint")?.Value == mount.gameObject
                && mount.FsmVariables.FindFsmBool("Installed")?.Value == true
                && ToolScrewSocketMatches(part, mount)
                && mount.FsmVariables.FindFsmFloat(part.Factory.Rule.ToolScrew!.Scalar)?.Value == tightness
                && mount.FsmVariables.FindFsmBool("Bolted")?.Value == (tightness >= 1)
                && Vector3.Distance(part.Data.transform.localPosition, new Vector3(0, 0, -tightness / 400f)) < .00001f;
        }

        private static bool ToolScrewSocketMatches(ReplacementBinding part, PlayMakerFSM mount)
        {
            // Saved plugs use the part's array index. The mount's AssemblyID is
            // installer scratch and remains at its prefab default after loading.
            int slot = part.Data.FsmVariables.FindFsmInt("AssemblyID")?.Value ?? 0;
            var c = SyncCatalog.ReplacementParts!;
            var database = FsmVariables.GlobalVariables.FindFsmGameObject(c["slotDatabaseVariable"])?.Value;
            if (slot < 1 || slot > part.Factory.Rule.SlotCount || database == null
                || ScenePath.Of(database.transform) != c["slotDatabasePath"]) return false;
            IList? slots = null;
            foreach (var component in database.GetComponents<MonoBehaviour>())
                if (component != null && component.GetType().Name == "PlayMakerArrayListProxy"
                    && component.GetType().GetField("referenceName")?.GetValue(component) as string == part.Factory.Rule.SlotReference)
                {
                    if (slots != null) return false;
                    slots = component.GetType().GetProperty("arrayList")?.GetValue(component, null) as IList;
                }
            if (slots == null || slots.Count != part.Factory.Rule.SlotCount + 1 || slots[0] != null || slots[slot] as GameObject != mount.gameObject) return false;
            for (int i = 1; i < slots.Count; i++) if (i != slot && slots[i] as GameObject == mount.gameObject) return false;
            return true;
        }

        private void PrepareReplicaToolScrew(ReplacementBinding part, PartToolScrew control)
        {
            var screw = control.Screw;
            screw.Fsm.States = new[] {
                ToolScrewState(screw, "MP pose", () => UpdateReplicaToolScrew(part)),
                ToolScrewState(screw, "MP tighten", () => OnReplicaToolScrew(part, PartFitOperation.ToolTighten)),
                ToolScrewState(screw, "MP loosen", () => OnReplicaToolScrew(part, PartFitOperation.ToolLoosen)) };
            var transitions = new List<FsmTransition>();
            foreach (var pair in new[] { new[] { "TIGHTEN", "MP tighten" }, new[] { "UNTIGHTEN", "MP loosen" },
                new[] { "REPAIRMODE_ON", "MP pose" }, new[] { "REPAIRMODE_OFF", "MP pose" } })
                transitions.Add(new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent(pair[0]), ToState = pair[1] });
            screw.Fsm.GlobalTransitions = transitions.ToArray(); screw.Fsm.StartState = "MP pose"; screw.Fsm.RestartOnEnable = false;
            screw.enabled = false;
        }

        private static FsmState ToolScrewState(PlayMakerFSM fsm, string name, Action callback) =>
            new FsmState(fsm.Fsm) { Name = name, Actions = new FsmStateAction[] { new FsmHookAction(callback) }, Transitions = new FsmTransition[0] };

        private void UpdateReplicaToolScrew(ReplacementBinding part)
        {
            var control = part.ToolScrew;
            if (!part.Replica || control == null || part.Data == null) return;
            bool fitted = part.FittedPresentation && part.Data.gameObject.activeInHierarchy;
            part.Data.gameObject.layer = fitted ? part.Factory.Rule.RemovalLayer : control.LooseLayer;
            control.Screw.enabled = fitted && !part.ToolScrewFailed;
            if (!fitted) return;
            PartIdentity.TryItemId(part.NativeId, out uint id); var state = _replacementReplica?.Get(id);
            bool ready = !part.ToolScrewFailed && control.Screw.enabled && state != null && PartAdjustmentViewReady(part, state) && !_pendingReplacements.Contains(id)
                && ResolveReplacementParent(state) == part.Data.transform.parent && part.Factory.Suppressor.Active
                && PartHandScrewPolicy.ValidTightness(control.Scalar.Value) && !_sparkplugToolFailed && _sparkplugTool != null;
            control.Pick.isTrigger = true;
            control.Pick.enabled = ready && _partFitClient?.Pending != true
                && FsmVariables.GlobalVariables.FindFsmBool(SyncCatalog.ReplacementParts!["replicaRepairVariable"])?.Value == true;
            if (part.Body != null) part.Body.detectCollisions = true;
            if (control.Screw.enabled && !control.Screw.Fsm.Started) control.Screw.Fsm.Start();
        }

        private void OnReplicaToolScrew(ReplacementBinding part, PartFitOperation operation)
        {
            try
            {
                UpdateReplicaToolScrew(part); var session = SessionManager.Instance; var control = part.ToolScrew;
                if (_bridge.ApplyingRemote || session == null || session.IsHost || session.State != SessionState.Connected
                    || DeathSyncManager.Instance?.IsLocalDead == true || Time.timeScale == 0 || control == null || !control.Pick.enabled
                    || Time.unscaledTime < control.NextRequestAt || !ToolSelectsSparkplug(part, operation)) return;
                PartIdentity.TryItemId(part.NativeId, out uint id); var state = _replacementReplica?.Get(id);
                if (state == null || !PartToolScrewPolicy.TryTurn(control.Scalar.Value, operation, out _)) return;
                EnsurePartFitClient();
                if (_partFitClient!.TryBegin(session.LocalPlayerId, id, state.Revision, operation))
                { control.NextRequestAt = Time.unscaledTime + PartToolScrewPolicy.Cooldown; ProcessPartFitting(session); }
                UpdateReplicaToolScrew(part);
            }
            catch (Exception e) { DisablePartToolScrew(part, e.Message); }
        }

        private bool ToolSelectsSparkplug(ReplacementBinding part, PartFitOperation operation)
        {
            var tool = _sparkplugTool;
            if (tool == null || !FitFsmReady(tool) || tool.FsmVariables.FindFsmGameObject("Bolt")?.Value != part.Data.gameObject
                || !PartToolScrewPolicy.MatchesTool(FsmVariables.GlobalVariables.FindFsmFloat("ToolWrenchSize")?.Value ?? float.NaN)) return false;
            string action = operation == PartFitOperation.ToolTighten ? "Tighten" : "Untighten";
            return tool.ActiveStateName == action || tool.ActiveStateName == action + " 2";
        }

        private void ProcessPartToolScrews(SessionManager session)
        {
            foreach (var part in _replacementParts.Values)
            {
                if (part.Factory.Rule.ToolScrew == null || part.Factory.Failed || part.Data == null) continue;
                GetPartToolScrew(part);
                if (!_sparkplugToolFailed && _sparkplugTool == null && _bridge.LocalPlayer != null)
                {
                    var rule = part.Factory.Rule.ToolScrew;
                    var node = ScenePath.FindRelative(_bridge.LocalPlayer, rule.ToolPath.Substring("PLAYER/".Length));
                    if (node != null)
                    {
                        try
                        {
                            var tool = FindPartAdjustmentFsm(node, rule.ToolFsm); var ray = FindPartAdjustmentFsm(node, rule.RaycastFsm);
                            if (!tool.Fsm.Initialized || !ray.Fsm.Initialized) continue;
                            BindSparkplugTool(tool, ray, rule);
                        }
                        catch (Exception e)
                        {
                            _sparkplugToolFailed = true;
                            WinterMPPlugin.Log.LogWarning("WorldSync: spark-plug tool recognition disabled: " + e.Message);
                            SyncEventLog.Record("sparkplug-tool-disabled", e.Message);
                        }
                    }
                }
                if (!session.IsHost) UpdateReplicaToolScrew(part);
            }
        }

        private void BindSparkplugTool(PlayMakerFSM tool, PlayMakerFSM ray, PartToolScrewData rule)
        {
            ValidateSparkplugTool(tool, ray, rule);
            RequireFit(FsmHook.OnStateEnter(tool, "Check bolt Name", () => RecognizeSparkplug(tool), out _sparkplugNameHook));
            _sparkplugNameState = FsmHook.FindState(tool, "Check bolt Name"); _sparkplugTool = tool;
        }

        private void RecognizeSparkplug(PlayMakerFSM tool)
        {
            var session = SessionManager.Instance;
            if (session == null || (session.State != SessionState.Connected && session.State != SessionState.Hosting)) return;
            var picked = tool.FsmVariables.FindFsmGameObject("Bolt")?.Value;
            foreach (var part in _replacementParts.Values)
                if (part.Data != null && part.Data.gameObject == picked && !part.Factory.Failed && !part.ToolScrewFailed
                    && part.Factory.Rule.ToolScrew != null && GetPartToolScrew(part) != null
                    && (part.Replica ? part.FittedPresentation && part.HasAppliedState : NativePartIdentity.Phase(part.Data) == NativePartPhase.Fitted))
                { tool.SendEvent("SPARKPLUG"); return; }
        }

        private void ClearPartToolScrews()
        {
            if (_sparkplugNameState != null && _sparkplugNameHook != null) RemoveReplacementHook(_sparkplugNameState, _sparkplugNameHook);
            _sparkplugNameState = null; _sparkplugNameHook = null; _sparkplugTool = null; _sparkplugToolFailed = false;
        }
    }
}
