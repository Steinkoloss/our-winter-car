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
    internal sealed partial class PokerSync
    {
        private readonly Dictionary<string, PlayMakerFSM> _fsms = new Dictionary<string, PlayMakerFSM>();
        private readonly Dictionary<PlayMakerFSM, FsmSuppressor> _suppressed = new Dictionary<PlayMakerFSM, FsmSuppressor>();
        private readonly List<InputHook> _hooks = new List<InputHook>();
        private readonly Queue<PokerIntent> _pending = new Queue<PokerIntent>();
        private readonly Dictionary<byte, float> _requestAt = new Dictionary<byte, float>();
        private readonly System.Random _random = new System.Random();
        private PokerData? _config;
        private Transform? _root, _logic;
        private FsmFloat? _cash;
        private FsmInt? _credit, _wins, _bet;
        private PokerLedger? _ledger;
        private PokerState? _view;
        private bool _ready, _failed, _host, _hasRevision;
        private uint _id;
        private ushort _sequence;
        private float _probeAt, _retryAt, _broadcastAt, _inputAt;

        private sealed class InputHook
        {
            public FsmState State = null!;
            public FsmStateAction Hook = null!, Forward = null!;
            public bool ForwardEnabled;
        }

        public void Update(SessionManager session)
        {
            if (_failed || session.PlayerCount == 0) return;
            try
            {
                _host = session.IsHost;
                if (!_ready && Time.unscaledTime >= _probeAt)
                {
                    _probeAt = Time.unscaledTime + 2f;
                    Setup();
                }
                if (!_ready) return;
                if (_host && _ledger != null && (_ledger.Advance(Time.unscaledTime) || Time.unscaledTime >= _broadcastAt))
                    Broadcast(session);
                if (_pending.Count > 0 && Time.unscaledTime >= _retryAt)
                {
                    _retryAt = Time.unscaledTime + 1f;
                    if (_host) OnIntent(_pending.Peek());
                    else session.SendWorldMessage(_pending.Peek(), Channel.ReliableOrdered);
                }
                Present();
            }
            catch (Exception e) { Disable(e); }
        }

        private void Setup()
        {
            SyncCatalog.EnsureLoaded();
            _config = SyncCatalog.VideoPoker;
            if (_config == null) return;
            _id = StableHash.Fnv1a32(_config["path"]);
            string prefix = _config["path"] + "/" + _config["logicPath"];
            // Native dealing reparents cards between used/unused decks while we
            // wait for a hand to finish. Rebuild paths instead of retaining aliases.
            _fsms.Clear();
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                string path = ScenePath.Of(fsm.transform);
                if (path != prefix && !path.StartsWith(prefix + "/", StringComparison.Ordinal)) continue;
                _fsms[(path == prefix ? "." : path.Substring(prefix.Length + 1)) + "::" + fsm.FsmName] = fsm;
                if (_root != null) continue;
                var root = fsm.transform;
                while (root != null && ScenePath.Of(root) != _config["path"]) root = root.parent;
                _root = root;
            }
            if (_root == null) return;
            _logic = _root.Find(_config["logicPath"]);
            if (_logic == null) return;
            var creditFsm = FindFsm(_config["credit"], _config["creditFsm"]);
            var winsFsm = FindFsm(_config["winnings"], _config["winningsFsm"]);
            var betFsm = FindFsm(_config["bet"], _config["betFsm"]);
            if (creditFsm == null || winsFsm == null || betFsm == null) return;
            _credit = creditFsm.FsmVariables.FindFsmInt(_config["creditVariable"]);
            _wins = winsFsm.FsmVariables.FindFsmInt(_config["winningsVariable"]);
            _bet = betFsm.FsmVariables.FindFsmInt(_config["betVariable"]);
            var banking = SyncCatalog.Banking;
            if (banking != null) _cash = FsmVariables.GlobalVariables.FindFsmFloat(banking.CashGlobal);
            if (_credit == null || _wins == null || _bet == null || _cash == null) return;
            if (_host && !NativeBetweenHands()) return;

            // Init loads serialized action references but never enters a state (verified
            // against PlayMaker 1.7.7). Assets can bind without enabling an inactive UI.
            foreach (var fsm in _fsms.Values)
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            if (!BindAssets()) return;
            if (_host)
                _ledger = new PokerLedger(new PokerState
                {
                    MachineId = _id, Credit = _credit.Value, Winnings = _wins.Value,
                    Bet = (byte)(_bet.Value == 0 ? 1 : CheckedBet(_bet.Value)),
                }, _config.Payouts, _config.AchievementThreshold, _random.Next);

            CaptureScene();
            foreach (var fsm in _fsms.Values)
            {
                var suppressor = new FsmSuppressor();
                if (!suppressor.Suppress(fsm)) throw new InvalidOperationException("Cannot pause native poker resolver.");
                _suppressed.Add(fsm, suppressor);
            }
            for (byte i = 0; i < _config.Buttons.Length; i++) InstallInput(i);
            _ready = true;
            WinterMPPlugin.Log.LogInfo("VideoPoker: host ledger/native presentation ready.");
        }

        private bool NativeBetweenHands()
        {
            if (_config == null || _logic == null) return false;
            var menu = _logic.Find(_config["menu"]);
            if (menu != null && menu.gameObject.activeSelf) return true;
            var dealer = FindFsm(_config["game"], _config["dealerFsm"]);
            if (dealer == null) return false;
            if (Array.IndexOf(_config.IdleStates, dealer.ActiveStateName) >= 0) return true;
            return !dealer.gameObject.activeInHierarchy && string.IsNullOrEmpty(dealer.ActiveStateName);
        }

        private static int CheckedBet(int bet)
        {
            if (bet < 1 || bet > 5) throw new InvalidOperationException("Invalid native poker bet.");
            return bet;
        }

        private PlayMakerFSM? FindFsm(string path, string name)
        {
            return _fsms.TryGetValue(path + "::" + name, out var fsm) ? fsm : null;
        }

        private void InstallInput(byte action)
        {
            if (_config == null || _root == null) return;
            var button = _root.Find(_config["buttonPath"] + "/" + _config.Buttons[action]);
            PlayMakerFSM? fsm = null;
            if (button != null)
                foreach (var candidate in button.GetComponents<PlayMakerFSM>())
                    if (candidate.FsmName == _config["buttonFsm"]) fsm = candidate;
            if (fsm == null) throw new InvalidOperationException("Missing native poker control.");
            if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            var state = FsmHook.FindState(fsm, _config["inputState"]);
            if (state == null) throw new InvalidOperationException("Missing poker input state.");
            FsmStateAction? forward = null;
            foreach (var candidate in state.Actions)
                if (candidate.GetType().Name == _config["inputAction"])
                {
                    if (forward != null) throw new InvalidOperationException("Ambiguous poker forwarding action.");
                    forward = candidate;
                }
            if (forward == null) throw new InvalidOperationException("Missing poker forwarding action.");
            var hook = new FsmHookAction(() =>
            {
                try { Queue(action); }
                catch (Exception e) { Disable(e); }
            });
            _hooks.Add(new InputHook { State = state, Hook = hook, Forward = forward, ForwardEnabled = forward.Enabled });
            forward.Enabled = false;
            var actions = new List<FsmStateAction>(state.Actions);
            actions.Insert(0, hook); state.Actions = actions.ToArray();
        }

        private void Queue(byte action)
        {
            var session = SessionManager.Instance;
            if (!_ready || _failed || session == null || _view == null
                || (session.State != SessionState.Hosting && session.State != SessionState.Connected)
                || Time.unscaledTime < _inputAt || _pending.Count >= 16 || !Available(_view, action)) return;
            _inputAt = Time.unscaledTime + .15f;
            _pending.Enqueue(new PokerIntent
            {
                MachineId = _id, PlayerId = session.LocalPlayerId, Sequence = ++_sequence,
                Action = action, Round = _view.Round,
            });
            if (_pending.Count == 1) _retryAt = 0;
        }

        public void OnIntent(PokerIntent request)
        {
            var session = SessionManager.Instance;
            if (!_ready || _failed || session == null || !session.IsHost || _ledger == null || _cash == null || request.MachineId != _id) return;
            try
            {
                float now = Time.unscaledTime;
                if (_requestAt.TryGetValue(request.PlayerId, out float next) && now < next) return;
                _requestAt[request.PlayerId] = now + .1f;
                if (!_ledger.TryGetReceipt(request, out var result))
                {
                    if (_root == null || !GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)) return;
                    result = _ledger.Apply(request, _cash.Value, now, (position - _root.position).sqrMagnitude <= 36f, out float cash);
                    _cash.Value = cash;
                    SyncEventLog.Record("poker-action", "player " + request.PlayerId + " seq " + request.Sequence
                        + " action " + request.Action + " round " + request.Round + " result " + result.Result);
                }
                if (result.Result == PokerResult.Stale) return;
                Broadcast(session);
                var wallet = WorldSyncManager.Instance?.BuildWalletState();
                if (wallet != null) session.SendWorldMessage(wallet, Channel.ReliableOrdered);
                session.SendWorldMessage(result, Channel.ReliableOrdered);
                if (request.PlayerId == session.LocalPlayerId) OnResult(result);
            }
            catch (Exception e) { Disable(e); }
        }

        public void OnState(PokerState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !PokerLedger.IsValid(state)) return;
            var config = SyncCatalog.VideoPoker;
            if (config == null || state.MachineId != StableHash.Fnv1a32(config["path"])) return;
            if (_hasRevision && _view != null)
            {
                uint delta = state.Revision - _view.Revision;
                if (delta == 0 || delta > int.MaxValue) return;
            }
            _hasRevision = true; _view = state;
        }

        public void OnResult(PokerResult result)
        {
            var session = SessionManager.Instance;
            if (session == null || result.MachineId != _id || result.PlayerId != session.LocalPlayerId
                || _pending.Count == 0 || result.Sequence != _pending.Peek().Sequence || result.Result > PokerResult.Distant) return;
            _pending.Dequeue(); _retryAt = 0;
            if (result.Result == PokerResult.Accepted) Award(result.Achievements);
            else session.AddSystemChat(result.Result == PokerResult.Busy ? "* VideoPoker is in use by another player."
                : result.Result == PokerResult.Funds ? "* VideoPoker cannot accept that payment or bet. Check the available money."
                : "* VideoPoker action declined. The machine has been refreshed.");
        }

        private void Broadcast(SessionManager session)
        {
            if (_ledger == null) return;
            _view = _ledger.Snapshot(); _broadcastAt = Time.unscaledTime + 5f;
            session.SendWorldMessage(_view, Channel.ReliableOrdered);
        }
        public void ForceBroadcast() { _broadcastAt = 0; }
        public void ForgetPlayer(byte playerId) { _ledger?.ForgetPlayer(playerId); _requestAt.Remove(playerId); _broadcastAt = 0; }

        private void Disable(Exception error)
        {
            if (_failed) return;
            _failed = true;
            WinterMPPlugin.Log.LogError("VideoPoker sync disabled: " + error);
        }

        public void Clear()
        {
            bool settled = false;
            PokerState? remaining = null;
            _ready = false;
            try
            {
                if (_host && _ledger != null && _cash != null)
                {
                    bool refunded = _ledger.FinishSession(_cash.Value, out float cash);
                    if (refunded) _cash.Value = cash;
                    remaining = _ledger.Snapshot();
                    settled = true;
                    if (!refunded) WinterMPPlugin.Log.LogWarning("VideoPoker cash cannot receive a precise refund; keeping credit "
                        + remaining.Credit + " and winnings " + remaining.Winnings + " in the native machine.");
                }
                RestoreScene();
                if (settled)
                {
                    if (_credit != null) _credit.Value = 0;
                    if (_wins != null) _wins.Value = 0;
                    if (_bet != null) _bet.Value = 1;
                    foreach (var fsm in _suppressed.Keys)
                    {
                        bool restart = fsm.Fsm.RestartOnEnable;
                        fsm.Fsm.RestartOnEnable = true;
                        try { fsm.Fsm.Stop(); }
                        finally { fsm.Fsm.RestartOnEnable = restart; }
                    }
                }
            }
            catch (Exception e) { WinterMPPlugin.Log.LogError("VideoPoker cleanup: " + e); }
            foreach (var hook in _hooks)
            {
                try
                {
                    var actions = new List<FsmStateAction>(hook.State.Actions); actions.Remove(hook.Hook);
                    hook.State.Actions = actions.ToArray(); hook.Forward.Enabled = hook.ForwardEnabled;
                }
                catch (Exception e) { WinterMPPlugin.Log.LogDebug("Poker control cleanup: " + e.Message); }
            }
            foreach (var suppressor in _suppressed.Values) suppressor.Restore();
            if (settled) ResetNativeMenu(remaining);
            _hooks.Clear(); _suppressed.Clear(); _fsms.Clear(); _pending.Clear(); _requestAt.Clear(); _assets.Clear();
            _config = null; _root = _logic = null; _ledger = null; _view = _presented = null;
            _cash = null; _credit = _wins = _bet = null;
            _failed = _hasRevision = _host = false;
            _sequence = 0; _probeAt = _retryAt = _broadcastAt = _inputAt = 0;
        }
    }
}
