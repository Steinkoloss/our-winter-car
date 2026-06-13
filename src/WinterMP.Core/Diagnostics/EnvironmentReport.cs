using System;
using System.IO;
using System.Text;
using UnityEngine;
using WinterMP.Core.Util;

namespace WinterMP.Core.Diagnostics
{
    /// <summary>
    /// Writes a full environment report to the BepInEx log and to
    /// &lt;game&gt;\WinterMP\diagnostics.log. The startup pass is lightweight
    /// (no assembly list, no Steam); the delayed pass (~30s) is complete.
    /// This is the primary data source for the M0/M1 verification runs.
    /// </summary>
    internal static class EnvironmentReport
    {
        /// <param name="phase">Label for the log.</param>
        /// <param name="includeSteam">
        /// Only true for the delayed pass: touching SteamAPI.Init during the very early
        /// boot phase (MonoBehaviour entrypoint) is a native-crash risk on this old
        /// CSteamworks-based wrapper, so the startup pass must stay Steam-free.
        /// </param>
        /// <param name="includeAssemblyList">
        /// Full <c>AppDomain</c> assembly enumeration is deferred to the delayed pass to
        /// avoid a first-frame hitch right after BepInEx chainload.
        /// </param>
        public static void Write(string phase, bool includeSteam, bool includeAssemblyList)
        {
            var sb = new StringBuilder(8 * 1024);
            sb.AppendLine($"=== WinterMP environment report ({phase}) ===");

            BootTrace.Crumb($"env({phase}): app fields");
            Try(sb, "unityVersion", () => Application.unityVersion);
            Try(sb, "productName", () => SafeApp.ProductName);
            Try(sb, "companyName", () => SafeApp.CompanyName);
            Try(sb, "gameVersion", () => SafeApp.GameVersion);
            Try(sb, "dataPath", () => Application.dataPath);
            Try(sb, "persistentDataPath", () => Application.persistentDataPath);
            Try(sb, "loadedLevel", () => Application.loadedLevelName + " (#" + Application.loadedLevel + ")");
            Try(sb, "platform", () => Application.platform.ToString());
            Try(sb, "clrVersion", () => Environment.Version.ToString());
            Try(sb, "mscorlib", () => typeof(int).Assembly.GetName().Version.ToString());
            Try(sb, "os", () => Environment.OSVersion.ToString());
            Try(sb, "commandLine", () => string.Join(" ", Environment.GetCommandLineArgs()));

            if (includeAssemblyList)
            {
                BootTrace.Crumb($"env({phase}): managed assemblies");
                sb.AppendLine("-- loaded managed assemblies:");
                try
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            var name = assembly.GetName();
                            sb.AppendLine($"   {name.Name} {name.Version}");
                        }
                        catch
                        {
                            // dynamic assemblies can refuse identity queries
                        }
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine($"   <error: {e.Message}>");
                }
            }
            else
            {
                sb.AppendLine("-- loaded managed assemblies: deferred to delayed report");
            }

            // NEVER use Process.GetCurrentProcess().Modules here: enumerating
            // modules natively crashes the game's ancient Mono 2.x x64 runtime
            // (uncatchable — kills the process before BepInEx flushes its log).
            // GetModuleHandle probes for specific names are safe.
            BootTrace.Crumb($"env({phase}): native module probe");
            sb.AppendLine("-- native steam modules (GetModuleHandle probe):");
            bool anyModule = false;
            foreach (var name in new[] { "steam_api.dll", "steam_api64.dll", "CSteamworks.dll", "steamclient.dll", "steamclient64.dll", "GameOverlayRenderer64.dll" })
            {
                if (GetModuleHandle(name) != IntPtr.Zero)
                {
                    sb.AppendLine($"   {name}");
                    anyModule = true;
                }
            }

            if (!anyModule) sb.AppendLine("   (none loaded yet)");

#if STEAMWORKS
            if (includeSteam)
            {
                BootTrace.Crumb($"env({phase}): steam diagnostics");
                Steam.SteamBootstrap.AppendDiagnostics(sb);
            }
            else
            {
                sb.AppendLine("-- steam: skipped in this phase (deferred to the delayed report)");
            }
#else
            sb.AppendLine("-- steam: transport NOT compiled in (Assembly-CSharp-firstpass.dll not found at build time)");
#endif

            BootTrace.Crumb($"env({phase}): writing report");
            string report = sb.ToString();
            WinterMPPlugin.Log.LogInfo(report);

            try
            {
                string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "diagnostics.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {report}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"Could not write diagnostics file: {e.Message}");
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private static void Try(StringBuilder sb, string name, Func<string> getter)
        {
            try
            {
                sb.AppendLine($"   {name}: {getter()}");
            }
            catch (Exception e)
            {
                sb.AppendLine($"   {name}: <error: {e.GetType().Name} {e.Message}>");
            }
        }
    }
}
