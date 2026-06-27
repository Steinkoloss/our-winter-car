using System.Text.RegularExpressions;

namespace WinterMP.Launcher.Services
{
    public sealed record GameInstall(string GameDir, string ExePath, string? BuildId);

    /// <summary>Finds the My Winter Car install by walking Steam's library folders.</summary>
    public static class GameLocator
    {
        public const string AppId = "4164420";

        public static GameInstall? FindInstall(string? customGameDir = null)
        {
            if (!string.IsNullOrWhiteSpace(customGameDir))
            {
                var manual = TryFromDirectory(customGameDir.Trim());
                if (manual != null) return manual;
            }

            return FindSteamInstall();
        }

        public static GameInstall? FindSteamInstall()
        {
            foreach (string library in SteamLibraryRoots())
            {
                string manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
                if (!File.Exists(manifest)) continue;

                string acf = File.ReadAllText(manifest);
                string? installDir = MatchValue(acf, "installdir");
                string? buildId = MatchValue(acf, "buildid");
                if (installDir == null) continue;

                string gameDir = Path.Combine(library, "steamapps", "common", installDir);
                var install = TryFromDirectory(gameDir, buildId);
                if (install != null) return install;
            }

            return null;
        }

        public static GameInstall? TryFromDirectory(string gameDir, string? buildId = null)
        {
            if (!Directory.Exists(gameDir)) return null;

            string? exe = FindGameExe(gameDir);
            if (exe == null) return null;

            return new GameInstall(gameDir, exe, buildId);
        }

        /// <summary>
        /// Every Steam library root (the directory holding <c>steamapps</c>): the Steam root
        /// itself plus the paths listed in libraryfolders.vdf. Same VDF format on Windows and
        /// Linux, so only the root discovery is platform-specific.
        /// </summary>
        public static IEnumerable<string> SteamLibraryRoots()
        {
            string? steamPath = Platform.Current.FindSteamRoot();
            if (steamPath == null) yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(steamPath) && seen.Add(steamPath))
                yield return steamPath;

            string steamApps = Path.Combine(steamPath, "steamapps");
            foreach (string vdfName in new[] { "libraryfolders.vdf", "libraryfolder.vdf" })
            {
                string vdf = Path.Combine(steamApps, vdfName);
                if (!File.Exists(vdf)) continue;

                string content;
                try
                {
                    content = File.ReadAllText(vdf);
                }
                catch
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\""))
                {
                    string folder = UnescapeVdfPath(match.Groups[1].Value);
                    if (Directory.Exists(folder) && seen.Add(folder))
                        yield return folder;
                }
            }
        }

        private static string UnescapeVdfPath(string path)
        {
            // VDF escapes backslashes on Windows ("C:\\Games"); Linux paths are plain "/home/...".
            path = path.Trim();
            while (path.Contains(@"\\", StringComparison.Ordinal))
                path = path.Replace(@"\\", @"\");
            return path;
        }

        private static string? MatchValue(string acf, string key)
        {
            var match = Regex.Match(acf, $"\"{key}\"\\s+\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string? FindGameExe(string gameDir)
        {
            // Unity convention: <Name>.exe next to <Name>_Data. Most robust way to find
            // the real game exe regardless of its exact name.
            foreach (string dataDir in Directory.GetDirectories(gameDir, "*_Data"))
            {
                string exe = Path.Combine(gameDir, Path.GetFileName(dataDir)[..^"_Data".Length] + ".exe");
                if (File.Exists(exe)) return exe;
            }

            foreach (string exe in Directory.GetFiles(gameDir, "*.exe"))
            {
                string name = Path.GetFileName(exe);
                if (!name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
                    return exe;
            }

            return null;
        }
    }
}
