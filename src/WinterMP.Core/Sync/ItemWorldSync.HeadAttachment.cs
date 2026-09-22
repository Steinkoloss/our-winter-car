using System;
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
        private CylinderHeadState? _headPublished, _headReceived;
        private GuestCylinderHead? _headView;
        private Rigidbody? _headObservedBody;
        private float _nextHeadPoll, _nextHeadKeepalive;
        private bool _headCaptureFailed;

        private static bool HeadIds(out uint head, out uint block)
        {
            head = block = 0;
            var c = SyncCatalog.CylinderHead;
            return c != null && PartIdentity.TryItemId(c.NativeId, out head) && PartIdentity.TryItemId(c.ParentNativeId, out block);
        }

        private void TrackCylinderHead(PlayMakerFSM data, uint id)
        {
            if (_headView != null || SessionManager.Instance?.IsHost != false || !GuestSaveGuard.ProtectWorld
                || !HeadIds(out uint head, out _) || id != head) return;
            var phase = NativePartIdentity.Phase(data);
            if (phase == NativePartPhase.Loose || phase == NativePartPhase.Fitted)
            { _headView = new GuestCylinderHead(data); _headView.BindTurns(RequestHeadFastener); }
        }

        internal bool CylinderHeadReady(PlayMakerFSM data) => _headView != null && _headView.Data == data
            && _headView.Ready && data != null && data.gameObject.activeInHierarchy;

        private NativePartPhase PresentedPartPhase(PlayMakerFSM? data)
        {
            if (_headView == null || _headView.Data != data) return NativePartIdentity.Phase(data);
            if (!_headView.Ready || _headReceived == null || _headView.AppliedRevision != _headReceived.Revision) return NativePartPhase.Unavailable;
            return _headReceived.ParentId == 0 ? NativePartPhase.Loose : NativePartPhase.Fitted;
        }

        internal CylinderHeadState? BuildCylinderHeadState()
        {
            if (SessionManager.Instance?.IsHost != true || !HeadIds(out uint id, out uint parentId)
                || !_nativeParts.TryGetValue(id, out var data) || data == null || !data.enabled || !data.Fsm.Started
                || !data.gameObject.activeInHierarchy) return null;
            try
            {
                var phase = NativePartIdentity.Phase(data);
                var body = data.GetComponent<Rigidbody>();
                var state = new CylinderHeadState { NetId = id, Position = data.transform.position.ToNet(),
                    Rotation = data.transform.rotation.ToNet(), Mass = body == null ? 0 : body.mass };
                if (phase == NativePartPhase.Fitted)
                {
                    if (body != null || !_nativeParts.TryGetValue(parentId, out var block) || block == null) return null;
                    var point = ScenePath.FindRelative(block.transform, SyncCatalog.CylinderHead!.MountPath);
                    if (point == null || data.transform.parent != point
                        || data.FsmVariables.FindFsmGameObject("InstallPoint")?.Value != point.gameObject) return null;
                    var mount = EngineBlockDataFsm(point.gameObject, "Data");
                    if (mount.FsmVariables.FindFsmBool("Installed")?.Value != true
                        || mount.FsmVariables.FindFsmGameObject("ActivePart")?.Value != data.gameObject
                        || mount.FsmVariables.FindFsmGameObject("AssemblyPoint")?.Value != point.gameObject) return null;
                    if (data.transform.localPosition.sqrMagnitude > .000001f
                        || Quaternion.Angle(data.transform.localRotation, Quaternion.identity) > .01f
                        || (data.transform.localScale - Vector3.one).sqrMagnitude > .000001f)
                        throw new InvalidOperationException("Native head mount pose changed.");
                    state.ParentId = parentId;
                }
                else if (phase != NativePartPhase.Loose || body == null || data.ActiveStateName != "Stop") return null;
                CaptureHeadFasteners(data, state);
                if (!CylinderHeadPolicy.Valid(state)) throw new InvalidOperationException("Invalid native head pose or mass.");
                state.Revision = _headPublished?.Revision ?? 1;
                if (_headPublished != null && (!CylinderHeadPolicy.SameAttachment(_headPublished, state)
                    || !CylinderHeadPolicy.SameFasteners(_headPublished, state)
                    || !ReferenceEquals(body, _headObservedBody))) state.Revision = unchecked(state.Revision + 1);
                _headPublished = CylinderHeadPolicy.Copy(state); _headObservedBody = body; _headCaptureFailed = false;
                return state;
            }
            catch (Exception e)
            {
                if (!_headCaptureFailed) WinterMPPlugin.Log.LogWarning("WorldSync: cylinder head attachment unavailable: " + e.Message);
                _headCaptureFailed = true; return null;
            }
        }

        internal void OnCylinderHeadState(CylinderHeadState state)
        {
            if (SessionManager.Instance?.IsHost != false || !HeadIds(out uint id, out uint parent)
                || state.NetId != id || (state.ParentId != 0 && state.ParentId != parent)
                || !CylinderHeadPolicy.CanReceive(_headReceived, state)) return;
            _headReceived = CylinderHeadPolicy.Copy(state);
        }

        private void ProcessCylinderHead(SessionManager session)
        {
            if (Time.unscaledTime < _nextHeadPoll || !HeadIds(out uint id, out _)) return;
            _nextHeadPoll = Time.unscaledTime + .2f;
            if (session.IsHost)
            {
                uint? previous = _headPublished?.Revision;
                var state = BuildCylinderHeadState();
                if (state != null && (previous != state.Revision || Time.unscaledTime >= _nextHeadKeepalive))
                { session.SendWorldMessage(state, Channel.ReliableOrdered); _nextHeadKeepalive = Time.unscaledTime + 2f; }
                return;
            }
            var view = _headView;
            if (view == null || view.Data == null || view.Failed) return;
            if (_headReceived == null) { _bridge.RequestObjectState(id); return; }
            try
            {
                if (!view.Paused)
                {
                    if (_vehicles == null || !_vehicles.PrepareGuestDamageIsolationNow()
                        || !_vehicles.PrepareGuestEngineProtectionForIsolation()) return;
                    _bridge.ForgetNativePartBindings(view.Data, descendants: false);
                    view.Pause();
                }
                Transform? parent = null;
                if (_headReceived.ParentId != 0)
                {
                    if (_nativeParts.TryGetValue(_headReceived.ParentId, out var block) && block != null)
                        parent = ScenePath.FindRelative(block.transform, SyncCatalog.CylinderHead!.MountPath);
                    if (parent == null || !parent.gameObject.activeInHierarchy || ScenePath.RelativeTo(parent, view.Data.transform) != null)
                    { RemoveNativeItemMotion(id); view.Hold(); return; }
                }
                if (view.Ready && view.SameAttachment(_headReceived) && (parent == null || view.Data.transform.parent == parent))
                {
                    if (view.AppliedRevision != _headReceived.Revision) view.RefreshFasteners(_headReceived);
                    else view.TickFasteners();
                    return;
                }
                RemoveNativeItemMotion(id);
                view.Apply(_headReceived, parent);
                if (parent == null)
                {
                    TryScanNativePart(view.Data.GetComponent<Rigidbody>());
                    if (_items.TryGetValue(id, out var item))
                        ApplySnapshotPose(item, _headReceived.Position.ToUnity(), _headReceived.Rotation.ToUnity());
                }
                SyncEventLog.Record("head-attachment", id.ToString("X8") + " revision=" + _headReceived.Revision
                    + (parent == null ? " loose" : " fitted"));
            }
            catch (Exception e)
            {
                RemoveNativeItemMotion(id); view.Failed = true; view.Hold();
                WinterMPPlugin.Log.LogWarning("WorldSync: cylinder head presentation disabled: " + e.Message);
            }
        }

        private void ClearCylinderHead()
        {
            if (_headView != null)
            {
                if (HeadIds(out uint id, out _)) RemoveNativeItemMotion(id);
                try { _headView.Restore(); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: cylinder head restore: " + e.Message); }
            }
            _headView = null; _headPublished = _headReceived = null; _headObservedBody = null;
            _headFasteners = null; _headFastenersFailed = false;
            _nextHeadPoll = _nextHeadKeepalive = 0; _headCaptureFailed = false;
        }
    }
}
