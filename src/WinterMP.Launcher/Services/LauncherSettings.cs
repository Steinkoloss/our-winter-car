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

        public int DisplayWidth { get; set; } = UnityDisplayPrefs.DefaultWidth;
        public int DisplayHeight { get; set; } = UnityDisplayPrefs.DefaultHeight;
        public bool Windowed { get; set; } = true;
        public int GraphicsQuality { get; set; } = UnityDisplayPrefs.DefaultQuality;
        public int MonitorIndex { get; set; } = UnityDisplayPrefs.DefaultMonitor;
        /// <summary>When false, display fields are seeded from the game's registry prefs on first load.</summary>
        public bool DisplaySettingsSaved { get; set; }

        public static LauncherSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return SeedFromRegistry(new LauncherSettings());
                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
                if (!settings.DisplaySettingsSaved)
                    SeedFromRegistry(settings);
                return settings;
            }
            catch
            {
                return SeedFromRegistry(new LauncherSettings());
            }
        }

        private static LauncherSettings SeedFromRegistry(LauncherSettings settings)
        {
            if (!UnityDisplayPrefs.TryRead(
                    out int width,
                    out int height,
                    out bool windowed,
                    out int quality,
                    out int monitor))
            {
                return settings;
            }

            settings.DisplayWidth = width;
            settings.DisplayHeight = height;
            settings.Windowed = windowed;
            settings.GraphicsQuality = quality;
            settings.MonitorIndex = monitor;
            return settings;
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
