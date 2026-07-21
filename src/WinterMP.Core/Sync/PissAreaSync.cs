using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Yard piss-stains as host-owned world state (COVERAGE-ROADMAP 8.3). <c>YARD/PissAreas</c>
    /// scales five persistent, world-visible snow stains; they are host-saved and
    /// shared-visible but run per-client. The <b>host</b> owns them: it reads the five scales
    /// and broadcasts them on change + join; guests apply. A host-scalar-state broadcaster.
    /// </summary>
    internal sealed class PissAreaSync
    {
        private const string PissPath = "YARD/PissAreas";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 3f;
        private const float KeepAliveSeconds = 30f;
        private const float ScaleQuant = 20f;

        private PlayMakerFSM? _logic;
        private readonly FsmFloat?[] _scales = new FsmFloat?[5];
        private bool _loggedFound;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private readonly byte[] _lastSent = new byte[5];

        private bool Ready => _logic != null && _scales[0] != null;

        public void Clear()
        {
            _logic = null;
            for (int i = 0; i < 5; i++) { _scales[i] = null; _lastSent[i] = 0; }
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

        public PissAreaState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(PissAreaState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            byte[] values = { message.Scale1, message.Scale2, message.Scale3, message.Scale4, message.Scale5 };
            try
            {
                for (int i = 0; i < 5; i++)
                    if (_scales[i] != null) _scales[i]!.Value = values[i] / ScaleQuant;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("PissAreaSync: apply failed: " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            byte[] values = { state.Scale1, state.Scale2, state.Scale3, state.Scale4, state.Scale5 };
            bool changed = !_hasLast;
            for (int i = 0; i < 5 && !changed; i++) changed = _lastSent[i] != values[i];
            if (!changed && !keepAlive) return;
            _hasLast = true;
            for (int i = 0; i < 5; i++) _lastSent[i] = values[i];
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private PissAreaState? BuildState()
        {
            if (!Ready) return null;
            byte Q(int i) => (byte)Mathf.Clamp((_scales[i] != null ? _scales[i]!.Value : 0f) * ScaleQuant, 0f, 255f);
            return new PissAreaState
            {
                Sequence = ++_outSequence,
                Scale1 = Q(0), Scale2 = Q(1), Scale3 = Q(2), Scale4 = Q(3), Scale5 = Q(4),
            };
        }

        private void Locate()
        {
            if (Ready) return;
            GameObject? go;
            try { go = GameObject.Find(PissPath); }
            catch { return; }
            if (go == null) return;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Logic") continue;
                _logic = fsm;
                for (int i = 0; i < 5; i++) _scales[i] = fsm.FsmVariables.FindFsmFloat("Scale" + (i + 1));
                break;
            }
            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("PissAreaSync: located yard piss areas.");
            }
        }
    }
}
