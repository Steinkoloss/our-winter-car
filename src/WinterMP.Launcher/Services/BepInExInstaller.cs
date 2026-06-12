using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WinterMP.Launcher.Services
{
    public enum BepInExStatus
    {
        NotInstalled,
        MissingEntrypointFix,
        Ready,
    }

    /// <summary>
    /// Detects and repairs the BepInEx 5 installation the mod rides on.
    ///
    /// My Winter Car specifics: BepInEx 5 x64, and BepInEx.cfg needs
    /// [Preloader.Entrypoint] Type = MonoBehaviour instead of the default.
    /// </summary>
    public static class BepInExInstaller
    {
        private const string BepInExZipName = "BepInEx_win_x64_5.4.23.5.zip";

        public static BepInExStatus GetStatus(string gameDir)
        {
            bool loaderPresent = File.Exists(Path.Combine(gameDir, "winhttp.dll"))
                                 && Directory.Exists(Path.Combine(gameDir, "BepInEx", "core"));
            if (!loaderPresent) return BepInExStatus.NotInstalled;

            string config = Path.Combine(gameDir, "BepInEx", "config", "BepInEx.cfg");
            if (!File.Exists(config) || !EntrypointIsFixed(File.ReadAllText(config)))
                return BepInExStatus.MissingEntrypointFix;

            return BepInExStatus.Ready;
        }

        public static string? GetInstalledModVersion(string gameDir)
        {
            string dll = Path.Combine(gameDir, "BepInEx", "plugins", "WinterMP", "WinterMP.Core.dll");
            if (!File.Exists(dll)) return null;

            try
            {
                return FileVersionInfo.GetVersionInfo(dll).FileVersion;
            }
            catch
            {
                return "unknown version";
            }
        }

        public static string InstallOrRepair(string gameDir)
        {
            var messages = new List<string>();
            var status = GetStatus(gameDir);

            if (status == BepInExStatus.NotInstalled)
            {
                ExtractBepInEx(gameDir);
                messages.Add("Installed BepInEx 5 x64 into the game folder.");
                status = GetStatus(gameDir);
            }

            messages.Add(EnsureConfig(gameDir, status));
            messages.Add(ModPayload.Deploy(gameDir));

            return string.Join("\n", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
        }

        private static string EnsureConfig(string gameDir, BepInExStatus status)
        {
            string configDir = Path.Combine(gameDir, "BepInEx", "config");
            Directory.CreateDirectory(configDir);
            string config = Path.Combine(configDir, "BepInEx.cfg");

            if (!File.Exists(config))
            {
                string template = LoadEmbeddedConfigTemplate();
                File.WriteAllText(config, template);
                return "Wrote BepInEx.cfg with MonoBehaviour entrypoint.";
            }

            if (status == BepInExStatus.MissingEntrypointFix || !EntrypointIsFixed(File.ReadAllText(config)))
            {
                string text = File.ReadAllText(config);
                string patched = PatchEntrypoint(text);
                if (patched != text)
                {
                    File.Copy(config, config + ".wintermp.bak", overwrite: true);
                    File.WriteAllText(config, patched);
                    return "Applied [Preloader.Entrypoint] Type = MonoBehaviour fix to BepInEx.cfg.";
                }
            }

            return "BepInEx.cfg already configured.";
        }

        private static void ExtractBepInEx(string gameDir)
        {
            string? zipPath = FindBepInExZip();
            if (zipPath == null)
            {
                throw new InvalidOperationException(
                    $"BepInEx package not found ({BepInExZipName}). Reinstall {Branding.ProductName} or place the zip in vendor/.");
            }

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                string dest = Path.Combine(gameDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }

        private static string? FindBepInExZip()
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "vendor", BepInExZipName),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "vendor", BepInExZipName)),
            };

            foreach (string path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            return null;
        }

        private static string LoadEmbeddedConfigTemplate()
        {
            var asm = Assembly.GetExecutingAssembly();
            string? resource = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("BepInEx.cfg", StringComparison.OrdinalIgnoreCase));
            if (resource == null)
                throw new InvalidOperationException("Embedded BepInEx.cfg template missing from launcher build.");

            using var stream = asm.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Embedded BepInEx.cfg stream missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static bool EntrypointIsFixed(string config)
        {
            var section = Regex.Match(config, @"\[Preloader\.Entrypoint\](.*?)(\n\[|$)", RegexOptions.Singleline);
            if (!section.Success) return false;
            return Regex.IsMatch(section.Groups[1].Value, @"(?m)^\s*Type\s*=\s*MonoBehaviour\s*$");
        }

        private static string PatchEntrypoint(string config)
        {
            var section = Regex.Match(config, @"\[Preloader\.Entrypoint\]", RegexOptions.Singleline);
            if (!section.Success) return config;

            return Regex.Replace(
                config,
                @"(\[Preloader\.Entrypoint\][^\[]*?^\s*Type\s*=\s*)[^\r\n]+",
                "${1}MonoBehaviour",
                RegexOptions.Singleline | RegexOptions.Multiline);
        }
    }
}
