namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Everything the launcher does that differs between Windows and Linux (Steam/Proton):
    /// Steam discovery, launching the game so Steamworks P2P initializes, locating the save
    /// folder, and opening files/URLs in the desktop shell. The rest of the launcher is
    /// platform-neutral and talks to <see cref="Platform.Current"/>.
    /// </summary>
    public interface IPlatformIntegration
    {
        /// <summary>The Steam root (the directory that contains <c>steamapps</c>), or null.</summary>
        string? FindSteamRoot();

        /// <summary>True when the Steam client process is running.</summary>
        bool IsSteamRunning();

        /// <summary>
        /// Launch My Winter Car with the given args (e.g. <c>-wintermp host -wintermp-fast</c>).
        /// Prefers launching through Steam so Steamworks initializes with the user's identity
        /// and friends list — that is what host/join over Steam P2P needs.
        /// </summary>
        void LaunchGame(GameInstall game, string launchArgs, Action<string> log);

        /// <summary>
        /// The game's save folder (<c>Amistech/My Winter Car</c>), or null when it can't be
        /// located. On Linux this lives inside the Proton prefix under <c>compatdata</c>.
        /// </summary>
        string? GetGameSaveDir();

        /// <summary>Open a file, folder, or URL with the OS default handler.</summary>
        void OpenInShell(string pathOrUrl);

        /// <summary>
        /// Whether the launcher can replace itself in place (the Windows Inno installer).
        /// False on Linux, where the launcher is updated via the package/tarball it shipped in.
        /// </summary>
        bool SupportsLauncherSelfUpdate { get; }
    }

    /// <summary>Resolves the platform integration once, by OS.</summary>
    public static class Platform
    {
        public static IPlatformIntegration Current { get; } =
            OperatingSystem.IsWindows() ? new WindowsPlatform() : new LinuxPlatform();
    }
}
