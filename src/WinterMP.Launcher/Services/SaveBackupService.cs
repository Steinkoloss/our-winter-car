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

            // Order by the timestamp embedded in the file name, not CreationTimeUtc — birth
            // time is unreliable on Linux (ext4 without statx birthtime), which would scramble
            // ordering and let PruneOldBackups delete the wrong backup. LastWriteTimeUtc is
            // reliable cross-platform for these write-once zips and feeds the displayed time.
            return Directory.GetFiles(BackupDir, "save-*.zip")
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Select(f => new SaveBackupEntry(f.FullName, f.LastWriteTimeUtc, f.Length))
                .ToList();
        }

        /// <summary>Returns the backup path, or null when there is no save folder yet.</summary>
        public static string? CreateBackup()
        {
            if (SaveDir is not { } saveDir || !Directory.Exists(saveDir)) return null;

            Directory.CreateDirectory(BackupDir);
            // ZipFile.CreateFromDirectory opens the target with FileMode.CreateNew and throws if
            // it exists. Two backups in the same second (manual Backup then Host, or restore's
            // safety backup) would otherwise crash and skip the pre-host save snapshot, so make
            // the name unique.
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string target = Path.Combine(BackupDir, $"save-{stamp}.zip");
            for (int n = 1; File.Exists(target); n++)
                target = Path.Combine(BackupDir, $"save-{stamp}-{n}.zip");
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

            // Oldest-first by name (timestamp-encoded), matching ListBackups — see note there.
            Array.Sort(files, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < files.Length - MaxBackups; i++)
                files[i].Delete();
        }
    }
}
