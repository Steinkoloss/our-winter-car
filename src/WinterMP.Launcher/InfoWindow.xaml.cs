using System.Windows;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class InfoWindow : Window
    {
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
            BuildStatusText.ToolTip = snapshot.GameDirTooltip;
            UpdateStatusText.Text = snapshot.UpdateStatus;
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
