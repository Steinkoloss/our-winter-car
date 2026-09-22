using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private const float DamageKeepAliveSeconds = 15f;
        private readonly VehicleDamageReplica _damageReplica = new VehicleDamageReplica();
        private readonly Dictionary<PlayMakerFSM, FsmSuppressor> _guestDamageSuppressors = new Dictionary<PlayMakerFSM, FsmSuppressor>();
        private float _nextDamageIsolationScanAt;
        private float _nextDamageIsolationErrorAt;
        private bool _damageIsolationReady;

        public void UpdateVehicleDamage(SessionManager session)
        {
            if (!IsDamageAuthority(session) || session.PlayerCount == 0) return;
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
            if (item.PartBreakagesFsm == null || now < item.NextDamageTickAt) return;
            item.NextDamageTickAt = now + 0.5f;
            var state = ReadDamageState(item, session.LocalPlayerId);
            item.LiveDamageMask = state.DamageMask;
            bool keepAlive = now >= item.NextDamageKeepAliveAt;
            if (!keepAlive && item.LastSentDamage != null
                && VehicleDamagePolicy.SameCondition(item.LastSentDamage, state)) return;
            item.NextDamageKeepAliveAt = now + DamageKeepAliveSeconds;
            item.LastSentDamage = state;
            state.Sequence = ++item.OutDamageSequence;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private static bool IsDamageAuthority(SessionManager? session)
        {
            bool active = session != null && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
            return VehicleDamagePolicy.IsAuthority(GuestSaveGuard.ProtectWorld, active, session != null && session.IsHost);
        }

        internal bool PrepareGuestDamageIsolation()
        {
            if (!GuestSaveGuard.ProtectWorld) return true;
            bool ready = true;
            foreach (var pair in _guestDamageSuppressors)
                try
                {
                    if (pair.Key != null)
                    {
                        pair.Key.Fsm.RestartOnEnable = false;
                        pair.Key.enabled = false;
                    }
                }
                catch (Exception e) { ready = false; NoteDamageIsolationFailure(e); }
            if (Time.unscaledTime < _nextDamageIsolationScanAt) return ready && _damageIsolationReady;
            _nextDamageIsolationScanAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            var bindings = SyncCatalog.VehicleDamage;
            if (bindings == null) return _damageIsolationReady = false;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || fsm.FsmName != bindings.FsmName || fsm.gameObject.name != bindings.ObjectName) continue;
                try { SuppressGuestDamage(fsm); }
                catch (Exception e) { ready = false; NoteDamageIsolationFailure(e); }
            }
            return _damageIsolationReady = ready;
        }

        private void NoteDamageIsolationFailure(Exception error)
        {
            if (Time.unscaledTime < _nextDamageIsolationErrorAt) return;
            _nextDamageIsolationErrorAt = Time.unscaledTime + 10f;
            WinterMPPlugin.Log.LogError("VehicleWorldSync: guest parts preserved; cannot pause native damage: " + error);
        }

        internal bool PrepareGuestDamageIsolationNow()
        {
            _nextDamageIsolationScanAt = 0f;
            return PrepareGuestDamageIsolation();
        }

        internal bool PrepareGuestEngineProtection() => _items.PrepareGuestEngineInputs();
        internal bool PrepareGuestEngineProtectionNow() => _items.PrepareGuestEngineInputs(force: true);
        internal bool PrepareGuestEngineProtectionForIsolation() => _items.PrepareGuestEngineInputs(force: true, admission: true);

        private void SuppressGuestDamage(PlayMakerFSM fsm)
        {
            if (_guestDamageSuppressors.ContainsKey(fsm)) return;
            var saved = new FsmSuppressor();
            if (!saved.Suppress(fsm)) throw new InvalidOperationException("Cannot pause native damage FSM.");
            _guestDamageSuppressors.Add(fsm, saved);
            SyncEventLog.Record("guest-damage-isolated", ScenePath.Of(fsm.transform));
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
            var target = reference != null ? reference.Value : null;
            if (target == null) return null;
            foreach (var fsm in target.GetComponents<PlayMakerFSM>())
                if (fsm != null && fsm.FsmName == bindings.PartFsmName)
                    return fsm.FsmVariables.FindFsmFloat(bindings.WearVariable);
            return null;
        }

        private static VehicleDamage ReadDamageState(SyncedItem item, byte owner)
        {
            var state = new VehicleDamage { VehicleId = item.Id, OwnerPlayerId = owner };
            uint broken = 0;
            for (int bit = 0; bit < VehicleDamage.PartSlots; bit++)
            {
                var wear = FindDamageWear(item, bit);
                if (wear == null || float.IsNaN(wear.Value) || float.IsInfinity(wear.Value)) continue;
                state.KnownPartsMask |= 1u << bit;
                state.Wear[bit] = wear.Value;
                if (wear.Value <= 0f) broken |= 1u << bit;
            }
            state.DamageMask = VehicleDamagePolicy.Reconcile(item.LiveDamageMask, state.KnownPartsMask, broken);
            return state;
        }

        private VehicleDamage? TryReadDamageState(SyncedItem item, byte owner)
        {
            // Native mount scalars and ActivePart references still belong to the
            // guest save. Even a driver/checksum must use the accepted host view.
            if (!IsDamageAuthority(SessionManager.Instance)) return _damageReplica.Get(item.Id);
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
            if (!IsDamageAuthority(SessionManager.Instance)) return null;
            var state = TryReadDamageState(item, owner);
            if (state != null) state.Sequence = ++item.OutDamageSequence;
            return state;
        }

        public void ApplyVehicleDamage(VehicleDamage message)
        {
            if (IsDamageAuthority(SessionManager.Instance)) return;
            // A native PISTON event rerolls oilpan/block collateral damage. No
            // received packet may enter that graph or write its saved mount data.
            if (_damageReplica.Receive(message))
                SyncEventLog.Record("vehicle-damage", message.VehicleId.ToString("X8") + " host mask " + message.DamageMask.ToString("X4"));
        }

        private static void DisableDamageSync(SyncedItem item, Exception error)
        {
            item.DamageSyncDisabled = true;
            WinterMPPlugin.Log.LogError("VehicleWorldSync: damage sync disabled for '" + item.Path + "': " + error);
        }

        internal void ClearDamageState()
        {
            _damageReplica.Clear();
            _nextDamageIsolationScanAt = 0f;
            _damageIsolationReady = false;
            // Guest save protection lasts until restart. Resuming a suspended
            // damage state on disconnect could follow a retained ActivePart.
            if (!GuestSaveGuard.ProtectWorld)
            {
                foreach (var saved in _guestDamageSuppressors.Values) saved.Restore();
                _guestDamageSuppressors.Clear();
            }
            foreach (var item in _items.Items.Values)
            {
                item.DamageSyncDisabled = false;
                item.LastSentDamage = null;
                item.LiveDamageMask = 0;
                item.OutDamageSequence = 0;
                item.NextDamageTickAt = 0f;
                item.NextDamageKeepAliveAt = 0f;
            }
        }
    }
}
