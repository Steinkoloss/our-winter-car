using System.Windows;
using Microsoft.Win32;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class SettingsWindow : Window
    {
        private readonly GameInstall? _game;
        public LauncherSettings Settings { get; private set; }

        public SettingsWindow(LauncherSettings settings, GameInstall? game)
        {
            InitializeComponent();
            Settings = settings;
            _game = game;
            GamePathBox.Text = settings.CustomGameDir ?? string.Empty;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select My Winter Car folder",
            };

            if (dialog.ShowDialog() == true)
                GamePathBox.Text = dialog.FolderName;
        }

        private void RemoveMod_Click(object sender, RoutedEventArgs e)
        {
            if (_game == null)
            {
                MessageBox.Show(this, "Game folder not found.", "Remove mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(this,
                "Remove the WinterMP mod from your game folder?\n\nBepInEx will remain installed.",
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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string text = GamePathBox.Text.Trim();
            Settings.CustomGameDir = string.IsNullOrEmpty(text) ? null : text;
            Settings.Save();
            DialogResult = true;
            Close();
        }
    }
}
