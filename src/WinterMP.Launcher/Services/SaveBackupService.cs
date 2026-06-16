using System.IO;
using System.IO.Compression;

namespace WinterMP.Launcher.Services
{
    public sealed record SaveBackupEntry(string FilePath, DateTime CreatedUtc, long SizeBytes);

    /// <summary>
    /// Zips the game save folder before hosted sessions. Backups are the safety net for
    /// the "host owns the savefile" model (PLAN.md §4.6).
    /// </summary>
    public static class SaveBackupService
    {
        private const int MaxBackups = 20;

        /// <summary>The game save folder. On Linux this resolves inside the Proton prefix.</summary>
        private static string? SaveDir => Platform.Current.GetGameSaveDir();

        private static string BackupDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterMP", "backups");

        public static bool SaveDirExists() => SaveDir is { } dir && Directory.Exists(dir);

        public static int CountBackups() => ListBackups().Count;

        public static IReadOnlyList<SaveBackupEntry> ListBackups()
        {
            if (!Directory.Exists(BackupDir)) return Array.Empty<SaveBackupEntry>();

            return Directory.GetFiles(BackupDir, "save-*.zip")
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => new SaveBackupEntry(f.FullName, f.CreationTimeUtc, f.Length))
                .ToList();
        }

        /// <summary>Returns the backup path, or null when there is no save folder yet.</summary>
        public static string? CreateBackup()
        {
            if (SaveDir is not { } saveDir || !Directory.Exists(saveDir)) return null;

            Directory.CreateDirectory(BackupDir);
            string target = Path.Combine(BackupDir, $"save-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(saveDir, target, CompressionLevel.Fastest, includeBaseDirectory: false);

            PruneOldBackups();
            return target;
        }

        public static string RestoreBackup(string zipPath)
        {
            if (!File.Exists(zipPath))
                throw new FileNotFoundException("Backup file not found.", zipPath);

            if (SaveDir is not { } saveDir)
                throw new InvalidOperationException(
                    "Could not locate the My Winter Car save folder. Launch the game once so its (Proton) save folder exists.");

            if (!Directory.Exists(saveDir))
                Directory.CreateDirectory(saveDir);

            string? safety = CreateBackup();
            string safetyNote = safety != null
                ? $"Current save backed up to {Path.GetFileName(safety)} before restore."
                : "No existing save to back up before restore.";

            foreach (string file in Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories))
                File.Delete(file);
            foreach (string dir in Directory.GetDirectories(saveDir))
                Directory.Delete(dir, recursive: true);

            ZipFile.ExtractToDirectory(zipPath, saveDir, overwriteFiles: true);
            return $"Restored save from {Path.GetFileName(zipPath)}. {safetyNote}";
        }

        public static void OpenBackupFolder()
        {
            Directory.CreateDirectory(BackupDir);
            Platform.Current.OpenInShell(BackupDir);
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
