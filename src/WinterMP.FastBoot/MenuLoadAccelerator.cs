using BepInEx.Logging;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// After Continue, nudges main-menu loading PlayMaker FSMs. Paths vary by game build,
    /// so we scan every active FSM on the scene.
    /// </summary>
    internal sealed class MenuLoadAccelerator
    {
        private const int GameLevelIndex = 3;

        private static readonly string[] SkipWaitEvents =
        {
            "loadedEvent",
            "LOAD",
            "FINISHED",
            "DONE",
            "Finish",
            "LOADED",
        };

        private float _pokeInterval = 0.15f;
        private bool _loadLevelScheduled;
        private float _nextLoadLevelAt;
        private float _nextPokeAt;
        private bool _loggedScan;

        public void Reset()
        {
            _loadLevelScheduled = false;
            _nextLoadLevelAt = 0f;
            _nextPokeAt = 0f;
            _pokeInterval = 0.15f;
            _loggedScan = false;
        }

        public void TryAdvance(
            ManualLogSource log,
            bool logTimings,
            float continueClickedAt,
            float forceLoadLevelAfterSeconds,
            bool devFast)
        {
            if (devFast)
                _pokeInterval = 0.05f;

            float now = Time.unscaledTime;
            if (now >= _nextPokeAt)
            {
                _nextPokeAt = now + _pokeInterval;
                TryPokeLoadingFsms(log, logTimings);
            }

            if (forceLoadLevelAfterSeconds <= 0f || _loadLevelScheduled) return;
            if (continueClickedAt < 0f || now - continueClickedAt < forceLoadLevelAfterSeconds) return;

            if (_nextLoadLevelAt <= 0f)
                _nextLoadLevelAt = now;

            if (now - _nextLoadLevelAt < 0.5f) return;

            _loadLevelScheduled = true;
            try
            {
                if (logTimings)
                {
                    log.LogInfo(
                        "FastBoot: still on MainMenu "
                        + (now - continueClickedAt).ToString("0.0")
                        + "s after Continue — calling LoadLevel(GAME #" + GameLevelIndex + ").");
                }

                Application.LoadLevel(GameLevelIndex);
            }
            catch (System.Exception e)
            {
                _loadLevelScheduled = false;
                log.LogWarning("FastBoot: LoadLevel(GAME) fallback failed: " + e.Message);
            }
        }

        private void TryPokeLoadingFsms(ManualLogSource log, bool logTimings)
        {
            var fsms = (PlayMakerFSM[])Object.FindObjectsOfType(typeof(PlayMakerFSM));
            int poked = 0;

            for (int i = 0; i < fsms.Length; i++)
            {
                PlayMakerFSM fsm = fsms[i];
                if (fsm == null || !fsm.gameObject.activeInHierarchy)
                    continue;

                if (!ShouldPoke(fsm))
                    continue;

                string path = fsm.gameObject.name;
                Transform? t = fsm.transform.parent;
                if (t != null)
                    path = t.name + "/" + path;

                for (int e = 0; e < SkipWaitEvents.Length; e++)
                {
                    try
                    {
                        fsm.SendEvent(SkipWaitEvents[e]);
                        poked++;
                        if (logTimings && !_loggedScan)
                        {
                            log.LogInfo(
                                "FastBoot: loading skip — '"
                                + SkipWaitEvents[e]
                                + "' on '"
                                + path
                                + "/"
                                + fsm.FsmName
                                + "' (state '"
                                + fsm.ActiveStateName
                                + "').");
                        }
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }

            if (logTimings && !_loggedScan)
            {
                _loggedScan = true;
                log.LogInfo("FastBoot: loading FSM scan — poked " + poked + " event(s) on MainMenu.");
            }
        }

        private static bool ShouldPoke(PlayMakerFSM fsm)
        {
            string fsmName = fsm.FsmName ?? string.Empty;
            string state = fsm.ActiveStateName ?? string.Empty;
            string objName = fsm.gameObject.name ?? string.Empty;

            if (ContainsIgnoreCase(fsmName, "Load")
                || ContainsIgnoreCase(fsmName, "LOAD")
                || ContainsIgnoreCase(objName, "Load")
                || ContainsIgnoreCase(state, "Wait")
                || ContainsIgnoreCase(state, "Load")
                || ContainsIgnoreCase(state, "LOAD"))
            {
                return true;
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            return haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
