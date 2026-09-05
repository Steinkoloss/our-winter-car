namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Thin facade over <see cref="IPlatformIntegration.LaunchGame"/>. The platform
    /// implementations (<see cref="WindowsPlatform"/>, <see cref="LinuxPlatform"/>) hold
    /// the OS-specific launch logic; callers just call <see cref="Launch"/> and log the
    /// returned status line.
    /// </summary>
    public static class GameLauncher
    {
        public static bool IsGameRunning()
        {
            foreach (string name in new[] { "mywintercar", "mywintercar.exe" })
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(name);
                try { if (processes.Length > 0) return true; }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            return false;
        }

        public static void RequireGameClosed()
        {
            if (IsGameRunning()) throw new InvalidOperationException("Close My Winter Car before installing, restoring saves or starting another session.");
        }

        /// <summary>Launches the game and returns a one-line status for the activity log.</summary>
        public static string Launch(GameInstall game, string args)
        {
            RequireGameClosed();
            string? logLine = null;
            Platform.Current.LaunchGame(game, args, msg => logLine = msg);
            return logLine ?? "Game launched.";
        }
    }
}
