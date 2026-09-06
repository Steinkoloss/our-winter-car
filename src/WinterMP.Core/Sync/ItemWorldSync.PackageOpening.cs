using System;
using System.Collections.Generic;
using System.Globalization;
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
        private sealed class PackageOpening
        {
            public uint ItemId;
            public PackageBinding Box = null!;
            public ReplacementFactory Contents = null!;
            public PackageOpenRequest? Request;
            public string ExpectedNativeId = string.Empty;
            public ushort BeforeQuantity;
            public float Deadline;
            public bool Entered, Dispatched, InvalidOutput;
            public GameObject? OutputObject;
            public uint OutputId;
            public int OutputCount, QuantityAfter;
        }
        private readonly PackageOpenLedger _packageOpenLedger = new PackageOpenLedger();
        private PackageOpenClient? _packageOpenClient;
        private PackageOpening? _packageOpening;

        private static FsmState ValidatePackageOpening(PlayMakerFSM use, PartsPackagesData c)
        {
            var state = PackageStateActions(use, c["openState"], "MasterAudioPlaySound", "SetBoolValue", "IntAdd",
                "SetGameObject", "SetFsmFloat", "SendEventByName");
            var add = state.Actions[2]; var spawn = state.Actions[3]; var wear = state.Actions[4]; var send = state.Actions[5];
            var amount = PackageField<FsmInt>(add, "add");
            var target = PackageField<FsmEventTarget>(send, "eventTarget");
            if (PackageField<FsmInt>(add, "intVariable")?.Name != c["quantityVariable"]
                || amount == null || amount.UseVariable || amount.Value != -1
                || PackageField<FsmGameObject>(spawn, "variable")?.Name != c["spawnPointVariable"]
                || PackageField<FsmGameObject>(spawn, "gameObject")?.Name != c["ownerVariable"]
                || PackageField<FsmOwnerDefault>(wear, "gameObject")?.GameObject.Name != c["contentsVariable"]
                || PackageField<FsmString>(wear, "fsmName")?.Value != c["contentsFsm"]
                || PackageField<FsmString>(wear, "variableName")?.Value != c["minimumWearVariable"]
                || target == null || target.target != FsmEventTarget.EventTarget.GameObjectFSM
                || target.gameObject.GameObject.Name != c["contentsVariable"] || target.fsmName.Value != c["contentsFsm"]
                || PackageField<FsmString>(send, "sendEvent")?.Value != c["contentsEvent"]
                || PackageField<FsmFloat>(send, "delay")?.Value != 0
                || state.Transitions.Length != 1 || state.Transitions[0].EventName != "FINISHED"
                || state.Transitions[0].ToState != c["checkQuantityState"])
                throw new InvalidOperationException("Box opening quantity/spawn bindings changed.");
            foreach (var action in state.Actions)
            {
                object? everyFrame = action.GetType().GetField("everyFrame")?.GetValue(action);
                if ((everyFrame is bool frame && frame) || (everyFrame is FsmBool fsmFrame && fsmFrame.Value))
                    throw new InvalidOperationException("Box opening is no longer a one-shot action.");
            }
            return state;
        }

        private void RegisterPackageOpening(uint id, PackageBinding box)
        {
            try
            {
                var c = SyncCatalog.PartsPackages!;
                var state = ValidatePackageOpening(box.Use, c);
                if (!FsmHook.EnsureRemoteEntry(box.Use, c["openState"])
                    || !FsmHook.EnsureRemoteEntry(box.Use, c["itemIdleState"])
                    || !FsmHook.EnsureRemoteEntry(box.Use, c["emptyState"])) throw new InvalidOperationException("Cannot guard box opening.");
                box.OpenState = state;
                box.OpenGuard = new FsmHookAction(() => OnPackageOpeningEntered(id, box));
                box.OpenTail = new FsmHookAction(() =>
                {
                    if (_packageOpening != null && _packageOpening.Box == box && _packageOpening.Entered)
                    {
                        _packageOpening.QuantityAfter = box.Use.FsmVariables.FindFsmInt(c["quantityVariable"]).Value;
                        _packageOpening.Dispatched = true;
                    }
                });
                var actions = new List<FsmStateAction>(state.Actions); actions.Insert(0, box.OpenGuard); actions.Add(box.OpenTail);
                state.Actions = actions.ToArray();
            }
            catch (Exception e) { FailPackageOpening(box, e.Message); }
        }

        private bool TryGetOpeningFactory(PackageBinding box, out ReplacementFactory factory)
        {
            factory = null!;
            var c = SyncCatalog.PartsPackages; var rc = SyncCatalog.ReplacementParts;
            if (c == null || rc == null || box.OpenFailed || box.Factory.Failed || box.Body == null || box.Use == null
                || !box.Use.Fsm.Started || !box.Use.enabled || !box.Use.gameObject.activeInHierarchy
                || box.Use.FsmVariables.FindFsmGameObject(c["ownerVariable"])?.Value != box.Body.gameObject
                || box.Use.FsmVariables.FindFsmGameObject(c["contentsVariable"])?.Value != box.Factory.Contents) return false;
            uint id = FactoryItemIdentity.FactoryId(box.Factory.Rule.ContentsPath, c["contentsFsm"]);
            if (!_replacementFactories.TryGetValue(id, out var found) || found.Failed || found.Fsm == null
                || found.Fsm.gameObject != box.Factory.Contents || !found.Fsm.Fsm.Started
                || !found.Fsm.enabled || !found.Fsm.gameObject.activeInHierarchy) return false;
            var counter = found.Fsm.FsmVariables.FindFsmInt(c["contentsCounterVariable"]);
            var count = found.Fsm.FsmVariables.FindFsmInt(c["contentsCountVariable"]);
            if (counter == null || counter.Value < 0 || counter.Value == int.MaxValue || (count != null && count.Value != 1)) return false;
            if (count == null)
            {
                var create = FsmHook.FindState(found.Fsm, rc["createState"]);
                if (create == null || create.Transitions.Length != 1 || create.Transitions[0].ToState != rc["factoryIdleState"]) return false;
            }
            factory = found; return true;
        }

        private bool OpeningFactoriesIdle()
        {
            var c = SyncCatalog.ReplacementParts;
            if (c == null) return false;
            // Known contents factories share PartSpawnPoint; a multi-frame native
            // operation must finish before another box replaces that global.
            foreach (var factory in _replacementFactories.Values)
                if (factory.Fsm != null && factory.Fsm.Fsm.Started && factory.Fsm.enabled
                    && factory.Fsm.ActiveStateName != c["factoryIdleState"]) return false;
            return true;
        }

        private PackageOpening NewPackageOpening(uint id, PackageBinding box, ReplacementFactory factory,
            ushort quantity, PackageOpenRequest? request)
        {
            int counter = factory.Fsm.FsmVariables.FindFsmInt(SyncCatalog.PartsPackages!["contentsCounterVariable"]).Value;
            return new PackageOpening { ItemId = id, Box = box, Contents = factory, BeforeQuantity = quantity,
                Request = request == null ? null : PackageOpenLedger.Copy(request), Deadline = Time.unscaledTime + 10f,
                ExpectedNativeId = factory.Rule.Prefix + (counter + 1).ToString(CultureInfo.InvariantCulture) };
        }

        private void OnPackageOpeningEntered(uint id, PackageBinding box)
        {
            var c = SyncCatalog.PartsPackages!;
            try
            {
                var session = SessionManager.Instance;
                if (session == null || !session.IsHost) return;
                if (_packageOpening != null && _packageOpening.Box == box && !_packageOpening.Entered)
                { _packageOpening.Entered = true; return; }
                var state = BuildPackageState(id);
                if (_packageOpening == null && state != null && state.Quantity > 0
                    && TryGetOpeningFactory(box, out var factory) && OpeningFactoriesIdle())
                {
                    _packageOpening = NewPackageOpening(id, box, factory, state.Quantity, null);
                    _packageOpening.Entered = true; return;
                }
            }
            catch (Exception e) { FailPackageOpening(box, e.Message); }
            // PlayMaker 1.7.7.6 stops ActivateActions as soon as this self-event
            // queues a transition, before the native decrement/creation actions.
            FsmHook.FireRemoteEntry(box.Use, box.Use.FsmVariables.FindFsmInt(c["quantityVariable"]).Value == 0
                ? c["emptyState"] : c["itemIdleState"]);
        }

        internal void OnHostPackageOpen(PackageOpenRequest request, byte actor)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            var replay = _packageOpenLedger.Inspect(request, actor, out bool canBegin);
            if (!canBegin) { if (replay != null) SendPackageOpenReceipt(session, replay); return; }
            PackageBinding? box = null;
            try
            {
                var c = SyncCatalog.PartsPackages;
                _packages.TryGetValue(request.ItemId, out box);
                var state = BuildPackageState(request.ItemId);
                bool available = c != null && box != null && !box.Replica && box.OpenGuard != null
                    && TryGetOpeningFactory(box, out _);
                bool nearby = box != null && box.Body != null && GuestNearPackage(session, actor, box.Body.position);
                bool ready = available && _packageOpening == null && OpeningFactoriesIdle()
                    && Array.IndexOf(c!.OpenReadyStates, box!.Use.ActiveStateName) >= 0
                    && _items.TryGetValue(request.ItemId, out var item)
                    && (item.RemoteOwner == WorldSyncIds.NoOwner || item.RemoteOwner == actor);
                var status = PackageOpenLedger.Check(request, state, available, nearby, ready);
                var receipt = _packageOpenLedger.Begin(request, actor, status);
                if (receipt == null) return;
                if (status != PackageOpenStatus.Busy)
                    SyncEventLog.Record("package-open-request", actor + ":" + request.Sequence + " "
                        + request.ItemId.ToString("X8") + " " + status);
                if (status == PackageOpenStatus.Pending && box != null && state != null && TryGetOpeningFactory(box, out var factory))
                {
                    _packageOpening = NewPackageOpening(request.ItemId, box, factory, state.Quantity, request);
                    FsmHook.FireRemoteEntry(box.Use, c!["openState"]);
                }
                SendPackageOpenReceipt(session, receipt);
            }
            catch (Exception e)
            {
                if (box != null) FailPackageOpening(box, e.Message);
                var receipt = _packageOpenLedger.Complete(request, false);
                if (receipt != null) SendPackageOpenReceipt(session, receipt);
            }
        }

        private static bool GuestNearPackage(SessionManager session, byte actor, Vector3 position)
        {
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
                if (player.PlayerId == actor && !player.IsDead && player.LastTransformTime > 0
                    && now >= player.LastTransformTime && now - player.LastTransformTime <= 2f)
                    return (player.Position - position).sqrMagnitude <= 9f;
            return false;
        }

        private void ObservePackageOpeningOutput(ReplacementFactory factory, Rigidbody body, string nativeId)
        {
            var opening = _packageOpening;
            if (opening == null || opening.Contents != factory || !opening.Entered || opening.Dispatched) return;
            opening.OutputCount++;
            if (nativeId != opening.ExpectedNativeId || !PartIdentity.TryItemId(nativeId, out uint id)) opening.InvalidOutput = true;
            else { opening.OutputObject = body.gameObject; opening.OutputId = id; }
        }

        private void ProcessPackageOpening(SessionManager session)
        {
            if (!session.IsHost)
            {
                var request = _packageOpenClient?.Poll(Time.unscaledTime);
                if (request != null) session.SendWorldMessage(request, Channel.ReliableOrdered);
                return;
            }
            var opening = _packageOpening;
            if (opening == null) return;
            try
            {
                if ((!opening.Dispatched && opening.Box.Use == null) || opening.Contents.Failed || opening.InvalidOutput
                    || opening.OutputCount > 1) throw new InvalidOperationException("Box opening lost its native output.");
                if (opening.Dispatched && opening.QuantityAfter != opening.BeforeQuantity - 1)
                    throw new InvalidOperationException("Box opening changed quantity unexpectedly.");
                if (opening.Dispatched && opening.OutputCount == 1 && opening.OutputObject != null)
                {
                    var data = NativePartIdentity.FindData(opening.OutputObject.transform);
                    if (data != null && _bridge.PartIdentities.TryRootId(data, out uint id))
                    {
                        if (id != opening.OutputId) throw new InvalidOperationException("Box output changed identity.");
                        // Native garbage can remove the physical part (or its empty
                        // box) before this deferred confirmation. Do not replay a
                        // successful creation just because its result was consumed.
                        if (data.FsmVariables.FindFsmBool(SyncCatalog.ReplacementParts!["consumedVariable"]).Value)
                        {
                            RecordItemRetirement(id);
                            session.SendWorldMessage(new ItemDespawn { ItemId = id }, Channel.ReliableOrdered);
                            FinishPackageOpening(session, opening, true); return;
                        }
                        var body = opening.OutputObject.GetComponent<Rigidbody>();
                        if (body == null) throw new InvalidOperationException("Box output lost its body without native disposal.");
                        TryScanNativePart(body);
                        if (BuildReplacementPartState(id) != null)
                        { FinishPackageOpening(session, opening, true); return; }
                    }
                }
                if (Time.unscaledTime >= opening.Deadline) throw new InvalidOperationException("Box output initialization timed out.");
            }
            catch (Exception e) { FailPackageOpening(opening.Box, e.Message); FinishPackageOpening(session, opening, false); }
        }

        private void FinishPackageOpening(SessionManager session, PackageOpening opening, bool accepted)
        {
            _packageOpening = null;
            var state = BuildPackageState(opening.ItemId);
            if (state != null) session.SendWorldMessage(state, Channel.ReliableOrdered);
            if (opening.Request != null)
            {
                var receipt = _packageOpenLedger.Complete(opening.Request, accepted, opening.OutputId);
                if (receipt != null) SendPackageOpenReceipt(session, receipt);
            }
            SyncEventLog.Record("package-open", opening.ItemId.ToString("X8") + (accepted ? " accepted " : " failed ") + opening.ExpectedNativeId);
        }

        private void SendPackageOpenReceipt(SessionManager session, PackageOpenReceipt receipt)
        {
            var box = BuildPackageState(receipt.ItemId);
            if (box != null) session.SendWorldMessage(box, Channel.ReliableOrdered);
            else if (_spawnLifecycle.IsRetired(receipt.ItemId))
                session.SendWorldMessage(new ItemDespawn { ItemId = receipt.ItemId }, Channel.ReliableOrdered);
            if (receipt.Status == PackageOpenStatus.Accepted)
            {
                var part = BuildReplacementPartState(receipt.ProducedItemId);
                if (part != null) session.SendWorldMessage(part, Channel.ReliableOrdered);
                else if (_spawnLifecycle.IsRetired(receipt.ProducedItemId))
                    session.SendWorldMessage(new ItemDespawn { ItemId = receipt.ProducedItemId }, Channel.ReliableOrdered);
            }
            session.SendWorldMessage(receipt, Channel.ReliableOrdered);
        }

        private void QueueGuestPackageOpening(uint id)
        {
            var session = SessionManager.Instance; var state = _packageReplica?.Get(id);
            if (session == null || session.IsHost || state == null || state.Quantity == 0) return;
            if (!_packages.TryGetValue(id, out var box) || !box.Replica || box.OpenFailed) return;
            uint contentsId = FactoryItemIdentity.FactoryId(box.Factory.Rule.ContentsPath, SyncCatalog.PartsPackages!["contentsFsm"]);
            if (!_replacementFactories.TryGetValue(contentsId, out var contents) || contents.Failed || !contents.Suppressor.Active)
            {
                if (Time.unscaledTime >= _nextPackageNotice)
                {
                    _nextPackageNotice = Time.unscaledTime + 3f;
                    session.AddSystemChat("The box contents are not ready to synchronize yet.");
                }
                return;
            }
            if (_packageOpenClient == null)
            {
                ulong token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
                _packageOpenClient = new PackageOpenClient(token == 0 ? 1 : token);
            }
            if (_packageOpenClient.TryBegin(session.LocalPlayerId, id, state.Revision)) ProcessPackageOpening(session);
        }

        private void PrepareGuestPackageOpening(PlayMakerFSM use, uint id, PartsPackagesData c)
        {
            var open = ValidatePackageOpening(use, c);
            if (FsmHook.HasState(use, c["openFeedbackState"])) throw new InvalidOperationException("Box feedback state already exists.");
            var feedback = new FsmState(use.Fsm) { Name = c["openFeedbackState"], Actions = new[] { open.Actions[0], open.Actions[1],
                new FsmHookAction(() => ReturnPackageToIdle(use, c)) } };
            var states = new List<FsmState>(use.Fsm.States); states.Add(feedback); use.Fsm.States = states.ToArray();
            if (!FsmHook.EnsureRemoteEntry(use, feedback.Name)) throw new InvalidOperationException("Cannot play box feedback.");
            open.Actions = new FsmStateAction[] { new FsmHookAction(() => { QueueGuestPackageOpening(id); ReturnPackageToIdle(use, c); }) };
        }

        private static void ReturnPackageToIdle(PlayMakerFSM use, PartsPackagesData c) => FsmHook.FireRemoteEntry(use,
            use.FsmVariables.FindFsmInt(c["quantityVariable"]).Value == 0 ? c["emptyState"] : c["itemIdleState"]);

        internal void OnPackageOpenReceipt(PackageOpenReceipt receipt)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.LocalPlayerId != receipt.PlayerId || _packageOpenClient == null
                || !_packageOpenClient.Receive(receipt)) return;
            if (receipt.Status == PackageOpenStatus.Accepted)
            {
                if (_packages.TryGetValue(receipt.ItemId, out var box) && box.Replica && box.Use != null)
                {
                    try
                    {
                        var state = _packageReplica?.Get(receipt.ItemId);
                        if (state != null) ApplyPackageQuantity(box, state.Quantity);
                        FsmHook.FireRemoteEntry(box.Use, SyncCatalog.PartsPackages!["openFeedbackState"]);
                    }
                    catch (Exception e) { FailPackageOpening(box, e.Message); }
                }
            }
            else if (Time.unscaledTime >= _nextPackageNotice)
            {
                _nextPackageNotice = Time.unscaledTime + 3f;
                session.AddSystemChat(receipt.Status == PackageOpenStatus.Stale ? "The box changed. Try opening it again."
                    : receipt.Status == PackageOpenStatus.Empty ? "The box is empty."
                    : receipt.Status == PackageOpenStatus.TooFar ? "Move closer to the box and try again."
                    : "This box could not be opened. Check the multiplayer log.");
            }
        }

        private static void FailPackageOpening(PackageBinding box, string reason)
        {
            if (box.OpenFailed) return;
            box.OpenFailed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: box opening disabled for " + box.NativeId + ": " + reason);
            SyncEventLog.Record("package-open-disabled", box.NativeId + " " + reason);
        }
        internal void ForgetPackageOpeningPlayer(byte playerId) => _packageOpenLedger.ForgetPlayer(playerId);
        private void ClearPackageOpenings()
        {
            _packageOpening = null; _packageOpenLedger.Clear(); _packageOpenClient = null;
            foreach (var box in _packages.Values) RemovePackageOpeningHooks(box);
        }
        private static void RemovePackageOpeningHooks(PackageBinding box)
        {
            if (box.OpenState == null) return;
            if (box.OpenGuard != null) RemoveReplacementHook(box.OpenState, box.OpenGuard);
            if (box.OpenTail != null) RemoveReplacementHook(box.OpenState, box.OpenTail);
            box.OpenGuard = box.OpenTail = null; box.OpenState = null;
        }
    }
}
