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
        private sealed class HeadFitting
        {
            internal PartFitRequest Request = null!;
            internal PlayMakerFSM Data = null!, Mount = null!;
            internal FsmState Check = null!, Commit = null!;
            internal FsmStateAction CheckHook = null!, CommitHook = null!;
            internal float Deadline;
            internal int CheckedFrame = -1;
            internal bool Committed;
        }
        private HeadFitting? _headFitting;
        private int _headHeldFrame = -10;

        private void OnHostHeadFit(SessionManager session, PartFitRequest request, byte actor)
        {
            try
            {
                _nativeParts.TryGetValue(request.ItemId, out var data);
                var mount = data == null ? null : GetHeadFitMount(data);
                var state = BuildCylinderHeadState();
                bool available = data != null && mount != null && FitFsmReady(data) && FitFsmReady(mount);
                bool install = request.Operation == PartFitOperation.Install;
                bool idle = available && (install ? data!.ActiveStateName == "Stop" && mount!.ActiveStateName == "Idle"
                    && mount.FsmVariables.FindFsmBool("Installed")?.Value == false
                    : mount!.ActiveStateName == "UPDATE" && (data!.ActiveStateName == "Mouse off" || data.ActiveStateName == "Mouse over"));
                var status = CylinderHeadPolicy.CheckRequest(request, state, available,
                    available && GuestNearPackage(session, actor, data!.transform.position) && GuestNearPackage(session, actor, mount!.transform.position),
                    available && HeadAtMount(data!, mount!), GuestOwnsFitPart(request.ItemId, actor), idle,
                    data?.FsmVariables.FindFsmFloat("Tightness")?.Value ?? float.NaN);
                var receipt = _partFitLedger.Begin(request, actor, status);
                if (receipt == null) return;
                if (status == PartFitStatus.Pending)
                {
                    var fitting = new HeadFitting { Request = PartFitLedger.Copy(request), Data = data!, Mount = mount!, Deadline = Time.unscaledTime + 3 };
                    _headFitting = fitting;
                    fitting.Check = FsmHook.FindState(mount!, install ? "Allow install?" : "Allow removal?")!;
                    fitting.Commit = FsmHook.FindState(mount!, install ? "Near" : "Remove part")!;
                    fitting.CheckHook = new FsmHookAction(() => fitting.CheckedFrame = Time.frameCount);
                    fitting.CommitHook = new FsmHookAction(() => ConfirmHeadFit(session, fitting));
                    PrependHeadHook(fitting.Check, fitting.CheckHook); PrependHeadHook(fitting.Commit, fitting.CommitHook);
                    if (install) data!.SendEvent(SyncCatalog.ReplacementParts!["fitEvent"]);
                    else FsmHook.FireRemoteEntry(data!, SyncCatalog.ReplacementParts!["removeState"]);
                    // Both native prerequisite chains are synchronous. Never leave
                    // a guest preview armed for an unrelated later host mouse click.
                    if (_headFitting == fitting && !fitting.Committed) FinishHeadFit(session, false, "Native prerequisite refused the operation.");
                }
                SendPartFitReceipt(session, receipt);
                SyncEventLog.Record("head-fit-request", request.ItemId.ToString("X8") + " " + actor + ":" + request.Sequence + " " + status);
            }
            catch (Exception e)
            {
                if (_headFitting != null) FinishHeadFit(session, false, e.Message);
                else
                {
                    var receipt = _partFitLedger.Begin(request, actor, PartFitStatus.Failed);
                    if (receipt != null) SendPartFitReceipt(session, receipt);
                    WinterMPPlugin.Log.LogWarning("WorldSync: cylinder head interaction: " + e.Message);
                }
            }
        }
        private static void PrependHeadHook(FsmState state, FsmStateAction action)
        {
            var actions = new List<FsmStateAction>(state.Actions); actions.Insert(0, action); state.Actions = actions.ToArray(); action.Init(state);
        }
        private void ConfirmHeadFit(SessionManager session, HeadFitting fitting)
        {
            if (_headFitting != fitting || fitting.Committed) return;
            try
            {
                bool install = fitting.Request.Operation == PartFitOperation.Install;
                if (fitting.CheckedFrame != Time.frameCount || !FitFsmReady(fitting.Data) || !FitFsmReady(fitting.Mount)
                    || _headPublished?.Revision != fitting.Request.ExpectedRevision
                    || fitting.Mount.FsmVariables.FindFsmGameObject("ActivePart")?.Value != fitting.Data.gameObject
                    || GetHeadFitMount(fitting.Data) != fitting.Mount
                    || !GuestNearPackage(session, fitting.Request.PlayerId, fitting.Data.transform.position)
                    || !GuestNearPackage(session, fitting.Request.PlayerId, fitting.Mount.transform.position)
                    || (install ? NativePartIdentity.Phase(fitting.Data) != NativePartPhase.Loose || !HeadAtMount(fitting.Data, fitting.Mount)
                        || !GuestOwnsFitPart(fitting.Request.ItemId, fitting.Request.PlayerId)
                        || fitting.Mount.FsmVariables.FindFsmBool("Installed")?.Value != false
                    : NativePartIdentity.Phase(fitting.Data) != NativePartPhase.Fitted
                        || !PartRemovalPolicy.Unbolted(fitting.Data.FsmVariables.FindFsmFloat("Tightness")?.Value ?? float.NaN)))
                    throw new InvalidOperationException("Cylinder head changed before native confirmation.");
                fitting.Committed = true;
                if (install) fitting.Mount.SendEvent(SyncCatalog.ReplacementParts!["fitConfirmEvent"]);
            }
            catch (Exception e)
            {
                // A removal guard runs at the write-state boundary. Redirect before
                // its native actions can detach the head after a failed recheck.
                if (fitting.Request.Operation == PartFitOperation.Remove)
                {
                    FsmHook.EnsureRemoteEntry(fitting.Mount, "UPDATE"); FsmHook.FireRemoteEntry(fitting.Mount, "UPDATE");
                }
                FinishHeadFit(session, false, e.Message);
            }
        }
        private void ProcessHeadFitting(SessionManager session)
        {
            var fitting = _headFitting; if (fitting == null) return;
            try
            {
                var state = BuildCylinderHeadState();
                bool install = fitting.Request.Operation == PartFitOperation.Install;
                if (fitting.Committed && state != null && state.NetId == fitting.Request.ItemId
                    && (install ? state.ParentId != 0 : state.ParentId == 0 && fitting.Mount.FsmVariables.FindFsmBool("Installed")?.Value == false))
                {
                    if (!install) TryScanNativePart(fitting.Data.GetComponent<Rigidbody>());
                    session.SendWorldMessage(state, Channel.ReliableOrdered); FinishHeadFit(session, true, ""); return;
                }
                if (!FitFsmReady(fitting.Data) || !FitFsmReady(fitting.Mount) || Time.unscaledTime >= fitting.Deadline)
                    throw new InvalidOperationException("Native cylinder head operation did not settle.");
            }
            catch (Exception e) { FinishHeadFit(session, false, e.Message); }
        }
        private void FinishHeadFit(SessionManager session, bool accepted, string reason)
        {
            var fitting = _headFitting; if (fitting == null) return;
            ClearHeadFitOperation();
            if (!accepted && fitting.Committed) _headFitFailed = true;
            var receipt = _partFitLedger.Complete(fitting.Request, accepted);
            if (receipt != null) SendPartFitReceipt(session, receipt);
            SyncEventLog.Record("head-fit", fitting.Request.ItemId.ToString("X8") + " " + (accepted ? "accepted" : reason));
            if (!accepted && fitting.Committed) WinterMPPlugin.Log.LogWarning("WorldSync: cylinder head interaction paused: " + reason);
        }
        private void ClearHeadFitOperation()
        {
            var fitting = _headFitting; _headFitting = null; if (fitting == null) return;
            if (fitting.Check != null && fitting.CheckHook != null) RemoveReplacementHook(fitting.Check, fitting.CheckHook);
            if (fitting.Commit != null && fitting.CommitHook != null) RemoveReplacementHook(fitting.Commit, fitting.CommitHook);
            if (!fitting.Committed && FitFsmReady(fitting.Mount) && fitting.Mount.FsmVariables.FindFsmGameObject("ActivePart")?.Value == fitting.Data.gameObject
                && (fitting.Mount.ActiveStateName == "Near" || fitting.Mount.ActiveStateName == "Far")) fitting.Mount.SendEvent("BACK");
        }
        private void ClearHeadFitting()
        {
            ClearHeadFitOperation(); _headFitData = _headFitMount = null; _headFitFailed = false; _headHeldFrame = -10;
        }

        private bool DrawHeadFitPrompt(SessionManager session)
        {
            var view = _headView; var state = _headReceived;
            if (view == null || !view.Ready || view.Failed || state == null || view.AppliedRevision != state.Revision || _headFitFailed) return false;
            var data = view.Data; var mount = GetHeadFitMount(data); if (mount == null) return false;
            bool install = state.ParentId == 0;
            if (install)
            {
                if (!_items.TryGetValue(state.NetId, out var item) || !item.LocallyOwned || !HeadAtMount(data, mount)) return false;
                if (IsPlayerHeldItem(item)) _headHeldFrame = Time.frameCount;
                else if (Time.frameCount - _headHeldFrame > 1) return false;
            }
            else
            {
                if (!state.FastenersAvailable || !PartRemovalPolicy.Unbolted(state.Tightness)) return false;
                var camera = Camera.main; var box = data.FsmVariables.FindFsmObject("Collider")?.Value as BoxCollider;
                if (camera == null || box == null || !PartInteractionHandFree()) return false;
                var ray = camera.ScreenPointToRay(Input.mousePosition); float limit = 1;
                if (Physics.Raycast(ray, out var hit, limit, 1 << 19)) limit = hit.distance;
                var origin = box.transform.InverseTransformPoint(ray.origin);
                var direction = box.transform.InverseTransformPoint(ray.origin + ray.direction) - origin;
                if (!PartRemovalPolicy.RayBox(origin.ToNet(), direction.ToNet(), box.center.ToNet(), box.size.ToNet(), limit, out _)) return false;
            }
            GUI.Box(new Rect(Screen.width / 2f - 150f, Screen.height / 2f + 55f, 300f, 28f),
                install ? "Left click to fit the cylinder head" : "Right click to remove the cylinder head");
            var input = Event.current;
            if (input.type == EventType.MouseDown && input.button == (install ? 0 : 1) && _partFitInputFrame != Time.frameCount)
            {
                _partFitInputFrame = Time.frameCount;
                RequestHeadFit(session, install ? PartFitOperation.Install : PartFitOperation.Remove); input.Use();
            }
            return true;
        }
        private bool RequestHeadFit(SessionManager session, PartFitOperation operation)
        {
            if (session.IsHost || _headReceived == null || _headView?.Ready != true || _headFitFailed) return false;
            EnsurePartFitClient();
            bool began = _partFitClient!.TryBegin(session.LocalPlayerId, _headReceived.NetId, _headReceived.Revision, operation);
            if (began) ProcessPartFitting(session); return began;
        }
    }
}
