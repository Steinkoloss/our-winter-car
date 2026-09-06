using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VenttiSync
    {
        private readonly VenttiGameReplica _gameReplica = new VenttiGameReplica();
        private readonly Dictionary<byte, float> _requestAt = new Dictionary<byte, float>();
        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<PlayMakerFSM, FsmSuppressor> _controls = new Dictionary<PlayMakerFSM, FsmSuppressor>();
        private readonly Dictionary<FsmStateAction, bool> _cutActions = new Dictionary<FsmStateAction, bool>();
        private VenttiLedger? _ledger;
        private VenttiTableData? _gameConfig;
        private FsmFloat? _cash, _betMaximum, _opponentLoss, _stress;
        private FsmInt? _propertyStage, _playerStage, _houseStage;
        private bool _gameReady, _gameFailed, _gameHost;
        private float _gameProbeAt, _gameBroadcastAt, _gameRetryAt, _inputAt, _effectsUntil;
        private uint _effectRound;
        private bool _hasEffectRound;
        private float _savedMaximum, _savedOpponentLoss;
        private int _savedPropertyStage, _savedPlayerStage, _savedHouseStage;

        private void UpdateGame(SessionManager session)
        {
            if (_gameFailed || _tableDisabled) return;
            try
            {
                _gameHost = session.IsHost;
                if (!_gameReady && Time.unscaledTime >= _gameProbeAt)
                {
                    _gameProbeAt = Time.unscaledTime + 2f;
                    SetupGame();
                }
                if (!_gameReady) return;
                foreach (var pair in _controls)
                    if (pair.Key != null && pair.Key.enabled) pair.Key.enabled = false;
                if (_ledger != null && _cash != null)
                {
                    bool changed = _ledger.Advance(Time.unscaledTime);
                    _ledger.TryCollect(_cash.Value, out float cash);
                    if (cash != _cash.Value) { _cash.Value = cash; changed = true; BroadcastWallet(session); }
                    var state = _ledger.Snapshot();
                    ApplyHostOutcome(state);
                    if (state.Phase == VenttiPhase.Resolved && Time.unscaledTime >= _effectsUntil && NativeIdle())
                        changed |= _ledger.ResetResolved();
                    if (changed || Time.unscaledTime >= _gameBroadcastAt) BroadcastGame(session);
                }
                var pending = _gameReplica.Pending;
                if (pending != null && Time.unscaledTime >= _gameRetryAt)
                {
                    _gameRetryAt = Time.unscaledTime + 1f;
                    if (session.IsHost) OnRequest(pending);
                    else session.SendWorldMessage(pending, Channel.ReliableOrdered);
                }
                PresentGame();
                ReadInput(session);
            }
            catch (Exception e) { DisableGame(e); }
        }

        private void SetupGame()
        {
            _gameConfig = SyncCatalog.VenttiTable;
            var config = _gameConfig;
            if (config == null || config.Rules == null || !TableReady || _anchor == null || _bet == null
                || _hitPlayer == null || _hitHouse == null || _stand == null || _manager == null) return;
            // Guests cut the native debit/draw actions before waiting for any visual assets or host state.
            if (!_gameHost) PauseControls();
            _betMaximum = _bet.FsmVariables.FindFsmFloat(config["betMaximum"]);
            _opponentLoss = _manager.FsmVariables.FindFsmFloat(config["opponentLoss"]);
            _propertyStage = _manager.FsmVariables.FindFsmInt(config["propertyStage"]);
            _playerStage = _hitPlayer.FsmVariables.FindFsmInt(config["cardStage"]);
            _houseStage = _hitHouse.FsmVariables.FindFsmInt(config["cardStage"]);
            var banking = SyncCatalog.Banking;
            if (banking != null) _cash = FsmVariables.GlobalVariables.FindFsmFloat(banking.CashGlobal);
            _stress = FsmVariables.GlobalVariables.FindFsmFloat(config["stressGlobal"]);
            if (_betMaximum == null || _opponentLoss == null || _propertyStage == null
                || _playerStage == null || _houseStage == null || _cash == null || _stress == null) return;
            if (_gameHost)
            {
                var used = _anchor.Find(config["usedDeckPath"]);
                var deck = _anchor.Find(config["deckPath"]);
                // A live manager must have completed its SAVEGAME load. Waiting also
                // avoids adopting a hand whose native cards have already been chosen.
                if (!_manager.Fsm.Initialized || !NativeIdle() || used == null || used.childCount != 0
                    || deck == null || deck.childCount != 52 || _playerStage.Value != 0 || _houseStage.Value != 0) return;
            }
            foreach (var fsm in new[] { _bet, _hitPlayer, _hitHouse, _stand, _manager })
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            if (!BindPresentation()) return;
            if (_gameHost) ValidateOutcomeActions();
            CaptureGameVariables();
            PauseControls();
            if (_gameHost)
            {
                if (_betValue == null || _betCar == null || _betHouse == null) return;
                if (_betCar.Value && _betHouse.Value) throw new InvalidOperationException("Ambiguous native Ventti wager.");
                _ledger = new VenttiLedger(_tableId, config.Rules, _betMaximum.Value, _propertyStage.Value,
                    _opponentLoss.Value, _betValue.Value, _betHouse.Value ? VenttiWager.House
                    : _betCar.Value ? VenttiWager.Car : VenttiWager.Cash, _random.Next);
            }
            _gameReady = true;
            WinterMPPlugin.Log.LogInfo("VenttiSync: host ledger and native card controls ready.");
        }

        private bool NativeIdle()
        {
            return _manager != null && _manager.enabled && _manager.gameObject.activeInHierarchy && _gameConfig != null
                && Array.IndexOf(_gameConfig.IdleStates, _manager.ActiveStateName) >= 0;
        }

        private void PauseControls()
        {
            foreach (var fsm in new[] { _bet, _hitPlayer, _hitHouse, _stand }) PauseControl(fsm);
            if (_anchor == null || _gameConfig == null) return;
            PauseControl(FindChildFsm(_anchor, _gameConfig["managerPath"], _gameConfig["resultFsm"]));
            if (!_gameHost) PauseControl(_manager);
        }

        private void PauseControl(PlayMakerFSM? fsm)
        {
            if (fsm == null || _controls.ContainsKey(fsm)) return;
            if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            // Cutting actions as well as disabling the component prevents an external
            // EnableFSM/SetActive from running a debit before our next Update.
            foreach (var state in fsm.Fsm.States)
                foreach (var action in state.Actions) CutAction(action);
            var pause = new FsmSuppressor();
            if (!pause.Suppress(fsm)) throw new InvalidOperationException("Cannot pause native Ventti control.");
            _controls.Add(fsm, pause);
        }

        private void CutAction(FsmStateAction action)
        {
            if (!_cutActions.ContainsKey(action)) _cutActions.Add(action, action.Enabled);
            action.Enabled = false;
        }

        private void CaptureGameVariables()
        {
            if (_gameHost) return;
            CaptureTable();
            _savedMaximum = _betMaximum!.Value; _savedOpponentLoss = _opponentLoss!.Value;
            _savedPropertyStage = _propertyStage!.Value;
            _savedPlayerStage = _playerStage!.Value; _savedHouseStage = _houseStage!.Value;
        }

        public void OnRequest(VenttiRequest request)
        {
            var session = SessionManager.Instance;
            if (_gameFailed || !_gameReady || session == null || !session.IsHost || _ledger == null || _cash == null
                || request.TableId != _tableId) return;
            try
            {
                float now = Time.unscaledTime;
                if (_requestAt.TryGetValue(request.PlayerId, out float next) && now < next) return;
                _requestAt[request.PlayerId] = now + .1f;
                if (!_ledger.TryGetReceipt(request, out var receipt))
                {
                    // Finish native property/animation actions before accepting the next hand.
                    if (now < _effectsUntil || !NativeIdle()) return;
                    bool near = _anchor != null && GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)
                        && (position - _anchor.position).sqrMagnitude <= 36f;
                    receipt = _ledger.Apply(request, _cash.Value, now, near, out float cash);
                    _cash.Value = cash;
                    ApplyHostOutcome(_ledger.Snapshot());
                    SyncEventLog.Record("ventti-action", "player " + request.PlayerId + " seq " + request.Sequence
                        + " action " + request.Action + " result " + receipt.Status);
                }
                BroadcastGame(session);
                BroadcastWallet(session);
                session.SendWorldMessage(receipt, Channel.ReliableOrdered);
                if (request.PlayerId == session.LocalPlayerId) OnReceipt(receipt);
            }
            catch (Exception e) { DisableGame(e); }
        }

        public void OnGameState(VenttiLedgerState state)
        {
            var session = SessionManager.Instance;
            var config = SyncCatalog.VenttiTable;
            if (_gameFailed || session == null || session.IsHost || config?.Rules == null) return;
            EnsureBuilt();
            _gameReplica.Receive(_tableId, config.Rules, state);
        }

        public void OnReceipt(VenttiReceipt receipt)
        {
            var session = SessionManager.Instance;
            if (session == null || receipt.PlayerId != session.LocalPlayerId || !_gameReplica.Acknowledge(receipt)) return;
            _gameRetryAt = 0;
            if (receipt.Status == VenttiStatus.Accepted && receipt.Outcome != VenttiTableState.None
                && _stress != null && _gameConfig != null)
            {
                bool won = receipt.Outcome == VenttiTableState.Win || receipt.Outcome == VenttiTableState.WinCar
                    || receipt.Outcome == VenttiTableState.WinHouse;
                _stress.Value += won ? -_gameConfig.WinStress : _gameConfig.LoseStress;
            }
            else if (receipt.Status != VenttiStatus.Accepted && _gameReplica.Pending == null)
                session.AddSystemChat(receipt.Status == VenttiStatus.Busy ? "* Ventti is in use by another player."
                    : receipt.Status == VenttiStatus.Funds ? "* Ventti cannot accept that bet. Check the shared money."
                    : "* Ventti action declined. The table has been refreshed.");
        }

        private void QueueGame(VenttiAction action)
        {
            var session = SessionManager.Instance;
            if (!_gameReady || _gameFailed || session == null || Time.unscaledTime < _inputAt
                || (session.State != SessionState.Hosting && session.State != SessionState.Connected)) return;
            if (!_gameReplica.Queue(session.LocalPlayerId, action)) return;
            _inputAt = Time.unscaledTime + .2f;
            _gameRetryAt = 0;
        }

        internal VenttiLedgerState? BuildGameSnapshot() => _gameFailed ? null : _ledger?.Snapshot();

        private void BroadcastGame(SessionManager session)
        {
            if (_ledger == null || _gameConfig?.Rules == null) return;
            var state = _ledger.Snapshot();
            if (!VenttiGameReplica.IsValid(_gameConfig.Rules, state)) throw new InvalidOperationException("Invalid Ventti host state.");
            _gameReplica.Receive(_tableId, _gameConfig.Rules, state);
            _gameBroadcastAt = Time.unscaledTime + 5f;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private static void BroadcastWallet(SessionManager session)
        {
            var wallet = WorldSyncManager.Instance?.BuildWalletState();
            if (wallet != null) session.SendWorldMessage(wallet, Channel.ReliableOrdered);
        }

        private void DisableGame(Exception e)
        {
            if (_gameFailed) return;
            _gameFailed = true;
            ClearInteraction();
            // Keep owned actions cut until teardown. Falling back to native input
            // with a committed deck or unpaid escrow could settle the same hand twice.
            WinterMPPlugin.Log.LogError("VenttiSync: table controls disabled: " + e);
            SyncEventLog.Record("ventti-game-disabled", e.Message);
        }

        private void ClearGame()
        {
            VenttiLedgerState? remaining = null;
            try
            {
                if (_gameHost && _ledger != null && _cash != null)
                {
                    _ledger.FinishSession(_cash.Value, out float cash);
                    _cash.Value = cash;
                    remaining = _ledger.Snapshot();
                    ApplyHostOutcome(remaining);
                    WriteProgress(remaining);
                }
                RestorePresentation();
                if (!_gameHost && _gameReady)
                {
                    if (_betMaximum != null) _betMaximum.Value = _savedMaximum;
                    if (_opponentLoss != null) _opponentLoss.Value = _savedOpponentLoss;
                    if (_propertyStage != null) _propertyStage.Value = _savedPropertyStage;
                    if (_playerStage != null) _playerStage.Value = _savedPlayerStage;
                    if (_houseStage != null) _houseStage.Value = _savedHouseStage;
                }
            }
            catch (Exception e) { WinterMPPlugin.Log.LogError("VenttiSync: settlement/restore failed: " + e); }
            foreach (var pair in _cutActions) pair.Key.Enabled = pair.Value;
            foreach (var pause in _controls.Values) pause.Restore();
            try
            {
                if (remaining != null && _manager != null && _gameConfig != null)
                {
                    if (_betCar != null) _betCar.Value = false;
                    if (_betHouse != null) _betHouse.Value = false;
                    if (remaining.Phase != VenttiPhase.Closed || remaining.PendingCash != 0)
                    {
                        FsmHook.EnsureRemoteEntry(_manager, _gameConfig["resetState"]);
                        FsmHook.FireRemoteEntry(_manager, _gameConfig["resetState"]);
                    }
                    if (_betValue != null) _betValue.Value = remaining.PendingCash;
                    if (remaining.PendingCash != 0)
                        WinterMPPlugin.Log.LogWarning("VenttiSync: wallet cannot hold a precise return; native paid stake retains " + remaining.PendingCash + " mk.");
                }
            }
            catch (Exception e) { WinterMPPlugin.Log.LogError("VenttiSync: native reset failed: " + e); }
            _cutActions.Clear(); _controls.Clear(); _requestAt.Clear(); _gameReplica.Clear();
            _ledger = null; _gameConfig = null; _cash = _betMaximum = _opponentLoss = _stress = null;
            _propertyStage = _playerStage = _houseStage = null;
            _gameReady = _gameFailed = _gameHost = _hasEffectRound = false;
            _gameProbeAt = _gameBroadcastAt = _gameRetryAt = _inputAt = _effectsUntil = 0;
        }
    }
}
