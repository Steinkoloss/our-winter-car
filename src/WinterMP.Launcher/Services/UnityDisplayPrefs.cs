using Microsoft.Win32;

namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Skips Unity 5's native ScreenSelector ("Play!" config dialog) by passing
    /// <c>-screen-*</c> command-line args at launch. Registry PlayerPrefs are also
    /// written so settings persist for non-launcher starts. BepInEx loads too late to
    /// dismiss the native dialog in-process.
    /// Key names verified against My Winter Car (Unity 5.0, company Amistech).
    /// </summary>
    public static class UnityDisplayPrefs
    {
        public const int DefaultWidth = 1280;
        public const int DefaultHeight = 720;
        public const int DefaultFullscreen = 0;
        public const int DefaultQuality = 5;
        public const int DefaultMonitor = 0;

        private const string RegistrySubKey = @"Software\Amistech\My Winter Car";

        private const string WidthKey = "Screenmanager Resolution Width_h182942802";
        private const string HeightKey = "Screenmanager Resolution Height_h2627697771";
        private const string FullscreenKey = "Screenmanager Is Fullscreen mode_h3981298716";
        private const string QualityKey = "UnityGraphicsQuality_h1669003810";
        private const string MonitorKey = "UnitySelectMonitor_h17969598";

        /// <summary>
        /// Prepends <c>-screen-*</c> args from launcher settings so Unity skips ScreenSelector.
        /// </summary>
        public static string WithScreenArgs(LauncherSettings settings, string? existingArgs)
        {
            string screen = BuildScreenArgs(settings);
            if (string.IsNullOrWhiteSpace(existingArgs))
                return screen;
            return screen + " " + existingArgs.Trim();
        }

        public static string BuildScreenArgs(LauncherSettings settings)
        {
            int fullscreen = settings.Windowed ? 0 : 1;
            int qualityIndex = Math.Clamp(
                settings.GraphicsQuality,
                0,
                MwcDisplayOptions.QualityNames.Length - 1);
            string quality = MwcDisplayOptions.QualityNames[qualityIndex];

            return string.Join(" ",
                $"-screen-fullscreen {fullscreen}",
                $"-screen-width {settings.DisplayWidth}",
                $"-screen-height {settings.DisplayHeight}",
                $"-screen-quality {QuoteCommandLineArg(quality)}");
        }

        public static void Apply(LauncherSettings settings, string? commandLineArgs = null)
        {
            int width = settings.DisplayWidth;
            int height = settings.DisplayHeight;
            int fullscreen = settings.Windowed ? 0 : 1;
            int quality = settings.GraphicsQuality;
            int monitor = settings.MonitorIndex;
            ParseScreenArgs(commandLineArgs, ref width, ref height, ref fullscreen);
            Write(width, height, fullscreen, quality, monitor);
        }

        public static void ApplyForCommandLine(string? commandLineArgs)
        {
            int width = DefaultWidth;
            int height = DefaultHeight;
            int fullscreen = DefaultFullscreen;
            ParseScreenArgs(commandLineArgs, ref width, ref height, ref fullscreen);
            Write(width, height, fullscreen, DefaultQuality, DefaultMonitor);
        }

        public static bool TryRead(out int width, out int height, out bool windowed, out int quality, out int monitor)
        {
            width = DefaultWidth;
            height = DefaultHeight;
            windowed = DefaultFullscreen == 0;
            quality = DefaultQuality;
            monitor = DefaultMonitor;

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistrySubKey, false);
            if (key == null) return false;

            if (!TryReadDword(key, WidthKey, out width)) return false;
            if (!TryReadDword(key, HeightKey, out height)) return false;
            if (!TryReadDword(key, FullscreenKey, out int fullscreen)) return false;
            windowed = fullscreen == 0;
            if (!TryReadDword(key, QualityKey, out quality)) return false;
            if (!TryReadDword(key, MonitorKey, out monitor)) return false;
            return true;
        }

        public static void Write(int width, int height, int fullscreen, int quality, int monitor)
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, true);
            if (key == null) return;

            key.SetValue(WidthKey, width, RegistryValueKind.DWord);
            key.SetValue(HeightKey, height, RegistryValueKind.DWord);
            key.SetValue(FullscreenKey, fullscreen, RegistryValueKind.DWord);
            key.SetValue(QualityKey, quality, RegistryValueKind.DWord);
            key.SetValue(MonitorKey, monitor, RegistryValueKind.DWord);
        }

        internal static void ParseScreenArgs(
            string? commandLineArgs,
            ref int width,
            ref int height,
            ref int fullscreen)
        {
            if (string.IsNullOrWhiteSpace(commandLineArgs)) return;

            string[] parts = commandLineArgs.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                if (i + 1 >= parts.Length) continue;

                if (string.Equals(parts[i], "-screen-width", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[i + 1], out int w))
                {
                    width = w;
                }
                else if (string.Equals(parts[i], "-screen-height", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(parts[i + 1], out int h))
                {
                    height = h;
                }
                else if (string.Equals(parts[i], "-screen-fullscreen", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(parts[i + 1], out int fs))
                {
                    fullscreen = fs;
                }
            }
        }

        private static bool TryReadDword(RegistryKey key, string name, out int value)
        {
            value = 0;
            object? raw = key.GetValue(name);
            if (raw is int i)
            {
                value = i;
                return true;
            }

            return false;
        }

        private static string QuoteCommandLineArg(string value)
        {
            if (value.IndexOfAny(new[] { ' ', '(', ')' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
