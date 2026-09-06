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
        private sealed class BagOpening
        {
            public BagBinding Bag = null!;
            public PendingSpawn Capture = null!;
            public BagOpenRequest Request = null!;
            public ushort BeforeRemaining;
            public int ExpectedItems;
            public float Deadline;
            public bool Entered, Publishing, Applied, Warned;
        }
        private sealed class BagHook
        {
            public PlayMakerFSM Use = null!;
            public FsmState State = null!;
            public FsmStateAction Action = null!;
        }
        private readonly Dictionary<uint, BagBinding> _bags = new Dictionary<uint, BagBinding>();
        private readonly List<BagHook> _bagHooks = new List<BagHook>();
        private readonly HashSet<PlayMakerFSM> _guardedBags = new HashSet<PlayMakerFSM>();
        private readonly BagOpenLedger _bagOpenLedger = new BagOpenLedger();
        private BagOpening? _bagOpening;
        internal int BagCount => _bags.Count;
        private uint _outBagSequence;
        private float _nextBagPoll, _bagRetryAt;

        private void RegisterBag(BagBinding bag)
        {
            if (_bags.TryGetValue(bag.Id, out var known))
            {
                if (known.Body != bag.Body) throw new InvalidOperationException("Duplicate persistent shopping bag ID.");
                return;
            }
            if (_items.ContainsKey(bag.Id) || _trackedBodies.ContainsKey(bag.Body))
                throw new InvalidOperationException("Shopping bag already has another item identity.");
            GuardUnboundBag(bag.Use);
            _bags.Add(bag.Id, bag);
            _items.Add(bag.Id, new SyncedItem { Id = bag.Id, Body = bag.Body,
                Path = ScenePath.Of(bag.Body.transform), LastPosition = bag.Body.position });
            _trackedBodies[bag.Body] = true;
            _bridge.HookedFsms[bag.Use] = true;
            SyncEventLog.Record("bag-bind", bag.NativeId + " " + bag.Id.ToString("X8"));
        }

        // Factory outputs are guarded before discovery can assign a transient ID.
        // Even an immediately picked-up bag cannot spill unregistered.
        private void GuardUnboundBag(PlayMakerFSM use)
        {
            var c = SyncCatalog.ShoppingBags;
            if (c == null || _guardedBags.Contains(use)) return;
            if (!FsmHook.EnsureRemoteEntry(use, c["itemIdleState"]))
                throw new InvalidOperationException("Bag idle state missing.");
            foreach (string key in new[] { "oneState", "allState", "confirmState", "garbageState" })
            {
                string state = c[key];
                if (!FsmHook.EnsureRemoteEntry(use, state)
                    || !FsmHook.OnStateEnter(use, state, () => OnBagStateEntered(use, state), out var hook) || hook == null)
                    throw new InvalidOperationException("Bag guard could not be installed: " + state);
                _bagHooks.Add(new BagHook { Use = use, State = FsmHook.FindState(use, state)!, Action = hook });
            }
            _guardedBags.Add(use);
        }

        private BagBinding? FindBoundBag(PlayMakerFSM use)
        {
            foreach (var bag in _bags.Values) if (bag.Use == use) return bag;
            return null;
        }

        private void OnBagStateEntered(PlayMakerFSM use, string state)
        {
            var c = SyncCatalog.ShoppingBags; var session = SessionManager.Instance;
            if (c == null || session == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected)) return;
            try
            {
                var bag = FindBoundBag(use);
                if (state == c["garbageState"])
                {
                    if (bag != null && session.IsHost)
                    {
                        // Keep the native root/Consumed flag for SAVEGAME cleanup.
                        RecordItemRetirement(bag.Id);
                        if (_items.TryGetValue(bag.Id, out var item)) item.DespawnSent = true;
                        session.SendWorldMessage(new ItemDespawn { ItemId = bag.Id }, Channel.ReliableOrdered);
                    }
                    else ReturnBagToIdle(use);
                    return;
                }
                if (state == c["confirmState"])
                {
                    if (bag == null || (!bag.Replica && _bagOpening != null)
                        || !BagActorOwns(bag, session.LocalPlayerId)) ReturnBagToIdle(use);
                    return;
                }
                if (bag == null) { ReturnBagToIdle(use); return; }
                bool all = state == c["allState"];
                if (!session.IsHost) { QueueBagOpen(bag, all); ReturnBagToIdle(use); return; }
                if (_bagOpening != null && _bagOpening.Bag == bag && !_bagOpening.Entered)
                { _bagOpening.Entered = true; return; }
                var current = BuildBagState(bag.Id);
                if (current != null)
                {
                    var request = new BagOpenRequest { PlayerId = session.LocalPlayerId, Sequence = ++_outBagSequence,
                        ItemId = bag.Id, ExpectedRevision = current.Revision, OpenAll = all };
                    if (BeginBagOpening(request, session.LocalPlayerId, true)) return;
                }
                ReturnBagToIdle(use);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("WorldSync: shopping bag interaction paused: " + e.Message);
                ReturnBagToIdle(use);
            }
        }

        private static void ReturnBagToIdle(PlayMakerFSM use)
        {
            var c = SyncCatalog.ShoppingBags;
            if (c != null && use != null) FsmHook.FireRemoteEntry(use, c["itemIdleState"]);
        }

        internal BagState? BuildBagState(uint id)
        {
            var c = SyncCatalog.ShoppingBags; var session = SessionManager.Instance;
            if (c == null || session == null || !session.IsHost || _spawnLifecycle.IsRetired(id)
                || !_bags.TryGetValue(id, out var bag) || bag.Factory.Failed || bag.Replica || bag.Body == null || bag.Use == null) return null;
            try
            {
                ushort remaining = ReadBagRemaining(bag);
                float condition = Mathf.Clamp(bag.Use.FsmVariables.FindFsmFloat(c["conditionVariable"]).Value, 0, 100);
                return new BagState { ItemId = id, FactoryId = bag.Factory.Id, NativeId = bag.NativeId,
                    Remaining = remaining, Condition = condition, Revision = bag.Publication.Observe(remaining, condition),
                    Position = bag.Body.position.ToNet(), Rotation = bag.Body.rotation.ToNet() };
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("WorldSync: cannot publish bag " + bag.NativeId + ": " + e.Message);
                return null;
            }
        }

        internal IEnumerable<BagState> BuildBagStates()
        {
            foreach (uint id in _bags.Keys)
            {
                var state = BuildBagState(id);
                if (state != null) yield return state;
            }
        }

        private bool BagActorOwns(BagBinding bag, byte actor)
        {
            if (!_items.TryGetValue(bag.Id, out var item) || bag.Body == null) return false;
            var session = SessionManager.Instance;
            if (session == null) return false;
            if (actor != session.LocalPlayerId && IsPlayerHeldItem(item)) return false;
            return item.RemoteOwner == WorldSyncIds.NoOwner || item.RemoteOwner == actor;
        }

        private bool BagContentsIdle(BagBinding bag)
        {
            var c = SyncCatalog.ShoppingBags;
            var logic = bag.Factory.Contents;
            return c != null && logic != null && logic.enabled && logic.Fsm.Started
                && (logic.ActiveStateName == c["contentsIdleState"] || logic.ActiveStateName == c["contentsStartState"]);
        }

        private bool BeginBagOpening(BagOpenRequest request, byte actor, bool alreadyEntered)
        {
            var session = SessionManager.Instance; var c = SyncCatalog.ShoppingBags;
            if (session == null || !session.IsHost || c == null) return false;
            var replay = _bagOpenLedger.Inspect(request, actor, out bool canBegin);
            if (!canBegin) { if (replay != null) SendBagReceipt(session, replay); return false; }
            _bags.TryGetValue(request.ItemId, out var bag);
            var state = BuildBagState(request.ItemId);
            var body = bag?.Body; var use = bag?.Use;
            bool available = bag != null && !bag.Factory.Failed && !bag.Replica && body != null && use != null && use.enabled;
            bool nearby = body != null && (actor == session.LocalPlayerId || GuestNearPackage(session, actor, body.position));
            bool ready = bag != null && use != null && available && _bagOpening == null && BagContentsIdle(bag)
                && (alreadyEntered || (Array.IndexOf(c.ReadyStates, use.ActiveStateName) >= 0
                    && use.ActiveStateName != c["oneState"] && use.ActiveStateName != c["allState"]));
            var status = BagOpenLedger.Check(request, state, available, nearby, available && bag != null && BagActorOwns(bag, actor), ready);
            var receipt = _bagOpenLedger.Begin(request, actor, status);
            if (receipt == null) return false;
            if (receipt.Status != BagOpenStatus.Pending) { SendBagReceipt(session, receipt); return false; }
            if (bag == null || state == null || use == null)
            {
                var failed = _bagOpenLedger.Complete(request, false);
                if (failed != null) SendBagReceipt(session, failed);
                return false;
            }
            try
            {
                PrepareBagSpillCapture();
                var capture = BeginBagSpill(bag, request.OpenAll ? c["allState"] : c["oneState"]);
                _bagOpening = new BagOpening { Bag = bag, Capture = capture, Request = BagOpenLedger.Copy(request),
                    BeforeRemaining = state.Remaining, ExpectedItems = request.OpenAll ? state.Remaining : 1,
                    Deadline = Time.unscaledTime + 15f, Entered = alreadyEntered };
                // Confirm's player input cannot be replayed. Its one setup assignment
                // is explicit, then only the host runs the verified native spill.
                bag.Factory.Contents.FsmVariables.FindFsmGameObject(c["currentBagVariable"]).Value = use.gameObject;
                use.FsmVariables.FindFsmFloat(c["conditionVariable"]).Value = state.Condition;
                if (!alreadyEntered) FsmHook.FireRemoteEntry(use, request.OpenAll ? c["allState"] : c["oneState"]);
                SendBagReceipt(session, receipt);
                SyncEventLog.Record("bag-open", actor + ":" + request.Sequence + " " + request.ItemId.ToString("X8"));
                return true;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("WorldSync: bag opening failed: " + e.Message);
                if (_bagOpening != null)
                {
                    _bagOpening.Warned = true;
                    return true;
                }
                var failed = _bagOpenLedger.Complete(request, false);
                if (failed != null) SendBagReceipt(session, failed);
                return false;
            }
        }

        internal void OnHostBagOpen(BagOpenRequest request, byte actor) => BeginBagOpening(request, actor, false);

        internal void ProcessBags(SessionManager session)
        {
            if (Time.unscaledTime < _bagRetryAt) return;
            try { ProcessBagsCore(session); }
            catch (Exception e)
            {
                _bagRetryAt = Time.unscaledTime + 5f;
                WinterMPPlugin.Log.LogWarning("WorldSync: shopping bag synchronization will retry: " + e.Message);
            }
        }

        private void ProcessBagsCore(SessionManager session)
        {
            ProcessBagBindings(session);
            UpdateBagPickup(session);
            if (session.IsHost && _bagOpening != null)
            {
                var opening = _bagOpening;
                if (!opening.Publishing)
                {
                    try
                    {
                        if (BagSpillNativeIdle(opening) && BagSpillReady(opening))
                        {
                            opening.Applied = ReadBagRemaining(opening.Bag) == opening.BeforeRemaining - opening.ExpectedItems
                                && opening.Capture.Captured.Count == opening.ExpectedItems;
                            opening.Publishing = true;
                        }
                    }
                    catch (Exception e)
                    {
                        opening.Bag.Factory.Failed = true;
                        if (!opening.Warned) WinterMPPlugin.Log.LogWarning("WorldSync: bag spill paused: " + e.Message);
                        opening.Warned = true;
                        // Keep capturing until the native loop has actually stopped.
                        // A timeout cannot detach late outputs from their bag.
                        opening.Publishing = BagSpillNativeIdle(opening);
                    }
                    if (!opening.Publishing && Time.unscaledTime >= opening.Deadline && !opening.Warned)
                    {
                        opening.Warned = true;
                        WinterMPPlugin.Log.LogWarning("WorldSync: waiting for the native bag spill to finish; further openings remain paused.");
                    }
                }
                if (opening.Publishing)
                {
                    try
                    {
                        PublishBagSpill(session, opening.Capture);
                        var receipt = _bagOpenLedger.Complete(opening.Request, opening.Applied);
                        _bagOpening = null;
                        if (receipt != null) SendBagReceipt(session, receipt);
                        if (!opening.Applied) WinterMPPlugin.Log.LogWarning("WorldSync: bag spill did not match its inventory; remaining contents retained on host.");
                    }
                    catch (Exception e)
                    {
                        // Each completed chunk is removed; a failed chunk keeps its
                        // epoch. Retrying cannot mint another ID for the same body.
                        if (!opening.Warned) WinterMPPlugin.Log.LogWarning("WorldSync: bag publication waiting: " + e.Message);
                        opening.Warned = true;
                    }
                }
            }
            if (Time.unscaledTime < _nextBagPoll) return;
            _nextBagPoll = Time.unscaledTime + .2f;
            if (session.IsHost)
            {
                foreach (var state in BuildBagStates())
                {
                    var publication = _bags[state.ItemId].Publication;
                    if (!publication.NeedsBroadcast) continue;
                    session.SendWorldMessage(state, Channel.ReliableOrdered);
                    publication.MarkBroadcast(state.Revision);
                }
            }
            else ProcessBagReplicas(session);
        }

        private void PublishBagSpill(SessionManager session, PendingSpawn capture)
        {
            for (int i = capture.Captured.Count - 1; i >= 0; i--)
                if (capture.Captured[i] == null || FindPackageUse(capture.Captured[i]) != null) capture.Captured.RemoveAt(i);
            // A large shopping trip can exceed a single packet's 32-entry limit.
            while (capture.Captured.Count > 0)
            {
                int count = Math.Min(capture.Captured.Count, ItemSpawn.MaxItems);
                var chunk = new PendingSpawn { ContainerId = capture.ContainerId, IsHost = true,
                    StateName = capture.StateName, Epoch = capture.Epoch };
                for (int i = 0; i < count; i++) chunk.Captured.Add(capture.Captured[i]);
                FinalizeHostSpawn(session, chunk);
                capture.Captured.RemoveRange(0, count);
                if (capture.Captured.Count > 0) capture.Epoch = MintSpawnEpoch(capture.ContainerId);
            }
            foreach (var state in BuildPackageStates()) session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private bool BagSpillNativeIdle(BagOpening opening)
        {
            if (!opening.Entered || !BagContentsIdle(opening.Bag)) return false;
            foreach (var factory in _bagSpillFactories.Values)
            {
                foreach (var request in factory.Requests) if (request.Opening == opening) return false;
                bool involved = false;
                foreach (var captured in _bagSpillBodies.Values) if (captured == factory) { involved = true; break; }
                if (involved && (factory.Fsm == null || !factory.Fsm.enabled
                    || factory.Fsm.ActiveStateName != SyncCatalog.ShoppingBags!["spillIdleState"])) return false;
            }
            return true;
        }

        private void SendBagReceipt(SessionManager session, BagOpenReceipt receipt)
        {
            var state = BuildBagState(receipt.ItemId);
            if (state != null) session.SendWorldMessage(state, Channel.ReliableOrdered);
            if (receipt.PlayerId != session.LocalPlayerId) session.SendWorldMessage(receipt, Channel.ReliableOrdered);
        }

        internal void ForgetBagPlayer(byte actor) => _bagOpenLedger.ForgetPlayer(actor);

        private void ClearBags()
        {
            if (_bagOpening != null)
                foreach (var body in _bagOpening.Capture.Captured)
                {
                    bool registered = false;
                    foreach (var item in _items.Values) if (item.Body == body) { registered = true; break; }
                    if (!registered && body != null) _trackedBodies.Remove(body);
                }
            _bagOpening = null; _bagOpenLedger.Clear(); _pendingBagRequest = null; _bagRetryAt = 0;
            ClearBagSpillCapture();
            foreach (var bag in _bags.Values)
            {
                if (bag.Use != null) _bridge.HookedFsms.Remove(bag.Use);
                RemoveTrackedItem(bag.Id, bag.Body);
                if (bag.Replica && bag.Use != null)
                {
                    ReleaseHeldBag(bag.Body); bag.Use.enabled = false; bag.Use.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(bag.Use.gameObject);
                }
            }
            foreach (var hook in _bagHooks)
            {
                if (hook.Use == null) continue;
                var actions = new List<FsmStateAction>(hook.State.Actions); actions.Remove(hook.Action); hook.State.Actions = actions.ToArray();
            }
            _bagHooks.Clear(); _guardedBags.Clear(); _bags.Clear(); _bagStates.Clear(); _pendingBagStates.Clear();
            ResetBagBindings();
            ClearBagPickup();
        }
    }
}
