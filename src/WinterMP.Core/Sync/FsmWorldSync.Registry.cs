using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
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
            if (!BeginRegistration(fsm)) return false;
            string path = ScenePath.Of(fsm.transform);
            if (!_bridge.PartIdentities.TryFsmId(fsm, path, out uint id)) return false;
            if (_doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: door id collision, not syncing '{path}'.");
                MarkRegistered(fsm);
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!HookState(fsm, state, () => OnDoorStateEntered(id, captured))) return false;
            }

            _doors[id] = new SyncedDoor { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            MarkRegistered(fsm);
            if (_bridge.FirstDoorRegisteredAt < 0f) _bridge.FirstDoorRegisteredAt = Time.unscaledTime;
            return true;
        }

        internal bool RegisterPart(PlayMakerFSM fsm, string[] syncedStates)
        {
            if (!BeginRegistration(fsm)) return false;
            string path = ScenePath.Of(fsm.transform);
            if (!_bridge.PartIdentities.TryFsmId(fsm, path, out uint id)) return false;
            if (_parts.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: part id collision, not syncing '{path}'.");
                MarkRegistered(fsm);
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!HookState(fsm, state, () => OnPartStateEntered(id, captured))) return false;
                if (state == "Stop" || state == "Bolted" || state == "Unbolted")
                {
                    if (!HookState(fsm, state, () => OnPartSettled(id))) return false;
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
            MarkRegistered(fsm);

            if (_pendingPartStates.TryGetValue(id, out var pending) && Time.unscaledTime < pending.ExpiresAt)
            {
                if (ApplyPartState(id, pending.Flags, pending.Tightness, pending.Wear, pending.ReceiptOrder))
                    _pendingPartStates.Remove(id);
            }

            return true;
        }

        internal bool RegisterBuy(PlayMakerFSM fsm, BuyProfile profile)
        {
            if (!BeginRegistration(fsm)) return false;
            string path = ScenePath.Of(fsm.transform);
            if (!_bridge.PartIdentities.TryFsmId(fsm, path, out uint id)) return false;
            if (_buys.ContainsKey(id) || _doors.ContainsKey(id) || _parts.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: buy id collision, not syncing '{path}'.");
                MarkRegistered(fsm);
                return false;
            }

            foreach (string state in profile.ResultStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!HookState(fsm, state, () => OnBuyResultStateEntered(id, captured))) return false;
            }

            foreach (var guard in profile.EntryGuards)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, guard.StateName)) return false;
                string capturedEvent = guard.TriggerEvent;
                if (!HookState(fsm, guard.StateName, () => OnBuyEntryGuard(id, capturedEvent))) return false;
            }

            _buys[id] = new SyncedBuy
            {
                Fsm = fsm,
                Path = path,
                EntryGuards = profile.EntryGuards,
                ResultStates = profile.ResultStates,
            };
            MarkRegistered(fsm);

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
            if (!fsm.Fsm.Initialized || !fsm.Fsm.Started || fsm.ActiveStateName == "Init") return false;
            if (!BeginRegistration(fsm)) return false;
            string path = ScenePath.Of(fsm.transform);
            if (!_bridge.PartIdentities.TryFsmId(fsm, path, out uint id)) return false;
            if (_bolts.ContainsKey(id) || _doors.ContainsKey(id) || _buys.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: bolt id collision, not syncing '{path}'.");
                MarkRegistered(fsm);
                return false;
            }

            var bolt = new SyncedBolt { Fsm = fsm, Path = path };
            try { BindNativeBolt(bolt); }
            catch (System.Exception e) { FailBolt(bolt, e); MarkRegistered(fsm); return false; }

            // Hook before native bounds checks so unsupported guest timing
            // adjustments can return to Set pos without reaching the mount.
            if (!HookState(fsm, "Tight?", () => OnBoltTurned(id, "TIGHTEN"))) return false;
            if (!HookState(fsm, "Loose?", () => OnBoltTurned(id, "UNTIGHTEN"))) return false;
            if (!HookState(fsm, "Set pos", () => OnBoltSettled(id))) return false;

            _bolts[id] = bolt;
            MarkRegistered(fsm);
            if (SessionManager.Instance?.IsHost == true) QueueHostBoltReport(id);

            if (_pendingBoltStates.TryGetValue(id, out var pending) && Time.unscaledTime < pending.ExpiresAt)
            {
                if (ApplyBoltState(id, pending.BoltTightness, pending.ScrewInt, pending.PartTightness, pending.ReceiptOrder))
                    _pendingBoltStates.Remove(id);
            }

            return true;
        }

        internal bool RegisterIgnition(PlayMakerFSM fsm, string[] syncedStates)
        {
            if (!BeginRegistration(fsm)) return false;
            string path = ScenePath.Of(fsm.transform);
            if (!_bridge.PartIdentities.TryFsmId(fsm, path, out uint id)) return false;
            if (_ignitions.ContainsKey(id) || _starters.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: ignition id collision, not syncing '{path}'.");
                MarkRegistered(fsm);
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!HookState(fsm, state, () => OnIgnitionStateEntered(id, captured))) return false;
            }

            _ignitions[id] = new SyncedIgnition { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            MarkRegistered(fsm);
            WinterMPPlugin.Log.LogInfo($"WorldSync: ignition registered: '{path}'.");
            return true;
        }

        internal bool RegisterControl(PlayMakerFSM fsm, string[] syncedStates)
        {
            return RegisterControl(fsm, syncedStates, null, null);
        }

        internal bool RegisterControl(PlayMakerFSM fsm, CatalogControlMatch match)
        {
            return RegisterControl(fsm, match.States, match.ScalarFloatName, match.ScalarCommitState);
        }

        private bool RegisterControl(
            PlayMakerFSM fsm,
            string[] syncedStates,
            string? scalarFloatName,
            string? scalarCommitState)
        {
            if (!BeginRegistration(fsm)) return false;
            string path = ScenePath.Of(fsm.transform);
            if (!_bridge.PartIdentities.TryFsmId(fsm, path, out uint id)) return false;
            if (_controls.ContainsKey(id) || _starters.ContainsKey(id) || _ignitions.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: control id collision, not syncing '{path}'.");
                MarkRegistered(fsm);
                return false;
            }

            HutongGames.PlayMaker.FsmFloat? scalarFloat = null;
            string commitState = scalarCommitState ?? string.Empty;
            if (scalarFloatName != null)
            {
                if (commitState.Length == 0 || !FsmHook.HasState(fsm, commitState))
                {
                    WinterMPPlugin.Log.LogWarning(
                        $"WorldSync: scalar control '{path}' has invalid commit-state metadata.");
                    return false;
                }

                scalarFloat = fsm.FsmVariables.FindFsmFloat(scalarFloatName);
                if (scalarFloat == null)
                {
                    WinterMPPlugin.Log.LogWarning(
                        $"WorldSync: scalar control '{path}' has no float '{scalarFloatName}'.");
                    return false;
                }

                if (!FsmHook.EnsureRemoteEntry(fsm, commitState)) return false;
                string capturedCommit = commitState;
                if (!HookState(fsm, capturedCommit, () => OnScalarControlCommitted(id))) return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!HookState(fsm, state, () => OnControlStateEntered(id, captured))) return false;
            }

            _controls[id] = new SyncedControl
            {
                Fsm = fsm,
                Path = path,
                SyncedStates = syncedStates,
                ScalarFloat = scalarFloat,
                ScalarCommitState = scalarFloat != null ? commitState : null,
            };
            MarkRegistered(fsm);

            if (_pendingRadiatorThermostatStates.TryGetValue(id, out var pending))
            {
                _pendingRadiatorThermostatStates.Remove(id);
                if (Time.unscaledTime < pending.ExpiresAt)
                    ApplyRadiatorThermostatState(id, pending.Rotation);
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: control registered: '{path}'.");
            return true;
        }

        internal bool RegisterStarter(PlayMakerFSM fsm, string[] syncedStates)
        {
            if (!BeginRegistration(fsm)) return false;
            string path = ScenePath.Of(fsm.transform);
            if (!_bridge.PartIdentities.TryFsmId(fsm, path, out uint id)) return false;
            if (_starters.ContainsKey(id) || _controls.ContainsKey(id) || _ignitions.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: starter id collision, not syncing '{path}'.");
                MarkRegistered(fsm);
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!HookState(fsm, state, () => OnStarterStateEntered(id, captured))) return false;
            }

            _starters[id] = new SyncedStarter { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            MarkRegistered(fsm);
            WinterMPPlugin.Log.LogInfo($"WorldSync: starter registered: '{path}'.");
            return true;
        }


    }
}
