using Avalonia.Controls;
using Avalonia.Interactivity;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class RestoreBackupWindow : Window
    {
        private sealed class BackupItem
        {
            public required SaveBackupEntry Entry { get; init; }

            public override string ToString() =>
                $"{Entry.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ({Entry.SizeBytes / 1024} KB)";
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

        private void OpenFolder_Click(object? sender, RoutedEventArgs e) =>
            SaveBackupService.OpenBackupFolder();

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private async void Restore_Click(object? sender, RoutedEventArgs e)
        {
            if (BackupList.SelectedItem is not BackupItem item)
            {
                await MessageBoxManager
                    .GetMessageBoxStandard("Restore", "Select a backup first.", ButtonEnum.Ok, MsBoxIcon.Warning)
                    .ShowWindowDialogAsync(this);
                return;
            }

            var result = await MessageBoxManager
                .GetMessageBoxStandard(
                    "Confirm restore",
                    $"Restore save from {item.Entry.CreatedUtc.ToLocalTime():g}?\n\nYour current save will be backed up first.",
                    ButtonEnum.YesNo,
                    MsBoxIcon.Warning)
                .ShowWindowDialogAsync(this);

            if (result != ButtonResult.Yes) return;

            try
            {
                ResultMessage = SaveBackupService.RestoreBackup(item.Entry.FilePath);
                Close(true);
            }
            catch (Exception ex)
            {
                await MessageBoxManager
                    .GetMessageBoxStandard("Restore failed", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error)
                    .ShowWindowDialogAsync(this);
            }
        }
    }
}
