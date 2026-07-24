using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// In-car radio power/channel/volume as host-owned world state (COVERAGE-ROADMAP 8.4).
    /// The home stereo is host-synced; the CORRIS/SORBET radios are not. Track content is
    /// cosmetic; power/channel/volume is minor shared state. The <b>host</b> owns each car
    /// radio and broadcasts its channel + volume on change + join; guests apply. A
    /// multi-source host-scalar-state broadcaster like <see cref="UtilityBillSync"/>.
    /// </summary>
    internal sealed class CarRadioSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 20f;
        private const float VolumeScale = 100f;
        private const float TuneEpsilon = 0.001f;

        // Both values live on the stock radio's volume knob; the separate TunerPivot/Tuner
        // "Knob" carries only its own copy of Tune, and the CD player's CDplrVolume "Knob"
        // is a different device entirely. Bind the one FSM that has both.
        private const string KnobChildPath = "StockRadio0/ButtonsRadio/Volume";

        private sealed class Radio
        {
            public byte Id;
            public string ContainerPath = string.Empty;
            public bool LoggedFound;
            public FsmFloat? Tune;
            public FsmFloat? Volume;
            public bool HasLast;
            public float LastTune;
            public byte LastVolume;
            public bool Ready => Tune != null || Volume != null;
        }

        private readonly Radio[] _radios =
        {
            new Radio { Id = CarRadioState.RadioCorris, ContainerPath = "CORRIS/Assemblies/VINP_RadioCorris" },
            new Radio { Id = CarRadioState.RadioSorbet, ContainerPath = "SORBET(190-200psi)/Functions/VINP_RadioSorbet" },
        };

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;

        public void Clear()
        {
            foreach (var r in _radios) { r.Tune = r.Volume = null; r.HasLast = false; r.LoggedFound = false; }
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = _lastRemoteSequence = 0;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                foreach (var r in _radios) Locate(r);
            }

            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            foreach (var r in _radios) HostBroadcastIfChanged(session, r, keepAlive);
        }

        public System.Collections.Generic.IEnumerable<CarRadioState> BuildSnapshots()
        {
            foreach (var r in _radios)
            {
                Locate(r);
                if (r.Ready) yield return BuildState(r);
            }
        }

        public void Apply(CarRadioState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Radio? radio = null;
            foreach (var r in _radios) if (r.Id == message.RadioId) { radio = r; break; }
            if (radio == null) return;
            Locate(radio);
            if (!radio.Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            try
            {
                if (radio.Tune != null) radio.Tune.Value = message.Tune;
                if (radio.Volume != null) radio.Volume.Value = message.Volume / VolumeScale;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("CarRadioSync: apply failed for " + radio.ContainerPath + ": " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, Radio radio, bool keepAlive)
        {
            Locate(radio);
            if (!radio.Ready) return;
            var state = BuildState(radio);
            // Tune is a continuous knob value, so compare with a deadband — an exact float
            // compare would resend on every imperceptible drift.
            bool changed = !radio.HasLast || Mathf.Abs(radio.LastTune - state.Tune) > TuneEpsilon
                || radio.LastVolume != state.Volume;
            if (!changed && !keepAlive) return;
            radio.HasLast = true;
            radio.LastTune = state.Tune;
            radio.LastVolume = state.Volume;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private CarRadioState BuildState(Radio radio)
        {
            return new CarRadioState
            {
                Sequence = ++_outSequence,
                RadioId = radio.Id,
                Tune = radio.Tune != null ? radio.Tune.Value : 0f,
                Volume = (byte)Mathf.Clamp((radio.Volume != null ? radio.Volume.Value : 0f) * VolumeScale, 0f, 255f),
            };
        }

        private void Locate(Radio radio)
        {
            if (radio.Ready) return;
            GameObject? go;
            try { go = GameObject.Find(radio.ContainerPath); }
            catch { return; }
            if (go == null) return;

            // Target the knob by path instead of taking the first FSM named "Knob" in the
            // subtree: each radio has three of them (tuner, radio volume, CD volume) and
            // hierarchy order decided which one we bound.
            Transform? knob;
            try { knob = go.transform.Find(KnobChildPath); }
            catch { knob = null; }
            if (knob == null) return;

            foreach (var fsm in knob.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Knob") continue;
                // "Tune" is the station value. There is no float named "Channel" anywhere on
                // these FSMs — the old bind was silently null, so only volume ever synced.
                radio.Tune = fsm.FsmVariables.FindFsmFloat("Tune");
                radio.Volume = fsm.FsmVariables.FindFsmFloat("Volume");
                break;
            }

            if (!radio.LoggedFound && radio.Ready)
            {
                radio.LoggedFound = true;
                WinterMPPlugin.Log.LogInfo($"CarRadioSync: located radio '{radio.ContainerPath}'.");
            }
        }
    }
}
