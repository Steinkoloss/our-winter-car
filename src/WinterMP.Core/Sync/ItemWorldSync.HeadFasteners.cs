using System;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private CylinderHeadFasteners? _headFasteners;
        private bool _headFastenersFailed;

        private void CaptureHeadFasteners(PlayMakerFSM data, CylinderHeadState state)
        {
            if (_headFastenersFailed) return;
            try
            {
                if (_headFasteners == null) _headFasteners = new CylinderHeadFasteners(data);
                if (!_headFasteners.Capture(state)) throw new InvalidOperationException("Head fastener values unavailable.");
            }
            catch (Exception e)
            {
                state.FastenersAvailable = false; state.Tightness = 0; Array.Clear(state.Fasteners, 0, state.Fasteners.Length);
                _headFastenersFailed = true;
                WinterMPPlugin.Log.LogWarning("WorldSync: head fasteners disabled: " + e.Message);
            }
        }
        private void OnHostHeadFastener(SessionManager session, PartFitRequest request, byte actor)
        {
            bool applying = _bridge.ApplyingRemote;
            try
            {
                var state = BuildCylinderHeadState(); var binding = _headFasteners;
                var data = binding?.Bolts[0].PartData;
                var mount = data == null ? null : GetHeadFitMount(data);
                bool ready = state?.ParentId != 0 && binding != null && binding.Ready(request.SlotIndex)
                    && data != null && FitFsmReady(data) && mount != null && FitFsmReady(mount) && mount.ActiveStateName == "UPDATE"
                    && mount.FsmVariables.FindFsmFloat("Tightness")?.Value == state?.Tightness;
                bool near = ready && GuestNearPackage(session, actor, binding!.Bolts[request.SlotIndex - 1].Fsm.transform.position);
                var status = CylinderHeadPolicy.CheckFastener(request, state, ready, near,
                    binding == null || Time.unscaledTime < binding.NextTurnAt);
                var receipt = _partFitLedger.Begin(request, actor, status); if (receipt == null) return;
                if (status == PartFitStatus.Pending)
                {
                    _bridge.ApplyingRemote = true; binding!.NextTurnAt = Time.unscaledTime + PartToolScrewPolicy.Cooldown;
                    binding.Turn(request.SlotIndex, request.Operation, state!);
                    receipt = _partFitLedger.Complete(request, true)!;
                }
                SendPartFitReceipt(session, receipt);
                SyncEventLog.Record("head-fastener", actor + ":" + request.Sequence + " slot=" + request.SlotIndex + " " + receipt.Status);
            }
            catch (Exception e)
            {
                _headFastenersFailed = true;
                var receipt = _partFitLedger.Complete(request, false) ?? _partFitLedger.Begin(request, actor, PartFitStatus.Failed);
                if (receipt != null) SendPartFitReceipt(session, receipt);
                WinterMPPlugin.Log.LogWarning("WorldSync: head fastener request failed: " + e.Message);
            }
            finally { _bridge.ApplyingRemote = applying; }
        }
        private bool RequestHeadFastener(byte slot, PartFitOperation operation)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected || _bridge.ApplyingRemote
                || _headView?.Ready != true || _headReceived == null || _headView.AppliedRevision != _headReceived.Revision
                || CylinderHeadPolicy.CheckFastener(new PartFitRequest { ItemId = _headReceived.NetId, ExpectedRevision = _headReceived.Revision,
                    SlotIndex = slot, Operation = operation }, _headReceived, true, true, false) != PartFitStatus.Pending) return false;
            EnsurePartFitClient();
            bool began = _partFitClient!.TryBegin(session.LocalPlayerId, _headReceived.NetId, _headReceived.Revision, operation, slot);
            if (began) ProcessPartFitting(session); return began;
        }
    }
}
