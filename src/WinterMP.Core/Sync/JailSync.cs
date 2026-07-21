using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared jail-sentence countdown as host-owned world state (COVERAGE-ROADMAP 4.2). The
    /// arrest→jail flow is offender-local, so the day countdown runs only on the jailed
    /// client. The <b>host</b> owns <c>JAIL/Functions :: Time</c> (DaysLeft): it broadcasts the
    /// remaining days + sentence on change + join; guests apply the countdown so the jail
    /// agrees and everyone is released together. The confinement position rides the normal
    /// player transform stream. A host-scalar-state broadcaster like <see cref="WelfareSync"/>.
    /// </summary>
    internal sealed class JailSync
    {
        private const string TimePath = "JAIL/Functions";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 20f;

        private PlayMakerFSM? _time;
        private FsmInt? _daysLeft;
        private bool _loggedFound;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private int _lastDays;
        private byte _lastFlags;

        // Sentence comes from the (host-authoritative) PlayerWanted record.
        private WantedSync? _wanted;
        public void BindWanted(WantedSync wanted) => _wanted = wanted;

        private bool Ready => _time != null && _daysLeft != null;

        public void Clear()
        {
            _time = null;
            _daysLeft = null;
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

        public JailState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(JailState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            try { if (_daysLeft != null) _daysLeft.Value = Mathf.Max(0, message.DaysLeft); }
            catch (System.Exception e) { WinterMPPlugin.Log.LogDebug("JailSync: apply failed: " + e.Message); }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            bool changed = !_hasLast || _lastDays != state.DaysLeft || _lastFlags != state.Flags;
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastDays = state.DaysLeft;
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private JailState? BuildState()
        {
            if (!Ready) return null;
            int days = _daysLeft!.Value;
            int sentence = 0;
            if (_wanted != null && _wanted.TryGetSentence(out int s, out _)) sentence = s;
            byte flags = days > 0 ? JailState.FlagJailed : (byte)0;
            return new JailState { Sequence = ++_outSequence, DaysLeft = days, Sentence = sentence, Flags = flags };
        }

        private void Locate()
        {
            if (Ready) return;
            GameObject? go;
            try { go = GameObject.Find(TimePath); }
            catch { return; }
            if (go == null) return;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Time") continue;
                _time = fsm;
                _daysLeft = fsm.FsmVariables.FindFsmInt("DaysLeft");
                break;
            }
            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("JailSync: located jail timer.");
            }
        }
    }
}
