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
            "WinterMP.FastBoot.dll",
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
                    $"{Branding.ProductName} mod files are missing from the launcher. Reinstall from GitHub.");

            string modDir = Path.Combine(gameDir, "BepInEx", "plugins", "WinterMP");
            Directory.CreateDirectory(modDir);

            // All-or-nothing: stage every file to a temp name first, then swap each into place only
            // after all copies succeed. A mid-copy failure (Defender quarantine, file lock, disk full)
            // therefore can't leave the install with mixed-version DLLs (a Core/Net protocol mismatch
            // that would desync or refuse connections mid-session).
            try
            {
                foreach (string file in RequiredFiles)
                    File.Copy(Path.Combine(PayloadDir, file), Path.Combine(modDir, file + ".tmp"), overwrite: true);
            }
            catch
            {
                CleanupTemps(modDir);
                throw;
            }

            foreach (string file in RequiredFiles)
            {
                string dst = Path.Combine(modDir, file);
                File.Move(dst + ".tmp", dst, overwrite: true);
            }

            return $"Deployed {Branding.ProductName} ({LauncherVersion}) — {RequiredFiles.Length} files to {modDir}";
        }

        private static void CleanupTemps(string modDir)
        {
            foreach (string file in RequiredFiles)
            {
                string tmp = Path.Combine(modDir, file + ".tmp");
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { /* best effort — leftover .tmp is harmless */ }
            }
        }
    }
}
