using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        // Bit index -> the breakage event on the PartBreakages "Damages" FSM. Applying a mask
        // fires the event; detecting a break reads the fired event (Fsm.LastTransition).
        private static readonly string[] DamageEvents =
        {
            "BEARING1", "BEARING2", "BEARING3", "BEARING4", "BEARING5",
            "CRANKSHAFT", "HEADGASKET", "PISTON1", "PISTON2", "PISTON3", "PISTON4",
            "OILPAN", "TIMINGBELT", "SEIZE", "BLOCK", "CAMFAIL",
        };

        private const float DamageKeepAliveSeconds = 15f;

        /// <summary>
        /// Engine part-breakage is owner-authoritative (COVERAGE-ROADMAP 2.1). The
        /// <c>PartBreakages</c> FSM rolls per-client, so a non-owner is suppressed (Chance=0)
        /// and instead applies the owner's broadcast mask; the owner detects its own breaks
        /// and broadcasts the accumulated mask on change + keepalive.
        /// </summary>
        public void UpdateVehicleDamage(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            float now = Time.unscaledTime;

            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null) continue;
                EnsureVehicleSystemsProbe(item);
                if (item.PartBreakagesFsm == null) continue;

                if (item.LocallyOwned)
                {
                    EnsureDamageHooks(item);
                    HostOrOwnerBroadcastDamage(session, item, now);
                }
                else if (item.RemoteOwner != WorldSyncIds.NoOwner)
                {
                    // Not ours right now — never let the local RNG roll a divergent break.
                    SuppressLocalRoll(item);
                }
            }
        }

        private static void SuppressLocalRoll(SyncedItem item)
        {
            try { if (item.PartBreakageChanceVar != null) item.PartBreakageChanceVar.Value = 0f; }
            catch { /* best-effort */ }
        }

        // Hook every state; when a breakage global transition lands, LastTransition names the
        // event. Only an owner's own roll advances the live mask (a non-owner applying a
        // remote break also enters these states but is guarded out below).
        private void EnsureDamageHooks(SyncedItem item)
        {
            if (item.DamageHooksInstalled || item.PartBreakagesFsm == null) return;

            var states = item.PartBreakagesFsm.Fsm != null ? item.PartBreakagesFsm.Fsm.States : null;
            if (states == null) return;

            bool all = true;
            foreach (var state in states)
            {
                if (state == null) continue;
                var capturedItem = item;
                if (!FsmHook.OnStateEnter(item.PartBreakagesFsm, state.Name, () => OnDamageStateEntered(capturedItem)))
                    all = false;
            }
            item.DamageHooksInstalled = all;
        }

        private void OnDamageStateEntered(SyncedItem item)
        {
            // Only the owner's authoritative roll counts. A non-owner reaches these states
            // only while replaying the owner's mask, which must not echo back.
            if (!item.LocallyOwned || item.PartBreakagesFsm == null) return;

            string eventName;
            try
            {
                var last = item.PartBreakagesFsm.Fsm != null ? item.PartBreakagesFsm.Fsm.LastTransition : null;
                eventName = last != null ? last.EventName : null;
            }
            catch { return; }

            int bit = IndexOfDamageEvent(eventName);
            if (bit < 0) return;
            item.LiveDamageMask |= 1u << bit;
        }

        private void HostOrOwnerBroadcastDamage(SessionManager session, SyncedItem item, float now)
        {
            bool keepAlive = now >= item.NextDamageKeepAliveAt;
            bool changed = !item.HasSentDamage || item.LastSentDamageMask != item.LiveDamageMask;
            if (!changed && !keepAlive) return;
            if (now < item.NextDamageTickAt && !changed) return;

            item.NextDamageTickAt = now + 0.5f;
            if (keepAlive) item.NextDamageKeepAliveAt = now + DamageKeepAliveSeconds;
            item.HasSentDamage = true;
            item.LastSentDamageMask = item.LiveDamageMask;

            session.SendWorldMessage(new VehicleDamage
            {
                VehicleId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                DamageMask = item.LiveDamageMask,
                Sequence = ++item.OutDamageSequence,
            }, Channel.ReliableOrdered);
        }

        /// <summary>Host gate: only the authenticated current owner may drive a vehicle's damage.</summary>
        public bool TryAcceptGuestVehicleDamage(VehicleDamage message, byte playerId)
        {
            if (message.OwnerPlayerId != playerId
                || !_items.Items.TryGetValue(message.VehicleId, out var item)
                || !item.IsVehicle)
                return false;
            if (item.RemoteOwner != playerId && item.RemoteOwner != WorldSyncIds.NoOwner) return false;
            return true;
        }

        /// <summary>Apply an owner's breakage mask onto a locally non-owned vehicle.</summary>
        public void ApplyVehicleDamage(VehicleDamage message)
        {
            if (!_items.Items.TryGetValue(message.VehicleId, out var item) || !item.IsVehicle || item.Body == null)
                return;
            EnsureVehicleSystemsProbe(item);
            if (item.PartBreakagesFsm == null) return;
            if (item.LocallyOwned) return; // we're authoritative for our own car

            // Drop stale/duplicate sequences.
            ushort diff = (ushort)(message.Sequence - item.LastDamageSequence);
            if (item.LastDamageSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            item.LastDamageSequence = message.Sequence;

            SuppressLocalRoll(item);

            uint fresh = message.DamageMask & ~item.AppliedDamageMask;
            item.AppliedDamageMask = message.DamageMask;
            if (fresh == 0) return;

            for (int bit = 0; bit < DamageEvents.Length; bit++)
            {
                if ((fresh & (1u << bit)) == 0) continue;
                try { item.PartBreakagesFsm.SendEvent(DamageEvents[bit]); }
                catch (System.Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"VehicleWorldSync: damage event {DamageEvents[bit]} failed on {item.Path}: {e.Message}");
                }
            }

            SyncEventLog.Record("vehicle-damage", $"{item.Id:X8} mask {message.DamageMask:X}");
        }

        private static int IndexOfDamageEvent(string? eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return -1;
            for (int i = 0; i < DamageEvents.Length; i++)
                if (DamageEvents[i] == eventName) return i;
            return -1;
        }
    }
}
