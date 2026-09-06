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
        private sealed class PartFitting
        {
            public PartFitRequest Request = null!;
            public ReplacementBinding Part = null!;
            public PlayMakerFSM Mount = null!;
            public float Deadline;
            public bool Committed;
            public FsmState? EntryState;
            public FsmStateAction? EntryGuard;
            public FsmState? CheckState;
            public FsmStateAction? CheckGuard;
            public int CheckedFrame = -1;
            public FsmState? SelectionState;
            public FsmStateAction? SelectionGuard;
        }
        private readonly PartFitLedger _partFitLedger = new PartFitLedger();
        private PartFitClient? _partFitClient;
        private PartFitting? _partFitting;
        private int _partFitInputFrame = -1;
        private bool _partFitPromptFailed;

        internal void OnHostPartFit(PartFitRequest request, byte actor)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            var replay = _partFitLedger.Inspect(request, actor, out bool begin);
            if (!begin) { if (replay != null) SendPartFitReceipt(session, replay); return; }
            if (_partFitting != null)
            {
                var busy = _partFitLedger.Begin(request, actor, PartFitStatus.Busy);
                if (busy != null) SendPartFitReceipt(session, busy);
                return;
            }
            if (request.Operation == PartFitOperation.Remove) { OnHostPartRemoval(request, actor, session); return; }
            try
            {
                _replacementParts.TryGetValue(request.ItemId, out var part);
                byte slot = 0;
                var mount = part == null || part.Replica ? null : GetPartFitMount(part, out slot);
                var state = BuildReplacementPartState(request.ItemId);
                bool available = mount != null && part != null && FitFsmReady(part.Data) && FitFsmReady(mount);
                bool near = available && GuestNearPackage(session, actor, part!.Data.transform.position)
                    && GuestNearPackage(session, actor, mount!.transform.position);
                bool inRange = available && PartAtMount(part!, mount!);
                bool free = mount != null && !mount.FsmVariables.FindFsmBool(SyncCatalog.ReplacementParts!["mountInstalledVariable"]).Value;
                bool idle = available && mount!.ActiveStateName == SyncCatalog.ReplacementParts!["fitMountIdleState"]
                    && part!.Data.ActiveStateName == SyncCatalog.ReplacementParts!["itemStopState"] && SlotInstallerIdle(part);
                var status = PartFitLedger.Check(request, state, available, near, inRange, free,
                    GuestOwnsFitPart(request.ItemId, actor), _partFitting != null || !idle, slot);
                var receipt = _partFitLedger.Begin(request, actor, status);
                if (receipt == null) return;
                if (status == PartFitStatus.Pending)
                {
                    _partFitting = new PartFitting { Request = PartFitLedger.Copy(request), Part = part!, Mount = mount!,
                        Deadline = Time.unscaledTime + 3f };
                    var fitting = _partFitting;
                    fitting.EntryState = FsmHook.FindState(mount!, SyncCatalog.ReplacementParts!["fitNearState"])!;
                    fitting.EntryGuard = new FsmHookAction(() => ConfirmPartFitting(fitting));
                    var actions = new List<FsmStateAction>(fitting.EntryState.Actions);
                    actions.Insert(0, fitting.EntryGuard); fitting.EntryState.Actions = actions.ToArray();
                    fitting.CheckState = FsmHook.FindState(mount!, SyncCatalog.ReplacementParts!["fitAllowState"])!;
                    fitting.CheckGuard = new FsmHookAction(() => fitting.CheckedFrame = Time.frameCount);
                    actions = new List<FsmStateAction>(fitting.CheckState.Actions);
                    actions.Insert(0, fitting.CheckGuard); fitting.CheckState.Actions = actions.ToArray();
                    GuardPartSlotSelection(session, fitting);
                    // The native entry checks occupancy, selects its own mount, and
                    // traverses its prerequisite/distance checks before reaching Near.
                    part!.Data.SendEvent(SyncCatalog.ReplacementParts!["fitEvent"]);
                    // Slot previews share one native installer. Never leave an
                    // unconfirmed guest selection waiting for a later host click.
                    if (slot != 0 && _partFitting == fitting && !fitting.Committed)
                        throw new InvalidOperationException("Native slot selection did not confirm immediately.");
                }
                SendPartFitReceipt(session, receipt);
                SyncEventLog.Record("part-fit-request", actor + ":" + request.Sequence + " " + request.ItemId.ToString("X8")
                    + " slot:" + request.SlotIndex + " " + status);
            }
            catch (Exception e)
            {
                if (_partFitting != null && _partFitting.Request.PlayerId == actor
                    && _partFitting.Request.Token == request.Token && _partFitting.Request.Sequence == request.Sequence)
                    FailPartFitting(session, e.Message);
                var receipt = _partFitLedger.Complete(request, false) ?? _partFitLedger.Begin(request, actor, PartFitStatus.Failed);
                if (receipt != null) SendPartFitReceipt(session, receipt);
            }
        }

        private bool GuestOwnsFitPart(uint id, byte actor)
        {
            if (!_items.TryGetValue(id, out var item)) return false;
            // The fitting click also releases the native grabber. Its final
            // transform can reach the host just before the reliable request.
            float age = Time.unscaledTime - item.LastRemoteReleaseAt;
            return PartFitLedger.HasAuthority(actor, item.LocallyOwned, item.RemoteOwner, item.LastRemoteSequenceOwner, age);
        }

        internal bool IsReplacementPart(PlayMakerFSM data)
        {
            var c = SyncCatalog.ReplacementParts;
            if (c == null || data == null) return false;
            string? nativeId = data.FsmVariables.FindFsmString(c["itemIdVariable"])?.Value;
            if (nativeId == null) return false;
            foreach (var factory in _replacementFactories.Values)
                if (factory.Rule.Identity.TryId(nativeId, out _)) return true;
            return false;
        }

        private static bool FitFsmReady(PlayMakerFSM fsm) => fsm != null && fsm.enabled && fsm.Fsm.Started
            && fsm.gameObject.activeInHierarchy;

        private static bool PartAtMount(ReplacementBinding part, PlayMakerFSM mount)
        {
            var c = SyncCatalog.ReplacementParts!;
            var point = mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value;
            if (part.Factory.Rule.SlotCount != 0)
                return point == mount.gameObject && part.SlotInstaller != null
                    && PartFitLedger.WithinMount(part.Data.transform.position.ToNet(), point.transform.position.ToNet(), SlotTolerance(part.SlotInstaller));
            var near = FsmHook.FindState(mount, c["fitNearState"]);
            FsmFloat? tolerance = null;
            if (near != null)
                foreach (var action in near.Actions)
                    if (action.GetType().Name == "FloatCompare") { tolerance = PackageField<FsmFloat>(action, "float2"); break; }
            return point == mount.gameObject && tolerance != null
                && PartFitLedger.WithinMount(part.Data.transform.position.ToNet(), point.transform.position.ToNet(), tolerance.Value);
        }

        private void ConfirmPartFitting(PartFitting fitting)
        {
            var session = SessionManager.Instance;
            if (_partFitting != fitting || fitting.Committed || session == null || !session.IsHost) return;
            try
            {
                if (fitting.CheckedFrame != Time.frameCount)
                    throw new InvalidOperationException("Fitting prerequisites are no longer current.");
                RequireFitCandidate(session, fitting);
                // Run at Near entry, directly after the native prerequisite and
                // distance transitions, before host mouse input or another tick.
                fitting.Committed = true;
                fitting.Mount.SendEvent(SyncCatalog.ReplacementParts!["fitConfirmEvent"]);
            }
            catch (Exception e) { FailPartFitting(session, e.Message); }
        }

        private void RequireFitCandidate(SessionManager session, PartFitting fitting)
        {
            var state = BuildReplacementPartState(fitting.Request.ItemId);
            if (state == null || state.Revision != fitting.Request.ExpectedRevision || state.Installed
                || Time.unscaledTime >= fitting.Deadline
                || !GuestNearPackage(session, fitting.Request.PlayerId, fitting.Part.Data.transform.position)
                || !GuestNearPackage(session, fitting.Request.PlayerId, fitting.Mount.transform.position)
                || !GuestOwnsFitPart(fitting.Request.ItemId, fitting.Request.PlayerId)
                || !PartAtMount(fitting.Part, fitting.Mount)
                || (fitting.Request.SlotIndex == 0
                    ? fitting.Mount.FsmVariables.FindFsmGameObject(SyncCatalog.ReplacementParts!["mountPartVariable"]).Value != fitting.Part.Data.gameObject
                    : !PartSlotCandidateMatches(fitting)))
                throw new InvalidOperationException("Fitting candidate moved or changed owner.");
        }

        private void ProcessPartFitting(SessionManager session)
        {
            if (!session.IsHost)
            {
                var retry = _partFitClient?.Poll(Time.unscaledTime);
                if (retry != null) session.SendWorldMessage(retry, Channel.ReliableOrdered);
                return;
            }
            var fitting = _partFitting;
            if (fitting == null) return;
            if (fitting.Request.Operation == PartFitOperation.Remove) { ProcessPartRemoval(session, fitting); return; }
            try
            {
                var c = SyncCatalog.ReplacementParts!;
                if (!FitFsmReady(fitting.Part.Data) || !FitFsmReady(fitting.Mount))
                    throw new InvalidOperationException("Fitting target became unavailable.");
                var state = BuildReplacementPartState(fitting.Request.ItemId);
                if (state != null && state.Installed && PartAttachmentPolicy.HasAttachment(state))
                {
                    if (fitting.Request.SlotIndex != 0 && (state.AssemblyId != fitting.Request.SlotIndex
                        || fitting.Part.Data.FsmVariables.FindFsmGameObject(SyncCatalog.ReplacementParts!["installPointVariable"])?.Value != fitting.Mount.gameObject))
                        throw new InvalidOperationException("Native fitting settled at a different slot.");
                    FinishPartFitting(session, true); return;
                }
                // Installation destroys the body's physics before its next-frame
                // reparent. Once committed, observe the result without repeating it.
                if (NativePartIdentity.Phase(fitting.Part.Data) == NativePartPhase.Fitted) fitting.Committed = true;
                if (Time.unscaledTime >= fitting.Deadline) throw new InvalidOperationException("Native fitting did not settle.");
                if (fitting.Committed) return;
                RequireFitCandidate(session, fitting);
                if (fitting.Mount.ActiveStateName == c["fitMountIdleState"])
                    throw new InvalidOperationException("Native mount refused the fitting prerequisites.");
            }
            catch (Exception e) { FailPartFitting(session, e.Message); }
        }

        private void CancelPartFitPreview(PartFitting fitting)
        {
            if (fitting.Request.Operation == PartFitOperation.Remove) { CancelPartRemoval(fitting); return; }
            if (fitting.Request.SlotIndex != 0) { CancelPartSlotPreview(fitting); return; }
            var c = SyncCatalog.ReplacementParts;
            if (c == null || !FitFsmReady(fitting.Mount) || fitting.Part.Data == null) return;
            if (fitting.Mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != fitting.Part.Data.gameObject) return;
            string state = fitting.Mount.ActiveStateName;
            if (state == c["fitNearState"] || state == c["fitFarState"])
                fitting.Mount.SendEvent(c["fitCancelEvent"]);
        }

        private void FailPartFitting(SessionManager session, string reason)
        {
            var fitting = _partFitting;
            if (fitting == null) return;
            try { CancelPartFitPreview(fitting); }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: fitting preview cleanup: " + e.Message); }
            if (fitting.Committed)
            {
                if (fitting.Request.Operation == PartFitOperation.Remove) fitting.Part.RemovalFailed = true;
                else fitting.Part.FitFailed = true;
            }
            SyncEventLog.Record("part-fit-failed", fitting.Request.ItemId.ToString("X8") + " " + reason);
            WinterMPPlugin.Log.LogWarning("WorldSync: part fitting: " + reason);
            FinishPartFitting(session, false);
        }

        private void FinishPartFitting(SessionManager session, bool accepted)
        {
            var fitting = _partFitting;
            _partFitting = null;
            if (fitting == null) return;
            RemovePartFitGuard(fitting);
            var receipt = _partFitLedger.Complete(fitting.Request, accepted);
            if (receipt != null) SendPartFitReceipt(session, receipt);
            SyncEventLog.Record(fitting.Request.Operation == PartFitOperation.Remove ? "part-remove" : "part-fit", fitting.Request.ItemId.ToString("X8") + (accepted ? " accepted" : " failed"));
        }

        private void SendPartFitReceipt(SessionManager session, PartFitReceipt receipt)
        {
            var state = BuildReplacementPartState(receipt.ItemId);
            if (state != null) session.SendWorldMessage(state, Channel.ReliableOrdered);
            else if (_spawnLifecycle.IsRetired(receipt.ItemId))
                session.SendWorldMessage(new ItemDespawn { ItemId = receipt.ItemId }, Channel.ReliableOrdered);
            session.SendWorldMessage(receipt, Channel.ReliableOrdered);
        }

        internal void DrawPartFitPrompt()
        {
            if (_partFitPromptFailed) return;
            try { DrawPartFitPromptCore(); }
            catch (Exception e)
            {
                _partFitPromptFailed = true;
                WinterMPPlugin.Log.LogWarning("WorldSync: fitting prompt disabled: " + e.Message);
                SyncEventLog.Record("part-fit-prompt-disabled", e.Message);
            }
        }

        private void DrawPartFitPromptCore()
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected
                || DeathSyncManager.Instance?.IsLocalDead == true || Time.timeScale == 0) return;
            if (_partFitClient?.Pending == true)
            { GUI.Box(new Rect(Screen.width / 2f - 140f, Screen.height / 2f + 55f, 280f, 28f), _partFitClient.Operation == PartFitOperation.Remove ? "Waiting for the host to remove the part…" : "Waiting for the host to fit the part…"); return; }
            if (!TryGetLocalPlayerPosition(out var player)) return;
            if (DrawPartRemovalPrompt(session)) return;
            foreach (var pair in _replacementParts)
            {
                var part = pair.Value;
                if (!part.Replica || part.FittedPresentation || part.Data == null || !part.Data.gameObject.activeInHierarchy
                    || !_items.TryGetValue(pair.Key, out var item) || !item.LocallyOwned
                    || (part.Data.transform.position - player).sqrMagnitude > 9f) continue;
                if (IsPlayerHeldItem(item)) part.FitHeldFrame = Time.frameCount;
                // Native PickUp can process this frame's left click before OnGUI,
                // detaching the held part. Retain exactly that click's candidate.
                else if (Time.frameCount - part.FitHeldFrame > 1) continue;
                var state = _replacementReplica?.Get(pair.Key);
                if (state == null || !part.Factory.Rule.Identity.CanCreate(state)) continue;
                var mount = GetPartFitMount(part, out byte slot);
                if (mount == null || !FitFsmReady(mount) || !PartAtMount(part, mount)
                    || !SlotInstallerIdle(part) || mount.FsmVariables.FindFsmBool(SyncCatalog.ReplacementParts!["mountInstalledVariable"]).Value) continue;
                GUI.Box(new Rect(Screen.width / 2f - 140f, Screen.height / 2f + 55f, 280f, 28f), "Left click to fit this part");
                var input = Event.current;
                if (input.type == EventType.MouseDown && input.button == 0 && _partFitInputFrame != Time.frameCount)
                {
                    _partFitInputFrame = Time.frameCount;
                    EnsurePartFitClient();
                    if (_partFitClient!.TryBegin(session.LocalPlayerId, pair.Key, state.Revision, PartFitOperation.Install, slot)) ProcessPartFitting(session);
                    input.Use();
                }
                return;
            }
        }

        private void EnsurePartFitClient()
        {
            if (_partFitClient != null) return;
            ulong token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            _partFitClient = new PartFitClient(token == 0 ? 1 : token);
        }

        internal void OnPartFitReceipt(PartFitReceipt receipt)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || receipt.PlayerId != session.LocalPlayerId
                || _partFitClient?.Receive(receipt) != true) return;
            if (receipt.Status == PartFitStatus.Accepted) return;
            if (receipt.Operation == PartFitOperation.Remove)
            {
                session.AddSystemChat(receipt.Status == PartFitStatus.Bolted ? "Loosen the bolts before removing this part."
                    : receipt.Status == PartFitStatus.Blocked ? "Another fitted part is blocking removal."
                    : receipt.Status == PartFitStatus.Stale ? "The part changed. Try removing it again."
                    : receipt.Status == PartFitStatus.TooFar ? "Move closer to the part and try again."
                    : receipt.Status == PartFitStatus.Busy ? "Another part operation is in progress. Try again."
                    : "The host could not remove this part. Check its mounting requirements.");
                return;
            }
            session.AddSystemChat(receipt.Status == PartFitStatus.TooFar ? "Hold the part closer to its fitting point and try again."
                : receipt.Status == PartFitStatus.Busy ? "The part or fitting point is in use. Try again."
                : receipt.Status == PartFitStatus.Stale ? "The part changed. Try fitting it again."
                : receipt.Status == PartFitStatus.Blocked ? "The fitting point is occupied."
                : "The host could not fit this part. Check its mounting requirements.");
        }

        internal void ForgetPartFittingPlayer(byte player)
        {
            if (_partFitting?.Request.PlayerId == player && !_partFitting.Committed && SessionManager.Instance != null)
                FailPartFitting(SessionManager.Instance, "Guest left or reconnected.");
            _partFitLedger.ForgetPlayer(player);
        }

        private void ClearPartFitting()
        {
            if (_partFitting != null)
            {
                try { CancelPartFitPreview(_partFitting); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: fitting cleanup: " + e.Message); }
                RemovePartFitGuard(_partFitting);
            }
            _partFitting = null; _partFitLedger.Clear(); _partFitClient = null; _partFitInputFrame = -1; _partFitPromptFailed = false;
        }

        private static void RemovePartFitGuard(PartFitting fitting)
        {
            if (fitting.EntryState != null && fitting.EntryGuard != null)
                RemoveReplacementHook(fitting.EntryState, fitting.EntryGuard);
            if (fitting.CheckState != null && fitting.CheckGuard != null)
                RemoveReplacementHook(fitting.CheckState, fitting.CheckGuard);
            if (fitting.SelectionState != null && fitting.SelectionGuard != null)
                RemoveReplacementHook(fitting.SelectionState, fitting.SelectionGuard);
            fitting.EntryState = null; fitting.EntryGuard = null;
            fitting.CheckState = null; fitting.CheckGuard = null;
        }
    }
}
