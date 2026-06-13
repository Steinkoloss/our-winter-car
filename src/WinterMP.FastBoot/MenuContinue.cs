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
        private bool _clicked;

        public float StepDelaySeconds { get; set; }

        public bool IsFinished => _finished;

        public float ClickedAt { get; private set; } = -1f;

        public void Reset()
        {
            _button = null;
            _fsm = null;
            _finished = false;
            _clicked = false;
            ClickedAt = -1f;
        }

        public bool TryAdvance(ManualLogSource log, bool logTimings)
        {
            if (_finished) return false;

            if (!ResolveContinueFsm()) return false;
            if (_button == null || !_button.activeSelf) return false;
            if (_fsm == null) return false;

            if (!_clicked)
            {
                if (logTimings)
                    log.LogInfo("FastBoot: auto-loading save (Continue).");

                _fsm.SendEvent("OVER");
                ClickedAt = Time.unscaledTime;
                _clicked = true;

                if (StepDelaySeconds <= 0f)
                {
                    _fsm.SendEvent("DOWN");
                    _finished = true;
                    if (logTimings)
                        log.LogInfo("FastBoot: Continue clicked — waiting for GAME load.");
                    return true;
                }

                return true;
            }

            if (Time.unscaledTime - ClickedAt < StepDelaySeconds) return false;

            _fsm.SendEvent("DOWN");
            _finished = true;
            if (logTimings)
                log.LogInfo("FastBoot: Continue clicked — waiting for GAME load.");
            return true;
        }

        private bool ResolveContinueFsm()
        {
            if (_fsm != null) return true;

            if (_button == null)
                _button = GameObject.Find(ContinuePath);
            if (_button == null) return false;

            foreach (var component in _button.GetComponents<PlayMakerFSM>())
            {
                if (component.FsmName != SetSizeFsm) continue;
                _fsm = component;
                return true;
            }

            return false;
        }
    }
}
