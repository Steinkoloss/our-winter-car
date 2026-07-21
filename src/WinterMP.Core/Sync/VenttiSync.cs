using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Ventti (blackjack) table at the Peräjärvi Kunnalliskoti as host-owned world state
    /// (COVERAGE-ROADMAP 1.2). The card draws are per-client RNG, so each peer resolves a
    /// different hand — and Ventti stakes real money <b>and the Satsuma car</b>, so a
    /// per-client outcome desyncs both the wallet and car ownership.
    ///
    /// Same model as <see cref="GamblingSync"/>: the <b>host</b> deals — a guest's bet /
    /// hit / stand / car-wager press is relayed as a <see cref="GamblingIntent"/>
    /// (Kind=Ventti on the reply), the host forces the matching button state on its own
    /// FSM so the real draw + resolution + payout run host-side, and the resolved hand
    /// totals broadcast via <see cref="GamblingState"/> while money rides
    /// <see cref="WalletState"/> and any car transfer runs through the host's own
    /// GameManager (never a local guest ownership write).
    /// </summary>
    internal sealed class VenttiSync
    {
        private const string TablePath = "PERAJARVI/Kunnalliskoti/Functions/RoomVenttiPig/Ventti/Table";

        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 1f;
        private const float KeepAliveSeconds = 20f;
        private const float IntentCooldownSeconds = 0.4f;
        private const float IntentPlayerPoseMaxAgeSeconds = 2f;
        private const float IntentPlayerMaxDistance = 6f;

        // Button -> the state a press transitions into (forced on the host to run the draw/bet).
        private const string StateBetIncrease = "Check high bet?"; // Bet FSM INCREASE path
        private const string StateWagerCar = "Cars";               // Bet FSM SATSUMA path
        private const string StateHit = "Draw";                    // HitPlayer FSM USE path
        private const string StateStand = "State 1";               // Stand FSM USE path

        private uint _tableId;
        private Transform? _anchor;
        private bool _loggedFound;

        private PlayMakerFSM? _bet;        // GAME/Gamestuff/Bet :: Use
        private PlayMakerFSM? _hitPlayer;  // GAME/Gamestuff/HitPlayer :: Use
        private PlayMakerFSM? _stand;      // GAME/Gamestuff/Stand :: Use
        private PlayMakerFSM? _manager;    // GameManager :: Use

        private FsmFloat? _betValue;       // Bet :: Bet
        private FsmInt? _playerHand;       // HitPlayer :: Hand
        private FsmInt? _houseHand;        // HitHouse :: Hand (read via manager subtree)
        private FsmBool? _betCar;          // GameManager :: BetCar

        private bool _hostEntriesReady;
        private bool _hooksInstalled;
        private float _nextIntentAt;
        private ushort _outIntentSequence;

        private readonly Dictionary<byte, ushort> _lastIntentSequences = new Dictionary<byte, ushort>();
        private bool _built;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;

        // Change detection.
        private bool _hasLast;
        private byte _lastFlags, _lastPlayer, _lastHouse, _lastBet;

        public void Clear()
        {
            _anchor = null;
            _bet = _hitPlayer = _stand = _manager = null;
            _betValue = null; _playerHand = _houseHand = null; _betCar = null;
            _tableId = 0;
            _loggedFound = false;
            _hostEntriesReady = false;
            _hooksInstalled = false;
            _outIntentSequence = 0;
            _lastIntentSequences.Clear();
            _built = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _hasLast = false;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            EnsureBuilt();

            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                Locate();
            }

            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;

            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        public void ForceBroadcast()
        {
            _nextHostTickAt = 0f;
            _nextKeepAliveAt = 0f;
        }

        // ---- Guest: apply host state -----------------------------------------

        public void OnRemoteState(GamblingState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            if (message.Kind != GamblingState.KindVentti || message.MachineId != _tableId) return;

            EnsureBuilt();
            Locate();
            try
            {
                WriteFloat(_betValue, message.Bet);
                WriteInt(_playerHand, message.V1);
                WriteInt(_houseHand, message.V2);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("VenttiSync: apply failed: " + e.Message);
            }
        }

        // ---- Host: apply a guest's intent ------------------------------------

        public bool TryAcceptIntent(GamblingIntent intent)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;
            if (intent.MachineId != _tableId) return false;

            EnsureBuilt();
            Locate();

            if (_anchor == null
                || !TryGetActionTarget(intent.Action, out var fsm, out var stateName)
                || fsm == null
                || !IsGuestNear(session, intent.PlayerId, _anchor.position))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"VenttiSync: dropped invalid or distant intent action {intent.Action} from player {intent.PlayerId}.");
                return false;
            }

            if (_lastIntentSequences.TryGetValue(intent.PlayerId, out ushort previous))
            {
                ushort difference = (ushort)(intent.Sequence - previous);
                if (difference == 0 || difference > short.MaxValue)
                {
                    WinterMPPlugin.Log.LogWarning(
                        $"VenttiSync: dropped stale intent sequence {intent.Sequence} from player {intent.PlayerId}.");
                    return false;
                }
            }

            _lastIntentSequences[intent.PlayerId] = intent.Sequence;
            FsmHook.EnsureRemoteEntry(fsm, stateName);
            FsmHook.FireRemoteEntry(fsm, stateName);
            SyncEventLog.Record("ventti-intent", $"action {intent.Action} player {intent.PlayerId}");
            _nextHostTickAt = 0f;
            return true;
        }

        // ---- Host broadcast ---------------------------------------------------

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            if (_hitPlayer == null && _bet == null) return;

            byte flags = 0, player, house, bet;
            try
            {
                if (ReadBool(_betCar)) flags |= GamblingState.FlagActive;
                player = ClampByte(ReadInt(_playerHand));
                house = ClampByte(ReadInt(_houseHand));
                bet = ClampByte(ReadFloat(_betValue));
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("VenttiSync: read failed: " + e.Message);
                return;
            }

            bool changed = !_hasLast || _lastFlags != flags
                || _lastPlayer != player || _lastHouse != house || _lastBet != bet;
            if (!changed && !keepAlive) return;

            _hasLast = true;
            _lastFlags = flags; _lastPlayer = player; _lastHouse = house; _lastBet = bet;

            session.SendWorldMessage(
                new GamblingState
                {
                    MachineId = _tableId,
                    Kind = GamblingState.KindVentti,
                    Flags = flags,
                    Credit = bet,
                    Bet = bet,
                    V1 = player,
                    V2 = house,
                    V3 = 0,
                    Payout = 0,
                },
                Channel.ReliableOrdered);
        }

        // ---- Discovery --------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            _tableId = StableHash.Fnv1a32(TablePath);
        }

        private void Locate()
        {
            GameObject? container;
            try { container = GameObject.Find(TablePath); }
            catch { return; }
            if (container == null) return;

            var root = container.transform;
            _anchor = root;

            if (_bet == null) _bet = FindChildFsm(root, "GAME/Gamestuff/Bet", "Use");
            if (_hitPlayer == null) _hitPlayer = FindChildFsm(root, "GAME/Gamestuff/HitPlayer", "Use");
            if (_stand == null) _stand = FindChildFsm(root, "GAME/Gamestuff/Stand", "Use");
            if (_manager == null) _manager = FindChildFsm(root, "GameManager", "Use");

            if (_betValue == null && _bet != null) _betValue = _bet.FsmVariables.FindFsmFloat("Bet");
            if (_playerHand == null && _hitPlayer != null) _playerHand = _hitPlayer.FsmVariables.FindFsmInt("Hand");
            if (_houseHand == null)
            {
                var hitHouse = FindChildFsm(root, "GAME/Gamestuff/HitHouse", "Use");
                if (hitHouse != null) _houseHand = hitHouse.FsmVariables.FindFsmInt("Hand");
            }
            if (_betCar == null && _manager != null) _betCar = _manager.FsmVariables.FindFsmBool("BetCar");

            if (!_loggedFound && (_hitPlayer != null || _bet != null))
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo($"VenttiSync: located table (id {_tableId:X8}).");
            }

            var session = SessionManager.Instance;
            if (session == null) return;
            if (session.IsHost) EnsureHostEntries();
            else InstallGuestHooks();
        }

        private void EnsureHostEntries()
        {
            if (_hostEntriesReady) return;
            bool all = true;
            all &= EnsureEntry(_bet, StateBetIncrease);
            all &= EnsureEntry(_bet, StateWagerCar);
            all &= EnsureEntry(_hitPlayer, StateHit);
            all &= EnsureEntry(_stand, StateStand);
            _hostEntriesReady = all;
        }

        private static bool EnsureEntry(PlayMakerFSM? fsm, string stateName)
        {
            if (fsm == null) return false;
            try { return FsmHook.EnsureRemoteEntry(fsm, stateName); }
            catch { return false; }
        }

        private void InstallGuestHooks()
        {
            if (_hooksInstalled) return;
            bool all = true;
            all &= HookOnce(_bet, StateBetIncrease, GamblingIntent.ActionVenttiBet);
            all &= HookOnce(_bet, StateWagerCar, GamblingIntent.ActionVenttiWagerCar);
            all &= HookOnce(_hitPlayer, StateHit, GamblingIntent.ActionVenttiHit);
            all &= HookOnce(_stand, StateStand, GamblingIntent.ActionVenttiStand);
            _hooksInstalled = all;
        }

        private bool HookOnce(PlayMakerFSM? fsm, string stateName, byte action)
        {
            if (fsm == null) return false;
            var capturedAction = action;
            return FsmHook.OnStateEnter(fsm, stateName, () => EmitIntent(capturedAction));
        }

        private void EmitIntent(byte action)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.PlayerCount == 0) return;
            if (Time.unscaledTime < _nextIntentAt) return;
            _nextIntentAt = Time.unscaledTime + IntentCooldownSeconds;

            session.SendWorldMessage(
                new GamblingIntent
                {
                    MachineId = _tableId,
                    Action = action,
                    PlayerId = session.LocalPlayerId,
                    Sequence = ++_outIntentSequence,
                },
                Channel.ReliableOrdered);
            SyncEventLog.Record("ventti-intent-out", $"action {action}");
        }

        private bool TryGetActionTarget(byte action, out PlayMakerFSM? fsm, out string stateName)
        {
            switch (action)
            {
                case GamblingIntent.ActionVenttiBet: fsm = _bet; stateName = StateBetIncrease; return true;
                case GamblingIntent.ActionVenttiWagerCar: fsm = _bet; stateName = StateWagerCar; return true;
                case GamblingIntent.ActionVenttiHit: fsm = _hitPlayer; stateName = StateHit; return true;
                case GamblingIntent.ActionVenttiStand: fsm = _stand; stateName = StateStand; return true;
                default: fsm = null; stateName = string.Empty; return false;
            }
        }

        private static PlayMakerFSM? FindChildFsm(Transform root, string childPath, string fsmName)
        {
            try
            {
                var child = root.Find(childPath);
                if (child == null) return null;
                foreach (var fsm in child.GetComponents<PlayMakerFSM>())
                {
                    if (fsm != null && fsm.FsmName == fsmName) return fsm;
                }
                return null;
            }
            catch { return null; }
        }

        private static bool IsGuestNear(SessionManager session, byte playerId, Vector3 targetPosition)
        {
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f || now - player.LastTransformTime > IntentPlayerPoseMaxAgeSeconds)
                    return false;
                return (player.Position - targetPosition).sqrMagnitude
                    <= IntentPlayerMaxDistance * IntentPlayerMaxDistance;
            }
            return false;
        }

        private static bool ReadBool(FsmBool? v) => v != null && v.Value;
        private static int ReadInt(FsmInt? v) => v != null ? v.Value : 0;
        private static float ReadFloat(FsmFloat? v) => v != null ? v.Value : 0f;
        private static void WriteInt(FsmInt? v, int value) { if (v != null) v.Value = value; }
        private static void WriteFloat(FsmFloat? v, float value) { if (v != null) v.Value = value; }
        private static byte ClampByte(float value) => (byte)Mathf.Clamp(value, 0f, 255f);
    }
}
