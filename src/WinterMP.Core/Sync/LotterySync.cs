using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// National lottery draw (Lotto / Megaveto) as host-owned world state
    /// (COVERAGE-ROADMAP 1.4). The draw RNG runs per-client, so peers roll different
    /// winning numbers and a ticket that wins on one loses on the other; the pot diverges
    /// too. The <b>host</b> owns the draw: it reads the round / winning line / national pot
    /// from <c>Systems/Lottery :: Numbers</c> and broadcasts <see cref="LotteryDrawState"/>
    /// on change + keepalive + join; guests write it back so every ticket is judged against
    /// the same numbers. Ticket buy-in routes through the host purchase path (the ticket Pay
    /// buttons are catalogued buys), so it debits the shared wallet.
    ///
    /// Note: number selection stays local (each player fills their own ticket); a guest's
    /// automatic win credit still rides <see cref="WalletState"/> reconciliation.
    /// </summary>
    internal sealed class LotterySync
    {
        private const string LotteryPath = "Systems/Lottery";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 30f;

        private PlayMakerFSM? _numbers;
        private FsmInt? _round;
        private FsmInt? _pot;
        private FsmString? _winning;
        private FsmBool? _drawDone;
        private bool _loggedFound;

        private bool _built;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;

        private bool _hasLast;
        private int _lastRound;
        private int _lastPot;
        private byte _lastFlags;

        private bool Ready => _numbers != null && _round != null;

        public void Clear()
        {
            _numbers = null;
            _round = _pot = null;
            _winning = null;
            _drawDone = null;
            _loggedFound = false;
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

        // ---- Guest: apply host draw ------------------------------------------

        public void Apply(LotteryDrawState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            EnsureBuilt();
            Locate();
            if (!Ready) return;

            try
            {
                if (_round != null) _round.Value = message.Round;
                if (_pot != null) _pot.Value = message.NationalPot;
                if (_winning != null && !string.IsNullOrEmpty(message.WinningNumbers))
                    _winning.Value = message.WinningNumbers;
                // Suppress the guest's local re-draw: the host's draw is authoritative.
                if (_drawDone != null) _drawDone.Value = message.DrawDone;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("LotterySync: apply failed: " + e.Message);
            }
        }

        // ---- Host broadcast ---------------------------------------------------

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            Locate();
            if (!Ready) return;

            int round, pot;
            byte flags = 0;
            string winning;
            try
            {
                round = _round!.Value;
                pot = _pot != null ? _pot.Value : 0;
                winning = _winning != null ? (_winning.Value ?? string.Empty) : string.Empty;
                if (_drawDone != null && _drawDone.Value) flags |= LotteryDrawState.FlagDrawDone;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("LotterySync: read failed: " + e.Message);
                return;
            }

            bool changed = !_hasLast || _lastRound != round || _lastPot != pot || _lastFlags != flags;
            if (!changed && !keepAlive) return;

            _hasLast = true;
            _lastRound = round;
            _lastPot = pot;
            _lastFlags = flags;

            session.SendWorldMessage(
                new LotteryDrawState
                {
                    Round = round,
                    NationalPot = pot,
                    WinningNumbers = winning,
                    Flags = flags,
                },
                Channel.ReliableOrdered);
        }

        // ---- Discovery --------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
        }

        private void Locate()
        {
            if (Ready) return;

            GameObject? go;
            try { go = GameObject.Find(LotteryPath); }
            catch { return; }
            if (go == null) return;

            if (_numbers == null)
            {
                foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                {
                    if (fsm != null && fsm.FsmName == "Numbers") { _numbers = fsm; break; }
                }
            }
            if (_numbers == null) return;

            var v = _numbers.FsmVariables;
            if (_round == null) _round = v.FindFsmInt("CurrentRound");
            if (_pot == null) _pot = v.FindFsmInt("NationalPot");
            if (_winning == null) _winning = v.FindFsmString("UTNational7");
            if (_drawDone == null) _drawDone = v.FindFsmBool("DrawDone");

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("LotterySync: located Systems/Lottery.");
            }
        }
    }
}
