using System;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Hooks the game's <c>SleepTrigger :: Activate</c> FSM (catalog dump
    /// <c>dump-23268598.json</c>, path * /Sleep/SleepTrigger). On the host,
    /// multi-player sleep requires guest consent before time skip (PLAN.md §4.4).
    /// On a guest the whole flow is blocked at Confirm: time is host-owned, and a
    /// locally-running sleep loop would fire every time-gated FSM (mail, jobs,
    /// interest) on the guest before TimeSync snaps the clock back.
    /// </summary>
    internal sealed class PlayerSleepHook
    {
        private const string ActivateFsmName = "Activate";
        private const string SleepTriggerObjectName = "SleepTrigger";
        private const string SleepTriggerPathMarker = "/Sleep/SleepTrigger";

        /// <summary>Host confirmed sleep or setup started — before AnimateSleep advances the clock.</summary>
        private static readonly string[] ConsentTriggerStates = { "Confirm", "Get positions" };

        /// <summary>Host finished the sleep-time loop — push an immediate TimeSync.</summary>
        private static readonly string[] PostSleepSyncStates = { "Calc rates" };

        private readonly System.Collections.Generic.HashSet<PlayMakerFSM> _hooked =
            new System.Collections.Generic.HashSet<PlayMakerFSM>();

        private float _nextProbeAt;

        public void Reset()
        {
            _hooked.Clear();
            _nextProbeAt = 0f;
        }

        public void Probe(SessionManager session)
        {
            if (session == null) return;
            if (Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + 3f;

            PlayMakerFSM[] fsms;
            try
            {
                fsms = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
            }
            catch
            {
                return;
            }

            for (int i = 0; i < fsms.Length; i++)
                TryHookSleepActivate(fsms[i]);
        }

        private void TryHookSleepActivate(PlayMakerFSM fsm)
        {
            if (fsm == null || _hooked.Contains(fsm)) return;
            if (fsm.FsmName != ActivateFsmName) return;
            if (fsm.gameObject.name != SleepTriggerObjectName) return;

            string path;
            try
            {
                path = ScenePath.Of(fsm.transform);
            }
            catch
            {
                return;
            }

            if (path.IndexOf(SleepTriggerPathMarker, StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var states = fsm.Fsm != null ? fsm.Fsm.States : null;
            if (states == null) return;

            bool hookedAny = false;
            for (int s = 0; s < ConsentTriggerStates.Length; s++)
            {
                string stateName = ConsentTriggerStates[s];
                if (!FsmHook.HasState(fsm, stateName)) continue;

                if (FsmHook.OnStateEnter(fsm, stateName, () => OnConsentStateEntered(fsm, stateName)))
                    hookedAny = true;
            }

            for (int s = 0; s < PostSleepSyncStates.Length; s++)
            {
                string stateName = PostSleepSyncStates[s];
                if (!FsmHook.HasState(fsm, stateName)) continue;

                if (FsmHook.OnStateEnter(fsm, stateName, OnPostSleepStateEntered))
                    hookedAny = true;
            }

            if (!hookedAny) return;

            _hooked.Add(fsm);
            WinterMPPlugin.Log.LogInfo(
                "SleepConsent: hooked Activate sleep FSM at " + path + ".");
        }

        private static void OnConsentStateEntered(PlayMakerFSM fsm, string stateName)
        {
            // Resolve live: the hook outlives a session, and the same machine may host
            // the next one — role must be decided at fire time, not install time.
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            if (!session.IsHost)
            {
                GuestBlockSleep(fsm, session);
                return;
            }

            var manager = SleepConsentManager.Instance;
            if (manager == null) return;

            if (stateName == "Get positions")
                manager.OnHostReachedGetPositions(fsm);
            else
                manager.OnHostSleepAttempt(fsm, stateName);
        }

        private static float _nextGuestHintAt;

        /// <summary>Guest: leave the sleep flow via Confirm's own "didn't confirm" route (State 3).</summary>
        private static void GuestBlockSleep(PlayMakerFSM fsm, SessionManager session)
        {
            if (FsmHook.EnsureRemoteEntry(fsm, "State 3"))
            {
                try
                {
                    FsmHook.FireRemoteEntry(fsm, "State 3");
                }
                catch
                {
                    // FSM may be tearing down.
                }
            }

            if (Time.unscaledTime >= _nextGuestHintAt)
            {
                _nextGuestHintAt = Time.unscaledTime + 10f;
                session.AddSystemChat("* Only the host can start sleep — ask the host to go to bed.");
            }
        }

        private static void OnPostSleepStateEntered()
        {
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0 || !session.IsHost) return;
            SleepConsentManager.Instance?.OnHostSleepCompleted();
        }
    }
}
