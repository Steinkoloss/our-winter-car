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
    /// the pursuit and sirens agree; guests apply <b>only</b> the siren so the lights match
    /// (mirroring the chase bool would make each guest raise its own fine — see Apply). DUI
    /// arrest escalation flows through the shared wanted/jail records (4.1/4.2).
    /// </summary>
    internal sealed class PursuitSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 1f;
        private const float KeepAliveSeconds = 15f;

        private sealed class CopCar
        {
            public string RootPath = string.Empty;     // TRAFFIC/.../POLICECARn
            public PlayMakerFSM? SirenFsm;     // Sirens<n> :: Blinking (Siren bool + NORMAL/OFF events)
            public PlayMakerFSM? Passenger;    // CopPassenger :: Logic (Chase bool)
            // The real siren bool lives on the car's Sirens<n>::Blinking. Navigation::Chase's
            // "Sirens" is a GameObjectVariable (a ref to that object), so FindFsmBool there
            // returns null — binding it was why this whole subsystem used to be a no-op.
            public FsmBool? Sirens;
            // CopPassenger::Logic "Chase" is HOST-READ ONLY — see the note on Apply().
            public FsmBool? Chase;
            public bool AppliedInit;
            public bool LastAppliedSiren;
        }

        private readonly CopCar[] _cars =
        {
            new CopCar { RootPath = "TRAFFIC/Police/Checkpoints/Cops/POLICECAR1" },
            new CopCar { RootPath = "TRAFFIC/Police/Checkpoints/Cops/POLICECAR2" },
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
            foreach (var c in _cars) { c.SirenFsm = c.Passenger = null; c.Sirens = c.Chase = null; c.AppliedInit = false; c.LastAppliedSiren = false; }
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

            // Only the siren is applied. FlagCarNChase is deliberately NOT mirrored onto
            // CopPassenger::Logic "Chase": that FSM walks Wait chase -> Distance player ->
            // Raise fine / Set wanted, so driving it on a guest would make every guest issue
            // its own fine and its own PoliceEvasion — which PoliceSync/WantedSync would then
            // relay back to the host as duplicates. The chase bits stay host-owned and are
            // broadcast for observability only; the guest sees the pursuit through the cop
            // car's NpcTransform pose stream plus these lights.
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
            if (car.Sirens == null) return;
            bool edge = !car.AppliedInit || car.LastAppliedSiren != on;
            car.AppliedInit = true;
            car.LastAppliedSiren = on;

            // Re-assert the bool on every message, not just on the edge: the 15 s keep-alive is
            // what heals a guest whose local FSM drifted, and edge-only writes would skip it.
            car.Sirens.Value = on;

            // The blink/audio loop is event-driven, so the bool alone won't restart a resting
            // FSM — drive the visual on the edge too. (Whether "NORMAL" is accepted from every
            // resting state is unverified: the catalog dump carries no global transitions. The
            // send is harmless if unhandled; a mismatch would show as lights that never start.)
            if (edge && car.SirenFsm != null)
            {
                try { car.SirenFsm.SendEvent(on ? "NORMAL" : "OFF"); }
                catch { /* best-effort */ }
            }
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
                // FsmBool refs are plain managed objects: they survive their FSM being
                // destroyed, so a re-instantiated cop car would wedge the binding forever.
                // Drop the vars whenever their owning component is gone (Unity fake-null).
                if (car.SirenFsm == null) car.Sirens = null;
                if (car.Passenger == null) car.Chase = null;
                if (car.Sirens != null && car.Chase != null) continue;

                if (car.SirenFsm == null)
                {
                    // The siren child is numbered per car (POLICECAR1/Sirens1,
                    // POLICECAR2/Sirens2), so match by prefix rather than a hardcoded
                    // index — a build renaming the child must not silently unbind us.
                    var siren = FindChildFsmByPrefix(car.RootPath, "Sirens", "Blinking");
                    if (siren != null) { car.SirenFsm = siren; car.Sirens = siren.FsmVariables.FindFsmBool("Siren"); }
                }
                if (car.Passenger == null)
                {
                    var logic = TryFind(car.RootPath + "/CopPassenger", "Logic");
                    if (logic != null) { car.Passenger = logic; car.Chase = logic.FsmVariables.FindFsmBool("Chase"); }
                }
            }
        }

        private static PlayMakerFSM? FindChildFsmByPrefix(string rootPath, string childPrefix, string fsmName)
        {
            GameObject? root;
            try { root = GameObject.Find(rootPath); }
            catch { return null; }
            if (root == null) return null;

            try
            {
                foreach (Transform child in root.transform)
                {
                    if (child == null || !child.name.StartsWith(childPrefix, System.StringComparison.Ordinal)) continue;
                    foreach (var fsm in child.GetComponents<PlayMakerFSM>())
                        if (fsm != null && fsm.FsmName == fsmName) return fsm;
                }
            }
            catch { /* best-effort */ }
            return null;
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
