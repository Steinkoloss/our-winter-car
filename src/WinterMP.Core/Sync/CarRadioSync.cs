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

        private sealed class Radio
        {
            public byte Id;
            public string ContainerPath = string.Empty;
            public bool LoggedFound;
            public FsmFloat? Channel;
            public FsmFloat? Volume;
            public bool HasLast;
            public byte LastChannel;
            public byte LastVolume;
            public bool Ready => Channel != null || Volume != null;
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
            foreach (var r in _radios) { r.Channel = r.Volume = null; r.HasLast = false; r.LoggedFound = false; }
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
                if (radio.Channel != null) radio.Channel.Value = message.Channel;
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
            bool changed = !radio.HasLast || radio.LastChannel != state.Channel || radio.LastVolume != state.Volume;
            if (!changed && !keepAlive) return;
            radio.HasLast = true;
            radio.LastChannel = state.Channel;
            radio.LastVolume = state.Volume;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private CarRadioState BuildState(Radio radio)
        {
            return new CarRadioState
            {
                Sequence = ++_outSequence,
                RadioId = radio.Id,
                Channel = (byte)Mathf.Clamp(radio.Channel != null ? radio.Channel.Value : 0f, 0f, 255f),
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

            foreach (var fsm in go.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm == null || fsm.FsmName != "Knob") continue;
                var channel = fsm.FsmVariables.FindFsmFloat("Channel");
                var volume = fsm.FsmVariables.FindFsmFloat("Volume");
                if (channel == null && volume == null) continue;
                radio.Channel = channel;
                radio.Volume = volume;
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
