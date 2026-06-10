using System.Diagnostics;
using System.Windows;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class MainWindow : Window
    {
        private GameInstall? _game;

        public MainWindow()
        {
            InitializeComponent();
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            _game = GameLocator.FindSteamInstall();

            if (_game == null)
            {
                GameStatusText.Text = "Not found — is My Winter Car installed via Steam?";
                BepInExStatusText.Text = "—";
                ModStatusText.Text = "—";
                HostButton.IsEnabled = false;
                PlayButton.IsEnabled = false;
                AppendLog("Game not found. Install My Winter Car and click Refresh.");
                return;
            }

            GameStatusText.Text = $"{_game.GameDir} (build {_game.BuildId ?? "?"})";

            var bepStatus = BepInExInstaller.GetStatus(_game.GameDir);
            BepInExStatusText.Text = bepStatus switch
            {
                BepInExStatus.NotInstalled => "Not installed",
                BepInExStatus.MissingEntrypointFix => "Installed — entrypoint fix missing (run Install / Repair)",
                BepInExStatus.Ready => "Installed",
                _ => "Unknown",
            };

            string? modVersion = BepInExInstaller.GetInstalledModVersion(_game.GameDir);
            ModStatusText.Text = modVersion ?? "Not installed";

            int backups = SaveBackupService.CountBackups();
            BackupStatusText.Text = SaveBackupService.SaveDirExists()
                ? $"{backups} backup(s) — save folder found"
                : $"{backups} backup(s) — save folder not found yet";

            bool playable = bepStatus == BepInExStatus.Ready && modVersion != null;
            HostButton.IsEnabled = playable;
            PlayButton.IsEnabled = playable;

            AppendLog($"Status refreshed. Game: OK, BepInEx: {bepStatus}, Mod: {modVersion ?? "missing"}.");
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshStatus();

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;

            try
            {
                var result = BepInExInstaller.InstallOrRepair(_game.GameDir);
                AppendLog(result);
            }
            catch (Exception ex)
            {
                AppendLog($"Install failed: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Install / Repair failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshStatus();
        }

        private void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? path = SaveBackupService.CreateBackup();
                AppendLog(path != null ? $"Backup created: {path}" : "No save folder found — nothing to back up.");
            }
            catch (Exception ex)
            {
                AppendLog($"Backup failed: {ex.Message}");
            }

            RefreshStatus();
        }

        private void HostButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;

            try
            {
                string? backup = SaveBackupService.CreateBackup();
                AppendLog(backup != null
                    ? $"Save backed up: {backup}"
                    : "No save folder found yet — starting without backup.");

                LaunchGame("-wintermp host");
                AppendLog("Game starting as host. Invite friends via Shift+Tab → friends list once in game.");
            }
            catch (Exception ex)
            {
                AppendLog($"Host launch failed: {ex.Message}");
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;

            try
            {
                LaunchGame(string.Empty);
                AppendLog("Game starting. To join a friend: accept their Steam invite or use \"Join Game\" in the friends list.");
            }
            catch (Exception ex)
            {
                AppendLog($"Launch failed: {ex.Message}");
            }
        }

        private void LaunchGame(string args)
        {
            // Verified on the real game: launching mywintercar.exe directly leaves the
            // process without a Steam app context (no steam_appid.txt ships with the
            // game), so SteamAPI.Init fails and the overlay never attaches. Launching
            // through steam.exe -applaunch gives the full Steam context and still
            // forwards our -wintermp arguments to the game's command line.
            string? steamExe = GameLocator.FindSteamExe();
            if (steamExe != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = $"-applaunch {GameLocator.AppId} {args}".TrimEnd(),
                    UseShellExecute = false,
                });
                return;
            }

            AppendLog("steam.exe not found — falling back to direct game launch (Steam features may not work).");
            Process.Start(new ProcessStartInfo
            {
                FileName = _game!.ExePath,
                Arguments = args,
                WorkingDirectory = _game.GameDir,
                UseShellExecute = false,
            });
        }

        private void AppendLog(string line)
        {
            LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {line}\n";
            LogScroll.ScrollToEnd();
        }
    }
}
