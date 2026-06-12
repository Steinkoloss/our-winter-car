using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// Tier 2 config helpers: in-game setup buttons on SplashScreen (see
    /// <see cref="TryDismissSetup"/>). The native Unity "Play!" resolution dialog
    /// is skipped by writing registry PlayerPrefs before the exe starts — see
    /// WinterMP.Launcher <c>UnityDisplayPrefs</c> and <c>tools/seed-mwc-display.ps1</c>.
    /// </summary>
    internal sealed class ConfigScreenSkip
    {
        private static readonly string[] SetupObjectPaths =
        {
            "Interface/Buttons/ButtonPlay",
            "Interface/Buttons/ButtonStart",
            "Setup/Play",
            "Setup/ButtonPlay",
            "Canvas/Play",
            "PlayButton",
        };

        private static bool _prefsSeeded;
        private readonly HashSet<string> _attemptedPaths = new HashSet<string>();
        private bool _setupDismissed;

        public void Reset()
        {
            _attemptedPaths.Clear();
            _setupDismissed = false;
        }

        public static void SeedDisplayPrefsFromCommandLine()
        {
            if (_prefsSeeded) return;
            _prefsSeeded = true;

            int? width = null;
            int? height = null;
            int? fullscreen = null;

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (i + 1 >= args.Length) continue;

                if (string.Equals(args[i], "-screen-width", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(args[i + 1], out int w))
                {
                    width = w;
                }
                else if (string.Equals(args[i], "-screen-height", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(args[i + 1], out int h))
                {
                    height = h;
                }
                else if (string.Equals(args[i], "-screen-fullscreen", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(args[i + 1], out int fs))
                {
                    fullscreen = fs;
                }
            }

            if (!width.HasValue && !height.HasValue && !fullscreen.HasValue)
                return;

            try
            {
                if (width.HasValue)
                    PlayerPrefs.SetInt("Screenmanager Resolution Width", width.Value);
                if (height.HasValue)
                    PlayerPrefs.SetInt("Screenmanager Resolution Height", height.Value);
                if (fullscreen.HasValue)
                    PlayerPrefs.SetInt("Screenmanager Is Fullscreen mode", fullscreen.Value);
                PlayerPrefs.Save();
            }
            catch
            {
                // best effort
            }
        }

        /// <summary>
        /// Tries each known setup path once on SplashScreen. Returns true when a button
        /// was clicked or all paths were exhausted.
        /// </summary>
        public bool TryDismissSetup(string level, ManualLogSource log, bool logTimings)
        {
            if (_setupDismissed || !BootPhase.AllowsSetupSkip(level))
                return false;

            foreach (string path in SetupObjectPaths)
            {
                if (_attemptedPaths.Contains(path)) continue;

                var target = GameObject.Find(path);
                if (target == null || !target.activeInHierarchy)
                {
                    _attemptedPaths.Add(path);
                    continue;
                }

                if (TryClickSetupButton(target, log, logTimings, path))
                {
                    _setupDismissed = true;
                    return true;
                }

                _attemptedPaths.Add(path);
            }

            if (_attemptedPaths.Count >= SetupObjectPaths.Length)
                _setupDismissed = true;

            return false;
        }

        private static bool TryClickSetupButton(
            GameObject target,
            ManualLogSource log,
            bool logTimings,
            string path)
        {
            foreach (var fsm in target.GetComponents<PlayMakerFSM>())
            {
                try
                {
                    fsm.SendEvent("CLICK");
                    if (logTimings)
                    {
                        log.LogInfo(
                            $"FastBoot: setup skip — CLICK on '{path}' ({fsm.FsmName}).");
                    }

                    return true;
                }
                catch
                {
                    // try next FSM on same object
                }
            }

            return false;
        }
    }
}
