using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class SettingsWindow : Window
    {
        private readonly GameInstall? _game;
        private bool _loadingDisplaySettings;

        public LauncherSettings Settings { get; private set; }

        // Parameterless ctor for the Avalonia XAML runtime loader / previewer (silences AVLN3001).
        // Delegates to the real ctor with no game association, a state already supported below.
        public SettingsWindow() : this(LauncherSettings.Load(), null) { }

        public SettingsWindow(LauncherSettings settings, GameInstall? game)
        {
            InitializeComponent();
            Settings = settings;
            _game = game;
            GamePathBox.Text = settings.CustomGameDir ?? string.Empty;
            InitializeDisplaySettings();
        }

        private async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            // async void: an exception escaping here would reach the dispatcher with no global
            // handler and tear down the launcher, so contain picker failures like every other handler.
            try
            {
                var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select My Winter Car folder",
                    AllowMultiple = false,
                });

                if (result.Count > 0)
                    GamePathBox.Text = result[0].Path.LocalPath;
            }
            catch (Exception ex)
            {
                await MessageBoxManager
                    .GetMessageBoxStandard("Browse", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error)
                    .ShowWindowDialogAsync(this);
            }
        }

        private void InitializeDisplaySettings()
        {
            _loadingDisplaySettings = true;
            try
            {
                QualityCombo.ItemsSource = MwcDisplayOptions.QualityNames;
                QualityCombo.SelectedIndex = Math.Clamp(
                    Settings.GraphicsQuality,
                    0,
                    MwcDisplayOptions.QualityNames.Length - 1);

                var monitorLabels = MwcDisplayOptions.GetMonitorLabels();
                MonitorCombo.ItemsSource = monitorLabels;
                MonitorCombo.SelectedIndex = Math.Clamp(
                    Settings.MonitorIndex,
                    0,
                    Math.Max(0, monitorLabels.Count - 1));

                RefreshResolutionCombo(Settings.DisplayWidth, Settings.DisplayHeight);
                WindowedCheck.IsChecked = Settings.Windowed;
            }
            finally
            {
                _loadingDisplaySettings = false;
            }
        }

        private void RefreshResolutionCombo(int preferredWidth, int preferredHeight)
        {
            int monitorIndex = MonitorCombo.SelectedIndex >= 0
                ? MonitorCombo.SelectedIndex
                : Settings.MonitorIndex;

            IReadOnlyList<ResolutionOption> options =
                MwcDisplayOptions.GetResolutionsForMonitor(monitorIndex);

            ResolutionOption? selected =
                MwcDisplayOptions.FindResolution(options, preferredWidth, preferredHeight);

            if (selected == null)
            {
                selected = new ResolutionOption(preferredWidth, preferredHeight);
                options = options.Prepend(selected).ToList();
            }

            ResolutionCombo.ItemsSource = options;
            ResolutionCombo.SelectedItem = selected;
        }

        private void MonitorCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_loadingDisplaySettings) return;

            int width = Settings.DisplayWidth;
            int height = Settings.DisplayHeight;
            if (ResolutionCombo.SelectedItem is ResolutionOption current)
            {
                width = current.Width;
                height = current.Height;
            }

            _loadingDisplaySettings = true;
            try
            {
                RefreshResolutionCombo(width, height);
            }
            finally
            {
                _loadingDisplaySettings = false;
            }

            SaveDisplaySettings();
        }

        private void DisplaySetting_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loadingDisplaySettings) return;
            SaveDisplaySettings();
        }

        private void Resolution_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_loadingDisplaySettings) return;
            SaveDisplaySettings();
        }

        private void SaveDisplaySettings()
        {
            if (ResolutionCombo.SelectedItem is ResolutionOption resolution)
            {
                Settings.DisplayWidth = resolution.Width;
                Settings.DisplayHeight = resolution.Height;
            }

            Settings.Windowed = WindowedCheck.IsChecked == true;
            Settings.GraphicsQuality = QualityCombo.SelectedIndex >= 0
                ? QualityCombo.SelectedIndex
                : UnityDisplayPrefs.DefaultQuality;
            Settings.MonitorIndex = MonitorCombo.SelectedIndex >= 0
                ? MonitorCombo.SelectedIndex
                : UnityDisplayPrefs.DefaultMonitor;
            Settings.DisplaySettingsSaved = true;
        }

        private async void RepairMod_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var game = GameLocator.FindInstall(GamePathBox.Text);
                if (game == null) throw new InvalidOperationException("Select the folder containing mywintercar.exe first.");
                string result = BepInExInstaller.InstallOrRepair(game.GameDir);
                Settings.AutoInstallEnabled = true;
                Settings.CustomGameDir = GamePathBox.Text?.Trim();
                Settings.Save();
                await MessageBoxManager.GetMessageBoxStandard("Install / Repair", result, ButtonEnum.Ok, MsBoxIcon.Info)
                    .ShowWindowDialogAsync(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Install / Repair", ex.Message, ButtonEnum.Ok, MsBoxIcon.Warning)
                    .ShowWindowDialogAsync(this);
            }
        }

        private async void RemoveMod_Click(object? sender, RoutedEventArgs e)
        {
            if (_game == null)
            {
                await MessageBoxManager
                    .GetMessageBoxStandard("Remove mod", "Game folder not found.", ButtonEnum.Ok, MsBoxIcon.Warning)
                    .ShowWindowDialogAsync(this);
                return;
            }

            var confirm = await MessageBoxManager
                .GetMessageBoxStandard(
                    "Remove mod",
                    $"Remove {Branding.ProductName} from your game folder?\n\nBepInEx will remain installed.",
                    ButtonEnum.YesNo,
                    MsBoxIcon.Warning)
                .ShowWindowDialogAsync(this);

            if (confirm != ButtonResult.Yes) return;

            try
            {
                string result = ModRemoval.RemoveFromGame(_game.GameDir);
                Settings.AutoInstallEnabled = false;
                Settings.Save();
                await MessageBoxManager
                    .GetMessageBoxStandard("Remove mod", result, ButtonEnum.Ok, MsBoxIcon.Info)
                    .ShowWindowDialogAsync(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager
                    .GetMessageBoxStandard("Remove mod failed", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error)
                    .ShowWindowDialogAsync(this);
            }
        }

        private void Done_Click(object? sender, RoutedEventArgs e)
        {
            string text = GamePathBox.Text?.Trim() ?? string.Empty;
            Settings.CustomGameDir = string.IsNullOrEmpty(text) ? null : text;
            SaveDisplaySettings();
            Settings.Save();
            Close(true);
        }
    }
}
