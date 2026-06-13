using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Applies production-safe FastBoot settings before host/join launches.
    ///
    /// Safe (enabled): splash/config skip, zero menu settle, async GAME preload,
    /// loading-FSM nudge (boot scenes only), short splash grace.
    ///
    /// Unsafe (forced off): DevMode, ES2 tag skip/whitelist, direct GAME load,
    /// forced LoadLevel fallback, ES2 save scan at startup — desync/crash risk in MP.
    /// </summary>
    public static class FastBootConfigSeed
    {
        public const string ConfigFileName = "com.ourwintercar.wintermp.fastboot.cfg";

        private static readonly (string Key, string Value)[] ProductionBootValues =
        {
            ("Enabled", "true"),
            ("SkipSplashScreen", "true"),
            ("SkipConfigScreen", "true"),
            ("AutoLoadSave", "true"),
            ("SplashGraceSeconds", "0.3"),
            ("MenuSettleSeconds", "0"),
            ("SaveCheckTimeoutSeconds", "0.5"),
            ("ContinueStepDelaySeconds", "0"),
            ("SkipMenuLoadWaits", "true"),
            ("ForceGameLoadAfterSeconds", "0"),
            ("PreloadGameAsync", "true"),
            ("DevMode", "false"),
            ("DevDirectGameLoad", "false"),
            ("DevSkipEs2Tags", "false"),
            ("DevEs2Whitelist", "false"),
            ("DevSkipEs2ExtraPrefixes", string.Empty),
            ("LogTimings", "true"),
            ("AnalyzeEs2SaveOnStartup", "false"),
        };

        /// <summary>Merge production boot keys; creates the file if missing.</summary>
        public static string ApplyProductionProfile(string gameDir)
        {
            string configDir = Path.Combine(gameDir, "BepInEx", "config");
            Directory.CreateDirectory(configDir);
            string path = Path.Combine(configDir, ConfigFileName);

            string before = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            string after = string.IsNullOrEmpty(before) ? BuildDefaultFile() : MergeBootKeys(before);

            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                File.WriteAllText(path, after, Encoding.UTF8);
                return "Applied FastBoot production defaults (safe speed opts on, dev ES2 hacks off).";
            }

            return "FastBoot production defaults already set.";
        }

        internal static string MergeBootKeys(string configText)
        {
            var lines = configText.Replace("\r\n", "\n").Split('\n').ToList();
            var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

                string key = line.Substring(0, eq).Trim();
                for (int v = 0; v < ProductionBootValues.Length; v++)
                {
                    if (!string.Equals(key, ProductionBootValues[v].Key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    lines[i] = ProductionBootValues[v].Key + " = " + FormatValue(ProductionBootValues[v].Value);
                    applied.Add(ProductionBootValues[v].Key);
                    break;
                }
            }

            if (applied.Count == ProductionBootValues.Length)
                return string.Join(Environment.NewLine, lines).Replace("\n", Environment.NewLine);

            int bootIndex = FindBootSectionIndex(lines);
            if (bootIndex < 0)
            {
                lines.Add(string.Empty);
                lines.Add("[Boot]");
                bootIndex = lines.Count - 1;
            }

            var insertAt = bootIndex + 1;
            for (int v = 0; v < ProductionBootValues.Length; v++)
            {
                if (applied.Contains(ProductionBootValues[v].Key)) continue;
                lines.Insert(insertAt++, ProductionBootValues[v].Key + " = " + FormatValue(ProductionBootValues[v].Value));
            }

            return string.Join(Environment.NewLine, lines).Replace("\n", Environment.NewLine);
        }

        private static int FindBootSectionIndex(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"^\s*\[Boot\]\s*$", RegexOptions.IgnoreCase))
                    return i;
            }

            return -1;
        }

        private static string FormatValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return value;

            return value;
        }

        private static string BuildDefaultFile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## FastBoot settings — production profile (managed by Our Winter Car launcher)");
            sb.AppendLine("## Plugin GUID: com.ourwintercar.wintermp.fastboot");
            sb.AppendLine();
            sb.AppendLine("[Boot]");
            for (int i = 0; i < ProductionBootValues.Length; i++)
            {
                sb.Append(ProductionBootValues[i].Key);
                sb.Append(" = ");
                sb.AppendLine(FormatValue(ProductionBootValues[i].Value));
            }

            return sb.ToString();
        }
    }
}
