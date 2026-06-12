namespace WinterMP.Launcher.Services
{
    public sealed class LauncherInfoSnapshot
    {
        public string GameStatus { get; init; } = "Searching…";
        public string? GameDirTooltip { get; init; }
        public string BepInExStatus { get; init; } = "—";
        public string ModStatus { get; init; } = "—";
        public string BackupStatus { get; init; } = "—";
        public string BuildStatus { get; init; } = "—";
        public string UpdateStatus { get; init; } = "Not checked yet";
    }
}
