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
        /// <summary>Launches the game and returns a one-line status for the activity log.</summary>
        public static string Launch(GameInstall game, string args)
        {
            string? logLine = null;
            Platform.Current.LaunchGame(game, args, msg => logLine = msg);
            return logLine ?? "Game launched.";
        }
    }
}
