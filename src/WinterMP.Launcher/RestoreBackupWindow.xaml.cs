using System.Windows;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class RestoreBackupWindow : Window
    {
        private sealed class BackupItem
        {
            public required SaveBackupEntry Entry { get; init; }
            public string Display => $"{Entry.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ({Entry.SizeBytes / 1024} KB)";
        }

        public string? ResultMessage { get; private set; }

        public RestoreBackupWindow()
        {
            InitializeComponent();
            foreach (var entry in SaveBackupService.ListBackups())
                BackupList.Items.Add(new BackupItem { Entry = entry });
            if (BackupList.Items.Count > 0)
                BackupList.SelectedIndex = 0;
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e) => SaveBackupService.OpenBackupFolder();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (BackupList.SelectedItem is not BackupItem item)
            {
                MessageBox.Show(this, "Select a backup first.", "Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(this,
                $"Restore save from {item.Entry.CreatedUtc.ToLocalTime():g}?\n\n" +
                "Your current save will be backed up first.",
                "Confirm restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                ResultMessage = SaveBackupService.RestoreBackup(item.Entry.FilePath);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
