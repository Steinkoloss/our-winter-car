using Avalonia.Controls;
using Avalonia.Interactivity;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class InfoWindow : Window
    {
        public event Action? ExportDiagnosticsRequested;
        public event Action? TesterGuideRequested;
        public InfoWindow()
        {
            InitializeComponent();
        }

        public void ApplySnapshot(LauncherInfoSnapshot snapshot)
        {
            GameStatusText.Text = snapshot.GameStatus;
            BepInExStatusText.Text = snapshot.BepInExStatus;
            ModStatusText.Text = snapshot.ModStatus;
            BackupStatusText.Text = snapshot.BackupStatus;
            BuildStatusText.Text = snapshot.BuildStatus;
            ToolTip.SetTip(BuildStatusText, snapshot.GameDirTooltip);
            UpdateStatusText.Text = snapshot.UpdateStatus;
        }

        private void Done_Click(object? sender, RoutedEventArgs e) => Close(true);
        private void ExportDiagnostics_Click(object? sender, RoutedEventArgs e) => ExportDiagnosticsRequested?.Invoke();
        private void TesterGuide_Click(object? sender, RoutedEventArgs e) => TesterGuideRequested?.Invoke();
    }
}
