using System.Windows;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class InfoWindow : Window
    {
        private readonly MainWindow _owner;

        public InfoWindow(MainWindow owner)
        {
            InitializeComponent();
            _owner = owner;
        }

        public void ApplySnapshot(LauncherInfoSnapshot snapshot, UpdateCheckResult? pendingUpdate)
        {
            GameStatusText.Text = snapshot.GameStatus;
            BepInExStatusText.Text = snapshot.BepInExStatus;
            ModStatusText.Text = snapshot.ModStatus;
            BackupStatusText.Text = snapshot.BackupStatus;
            BuildStatusText.Text = snapshot.BuildStatus;
            BuildStatusText.ToolTip = snapshot.GameDirTooltip;
            UpdateStatusText.Text = snapshot.UpdateStatus;
            CheckButton.IsEnabled = !_owner.IsUpdateBusy;
        }

        private async void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            CheckButton.IsEnabled = false;
            try
            {
                await _owner.CheckForUpdatesAsync(showUpToDate: true);
                ApplySnapshot(_owner.BuildInfoSnapshot(), _owner.PendingUpdate);
            }
            finally
            {
                CheckButton.IsEnabled = !_owner.IsUpdateBusy;
            }
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
