using System.Collections.Generic;
using BepInEx.Logging;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// Drives the main-menu Continue button once (Interface/Buttons/ButtonContinue).
    /// </summary>
    internal sealed class MenuContinue
    {
        private const string ContinuePath = "Interface/Buttons/ButtonContinue";
        private const string SetSizeFsm = "SetSize";

        private GameObject? _button;
        private PlayMakerFSM? _fsm;
        private bool _finished;
        private float _readyAt = -1f;

        public float StepDelaySeconds { get; set; }

        public bool IsFinished => _finished;

        public float ClickedAt { get; private set; } = -1f;

        public void Reset()
        {
            _button = null;
            _fsm = null;
            _finished = false;
            _readyAt = -1f;
            ClickedAt = -1f;
        }

        public bool TryAdvance(ManualLogSource log, bool logTimings)
        {
            if (_finished) return false;

            if (!ResolveContinueFsm()) return false;
            if (_button == null) return false;
            if (!_button.activeInHierarchy || _fsm == null || !_fsm.enabled
                || !_fsm.Fsm.Initialized || !_fsm.Fsm.Started) return false;

            if (_readyAt < 0f)
            {
                _readyAt = Time.unscaledTime;
                if (logTimings) log.LogInfo("FastBoot: preparing Continue.");
            }
            if (Time.unscaledTime - _readyAt < StepDelaySeconds || !TryClick(_fsm)) return false;

            ClickedAt = Time.unscaledTime;
            _finished = true;
            if (logTimings)
                log.LogInfo("FastBoot: Continue clicked — waiting for GAME load.");
            return true;
        }

        private static bool TryClick(PlayMakerFSM fsm)
        {
            var before = fsm.Fsm.ActiveState;
            if (before == null) return false;
            var hover = HasTransition(before, "DOWN") ? before : NextState(fsm, before, "OVER");
            if (hover == null || !hover.IsInitialized || !HasTransition(hover, "DOWN")) return false;
            var paused = new List<FsmStateAction>();
            try
            {
                // MousePickEvent checks the physical pointer during OnEnter and can
                // immediately send OFF, cancelling our OVER before DOWN is delivered.
                // Pause only that input poll for this synchronous synthetic click.
                foreach (var action in hover.Actions)
                    if (action.Enabled && action.GetType().FullName == "HutongGames.PlayMaker.Actions.MousePickEvent"
                        && action.GetType().Assembly.GetName().Name == "Assembly-CSharp")
                    { paused.Add(action); action.Enabled = false; }
                if (!ReferenceEquals(before, hover)) fsm.SendEvent("OVER");
                if (!ReferenceEquals(fsm.Fsm.ActiveState, hover)) return false;
                fsm.SendEvent("DOWN");
                return !ReferenceEquals(fsm.Fsm.ActiveState, hover);
            }
            finally { foreach (var action in paused) action.Enabled = true; }
        }

        private static bool HasTransition(FsmState state, string name)
        {
            foreach (var edge in state.Transitions) if (edge.EventName == name) return true;
            return false;
        }

        private static FsmState? NextState(PlayMakerFSM fsm, FsmState state, string name)
        {
            foreach (var edge in state.Transitions)
                if (edge.EventName == name)
                    foreach (var next in fsm.Fsm.States) if (next.Name == edge.ToState) return next;
            return null;
        }

        private bool ResolveContinueFsm()
        {
            if (_fsm != null) return true;

            if (_button == null)
                _button = GameObject.Find(ContinuePath);
            if (_button == null) return false;

            var fsms = _button.GetComponents<PlayMakerFSM>();
            if (fsms == null || fsms.Length == 0)
                fsms = _button.GetComponentsInChildren<PlayMakerFSM>(true);

            foreach (var component in fsms)
            {
                if (component.FsmName != SetSizeFsm) continue;
                _fsm = component;
                return true;
            }

            return false;
        }
    }
}
