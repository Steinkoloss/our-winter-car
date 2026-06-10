using System.Diagnostics;
using System.IO;
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
        public static BepInExStatus GetStatus(string gameDir)
        {
            bool loaderPresent = File.Exists(Path.Combine(gameDir, "winhttp.dll"))
                                 && Directory.Exists(Path.Combine(gameDir, "BepInEx", "core"));
            if (!loaderPresent) return BepInExStatus.NotInstalled;

            // Config only exists after the game ran once with BepInEx; treat a missing
            // config as "fix missing" so Install/Repair explains the next step.
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
            var status = GetStatus(gameDir);

            switch (status)
            {
                case BepInExStatus.NotInstalled:
                    // TODO(M8): bundle the BepInEx 5 x64 zip with the launcher and extract it
                    // here, then deploy the mod DLLs from an embedded payload.
                    throw new InvalidOperationException(
                        "Automatic BepInEx installation is not implemented yet.\n\n" +
                        "Manual steps:\n" +
                        "1. Download BepInEx_x64 5.4.23+ from github.com/BepInEx/BepInEx/releases\n" +
                        "2. Extract it into the game folder:\n   " + gameDir + "\n" +
                        "3. Run the game once, then use Install / Repair again to apply the config fix.");

                case BepInExStatus.MissingEntrypointFix:
                    string config = Path.Combine(gameDir, "BepInEx", "config", "BepInEx.cfg");
                    if (!File.Exists(config))
                        return "BepInEx is installed but has not generated its config yet. " +
                               "Run the game once, then click Install / Repair again.";

                    string text = File.ReadAllText(config);
                    string patched = PatchEntrypoint(text);
                    if (patched != text)
                    {
                        File.Copy(config, config + ".wintermp.bak", overwrite: true);
                        File.WriteAllText(config, patched);
                        return "Applied [Preloader.Entrypoint] Type = MonoBehaviour fix to BepInEx.cfg.";
                    }

                    return "BepInEx.cfg already configured.";

                case BepInExStatus.Ready:
                    return "BepInEx is installed and configured. (Mod deployment from the launcher lands in M8 — " +
                           "for now, build with MwcGamePath set to auto-deploy.)";

                default:
                    return "Nothing to do.";
            }
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

            // Replace the first "Type = ..." after the section header.
            return Regex.Replace(
                config,
                @"(\[Preloader\.Entrypoint\][^\[]*?^\s*Type\s*=\s*)[^\r\n]+",
                "${1}MonoBehaviour",
                RegexOptions.Singleline | RegexOptions.Multiline);
        }
    }
}
