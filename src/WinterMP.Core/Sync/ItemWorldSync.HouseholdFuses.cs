using System;
using System.Collections;
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
        private sealed class FuseHolderBinding
        {
            internal byte Index;
            internal int Home;
            internal GameObject Original = null!, Object = null!;
            internal PlayMakerFSM Use = null!, Screw = null!, Removal = null!, Insert = null!;
            internal FsmFloat Tightness = null!;
            internal FsmInt Fuse = null!;
            internal GameObject Mesh = null!, Tip = null!;
            internal uint ControlRevision = 1;
            internal HouseholdFuseHolder? Last;
            internal SyncedItem? Item;
            internal float BusyUntil;
        }
        private sealed class FuseTableBinding
        {
            internal int Offset;
            internal GameObject Database = null!;
            internal PlayMakerFSM Shock = null!;
            internal IList Fuses = null!;
            internal PlayMakerFSM[] Slots = null!;
            internal Transform[] Pivots = null!;
        }
        private FuseHolderBinding[]? _fuseHolders;
        private FuseTableBinding[]? _fuseTables;
        private HouseholdFuseState? _remoteFuses;
        private readonly List<Action> _fuseRestore = new List<Action>();
        private readonly List<GameObject> _fuseClones = new List<GameObject>();
        private readonly Dictionary<byte, uint> _fuseSequences = new Dictionary<byte, uint>();
        private readonly Dictionary<uint, HouseholdFuseIntent> _pendingFuseIntents = new Dictionary<uint, HouseholdFuseIntent>();
        private bool _fuseFailed, _fusePickupGuardInstalled;
        private float _fuseProbeAt, _fuseTickAt;
        private uint _fuseRevision, _fuseIntentSequence;
        private byte _fuseRemoteActor = 255;

        private void UpdateHouseholdFuses(SessionManager session)
        {
            if (_fuseFailed) return;
            try
            {
                RefreshHouseholdFuses();
                if (_fuseHolders == null) return;
                GuardFusePartPickup();
                foreach (var h in _fuseHolders) RegisterFuseBody(h);
                if (!session.IsHost || Time.unscaledTime < _fuseTickAt) return;
                _fuseTickAt = Time.unscaledTime + .25f;
                session.SendWorldMessage(CaptureHouseholdFuses(), Channel.ReliableOrdered);
            }
            catch (Exception e) { HouseholdFusesFailed(e); }
        }
        internal HouseholdFuseState? BuildHouseholdFuseSnapshot()
        {
            if (_fuseFailed || SessionManager.Instance?.IsHost != true) return null;
            try { RefreshHouseholdFuses(); return _fuseHolders == null ? null : CaptureHouseholdFuses(); }
            catch (Exception e) { HouseholdFusesFailed(e); return null; }
        }
        private HouseholdFuseState CaptureHouseholdFuses()
        {
            var s = new HouseholdFuseState { Revision = NextFuseRevision(ref _fuseRevision) };
            foreach (var table in _fuseTables!)
                for (int i = 0; i < table.Fuses.Count; i++) if ((bool)table.Fuses[i]) s.PowerMask |= (ushort)(1 << (table.Offset + i));
            foreach (var h in _fuseHolders!)
            {
                var table = _fuseTables[h.Home]; byte slot = 255;
                float tightness = h.Tightness.Value;
                if (tightness < 0 || tightness > 8 || tightness != Mathf.Round(tightness)) throw new InvalidOperationException("Invalid native fuse tightness.");
                // Native load keeps a loose holder's scene parent; Installed? uses tightness instead.
                if (tightness > 0)
                    for (int i = 0; i < table.Pivots.Length; i++) if (h.Object.transform.parent == table.Pivots[i]) slot = (byte)(table.Offset + i);
                var entry = new HouseholdFuseHolder { Slot = slot, Fuse = (byte)h.Fuse.Value, Tightness = (byte)tightness,
                    Flags = (byte)((h.Mesh.activeSelf ? 1 : 0) | (h.Tip.activeSelf ? 2 : 0) | (h.Insert.gameObject.activeSelf ? 4 : 0)),
                    Position = h.Object.transform.position.ToNet(), Rotation = h.Object.transform.rotation.ToNet() };
                if (h.Last != null && (h.Last.Slot != entry.Slot || h.Last.Fuse != entry.Fuse || h.Last.Tightness != entry.Tightness || h.Last.Flags != entry.Flags))
                    NextFuseRevision(ref h.ControlRevision);
                entry.ControlRevision = h.ControlRevision; h.Last = entry; s.Holders[h.Index] = entry;
            }
            if (!HouseholdFusePolicy.Valid(s)) throw new InvalidOperationException("Native household fuse state is inconsistent.");
            return s;
        }
        internal void ReceiveHouseholdFuses(HouseholdFuseState state)
        {
            if (_fuseFailed || SessionManager.Instance?.IsHost != false || !HouseholdFusePolicy.Valid(state)
                || _remoteFuses != null && !TaxiServicePolicy.Newer(_remoteFuses.Revision, state.Revision)) return;
            _remoteFuses = state;
            try { RefreshHouseholdFuses(); if (_fuseHolders != null) PresentHouseholdFuses(state); }
            catch (Exception e) { HouseholdFusesFailed(e); }
        }
        internal void ReceiveHouseholdFuseIntent(HouseholdFuseIntent intent, byte actor)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _fuseFailed || !HouseholdFusePolicy.Valid(intent) || actor != intent.PlayerId) return;
            var result = new HouseholdFuseResult { PlayerId = actor, Sequence = intent.Sequence, Holder = intent.Holder };
            try
            {
                RefreshHouseholdFuses(); if (_fuseHolders == null) return;
                if (_fuseSequences.TryGetValue(actor, out uint previous) && !TaxiServicePolicy.Newer(previous, intent.Sequence)) return;
                _fuseSequences[actor] = intent.Sequence;
                var h = _fuseHolders[intent.Holder]; var table = _fuseTables![h.Home];
                Vector3 target = intent.Action == HouseholdFuseAction.FitHolder ? table.Slots[intent.Slot - table.Offset].transform.position : h.Object.transform.position;
                bool near = GamblingSync.TryPlayerPosition(session, actor, out var position) && (position - target).sqrMagnitude <= 9;
                uint itemId = intent.Action == HouseholdFuseAction.InsertFuse ? intent.ItemId : HouseholdFusePolicy.ItemId(intent.Holder);
                bool owned = _items.TryGetValue(itemId, out var item) && item.Body != null && !IsHeldByLocalPlayer(item.Body)
                    && item.RemoteOwner == actor && Time.unscaledTime - item.LastRemoteAt <= 2 && (item.Body.position - target).sqrMagnitude <= 2.25f;
                if (intent.Action == HouseholdFuseAction.InsertFuse)
                    owned &= _supplies.TryGetValue(itemId, out var supply) && !supply.Factory.Failed && supply.Factory.Rule.ContentsFsm == "Fuse"
                        && supply.Use != null && !supply.Use.FsmVariables.FindFsmBool("Consumed").Value;
                bool busy = Time.unscaledTime < h.BusyUntil || !FuseActionReady(h, intent);
                var state = CaptureHouseholdFuses();
                if (HouseholdFusePolicy.CanAct(state, intent, actor, near, owned, busy))
                {
                    NextFuseRevision(ref h.ControlRevision); h.BusyUntil = Time.unscaledTime + .35f;
                    _fuseRemoteActor = actor;
                    try
                    {
                        switch (intent.Action)
                        {
                            case HouseholdFuseAction.InsertFuse:
                                var fuseBody = item?.Body ?? throw new InvalidOperationException("Accepted fuse disappeared.");
                                h.Insert.FsmVariables.FindFsmGameObject("Part").Value = fuseBody.gameObject;
                                FsmHook.FireRemoteEntry(h.Insert, SyncCatalog.HouseholdFuses!["assemblyState"]); break;
                            case HouseholdFuseAction.FitHolder:
                                RemoveFuseMotion(h);
                                var slot = table.Slots[intent.Slot - table.Offset]; slot.FsmVariables.FindFsmGameObject("Part").Value = h.Object;
                                FsmHook.FireRemoteEntry(slot, SyncCatalog.HouseholdFuses!["assemblyState"]); break;
                            case HouseholdFuseAction.RemoveHolder: FsmHook.FireRemoteEntry(h.Removal, SyncCatalog.HouseholdFuses!["removeState"]); break;
                            default:
                                h.Screw.FsmVariables.FindFsmFloat("Tightness").Value = h.Tightness.Value;
                                FsmHook.FireRemoteEntry(h.Screw, SyncCatalog.HouseholdFuses![intent.Action == HouseholdFuseAction.Tighten ? "tightenState" : "loosenState"]);
                                result.Shock = true; break;
                        }
                    }
                    finally { _fuseRemoteActor = 255; }
                    result.Accepted = true;
                    SyncEventLog.Record("household-fuse", actor + "/" + intent.Holder + "/" + intent.Action);
                }
                else SyncEventLog.Record("household-fuse-rejected", actor + "/" + intent.Holder + "/" + intent.Action);
                session.SendWorldMessage(CaptureHouseholdFuses(), Channel.ReliableOrdered);
                session.SendWorldMessage(result, Channel.ReliableOrdered);
            }
            catch (Exception e) { HouseholdFusesFailed(e); }
        }
        private bool FuseActionReady(FuseHolderBinding h, HouseholdFuseIntent i)
        {
            if (i.Action == HouseholdFuseAction.InsertFuse) return h.Insert.enabled && h.Insert.gameObject.activeInHierarchy;
            if (i.Action == HouseholdFuseAction.FitHolder) return _fuseTables![h.Home].Slots[i.Slot - _fuseTables[h.Home].Offset].gameObject.activeInHierarchy;
            var fsm = i.Action == HouseholdFuseAction.RemoveHolder ? h.Removal : h.Screw;
            if (!fsm.enabled || !fsm.gameObject.activeInHierarchy || !fsm.Fsm.Started) return false;
            return i.Action == HouseholdFuseAction.RemoveHolder || fsm.ActiveStateName == SyncCatalog.HouseholdFuses!["screwIdle"] || fsm.ActiveStateName == "Get scroll";
        }
        internal void ForgetHouseholdFuseIntents(byte actor)
        {
            _fuseSequences.Remove(actor);
            if (_fuseHolders != null && SessionManager.Instance?.IsHost == true) foreach (var h in _fuseHolders) NextFuseRevision(ref h.ControlRevision);
        }
        private static uint NextFuseRevision(ref uint revision) { revision = unchecked(revision + 1); if (revision == 0) revision = 1; return revision; }
        private void HouseholdFusesFailed(Exception e)
        {
            if (_fuseFailed) return; _fuseFailed = true;
            if (SessionManager.Instance?.IsHost == false && _fuseHolders != null)
                foreach (var h in _fuseHolders) { if (h.Screw != null) h.Screw.enabled = false; if (h.Removal != null) h.Removal.enabled = false; if (h.Insert != null) h.Insert.enabled = false; }
            WinterMPPlugin.Log.LogError("Shared household fuses unavailable: " + e);
        }
    }
}
