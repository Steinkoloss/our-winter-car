using System.Diagnostics;

namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Linux Steam/Proton integration. My Winter Car is a Windows build run through Proton,
    /// so the game is launched via <c>steam -applaunch</c> (Steam sets up the Proton prefix
    /// and Steamworks identity) and the save folder lives inside that prefix's compatdata.
    /// </summary>
    internal sealed class LinuxPlatform : IPlatformIntegration
    {
        // Mirrors tools/linux-common.sh find_steam_root.
        private static readonly string[] SteamRootCandidates =
        {
            Environment.GetEnvironmentVariable("STEAM_DIR") ?? string.Empty,
            Combine("~/.steam/root"),
            Combine("~/.steam/steam"),
            Combine("~/.local/share/Steam"),
            Combine("~/.var/app/com.valvesoftware.Steam/.local/share/Steam"),
        };

        public bool SupportsLauncherSelfUpdate => false;

        public string? FindSteamRoot()
        {
            foreach (string candidate in SteamRootCandidates)
            {
                if (!string.IsNullOrEmpty(candidate) && Directory.Exists(Path.Combine(candidate, "steamapps")))
                    return candidate;
            }

            return null;
        }

        public bool IsSteamRunning()
        {
            try
            {
                return Process.GetProcessesByName("steam").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public void LaunchGame(GameInstall game, string launchArgs, Action<string> log)
        {
            (string fileName, string argPrefix)? steam = FindSteamLauncher();
            if (steam == null)
            {
                log("Steam not found on PATH. Start Steam and launch My Winter Car once so Proton is set up, then retry.");
                return;
            }

            // Always go through Steam on Linux: the game is a Windows .exe that needs Proton,
            // and Steamworks P2P only works when Steam launched the process.
            var psi = new ProcessStartInfo
            {
                FileName = steam.Value.fileName,
                Arguments = $"{steam.Value.argPrefix}-applaunch {GameLocator.AppId} {launchArgs}".Trim(),
                UseShellExecute = false,
            };
            Process.Start(psi);
            log("Launching via Steam (-applaunch) under Proton.");
        }

        public string? GetGameSaveDir()
        {
            const string relative =
                "pfx/drive_c/users/steamuser/AppData/LocalLow/Amistech/My Winter Car";

            string? firstGuess = null;
            foreach (string root in GameLocator.SteamLibraryRoots())
            {
                string candidate = Path.Combine(root, "steamapps", "compatdata", GameLocator.AppId, relative);
                firstGuess ??= candidate;
                if (Directory.Exists(candidate))
                    return candidate;
            }

            // Not created yet (game never launched) — return the most likely path so a
            // backup/restore can still create it under the primary library.
            return firstGuess;
        }

        public void OpenInShell(string pathOrUrl)
        {
            // ArgumentList (not Arguments) so a path with spaces — e.g. ".../common/My Winter Car" —
            // reaches xdg-open as one argv entry instead of being split on whitespace.
            var psi = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
            psi.ArgumentList.Add(pathOrUrl);
            Process.Start(psi);
        }

        /// <summary>Native <c>steam</c> on PATH, else Flatpak Steam, else null.</summary>
        private static (string fileName, string argPrefix)? FindSteamLauncher()
        {
            string? onPath = FindOnPath("steam");
            if (onPath != null)
                return (onPath, string.Empty);

            foreach (string fixedPath in new[] { "/usr/bin/steam", "/usr/games/steam", "/bin/steam" })
            {
                if (File.Exists(fixedPath))
                    return (fixedPath, string.Empty);
            }

            string flatpakSteam = Combine("~/.var/app/com.valvesoftware.Steam");
            if (Directory.Exists(flatpakSteam) && FindOnPath("flatpak") is { } flatpak)
                return (flatpak, "run com.valvesoftware.Steam ");

            return null;
        }

        private static string? FindOnPath(string exe)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string full = Path.Combine(dir, exe);
                if (File.Exists(full))
                    return full;
            }

            return null;
        }

        private static string Combine(string tildePath) => tildePath.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), tildePath[2..])
            : tildePath;
    }
}
