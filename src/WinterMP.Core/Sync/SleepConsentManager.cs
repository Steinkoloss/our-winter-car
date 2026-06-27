using System.Collections.Generic;
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
        private const float ConsentTimeoutSeconds = 90f;

        public static SleepConsentManager? Instance { get; private set; }

        private byte _nextRequestId;
        private byte _activeRequestId;
        private bool _waitingForGuests;
        private bool _consentGranted;
        private bool _awaitingPostSleepSync;
        private PlayMakerFSM? _pendingSleepFsm;
        private string? _pendingSleepState;
        private float _consentDeadlineAt;
        private readonly Dictionary<byte, bool> _responses = new Dictionary<byte, bool>();
        // Guest ids the consent request was actually sent to. Frozen at request time so a guest
        // that joins mid-round is not required to respond (it never got the prompt) — otherwise the
        // round stalls until the 90s timeout. Departed guests are skipped via the live-roster check.
        private readonly List<byte> _awaitedPlayerIds = new List<byte>();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Called when the host enters the bed confirm FSM state.</summary>
        public void OnHostSleepAttempt(PlayMakerFSM? sleepFsm = null, string? sleepState = null)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.PlayerCount == 0)
                return;

            if (_waitingForGuests) return;

            _pendingSleepFsm = sleepFsm;
            _pendingSleepState = sleepState;
            _consentGranted = false;
            _activeRequestId = ++_nextRequestId;
            if (_activeRequestId == 0) _activeRequestId = ++_nextRequestId;

            _responses.Clear();
            _awaitedPlayerIds.Clear();
            foreach (var player in session.Players)
                _awaitedPlayerIds.Add(player.PlayerId);
            _waitingForGuests = true;
            _consentDeadlineAt = Time.unscaledTime + ConsentTimeoutSeconds;

            var request = new SleepConsentRequest
            {
                RequestId = _activeRequestId,
                InitiatorPlayerId = session.LocalPlayerId,
            };
            session.BroadcastProfileMessage(request);
            session.AddSystemChat("* Waiting for guests to accept sleep…");
            WinterMPPlugin.Log.LogInfo("SleepConsent: host requested guest approval.");
        }

        /// <summary>
        /// Gate the transition into <c>Get positions</c> — rollback if the host clicked
        /// through before every guest accepted.
        /// </summary>
        public void OnHostReachedGetPositions(PlayMakerFSM fsm)
        {
            if (!_waitingForGuests && _consentGranted) return;

            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.PlayerCount == 0)
                return;

            if (_consentGranted) return;

            RollbackToConfirm(fsm);
            session.AddSystemChat("* Waiting for guest sleep consent before continuing.");
            WinterMPPlugin.Log.LogInfo("SleepConsent: held host at Get positions — guests pending.");
        }

        /// <summary>Push clock sync once the host sleep FSM reaches Calc rates.</summary>
        public void OnHostSleepCompleted()
        {
            if (!_awaitingPostSleepSync) return;

            _awaitingPostSleepSync = false;
            _consentGranted = false;
            WorldSyncManager.Instance?.BroadcastTimeSyncNow();
            WinterMPPlugin.Log.LogInfo("SleepConsent: post-sleep TimeSync broadcast.");
        }

        private void Update()
        {
            if (!_waitingForGuests) return;

            try
            {
                var session = SessionManager.Instance;
                if (session == null || !session.IsHost || session.PlayerCount == 0)
                {
                    CancelWaiting();
                    return;
                }

                if (Time.unscaledTime < _consentDeadlineAt) return;

                session.AddSystemChat("* Sleep cancelled — timed out waiting for guests.");
                WinterMPPlugin.Log.LogWarning("SleepConsent: timed out waiting for guest responses.");
                FinishRound(session, accepted: false, timedOut: true);
            }
            catch (System.Exception e)
            {
                // Crash containment: a fault here must not escape into Unity's loop; drop the round.
                WinterMPPlugin.Log.LogError("SleepConsent Update failed: " + e);
                CancelWaiting();
            }
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
                FinishRound(session, accepted: !AnyDeclined(), timedOut: false);
        }

        public void OnGuestRequest(SleepConsentRequest request)
        {
            UI.SleepConsentPrompt.Instance?.ShowRequest(request);
        }

        public void OnGuestResult(SleepConsentResult result)
        {
            UI.SleepConsentPrompt.Instance?.DismissRequest(result.RequestId);

            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            if (!result.Accepted)
            {
                session.AddSystemChat("* Host sleep was cancelled.");
                return;
            }

            session.AddSystemChat("* Everyone accepted — time will advance.");
            PlayerSyncManager.Instance?.NeedsSync.ApplyRestedFromSleep();
        }

        private bool AllGuestsResponded(SessionManager session)
        {
            // Only require responses from guests present when the request was sent (frozen set),
            // and skip any that have since disconnected so a leaver cannot stall the round.
            int awaited = 0;
            for (int i = 0; i < _awaitedPlayerIds.Count; i++)
            {
                byte id = _awaitedPlayerIds[i];
                if (!IsPlayerPresent(session, id)) continue;
                awaited++;
                if (!_responses.ContainsKey(id))
                    return false;
            }

            return awaited > 0;
        }

        private static bool IsPlayerPresent(SessionManager session, byte playerId)
        {
            foreach (var player in session.Players)
            {
                if (player.PlayerId == playerId) return true;
            }

            return false;
        }

        private bool AnyDeclined()
        {
            foreach (var pair in _responses)
            {
                if (!pair.Value) return true;
            }

            return false;
        }

        private void FinishRound(SessionManager session, bool accepted, bool timedOut)
        {
            _waitingForGuests = false;
            _responses.Clear();
            _awaitedPlayerIds.Clear();
            _consentDeadlineAt = 0f;

            var sleepFsm = _pendingSleepFsm;
            var sleepState = _pendingSleepState;
            _pendingSleepFsm = null;
            _pendingSleepState = null;

            byte requestId = _activeRequestId;
            BroadcastResult(session, requestId, accepted);

            if (accepted)
            {
                _consentGranted = true;
                _awaitingPostSleepSync = true;
                session.AddSystemChat("* Everyone accepted — sleeping.");
                WinterMPPlugin.Log.LogInfo("SleepConsent: all guests accepted.");
                TryProceedSleep(sleepFsm, sleepState);
            }
            else
            {
                _consentGranted = false;
                _awaitingPostSleepSync = false;
                TryAbortSleep(sleepFsm);
                if (timedOut)
                    WinterMPPlugin.Log.LogWarning("SleepConsent: cancelled due to timeout.");
                else
                {
                    session.AddSystemChat("* Sleep cancelled — a guest declined.");
                    WinterMPPlugin.Log.LogWarning("SleepConsent: cancelled because a guest declined.");
                }
            }
        }

        private void BroadcastResult(SessionManager session, byte requestId, bool accepted)
        {
            session.BroadcastProfileMessage(new SleepConsentResult
            {
                RequestId = requestId,
                Accepted = accepted,
            });
        }

        private void CancelWaiting()
        {
            _waitingForGuests = false;
            _consentGranted = false;
            _awaitingPostSleepSync = false;
            _responses.Clear();
            _awaitedPlayerIds.Clear();
            _consentDeadlineAt = 0f;
            _pendingSleepFsm = null;
            _pendingSleepState = null;
        }

        private static void RollbackToConfirm(PlayMakerFSM fsm)
        {
            if (FsmHook.EnsureRemoteEntry(fsm, "Confirm"))
            {
                try
                {
                    FsmHook.FireRemoteEntry(fsm, "Confirm");
                }
                catch
                {
                    // FSM may be tearing down.
                }
            }
        }

        private static void TryAbortSleep(PlayMakerFSM? fsm)
        {
            if (fsm == null) return;

            foreach (string evt in new[] { "STOP", "ABORT" })
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

        private static void TryProceedSleep(PlayMakerFSM? fsm, string? enteredState)
        {
            if (fsm == null) return;

            if (HasFsmEvent(fsm, "ACTIVATE"))
            {
                try
                {
                    fsm.SendEvent("ACTIVATE");
                    WinterMPPlugin.Log.LogInfo("SleepConsent: sent ACTIVATE to proceed with sleep.");
                    return;
                }
                catch
                {
                    // FSM may be tearing down.
                }
            }

            if (string.IsNullOrEmpty(enteredState)) return;

            string stateName = enteredState!;
            if (FsmHook.EnsureRemoteEntry(fsm, stateName))
            {
                try
                {
                    FsmHook.FireRemoteEntry(fsm, stateName);
                    WinterMPPlugin.Log.LogInfo("SleepConsent: replayed sleep state " + stateName + ".");
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
