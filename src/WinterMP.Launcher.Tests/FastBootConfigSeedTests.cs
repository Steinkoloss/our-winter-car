using System.Collections.Generic;
using System.IO;
using WinterMP.Launcher.Services;
using Xunit;

namespace WinterMP.Launcher.Tests
{
    /// <summary>
    /// Guards the FastBoot production profile against a regression that would re-enable the
    /// save-corrupting ES2 tiers, and verifies the merge only ever rewrites managed [Boot] keys.
    /// </summary>
    public sealed class FastBootConfigSeedTests
    {
        private static string NewGameDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "wmp-fastboot-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string ConfigPath(string gameDir) =>
            Path.Combine(gameDir, "BepInEx", "config", FastBootConfigSeed.ConfigFileName);

        private static Dictionary<string, string> ReadBootSection(string path) =>
            ReadSection(path, "Boot");

        // Parse "Key = Value" lines under [sectionName] from a written cfg.
        private static Dictionary<string, string> ReadSection(string path, string sectionName)
        {
            var values = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                if (line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(section, sectionName, System.StringComparison.OrdinalIgnoreCase)) continue;

                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            return values;
        }

        [Fact]
        public void FreshProfile_DisablesSaveUnsafeTiers_AndEnablesSafeTiers()
        {
            string gameDir = NewGameDir();
            try
            {
                FastBootConfigSeed.ApplyProductionProfile(gameDir);
                var boot = ReadBootSection(ConfigPath(gameDir));

                // Save-unsafe tiers must never ship enabled — they skip ES2 tags with no real recovery.
                Assert.Equal("false", boot["DevSkipEs2Tags"]);
                Assert.Equal("false", boot["DevEs2Whitelist"]);
                Assert.Equal("false", boot["DeferredEs2Hydrate"]);
                Assert.Equal("false", boot["DevDirectGameLoad"]);

                // Save-safe speed tiers stay on.
                Assert.Equal("true", boot["Enabled"]);
                Assert.Equal("true", boot["PreloadGameAsync"]);
                Assert.Equal("true", boot["AutoLoadSave"]);
                Assert.Equal("true", boot["SkipSplashScreen"]);
            }
            finally
            {
                Directory.Delete(gameDir, recursive: true);
            }
        }

        [Fact]
        public void StaleConfig_WithUnsafeTiersOn_IsForcedBackToSafe()
        {
            string gameDir = NewGameDir();
            try
            {
                string path = ConfigPath(gameDir);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path,
                    "[Boot]\nEnabled = false\nDevEs2Whitelist = true\nDevSkipEs2Tags = true\nDeferredEs2Hydrate = true\n");

                FastBootConfigSeed.ApplyProductionProfile(gameDir);
                var boot = ReadBootSection(path);

                Assert.Equal("true", boot["Enabled"]);
                Assert.Equal("false", boot["DevEs2Whitelist"]);
                Assert.Equal("false", boot["DevSkipEs2Tags"]);
                Assert.Equal("false", boot["DeferredEs2Hydrate"]);
            }
            finally
            {
                Directory.Delete(gameDir, recursive: true);
            }
        }

        [Fact]
        public void SameNamedKeyInOtherSection_IsNotTreatedAsTheBootValue()
        {
            string gameDir = NewGameDir();
            try
            {
                string path = ConfigPath(gameDir);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // A foreign [Other] section owns a key that collides with a managed one. The merge
                // must leave it alone and still write the real [Boot] value.
                File.WriteAllText(path, "[Other]\nDevEs2Whitelist = true\n\n[Boot]\nEnabled = true\n");

                FastBootConfigSeed.ApplyProductionProfile(gameDir);

                // The managed [Boot] value is written safe...
                Assert.Equal("false", ReadSection(path, "Boot")["DevEs2Whitelist"]);
                // ...while the foreign [Other] key with the same name is left untouched.
                Assert.Equal("true", ReadSection(path, "Other")["DevEs2Whitelist"]);
            }
            finally
            {
                Directory.Delete(gameDir, recursive: true);
            }
        }

        [Fact]
        public void ApplyingTwice_IsIdempotent()
        {
            string gameDir = NewGameDir();
            try
            {
                FastBootConfigSeed.ApplyProductionProfile(gameDir);
                string second = FastBootConfigSeed.ApplyProductionProfile(gameDir);
                Assert.Equal("FastBoot speed profile already set.", second);
            }
            finally
            {
                Directory.Delete(gameDir, recursive: true);
            }
        }

        [Fact]
        public void ApplyingTwice_OnPartialConfig_IsIdempotent()
        {
            // Exercises the "insert missing keys" merge branch, then re-applies. The second pass must
            // be a no-op — a regression in line-ending handling there would re-write the file forever.
            string gameDir = NewGameDir();
            try
            {
                string path = ConfigPath(gameDir);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "[Boot]\nEnabled = true\n");

                FastBootConfigSeed.ApplyProductionProfile(gameDir);
                string second = FastBootConfigSeed.ApplyProductionProfile(gameDir);
                Assert.Equal("FastBoot speed profile already set.", second);
            }
            finally
            {
                Directory.Delete(gameDir, recursive: true);
            }
        }
    }
}
