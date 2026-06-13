using BepInEx.Logging;
using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// EXPERIMENTAL: skip Continue + ES2 hydrate — MainMenu goes straight to GAME.
    /// MWC normally requires ES2 hydrate first; this can crash. Prefer DevMode with
    /// fast Continue + ES2 tag skip instead.
    /// </summary>
    internal sealed class DevDirectGameLoad
    {
        private const float MinMenuSeconds = 0.05f;

        private bool _scheduled;
        private bool _finished;

        public bool IsFinished => _finished;

        public void Reset()
        {
            _scheduled = false;
            _finished = false;
            LoadPipeline.DevDirectLoadActive = false;
        }

        public void TryAdvance(ManualLogSource log, bool logTimings, float mainMenuSeenAt)
        {
            if (_finished || _scheduled || mainMenuSeenAt < 0f) return;

            float now = Time.realtimeSinceStartup;
            if (now - mainMenuSeenAt < MinMenuSeconds) return;

            _scheduled = true;
            LoadPipeline.DevDirectLoadActive = true;

            if (logTimings)
            {
                log.LogWarning(
                    "FastBoot DEV: experimental direct LoadLevel(GAME #"
                    + LoadPipeline.GameLevelIndex
                    + ") — no ES2 hydrate. Set Boot.DevDirectGameLoad=false if this crashes.");
            }

            try
            {
                Application.LoadLevel(LoadPipeline.GameLevelIndex);
                _finished = true;
            }
            catch (System.Exception e)
            {
                _scheduled = false;
                LoadPipeline.DevDirectLoadActive = false;
                log.LogWarning("FastBoot DEV: direct LoadLevel(GAME) failed: " + e.Message);
            }
        }
    }
}
