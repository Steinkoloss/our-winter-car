using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-initiated sleep / time-skip consent (PLAN.md §4.4). Guests must accept
    /// before the host proceeds when multiple players are connected.
    /// </summary>
    public sealed class SleepConsentManager : MonoBehaviour
    {
        public static SleepConsentManager? Instance { get; private set; }

        private byte _nextRequestId;
        private byte _activeRequestId;
        private bool _waitingForGuests;
        private PlayMakerFSM? _pendingSleepFsm;
        private readonly Dictionary<byte, bool> _responses = new Dictionary<byte, bool>();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Called when the host enters a bed/sleep FSM state.</summary>
        public void OnHostSleepAttempt(PlayMakerFSM? sleepFsm = null)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.PlayerCount == 0)
                return;

            if (_waitingForGuests) return;

            _pendingSleepFsm = sleepFsm;
            _activeRequestId = ++_nextRequestId;
            if (_activeRequestId == 0) _activeRequestId = ++_nextRequestId;

            _responses.Clear();
            _waitingForGuests = true;

            var request = new SleepConsentRequest
            {
                RequestId = _activeRequestId,
                InitiatorPlayerId = session.LocalPlayerId,
            };
            session.BroadcastProfileMessage(request);
            session.AddSystemChat("* Waiting for guests to accept sleep…");
            WinterMPPlugin.Log.LogInfo("SleepConsent: host requested guest approval.");
        }

        public void OnRemoteResponse(SleepConsentResponse response)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !_waitingForGuests) return;
            if (response.RequestId != _activeRequestId) return;

            _responses[response.PlayerId] = response.Accepted;

            string name = session.ResolvePlayerName(response.PlayerId);
            session.AddSystemChat(response.Accepted
                ? $"* {name} accepted sleep"
                : $"* {name} declined sleep");

            if (AllGuestsResponded(session))
                FinishRound(session, accepted: !AnyDeclined());
        }

        public void OnGuestRequest(SleepConsentRequest request)
        {
            UI.SleepConsentPrompt.Instance?.ShowRequest(request);
        }

        private bool AllGuestsResponded(SessionManager session)
        {
            foreach (var player in session.Players)
            {
                if (!_responses.ContainsKey(player.PlayerId))
                    return false;
            }

            return session.PlayerCount > 0;
        }

        private bool AnyDeclined()
        {
            foreach (var pair in _responses)
            {
                if (!pair.Value) return true;
            }

            return false;
        }

        private void FinishRound(SessionManager session, bool accepted)
        {
            _waitingForGuests = false;
            _responses.Clear();

            if (accepted)
            {
                session.AddSystemChat("* Everyone accepted — sleeping.");
                WinterMPPlugin.Log.LogInfo("SleepConsent: all guests accepted.");
            }
            else
            {
                session.AddSystemChat("* Sleep cancelled — a guest declined.");
                TryAbortSleep(_pendingSleepFsm);
                WinterMPPlugin.Log.LogWarning("SleepConsent: cancelled because a guest declined.");
            }

            _pendingSleepFsm = null;
        }

        private static void TryAbortSleep(PlayMakerFSM? fsm)
        {
            if (fsm == null) return;

            foreach (string evt in new[] { "STOP", "WAKE", "RESET", "FINISHED", "CANCEL" })
            {
                if (!HasFsmEvent(fsm, evt)) continue;

                try
                {
                    fsm.SendEvent(evt);
                    WinterMPPlugin.Log.LogInfo("SleepConsent: sent " + evt + " to abort sleep.");
                    return;
                }
                catch
                {
                    // FSM may be tearing down.
                }
            }
        }

        private static bool HasFsmEvent(PlayMakerFSM fsm, string eventName)
        {
            var events = fsm.Fsm != null ? fsm.Fsm.Events : null;
            if (events == null) return false;

            for (int i = 0; i < events.Length; i++)
            {
                if (events[i] != null && events[i].Name == eventName) return true;
            }

            return false;
        }
    }
}
