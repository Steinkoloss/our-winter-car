using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private void InstallGuestFuses()
        {
            var c = SyncCatalog.HouseholdFuses!;
            foreach (var table in _fuseTables!)
            {
                var saved = new object[table.Fuses.Count]; table.Fuses.CopyTo(saved, 0);
                _fuseRestore.Add(() => { for (int i = 0; i < saved.Length; i++) table.Fuses[i] = saved[i]; });
                foreach (var slot in table.Slots)
                {
                    bool active = slot.gameObject.activeSelf;
                    _fuseRestore.Add(() => { if (slot != null) slot.gameObject.SetActive(active); });
                    foreach (var variable in slot.FsmVariables.GameObjectVariables)
                    { var original = variable.Value; _fuseRestore.Add(() => variable.Value = original); }
                    ReplaceFuseControl(slot, c["assemblyWait"], c["assemblyIdle"], () => QueueFuseHolderFit(slot, table), true);
                    ReplaceFuseControl(slot, c["assemblyState"], c["assemblyIdle"], () => QueueFuseHolderFit(slot, table), true);
                }
            }
            foreach (var h in _fuseHolders!)
            {
                var original = h.Original; bool active = original.activeSelf;
                foreach (var fsm in original.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    var pause = new FsmSuppressor();
                    if (!pause.Suppress(fsm)) throw new InvalidOperationException("Cannot isolate saved household holder.");
                    _fuseRestore.Add(pause.Restore);
                }
                original.SetActive(false); _fuseRestore.Add(() => { if (original != null) original.SetActive(active); });
                var clone = (GameObject)UnityEngine.Object.Instantiate(original, original.transform.position, original.transform.rotation);
                _fuseClones.Add(clone); clone.name = "fuse holder(Clone)"; h.Object = clone;
                BindFuseHolder(h, c);
                ReplaceFuseControl(h.Screw, c["tightenState"], c["screwIdle"], () => QueueFuseAction(h, HouseholdFuseAction.Tighten), false);
                ReplaceFuseControl(h.Screw, c["loosenState"], c["screwIdle"], () => QueueFuseAction(h, HouseholdFuseAction.Loosen), false);
                ReplaceFuseControl(h.Removal, c["removeState"], c["removalIdle"], () => QueueFuseAction(h, HouseholdFuseAction.RemoveHolder), false);
                ReplaceFuseControl(h.Insert, c["assemblyWait"], c["assemblyIdle"], () => QueueFuseInsert(h), false);
                ReplaceFuseControl(h.Insert, c["assemblyState"], c["assemblyIdle"], () => QueueFuseInsert(h), false);
                StartFuseInput(h.Screw, c["screwIdle"]); StartFuseInput(h.Removal, c["removalIdle"]); StartFuseInput(h.Insert, c["assemblyIdle"]);
                var body = clone.GetComponent<Rigidbody>(); if (body == null) body = clone.AddComponent<Rigidbody>();
                body.mass = .2f; body.isKinematic = true; body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
        }
        private static void StartFuseInput(PlayMakerFSM fsm, string state)
        {
            fsm.Fsm.StartState = state; fsm.Fsm.RestartOnEnable = true; fsm.enabled = true;
        }
        private void ReplaceFuseControl(PlayMakerFSM fsm, string stateName, string idle, Action callback, bool restore)
        {
            var state = FsmHook.FindState(fsm, stateName) ?? throw new InvalidOperationException("Missing fuse input state.");
            var actions = state.Actions; var transitions = state.Transitions;
            var hook = new FsmHookAction(() =>
            {
                if (!_fuseFailed) try { callback(); } catch (Exception e) { HouseholdFusesFailed(e); }
                var gui = FsmVariables.GlobalVariables.FindFsmBool("GUIassemble"); if (gui != null) gui.Value = false;
            });
            hook.Init(state); var replacement = new FsmStateAction[] { hook };
            var routes = new[] { new FsmTransition { FsmEvent = FsmEvent.Finished, ToState = idle } };
            state.Actions = replacement; state.Transitions = routes;
            if (restore) _fuseRestore.Add(() => { if (ReferenceEquals(state.Actions, replacement)) state.Actions = actions;
                if (ReferenceEquals(state.Transitions, routes)) state.Transitions = transitions; });
        }
        private void PresentHouseholdFuses(HouseholdFuseState state)
        {
            foreach (var table in _fuseTables!)
            {
                for (int i = 0; i < table.Fuses.Count; i++)
                {
                    table.Fuses[i] = (state.PowerMask & (1 << (table.Offset + i))) != 0;
                    bool occupied = false;
                    foreach (var h in state.Holders) if (h.Slot == table.Offset + i) occupied = true;
                    table.Slots[i].gameObject.SetActive(!occupied);
                }
            }
            foreach (var h in _fuseHolders!)
            {
                var entry = state.Holders[h.Index]; bool fitted = entry.Slot != 255;
                bool changed = h.Last == null || h.Last.Slot != entry.Slot;
                var body = h.Object.GetComponent<Rigidbody>();
                if (changed)
                {
                    RemoveFuseMotion(h);
                    h.Object.transform.SetParent(fitted ? _fuseTables[h.Home].Pivots[entry.Slot - _fuseTables[h.Home].Offset] : null, true);
                    body.isKinematic = fitted; body.velocity = body.angularVelocity = Vector3.zero;
                }
                if (fitted || changed) { h.Object.transform.position = entry.Position.ToUnity(); h.Object.transform.rotation = entry.Rotation.ToUnity(); }
                h.Use.enabled = false; h.Tightness.Value = entry.Tightness; h.Fuse.Value = entry.Fuse;
                if (fitted) h.Use.FsmVariables.FindFsmInt("SlotID").Value = entry.Slot - _fuseTables[h.Home].Offset;
                h.Screw.FsmVariables.FindFsmFloat("Tightness").Value = entry.Tightness;
                h.Removal.FsmVariables.FindFsmFloat("Tightness").Value = entry.Tightness;
                h.Object.tag = fitted ? "Untagged" : "PART"; h.Object.layer = fitted ? 12 : 19;
                h.Mesh.SetActive((entry.Flags & 1) != 0); h.Tip.SetActive((entry.Flags & 2) != 0);
                h.Insert.gameObject.SetActive((entry.Flags & 4) != 0);
                h.Screw.enabled = h.Removal.enabled = fitted;
                h.Last = entry; h.ControlRevision = entry.ControlRevision;
                h.Object.SetActive(true); RegisterFuseBody(h);
            }
        }
        private void QueueFuseAction(FuseHolderBinding holder, HouseholdFuseAction action, uint itemId = 0, byte slot = 255)
        {
            var session = SessionManager.Instance;
            if (_remoteFuses == null || session == null || session.IsHost) return;
            foreach (var pending in _pendingFuseIntents.Values) if (pending.Holder == holder.Index) return;
            var intent = new HouseholdFuseIntent { PlayerId = session.LocalPlayerId, Holder = holder.Index, Slot = slot, ItemId = itemId,
                Action = action, ControlRevision = _remoteFuses.Holders[holder.Index].ControlRevision, Sequence = NextFuseRevision(ref _fuseIntentSequence) };
            _pendingFuseIntents.Add(intent.Sequence, intent); session.SendWorldMessage(intent, Channel.ReliableOrdered);
        }
        private void QueueFuseInsert(FuseHolderBinding holder)
        {
            var part = holder.Insert.FsmVariables.FindFsmGameObject("Part").Value;
            foreach (var pair in _supplies)
                if (pair.Value.Use != null && pair.Value.Use.gameObject == part && pair.Value.Body != null && IsHeldByLocalPlayer(pair.Value.Body))
                { QueueFuseAction(holder, HouseholdFuseAction.InsertFuse, pair.Key); return; }
        }
        private void QueueFuseHolderFit(PlayMakerFSM slot, FuseTableBinding table)
        {
            var part = slot.FsmVariables.FindFsmGameObject("Part").Value;
            foreach (var h in _fuseHolders!)
                if (h.Object == part && h.Item != null && IsHeldByLocalPlayer(h.Item.Body))
                {
                    if (!HouseholdFusePolicy.SameHome(h.Index, table.Offset)) return;
                    QueueFuseAction(h, HouseholdFuseAction.FitHolder, 0, (byte)(table.Offset + Array.IndexOf(table.Slots, slot))); return;
                }
        }
        internal void ReceiveHouseholdFuseResult(HouseholdFuseResult result)
        {
            var session = SessionManager.Instance;
            if (_fuseFailed || session == null || session.IsHost || result.PlayerId != session.LocalPlayerId
                || !_pendingFuseIntents.TryGetValue(result.Sequence, out var intent) || intent.Holder != result.Holder) return;
            _pendingFuseIntents.Remove(result.Sequence);
            if (!result.Accepted || !result.Shock || _fuseHolders == null || (intent.Action != HouseholdFuseAction.Tighten && intent.Action != HouseholdFuseAction.Loosen)) return;
            try { _fuseTables![_fuseHolders[result.Holder].Home].Shock.SendEvent(SyncCatalog.HouseholdFuses!["shockEvent"]); }
            catch (Exception e) { HouseholdFusesFailed(e); }
        }
    }
}
