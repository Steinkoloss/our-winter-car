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
    /// Shared flea-market sale table as host-owned world state (COVERAGE-ROADMAP 3.1). The
    /// <c>SaleTable :: Sell</c> FSM rolls a day-timed random sale per-client, so items sell
    /// on different days and the proceeds (→ shared wallet) diverge. The <b>host</b> owns the
    /// table: it runs the sale RNG and broadcasts the accumulated <c>MoneyTotal</c> + rent;
    /// guests suppress their local Sell FSM and apply the host's numbers. Renting the table
    /// and collecting the envelope are relayed as intents so the shared wallet moves once.
    /// Per-item placement/pricing stays local (dynamic picked-object refs; see PLAN §4.4).
    /// </summary>
    internal sealed class FleaSaleSync
    {
        private const string TablePath = "FleaMarket/SaleTable";
        private const string RentButtonPath = "FleaMarket/LOD/OpenHours/BuyTableRent";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 20f;
        private const float IntentCooldownSeconds = 0.5f;
        private const float IntentPlayerPoseMaxAgeSeconds = 2f;
        private const float IntentPlayerMaxDistance = 8f;

        private Transform? _anchor;
        private PlayMakerFSM? _logic;   // SaleTable :: Logic
        private PlayMakerFSM? _sell;    // SaleTable :: Sell (RNG — suppressed on guests)
        private PlayMakerFSM? _rentButton; // BuyTableRent :: Buy (the actual "rent a week" control)
        private FsmFloat? _moneyTotal;
        private FsmInt? _rentDays;
        private bool _loggedFound;
        private bool _sellSuppressed;
        private bool _rentHookState;

        private bool _built;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private float _nextIntentAt;
        private ushort _outIntentSequence;
        private readonly Dictionary<byte, ushort> _lastIntentSequences = new Dictionary<byte, ushort>();

        private bool _hasLast;
        private int _lastMoney;
        private ushort _lastRent;
        private byte _lastFlags;

        private bool Ready => _logic != null && _moneyTotal != null;

        public void Clear()
        {
            _anchor = null;
            _logic = _sell = _rentButton = null;
            _moneyTotal = null;
            _rentDays = null;
            _loggedFound = false;
            _sellSuppressed = false;
            _rentHookState = false;
            _built = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = _nextIntentAt = 0f;
            _outIntentSequence = 0;
            _lastIntentSequences.Clear();
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

            if (session.IsHost)
            {
                if (Time.unscaledTime < _nextHostTickAt) return;
                _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
                bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
                if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
                HostBroadcastIfChanged(session, keepAlive);
            }
            else
            {
                SuppressLocalSaleRng();
            }
        }

        public FleaSaleState? BuildSnapshot()
        {
            EnsureBuilt();
            Locate();
            if (!Ready) return null;
            return BuildState();
        }

        // ---- Guest apply -----------------------------------------------------

        public void Apply(FleaSaleState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            EnsureBuilt();
            Locate();
            if (!Ready) return;

            try
            {
                if (_moneyTotal != null) _moneyTotal.Value = message.MoneyTotal;
                if (_rentDays != null) _rentDays.Value = message.RentDays;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("FleaSaleSync: apply failed: " + e.Message);
            }
        }

        // ---- Host intent -----------------------------------------------------

        public bool TryAcceptIntent(FleaSaleIntent intent)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;
            EnsureBuilt();
            Locate();
            if (_logic == null || _anchor == null || !IsGuestNear(session, intent.PlayerId, _anchor.position))
            {
                WinterMPPlugin.Log.LogWarning($"FleaSaleSync: dropped invalid/distant intent action {intent.Action} from player {intent.PlayerId}.");
                return false;
            }
            if (_lastIntentSequences.TryGetValue(intent.PlayerId, out ushort previous))
            {
                ushort difference = (ushort)(intent.Sequence - previous);
                if (difference == 0 || difference > short.MaxValue) return false;
            }
            _lastIntentSequences[intent.PlayerId] = intent.Sequence;

            string eventName = intent.Action == FleaSaleIntent.ActionRent ? "RENT" : "MONEY";
            try { _logic.SendEvent(eventName); }
            catch (System.Exception e) { WinterMPPlugin.Log.LogDebug("FleaSaleSync: event failed: " + e.Message); }
            SyncEventLog.Record("flea-intent", $"action {intent.Action} player {intent.PlayerId}");
            _nextHostTickAt = 0f;
            return true;
        }

        // ---- Host broadcast --------------------------------------------------

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            Locate();
            if (!Ready) return;

            var state = BuildState();
            if (state == null) return;

            bool changed = !_hasLast || _lastMoney != Mathf.RoundToInt(state.MoneyTotal)
                || _lastRent != state.RentDays || _lastFlags != state.Flags;
            if (!changed && !keepAlive) return;

            _hasLast = true;
            _lastMoney = Mathf.RoundToInt(state.MoneyTotal);
            _lastRent = state.RentDays;
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private FleaSaleState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            int rent = _rentDays != null ? _rentDays.Value : 0;
            if (rent > 0) flags |= FleaSaleState.FlagRented;
            return new FleaSaleState
            {
                Sequence = ++_outIntentSequence,
                MoneyTotal = _moneyTotal!.Value,
                RentDays = (ushort)Mathf.Clamp(rent, 0, ushort.MaxValue),
                Flags = flags,
            };
        }

        // On a guest, keep the table's own random-sale FSM from resolving sales locally —
        // only the host may sell and credit the shared wallet.
        private void SuppressLocalSaleRng()
        {
            if (_sellSuppressed || _sell == null) return;
            try { _sell.enabled = false; _sellSuppressed = true; }
            catch { /* best-effort */ }
        }

        // ---- Discovery -------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
        }

        private void Locate()
        {
            if (Ready && _sell != null && _rentButton != null) return;

            GameObject? go;
            try { go = GameObject.Find(TablePath); }
            catch { return; }
            if (go == null) return;

            _anchor = go.transform;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null) continue;
                if (_logic == null && fsm.FsmName == "Logic") _logic = fsm;
                if (_sell == null && fsm.FsmName == "Sell") _sell = fsm;
            }
            if (_logic != null && _moneyTotal == null)
            {
                _moneyTotal = _logic.FsmVariables.FindFsmFloat("MoneyTotal");
                _rentDays = _logic.FsmVariables.FindFsmInt("RentDays");
            }
            if (_rentButton == null)
            {
                GameObject? rentGo;
                try { rentGo = GameObject.Find(RentButtonPath); }
                catch { rentGo = null; }
                if (rentGo != null)
                    foreach (var fsm in rentGo.GetComponents<PlayMakerFSM>())
                        if (fsm != null && fsm.FsmName == "Buy") { _rentButton = fsm; break; }
            }

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("FleaSaleSync: located flea sale table.");
            }

            var session = SessionManager.Instance;
            if (session != null && !session.IsHost) InstallGuestHooks();
        }

        // Guest: relay the rent-table press to the host.
        private void InstallGuestHooks()
        {
            if (_rentHookState || _rentButton == null) return;
            // A rent press adds a week via BuyTableRent :: Buy "Add"; relay it so the host charges
            // the shared wallet + advances RentDays. (The old hook was SaleTable::Logic "Set array
            // ID", which fires when any item is PLACED on the table — spurious rent, and the real
            // rent press never relayed.)
            if (FsmHook.OnStateEnter(_rentButton, "Add", () => EmitIntent(FleaSaleIntent.ActionRent)))
                _rentHookState = true;
        }

        private void EmitIntent(byte action)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.PlayerCount == 0) return;
            if (Time.unscaledTime < _nextIntentAt) return;
            _nextIntentAt = Time.unscaledTime + IntentCooldownSeconds;

            session.SendWorldMessage(new FleaSaleIntent
            {
                Action = action,
                PlayerId = session.LocalPlayerId,
                Sequence = ++_outIntentSequence,
            }, Channel.ReliableOrdered);
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
    }
}
