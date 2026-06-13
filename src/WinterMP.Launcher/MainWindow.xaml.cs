using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
        private bool _installInProgress;
        private DateTime _lastAutoInstallAttempt = DateTime.MinValue;
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _updateTimer;
        private bool _updateCheckInProgress;
        private InfoWindow? _openInfoWindow;
        private string _updateStatusText = "Checking…";

        public MainWindow()
        {
            InitializeComponent();
            Title = $"{Branding.LauncherWindowTitle} {ModPayload.LauncherVersion}";
            SubtitleText.Text =
                $"Host or join via Steam · protocol v{ModMeta.ProtocolVersion}";
            AppendLog($"{Branding.LauncherWindowTitle} {ModPayload.LauncherVersion}");

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (_, _) =>
            {
                RefreshStatus();
                TryAutoInstallIfNeeded();
            };

            _updateTimer = new DispatcherTimer { Interval = UpdateChecker.BackgroundCheckInterval };
            _updateTimer.Tick += async (_, _) => await RunPeriodicUpdateCheckAsync();

            RefreshStatus();
            ShowLastInstallFailureIfAny();

            Loaded += async (_, _) =>
            {
                _statusTimer.Start();
                _updateTimer.Start();
                TryAutoInstallIfNeeded();
                await CheckForUpdatesAsync();
                if (_pendingUpdate?.AnyUpdateAvailable == true)
                    await PromptForUpdateAsync(required: false);
            };

            Closed += (_, _) =>
            {
                _statusTimer.Stop();
                _updateTimer.Stop();
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
                        "The last automatic install failed (often because the game was not found yet). " +
                        "Open Settings and set the game folder if needed.");
                }
            }
            catch
            {
                // ignore unreadable log
            }
        }

        private async Task RunPeriodicUpdateCheckAsync()
        {
            if (_updateCheckInProgress || _updateBusy) return;
            if (!ShouldRunBackgroundUpdateCheck()) return;
            await CheckForUpdatesAsync(quiet: true);
        }

        private bool ShouldRunBackgroundUpdateCheck()
        {
            if (!_settings.LastUpdateCheckUtc.HasValue) return true;
            return DateTime.UtcNow - _settings.LastUpdateCheckUtc.Value >= UpdateChecker.BackgroundCheckInterval;
        }

        internal async Task CheckForUpdatesAsync(bool quiet = false)
        {
            if (_updateCheckInProgress) return;
            _updateCheckInProgress = true;

            bool hadUpdate = _pendingUpdate?.AnyUpdateAvailable == true;
            try
            {
                if (!quiet) AppendLog("Checking for updates...");
                var result = await UpdateChecker.CheckAsync(_game?.GameDir);
                _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                _settings.Save();

                _pendingUpdate = result;
                ApplyUpdateUi();

                if (!result.IsSuccess)
                {
                    _updateStatusText = result.ErrorMessage ?? "Update check failed";
                    if (!quiet)
                        AppendLog($"Update check failed: {result.ErrorMessage}");
                    RefreshOpenInfoWindow();
                    return;
                }

                if (result.AnyUpdateAvailable)
                {
                    _updateStatusText = result.StatusSummary;
                    if (!quiet || !hadUpdate)
                        AppendLog($"Update available: {result.StatusSummary} ({result.ReleaseUrl})");
                    RefreshOpenInfoWindow();
                    return;
                }

                _updateStatusText = $"Up to date ({result.Tag})";
                if (!quiet)
                    AppendLog($"Up to date ({result.Tag}).");
            }
            catch (Exception ex)
            {
                _updateStatusText = "Update check failed";
                if (!quiet)
                    AppendLog($"Update check failed: {ex.Message}");
                RefreshOpenInfoWindow();
            }
            finally
            {
                _updateCheckInProgress = false;
            }
        }

        private void ApplyUpdateUi()
        {
            if (_pendingUpdate == null || !_pendingUpdate.IsSuccess)
            {
                UpdateBannerPanel.Visibility = Visibility.Collapsed;
                return;
            }

            bool showBanner = _pendingUpdate.AnyUpdateAvailable;

            _updateStatusText = _pendingUpdate.AnyUpdateAvailable
                ? _pendingUpdate.StatusSummary
                : $"Up to date ({_pendingUpdate.Tag})";

            UpdateBannerPanel.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
            if (!showBanner) return;

            UpdateBannerTitle.Text = $"Update available: {_pendingUpdate.Tag}";
            var lines = new List<string>();
            if (_pendingUpdate.LauncherUpdateAvailable)
                lines.Add($"Launcher: {_pendingUpdate.LauncherVersion} → {_pendingUpdate.RemoteVersion}");
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
            RefreshOpenInfoWindow();
        }

        internal LauncherInfoSnapshot BuildInfoSnapshot()
        {
            ushort protocol = ModMeta.ProtocolVersion;
            string buildStatus = protocol > 0
                ? $"launcher {ModPayload.LauncherVersion}, mod {ModMeta.ModVersion}, protocol v{protocol}"
                : $"launcher {ModPayload.LauncherVersion} (rebuild launcher)";

            if (_game == null)
            {
                return new LauncherInfoSnapshot
                {
                    GameStatus = string.IsNullOrWhiteSpace(_settings.CustomGameDir)
                        ? "Not found — install via Steam or set folder in Settings"
                        : "Folder not found — check path in Settings",
                    BepInExStatus = "—",
                    ModStatus = "—",
                    BackupStatus = "—",
                    BuildStatus = buildStatus,
                    UpdateStatus = _updateStatusText,
                };
            }

            var bepStatus = BepInExInstaller.GetStatus(_game.GameDir);
            string bepInExStatus = bepStatus switch
            {
                BepInExStatus.NotInstalled => "Not installed",
                BepInExStatus.MissingEntrypointFix => "Needs config fix",
                BepInExStatus.Ready => "Ready",
                _ => "Unknown",
            };

            string? modVersion = BepInExInstaller.GetInstalledModVersion(_game.GameDir);
            string modStatus = modVersion ?? "Not installed";
            if (modVersion != null && modVersion != ModMeta.ModVersion)
                modStatus += $" (bundle {ModMeta.ModVersion})";

            int backups = SaveBackupService.CountBackups();
            string backupStatus = SaveBackupService.SaveDirExists()
                ? $"{backups} backup(s)"
                : $"{backups} backup(s) — no save yet";

            return new LauncherInfoSnapshot
            {
                GameStatus = $"Found · build {_game.BuildId ?? "?"}",
                GameDirTooltip = _game.GameDir,
                BepInExStatus = bepInExStatus,
                ModStatus = modStatus,
                BackupStatus = backupStatus,
                BuildStatus = buildStatus,
                UpdateStatus = _updateStatusText,
            };
        }

        private void RefreshOpenInfoWindow()
        {
            _openInfoWindow?.ApplySnapshot(BuildInfoSnapshot());
        }

        private string BuildUpdatePromptMessage(bool required)
        {
            if (_pendingUpdate == null) return string.Empty;

            var lines = new List<string> { $"{_pendingUpdate.Tag} is available.\n" };
            if (_pendingUpdate.LauncherUpdateAvailable)
                lines.Add($"Launcher: {_pendingUpdate.LauncherVersion} → {_pendingUpdate.RemoteVersion}");
            if (_pendingUpdate.ModUpdateAvailable)
            {
                Version from = _pendingUpdate.InstalledModVersion ?? _pendingUpdate.BundledModVersion;
                lines.Add($"Mod: {from} → {_pendingUpdate.RemoteVersion}");
            }

            lines.Add("\nUpdate now? Everyone in a session needs the same version.");
            if (required)
                lines.Add("\nYou can't host or join until you're up to date.");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Shows the update prompt. Returns true when play/setup may continue.
        /// </summary>
        private async Task<bool> PromptForUpdateAsync(bool required)
        {
            if (_updateBusy || _pendingUpdate == null || !_pendingUpdate.IsSuccess) return true;
            if (!_pendingUpdate.AnyUpdateAvailable) return true;

            var answer = MessageBox.Show(this,
                BuildUpdatePromptMessage(required),
                "Update?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return !required;

            await ApplyPendingUpdatesAsync(skipConfirm: true);
            return !_pendingUpdate.AnyUpdateAvailable;
        }

        private async Task ApplyPendingUpdatesAsync(bool skipConfirm)
        {
            if (_pendingUpdate == null || !_pendingUpdate.AnyUpdateAvailable) return;

            if (_pendingUpdate.LauncherUpdateAvailable && _pendingUpdate.SetupDownloadUrl != null)
            {
                await RunLauncherUpdateAsync(skipConfirm);
                return;
            }

            if (_pendingUpdate.ModUpdateAvailable && _game != null && _pendingUpdate.PayloadDownloadUrl != null)
                await RunModUpdateAsync();
        }

        private void RefreshStatus()
        {
            _game = GameLocator.FindInstall(_settings.CustomGameDir);
            WarningStatusText.Visibility = Visibility.Collapsed;
            WarningStatusText.Text = string.Empty;

            if (_game == null)
            {
                HostButton.IsEnabled = false;
                JoinButton.IsEnabled = false;
                OpenGameButton.IsEnabled = false;
                ApplyUpdateUi();
                RefreshOpenInfoWindow();
                return;
            }

            OpenGameButton.IsEnabled = true;

            var compat = CompatManifest.Load();
            compat?.ValidatePayload()?.Let(ShowWarning);
            compat?.ValidateGameBuild(_game.BuildId)?.Let(ShowWarning);

            var bepStatus = BepInExInstaller.GetStatus(_game.GameDir);
            string? modVersion = BepInExInstaller.GetInstalledModVersion(_game.GameDir);

            if (modVersion != null && modVersion != ModMeta.ModVersion)
                ShowWarning("Installed mod differs from launcher bundle — use Update mod in the banner.");

            bool playable = bepStatus == BepInExStatus.Ready && modVersion != null;
            HostButton.IsEnabled = playable && !_installInProgress;
            JoinButton.IsEnabled = playable && !_installInProgress;

            if (!ModPayload.PayloadPresent())
                ShowWarning($"Mod payload missing from launcher — reinstall {Branding.ProductName}.");

            if (!BepInExInstaller.VendorPackagePresent())
                ShowWarning("BepInEx package missing from launcher (vendor folder). Reinstall from GitHub.");

            if (!BepInExInstaller.IsFullyInstalled(_game.GameDir) && !_installInProgress)
                ShowWarning("Mod not installed yet — installing automatically when possible.");

            ApplyUpdateUi();
            RefreshOpenInfoWindow();
        }

        private void ShowWarning(string text)
        {
            WarningStatusText.Visibility = Visibility.Visible;
            if (string.IsNullOrEmpty(WarningStatusText.Text))
                WarningStatusText.Text = text;
            else
                WarningStatusText.Text += "\n" + text;
        }

        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InfoWindow { Owner = this };
            dialog.ApplySnapshot(BuildInfoSnapshot());
            _openInfoWindow = dialog;
            dialog.Closed += (_, _) => _openInfoWindow = null;
            dialog.ShowDialog();
        }

        private void TryAutoInstallIfNeeded()
        {
            if (_installInProgress || _updateBusy || _game == null) return;
            if (BepInExInstaller.IsFullyInstalled(_game.GameDir)) return;
            if (!ModPayload.PayloadPresent() || !BepInExInstaller.VendorPackagePresent()) return;

            // Status refreshes every second; retry install at most every 30 s on failure.
            if ((DateTime.UtcNow - _lastAutoInstallAttempt).TotalSeconds < 30) return;

            _installInProgress = true;
            _lastAutoInstallAttempt = DateTime.UtcNow;
            try
            {
                AppendLog("Installing BepInEx and mod automatically…");
                if (TryInstallMod(out _))
                    AppendLog("Install complete.");
            }
            finally
            {
                _installInProgress = false;
                RefreshStatus();
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

                if (BepInExInstaller.IsFullyInstalled(_game.GameDir))
                    return true;

                error = "Install finished but BepInEx or the mod is still missing. Check Windows Defender exclusions.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppendLog($"Install failed: {ex.Message}");
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
                    _pendingUpdate.PayloadDownloadUrl, _game.GameDir);
                AppendLog(result);
                AppendLog("Mod updated — restarting launcher.");
                UpdateChecker.RestartApplication();
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                AppendLog($"Mod update failed: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Mod update failed", MessageBoxButton.OK, MessageBoxImage.Error);
                _updateBusy = false;
                RefreshStatus();
            }
        }

        private async Task RunLauncherUpdateAsync(bool skipConfirm = false)
        {
            if (_pendingUpdate?.SetupDownloadUrl == null) return;
            if (_updateBusy) return;

            if (!skipConfirm)
            {
                var confirm = MessageBox.Show(this,
                    $"Download and install {_pendingUpdate.Tag}?\n\n" +
                    "The launcher will close and restart when the update finishes.",
                    "Update launcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }

            _updateBusy = true;
            SetUpdateButtonsEnabled(false);
            try
            {
                AppendLog($"Downloading launcher update {_pendingUpdate.Tag}...");
                string setupPath = await UpdateChecker.DownloadLauncherSetupAsync(
                    _pendingUpdate.SetupDownloadUrl);
                AppendLog("Installing update — launcher will restart.");
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

        private void OpenGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;
            Process.Start(new ProcessStartInfo { FileName = _game.GameDir, UseShellExecute = true });
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsWindow(_settings, _game) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _settings = dialog.Settings;
                RefreshStatus();
                TryAutoInstallIfNeeded();
            }
        }

        private async void HostButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;
            if (!await PromptForUpdateAsync(required: true)) return;
            if (!EnsureReadyForLaunch()) return;

            try
            {
                LaunchGame("-wintermp host");
                AppendLog("Launching as HOST (save backup in background)…");

                _ = Task.Run(() => SaveBackupService.CreateBackup())
                    .ContinueWith(t =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (t.IsFaulted)
                            {
                                AppendLog($"Save backup failed: {t.Exception?.GetBaseException().Message}");
                                return;
                            }

                            string? backup = t.Result;
                            AppendLog(backup != null
                                ? $"Save backed up: {backup}"
                                : "No save yet — hosting without backup.");
                        });
                    }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                AppendLog($"Host launch failed: {ex.Message}");
            }
        }

        private async void JoinButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null) return;
            if (!await PromptForUpdateAsync(required: true)) return;
            if (!EnsureReadyForLaunch()) return;

            try
            {
                LaunchGame("-wintermp join");
                AppendLog("Launching to join via Steam (not hosting).");
            }
            catch (Exception ex)
            {
                AppendLog($"Join launch failed: {ex.Message}");
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
                    error ?? $"{Branding.ProductName} could not install into the game folder.\n\n" +
                    "If Windows Defender removed files, add your My Winter Car folder to exclusions and try again.",
                    "Not ready",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            RefreshStatus();
            return true;
        }

        private void LaunchGame(string args)
        {
            if (_game != null
                && MainDataBootPatch.TryDisableResolutionDialog(_game.GameDir, _game.BuildId, out string? bootPatch)
                && !string.IsNullOrWhiteSpace(bootPatch)
                && bootPatch != "Resolution dialog already disabled in mainData.")
            {
                AppendLog(bootPatch);
            }

            string launchArgs = UnityDisplayPrefs.WithScreenArgs(_settings, args);
            UnityDisplayPrefs.Apply(_settings, launchArgs);

            if (_game != null)
            {
                string fastBootNote = FastBootConfigSeed.ApplyProductionProfile(_game.GameDir);
                if (!string.Equals(fastBootNote, "FastBoot speed profile already set.", StringComparison.Ordinal))
                    AppendLog(fastBootNote);
            }

            string? steamExe = GameLocator.FindSteamExe();
            if (steamExe != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = $"-applaunch {GameLocator.AppId} {launchArgs}".TrimEnd(),
                    UseShellExecute = false,
                });
                return;
            }

            AppendLog("steam.exe not found — launching game directly (Steam MP may not work).");
            Process.Start(new ProcessStartInfo
            {
                FileName = _game!.ExePath,
                Arguments = launchArgs,
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
