using System;
using HutongGames.PlayMaker;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Action injected at the head of a PlayMaker state's action list; fires a
    /// callback every time the state is entered. Prepended (not appended) because
    /// original actions may transition away synchronously during their OnEnter,
    /// which would skip anything behind them.
    /// </summary>
    internal sealed class FsmHookAction : FsmStateAction
    {
        private readonly Action _callback;

        public FsmHookAction(Action callback)
        {
            _callback = callback;
        }

        public override void Reset()
        {
        }

        public override void OnEnter()
        {
            try
            {
                _callback();
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"WorldSync: FSM hook callback failed: {e}");
            }

            Finish();
        }
    }

    /// <summary>
    /// PlayMaker FSM plumbing for the world-sync layer (PLAN §4.2):
    /// - observe state entry on cataloged FSMs (door opened, bolt turned, ...)
    /// - replay a state entry remotely through an injected "MP_*" global transition
    ///   so the original state's actions (animation, sound, save flags) run there too.
    /// </summary>
    internal static class FsmHook
    {
        /// <summary>Invoke <paramref name="callback"/> whenever <paramref name="stateName"/> is entered.</summary>
        public static bool OnStateEnter(PlayMakerFSM fsm, string stateName, Action callback)
        {
            var state = FindState(fsm, stateName);
            if (state == null) return false;

            try
            {
                // Reading state.Actions lazy-loads the FSM's ActionData, which needs the
                // state's back-ref to its Fsm. On an FSM that hasn't initialized yet
                // (e.g. hooked via Transform.Find on an inactive object — state.Fsm == null)
                // that throws deep inside PlayMaker. Swallow it and report "not hooked" so
                // the caller retries once the FSM Awakes, instead of letting the NRE escape
                // and trip WorldSyncManager's shared kill switch (which disables ALL world
                // sync, vehicles included).
                var actions = state.Actions ?? new FsmStateAction[0];
                var expanded = new FsmStateAction[actions.Length + 1];
                expanded[0] = new FsmHookAction(callback);
                Array.Copy(actions, 0, expanded, 1, actions.Length);
                state.Actions = expanded;
                return true;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug(
                    $"FsmHook: state '{stateName}' on '{fsm.FsmName}' not ready to hook: {e.Message}");
                return false;
            }
        }

        /// <summary>"Open door" → "MP_OPEN_DOOR" — never collides with the game's own event names.</summary>
        public static string RemoteEntryEventName(string stateName)
        {
            return "MP_" + stateName.ToUpperInvariant().Replace(' ', '_');
        }

        /// <summary>
        /// Make <paramref name="stateName"/> remotely enterable: register a synthetic
        /// event plus a global transition targeting the state. Idempotent.
        /// </summary>
        public static bool EnsureRemoteEntry(PlayMakerFSM fsm, string stateName)
        {
            if (FindState(fsm, stateName) == null) return false;

            string eventName = RemoteEntryEventName(stateName);
            var globals = fsm.Fsm.GlobalTransitions ?? new FsmTransition[0];
            foreach (var existing in globals)
            {
                if (existing != null && existing.EventName == eventName)
                    return true;
            }

            var fsmEvent = FsmEvent.GetFsmEvent(eventName);
            var transition = new FsmTransition
            {
                FsmEvent = fsmEvent,
                ToState = stateName,
            };

            var expandedGlobals = new FsmTransition[globals.Length + 1];
            Array.Copy(globals, expandedGlobals, globals.Length);
            expandedGlobals[globals.Length] = transition;
            fsm.Fsm.GlobalTransitions = expandedGlobals;

            // Keep the FSM's event table consistent (editor/runtime bookkeeping).
            var events = fsm.Fsm.Events ?? new FsmEvent[0];
            bool known = false;
            foreach (var existing in events)
            {
                if (existing != null && existing.Name == eventName)
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                var expandedEvents = new FsmEvent[events.Length + 1];
                Array.Copy(events, expandedEvents, events.Length);
                expandedEvents[events.Length] = fsmEvent;
                fsm.Fsm.Events = expandedEvents;
            }

            return true;
        }

        /// <summary>Enter the state via its synthetic global transition (see <see cref="EnsureRemoteEntry"/>).</summary>
        public static void FireRemoteEntry(PlayMakerFSM fsm, string stateName)
        {
            fsm.SendEvent(RemoteEntryEventName(stateName));
        }

        public static FsmState? FindState(PlayMakerFSM fsm, string stateName)
        {
            var states = fsm.Fsm != null ? fsm.Fsm.States : null;
            if (states == null) return null;
            foreach (var state in states)
            {
                if (state != null && state.Name == stateName)
                    return state;
            }

            return null;
        }

        public static bool HasState(PlayMakerFSM fsm, string stateName)
        {
            return FindState(fsm, stateName) != null;
        }
    }
}
