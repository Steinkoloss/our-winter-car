using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// Boot accelerators: skip splash, dismiss setup UI, auto-continue when a save exists.
    /// All PlayMaker automation is restricted to SplashScreen / MainMenu — never GAME.
    /// </summary>
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public sealed class FastBootPlugin : BaseUnityPlugin
    {
        private const int MenuLevelIndex = 1;

        private ConfigEntry<bool> _enabled = null!;
        private ConfigEntry<bool> _skipSplash = null!;
        private ConfigEntry<bool> _skipConfigScreen = null!;
        private ConfigEntry<bool> _autoLoadSave = null!;
        private ConfigEntry<float> _splashGraceSeconds = null!;
        private ConfigEntry<float> _menuSettleSeconds = null!;
        private ConfigEntry<bool> _logTimings = null!;

        private bool _bootComplete;
        private bool _splashSkipScheduled;
        private bool _splashSkipDone;
        private float _splashSeenAt = -1f;
        private float _mainMenuSeenAt = -1f;
        private string _lastLevel = string.Empty;

        private readonly ConfigScreenSkip _configSkip = new ConfigScreenSkip();
        private readonly MenuContinue _continue = new MenuContinue();
        private readonly BootTimer _timer = new BootTimer();

        private void Awake()
        {
            _timer.PluginAwakeAt = Time.realtimeSinceStartup;

            _enabled = Config.Bind("Boot", "Enabled", true, "Master switch for all FastBoot tiers.");
            _skipSplash = Config.Bind(
                "Boot", "SkipSplashScreen", true,
                "Load MainMenu after SplashGraceSeconds on SplashScreen.");
            _skipConfigScreen = Config.Bind(
                "Boot", "SkipConfigScreen", true,
                "Seed -screen-* prefs and CLICK known setup Play buttons on SplashScreen only.");
            _autoLoadSave = Config.Bind(
                "Boot", "AutoLoadSave", true,
                "Click Continue on MainMenu when savefile.txt exists. Hosts wait for a connected player.");
            _splashGraceSeconds = Config.Bind(
                "Boot", "SplashGraceSeconds", 1.0f,
                "Seconds on SplashScreen before LoadLevel(MainMenu).");
            _menuSettleSeconds = Config.Bind(
                "Boot", "MenuSettleSeconds", 0.75f,
                "Seconds on MainMenu before clicking Continue.");
            _logTimings = Config.Bind(
                "Boot", "LogTimings", true,
                "Log boot phase timings and a summary when GAME loads.");

            if (HasCommandLineFlag("-no-fastboot"))
                _enabled.Value = false;

            if (!_enabled.Value)
            {
                Logger.LogInfo("FastBoot inactive (-no-fastboot or Boot.Enabled=false).");
                return;
            }

            if (_skipConfigScreen.Value)
                ConfigScreenSkip.SeedDisplayPrefsFromCommandLine();

            Logger.LogInfo(
                "FastBoot v" + MyPluginInfo.PLUGIN_VERSION + " active: "
                + FeatureList());
            Logger.LogInfo(
                "FastBoot safety: PlayMaker automation only on SplashScreen + MainMenu; "
                + "stops permanently once GAME loads.");
        }

        private string FeatureList()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_skipSplash.Value) parts.Add("skip splash");
            if (_skipConfigScreen.Value) parts.Add("skip config");
            if (_autoLoadSave.Value) parts.Add("auto-load save");
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none";
        }

        private void Update()
        {
            if (!_enabled.Value || _bootComplete) return;

            string level;
            try
            {
                level = Application.loadedLevelName ?? string.Empty;
            }
            catch
            {
                return;
            }

            float now = Time.realtimeSinceStartup;

            if (BootPhase.IsGame(level))
            {
                FinishBoot(now);
                return;
            }

            WatchLevelChanges(level, now);

            if (_skipConfigScreen.Value && BootPhase.AllowsSetupSkip(level))
                _configSkip.TryDismissSetup(level, Logger, _logTimings.Value);

            if (_skipSplash.Value && !_splashSkipDone)
                UpdateSplashSkip(level, now);

            if (_autoLoadSave.Value && BootPhase.AllowsAutoLoad(level))
                UpdateAutoLoad(now);
        }

        private void WatchLevelChanges(string level, float now)
        {
            if (level == _lastLevel) return;

            if (_logTimings.Value && level.Length > 0)
                Logger.LogInfo($"FastBoot: level '{level}' at {now:0.0}s.");

            if (level == BootPhase.Splash)
                _timer.NoteSplash(now);

            if (level == BootPhase.Menu)
            {
                _mainMenuSeenAt = now;
                _timer.NoteMenu(now);
            }

            _lastLevel = level;
        }

        private void FinishBoot(float now)
        {
            _bootComplete = true;
            _timer.NoteGame(now);

            if (_logTimings.Value)
            {
                Logger.LogInfo($"FastBoot: boot complete — {_timer.FormatSummary()}.");
            }
        }

        private void UpdateSplashSkip(string level, float now)
        {
            if (level == BootPhase.Menu)
            {
                _splashSkipDone = true;
                return;
            }

            if (level != BootPhase.Splash) return;

            if (_splashSeenAt < 0f)
                _splashSeenAt = now;

            if (_splashSkipScheduled) return;
            if (now - _splashSeenAt < _splashGraceSeconds.Value) return;

            _splashSkipScheduled = true;
            try
            {
                if (_logTimings.Value)
                {
                    Logger.LogInfo(
                        $"FastBoot: LoadLevel({BootPhase.Menu} #{MenuLevelIndex}) "
                        + $"after {now - _splashSeenAt:0.0}s on splash.");
                }

                Application.LoadLevel(MenuLevelIndex);
            }
            catch (Exception e)
            {
                _splashSkipScheduled = false;
                Logger.LogError($"FastBoot: LoadLevel failed: {e.Message}");
            }
        }

        private void UpdateAutoLoad(float now)
        {
            if (_continue.IsFinished) return;
            if (!SaveFileProbe.HasSave()) return;
            if (!SessionGate.CanAutoLoadContinue()) return;

            if (_mainMenuSeenAt < 0f || now - _mainMenuSeenAt < _menuSettleSeconds.Value)
                return;

            _continue.TryAdvance(Logger, _logTimings.Value);
        }

        private static bool HasCommandLineFlag(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
