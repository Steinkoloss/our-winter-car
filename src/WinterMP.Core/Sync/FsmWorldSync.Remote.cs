using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        // ------------------------------------------------------------------ network -> world

        public void OnRemoteStateEnter(FsmStateEnter message)
        {
            if (!TryApplyStateEnter(message.NetId, message.StateName))
                QueuePending(message.NetId, false, message.StateName);
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
            if (!_bolts.TryGetValue(netId, out var bolt) || bolt.Fsm == null) return false;
            if (!bolt.Fsm.gameObject.activeInHierarchy || !bolt.Fsm.enabled) return false;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} {eventName} (remote).");
            _bridge.ApplyingRemote = true;
            try
            {
                bolt.Fsm.SendEvent(eventName);
            }
            finally
            {
                _bridge.ApplyingRemote = false;
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

            if (session != null && session.IsHost)
            {
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
            }
        }


    }
}
