using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Police pursuit chase/siren state as host-owned world state (COVERAGE-ROADMAP 4.4). The
    /// cop cars stream their pose over <see cref="NpcTransform"/> (host-sim, guest AI frozen),
    /// but whether they are chasing and their sirens are decided per-client. The <b>host</b>
    /// owns the pursuit: it reads each POLICECAR's chase + siren and broadcasts the flags so
    /// the pursuit and sirens agree; guests apply the siren so the lights match. DUI arrest
    /// escalation flows through the shared wanted/jail records (4.1/4.2).
    /// </summary>
    internal sealed class PursuitSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 1f;
        private const float KeepAliveSeconds = 15f;

        private sealed class CopCar
        {
            public string NavPath = string.Empty;
            public string LogicPath = string.Empty;
            public PlayMakerFSM? Navigation;   // Navigation :: Chase (Sirens bool)
            public PlayMakerFSM? Passenger;    // CopPassenger :: Logic (Chase bool)
            public FsmBool? Sirens;
            public FsmBool? Chase;
        }

        private readonly CopCar[] _cars =
        {
            new CopCar { NavPath = "TRAFFIC/Police/Checkpoints/Cops/POLICECAR1/Navigation",
                         LogicPath = "TRAFFIC/Police/Checkpoints/Cops/POLICECAR1/CopPassenger" },
            new CopCar { NavPath = "TRAFFIC/Police/Checkpoints/Cops/POLICECAR2/Navigation",
                         LogicPath = "TRAFFIC/Police/Checkpoints/Cops/POLICECAR2/CopPassenger" },
        };

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private byte _lastFlags;

        public void Clear()
        {
            foreach (var c in _cars) { c.Navigation = c.Passenger = null; c.Sirens = c.Chase = null; }
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

        public PursuitState? BuildSnapshot()
        {
            Locate();
            return new PursuitState { Sequence = ++_outSequence, Flags = ReadFlags() };
        }

        public void Apply(PursuitState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            try
            {
                ApplySiren(_cars[0], (message.Flags & PursuitState.FlagCar1Siren) != 0);
                ApplySiren(_cars[1], (message.Flags & PursuitState.FlagCar2Siren) != 0);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("PursuitSync: apply failed: " + e.Message);
            }
        }

        private static void ApplySiren(CopCar car, bool on)
        {
            if (car.Sirens != null) car.Sirens.Value = on;
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            byte flags = ReadFlags();
            bool changed = !_hasLast || _lastFlags != flags;
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastFlags = flags;
            session.SendWorldMessage(new PursuitState { Sequence = ++_outSequence, Flags = flags }, Channel.ReliableOrdered);
        }

        private byte ReadFlags()
        {
            byte flags = 0;
            try
            {
                if (_cars[0].Chase != null && _cars[0].Chase!.Value) flags |= PursuitState.FlagCar1Chase;
                if (_cars[1].Chase != null && _cars[1].Chase!.Value) flags |= PursuitState.FlagCar2Chase;
                if (_cars[0].Sirens != null && _cars[0].Sirens!.Value) flags |= PursuitState.FlagCar1Siren;
                if (_cars[1].Sirens != null && _cars[1].Sirens!.Value) flags |= PursuitState.FlagCar2Siren;
            }
            catch { /* best-effort */ }
            return flags;
        }

        private void Locate()
        {
            foreach (var car in _cars)
            {
                if (car.Sirens != null && car.Chase != null) continue;
                if (car.Navigation == null)
                {
                    var nav = TryFind(car.NavPath, "Chase");
                    if (nav != null) { car.Navigation = nav; car.Sirens = nav.FsmVariables.FindFsmBool("Sirens"); }
                }
                if (car.Passenger == null)
                {
                    var logic = TryFind(car.LogicPath, "Logic");
                    if (logic != null) { car.Passenger = logic; car.Chase = logic.FsmVariables.FindFsmBool("Chase"); }
                }
            }
        }

        private static PlayMakerFSM? TryFind(string path, string fsmName)
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
