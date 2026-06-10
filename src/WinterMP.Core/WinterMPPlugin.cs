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
            Util.BootTrace.Crumb("plugin type cctor");
        }

        private void Awake()
        {
            Log = Logger;

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

            Util.BootTrace.Crumb("Awake step 3: startup environment report");
            Diagnostics.EnvironmentReport.Write("startup", includeSteam: false);

            Util.BootTrace.Crumb("Awake step 4: creating WinterMP GameObject");
            var root = new GameObject("WinterMP");
            root.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(root);

            Util.BootTrace.Crumb("Awake step 5: adding DiagnosticsTicker");
            root.AddComponent<Diagnostics.DiagnosticsTicker>();
            Util.BootTrace.Crumb("Awake step 6: adding SessionManager");
            var session = root.AddComponent<SessionManager>();
            root.AddComponent<Sync.PlayerSyncManager>();
            var worldSync = root.AddComponent<Sync.WorldSyncManager>();
            worldSync.Configure(launch);
            root.AddComponent<Sync.PassengerController>();
            Util.BootTrace.Crumb("Awake step 7: SessionManager.Initialize");
            session.Initialize(launch);

            Util.BootTrace.Crumb("Awake done");
            Log.LogInfo($"WinterMP {MyPluginInfo.PLUGIN_VERSION} loaded (mode: {launch.Mode}, protocol v{WinterMP.Net.ProtocolInfo.Version}).");
        }
    }
}
