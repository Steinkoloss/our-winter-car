using System.Diagnostics;

namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Cross-platform environment helpers. My Winter Car is a Windows game; on Linux it
    /// runs through Steam Proton (a Wine prefix), so the Steam layout, the save folder and
    /// the way we open files/URLs all differ per OS. Centralizing them here keeps the rest
    /// of the launcher OS-agnostic — the old WPF build assumed Windows everywhere.
    /// </summary>
    public static class PlatformEnv
    {
        public static bool IsWindows => OperatingSystem.IsWindows();
        public static bool IsLinux => OperatingSystem.IsLinux();
        public static bool IsMac => OperatingSystem.IsMacOS();

        public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>Candidate Steam install roots (Linux/macOS), most-specific first.</summary>
        public static IEnumerable<string> SteamRoots()
        {
            string? env = Environment.GetEnvironmentVariable("STEAM_DIR");
            if (!string.IsNullOrWhiteSpace(env))
                yield return env;

            if (IsLinux)
            {
                yield return Path.Combine(Home, ".steam", "root");
                yield return Path.Combine(Home, ".steam", "steam");
                yield return Path.Combine(Home, ".local", "share", "Steam");
                yield return Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
            }
            else if (IsMac)
            {
                yield return Path.Combine(Home, "Library", "Application Support", "Steam");
            }
        }

        /// <summary>First Steam root that actually contains a steamapps folder, or null.</summary>
        public static string? FindSteamRoot()
        {
            foreach (string root in SteamRoots())
            {
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(Path.Combine(root, "steamapps")))
                    return root;
            }
            return null;
        }

        /// <summary>The Proton compatdata prefix for MWC (where the Windows user profile lives).</summary>
        public static string? ProtonPrefix()
        {
            string? root = FindSteamRoot();
            if (root == null) return null;
            string pfx = Path.Combine(root, "steamapps", "compatdata", GameLocator.AppId, "pfx");
            return Directory.Exists(pfx) ? pfx : null;
        }

        /// <summary>Newest available Proton's <c>proton</c> script, or null when none is installed.</summary>
        public static string? FindProton()
        {
            string? root = FindSteamRoot();
            if (root == null) return null;

            string common = Path.Combine(root, "steamapps", "common");
            if (!Directory.Exists(common)) return null;

            // Experimental wins; otherwise highest-versioned "Proton - N.M" by name order.
            string experimental = Path.Combine(common, "Proton - Experimental", "proton");
            if (File.Exists(experimental)) return experimental;

            string? best = null;
            foreach (string dir in Directory.GetDirectories(common, "Proton*"))
            {
                string proton = Path.Combine(dir, "proton");
                if (!File.Exists(proton)) continue;
                if (best == null || string.CompareOrdinal(dir, Path.GetDirectoryName(best)) > 0)
                    best = proton;
            }
            return best;
        }

        /// <summary>
        /// The game's LocalLow save folder. On Linux it lives inside the Proton prefix
        /// (drive_c/users/steamuser/AppData/LocalLow), not the native home directory.
        /// </summary>
        public static string SaveDir()
        {
            if (IsWindows)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "Amistech", "My Winter Car");
            }

            string? pfx = ProtonPrefix();
            string localLow = pfx != null
                ? Path.Combine(pfx, "drive_c", "users", "steamuser", "AppData", "LocalLow")
                : Path.Combine(Home, ".steam"); // unreachable in practice; keeps a non-null path
            return Path.Combine(localLow, "Amistech", "My Winter Car");
        }

        /// <summary>Per-user app-data root for launcher state (settings, backups, logs).</summary>
        public static string AppDataDir() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterMP");

        public static void OpenFolder(string path)
        {
            try
            {
                if (IsWindows)
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    return;
                }

                var psi = new ProcessStartInfo(IsMac ? "open" : "xdg-open");
                psi.ArgumentList.Add(path);
                Process.Start(psi);
            }
            catch
            {
                // Opening a file manager is best-effort; never crash the launcher over it.
            }
        }

        public static void OpenUrl(string url)
        {
            try
            {
                if (IsWindows)
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    return;
                }

                var psi = new ProcessStartInfo(IsMac ? "open" : "xdg-open");
                psi.ArgumentList.Add(url);
                Process.Start(psi);
            }
            catch
            {
            }
        }
    }
}
