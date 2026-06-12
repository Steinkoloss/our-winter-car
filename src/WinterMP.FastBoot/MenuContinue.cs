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

        private int _step;
        private float _nextAt;
        private bool _finished;

        public bool IsFinished => _finished;

        public bool TryAdvance(ManualLogSource log, bool logTimings)
        {
            if (_finished || _step >= 2) return false;
            if (Time.unscaledTime < _nextAt) return false;

            var button = GameObject.Find(ContinuePath);
            if (button == null) return false;

            PlayMakerFSM? fsm = null;
            foreach (var component in button.GetComponents<PlayMakerFSM>())
            {
                if (component.FsmName == SetSizeFsm)
                {
                    fsm = component;
                    break;
                }
            }

            if (fsm == null) return false;

            switch (_step)
            {
                case 0:
                    if (logTimings)
                        log.LogInfo("FastBoot: auto-loading save (Continue).");
                    fsm.SendEvent("OVER");
                    _step = 1;
                    _nextAt = Time.unscaledTime + 0.5f;
                    return true;
                case 1:
                    fsm.SendEvent("DOWN");
                    _finished = true;
                    if (logTimings)
                        log.LogInfo("FastBoot: Continue clicked — waiting for GAME load.");
                    return true;
            }

            return false;
        }
    }
}
