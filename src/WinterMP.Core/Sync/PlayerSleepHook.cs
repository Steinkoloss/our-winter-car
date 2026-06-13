using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Hooks bed/sleep PlayMaker states on the host PLAYER (and bed objects) so
    /// multi-player sleep requires guest consent (PLAN.md §4.4).
    /// </summary>
    internal sealed class PlayerSleepHook
    {
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

            TryHookObject("PLAYER", session);
            TryHookObject("Bed", session);
        }

        private void TryHookObject(string objectName, SessionManager session)
        {
            var go = GameObject.Find(objectName);
            if (go == null) return;

            var fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
            for (int i = 0; i < fsms.Length; i++)
                TryHookFsm(fsms[i], session);
        }

        private void TryHookFsm(PlayMakerFSM fsm, SessionManager session)
        {
            if (fsm == null || _hooked.Contains(fsm)) return;

            var states = fsm.Fsm != null ? fsm.Fsm.States : null;
            if (states == null) return;

            bool hookedAny = false;
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state == null || state.Name == null) continue;
                if (!IsSleepState(state.Name)) continue;

                string captured = state.Name;
                if (FsmHook.OnStateEnter(fsm, captured, () => OnSleepStateEntered(fsm, session)))
                    hookedAny = true;
            }

            if (hookedAny)
            {
                _hooked.Add(fsm);
                WinterMPPlugin.Log.LogInfo(
                    "SleepConsent: hooked sleep states on " + fsm.gameObject.name + ".");
            }
        }

        private static bool IsSleepState(string name)
        {
            if (name.IndexOf("sleep", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("time skip", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void OnSleepStateEntered(PlayMakerFSM fsm, SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            var manager = SleepConsentManager.Instance;
            if (manager == null) return;

            manager.OnHostSleepAttempt(fsm);
        }
    }
}
