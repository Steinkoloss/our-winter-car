using System.Diagnostics;
using System.IO;
using System.Windows;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class MainWindow : Window
    {
        private GameInstall? _game;
        private readonly System.Text.StringBuilder _log = new();
        private LauncherSettings _settings = LauncherSettings.Load();
        private UpdateCheckResult? _pendingUpdate;
        private bool _updateBusy;

        public MainWindow()
        {
            InitializeComponent();
            Title = $"{Branding.LauncherWindowTitle} {ModPayload.LauncherVersion}";
            SubtitleText.Text = $"Multiplayer for My Winter Car · protocol v{ModMeta.ProtocolVersion}";
            AppendLog($"{Branding.LauncherWindowTitle} {ModPayload.LauncherVersion}");
            RefreshStatus();
            ShowLastInstallFailureIfAny();
            ShowWelcomeIfNeeded();
            Loaded += async (_, _) =>
            {
                TryAutoInstallIfNeeded();
                await CheckForUpdatesAsync(showUpToDate: false);
            };
        }

        private static string LastInstallLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterMP", "last-install.log");

        private void ShowLastInstallFailureIfAny()
        {
            if (!File.Exists(LastInstallLogPath)) return;

            try
            {
                if (File.ReadAllText(LastInstallLogPath).Contains("ERROR", StringComparison.Ordinal))
                {
                    ShowWarning(
                        "The last automatic mod install failed (often because the game was not found yet). " +
                        "The launcher will retry now, or click Install / Repair.");
                }
            }
            catch
            {
                // ignore unreadable log
            }
        }

        private void ShowWelcomeIfNeeded()
        {
            if (_settings.SeenWelcome) return;

            MessageBox.Show(this,
                $"Welcome to {Branding.ProductName}!\n\n" +
                "The launcher installs BepInEx and the mod into your My Winter Car folder automatically.\n\n" +
                "1. HOST GAME — backs up save and opens a Steam lobby\n" +
                "2. Friends install the same build, then use Steam Join Game\n\n" +
                "Never save the game as a guest.",
                Branding.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _settings.SeenWelcome = true;
            _settings.Save();
        }

        private async Task CheckForUpdatesAsync(bool showUpToDate)
        {
            try
            {
                AppendLog("Checking for updates...");
                var result = await UpdateChecker.CheckAsync(_game?.GameDir, _settings.GitHubToken);
                _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                _settings.Save();

                _pendingUpdate = result;
                ApplyUpdateUi();

                if (!result.IsSuccess)
                {
                    UpdateStatusText.Text = result.ErrorMessage ?? "Update check failed";
                    AppendLog($"Update check failed: {result.ErrorMessage}");
                    return;
                }

                if (result.AnyUpdateAvailable)
                {
                    AppendLog($"Update available: {result.StatusSummary} ({result.ReleaseUrl})");
                    return;
                }

                UpdateStatusText.Text = $"Up to date ({result.Tag})";
                AppendLog($"Up to date ({result.Tag}).");
                if (showUpToDate)
                {
                    MessageBox.Show(this,
                        $"{Branding.ProductName} is up to date ({result.Tag}).",
                        "No updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "Update check failed";
                AppendLog($"Update check failed: {ex.Message}");
            }
        }

        private void ApplyUpdateUi()
        {
            if (_pendingUpdate == null || !_pendingUpdate.IsSuccess)
            {
                UpdateBannerPanel.Visibility = Visibility.Collapsed;
                return;
            }

            bool dismissed = _settings.DismissedUpdateTag == _pendingUpdate.Tag;
            bool showBanner = _pendingUpdate.AnyUpdateAvailable && !dismissed;

            UpdateStatusText.Text = _pendingUpdate.AnyUpdateAvailable
                ? _pendingUpdate.StatusSummary
                : $"Up to date ({_pendingUpdate.Tag})";

            UpdateBannerPanel.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
            if (!showBanner) return;

            UpdateBannerTitle.Text = $"Update available: {_pendingUpdate.Tag}";
            var lines = new List<string>();
            if (_pendingUpdate.LauncherUpdateAvailable)
            {
                lines.Add($"Launcher: {_pendingUpdate.LauncherVersion} → {_pendingUpdate.RemoteVersion}");
            }
            if (_pendingUpdate.ModUpdateAvailable)
            {
                Version from = _pendingUpdate.InstalledModVersion ?? _pendingUpdate.BundledModVersion;
                lines.Add($"Mod: {from} → {_pendingUpdate.RemoteVersion}");
            }
            UpdateBannerText.Text = string.Join("\n", lines);

            UpdateLauncherButton.IsEnabled = _pendingUpdate.LauncherUpdateAvailable
                && _pendingUpdate.SetupDownloadUrl != null
                && !_updateBusy;
            UpdateModButton.IsEnabled = _pendingUpdate.ModUpdateAvailable
                && _game != null
                && _pendingUpdate.PayloadDownloadUrl != null
                && !_updateBusy;
            UpdateReleaseNotesButton.IsEnabled = !string.IsNullOrEmpty(_pendingUpdate.ReleaseUrl);
        }

        private void RefreshStatus()
        {
            _game = GameLocator.FindInstall(_settings.CustomGameDir);
            WarningStatusText.Visibility = Visibility.Collapsed;
            WarningStatusText.Text = string.Empty;

            ushort protocol = ModMeta.ProtocolVersion;
            BuildStatusText.Text = protocol > 0
                ? $"launcher {ModPayload.LauncherVersion}, mod {ModMeta.ModVersion}, protocol v{protocol}"
                : $"launcher {ModPayload.LauncherVersion} (rebuild launcher)";

            if (_game == null)
            {
                GameStatusText.Text = "Not found — set folder in Settings or install via Steam";
                BepInExStatusText.Text = "—";
                ModStatusText.Text = "—";
                HostButton.IsEnabled = false;
                PlayButton.IsEnabled = false;
                InstallButton.IsEnabled = false;
                OpenGameButton.IsEnabled = false;
                ShowWarning("Game not found. Open Settings to browse to your install folder.");
                ApplyUpdateUi();
                return;
            }

            GameStatusText.Text = $"{_game.GameDir} (build {_game.BuildId ?? "?"})";
            OpenGameButton.IsEnabled = true;
            InstallButton.IsEnabled = true;

            var compat = CompatManifest.Load();
            compat?.ValidatePayload()?.Let(ShowWarning);
            compat?.ValidateGameBuild(_game.BuildId)?.Let(ShowWarning);

            var bepStatus = BepInExInstaller.GetStatus(_game.GameDir);
            BepInExStatusText.Text = bepStatus switch
            {
                BepInExStatus.NotInstalled => "Not installed",
                BepInExStatus.MissingEntrypointFix => "Needs config fix",
                BepInExStatus.Ready => "Ready",
                _ => "Unknown",
            };

            string? modVersion = BepInExInstaller.GetInstalledModVersion(_game.GameDir);
            ModStatusText.Text = modVersion ?? "Not installed";

            if (modVersion != null && modVersion != ModMeta.ModVersion)
            {
                ModStatusText.Text += $" (launcher bundle {ModMeta.ModVersion})";
                ShowWarning("Installed mod differs from launcher bundle — use Update mod or Install / Repair.");
            }

            int backups = SaveBackupService.CountBackups();
            BackupStatusText.Text = SaveBackupService.SaveDirExists()
                ? $"{backups} backup(s)"
                : $"{backups} backup(s) — no save yet";

            bool playable = bepStatus == BepInExStatus.Ready && modVersion != null;
            HostButton.IsEnabled = playable;
            PlayButton.IsEnabled = playable;

            if (!ModPayload.PayloadPresent())
                ShowWarning($"Mod payload missing from launcher — reinstall {Branding.ProductName}.");

            if (!BepInExInstaller.VendorPackagePresent())
                ShowWarning("BepInEx package missing from launcher (vendor folder). Reinstall from GitHub.");

            if (_game != null && !BepInExInstaller.IsFullyInstalled(_game.GameDir))
                ShowWarning("Mod not installed in the game folder yet.");

            ApplyUpdateUi();
        }

        private void ShowWarning(string text)
        {
            WarningStatusText.Visibility = Visibility.Visible;
            if (string.IsNullOrEmpty(WarningStatusText.Text))
                WarningStatusText.Text = text;
            else
                WarningStatusText.Text += "\n" + text;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
            await CheckForUpdatesAsync(showUpToDate: false);
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) =>
            await CheckForUpdatesAsync(showUpToDate: true);

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;
            RunInstall(showSuccessDialog: true);
        }

        private void TryAutoInstallIfNeeded()
        {
            if (_game == null || BepInExInstaller.IsFullyInstalled(_game.GameDir))
                return;

            if (!ModPayload.PayloadPresent() || !BepInExInstaller.VendorPackagePresent())
                return;

            AppendLog("Mod not installed — running Install / Repair automatically…");
            RunInstall(showSuccessDialog: false);
        }

        private void RunInstall(bool showSuccessDialog)
        {
            if (_game == null) return;

            if (!TryInstallMod(out string? error))
            {
                if (showSuccessDialog || !string.IsNullOrWhiteSpace(error))
                {
                    MessageBox.Show(this,
                        error ?? "Install / Repair failed.",
                        "Install / Repair failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                return;
            }

            if (showSuccessDialog)
            {
                MessageBox.Show(this,
                    $"BepInEx and {Branding.ProductName} are installed in:\n{_game.GameDir}",
                    "Install / Repair",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private bool TryInstallMod(out string? error)
        {
            error = null;
            if (_game == null)
            {
                error = "Game folder not found. Open Settings and browse to your My Winter Car folder.";
                return false;
            }

            try
            {
                string result = BepInExInstaller.InstallOrRepair(_game.GameDir);
                AppendLog(result.Replace("\n", "\n"));
                RefreshStatus();

                if (BepInExInstaller.IsFullyInstalled(_game.GameDir))
                    return true;

                error = "Install finished but BepInEx or the mod is still missing. Check Windows Defender exclusions.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppendLog($"Install failed: {ex.Message}");
                RefreshStatus();
                return false;
            }
        }

        private async void UpdateModButton_Click(object sender, RoutedEventArgs e) => await RunModUpdateAsync();

        private async void UpdateLauncherButton_Click(object sender, RoutedEventArgs e) => await RunLauncherUpdateAsync();

        private async Task RunModUpdateAsync()
        {
            if (_game == null || _pendingUpdate?.PayloadDownloadUrl == null) return;
            if (_updateBusy) return;

            _updateBusy = true;
            SetUpdateButtonsEnabled(false);
            try
            {
                AppendLog($"Downloading mod update {_pendingUpdate.Tag}...");
                string result = await UpdateChecker.DownloadAndApplyPayloadAsync(
                    _pendingUpdate.PayloadDownloadUrl, _game.GameDir, _settings.GitHubToken);
                AppendLog(result);
                MessageBox.Show(this, result, "Mod updated", MessageBoxButton.OK, MessageBoxImage.Information);
                await CheckForUpdatesAsync(showUpToDate: false);
            }
            catch (Exception ex)
            {
                AppendLog($"Mod update failed: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Mod update failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _updateBusy = false;
                RefreshStatus();
            }
        }

        private async Task RunLauncherUpdateAsync()
        {
            if (_pendingUpdate?.SetupDownloadUrl == null) return;
            if (_updateBusy) return;

            var confirm = MessageBox.Show(this,
                $"Download and install {_pendingUpdate.Tag}?\n\n" +
                "The launcher will close and the installer will run silently.",
                "Update launcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _updateBusy = true;
            SetUpdateButtonsEnabled(false);
            try
            {
                AppendLog($"Downloading launcher update {_pendingUpdate.Tag}...");
                string setupPath = await UpdateChecker.DownloadLauncherSetupAsync(
                    _pendingUpdate.SetupDownloadUrl, _settings.GitHubToken);
                AppendLog($"Running installer: {setupPath}");
                UpdateChecker.RunLauncherSetup(setupPath);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                AppendLog($"Launcher update failed: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Launcher update failed", MessageBoxButton.OK, MessageBoxImage.Error);
                _updateBusy = false;
                RefreshStatus();
            }
        }

        private void SetUpdateButtonsEnabled(bool enabled)
        {
            UpdateModButton.IsEnabled = enabled;
            UpdateLauncherButton.IsEnabled = enabled;
        }

        private void DismissUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate == null) return;
            _settings.DismissedUpdateTag = _pendingUpdate.Tag;
            _settings.Save();
            UpdateBannerPanel.Visibility = Visibility.Collapsed;
            AppendLog($"Dismissed update banner for {_pendingUpdate.Tag}.");
        }

        private void UpdateReleaseNotesButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingUpdate?.ReleaseUrl)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = _pendingUpdate.ReleaseUrl,
                UseShellExecute = true,
            });
        }

        private void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? path = SaveBackupService.CreateBackup();
                AppendLog(path != null ? $"Backup created: {path}" : "No save folder found.");
            }
            catch (Exception ex)
            {
                AppendLog($"Backup failed: {ex.Message}");
            }

            RefreshStatus();
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new RestoreBackupWindow { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ResultMessage != null)
                AppendLog(dialog.ResultMessage);
            RefreshStatus();
        }

        private void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string zip = DiagnosticsService.CreateBundle(_game, _log.ToString());
                AppendLog($"Diagnostics saved: {zip}");
                Process.Start(new ProcessStartInfo { FileName = zip, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Diagnostics failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;
            Process.Start(new ProcessStartInfo { FileName = _game.GameDir, UseShellExecute = true });
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string help = Path.Combine(AppContext.BaseDirectory, "PLAYERS.md");
            if (File.Exists(help))
            {
                Process.Start(new ProcessStartInfo { FileName = help, UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/Steinkoloss/our-winter-car/releases/latest",
                UseShellExecute = true,
            });
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsWindow(_settings, _game) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _settings = dialog.Settings;
                RefreshStatus();
                _ = CheckForUpdatesAsync(showUpToDate: false);
            }
        }

        private void HostButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;
            if (!EnsureReadyForLaunch()) return;

            try
            {
                string? backup = SaveBackupService.CreateBackup();
                AppendLog(backup != null ? $"Save backed up: {backup}" : "No save yet — hosting without backup.");
                LaunchGame("-wintermp host");
                AppendLog("Launching as HOST.");
            }
            catch (Exception ex)
            {
                AppendLog($"Host launch failed: {ex.Message}");
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;
            if (!EnsureReadyForLaunch()) return;

            try
            {
                LaunchGame(string.Empty);
                AppendLog("Launching solo.");
            }
            catch (Exception ex)
            {
                AppendLog($"Launch failed: {ex.Message}");
            }
        }

        private bool EnsureReadyForLaunch()
        {
            RefreshStatus();
            if (_game != null && BepInExInstaller.IsFullyInstalled(_game.GameDir))
                return true;

            AppendLog("Installing BepInEx and mod before launch…");
            if (!TryInstallMod(out string? error))
            {
                MessageBox.Show(this,
                    error ?? $"Install / Repair failed. Both BepInEx and {Branding.ProductName} must be ready before launch.\n\n" +
                    "If Windows Defender removed files, add your My Winter Car folder to exclusions and try again.",
                    "Not ready",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void LaunchGame(string args)
        {
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

            AppendLog("steam.exe not found — launching game directly (Steam MP may not work).");
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
            _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            LogText.Text = _log.ToString();
            LogScroll.ScrollToEnd();
        }
    }

    internal static class StringExtensions
    {
        public static void Let(this string value, Action<string> action) => action(value);
    }
}
