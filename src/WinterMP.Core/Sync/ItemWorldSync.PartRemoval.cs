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
        private static void ValidatePartRemoval(ReplacementBinding part)
        {
            if (part.RemovalValidated || part.RemovalFailed || part.Data == null) return;
            var c = SyncCatalog.ReplacementParts!;
            try
            {
                var data = part.Data;
                RequireFitTransition(data, null, c["removeRecheckEvent"], c["removeTightnessState"]);
                var box = data.FsmVariables.FindFsmObject(c["removeColliderVariable"])?.Value as BoxCollider;
                RequireFit(box != null && box.gameObject == data.gameObject);
                part.RemovalCollider = box;
                var tight = new List<FsmStateAction>();
                foreach (var action in FsmHook.FindState(data, c["removeTightnessState"])!.Actions)
                    if (!(action is FsmHookAction)) tight.Add(action);
                int offset = tight.Count == 3 ? 1 : 0;
                RequireFit(tight.Count == offset + 2);
                if (offset == 1)
                {
                    RequireFit(tight[0].Enabled && tight[0].GetType().Name == "FloatClamp"
                        && PackageField<FsmFloat>(tight[0], "floatVariable")?.Name == c["removeTightnessVariable"]
                        && PackageField<FsmFloat>(tight[0], "minValue")?.Value == 0);
                }
                var compare = tight[offset + 1];
                RequireFit(tight[offset].Enabled && tight[offset].GetType().Name == "SetFsmFloat"
                    && FitTargetVariable(PackageField<FsmOwnerDefault>(tight[offset], "gameObject"), c["installPointVariable"])
                    && PackageField<FsmString>(tight[offset], "fsmName")?.Value == c["itemFsm"]
                    && PackageField<FsmString>(tight[offset], "variableName")?.Value == c["removeTightnessVariable"]
                    && PackageField<FsmFloat>(tight[offset], "setValue")?.Name == c["removeTightnessVariable"]
                    && compare.Enabled && compare.GetType().Name == "FloatCompare"
                    && PackageField<FsmFloat>(compare, "float1")?.Name == c["removeTightnessVariable"]
                    && PackageField<FsmFloat>(compare, "float1")?.UseVariable == true
                    && PackageField<FsmFloat>(compare, "float2")?.UseVariable == false
                    && PackageField<FsmFloat>(compare, "float2")?.Value == 1
                    && PackageField<FsmFloat>(compare, "tolerance")?.Value == 0
                    && PackageField<FsmEvent>(compare, "lessThan")?.Name == "BACK"
                    && PackageField<FsmEvent>(compare, "equal")?.Name == "PROCEED"
                    && PackageField<FsmEvent>(compare, "greaterThan")?.Name == "PROCEED");
                RequireFitTransition(data, c["removeTightnessState"], "BACK", c["removeUnboltedState"]);
                RequireFitTransition(data, c["removeTightnessState"], "PROCEED", c["removeBoltedState"]);
                var off = NativePartActions(data, c["removeMouseOffState"], "SetBoolValue", "MousePickEvent");
                var over = NativePartActions(data, c["removeMouseOverState"], "SetBoolValue", "MousePickEvent", "GetMouseButtonDown");
                foreach (var pick in new[] { off[1], over[1] })
                {
                    var mask = PackageField<FsmInt[]>(pick, "layerMask");
                    RequireFit(PackageField<FsmOwnerDefault>(pick, "GameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                        && PackageField<FsmFloat>(pick, "rayDistance")?.Value == 1
                        && PackageField<FsmFloat>(pick, "rayDistance")?.UseVariable == false
                        && mask != null && mask.Length == 1 && !mask[0].UseVariable && mask[0].Value == 19
                        && PackageField<FsmBool>(pick, "invertMask")?.Value == false);
                }
                RequireFit(Convert.ToInt32(over[2].GetType().GetField("button")!.GetValue(over[2])) == 1
                    && PackageField<FsmEvent>(over[2], "sendEvent")?.Name == c["fitConfirmEvent"]);
                RequireFitTransition(data, c["removeMouseOverState"], c["fitConfirmEvent"], c["removeState"]);
                var remove = NativePartActions(data, c["removeState"], "SetFsmGameObject", "SendEventByName", "NextFrameEvent");
                RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(remove[0], "gameObject"), c["installPointVariable"])
                    && PackageField<FsmString>(remove[0], "fsmName")?.Value == c["itemFsm"]
                    && PackageField<FsmString>(remove[0], "variableName")?.Value == c["mountPartVariable"]
                    && PackageField<FsmGameObject>(remove[0], "setValue")?.Name == c["fitOwnerVariable"]
                    && PackageField<FsmGameObject>(remove[0], "setValue")?.UseVariable == true);
                var target = PackageField<FsmEventTarget>(remove[1], "eventTarget");
                RequireFit(target != null && target.target == FsmEventTarget.EventTarget.GameObjectFSM
                    && FitTargetVariable(target.gameObject, c["installPointVariable"]) && target.fsmName.Value == c["itemFsm"]
                    && PackageField<FsmString>(remove[1], "sendEvent")?.Value == c["removeEvent"]
                    && PackageField<FsmFloat>(remove[1], "delay")?.Value == 0
                    && PackageField<FsmEvent>(remove[2], "sendEvent")?.Name == "FINISHED");
                foreach (var action in remove) RequireFitOneShot(action);
                foreach (var action in tight) RequireFitOneShot(action);
                part.RemovalValidated = true;
            }
            catch (Exception e)
            {
                part.RemovalFailed = true;
                WinterMPPlugin.Log.LogWarning("WorldSync: removal disabled for " + part.NativeId + ": " + e.Message);
                SyncEventLog.Record("part-remove-disabled", part.NativeId + " " + e.Message);
            }
        }

        private static PlayMakerFSM? FindRemovalMount(ReplacementBinding part)
        {
            var c = SyncCatalog.ReplacementParts!;
            if (part.Data == null) return null;
            var point = part.Data.FsmVariables.FindFsmGameObject(c["installPointVariable"])?.Value;
            if (point == null) return null;
            foreach (var mount in point.GetComponents<PlayMakerFSM>())
                if (mount.FsmName == c["itemFsm"]) return mount;
            return null;
        }

        private bool NativeRemovalReady(ReplacementBinding part, PlayMakerFSM? mount, bool entering = false)
        {
            ValidatePartRemoval(part);
            var c = SyncCatalog.ReplacementParts!;
            if (!part.RemovalValidated || part.RemovalFailed || part.Replica || part.Factory.Failed
                || mount == null || !FitFsmReady(mount) || !FitFsmReady(part.Data) || part.Body != null
                || part.RemovalCollider == null || !part.RemovalCollider.enabled || !part.RemovalCollider.isTrigger
                || part.Data.gameObject.layer != 19 || part.Data.gameObject.tag != "Untagged"
                || NativePartIdentity.Phase(part.Data) != NativePartPhase.Fitted
                || part.Data.FsmVariables.FindFsmGameObject(c["fitOwnerVariable"])?.Value != part.Data.gameObject
                || mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != part.Data.gameObject
                || mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value != true
                || part.Data.transform.parent == null
                || mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value != part.Data.transform.parent.gameObject
                || !PartRemovalPolicy.Unbolted(part.Data.FsmVariables.FindFsmFloat(c["removeTightnessVariable"])?.Value ?? float.NaN)) return false;
            string active = part.Data.ActiveStateName;
            return active == c["removeMouseOffState"] || active == c["removeMouseOverState"]
                || (entering && active == c["removeState"]);
        }

        private bool PublishRemovalAllowed(ReplacementBinding part, ReplacementPartState state)
        {
            bool entering = _partFitting != null && _partFitting.Part == part
                && _partFitting.Request.Operation == PartFitOperation.Remove && !_partFitting.Committed;
            return PartAttachmentPolicy.HasAttachment(state) && NativeRemovalReady(part, FindRemovalMount(part), entering);
        }

        private void OnHostPartRemoval(PartFitRequest request, byte actor, SessionManager session)
        {
            try
            {
                _replacementParts.TryGetValue(request.ItemId, out var part);
                var mount = part == null ? null : FindRemovalMount(part);
                var state = BuildReplacementPartState(request.ItemId);
                bool available = part != null && !part.Replica && !part.RemovalFailed && mount != null
                    && FitFsmReady(part.Data) && FitFsmReady(mount);
                bool nearby = available && GuestNearPackage(session, actor, part!.Data.transform.position)
                    && GuestNearPackage(session, actor, mount!.transform.position);
                var status = PartRemovalPolicy.Check(request, state, part?.Factory.Rule.Identity.TightnessIndex ?? -1,
                    available, nearby, _partFitting == null && available && NativeRemovalReady(part!, mount));
                if (status == PartFitStatus.Pending)
                {
                    var c = SyncCatalog.ReplacementParts!;
                    RequireFitTransition(mount!, null, c["removeEvent"], c["removeAllowState"]);
                    RequireFit(FsmHook.EnsureRemoteEntry(part!.Data, c["removeState"]));
                }
                var receipt = _partFitLedger.Begin(request, actor, status);
                if (receipt == null) return;
                if (status == PartFitStatus.Pending)
                {
                    var c = SyncCatalog.ReplacementParts!;
                    var removal = new PartFitting { Request = PartFitLedger.Copy(request), Part = part!, Mount = mount!,
                        Deadline = Time.unscaledTime + 3f };
                    _partFitting = removal;
                    removal.EntryState = FsmHook.FindState(part!.Data, c["removeState"]);
                    removal.EntryGuard = new FsmHookAction(() => ConfirmPartRemoval(session, removal));
                    var actions = new List<FsmStateAction>(removal.EntryState!.Actions);
                    actions.Insert(0, removal.EntryGuard); removal.EntryState.Actions = actions.ToArray();
                    FsmHook.FireRemoteEntry(part.Data, c["removeState"]);
                }
                SendPartFitReceipt(session, receipt);
                SyncEventLog.Record("part-remove-request", actor + ":" + request.Sequence + " " + request.ItemId.ToString("X8") + " " + status);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("WorldSync: part removal: " + e.Message);
                SyncEventLog.Record("part-remove-failed", request.ItemId.ToString("X8") + " " + e.Message);
                if (_partFitting == null) _partFitLedger.Begin(request, actor, PartFitStatus.Failed);
                else FailPartFitting(session, e.Message);
                var receipt = _partFitLedger.Complete(request, false);
                if (receipt != null) SendPartFitReceipt(session, receipt);
            }
        }

        private void ConfirmPartRemoval(SessionManager session, PartFitting removal)
        {
            if (_partFitting != removal || removal.Committed) return;
            try
            {
                var state = BuildReplacementPartState(removal.Request.ItemId);
                if (Time.unscaledTime >= removal.Deadline || PartRemovalPolicy.Check(removal.Request, state,
                    removal.Part.Factory.Rule.Identity.TightnessIndex, true,
                    GuestNearPackage(session, removal.Request.PlayerId, removal.Part.Data.transform.position)
                        && GuestNearPackage(session, removal.Request.PlayerId, removal.Mount.transform.position),
                    NativeRemovalReady(removal.Part, removal.Mount, true)) != PartFitStatus.Pending)
                    throw new InvalidOperationException("Removal target or prerequisites changed.");
                removal.Committed = true;
            }
            catch (Exception e) { FailPartFitting(session, e.Message); }
        }

        private void ProcessPartRemoval(SessionManager session, PartFitting removal)
        {
            try
            {
                if (!FitFsmReady(removal.Part.Data) || !FitFsmReady(removal.Mount))
                    throw new InvalidOperationException("Removal target became unavailable.");
                var c = SyncCatalog.ReplacementParts!;
                var state = BuildReplacementPartState(removal.Request.ItemId);
                bool stillOccupies = removal.Mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value == true
                    && removal.Mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value == removal.Part.Data.gameObject;
                if (removal.Committed && state != null && removal.Part.Factory.Rule.Identity.CanCreate(state)
                    && removal.Part.Body != null && !stillOccupies && removal.Part.Data.ActiveStateName == c["itemStopState"])
                {
                    TryScanNativePart(removal.Part.Body);
                    FinishPartFitting(session, true); return;
                }
                if (Time.unscaledTime >= removal.Deadline) throw new InvalidOperationException("Native removal did not settle.");
            }
            catch (Exception e) { FailPartFitting(session, e.Message); }
        }

        private void CancelPartRemoval(PartFitting removal)
        {
            if (removal.Committed || !FitFsmReady(removal.Part.Data)) return;
            var c = SyncCatalog.ReplacementParts!;
            float tightness = removal.Part.Data.FsmVariables.FindFsmFloat(c["removeTightnessVariable"])?.Value ?? float.NaN;
            // Stop the native Remove actions before they can alter a mount. Only
            // derive fitted interaction again if this part still owns that mount.
            bool ownsMount = removal.Mount != null
                && FindRemovalMount(removal.Part) == removal.Mount
                && removal.Mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value == removal.Part.Data.gameObject
                && removal.Mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value == true
                && !float.IsNaN(tightness) && !float.IsInfinity(tightness)
                && NativePartIdentity.Phase(removal.Part.Data) == NativePartPhase.Fitted;
            if (ownsMount) removal.Part.Data.SendEvent(c["removeRecheckEvent"]);
            else if (FsmHook.EnsureRemoteEntry(removal.Part.Data, c["itemStopState"]))
                FsmHook.FireRemoteEntry(removal.Part.Data, c["itemStopState"]);
        }

        private bool PartInteractionHandFree()
        {
            var c = SyncCatalog.ReplacementParts;
            var player = _bridge.LocalPlayer;
            if (c == null || player == null) return false;
            var hand = ScenePath.FindRelative(player, c["removeHandPath"]);
            if (hand == null || !hand.gameObject.activeInHierarchy) return false;
            bool free = false;
            foreach (var fsm in hand.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == c["removeHandFsm"] && FitFsmReady(fsm) && fsm.ActiveStateName == c["removeHandIdleState"]) free = true;
            return free;
        }

        private bool DrawPartRemovalPrompt(SessionManager session)
        {
            var camera = Camera.main;
            if (camera == null || !PartInteractionHandFree()) return false;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            float nearest = 1f;
            if (Physics.Raycast(ray, out var hit, nearest, 1 << 19)) nearest = hit.distance;
            ReplacementBinding? selected = null;
            ReplacementPartState? selectedState = null;
            uint selectedId = 0;
            foreach (var pair in _replacementParts)
            {
                var part = pair.Value;
                if (!part.Replica || !part.FittedPresentation || part.Data == null || !part.Data.gameObject.activeInHierarchy
                    || !part.RemovalValidated || part.RemovalFailed || part.RemovalCollider == null) continue;
                var state = _replacementReplica?.Get(pair.Key);
                if (state == null || !PartAttachmentPolicy.HasAttachment(state)) continue;
                var box = part.RemovalCollider;
                var origin = box.transform.InverseTransformPoint(ray.origin);
                var direction = box.transform.InverseTransformPoint(ray.origin + ray.direction) - origin;
                if (!PartRemovalPolicy.RayBox(origin.ToNet(), direction.ToNet(), box.center.ToNet(), box.size.ToNet(), nearest, out float distance)) continue;
                nearest = distance; selected = part; selectedState = state; selectedId = pair.Key;
            }
            if (selected == null || selectedState == null || !selectedState.RemovalAllowed) return false;
            GUI.Box(new Rect(Screen.width / 2f - 140f, Screen.height / 2f + 55f, 280f, 28f), "Right click to remove this part");
            var input = Event.current;
            if (input.type == EventType.MouseDown && input.button == 1 && _partFitInputFrame != Time.frameCount)
            {
                _partFitInputFrame = Time.frameCount;
                EnsurePartFitClient();
                if (_partFitClient!.TryBegin(session.LocalPlayerId, selectedId, selectedState.Revision, PartFitOperation.Remove))
                    ProcessPartFitting(session);
                input.Use();
            }
            return true;
        }
    }
}
