using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Incoming phone calls as host-decided events (COVERAGE-ROADMAP 6.3). Which topic rings
    /// and when is per-client RNG/time, so the phones ring for different calls. The <b>host</b>
    /// watches its ringing phone and broadcasts a <see cref="PhoneCallEvent"/> (the Topic) when
    /// a new call starts; guests set their local phone Topic and fire the matching ring event so
    /// both phones ring for the same call (audio/subtitle local). A discrete-event broadcaster.
    /// </summary>
    internal sealed class PhoneSync
    {
        // The home phone (HOMENEW) and the yard living-room phone.
        private static readonly string[] RingingPaths =
        {
            "HOMENEW/Functions/FunctionsDisable/Telephone/Logic/RingingNEW",
            "YARD/Building/LIVINGROOM/Telephone 1/Logic/RingingOLD",
        };

        private const float ProbeIntervalSeconds = 5f;
        private const float PollIntervalSeconds = 0.5f;

        private PlayMakerFSM? _ringing;
        private FsmString? _topic;
        private string _lastTopic = string.Empty;
        private ushort _lastCallId;
        private bool _loggedFound;
        private float _nextProbeAt;
        private float _nextPollAt;
        private ushort _outCallId;

        private bool Ready => _ringing != null && _topic != null;

        public void Clear()
        {
            _ringing = null;
            _topic = null;
            _lastTopic = string.Empty;
            _lastCallId = 0;
            _loggedFound = false;
            _nextProbeAt = _nextPollAt = 0f;
            _outCallId = 0;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }
            if (!session.IsHost || !Ready) return;
            if (Time.unscaledTime < _nextPollAt) return;
            _nextPollAt = Time.unscaledTime + PollIntervalSeconds;

            // Re-arm between calls: the ringing object is deactivated once a call ends
            // ("Disable phone"), and the game may leave Topic holding the old string — a
            // repeat call with the same topic would otherwise never re-edge.
            bool phoneActive;
            try { phoneActive = _ringing!.gameObject.activeInHierarchy; }
            catch { phoneActive = false; }
            if (!phoneActive) { _lastTopic = string.Empty; return; }

            string topic = _topic!.Value ?? string.Empty;
            if (string.IsNullOrEmpty(topic) || topic == _lastTopic) { _lastTopic = topic; return; }
            _lastTopic = topic;

            session.SendWorldMessage(new PhoneCallEvent { CallId = ++_outCallId, Topic = topic }, Channel.ReliableOrdered);
            SyncEventLog.Record("phone-call", topic);
        }

        public void Apply(PhoneCallEvent message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            // Ignore a call id we already rang (reliable-ordered dedup).
            ushort diff = (ushort)(message.CallId - _lastCallId);
            if (_lastCallId != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastCallId = message.CallId;

            try
            {
                // The ringing object is INACTIVE between calls, and a SendEvent at a
                // deactivated FSM is a silent no-op — activate it first so the FSM runs its
                // ring flow like the vanilla caller does. The topic transitions hang off the
                // post-ANSWER state, which reads the Topic var we set; the SendEvent below is
                // best-effort for builds where the topic events are global entries (the
                // stale dump cannot show global transitions — verify with R3.1b).
                if (_ringing != null && !_ringing.gameObject.activeInHierarchy)
                    _ringing.gameObject.SetActive(true);
                if (_topic != null) _topic.Value = message.Topic;
                if (!string.IsNullOrEmpty(message.Topic) && FsmHasEvent(_ringing!, message.Topic))
                    _ringing!.SendEvent(message.Topic);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("PhoneSync: apply failed: " + e.Message);
            }
        }

        private static bool FsmHasEvent(PlayMakerFSM fsm, string eventName)
        {
            try
            {
                var events = fsm.Fsm != null ? fsm.Fsm.Events : null;
                if (events == null) return false;
                foreach (var e in events) if (e != null && e.Name == eventName) return true;
            }
            catch { }
            return false;
        }

        private void Locate()
        {
            if (Ready) return;
            // The ringing objects are INACTIVE between calls and GameObject.Find cannot see
            // inactive objects — without the full scan a guest whose phone isn't currently
            // ringing never binds, and every relayed call event is dropped at !Ready.
            try
            {
                var fsms = ScenePath.ScanFsms();
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || fsm.FsmName != "Ring") continue;
                    string path;
                    try { path = ScenePath.Of(fsm.transform); }
                    catch { continue; }
                    bool known = false;
                    foreach (var candidate in RingingPaths)
                        if (path == candidate) { known = true; break; }
                    if (!known) continue;
                    var topic = fsm.FsmVariables.FindFsmString("Topic");
                    if (topic == null) continue;
                    _ringing = fsm;
                    _topic = topic;
                    break;
                }
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("PhoneSync: locate failed: " + e.Message);
            }
            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("PhoneSync: located ringing phone.");
            }
        }
    }
}
