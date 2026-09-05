using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared wanted level as a host-owned record (COVERAGE-ROADMAP 4.1). The
    /// <c>Systems/PlayerWanted :: Activate</c> crime counters accrue per-client, so the group
    /// disagrees on how wanted it is. The <b>host</b> owns the group wanted level: it
    /// broadcasts the counters + sentence on change + join; a guest whose local counter rises
    /// (it committed a crime) reports the delta via <see cref="CrimeReport"/>, which the host
    /// adds to its authoritative counter. Guest evidence lives in a pending-delta accumulator
    /// that the host broadcast cannot stomp (the synced counter is both crime source and sync
    /// target, so a naive "report the counter" design erased unaccepted crimes on the next
    /// broadcast); the host queues reports that arrive before its FSM binds, so the reliable
    /// channel makes delivery exact-once without a resend/ack loop.
    /// Foundation for arrest/jail (4.2) and pursuit (4.4).
    /// </summary>
    internal sealed class WantedSync
    {
        private const string WantedPath = "Systems/PlayerWanted";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 30f;
        private const int MaxCrimeDelta = 32; // reject absurd guest reports
        private const int ReportableCount = 5; // manslaughter..daysFines

        private PlayMakerFSM? _fsm;
        // 0 manslaughter, 1 attempted, 2 evasion, 3 fatality, 4 daysFines, 5 sentence, 6 daysInJail
        private readonly FsmInt?[] _counters = new FsmInt?[7];
        private FsmBool? _cousin;
        private bool _loggedFound;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private readonly int[] _lastSent = new int[7];
        private byte _lastFlags;

        // Guest-side crime evidence, decoupled from the synced counter: _pendingDelta is what
        // we still owe the host, _lastObservedLocal is the counter value we last diffed
        // against. Apply() re-baselines the observation but never touches the pending delta,
        // so a host broadcast can no longer erase an unreported crime.
        private readonly int[] _pendingDelta = new int[ReportableCount];
        private readonly int[] _lastObservedLocal = new int[ReportableCount];
        private bool _observedSeeded;
        private ushort _outReportSequence;
        private readonly Dictionary<byte, ushort> _lastReportSequences = new Dictionary<byte, ushort>();
        // Host-side: reports that arrived before the PlayerWanted FSM bound (session start)
        // are parked, not rejected — the sender does not re-send (reliable channel).
        private const int MaxQueuedReports = 64;
        private readonly List<CrimeReport> _queuedReports = new List<CrimeReport>();

        private static readonly string[] CounterVars =
        {
            "ManSlaughter", "AttemptedManslaughter", "PoliceEvasion", "TrafficFatality",
            "DaysFines", "Sentence", "DaysInJail",
        };

        private bool Ready => _fsm != null && _counters[0] != null;

        public void Clear()
        {
            _fsm = null;
            for (int i = 0; i < _counters.Length; i++) _counters[i] = null;
            _cousin = null;
            _loggedFound = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = _lastRemoteSequence = _outReportSequence = 0;
            _hasLast = false;
            _lastFlags = 0;
            for (int i = 0; i < _lastSent.Length; i++) _lastSent[i] = 0;
            for (int i = 0; i < _pendingDelta.Length; i++) _pendingDelta[i] = 0;
            for (int i = 0; i < _lastObservedLocal.Length; i++) _lastObservedLocal[i] = 0;
            _observedSeeded = false;
            _lastReportSequences.Clear();
            _queuedReports.Clear();
        }

        /// <summary>Host: a player (re)joined — its report counter restarted; drop the stale latch.</summary>
        public void ForgetPlayer(byte playerId) => _lastReportSequences.Remove(playerId);

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }
            if (!Ready) return;

            if (session.IsHost)
            {
                DrainQueuedReports();
                if (Time.unscaledTime < _nextHostTickAt) return;
                _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
                bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
                if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
                HostBroadcastIfChanged(session, keepAlive);
            }
            else
            {
                ReportLocalCrimes(session);
            }
        }

        public WantedState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        // ---- Guest: report local crime deltas --------------------------------

        private void ReportLocalCrimes(SessionManager session)
        {
            // Accrue evidence: only a RISE of the local counter is a crime we committed.
            // The first observation seeds the baseline instead — a save that already holds
            // counters must not be replayed to the host as fresh crimes.
            for (int i = 0; i < ReportableCount; i++)
            {
                int local = _counters[i] != null ? _counters[i]!.Value : 0;
                if (_observedSeeded)
                {
                    int rise = local - _lastObservedLocal[i];
                    if (rise > 0) _pendingDelta[i] += rise;
                }
                _lastObservedLocal[i] = local;
            }
            if (!_observedSeeded) { _observedSeeded = true; return; }

            for (byte i = 0; i < ReportableCount; i++)
            {
                if (_pendingDelta[i] <= 0) continue;
                int delta = Mathf.Min(_pendingDelta[i], MaxCrimeDelta);
                _pendingDelta[i] -= delta;

                session.SendWorldMessage(new CrimeReport
                {
                    PlayerId = session.LocalPlayerId,
                    Sequence = ++_outReportSequence,
                    CrimeType = i,
                    Delta = delta,
                }, Channel.ReliableOrdered);
                SyncEventLog.Record("crime-report", $"type {i} delta {delta}");
            }
        }

        // ---- Guest apply -----------------------------------------------------

        public void Apply(WantedState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            int[] values =
            {
                message.Manslaughter, message.AttemptedManslaughter, message.PoliceEvasion,
                message.TrafficFatality, message.DaysFines, message.Sentence, message.DaysInJail,
            };
            try
            {
                for (int i = 0; i < _counters.Length; i++)
                    if (_counters[i] != null) _counters[i]!.Value = Mathf.Max(0, values[i]);
                if (_cousin != null) _cousin.Value = (message.Flags & WantedState.FlagCousin) != 0;
                // The stomp itself must not read as a local change — but pending (unreported)
                // deltas stay: they are evidence the host has not incorporated yet.
                for (int i = 0; i < ReportableCount; i++) _lastObservedLocal[i] = values[i];
                _observedSeeded = true;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("WantedSync: apply failed: " + e.Message);
            }
        }

        // ---- Host: accept a guest crime report -------------------------------

        public bool TryAcceptCrimeReport(CrimeReport message)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;
            if (message.CrimeType >= ReportableCount || message.Delta <= 0 || message.Delta > MaxCrimeDelta)
                return false;

            if (_lastReportSequences.TryGetValue(message.PlayerId, out ushort previous))
            {
                ushort d = (ushort)(message.Sequence - previous);
                if (d == 0 || d > short.MaxValue) return false;
            }
            _lastReportSequences[message.PlayerId] = message.Sequence;

            Locate();
            if (!TryApplyReport(message))
            {
                // Not appliable yet (FSM unbound / tearing down). The guest already spent
                // this delta and will not re-send — park it instead of dropping the crime.
                if (_queuedReports.Count < MaxQueuedReports) _queuedReports.Add(message);
                else WinterMPPlugin.Log.LogWarning("WantedSync: crime-report queue full, dropping report.");
            }

            return true;
        }

        private bool TryApplyReport(CrimeReport message)
        {
            if (!Ready) return false;
            var counter = _counters[message.CrimeType];
            if (counter == null) return false;
            try { counter.Value = Mathf.Max(0, counter.Value + message.Delta); }
            catch { return false; }

            SyncEventLog.Record("crime-accept", $"player {message.PlayerId} type {message.CrimeType} +{message.Delta}");
            _nextHostTickAt = 0f; // broadcast promptly
            return true;
        }

        private void DrainQueuedReports()
        {
            if (_queuedReports.Count == 0 || !Ready) return;
            for (int i = 0; i < _queuedReports.Count; i++)
            {
                if (!TryApplyReport(_queuedReports[i]))
                {
                    // Still not appliable — keep the tail for the next tick.
                    _queuedReports.RemoveRange(0, i);
                    return;
                }
            }
            _queuedReports.Clear();
        }

        // ---- Host broadcast --------------------------------------------------

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            int[] values =
            {
                state.Manslaughter, state.AttemptedManslaughter, state.PoliceEvasion,
                state.TrafficFatality, state.DaysFines, state.Sentence, state.DaysInJail,
            };
            bool changed = !_hasLast || _lastFlags != state.Flags;
            for (int i = 0; i < values.Length && !changed; i++) changed = _lastSent[i] != values[i];
            if (!changed && !keepAlive) return;

            _hasLast = true;
            _lastFlags = state.Flags;
            for (int i = 0; i < values.Length; i++) _lastSent[i] = values[i];
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private WantedState? BuildState()
        {
            if (!Ready) return null;
            int R(int i) => _counters[i] != null ? _counters[i]!.Value : 0;
            byte flags = 0;
            if (_cousin != null && _cousin.Value) flags |= WantedState.FlagCousin;
            return new WantedState
            {
                Sequence = ++_outSequence,
                Manslaughter = R(0), AttemptedManslaughter = R(1), PoliceEvasion = R(2),
                TrafficFatality = R(3), DaysFines = R(4), Sentence = R(5), DaysInJail = R(6),
                Flags = flags,
            };
        }

        // ---- expose current sentence/jail for the jail subsystem (4.2) -------

        public bool TryGetSentence(out int sentence, out int daysInJail)
        {
            sentence = _counters[5] != null ? _counters[5]!.Value : 0;
            daysInJail = _counters[6] != null ? _counters[6]!.Value : 0;
            return Ready;
        }

        private void Locate()
        {
            if (Ready) return;
            GameObject? go;
            try { go = GameObject.Find(WantedPath); }
            catch { return; }
            if (go == null) return;

            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Activate") continue;
                _fsm = fsm;
                for (int i = 0; i < CounterVars.Length; i++)
                    _counters[i] = fsm.FsmVariables.FindFsmInt(CounterVars[i]);
                _cousin = fsm.FsmVariables.FindFsmBool("Cousin");
                break;
            }

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("WantedSync: located PlayerWanted.");
            }
        }
    }
}
