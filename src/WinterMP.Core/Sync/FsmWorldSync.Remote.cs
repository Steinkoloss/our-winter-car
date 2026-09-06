using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private const float GuestInteractionPoseMaxAgeSeconds = 2f;
        // Interaction colliders are often nested below a door/control pivot, so
        // this deliberately covers a car door or wide garage switch while still
        // excluding arbitrary map-wide FSM writes from a remote peer.
        private const float GuestInteractionMaxDistance = 12f;

        // ------------------------------------------------------------------ network -> world

        /// <summary>
        /// Host gate for a guest's generic FSM transition. FsmStateEnter has no
        /// player id on the wire, so SessionManager binds it to its authenticated
        /// peer before reaching here. Only catalogued interactable FSMs near that
        /// peer's fresh pose may be applied or relayed.
        /// </summary>
        public bool TryAcceptGuestStateEnter(
            FsmStateEnter message,
            byte playerId,
            out RadiatorThermostatState? thermostatState)
        {
            thermostatState = null;
            if (!TryGetGuestInteractable(message.NetId, message.StateName, out var fsm, out var path))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropped guest FSM state {message.NetId:X8} '{message.StateName}' (not an interactable state).");
                return false;
            }
            if (!IsGuestNear(session: SessionManager.Instance, playerId, fsm.transform.position))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropped distant guest FSM state {message.NetId:X8} '{message.StateName}' on {path}.");
                return false;
            }

            SyncedControl? scalarControl = null;
            if (_controls.TryGetValue(message.NetId, out var control) && control.ScalarFloat != null)
            {
                if (!fsm.gameObject.activeInHierarchy || !fsm.enabled)
                {
                    WinterMPPlugin.Log.LogWarning(
                        $"WorldSync: dropped inactive guest thermostat state {message.NetId:X8} '{message.StateName}'.");
                    return false;
                }

                scalarControl = control;
            }

            OnRemoteStateEnter(message);
            if (scalarControl != null)
            {
                if (TryBuildRadiatorThermostatState(message.NetId, out var scalarState))
                    thermostatState = scalarState;
                else
                    WinterMPPlugin.Log.LogWarning(
                        $"WorldSync: guest thermostat state {message.NetId:X8} applied without a readable scalar value.");
            }
            return true;
        }

        /// <summary>Host gate for the only allowed raw events: nearby bolt turns.</summary>
        public bool TryAcceptGuestRawEvent(FsmRawEvent message, byte playerId)
        {
            if (Array.IndexOf(AllowedRawEvents, message.EventName) < 0
                || !_bolts.TryGetValue(message.NetId, out var bolt) || bolt.Fsm == null)
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropped guest raw event {message.NetId:X8} '{message.EventName}'.");
                return false;
            }
            if (!IsGuestNearBolt(SessionManager.Instance, playerId, bolt.Fsm.transform.position))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropped distant guest bolt event {message.NetId:X8} '{message.EventName}'.");
                return false;
            }

            // Guest intents must be executable now; never park a turn for a later
            // installation or relay relative operations to other guests.
            if (!BoltReady(bolt) || NativePartIdentity.Phase(bolt.PartData) != NativePartPhase.Fitted) return false;
            if (IsBoltTimingAdjustment(bolt, message.EventName))
            {
                QueueHostBoltReport(message.NetId);
                return false;
            }
            if (!TryApplyRawEvent(message.NetId, message.EventName)) return false;
            QueueHostBoltReport(message.NetId);
            return true;
        }

        /// <summary>Guest reports request a correction; only the host's native turn changes the bolt.</summary>
        public bool TryAcceptGuestBoltState(BoltState message, byte playerId)
        {
            if (!BoltStatePolicy.Valid(message) || !_bolts.TryGetValue(message.NetId, out var bolt) || !BoltReady(bolt)
                || !IsGuestNearBolt(SessionManager.Instance, playerId, bolt.Fsm.transform.position))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: dropped guest bolt state {message.NetId:X8}.");
                return false;
            }
            QueueHostBoltReport(message.NetId, message);
            return true;
        }

        /// <summary>A nearby guest asks for the host's settled part scalars. Native
        /// install/bolt events already run on the host; reports cannot rewrite its wear.</summary>
        public bool TryAcceptGuestPartState(PartState message, byte playerId)
        {
            if (!PartStatePolicy.Valid(message) || !_parts.TryGetValue(message.NetId, out var part) || part.Fsm == null
                || !part.Fsm.gameObject.activeInHierarchy || !part.Fsm.enabled
                || !IsGuestNear(SessionManager.Instance, playerId, part.Fsm.transform.position))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: dropped guest part state {message.NetId:X8}.");
                return false;
            }
            // Hooks can report before the same-frame native transition completes.
            // Resample in ProcessPending after queued interaction events have run.
            _hostPartReports[message.NetId] = message;
            return true;
        }

        /// <summary>
        /// Host gate for a guest purchase. A payment intent is an authority request,
        /// not a generic remote FSM event: it must name a catalogued entry guard,
        /// come from the authenticated player near that exact target, and advance
        /// that player's monotonic purchase sequence.
        /// </summary>
        public bool TryAcceptGuestPurchaseIntent(PurchaseIntent intent, byte playerId)
        {
            if (intent.PlayerId != playerId || !_buys.TryGetValue(intent.NetId, out var buy) || buy.Fsm == null)
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropped guest purchase {intent.NetId:X8} '{intent.EventName}' (unknown target or identity).");
                return false;
            }
            if (!IsBuyEntryEvent(buy, intent.EventName))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropped guest purchase {intent.NetId:X8} '{intent.EventName}' (not an entry guard).");
                return false;
            }
            if (!IsGuestNear(SessionManager.Instance, playerId, buy.Fsm.transform.position))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropped distant guest purchase {intent.NetId:X8} '{intent.EventName}' on {buy.Path}.");
                return false;
            }
            if (_lastGuestPurchaseSequences.TryGetValue(playerId, out ushort previous))
            {
                ushort difference = (ushort)(intent.Sequence - previous);
                if (difference == 0 || difference > short.MaxValue)
                {
                    WinterMPPlugin.Log.LogWarning(
                        $"WorldSync: dropped stale guest purchase sequence {intent.Sequence} from player {playerId}.");
                    return false;
                }
            }

            _lastGuestPurchaseSequences[playerId] = intent.Sequence;
            return true;
        }

        public void OnRemoteStateEnter(FsmStateEnter message)
        {
            if (!TryApplyStateEnter(message.NetId, message.StateName))
                QueuePending(message.NetId, false, message.StateName);
        }

        private bool TryGetGuestInteractable(uint netId, string stateName, out PlayMakerFSM fsm, out string path)
        {
            if (_doors.TryGetValue(netId, out var door) && door.Fsm != null
                && Array.IndexOf(door.SyncedStates, stateName) >= 0)
            {
                fsm = door.Fsm;
                path = door.Path;
                return true;
            }
            if (_ignitions.TryGetValue(netId, out var ignition) && ignition.Fsm != null
                && Array.IndexOf(ignition.SyncedStates, stateName) >= 0)
            {
                fsm = ignition.Fsm;
                path = ignition.Path;
                return true;
            }
            if (_controls.TryGetValue(netId, out var control) && control.Fsm != null
                && Array.IndexOf(control.SyncedStates, stateName) >= 0)
            {
                fsm = control.Fsm;
                path = control.Path;
                return true;
            }
            if (_starters.TryGetValue(netId, out var starter) && starter.Fsm != null
                && Array.IndexOf(starter.SyncedStates, stateName) >= 0)
            {
                fsm = starter.Fsm;
                path = starter.Path;
                return true;
            }
            if (_parts.TryGetValue(netId, out var part) && part.Fsm != null
                && !_bridge.IsReplacementPart(part.Fsm)
                && Array.IndexOf(part.SyncedStates, stateName) >= 0
                && stateName != "Bolted" && stateName != "Unbolted" && stateName != "Stop")
            {
                fsm = part.Fsm;
                path = part.Path;
                return true;
            }

            fsm = null!;
            path = string.Empty;
            return false;
        }

        private static bool IsGuestNear(SessionManager? session, byte playerId, Vector3 targetPosition)
        {
            if (session == null || !session.IsHost) return false;
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f || now - player.LastTransformTime > GuestInteractionPoseMaxAgeSeconds)
                    return false;
                return (player.Position - targetPosition).sqrMagnitude
                    <= GuestInteractionMaxDistance * GuestInteractionMaxDistance;
            }
            return false;
        }

        private static bool IsBuyEntryEvent(SyncedBuy buy, string eventName)
        {
            foreach (var guard in buy.EntryGuards)
            {
                if (guard.TriggerEvent == eventName) return true;
            }
            return false;
        }

        public void OnRemoteRawEvent(FsmRawEvent message)
        {
            if (Array.IndexOf(AllowedRawEvents, message.EventName) < 0)
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: refusing non-whitelisted event '{message.EventName}'.");
                return;
            }

            if (!TryApplyRawEvent(message.NetId, message.EventName))
                QueuePending(message.NetId, true, message.EventName);
        }

        private bool TryApplyStateEnter(uint netId, string stateName)
        {
            if (_doors.TryGetValue(netId, out var door) && door.Fsm != null)
            {
                if (Array.IndexOf(door.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced state of {door.Path}; dropped.");
                    return true; // don't queue — it would never become valid
                }

                if (!door.Fsm.gameObject.activeInHierarchy || !door.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: door {netId:X8} -> '{stateName}' (remote).");
                _bridge.ApplyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(door.Fsm, stateName);
                    door.LastSyncedState = stateName;
                }
                finally
                {
                    _bridge.ApplyingRemote = false;
                }

                if (_bridge.SelfTest)
                    SessionManager.Instance?.SendChat($"[ws] applied '{stateName}' on {netId:X8}");

                return true;
            }

            if (_ignitions.TryGetValue(netId, out var ignition) && ignition.Fsm != null)
            {
                if (Array.IndexOf(ignition.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced ignition state of {ignition.Path}; dropped.");
                    return true;
                }

                if (!ignition.Fsm.gameObject.activeInHierarchy || !ignition.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: ignition {netId:X8} -> '{stateName}' (remote).");
                _bridge.ApplyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(ignition.Fsm, stateName);
                    ignition.LastSyncedState = stateName;
                }
                finally
                {
                    _bridge.ApplyingRemote = false;
                }

                return true;
            }

            if (_controls.TryGetValue(netId, out var control) && control.Fsm != null)
            {
                if (Array.IndexOf(control.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced control state of {control.Path}; dropped.");
                    return true;
                }

                if (!control.Fsm.gameObject.activeInHierarchy || !control.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: control {netId:X8} -> '{stateName}' (remote).");
                _vehicles.PrepareRemoteControl(control);
                _bridge.ApplyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(control.Fsm, stateName);
                    control.LastSyncedState = stateName;
                    VehicleWorldSync.FinishRemoteControl(control, stateName);
                }
                finally
                {
                    _bridge.ApplyingRemote = false;
                }

                return true;
            }

            if (_starters.TryGetValue(netId, out var starter) && starter.Fsm != null)
            {
                if (Array.IndexOf(starter.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced starter state of {starter.Path}; dropped.");
                    return true;
                }

                if (!starter.Fsm.gameObject.activeInHierarchy || !starter.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: starter {netId:X8} -> '{stateName}' (remote).");
                _bridge.ApplyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(starter.Fsm, stateName);
                    starter.LastSyncedState = stateName;
                }
                finally
                {
                    _bridge.ApplyingRemote = false;
                }

                return true;
            }

            if (_parts.TryGetValue(netId, out var part) && part.Fsm != null)
            {
                if (part.Replica) { _bridge.RequestObjectState(netId); return true; }
                if (IsDerivedPartState(stateName)) return true;
                if (Array.IndexOf(part.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced part state of {part.Path}; dropped.");
                    return true;
                }

                if (!part.Fsm.gameObject.activeInHierarchy || !part.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: part {netId:X8} -> '{stateName}' (remote).");
                _bridge.ApplyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(part.Fsm, stateName);
                    part.LastSyncedState = stateName;
                }
                finally
                {
                    _bridge.ApplyingRemote = false;
                }

                return true;
            }

            if (_buys.TryGetValue(netId, out var buy) && buy.Fsm != null)
            {
                if (Array.IndexOf(buy.ResultStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced buy state of {buy.Path}; dropped.");
                    return true;
                }

                if (!buy.Fsm.gameObject.activeInHierarchy || !buy.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: buy {netId:X8} -> '{stateName}' (remote).");
                _bridge.ApplyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(buy.Fsm, stateName);
                    buy.LastSyncedState = stateName;
                }
                finally
                {
                    _bridge.ApplyingRemote = false;
                }

                return true;
            }

            return false;
        }

        private bool TryApplyRawEvent(uint netId, string eventName)
        {
            if (!_bolts.TryGetValue(netId, out var bolt) || !BoltReady(bolt)) return false;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} {eventName} (remote).");
            bool applying = _bridge.ApplyingRemote;
            _bridge.ApplyingRemote = true;
            try
            {
                bolt.Fsm.SendEvent(eventName);
            }
            catch (Exception e) { FailBolt(bolt, e); return false; }
            finally
            {
                _bridge.ApplyingRemote = applying;
            }

            if (_bridge.SelfTest)
                SessionManager.Instance?.SendChat($"[ws] applied {eventName} on {netId:X8}");

            return true;
        }

        private void QueuePending(uint netId, bool isRawEvent, string name)
        {
            // The target FSM is in a disabled LOD cell or not yet scanned — keep the
            // event until the world streams it in (eventual consistency for doors
            // opened far away from the other player).
            _pending.Add(new PendingFsmApply
            {
                NetId = netId,
                IsRawEvent = isRawEvent,
                Name = name,
                ExpiresAt = Time.unscaledTime + PendingTtlSeconds,
            });
        }

        internal void ProcessPending()
        {
            var session = SessionManager.Instance;
            ProcessReplicaBolts();
            ProcessReplicaPartViews();

            if (_pendingBoltStates.Count > 0)
            {
                foreach (uint id in new List<uint>(_pendingBoltStates.Keys))
                {
                    var pending = _pendingBoltStates[id];
                    if (ApplyBoltState(id, pending.BoltTightness, pending.ScrewInt, pending.PartTightness, pending.ReceiptOrder))
                    { _pendingBoltStates.Remove(id); continue; }
                    if (Time.unscaledTime < pending.ExpiresAt) continue;
                    _pendingBoltStates.Remove(id);
                    if (session != null && !session.IsHost) _bridge.RequestObjectState(id);
                }
            }

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var entry = _pending[i];
                bool applied = entry.IsRawEvent
                    ? TryApplyRawEvent(entry.NetId, entry.Name)
                    : TryApplyStateEnter(entry.NetId, entry.Name);

                if (applied || Time.unscaledTime >= entry.ExpiresAt)
                {
                    if (!applied)
                    {
                        WinterMPPlugin.Log.LogWarning($"WorldSync: dropping expired event {entry.Name} for {entry.NetId:X8}.");
                        if (session != null && !session.IsHost)
                            _bridge.RequestObjectState(entry.NetId);
                    }

                    _pending.RemoveAt(i);
                }
            }

            if (_pendingRadiatorThermostatStates.Count > 0)
            {
                var thermostatIds = new List<uint>(_pendingRadiatorThermostatStates.Keys);
                foreach (uint id in thermostatIds)
                {
                    if (!_pendingRadiatorThermostatStates.TryGetValue(id, out var pending)) continue;
                    if (ApplyRadiatorThermostatState(id, pending.Rotation))
                    {
                        _pendingRadiatorThermostatStates.Remove(id);
                        continue;
                    }

                    if (Time.unscaledTime < pending.ExpiresAt) continue;
                    _pendingRadiatorThermostatStates.Remove(id);
                    WinterMPPlugin.Log.LogWarning($"WorldSync: dropping expired thermostat state for {id:X8}.");
                    if (session != null && !session.IsHost)
                        _bridge.RequestObjectState(id);
                }
            }

            if (_pendingPartStates.Count > 0)
            {
                var ids = new List<uint>(_pendingPartStates.Keys);
                foreach (uint id in ids)
                {
                    var pending = _pendingPartStates[id];
                    if (ApplyPartState(id, pending.Flags, pending.Tightness, pending.Wear, pending.ReceiptOrder))
                    { _pendingPartStates.Remove(id); continue; }
                    if (Time.unscaledTime < pending.ExpiresAt) continue;
                    _pendingPartStates.Remove(id);
                    if (session != null && !session.IsHost) _bridge.RequestObjectState(id);
                }
            }

            if (session != null && session.IsHost)
            {
                ProcessHostBoltReports(session);
                for (int i = _pendingPurchaseIntents.Count - 1; i >= 0; i--)
                {
                    var intent = _pendingPurchaseIntents[i];
                    if (TryExecuteHostPurchase(intent.NetId, intent.EventName)
                        || Time.unscaledTime >= intent.ExpiresAt)
                    {
                        if (Time.unscaledTime >= intent.ExpiresAt)
                            WinterMPPlugin.Log.LogWarning($"WorldSync: dropping expired buy intent {intent.EventName} for {intent.NetId:X8}.");
                        _pendingPurchaseIntents.RemoveAt(i);
                    }
                }
                if (_hostPartReports.Count > 0)
                {
                    var ids = new List<uint>(_hostPartReports.Keys);
                    foreach (uint id in ids)
                    {
                        var current = BuildPartState(id);
                        if (current == null) continue;
                        var reply = PartStatePolicy.HostReply(_hostPartReports[id], current);
                        _hostPartReports.Remove(id);
                        if (reply != null) session.SendWorldMessage(reply, WinterMP.Net.Channel.ReliableOrdered);
                    }
                }
            }
        }


    }
}
