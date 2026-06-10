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
                if (!Directory.Exists(gameDir)) continue;

                string? exe = FindGameExe(gameDir);
                if (exe == null) continue;

                return new GameInstall(gameDir, exe, buildId);
            }

            return null;
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
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return key?.GetValue("SteamPath") as string is { Length: > 0 } path
                    ? path.Replace('/', '\\')
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> GetLibraryFolders(string steamPath)
        {
            yield return steamPath;

            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;

            string content;
            try
            {
                content = File.ReadAllText(vdf);
            }
            catch
            {
                yield break;
            }

            foreach (Match match in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\""))
            {
                string path = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path))
                    yield return path;
            }
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
