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
            if (!Enabled) { Finish(); return; }
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
            return OnStateEnter(fsm, stateName, callback, out _);
        }

        internal static bool OnStateEnter(PlayMakerFSM fsm, string stateName, Action callback, out FsmStateAction? hook)
        {
            hook = null;
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
                hook = new FsmHookAction(callback);
                expanded[0] = hook;
                Array.Copy(actions, 0, expanded, 1, actions.Length);
                state.Actions = expanded;
                return true;
            }
            catch (Exception e)
            {
                hook = null;
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

    /// <summary>
    /// Guest-side kill switch for exactly one FSM, with restore.
    ///
    /// <see cref="OnStateEnter"/> only *observes* — it prepends an action, and the state's own
    /// actions still run. When a guest must not resolve something locally (RNG, payout, an
    /// ownership transfer), the FSM has to be cut instead.
    ///
    /// Disabling the component is a hard cut, verified against the shipped PlayMaker.dll:
    /// <c>Fsm.Active</c> gates on <c>owner.enabled</c>, and <c>Fsm.ProcessEvent</c> early-returns
    /// when inactive — so a disabled FSM cannot be driven into a state by its own Update, by a
    /// targeted SendEvent, by a global transition, or by a broadcast.
    ///
    /// The subtlety is <c>Fsm.RestartOnEnable</c> (default true). Left alone, disabling runs
    /// <c>Stop() -> StopAndReset()</c> — firing ExitState on the very state we are freezing —
    /// and re-enabling would restart the FSM at its start state. Pinning it false across the
    /// window makes this freeze-in-place / resume-in-place instead.
    /// </summary>
    internal sealed class FsmSuppressor
    {
        private PlayMakerFSM? _fsm;
        private bool _previousEnabled;
        private bool _previousRestartOnEnable = true;
        private bool _active;

        /// <summary>True only while a live component is confirmed disabled by us.</summary>
        public bool Active { get { return _active && _fsm != null; } }

        public bool Suppress(PlayMakerFSM? fsm)
        {
            if (fsm == null) return false;
            if (_active && !ReferenceEquals(fsm, _fsm)) Restore();
            if (_active) return true;

            try
            {
                _previousEnabled = fsm.enabled;
                _previousRestartOnEnable = true;

                var inner = fsm.Fsm;
                if (inner != null)
                {
                    _previousRestartOnEnable = inner.RestartOnEnable;
                    inner.RestartOnEnable = false;
                }

                if (fsm.enabled) fsm.enabled = false;
                _fsm = fsm;
                _active = true;
                return true;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("FsmSuppressor: suppress failed: " + e.Message);
                _fsm = null;
                _active = false;
                return false;
            }
        }

        public void Restore()
        {
            var fsm = _fsm;
            _fsm = null;
            _active = false;
            if (fsm == null) return;   // Unity fake-null also covers a destroyed component

            try
            {
                // Order matters: OnEnable fires inside this assignment and reads
                // RestartOnEnable, which must still be false or it restarts the FSM.
                fsm.enabled = _previousEnabled;
                var inner = fsm.Fsm;
                if (inner != null) inner.RestartOnEnable = _previousRestartOnEnable;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("FsmSuppressor: restore failed: " + e.Message);
            }
        }
    }
}
