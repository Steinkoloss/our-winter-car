using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Applies the launcher FastBoot speed profile before host/join launches.
    ///
    /// Enabled (all save-safe): splash/config skip, fast Continue, async GAME preload,
    /// loading-FSM nudge, host Continue without waiting for a guest.
    ///
    /// Deliberately OFF — these skip ES2 save tags during the boot load and can silently corrupt
    /// the savefile: ES2 tag skip, ES2 boot whitelist, deferred ES2 catch-up. The game loads save
    /// data lazily per-object via PlayMaker; saves are per-tag merges of the *live* value; and
    /// ES2.LoadAll cannot push values back into the world — so a skipped tag stays at its default
    /// and the next in-game save overwrites the real data. The async GAME preload is where the real
    /// boot speedup comes from and it touches no save data. These tiers remain available as opt-in
    /// dev flags in the .cfg, but the launcher never enables them.
    ///
    /// Also excluded: direct GAME load (skips ES2 entirely — crashes), ES2 save scan at startup.
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
            ("SplashGraceSeconds", "0"),
            ("MenuSettleSeconds", "0"),
            ("SaveCheckTimeoutSeconds", "0"),
            ("ContinueStepDelaySeconds", "0"),
            ("SkipMenuLoadWaits", "true"),
            ("ForceGameLoadAfterSeconds", "90"),
            ("PreloadGameAsync", "true"),
            ("DevMode", "true"),
            ("BypassHostContinueWait", "true"),
            ("DevDirectGameLoad", "false"),
            // ES2 tag skipping / boot whitelist corrupt saves (see class summary) — force OFF.
            ("DevSkipEs2Tags", "false"),
            ("DevEs2Whitelist", "false"),
            ("DevSkipEs2ExtraPrefixes", string.Empty),
            ("LogTimings", "false"),
            // Deferred ES2 catch-up is a no-op for world state (ES2.LoadAll restores nothing) and
            // only exists to "repair" the skip above; with the skip off it must stay off too.
            ("DeferredEs2Hydrate", "false"),
            ("DeferredHydrateDelaySeconds", "3"),
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
                return "Applied FastBoot save-safe startup settings.";
            }

            return "FastBoot speed profile already set.";
        }

        internal static string MergeBootKeys(string configText)
        {
            var lines = configText.Replace("\r\n", "\n").Split('\n').ToList();
            var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string currentSection = string.Empty;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                string trimmed = line.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

                // Only a managed key under [Boot] is authoritative: a same-named key in another
                // section must not be rewritten (its value would land where FastBoot won't read it)
                // nor counted as applied — otherwise the real [Boot] value is never inserted.
                if (!string.Equals(currentSection, "Boot", StringComparison.OrdinalIgnoreCase)) continue;

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
                return string.Join(Environment.NewLine, lines);

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

            return string.Join(Environment.NewLine, lines);
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
            sb.AppendLine("## FastBoot settings — speed profile (managed by Our Winter Car launcher)");
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
