
namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Headless install path: the Inno Setup installer (Windows) and automation scripts
    /// pass <c>--install-mod</c> to run a mod install before any UI is shown.
    /// On non-Windows the dialog boxes are replaced by console output — the Avalonia
    /// app isn't started yet when this runs, so GUI dialogs aren't available.
    /// </summary>
    public static class CliInstallRunner
    {
        public const int ExitOk = 0;
        public const int ExitGameNotFound = 1;
        public const int ExitInstallFailed = 2;
        public const int ExitPayloadMissing = 3;

        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterMP", "last-install.log");

        /// <returns>True when args were handled and the process should exit.</returns>
        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = ExitOk;
            if (!args.Contains("--install-mod", StringComparer.OrdinalIgnoreCase))
                return false;

            bool silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
            string? gameDirArg = ReadGameDirArg(args);
            var log = new List<string> { $"[{DateTime.Now:u}] {Branding.ProductName} mod install" };

            try
            {
                if (!ModPayload.PayloadPresent())
                {
                    log.Add("ERROR: mod payload missing from launcher folder.");
                    WriteLog(log);
                    if (!silent)
                        Console.Error.WriteLine($"{Branding.ProductName} mod files are missing. Reinstall from GitHub.");

                    exitCode = ExitPayloadMissing;
                    return true;
                }

                var settings = LauncherSettings.Load();
                if (!string.IsNullOrWhiteSpace(gameDirArg))
                    settings.CustomGameDir = gameDirArg.Trim();

                var game = GameLocator.FindInstall(settings.CustomGameDir);
                if (game == null)
                {
                    log.Add("ERROR: My Winter Car not found via Steam.");
                    if (!string.IsNullOrWhiteSpace(settings.CustomGameDir))
                        log.Add($"Custom path tried: {settings.CustomGameDir}");
                    WriteLog(log);

                    if (!silent)
                        Console.Error.WriteLine(
                            "My Winter Car was not found. " +
                            $"Install the game via Steam, or open {Branding.LauncherWindowTitle} → Settings and browse to your game folder.");

                    exitCode = ExitGameNotFound;
                    return true;
                }

                log.Add($"Game: {game.GameDir} (build {game.BuildId ?? "?"})");
                string result = BepInExInstaller.InstallOrRepair(game.GameDir);
                log.Add(result);
                WriteLog(log);

                if (!silent)
                    Console.WriteLine(result);

                exitCode = ExitOk;
                return true;
            }
            catch (Exception ex)
            {
                log.Add($"ERROR: {ex.Message}");
                WriteLog(log);

                if (!silent)
                    Console.Error.WriteLine(ex.Message);

                exitCode = ExitInstallFailed;
                return true;
            }
        }

        private static string? ReadGameDirArg(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--game-dir", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        private static void WriteLog(IEnumerable<string> lines)
        {
            string? dir = Path.GetDirectoryName(LogPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllLines(LogPath, lines);
        }
    }
}
