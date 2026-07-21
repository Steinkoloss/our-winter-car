using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Rally (Suvi-Sprint) results ledger + enroll + penalties as host-owned world state
    /// (COVERAGE-ROADMAP 5.1). <see cref="RallySync"/> syncs only stage timing; the
    /// <c>RACES/RALLY/ResultsWeekend</c> scoring/placement, enroll and <c>ParcFerme</c>
    /// penalty are unsynced. The <b>host</b> owns the board: it reads the results record + the
    /// Penalties FSM and broadcasts the standings + enroll + penalty on change + join; guests
    /// apply. Reward payout rides the existing host-gated race price triggers (v50). A
    /// host-scalar-state broadcaster like <see cref="TaxiJobSync"/>.
    /// </summary>
    internal sealed class RallyResultsSync
    {
        private const string ResultsPath = "RACES/RALLY/ResultsWeekend";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 25f;

        private PlayMakerFSM? _data;
        private PlayMakerFSM? _penalties;
        private FsmInt? _ss1;
        private FsmInt? _ss2;
        private FsmInt? _ss3;
        private FsmInt? _classLevel;
        private FsmFloat? _totalTime;
        private FsmFloat? _timePenalty;
        private FsmBool? _raceOver;
        private FsmBool? _winner;
        private FsmBool? _registered;
        private FsmBool? _secondDay;
        private bool _loggedFound;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private int _lastSum;
        private byte _lastFlags;

        private bool Ready => _data != null && _ss1 != null;

        public void Clear()
        {
            _data = _penalties = null;
            _ss1 = _ss2 = _ss3 = _classLevel = null;
            _totalTime = _timePenalty = null;
            _raceOver = _winner = _registered = _secondDay = null;
            _loggedFound = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = _lastRemoteSequence = 0;
            _hasLast = false;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }

            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        public RallyResultsState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(RallyResultsState message)
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
                if (_ss1 != null) _ss1.Value = message.TimeSS1;
                if (_ss2 != null) _ss2.Value = message.TimeSS2;
                if (_ss3 != null) _ss3.Value = message.TimeSS3;
                if (_classLevel != null) _classLevel.Value = message.PlayerClassLevel;
                if (_totalTime != null) _totalTime.Value = message.PlayerTimeTotal;
                if (_timePenalty != null) _timePenalty.Value = message.TimePenalty;
                if (_raceOver != null) _raceOver.Value = (message.Flags & RallyResultsState.FlagRaceOver) != 0;
                if (_winner != null) _winner.Value = (message.Flags & RallyResultsState.FlagWinner) != 0;
                if (_registered != null) _registered.Value = (message.Flags & RallyResultsState.FlagRegistered) != 0;
                if (_secondDay != null) _secondDay.Value = (message.Flags & RallyResultsState.FlagSecondDay) != 0;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("RallyResultsSync: apply failed: " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            int sum = state.TimeSS1 + state.TimeSS2 + state.TimeSS3;
            bool changed = !_hasLast || _lastSum != sum || _lastFlags != state.Flags;
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastSum = sum;
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private RallyResultsState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            if (_raceOver != null && _raceOver.Value) flags |= RallyResultsState.FlagRaceOver;
            if (_winner != null && _winner.Value) flags |= RallyResultsState.FlagWinner;
            if (_registered != null && _registered.Value) flags |= RallyResultsState.FlagRegistered;
            if (_secondDay != null && _secondDay.Value) flags |= RallyResultsState.FlagSecondDay;
            return new RallyResultsState
            {
                Sequence = ++_outSequence,
                TimeSS1 = _ss1!.Value,
                TimeSS2 = _ss2 != null ? _ss2.Value : 0,
                TimeSS3 = _ss3 != null ? _ss3.Value : 0,
                PlayerTimeTotal = _totalTime != null ? _totalTime.Value : 0f,
                PlayerClassLevel = _classLevel != null ? _classLevel.Value : 0,
                TimePenalty = _timePenalty != null ? _timePenalty.Value : 0f,
                Flags = flags,
            };
        }

        private void Locate()
        {
            if (Ready && _penalties != null) return;

            GameObject? go;
            try { go = GameObject.Find(ResultsPath); }
            catch { return; }
            if (go == null) return;

            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null) continue;
                if (_data == null && fsm.FsmName == "Data") _data = fsm;
                if (_penalties == null && fsm.FsmName == "Penalties") _penalties = fsm;
            }
            if (_data != null && _ss1 == null)
            {
                var v = _data.FsmVariables;
                _ss1 = v.FindFsmInt("TimeSS1");
                _ss2 = v.FindFsmInt("TimeSS2");
                _ss3 = v.FindFsmInt("TimeSS3");
                _classLevel = v.FindFsmInt("PlayerClassLevel");
                _totalTime = v.FindFsmFloat("PlayerTimeTotal");
                _raceOver = v.FindFsmBool("RaceOver");
                _winner = v.FindFsmBool("Winner");
                _registered = v.FindFsmBool("Registered");
                _secondDay = v.FindFsmBool("SecondDay");
            }
            if (_penalties != null && _timePenalty == null)
                _timePenalty = _penalties.FsmVariables.FindFsmFloat("OverallTimePenalty");

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("RallyResultsSync: located rally results.");
            }
        }
    }
}
