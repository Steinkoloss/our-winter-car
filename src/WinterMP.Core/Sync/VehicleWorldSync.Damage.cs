using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private const float DamageKeepAliveSeconds = 15f;
        private readonly Dictionary<FsmState, FsmHookAction> _damageHooks = new Dictionary<FsmState, FsmHookAction>();

        public void UpdateVehicleDamage(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            float now = Time.unscaledTime;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || item.DamageSyncDisabled) continue;
                try { UpdateDamage(session, item, now); }
                catch (Exception e) { DisableDamageSync(item, e); }
            }
        }

        private void UpdateDamage(SessionManager session, SyncedItem item, float now)
        {
            EnsureVehicleSystemsProbe(item);
            if (item.PartBreakagesFsm == null) return;
            EnsureDamageHooks(item);
            if (IsDamageAuthority(session, item))
            {
                item.PendingDamage = null;
                if (now < item.NextDamageTickAt) return;
                item.NextDamageTickAt = now + 0.5f;
                var state = ReadDamageState(item, session.LocalPlayerId);
                item.LiveDamageMask = state.DamageMask;
                item.AppliedDamageMask = state.DamageMask;
                bool keepAlive = now >= item.NextDamageKeepAliveAt;
                if (!keepAlive && item.LastSentDamage != null
                    && VehicleDamagePolicy.SameCondition(item.LastSentDamage, state)) return;
                item.NextDamageKeepAliveAt = now + DamageKeepAliveSeconds;
                item.LastSentDamage = state;
                state.Sequence = ++item.OutDamageSequence;
                session.SendWorldMessage(state, Channel.ReliableOrdered);
            }
            else if (item.PendingDamage != null)
                ApplyDamageValues(item, item.PendingDamage);
        }

        private static bool IsDamageAuthority(SessionManager? session, SyncedItem item)
        {
            if (session == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected))
                return true;
            return item.LocallyOwned || (session.IsHost && item.RemoteOwner == WorldSyncIds.NoOwner);
        }

        private void EnsureDamageHooks(SyncedItem item)
        {
            var bindings = SyncCatalog.VehicleDamage;
            if (item.DamageHooksInstalled || item.PartBreakagesFsm == null || bindings == null) return;
            if (!FsmHook.EnsureRemoteEntry(item.PartBreakagesFsm, bindings.IdleState)) return;
            var states = item.PartBreakagesFsm.Fsm.States;
            if (states == null) return;
            bool all = true;
            foreach (var state in states)
            {
                if (state == null || state.Name == bindings.IdleState || _damageHooks.ContainsKey(state)) continue;
                uint id = item.Id;
                var fsm = item.PartBreakagesFsm;
                var hook = new FsmHookAction(() =>
                {
                    // Resolve the current item after reconnect instead of retaining an
                    // old ownership flag in a hook that survives the session.
                    if (!_items.Items.TryGetValue(id, out var current) || current.PartBreakagesFsm != fsm) return;
                    if (!current.ApplyingRemoteDamage && !IsDamageAuthority(SessionManager.Instance, current))
                        FsmHook.FireRemoteEntry(fsm, bindings.IdleState);
                });
                try
                {
                    var actions = state.Actions ?? new FsmStateAction[0];
                    var expanded = new FsmStateAction[actions.Length + 1];
                    expanded[0] = hook;
                    Array.Copy(actions, 0, expanded, 1, actions.Length);
                    state.Actions = expanded;
                    _damageHooks.Add(state, hook);
                }
                catch (Exception)
                {
                    // Inactive, uninitialized FSMs cannot materialize ActionData yet.
                    // Retry instead of disabling the whole vehicle on a late bind.
                    all = false;
                }
            }
            item.DamageHooksInstalled = all;
        }

        private static FsmFloat? FindDamageWear(SyncedItem item, int bit)
        {
            var bindings = SyncCatalog.VehicleDamage;
            if (item.PartBreakagesFsm == null || bindings == null || bindings.PartVariables[bit].Length == 0) return null;
            if (item.DamagePartReferences == null)
                item.DamagePartReferences = new FsmGameObject?[VehicleDamage.PartSlots];
            var reference = item.DamagePartReferences[bit];
            if (reference == null)
            {
                reference = item.PartBreakagesFsm.FsmVariables.FindFsmGameObject(bindings.PartVariables[bit]);
                item.DamagePartReferences[bit] = reference;
            }
            // Do not cache the target's Wear: a replacement changes reference.Value.
            var target = reference != null ? reference.Value : null;
            if (target == null) return null;
            foreach (var fsm in target.GetComponents<PlayMakerFSM>())
                if (fsm != null && fsm.FsmName == bindings.PartFsmName)
                    return fsm.FsmVariables.FindFsmFloat(bindings.WearVariable);
            return null;
        }

        private static VehicleDamage ReadDamageState(SyncedItem item, byte owner)
        {
            var state = new VehicleDamage
            {
                VehicleId = item.Id, OwnerPlayerId = owner,
            };
            uint broken = 0;
            for (int bit = 0; bit < VehicleDamage.PartSlots; bit++)
            {
                var wear = FindDamageWear(item, bit);
                if (wear == null || float.IsNaN(wear.Value) || float.IsInfinity(wear.Value)) continue;
                state.KnownPartsMask |= 1u << bit;
                state.Wear[bit] = wear.Value;
                if (wear.Value <= 0f) broken |= 1u << bit;
            }
            state.DamageMask = VehicleDamagePolicy.Reconcile(item.LiveDamageMask | item.AppliedDamageMask,
                state.KnownPartsMask, broken);
            return state;
        }

        private VehicleDamage? TryReadDamageState(SyncedItem item, byte owner)
        {
            if (item.DamageSyncDisabled || item.Body == null) return null;
            try
            {
                EnsureVehicleSystemsProbe(item);
                if (item.PartBreakagesFsm == null) return null;
                return ReadDamageState(item, owner);
            }
            catch (Exception e) { DisableDamageSync(item, e); return null; }
        }

        internal VehicleDamage? TryBuildDamageSnapshot(SyncedItem item, byte owner)
        {
            // Snapshot/checksum reads must not advance the periodic change baseline.
            var state = TryReadDamageState(item, owner);
            if (state != null) state.Sequence = ++item.OutDamageSequence;
            return state;
        }

        public bool TryAcceptGuestVehicleDamage(VehicleDamage message, byte playerId)
        {
            if (!VehicleDamagePolicy.IsValid(message) || message.OwnerPlayerId != playerId
                || !_items.Items.TryGetValue(message.VehicleId, out var item) || !item.IsVehicle
                || item.LocallyOwned || item.DamageSyncDisabled) return false;
            return item.RemoteOwner == playerId || item.RemoteOwner == WorldSyncIds.NoOwner;
        }

        public void ApplyVehicleDamage(VehicleDamage message)
        {
            if (!VehicleDamagePolicy.IsValid(message)
                || !_items.Items.TryGetValue(message.VehicleId, out var item) || !item.IsVehicle
                || item.Body == null || item.LocallyOwned || item.DamageSyncDisabled) return;
            if (item.HasDamageSequence && message.OwnerPlayerId == item.LastDamageSequenceOwner)
            {
                ushort difference = (ushort)(message.Sequence - item.LastDamageSequence);
                if (difference == 0 || difference > short.MaxValue) return;
            }
            item.HasDamageSequence = true;
            item.LastDamageSequenceOwner = message.OwnerPlayerId;
            item.LastDamageSequence = message.Sequence;
            item.PendingDamage = message;
            item.LiveDamageMask = message.DamageMask;
            try
            {
                EnsureVehicleSystemsProbe(item);
                EnsureDamageHooks(item);
                ApplyDamageValues(item, message);
            }
            catch (Exception e) { DisableDamageSync(item, e); }
        }

        private static void ApplyDamageValues(SyncedItem item, VehicleDamage message)
        {
            var bindings = SyncCatalog.VehicleDamage;
            if (item.PartBreakagesFsm == null || item.LocallyOwned || bindings == null) return;
            for (int bit = 0; bit < VehicleDamage.PartSlots; bit++)
            {
                uint flag = 1u << bit;
                if ((message.KnownPartsMask & flag) == 0) continue;
                var wear = FindDamageWear(item, bit);
                if (wear == null) continue; // retained message retries after a late/replacement bind
                bool broken = (message.DamageMask & flag) != 0;
                bool replay = broken && ((item.AppliedDamageMask & flag) == 0 || (item.AppliedDamageParts & flag) == 0);
                wear.Value = message.Wear[bit];
                if (replay)
                {
                    if (!item.DamageHooksInstalled || !item.PartBreakagesFsm.Fsm.Active) continue;
                    item.ApplyingRemoteDamage = true;
                    try { item.PartBreakagesFsm.SendEvent(bindings.Events[bit]); }
                    finally { item.ApplyingRemoteDamage = false; }
                    // Vanilla sets Wear to zero while replaying breakage; preserve
                    // the exact authoritative value (which can already be negative).
                    wear.Value = message.Wear[bit];
                }
                item.AppliedDamageMask = VehicleDamagePolicy.Reconcile(item.AppliedDamageMask, flag, message.DamageMask);
                item.AppliedDamageParts |= flag;
            }
        }

        private static void DisableDamageSync(SyncedItem item, Exception error)
        {
            item.DamageSyncDisabled = true;
            item.PendingDamage = null;
            WinterMPPlugin.Log.LogError("VehicleWorldSync: damage sync disabled for '" + item.Path + "': " + error);
        }

        internal void ClearDamageHooks()
        {
            foreach (var entry in _damageHooks)
            {
                try
                {
                    var actions = new List<FsmStateAction>(entry.Key.Actions);
                    actions.Remove(entry.Value);
                    entry.Key.Actions = actions.ToArray();
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug("VehicleWorldSync: damage hook cleanup: " + e.Message);
                }
            }
            _damageHooks.Clear();
            foreach (var item in _items.Items.Values)
            {
                item.DamageHooksInstalled = false;
                item.DamageSyncDisabled = false;
                item.PendingDamage = null;
                item.LastSentDamage = null;
                item.HasDamageSequence = false;
                item.AppliedDamageParts = 0;
                item.NextDamageTickAt = 0f;
            }
        }
    }
}
