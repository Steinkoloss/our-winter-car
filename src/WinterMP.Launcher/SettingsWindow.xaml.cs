using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class SettingsWindow : Window
    {
        private readonly GameInstall? _game;
        private bool _loadingDisplaySettings;

        public LauncherSettings Settings { get; private set; }

        public SettingsWindow(LauncherSettings settings, GameInstall? game)
        {
            InitializeComponent();
            Settings = settings;
            _game = game;
            GamePathBox.Text = settings.CustomGameDir ?? string.Empty;
            InitializeDisplaySettings();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select My Winter Car folder" };
            if (dialog.ShowDialog() == true)
                GamePathBox.Text = dialog.FolderName;
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

                MonitorCombo.ItemsSource = MwcDisplayOptions.GetMonitorLabels();
                MonitorCombo.SelectedIndex = Math.Clamp(
                    Settings.MonitorIndex,
                    0,
                    Math.Max(0, MonitorCombo.Items.Count - 1));

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
            int monitorIndex = MonitorCombo.SelectedIndex >= 0 ? MonitorCombo.SelectedIndex : Settings.MonitorIndex;
            IReadOnlyList<ResolutionOption> options = MwcDisplayOptions.GetResolutionsForMonitor(monitorIndex);

            ResolutionOption? selected = MwcDisplayOptions.FindResolution(options, preferredWidth, preferredHeight);
            if (selected == null)
            {
                selected = new ResolutionOption(preferredWidth, preferredHeight);
                options = options.Prepend(selected).ToList();
            }

            ResolutionCombo.ItemsSource = options;
            ResolutionCombo.SelectedItem = selected;
        }

        private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

            DisplaySetting_Changed(sender, e);
        }

        private void DisplaySetting_Changed(object sender, RoutedEventArgs e)
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

        private void SaveGamePath()
        {
            string text = GamePathBox.Text.Trim();
            Settings.CustomGameDir = string.IsNullOrEmpty(text) ? null : text;
        }

        private void RemoveMod_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null)
            {
                MessageBox.Show(this, "Game folder not found.", "Remove mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(this,
                $"Remove {Branding.ProductName} from your game folder?\n\nBepInEx will remain installed.",
                "Remove mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                string result = ModRemoval.RemoveFromGame(_game.GameDir);
                MessageBox.Show(this, result, "Remove mod", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Remove mod failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            SaveGamePath();
            SaveDisplaySettings();
            Settings.Save();
            DialogResult = true;
            Close();
        }
    }
}
