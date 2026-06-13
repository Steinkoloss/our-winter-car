namespace WinterMP.FastBoot
{
    /// <summary>Collects boot phase timestamps for the summary log line.</summary>
    internal sealed class BootTimer
    {
        public float PluginAwakeAt { get; set; }
        public float SplashAt { get; set; } = -1f;
        public float MenuAt { get; set; } = -1f;
        public float GameAt { get; set; } = -1f;
        public float ContinueAt { get; set; } = -1f;

        public void NoteContinue(float t)
        {
            if (ContinueAt < 0f) ContinueAt = t;
        }

        public void NoteSplash(float t)
        {
            if (SplashAt < 0f) SplashAt = t;
        }

        public void NoteMenu(float t)
        {
            if (MenuAt < 0f) MenuAt = t;
        }

        public void NoteGame(float t)
        {
            if (GameAt < 0f) GameAt = t;
        }

        public string FormatSummary()
        {
            string splash = SplashAt >= 0f ? $"{SplashAt:0.0}s" : "?";
            string menu = MenuAt >= 0f ? $"{MenuAt:0.0}s" : "?";
            string game = GameAt >= 0f ? $"{GameAt:0.0}s" : "?";
            string splashToMenu = SplashAt >= 0f && MenuAt >= 0f
                ? $", splash→menu {MenuAt - SplashAt:0.0}s"
                : string.Empty;
            string menuToGame = MenuAt >= 0f && GameAt >= 0f
                ? $", menu→GAME {GameAt - MenuAt:0.0}s"
                : string.Empty;
            string continueWait = ContinueAt >= 0f && GameAt >= 0f
                ? $", Continue→GAME {GameAt - ContinueAt:0.0}s"
                : string.Empty;
            return $"splash {splash}{splashToMenu} -> menu {menu}{menuToGame}{continueWait} -> GAME {game} (plugin Awake {PluginAwakeAt:0.0}s)";
        }
    }
}
