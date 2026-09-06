using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared jail-sentence countdown (COVERAGE-ROADMAP 4.2). The arrest→jail flow is
    /// offender-local, so the day countdown runs ONLY on the jailed client's
    /// <c>JAIL/Functions :: Time</c> FSM — the other clients' FSMs idle at 0. Therefore the
    /// JAILED client owns the record (v83): while its local DaysLeft is positive it reports
    /// to the host, which adopts and relays; with nobody (or the host) jailed, the host
    /// broadcasts its own FSM. Non-jailed clients write DaysLeft for presentation; the
    /// jailed client ignores broadcasts about itself so the relay cannot stomp the
    /// countdown it is the source of.
    /// </summary>
    internal sealed class JailSync
    {
        private const string TimePath = "JAIL/Functions";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 20f;
        private const float GuestReportSeconds = 5f;
        // A guest record older than this is stale (client left / stopped reporting);
        // fall back to the host's own FSM rather than keepalive a frozen countdown.
        private const float GuestRecordTtlSeconds = 30f;

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
        private byte _lastJailed = JailState.NoPlayer;

        // Host: last adopted guest record (guest-offender case).
        private byte _recordPlayer = JailState.NoPlayer;
        private int _recordDays;
        private float _recordFreshAt = -999f;
        private readonly Dictionary<byte, ushort> _lastReportSequences = new Dictionary<byte, ushort>();

        // Guest: own-report bookkeeping.
        private int _lastReportedDays = -1;
        private float _nextReportAt;

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
            _lastJailed = JailState.NoPlayer;
            _recordPlayer = JailState.NoPlayer;
            _recordDays = 0;
            _recordFreshAt = -999f;
            _lastReportSequences.Clear();
            _lastReportedDays = -1;
            _nextReportAt = 0f;
        }

        /// <summary>Host: a player (re)joined — its report counter restarted; drop the stale latch.</summary>
        public void ForgetPlayer(byte playerId) => _lastReportSequences.Remove(playerId);

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }

            if (!session.IsHost)
            {
                if (Ready) GuestReportOwnSentence(session);
                return;
            }

            // The host relays a jailed GUEST's record even when its own jail FSM never
            // activated (JAIL/Functions wakes on arrest) — gating the broadcast on Ready
            // was exactly the path that silenced guest sentences.
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        public JailState? BuildSnapshot()
        {
            Locate();
            return BuildState();
        }

        // ---- Guest: own the countdown while jailed ---------------------------

        private void GuestReportOwnSentence(SessionManager session)
        {
            int days;
            try { days = _daysLeft != null ? _daysLeft.Value : 0; }
            catch { return; }

            bool changed = days != _lastReportedDays;
            bool keepAlive = days > 0 && Time.unscaledTime >= _nextReportAt;
            // One final 0-report on release so the host retires the record promptly.
            bool released = days <= 0 && _lastReportedDays > 0;
            if (!changed && !keepAlive && !released) return;
            if (days <= 0 && _lastReportedDays <= 0) { _lastReportedDays = days; return; }

            _lastReportedDays = days;
            _nextReportAt = Time.unscaledTime + GuestReportSeconds;

            session.SendWorldMessage(new JailState
            {
                Sequence = ++_outSequence,
                DaysLeft = Mathf.Max(0, days),
                Sentence = 0, // host recomputes from its own wanted record
                Flags = days > 0 ? JailState.FlagJailed : (byte)0,
                JailedPlayerId = session.LocalPlayerId,
            }, Channel.ReliableOrdered);
        }

        /// <summary>Host: a guest reports the countdown it is serving. Adopt + relay next tick.</summary>
        public void OnHostJailReport(JailState message)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;

            if (_lastReportSequences.TryGetValue(message.JailedPlayerId, out ushort previous))
            {
                ushort d = (ushort)(message.Sequence - previous);
                if (d == 0 || d > short.MaxValue) return;
            }
            _lastReportSequences[message.JailedPlayerId] = message.Sequence;

            if (message.DaysLeft > 0)
            {
                _recordPlayer = message.JailedPlayerId;
                _recordDays = message.DaysLeft;
                _recordFreshAt = Time.unscaledTime;
            }
            else if (_recordPlayer == message.JailedPlayerId)
            {
                // Served out — retire the guest record.
                _recordPlayer = JailState.NoPlayer;
                _recordDays = 0;
            }

            _nextHostTickAt = 0f; // relay promptly
        }

        // ---- Apply (both roles receive host broadcasts) ----------------------

        public void Apply(JailState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            // We ARE the jailed player — our FSM is the source; the relay must not stomp it.
            if (message.JailedPlayerId == session.LocalPlayerId) return;
            // We are ALSO serving a sentence (two players jailed at once): our own live
            // countdown wins locally; the record slot on the wire only carries one player.
            try { if (_daysLeft != null && _daysLeft.Value > 0) return; } catch { }

            try { if (_daysLeft != null) _daysLeft.Value = Mathf.Max(0, message.DaysLeft); }
            catch (System.Exception e) { WinterMPPlugin.Log.LogDebug("JailSync: apply failed: " + e.Message); }
        }

        // ---- Host broadcast --------------------------------------------------

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            bool changed = !_hasLast || _lastDays != state.DaysLeft || _lastFlags != state.Flags
                || _lastJailed != state.JailedPlayerId;
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastDays = state.DaysLeft;
            _lastFlags = state.Flags;
            _lastJailed = state.JailedPlayerId;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private JailState? BuildState()
        {
            bool guestRecordLive = _recordPlayer != JailState.NoPlayer
                && Time.unscaledTime - _recordFreshAt <= GuestRecordTtlSeconds;
            // A guest record stands on its own; the host's own FSM is only needed otherwise.
            if (!guestRecordLive && !Ready) return null;

            int days;
            byte jailed;
            if (guestRecordLive)
            {
                days = _recordDays;
                jailed = _recordPlayer;
            }
            else
            {
                days = _daysLeft!.Value;
                jailed = days > 0 ? (SessionManager.Instance?.LocalPlayerId ?? 0) : JailState.NoPlayer;
            }

            int sentence = 0;
            if (_wanted != null && _wanted.TryGetSentence(out int s, out _)) sentence = s;
            byte flags = days > 0 ? JailState.FlagJailed : (byte)0;
            return new JailState
            {
                Sequence = ++_outSequence,
                DaysLeft = days,
                Sentence = sentence,
                Flags = flags,
                JailedPlayerId = jailed,
            };
        }

        private void Locate()
        {
            if (Ready) return;
            // JAIL/Functions is INACTIVE until an arrest activates it, and GameObject.Find
            // cannot see inactive objects — scan all FSMs instead so a never-jailed client
            // still binds (its DaysLeft stays 0 until the game activates the jail).
            try
            {
                var fsms = ScenePath.ScanFsms();
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || fsm.FsmName != "Time") continue;
                    string path;
                    try { path = ScenePath.Of(fsm.transform); }
                    catch { continue; }
                    if (path != TimePath) continue;
                    _time = fsm;
                    _daysLeft = fsm.FsmVariables.FindFsmInt("DaysLeft");
                    break;
                }
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("JailSync: locate failed: " + e.Message);
            }
            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("JailSync: located jail timer.");
            }
        }
    }
}
