using System.IO;
using System.Reflection;

namespace WinterMP.Launcher.Services
{
    /// <summary>Copies bundled WinterMP plugin DLLs into the game folder.</summary>
    public static class ModPayload
    {
        public static readonly string[] RequiredFiles =
        {
            "WinterMP.Core.dll",
            "WinterMP.Net.dll",
            "sync-catalog.json",
            "wintermp-compat.json",
        };

        public static string PayloadDir
        {
            get
            {
                string nextToExe = Path.Combine(AppContext.BaseDirectory, "payload");
                if (Directory.Exists(nextToExe)) return nextToExe;

                string dev = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..",
                    "WinterMP.Core", "bin", "Release", "net35"));
                return Directory.Exists(dev) ? dev : nextToExe;
            }
        }

        public static string LauncherVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        public static bool PayloadPresent()
        {
            foreach (string file in RequiredFiles)
            {
                if (!File.Exists(Path.Combine(PayloadDir, file)))
                    return false;
            }

            return true;
        }

        public static string Deploy(string gameDir)
        {
            if (!PayloadPresent())
                throw new InvalidOperationException(
                    "WinterMP mod files are missing from the launcher install. Reinstall WinterMP.");

            string modDir = Path.Combine(gameDir, "BepInEx", "plugins", "WinterMP");
            Directory.CreateDirectory(modDir);

            int copied = 0;
            foreach (string file in RequiredFiles)
            {
                string src = Path.Combine(PayloadDir, file);
                string dst = Path.Combine(modDir, file);
                File.Copy(src, dst, overwrite: true);
                copied++;
            }

            return $"Deployed WinterMP ({LauncherVersion}) — {copied} files to {modDir}";
        }
    }
}
