namespace WinterMP.FastBoot
{
    /// <summary>
    /// Level names and boot-only automation rules. FastBoot must never drive PlayMaker
    /// FSMs on <see cref="Game"/> — v0.1.1 scanned every "Button" FSM in the loaded
    /// scene (including GAME) every 0.15s and spammed CLICK/DOWN/OVER events, which
    /// broke movement, menus, and saves.
    /// </summary>
    internal static class BootPhase
    {
        public const string Splash = "SplashScreen";
        public const string Menu = "MainMenu";
        public const string Game = "GAME";

        public static bool IsGame(string level) => level == Game;

        /// <summary>Only the splash scene may receive setup-button automation.</summary>
        public static bool AllowsSetupSkip(string level) => level == Splash;

        /// <summary>Continue automation is main-menu only.</summary>
        public static bool AllowsAutoLoad(string level) => level == Menu;
    }
}
