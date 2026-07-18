using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WinterMP.Launcher.Services
{
    /// <summary>Windows Steam integration: registry discovery + steam.exe launch.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsPlatform : IPlatformIntegration
    {
        public bool SupportsLauncherSelfUpdate => true;

        public string? FindSteamRoot()
        {
            string?[] candidates =
            {
                ReadRegistrySteamPath(Registry.CurrentUser, @"Software\Valve\Steam"),
                ReadRegistrySteamPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
                ReadRegistrySteamPath(Registry.LocalMachine, @"SOFTWARE\Valve\Steam"),
            };

            foreach (string? path in candidates)
            {
                if (path != null && Directory.Exists(path))
                    return path;
            }

            return null;
        }

        public bool IsSteamRunning()
        {
            try
            {
                return Process.GetProcessesByName("steam").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public void LaunchGame(GameInstall game, string launchArgs, Action<string> log)
        {
            string? steamExe = FindSteamExe();

            if (IsSteamRunning())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = game.ExePath,
                    Arguments = launchArgs,
                    WorkingDirectory = game.GameDir,
                    UseShellExecute = false,
                    Environment = { ["SteamAppId"] = GameLocator.AppId },
                });
                log("Launching game directly (Steam is running — faster than -applaunch).");
                return;
            }

            if (steamExe != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = $"-applaunch {GameLocator.AppId} {launchArgs}".TrimEnd(),
                    UseShellExecute = false,
                });
                log("Launching via Steam (-applaunch). Start Steam first next time for a faster boot.");
                return;
            }

            log("steam.exe not found — launching game directly (Steam MP may not work).");
            Process.Start(new ProcessStartInfo
            {
                FileName = game.ExePath,
                Arguments = launchArgs,
                WorkingDirectory = game.GameDir,
                UseShellExecute = false,
                Environment = { ["SteamAppId"] = GameLocator.AppId },
            });
        }

        public string GetGameSaveDir() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Amistech", "My Winter Car");

        public void OpenInShell(string pathOrUrl) =>
            Process.Start(new ProcessStartInfo { FileName = pathOrUrl, UseShellExecute = true });

        private string? FindSteamExe()
        {
            string? root = FindSteamRoot();
            if (root == null) return null;
            string exe = Path.Combine(root, "steam.exe");
            return File.Exists(exe) ? exe : null;
        }

        private static string? ReadRegistrySteamPath(RegistryKey root, string subKey)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                return key?.GetValue("SteamPath") as string is { Length: > 0 } path
                    ? path.Replace('/', '\\')
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
