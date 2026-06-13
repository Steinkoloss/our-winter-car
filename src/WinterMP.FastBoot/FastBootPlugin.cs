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
    [BepInDependency("com.ourwintercar.wintermp", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class FastBootPlugin : BaseUnityPlugin
    {
        internal static FastBootPlugin? Instance { get; private set; }

        private const int MenuLevelIndex = 1;

        private ConfigEntry<bool> _enabled = null!;
        private ConfigEntry<bool> _skipSplash = null!;
        private ConfigEntry<bool> _skipConfigScreen = null!;
        private ConfigEntry<bool> _autoLoadSave = null!;
        private ConfigEntry<float> _splashGraceSeconds = null!;
        private ConfigEntry<float> _menuSettleSeconds = null!;
        private ConfigEntry<float> _saveCheckTimeoutSeconds = null!;
        private ConfigEntry<float> _continueStepDelaySeconds = null!;
        private ConfigEntry<bool> _skipMenuLoadWaits = null!;
        private ConfigEntry<float> _forceGameLoadAfterSeconds = null!;
        private ConfigEntry<bool> _preloadGameAsync = null!;
        private ConfigEntry<bool> _devMode = null!;
        private ConfigEntry<bool> _bypassHostContinueWait = null!;
        private ConfigEntry<bool> _devDirectGameLoad = null!;
        private ConfigEntry<bool> _devSkipEs2Tags = null!;
        private ConfigEntry<bool> _devEs2Whitelist = null!;
        private ConfigEntry<string> _devSkipEs2ExtraPrefixes = null!;
        private ConfigEntry<bool> _logTimings = null!;
        private ConfigEntry<bool> _analyzeEs2OnStartup = null!;

        private bool _fastContinue;
        private bool _bypassHostWait;
        private bool _devDirectActive;
        private bool _es2SkipActive;
        private bool _es2WhitelistActive;

        private bool _bootComplete;
        private bool _splashSkipScheduled;
        private bool _splashSkipDone;
        private float _splashSeenAt = -1f;
        private float _mainMenuSeenAt = -1f;
        private string _lastLevel = string.Empty;

        private bool _continueHydrateStarted;

        private readonly ConfigScreenSkip _configSkip = new ConfigScreenSkip();
        private readonly MenuContinue _continue = new MenuContinue();
        private readonly MenuLoadAccelerator _menuLoad = new MenuLoadAccelerator();
        private readonly DevDirectGameLoad _devDirect = new DevDirectGameLoad();
        private readonly BootTimer _timer = new BootTimer();

        private void Awake()
        {
            Instance = this;
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
                "Boot", "SplashGraceSeconds", 0.4f,
                "Seconds on SplashScreen before LoadLevel(MainMenu).");
            _menuSettleSeconds = Config.Bind(
                "Boot", "MenuSettleSeconds", 0f,
                "Seconds on MainMenu before clicking Continue.");
            _saveCheckTimeoutSeconds = Config.Bind(
                "Boot", "SaveCheckTimeoutSeconds", 0.5f,
                "Max wait for Check Save FSM before clicking Continue anyway.");
            _continueStepDelaySeconds = Config.Bind(
                "Boot", "ContinueStepDelaySeconds", 0f,
                "Seconds between Continue OVER and DOWN. 0 = same frame.");
            _skipMenuLoadWaits = Config.Bind(
                "Boot", "SkipMenuLoadWaits", true,
                "Nudge main-menu loading PlayMaker FSMs after Continue to skip artificial waits.");
            _forceGameLoadAfterSeconds = Config.Bind(
                "Boot", "ForceGameLoadAfterSeconds", 0f,
                "0 = off. If still on MainMenu this many seconds after Continue, call LoadLevel(GAME) as fallback.");
            _preloadGameAsync = Config.Bind(
                "Boot", "PreloadGameAsync", true,
                "Start LoadLevelAsync(GAME) on Continue, overlapping ES2 hydrate.");
            _devMode = Config.Bind(
                "Boot", "DevMode", true,
                "Fast Continue (zero menu settle / save-check wait). -fastboot-dev also enables this.");
            _bypassHostContinueWait = Config.Bind(
                "Boot", "BypassHostContinueWait", true,
                "Hosts click Continue without waiting for a connected guest.");
            _devDirectGameLoad = Config.Bind(
                "Boot", "DevDirectGameLoad", false,
                "EXPERIMENTAL: skip Continue + ES2 and LoadLevel(GAME) from MainMenu. Crashes — leave false.");
            _devSkipEs2Tags = Config.Bind(
                "Boot", "DevSkipEs2Tags", true,
                "Skip nonessential ES2 tags during Continue hydrate.");
            _devEs2Whitelist = Config.Bind(
                "Boot", "DevEs2Whitelist", true,
                "Only hydrate core boot tags (World*, Player*, vehicles). Much faster Continue→GAME.");
            _devSkipEs2ExtraPrefixes = Config.Bind(
                "Boot", "DevSkipEs2ExtraPrefixes", string.Empty,
                "Extra comma-separated ES2 tag prefixes to skip during dev hydrate.");
            _logTimings = Config.Bind(
                "Boot", "LogTimings", true,
                "Log boot phase timings and a summary when GAME loads.");
            _analyzeEs2OnStartup = Config.Bind(
                "Boot", "AnalyzeEs2SaveOnStartup", false,
                "Scan savefile.txt tag names at plugin load (dev diagnostics only — costs startup time).");

            if (HasCommandLineFlag("-no-fastboot"))
                _enabled.Value = false;

            bool cmdlineFast = HasCommandLineFlag("-fastboot-dev");
            _fastContinue = _devMode.Value || cmdlineFast;
            _bypassHostWait = _bypassHostContinueWait.Value || cmdlineFast;
            _devDirectActive = (_fastContinue || cmdlineFast) && _devDirectGameLoad.Value;
            _es2WhitelistActive = _devEs2Whitelist.Value;
            _es2SkipActive = _devSkipEs2Tags.Value || _es2WhitelistActive;

            if (!_enabled.Value)
            {
                Logger.LogInfo("FastBoot inactive (-no-fastboot or Boot.Enabled=false).");
                return;
            }

            if (_skipConfigScreen.Value)
                ConfigScreenSkip.SeedDisplayPrefsFromCommandLine();

            _continue.StepDelaySeconds = _fastContinue ? 0f : _continueStepDelaySeconds.Value;
            LoadPipeline.PreloadEnabled = _preloadGameAsync.Value;
            LoadPipeline.LogTimings = _logTimings.Value;
            Es2HydratePolicy.Configure(
                _es2SkipActive,
                _fastContinue || _es2SkipActive,
                _es2WhitelistActive,
                _devSkipEs2ExtraPrefixes.Value);
            SessionGate.Configure(_bypassHostWait);
            HarmonyBootstrap.Apply(Logger);

            if (_analyzeEs2OnStartup.Value)
                Es2SaveAnalyzer.TryWriteOfflineReport(Logger);

            if (_fastContinue || _es2SkipActive || _bypassHostWait)
            {
                Logger.LogInfo(
                    "FastBoot speed: fast Continue="
                    + (_fastContinue ? "on" : "off")
                    + ", host wait bypass="
                    + (_bypassHostWait ? "on" : "off")
                    + ", ES2 skip="
                    + (_es2SkipActive ? "on" : "off")
                    + ", ES2 whitelist="
                    + (_es2WhitelistActive ? "on" : "off")
                    + ", direct GAME="
                    + (_devDirectActive ? "on (experimental)" : "off")
                    + ".");
            }

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
            if (_preloadGameAsync.Value) parts.Add("async GAME preload");
            if (_devDirectActive) parts.Add("direct GAME (experimental)");
            if (_es2SkipActive) parts.Add("ES2 skip");
            if (_es2WhitelistActive) parts.Add("ES2 whitelist");
            if (_bypassHostWait) parts.Add("host wait bypass");
            if (_fastContinue) parts.Add("fast Continue");
            if (_skipMenuLoadWaits.Value) parts.Add("skip menu load waits");
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

            if (_devDirectActive && BootPhase.AllowsAutoLoad(level))
            {
                _devDirect.TryAdvance(Logger, _logTimings.Value, _mainMenuSeenAt);
            }
            else if (_autoLoadSave.Value && BootPhase.AllowsAutoLoad(level))
                UpdateAutoLoad(now);

            if (_skipMenuLoadWaits.Value
                && BootPhase.AllowsAutoLoad(level)
                && !_devDirectActive
                && _continue.ClickedAt >= 0f)
            {
                _menuLoad.TryAdvance(
                    Logger,
                    _logTimings.Value,
                    _continue.ClickedAt,
                    _forceGameLoadAfterSeconds.Value,
                    _fastContinue);
            }
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
                MainMenuSaveCheck.Reset();
                _continue.Reset();
                _menuLoad.Reset();
                _devDirect.Reset();
                _continueHydrateStarted = false;
                Es2SaveAnalyzer.ResetBoot();
                Es2HydratePolicy.ResetBoot();
                LoadPipeline.ResetBootMetrics(false);
            }

            _lastLevel = level;
        }

        private void FinishBoot(float now)
        {
            _bootComplete = true;
            _timer.NoteGame(now);
            Es2HydratePolicy.EndContinueHydrate();

            if (_logTimings.Value)
            {
                Logger.LogInfo("FastBoot: boot complete — " + _timer.FormatSummary() + ".");
                LoadPipeline.WriteReport(Logger, _timer.ContinueAt, _timer.GameAt);
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

                LoadPipeline.AbandonPreload();
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

            if (_mainMenuSeenAt < 0f) return;

            float menuSettle = _fastContinue ? 0f : _menuSettleSeconds.Value;
            float saveTimeout = _fastContinue ? 0f : _saveCheckTimeoutSeconds.Value;

            if (!MainMenuSaveCheck.IsSaveValid()
                && now - _mainMenuSeenAt < menuSettle)
            {
                return;
            }

            if (!MainMenuSaveCheck.AllowsContinueClick(_mainMenuSeenAt, saveTimeout))
                return;

            if (_continue.TryAdvance(Logger, _logTimings.Value) && _continue.ClickedAt >= 0f)
            {
                _timer.NoteContinue(_continue.ClickedAt);
                if (!_continueHydrateStarted)
                {
                    _continueHydrateStarted = true;
                    Es2HydratePolicy.BeginContinueHydrate();
                    if (_preloadGameAsync.Value)
                        LoadPipeline.TryBeginPreload(Logger);
                }
            }
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
