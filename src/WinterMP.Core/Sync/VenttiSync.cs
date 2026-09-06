using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;

namespace WinterMP.Core.Sync
{
    /// <summary>Host-owned Ventti hands, native presentation and property transfers.</summary>
    internal sealed partial class VenttiSync
    {
        private const float ProbeIntervalSeconds = 5f, HostTickSeconds = 1f, KeepAliveSeconds = 20f;
        private uint _tableId;
        private Transform? _anchor;
        private PlayMakerFSM? _bet, _hitPlayer, _hitHouse, _stand, _manager;
        private FsmFloat? _betValue;
        private FsmInt? _playerHand, _houseHand;
        private FsmBool? _betCar, _betHouse;
        private readonly FsmSuppressor _managerSuppressor = new FsmSuppressor();
        private bool _built, _lockdownLogged;
        private bool _wagersSaved, _savedCarWager, _savedHouseWager;
        private float _nextProbeAt, _nextHostTickAt, _nextKeepAliveAt;
        private bool ManagerLocked => _manager != null && _managerSuppressor.Active && !_manager.enabled;

        public void Clear()
        {
            ClearReactions();
            ClearGame();
            ClearProperties();
            ClearTable();
            if (_wagersSaved)
            {
                if (_betCar != null) _betCar.Value = _savedCarWager;
                if (_betHouse != null) _betHouse.Value = _savedHouseWager;
            }
            _managerSuppressor.Restore();
            _anchor = null;
            _bet = _hitPlayer = _hitHouse = _stand = _manager = null;
            _betValue = null; _playerHand = _houseHand = null; _betCar = _betHouse = null;
            _tableId = 0; _built = _lockdownLogged = _wagersSaved = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0;
        }

        public void Update(SessionManager session)
        {
            try { UpdateTable(session); }
            catch (Exception e) { DisableTable(e); }
        }

        private void UpdateTable(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            UpdateProperties(session);
            EnsureBuilt();
            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                Locate();
            }
            if (!session.IsHost) GuestLockdown();
            UpdateGame(session);
            if (_gameReady || _gameReplica.Current != null) return;
            if (!session.IsHost) { ApplyTable(); return; }
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        private void GuestLockdown()
        {
            if (!_wagersSaved && _betCar != null && _betHouse != null)
            {
                _savedCarWager = _betCar.Value; _savedHouseWager = _betHouse.Value; _wagersSaved = true;
            }
            if (_manager != null && !_managerSuppressor.Active && _managerSuppressor.Suppress(_manager))
            {
                if (!_lockdownLogged)
                {
                    _lockdownLogged = true;
                    WinterMPPlugin.Log.LogInfo("VenttiSync: guest property resolver paused; host owns transfers.");
                    SyncEventLog.Record("ventti-guest-lockdown", "table " + _tableId);
                }
            }
            if (_manager != null && _managerSuppressor.Active && _manager.enabled) _manager.enabled = false;
            // Never arm the guest's transfer states, even if another native FSM writes the flags.
            if (_betCar != null) _betCar.Value = false;
            if (_betHouse != null) _betHouse.Value = false;
        }

        public void ForceBroadcast()
        {
            _sceneTickAt = _sceneKeepAliveAt = 0;
            _nextPropertyKeepAliveAt = _nextPropertyTickAt = _nextHostTickAt = _nextKeepAliveAt = _gameBroadcastAt = 0;
        }

        public void ForgetPlayer(byte playerId)
        {
            _ledger?.ForgetPlayer(playerId);
            _requestAt.Remove(playerId);
            _gameBroadcastAt = 0;
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            var bindings = SyncCatalog.VenttiTable;
            if (bindings == null) return;
            _tableId = StableHash.Fnv1a32(bindings["tablePath"]);
            _built = true;
        }

        private void Locate()
        {
            if (_tableDisabled) return;
            try { LocateTableBindings(); }
            catch (Exception e) { DisableTable(e); }
        }

        private void LocateTableBindings()
        {
            var bindings = SyncCatalog.VenttiTable;
            if (bindings == null) return;
            if (_anchor == null)
            {
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || fsm.FsmName != bindings["fsm"]) continue;
                    var root = FindPropertyAncestor(fsm.transform, ScenePath.Of(fsm.transform), bindings["tablePath"]);
                    if (root != null) { _anchor = root.transform; break; }
                }
            }
            if (_anchor == null) return;
            if (_bet == null) _bet = FindChildFsm(_anchor, bindings["betPath"], bindings["fsm"]);
            if (_hitPlayer == null) _hitPlayer = FindChildFsm(_anchor, bindings["playerPath"], bindings["fsm"]);
            if (_hitHouse == null) _hitHouse = FindChildFsm(_anchor, bindings["housePath"], bindings["fsm"]);
            if (_stand == null) _stand = FindChildFsm(_anchor, bindings["standPath"], bindings["fsm"]);
            if (_manager == null) _manager = FindChildFsm(_anchor, bindings["managerPath"], bindings["fsm"]);
            if (_betValue == null && _bet != null) _betValue = _bet.FsmVariables.FindFsmFloat(bindings["stake"]);
            if (_playerHand == null && _hitPlayer != null) _playerHand = _hitPlayer.FsmVariables.FindFsmInt(bindings["playerHand"]);
            if (_houseHand == null && _hitHouse != null) _houseHand = _hitHouse.FsmVariables.FindFsmInt(bindings["houseHand"]);
            if (_resultStatus == null)
            {
                var result = FindChildFsm(_anchor, bindings["managerPath"], bindings["resultFsm"]);
                if (result != null) _resultStatus = result.FsmVariables.FindFsmString(bindings["result"]);
            }
            if (_manager != null)
            {
                if (_betCar == null) _betCar = _manager.FsmVariables.FindFsmBool(bindings["wagerCar"]);
                if (_betHouse == null) _betHouse = _manager.FsmVariables.FindFsmBool(bindings["wagerHouse"]);
            }
        }

        private static PlayMakerFSM? FindChildFsm(Transform root, string childPath, string fsmName)
        {
            var child = root.Find(childPath);
            if (child == null) return null;
            foreach (var fsm in child.GetComponents<PlayMakerFSM>())
                if (fsm != null && fsm.FsmName == fsmName) return fsm;
            return null;
        }
    }
}
