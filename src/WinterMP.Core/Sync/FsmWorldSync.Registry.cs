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
        internal static bool ClassifyBuy(PlayMakerFSM fsm, out BuyProfile profile)
        {
            profile = default;
            if (!SyncCatalog.TryMatchBuy(fsm, out var catalog) || catalog == null)
                return false;

            profile.EntryGuards = ToBuyGuards(catalog.EntryGuards);
            profile.ResultStates = catalog.ResultStates;
            return true;
        }

        private static BuyEntryGuard[] ToBuyGuards(CatalogBuyGuard[] guards)
        {
            var result = new BuyEntryGuard[guards.Length];
            for (int i = 0; i < guards.Length; i++)
            {
                result[i] = new BuyEntryGuard
                {
                    StateName = guards[i].StateName,
                    TriggerEvent = guards[i].TriggerEvent,
                };
            }

            return result;
        }

        internal static bool HasAllStates(PlayMakerFSM fsm, params string[] states)
        {
            foreach (string state in states)
            {
                if (!FsmHook.HasState(fsm, state)) return false;
            }

            return true;
        }

        internal bool RegisterDoor(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: door id collision, not syncing '{path}'.");
                _bridge.HookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnDoorStateEntered(id, captured))) return false;
            }

            _doors[id] = new SyncedDoor { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _bridge.HookedFsms[fsm] = true;
            if (_bridge.FirstDoorRegisteredAt < 0f) _bridge.FirstDoorRegisteredAt = Time.unscaledTime;
            return true;
        }

        internal bool RegisterSpawnContainer(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint baseId = StableHash.Fnv1a32(path + "::" + fsm.FsmName);

            // Grocery bags are runtime clones that ALL share the scene path
            // "shopping bag(itemx)", so they all hash to the same base id. The first bag
            // would register and every later one would collide and never get its open
            // hook — which is exactly why opening a fresh bag stopped syncing. Reclaim
            // entries whose bag was destroyed, and salt past any still-live duplicate so
            // every concurrent bag gets its own hook. Container ids never need to match
            // across peers — they only scope the local capture and key manifest dedup.
            uint id = baseId;
            bool placed = false;
            for (uint salt = 0; salt < 64; salt++)
            {
                id = baseId + salt;
                if (_spawnContainers.TryGetValue(id, out var existing))
                {
                    if (existing.Fsm == null) { _spawnContainers.Remove(id); placed = true; break; } // dead — reclaim
                    if (existing.Fsm == fsm) { _bridge.HookedFsms[fsm] = true; return false; }       // already ours
                    continue;                                                                          // live duplicate
                }

                if (_doors.ContainsKey(id)) continue;
                placed = true;
                break;
            }

            if (!placed)
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: spawn-container id space full for '{path}'.");
                Util.BootTrace.Crumb($"SPAWN-REG idspace-full '{path}'");
                _bridge.HookedFsms[fsm] = true;
                return false;
            }

            // Hook-only: spawn states are never entered remotely (the spill is
            // replicated by clone capture + manifest, not by firing the FSM), so no
            // injected MP_* remote-entry transitions are needed here.
            foreach (string state in syncedStates)
            {
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnSpawnStateEntered(id, captured)))
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: spawn-container '{path}' could not hook state '{state}'; not syncing.");
                    return false;
                }
            }

            _spawnContainers[id] = new SyncedSpawnContainer { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _bridge.HookedFsms[fsm] = true;
            WinterMPPlugin.Log.LogInfo($"WorldSync: spawn-container registered: '{path}'.");
            Util.BootTrace.Crumb($"SPAWN-REG ok '{path}' states=[{string.Join(",", syncedStates)}]");
            return true;
        }

        internal bool RegisterPart(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_parts.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: part id collision, not syncing '{path}'.");
                _bridge.HookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnPartStateEntered(id, captured))) return false;
                if (state == "Stop" || state == "Bolted" || state == "Unbolted")
                {
                    if (!FsmHook.OnStateEnter(fsm, state, () => OnPartSettled(id))) return false;
                }
            }

            _parts[id] = new SyncedPart
            {
                Fsm = fsm,
                Path = path,
                SyncedStates = syncedStates,
                InstalledVar = fsm.FsmVariables.FindFsmBool("Installed"),
                TightnessVar = fsm.FsmVariables.FindFsmFloat("Tightness"),
                WearVar = fsm.FsmVariables.FindFsmFloat("Wear"),
            };
            _bridge.HookedFsms[fsm] = true;

            if (_pendingPartStates.TryGetValue(id, out var pending) && Time.unscaledTime < pending.ExpiresAt)
            {
                _pendingPartStates.Remove(id);
                ApplyPartState(id, pending.Flags, pending.Tightness, pending.Wear);
            }

            return true;
        }

        internal bool RegisterBuy(PlayMakerFSM fsm, BuyProfile profile)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_buys.ContainsKey(id) || _doors.ContainsKey(id) || _parts.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: buy id collision, not syncing '{path}'.");
                _bridge.HookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in profile.ResultStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnBuyResultStateEntered(id, captured))) return false;
            }

            foreach (var guard in profile.EntryGuards)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, guard.StateName)) return false;
                string capturedEvent = guard.TriggerEvent;
                if (!FsmHook.OnStateEnter(fsm, guard.StateName, () => OnBuyEntryGuard(id, capturedEvent))) return false;
            }

            _buys[id] = new SyncedBuy
            {
                Fsm = fsm,
                Path = path,
                EntryGuards = profile.EntryGuards,
                ResultStates = profile.ResultStates,
            };
            _bridge.HookedFsms[fsm] = true;

            for (int i = _pendingPurchaseIntents.Count - 1; i >= 0; i--)
            {
                var pending = _pendingPurchaseIntents[i];
                if (pending.NetId != id) continue;
                if (TryExecuteHostPurchase(id, pending.EventName))
                    _pendingPurchaseIntents.RemoveAt(i);
            }

            return true;
        }

        internal bool RegisterBolt(PlayMakerFSM fsm)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_bolts.ContainsKey(id) || _doors.ContainsKey(id) || _buys.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: bolt id collision, not syncing '{path}'.");
                _bridge.HookedFsms[fsm] = true;
                return false;
            }

            // "Tight?"/"Loose?" are entered exactly when the wrench turns the bolt;
            // their own BACK check rejects over/under-tightening on each machine.
            if (!FsmHook.OnStateEnter(fsm, "Tight?", () => OnBoltTurned(id, "TIGHTEN"))) return false;
            if (!FsmHook.OnStateEnter(fsm, "Loose?", () => OnBoltTurned(id, "UNTIGHTEN"))) return false;
            if (!FsmHook.OnStateEnter(fsm, "Set pos", () => OnBoltSettled(id))) return false;

            _bolts[id] = new SyncedBolt
            {
                Fsm = fsm,
                Path = path,
                BoltTightnessVar = fsm.FsmVariables.FindFsmInt("BoltTightness"),
                ScrewIntVar = fsm.FsmVariables.FindFsmInt("ScrewInt"),
                TightnessFVar = fsm.FsmVariables.FindFsmFloat("TightnessF"),
                ScrewFloatVar = fsm.FsmVariables.FindFsmFloat("ScrewFloat"),
            };
            _bridge.HookedFsms[fsm] = true;

            if (_pendingBoltStates.TryGetValue(id, out var pending) && Time.unscaledTime < pending.ExpiresAt)
            {
                _pendingBoltStates.Remove(id);
                ApplyBoltState(id, pending.BoltTightness, pending.ScrewInt);
            }

            return true;
        }

        internal bool RegisterIgnition(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_ignitions.ContainsKey(id) || _starters.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: ignition id collision, not syncing '{path}'.");
                _bridge.HookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnIgnitionStateEntered(id, captured))) return false;
            }

            _ignitions[id] = new SyncedIgnition { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _bridge.HookedFsms[fsm] = true;
            WinterMPPlugin.Log.LogInfo($"WorldSync: ignition registered: '{path}'.");
            return true;
        }

        internal bool RegisterControl(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_controls.ContainsKey(id) || _starters.ContainsKey(id) || _ignitions.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: control id collision, not syncing '{path}'.");
                _bridge.HookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnControlStateEntered(id, captured))) return false;
            }

            _controls[id] = new SyncedControl { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _bridge.HookedFsms[fsm] = true;
            WinterMPPlugin.Log.LogInfo($"WorldSync: control registered: '{path}'.");
            return true;
        }

        internal bool RegisterStarter(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_starters.ContainsKey(id) || _controls.ContainsKey(id) || _ignitions.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: starter id collision, not syncing '{path}'.");
                _bridge.HookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnStarterStateEntered(id, captured))) return false;
            }

            _starters[id] = new SyncedStarter { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _bridge.HookedFsms[fsm] = true;
            WinterMPPlugin.Log.LogInfo($"WorldSync: starter registered: '{path}'.");
            return true;
        }


    }
}
