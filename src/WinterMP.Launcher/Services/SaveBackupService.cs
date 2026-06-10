using System.IO;
using System.IO.Compression;

namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// Zips the game save folder before hosted sessions. Backups are the safety net for
    /// the "host owns the savefile" model (PLAN.md §4.6).
    /// </summary>
    public static class SaveBackupService
    {
        private const int MaxBackups = 20;

        // TODO(M1): verify the exact folder name on a real install; MSC used
        // ...\LocalLow\Amistech\My Summer Car.
        private static string SaveDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Amistech", "My Winter Car");

        private static string BackupDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterMP", "backups");

        public static bool SaveDirExists() => Directory.Exists(SaveDir);

        public static int CountBackups()
        {
            return Directory.Exists(BackupDir)
                ? Directory.GetFiles(BackupDir, "save-*.zip").Length
                : 0;
        }

        /// <summary>Returns the backup path, or null when there is no save folder yet.</summary>
        public static string? CreateBackup()
        {
            if (!SaveDirExists()) return null;

            Directory.CreateDirectory(BackupDir);
            string target = Path.Combine(BackupDir, $"save-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(SaveDir, target, CompressionLevel.Fastest, includeBaseDirectory: false);

            PruneOldBackups();
            return target;
        }

        private static void PruneOldBackups()
        {
            var files = new DirectoryInfo(BackupDir).GetFiles("save-*.zip");
            if (files.Length <= MaxBackups) return;

            Array.Sort(files, (a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));
            for (int i = 0; i < files.Length - MaxBackups; i++)
                files[i].Delete();
        }
    }
}
