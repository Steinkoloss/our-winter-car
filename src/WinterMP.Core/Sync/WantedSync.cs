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
    /// adds to its authoritative counter — so a guest's crime is never erased by the broadcast.
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

        // Guest-side: local value already reported to the host per reportable counter.
        private readonly int[] _reportedUpTo = new int[ReportableCount];
        private ushort _outReportSequence;
        private readonly Dictionary<byte, ushort> _lastReportSequences = new Dictionary<byte, ushort>();

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
            for (int i = 0; i < _reportedUpTo.Length; i++) _reportedUpTo[i] = 0;
            _lastReportSequences.Clear();
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }
            if (!Ready) return;

            if (session.IsHost)
            {
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
            for (byte i = 0; i < ReportableCount; i++)
            {
                int local = _counters[i] != null ? _counters[i]!.Value : 0;
                int delta = local - _reportedUpTo[i];
                if (delta <= 0) continue;
                if (delta > MaxCrimeDelta) delta = MaxCrimeDelta;
                _reportedUpTo[i] = local;

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
                // Re-baseline what we've reported so future local rises re-diff against the host.
                for (int i = 0; i < ReportableCount; i++) _reportedUpTo[i] = values[i];
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
            Locate();
            if (!Ready || message.CrimeType >= ReportableCount || message.Delta <= 0 || message.Delta > MaxCrimeDelta)
                return false;

            if (_lastReportSequences.TryGetValue(message.PlayerId, out ushort previous))
            {
                ushort d = (ushort)(message.Sequence - previous);
                if (d == 0 || d > short.MaxValue) return false;
            }
            _lastReportSequences[message.PlayerId] = message.Sequence;

            var counter = _counters[message.CrimeType];
            if (counter == null) return false;
            try { counter.Value = Mathf.Max(0, counter.Value + message.Delta); }
            catch { return false; }

            SyncEventLog.Record("crime-accept", $"player {message.PlayerId} type {message.CrimeType} +{message.Delta}");
            _nextHostTickAt = 0f; // broadcast promptly
            return true;
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
