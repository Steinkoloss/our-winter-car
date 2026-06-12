using System.IO;
using System.Text.Json;

namespace WinterMP.Launcher.Services
{
    public sealed class LauncherSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterMP", "launcher-settings.json");

        public bool SeenWelcome { get; set; }
        /// <summary>Hide the update banner for this release tag until a newer one ships.</summary>
        public string? DismissedUpdateTag { get; set; }
        public DateTime? LastUpdateCheckUtc { get; set; }
        /// <summary>GitHub PAT with repo read access — required for updates while the repo is private.</summary>
        public string? GitHubToken { get; set; }
        /// <summary>Manual override when Steam library detection fails.</summary>
        public string? CustomGameDir { get; set; }

        public static LauncherSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new LauncherSettings();
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            }
            catch
            {
                return new LauncherSettings();
            }
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
