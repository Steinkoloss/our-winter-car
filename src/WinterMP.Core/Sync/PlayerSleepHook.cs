using System;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Hooks the game's <c>SleepTrigger :: Activate</c> FSM (catalog dump
    /// <c>dump-23268598.json</c>, path * /Sleep/SleepTrigger) so multi-player sleep
    /// requires guest consent before time skip (PLAN.md §4.4).
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
            if (session == null || !session.IsHost) return;
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
                TryHookSleepActivate(fsms[i], session);
        }

        private void TryHookSleepActivate(PlayMakerFSM fsm, SessionManager session)
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

                if (FsmHook.OnStateEnter(fsm, stateName, () => OnConsentStateEntered(fsm, session, stateName)))
                    hookedAny = true;
            }

            for (int s = 0; s < PostSleepSyncStates.Length; s++)
            {
                string stateName = PostSleepSyncStates[s];
                if (!FsmHook.HasState(fsm, stateName)) continue;

                if (FsmHook.OnStateEnter(fsm, stateName, () => OnPostSleepStateEntered(session)))
                    hookedAny = true;
            }

            if (!hookedAny) return;

            _hooked.Add(fsm);
            WinterMPPlugin.Log.LogInfo(
                "SleepConsent: hooked Activate sleep FSM at " + path + ".");
        }

        private static void OnConsentStateEntered(PlayMakerFSM fsm, SessionManager session, string stateName)
        {
            if (session.PlayerCount == 0) return;

            var manager = SleepConsentManager.Instance;
            if (manager == null) return;

            if (stateName == "Get positions")
                manager.OnHostReachedGetPositions(fsm);
            else
                manager.OnHostSleepAttempt(fsm, stateName);
        }

        private static void OnPostSleepStateEntered(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            SleepConsentManager.Instance?.OnHostSleepCompleted();
        }
    }
}
