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
                if (_topic != null) _topic.Value = message.Topic;
                // Fire the matching ring event if the topic names one (JOKE1/FERNDALE/...).
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
            foreach (var path in RingingPaths)
            {
                GameObject? go;
                try { go = GameObject.Find(path); }
                catch { continue; }
                if (go == null) continue;
                foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.FsmName != "Ring") continue;
                    var topic = fsm.FsmVariables.FindFsmString("Topic");
                    if (topic == null) continue;
                    _ringing = fsm;
                    _topic = topic;
                    break;
                }
                if (Ready) break;
            }
            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("PhoneSync: located ringing phone.");
            }
        }
    }
}
