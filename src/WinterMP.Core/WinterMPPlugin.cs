using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class WinterMPPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log = null!;
        internal static ConfigEntry<string> PlayerNameOverride = null!;
        internal static ConfigEntry<bool> DevKeysEnabled = null!;

        static WinterMPPlugin()
        {
        }

        private void Awake()
        {
            Log = Logger;
            Util.InstanceLogRedirect.TryConfigure();
            Util.BootTrace.Crumb("Awake begin");

            // Breadcrumbs survive native crashes (BepInEx's disk log is buffered).
            // Do NOT touch Steam in here — the MonoBehaviour entrypoint runs before
            // the game initializes its own Steam integration (native-crash risk on
            // the old CSteamworks wrapper).
            Util.BootTrace.Crumb("Awake step 1: binding config");
            PlayerNameOverride = Config.Bind(
                "General", "PlayerName", string.Empty,
                "Display name shown to other players. Empty = use Steam persona name (or OS user name without Steam).");
            DevKeysEnabled = Config.Bind(
                "Development", "DevKeysEnabled", true,
                "Enable development hotkeys (F8 = start loopback dev session).");

            Util.BootTrace.Crumb("Awake step 2: parsing command line");
            var launch = LaunchOptions.FromCommandLine(Environment.GetCommandLineArgs());
            Util.BootTrace.Crumb("Awake step 2b: mode=" + launch.Mode);

            Util.BootTrace.Crumb("Awake step 3: creating WinterMP GameObject");
            var root = new GameObject("WinterMP");
            root.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(root);

            Util.BootTrace.Crumb("Awake step 5: adding DiagnosticsTicker");
            root.AddComponent<Diagnostics.DiagnosticsTicker>();
            Util.BootTrace.Crumb("Awake step 6: adding SessionManager");
            var session = root.AddComponent<SessionManager>();
            root.AddComponent<UI.MainMenuHostGate>();
            root.AddComponent<UI.MainMenuJoinBrowser>();
            root.AddComponent<Sync.PlayerSyncManager>();
            root.AddComponent<UI.GuestSpawnPrompt>();
            root.AddComponent<Sync.SleepConsentManager>();
            root.AddComponent<UI.SleepConsentPrompt>();
            root.AddComponent<Sync.DeathSyncManager>();
            var worldSync = root.AddComponent<Sync.WorldSyncManager>();
            worldSync.Configure(launch);
            root.AddComponent<Sync.PassengerController>();
            Util.BootTrace.Crumb("Awake step 7: SessionManager.Initialize");
            session.Initialize(launch);

            Util.BootTrace.Crumb("Awake done");

            if (launch.Mode == LaunchMode.HostLocal)
                TryReleaseHostLocalMutex();

            Log.LogInfo($"WinterMP {MyPluginInfo.PLUGIN_VERSION} loaded (mode: {launch.Mode}, protocol v{WinterMP.Net.ProtocolInfo.Version}).");
        }

        private static void TryReleaseHostLocalMutex()
        {
            if (Util.SingleInstanceUnlocker.Release())
                Log.LogInfo("HostLocal: single-instance mutex released (second instance can start).");
            else
                Log.LogWarning("HostLocal: single-instance mutex not found to release.");

            // Always signal ready — on Linux/Proton the mutex doesn't exist but the
            // ready flag is still needed for the shell script to launch the guest.
            Util.HostLocalReadySignal.MarkReady();
        }
    }
}
