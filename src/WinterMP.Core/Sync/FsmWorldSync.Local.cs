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
        // ------------------------------------------------------------------ local -> network

        private void OnDoorStateEntered(uint netId, string stateName)
        {
            if (_bridge.ApplyingRemote) return;

            // Track even without peers: the join snapshot replays everything the
            // host touched before the guest arrived.
            if (_doors.TryGetValue(netId, out var door))
                door.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: door {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private void OnSpawnStateEntered(uint netId, string stateName)
        {
            // The bag fired naturally on THIS machine — spawns are never applied
            // remotely (the bag's "Confirm" state bounces remote entries back to
            // "Wait player" without a live player interaction, so replicating a
            // spill means capturing clones, not firing FSMs). This hook runs before
            // the state's spawn actions; the spill lands within the capture window.
            if (_bridge.ApplyingRemote) return;

            var session = SessionManager.Instance;
            if (session == null) return;
            if (!_spawnContainers.TryGetValue(netId, out var spawn) || spawn.Fsm == null) return;

            Vector3 near = spawn.Fsm.transform.position;
            if (session.State == SessionState.Hosting)
            {
                // Deliberately NOT gated on PlayerCount: a host that shops before the
                // friend joins must still capture — otherwise the scanner grabs the
                // clones under ordinal ids the guest can never resolve (invisible
                // items + a checksum-mismatch resync loop), and the retained manifest
                // is exactly what the join-snapshot replay needs.
                WinterMPPlugin.Log.LogInfo($"WorldSync: spawn-container {netId:X8} -> '{stateName}' (host).");
                Util.BootTrace.Crumb($"SPAWN-FIRE host {netId:X8} -> '{stateName}'");
                SyncEventLog.Record("spawn-fsm", $"{netId:X8} -> {stateName}");
                _bridge.StartHostSpawnCapture(netId, stateName, near);
                return;
            }

            if (session.State != SessionState.Connected) return;

            // Guest: let the spill run, capture the clones, offer them to the host —
            // it mints ids and answers with the manifest that binds them for everyone.
            WinterMPPlugin.Log.LogInfo($"WorldSync: spawn-container {netId:X8} -> '{stateName}' (guest, offering).");
            Util.BootTrace.Crumb($"SPAWN-OFFER guest {netId:X8} -> '{stateName}'");
            SyncEventLog.Record("spawn-offer", $"{netId:X8} -> {stateName}");
            _bridge.StartGuestSpawnOffer(netId, stateName, near);
        }

        private void OnPartStateEntered(uint netId, string stateName)
        {
            if (_bridge.ApplyingRemote) return;

            if (_parts.TryGetValue(netId, out var part))
                part.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: part {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private static bool SessionSyncActive(SessionManager session)
        {
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected)
                return false;
            return session.IsHost ? session.PlayerCount > 0 : true;
        }

        private void OnBuyEntryGuard(uint netId, string triggerEvent)
        {
            if (_bridge.ApplyingRemote) return;

            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !SessionSyncActive(session)) return;
            if (!_buys.TryGetValue(netId, out var buy))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: buy guard on unknown id {netId:X8}.");
                return;
            }

            _bridge.RestoreGuestMoney();

            WinterMPPlugin.Log.LogInfo($"WorldSync: buy {netId:X8} intent {triggerEvent} (guest).");
            session.SendWorldMessage(new PurchaseIntent
            {
                PlayerId = session.LocalPlayerId,
                NetId = netId,
                EventName = triggerEvent,
                Sequence = ++_outPurchaseSequence,
            }, Channel.ReliableOrdered);

            _bridge.ApplyingRemote = true;
            try
            {
                AbortGuestBuy(buy);
            }
            finally
            {
                _bridge.ApplyingRemote = false;
            }
        }

        private void OnBuyResultStateEntered(uint netId, string stateName)
        {
            if (_bridge.ApplyingRemote) return;

            if (_buys.TryGetValue(netId, out var buy))
                buy.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0 || !session.IsHost) return;

            _bridge.NotifyMoneyChanged();

            WinterMPPlugin.Log.LogInfo($"WorldSync: buy {netId:X8} -> '{stateName}' (local).");
            SyncEventLog.Record("buy", $"{netId:X8} -> {stateName}");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private static void AbortGuestBuy(SyncedBuy buy)
        {
            try
            {
                if (HasFsmEvent(buy.Fsm, "STOP"))
                    buy.Fsm.SendEvent("STOP");
                else if (HasFsmEvent(buy.Fsm, "RESET"))
                    buy.Fsm.SendEvent("RESET");
                else
                    buy.Fsm.SendEvent("FINISHED");
            }
            catch
            {
                // FSM may be tearing down.
            }
        }

        private static bool HasFsmEvent(PlayMakerFSM fsm, string eventName)
        {
            var events = fsm.Fsm != null ? fsm.Fsm.Events : null;
            if (events == null) return false;
            foreach (var evt in events)
            {
                if (evt != null && evt.Name == eventName) return true;
            }

            return false;
        }

        public void OnHostPurchaseIntent(PurchaseIntent intent)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: buy intent from player {intent.PlayerId}: {intent.NetId:X8} {intent.EventName}.");

            if (!_buys.ContainsKey(intent.NetId))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: buy intent for unregistered id {intent.NetId:X8} (scan still catching up?).");
            }

            if (!TryExecuteHostPurchase(intent.NetId, intent.EventName))
            {
                _pendingPurchaseIntents.Add(new PendingPurchaseIntent
                {
                    NetId = intent.NetId,
                    EventName = intent.EventName,
                    ExpiresAt = Time.unscaledTime + PendingTtlSeconds,
                });
            }
        }

        private static string? ResolveForcedPurchaseState(SyncedBuy buy, string eventName)
        {
            switch (eventName)
            {
                case "USE":
                    if (FsmHook.HasState(buy.Fsm, "Check car")) return "Check car";
                    if (FsmHook.HasState(buy.Fsm, "1") && FsmHook.HasState(buy.Fsm, "Remove order")) return "1";
                    if (FsmHook.HasState(buy.Fsm, "Check money")) return "Check money";
                    if (Array.IndexOf(buy.ResultStates, "Purchase") >= 0) return "Purchase";
                    break;
                case "PAY":
                    if (FsmHook.HasState(buy.Fsm, "Check money")) return "Check money";
                    if (FsmHook.HasState(buy.Fsm, "Wait payment")) return "Wait payment";
                    break;
                case "PAYMENT":
                    if (FsmHook.HasState(buy.Fsm, "Spawn package")) return "Spawn package";
                    // Fleetari: PAYMENT confirms the bill after Wait payment, not the initial order.
                    if (buy.Fsm.ActiveStateName == "Wait payment" && FsmHook.HasState(buy.Fsm, "State 3"))
                        return "State 3";
                    if (FsmHook.HasState(buy.Fsm, "Pending cost")) return "Pending cost";
                    if (FsmHook.HasState(buy.Fsm, "State 3")) return "State 3";
                    break;
                case "BUY":
                    if (Array.IndexOf(buy.ResultStates, "Fleetari 2") >= 0) return "Fleetari 2";
                    break;
                case "CLICK":
                    if (FsmHook.HasState(buy.Fsm, "Pending cost")) return "Pending cost";
                    break;
                case "PURCHASE":
                    if (FsmHook.HasState(buy.Fsm, "Purchase event")) return "Purchase event";
                    if (FsmHook.HasState(buy.Fsm, "Check inventory")) return "Check inventory";
                    if (Array.IndexOf(buy.ResultStates, "Add") >= 0) return "Add";
                    if (Array.IndexOf(buy.ResultStates, "Cashier") >= 0) return "Cashier";
                    if (Array.IndexOf(buy.ResultStates, "Purchase") >= 0) return "Purchase";
                    break;
                case "DEPURCHASE":
                    if (FsmHook.HasState(buy.Fsm, "Check if 0")) return "Check if 0";
                    if (Array.IndexOf(buy.ResultStates, "Subtract") >= 0) return "Subtract";
                    if (Array.IndexOf(buy.ResultStates, "State 1") >= 0) return "State 1";
                    break;
            }

            return null;
        }

        private bool TryExecuteHostPurchase(uint netId, string eventName)
        {
            if (!_buys.TryGetValue(netId, out var buy) || buy.Fsm == null) return false;
            if (!buy.Fsm.gameObject.activeInHierarchy || !buy.Fsm.enabled) return false;

            // SendEvent(USE) only works when the FSM is already in Wait button — the
            // host is often elsewhere in the shop. Jump straight into the purchase
            // pipeline via the injected MP_* global transition instead.
            string? forcedState = ResolveForcedPurchaseState(buy, eventName);
            if (forcedState != null)
            {
                if (!FsmHook.EnsureRemoteEntry(buy.Fsm, forcedState)) return false;

                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: buy {netId:X8} remote entry '{forcedState}' for {eventName} (host).");
                FsmHook.FireRemoteEntry(buy.Fsm, forcedState);
                return true;
            }

            if (!HasFsmEvent(buy.Fsm, eventName)) return false;

            WinterMPPlugin.Log.LogInfo($"WorldSync: buy {netId:X8} event {eventName} (host).");
            buy.Fsm.SendEvent(eventName);
            return true;
        }

        private void OnPartSettled(uint netId)
        {
            if (_bridge.ApplyingRemote) return;
            if (!_parts.TryGetValue(netId, out var part)) return;

            ReadPartVars(part, out byte flags, out byte tightness, out byte wear);
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: part {netId:X8} settled installed={((flags & PartState.FlagInstalled) != 0)} tightness={tightness} wear={wear} (local).");
            session.SendWorldMessage(new PartState
            {
                NetId = netId,
                Flags = flags,
                Tightness = tightness,
                Wear = wear,
            }, Channel.ReliableOrdered);
        }

        private static void ReadPartVars(SyncedPart part, out byte flags, out byte tightness, out byte wear)
        {
            bool installed = part.InstalledVar != null && part.InstalledVar.Value;
            flags = installed ? PartState.FlagInstalled : (byte)0;
            tightness = EncodeUnitFloat(part.TightnessVar != null ? part.TightnessVar.Value : 0f);
            wear = EncodeUnitFloat(part.WearVar != null ? part.WearVar.Value : 0f);
        }

        private static byte EncodeUnitFloat(float value) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 0, 255);

        private static float DecodeUnitFloat(byte value) => value / 255f;

        internal static bool ShouldIncludePartSnapshot(SyncedPart part, out byte flags, out byte tightness, out byte wear)
        {
            ReadPartVars(part, out flags, out tightness, out wear);
            if ((flags & PartState.FlagInstalled) != 0) return true;
            if (tightness > 0) return true;
            if (wear > 0) return true;

            try
            {
                string active = part.Fsm.Fsm.ActiveStateName;
                return active == "Bolted" || active == "Unbolted";
            }
            catch
            {
                return false;
            }
        }

        private void OnBoltTurned(uint netId, string eventName)
        {
            if (_bridge.ApplyingRemote) return;
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} {eventName} (local).");
            session.SendWorldMessage(new FsmRawEvent { NetId = netId, EventName = eventName }, Channel.ReliableOrdered);
        }

        private void OnBoltSettled(uint netId)
        {
            if (_bridge.ApplyingRemote) return;
            if (!_bolts.TryGetValue(netId, out var bolt)) return;

            ReadBoltVars(bolt, out ushort tightness, out ushort screwInt);
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} settled tightness={tightness} screw={screwInt} (local).");
            session.SendWorldMessage(new BoltState
            {
                NetId = netId,
                BoltTightness = tightness,
                ScrewInt = screwInt,
            }, Channel.ReliableOrdered);
        }

        internal static void ReadBoltVars(SyncedBolt bolt, out ushort tightness, out ushort screwInt)
        {
            int rawTightness = bolt.BoltTightnessVar != null ? bolt.BoltTightnessVar.Value : 0;
            int rawScrew = bolt.ScrewIntVar != null ? bolt.ScrewIntVar.Value : 0;
            tightness = (ushort)Mathf.Clamp(rawTightness, 0, ushort.MaxValue);
            screwInt = (ushort)Mathf.Clamp(rawScrew, 0, ushort.MaxValue);
        }

        private void OnIgnitionStateEntered(uint netId, string stateName)
        {
            if (_bridge.ApplyingRemote) return;

            if (_ignitions.TryGetValue(netId, out var ignition))
                ignition.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: ignition {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private void OnControlStateEntered(uint netId, string stateName)
        {
            if (_bridge.ApplyingRemote) return;

            if (_controls.TryGetValue(netId, out var control))
                control.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: control {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private void OnStarterStateEntered(uint netId, string stateName)
        {
            if (_bridge.ApplyingRemote) return;

            if (_starters.TryGetValue(netId, out var starter))
                starter.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: starter {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }


    }
}
