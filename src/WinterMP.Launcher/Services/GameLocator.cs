using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

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
            string? steamPath = GetSteamPath();
            if (steamPath == null) return null;

            foreach (string library in GetLibraryFolders(steamPath))
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

        /// <summary>Full path to steam.exe, or null when Steam is not installed.</summary>
        public static string? FindSteamExe()
        {
            string? steamPath = GetSteamPath();
            if (steamPath == null) return null;

            string exe = Path.Combine(steamPath, "steam.exe");
            return File.Exists(exe) ? exe : null;
        }

        private static string? GetSteamPath()
        {
            string?[] candidates =
            {
                ReadRegistrySteamPath(Registry.CurrentUser, @"Software\Valve\Steam"),
                ReadRegistrySteamPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
                ReadRegistrySteamPath(Registry.LocalMachine, @"SOFTWARE\Valve\Steam"),
            };

            foreach (string? path in candidates)
            {
                if (path != null && Directory.Exists(path))
                    return path;
            }

            return null;
        }

        private static string? ReadRegistrySteamPath(RegistryKey root, string subKey)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                return key?.GetValue("SteamPath") as string is { Length: > 0 } path
                    ? UnescapeVdfPath(path)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> GetLibraryFolders(string steamPath)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in EnumerateExistingFolder(steamPath))
            {
                if (seen.Add(folder))
                    yield return folder;
            }

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
                    foreach (string folder in EnumerateExistingFolder(match.Groups[1].Value))
                    {
                        if (seen.Add(folder))
                            yield return folder;
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateExistingFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) yield break;

            path = UnescapeVdfPath(path);
            if (Directory.Exists(path))
                yield return path;
        }

        private static string UnescapeVdfPath(string path)
        {
            path = path.Replace('/', '\\').Trim();
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
