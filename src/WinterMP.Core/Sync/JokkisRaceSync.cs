using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// JOKKIS banger-race lifecycle as host-owned world state (COVERAGE-ROADMAP 5.3). The
    /// JOKKIS <c>DB/RaceTrigger :: Data</c> lap/time/checkpoint runs per-client (same structure
    /// as CORRIS). Rather than refactor the CORRIS-coupled <see cref="IceRaceSync"/>, JOKKIS is
    /// host-authoritative: the host reads the race record and broadcasts lap/time/checkpoint on
    /// change + join; guests apply so both see the same standings. A host-scalar-state
    /// broadcaster like <see cref="TaxiJobSync"/>.
    /// </summary>
    internal sealed class JokkisRaceSync
    {
        private const string RacePath = "JOKKIS/DB/RaceTrigger";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 1f;
        private const float KeepAliveSeconds = 20f;

        private PlayMakerFSM? _data;
        private FsmInt? _laps;
        private FsmFloat? _time;
        private FsmBool? _checkpoint1;
        private FsmBool? _checkpoint2;
        private bool _loggedFound;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private int _lastLaps;
        private byte _lastFlags;

        private bool Ready => _data != null && _laps != null;

        public void Clear()
        {
            _data = null;
            _laps = null; _time = null; _checkpoint1 = _checkpoint2 = null;
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

        public JokkisRaceState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(JokkisRaceState message)
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
                if (_laps != null) _laps.Value = message.Laps;
                if (_time != null) _time.Value = message.TimeCentiseconds / 100f;
                if (_checkpoint1 != null) _checkpoint1.Value = (message.Flags & JokkisRaceState.FlagCheckpoint1) != 0;
                if (_checkpoint2 != null) _checkpoint2.Value = (message.Flags & JokkisRaceState.FlagCheckpoint2) != 0;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("JokkisRaceSync: apply failed: " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            bool changed = !_hasLast || _lastLaps != state.Laps || _lastFlags != state.Flags;
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastLaps = state.Laps;
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private JokkisRaceState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            if (_checkpoint1 != null && _checkpoint1.Value) flags |= JokkisRaceState.FlagCheckpoint1;
            if (_checkpoint2 != null && _checkpoint2.Value) flags |= JokkisRaceState.FlagCheckpoint2;
            return new JokkisRaceState
            {
                Sequence = ++_outSequence,
                Laps = _laps!.Value,
                TimeCentiseconds = _time != null ? Mathf.RoundToInt(_time.Value * 100f) : 0,
                Flags = flags,
            };
        }

        private void Locate()
        {
            if (Ready) return;
            GameObject? go;
            try { go = GameObject.Find(RacePath); }
            catch { return; }
            if (go == null) return;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Data") continue;
                _data = fsm;
                var v = fsm.FsmVariables;
                _laps = v.FindFsmInt("Laps");
                _time = v.FindFsmFloat("Time");
                _checkpoint1 = v.FindFsmBool("Checkpoint1");
                _checkpoint2 = v.FindFsmBool("Checkpoint2");
                break;
            }
            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("JokkisRaceSync: located JOKKIS race.");
            }
        }
    }
}
