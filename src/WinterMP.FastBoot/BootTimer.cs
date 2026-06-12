namespace WinterMP.FastBoot
{
    /// <summary>Collects boot phase timestamps for the summary log line.</summary>
    internal sealed class BootTimer
    {
        public float PluginAwakeAt { get; set; }
        public float SplashAt { get; set; } = -1f;
        public float MenuAt { get; set; } = -1f;
        public float GameAt { get; set; } = -1f;

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
            return $"splash {splash} -> menu {menu} -> GAME {game} (plugin Awake {PluginAwakeAt:0.0}s)";
        }
    }
}
