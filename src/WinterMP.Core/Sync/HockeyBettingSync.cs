using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Hockey betting round as host-owned world state (COVERAGE-ROADMAP R1.4, betting half).
    /// Each client's <c>Systems/HockeyGames</c> simulates the season with its own RNG, so the
    /// matchup, odds and the payout-deciding round result diverge. The <b>host</b> owns
    /// <c>Betting :: Logic</c> (+ Runkosarja's <c>KurPaWins</c>) and broadcasts on change +
    /// join; guests write the values back — their own re-sims get stomped. The standings
    /// TABLE stays per-client (ES2 array save keys, no FSM variable to carry — display-only
    /// residual). A host-scalar-state broadcaster like <see cref="WorldScalarsSync"/>.
    /// </summary>
    internal sealed class HockeyBettingSync
    {
        private const string BettingPath = "Systems/HockeyGames/Betting";
        private const string RunkosarjaPath = "Systems/HockeyGames/Runkosarja";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 3f;
        private const float KeepAliveSeconds = 30f;
        private const int MaxResultLength = 256;

        private FsmInt? _latestRound;
        private FsmInt? _gameIndex;
        private FsmInt? _team1Id;
        private FsmInt? _team2Id;
        private FsmFloat? _team1Odds;
        private FsmFloat? _team2Odds;
        private FsmFloat? _tieOdds;
        private FsmString? _result;
        private FsmBool? _kurpaWins;
        private bool _loggedFound;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private int _lastRound;
        private int _lastGame;
        private string _lastResult = string.Empty;
        private byte _lastFlags;

        private bool Ready => _latestRound != null && _team1Odds != null;

        public void Clear()
        {
            _latestRound = _gameIndex = _team1Id = _team2Id = null;
            _team1Odds = _team2Odds = _tieOdds = null;
            _result = null;
            _kurpaWins = null;
            _loggedFound = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = _lastRemoteSequence = 0;
            _hasLast = false;
            _lastResult = string.Empty;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }
            if (!session.IsHost || !Ready) return;

            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        public HockeyBettingState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(HockeyBettingState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            try
            {
                if (_latestRound != null) _latestRound.Value = message.LatestRound;
                if (_gameIndex != null) _gameIndex.Value = message.GameIndex;
                if (_team1Id != null) _team1Id.Value = message.Team1Id;
                if (_team2Id != null) _team2Id.Value = message.Team2Id;
                if (_team1Odds != null && message.Team1Odds > 0f) _team1Odds.Value = message.Team1Odds;
                if (_team2Odds != null && message.Team2Odds > 0f) _team2Odds.Value = message.Team2Odds;
                if (_tieOdds != null && message.TieOdds > 0f) _tieOdds.Value = message.TieOdds;
                if (_result != null && message.Result != null) _result.Value = message.Result;
                if (_kurpaWins != null) _kurpaWins.Value = message.KurPaWins;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("HockeyBettingSync: apply failed: " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            bool changed = !_hasLast || _lastRound != state.LatestRound || _lastGame != state.GameIndex
                || _lastFlags != state.Flags
                || !string.Equals(_lastResult, state.Result, System.StringComparison.Ordinal);
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastRound = state.LatestRound;
            _lastGame = state.GameIndex;
            _lastResult = state.Result;
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private HockeyBettingState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            if (_kurpaWins != null && _kurpaWins.Value) flags |= HockeyBettingState.FlagKurPaWins;
            string result = _result != null ? (_result.Value ?? string.Empty) : string.Empty;
            if (result.Length > MaxResultLength) result = result.Substring(0, MaxResultLength);
            return new HockeyBettingState
            {
                Sequence = ++_outSequence,
                LatestRound = _latestRound!.Value,
                GameIndex = _gameIndex != null ? _gameIndex.Value : 0,
                Team1Id = _team1Id != null ? _team1Id.Value : 0,
                Team2Id = _team2Id != null ? _team2Id.Value : 0,
                Team1Odds = _team1Odds != null ? _team1Odds.Value : 0f,
                Team2Odds = _team2Odds != null ? _team2Odds.Value : 0f,
                TieOdds = _tieOdds != null ? _tieOdds.Value : 0f,
                Result = result,
                Flags = flags,
            };
        }

        private void Locate()
        {
            if (Ready && _kurpaWins != null) return;

            if (_latestRound == null)
            {
                var betting = FindFsm(BettingPath, "Logic");
                if (betting != null)
                {
                    var v = betting.FsmVariables;
                    _latestRound = v.FindFsmInt("LatestRound");
                    _gameIndex = v.FindFsmInt("GameIndex");
                    _team1Id = v.FindFsmInt("Team1ID");
                    _team2Id = v.FindFsmInt("Team2ID");
                    _team1Odds = v.FindFsmFloat("Team1Odds");
                    _team2Odds = v.FindFsmFloat("Team2Odds");
                    _tieOdds = v.FindFsmFloat("TieOdds");
                    _result = v.FindFsmString("Result");
                }
            }

            if (_kurpaWins == null)
            {
                var runkosarja = FindFsm(RunkosarjaPath, "Data");
                if (runkosarja != null)
                    _kurpaWins = runkosarja.FsmVariables.FindFsmBool("KurPaWins");
            }

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("HockeyBettingSync: located hockey betting.");
            }
        }

        private static PlayMakerFSM? FindFsm(string path, string fsmName)
        {
            GameObject? go;
            try { go = GameObject.Find(path); }
            catch { return null; }
            if (go == null) return null;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm != null && fsm.FsmName == fsmName) return fsm;
            return null;
        }
    }
}
