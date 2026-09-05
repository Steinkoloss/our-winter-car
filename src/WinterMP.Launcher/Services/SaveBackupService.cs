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
            GameLauncher.RequireGameClosed();
            if (SaveDir is not { } saveDir || !Directory.Exists(saveDir)) return null;
            return CreateBackup(saveDir, BackupDir);
        }

        internal static string CreateBackup(string saveDir, string backupDir)
        {
            Directory.CreateDirectory(backupDir);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
            string target = Path.Combine(backupDir, $"save-{stamp}-{Guid.NewGuid():N}.zip");
            string partial = target + ".partial";
            try
            {
                ZipFile.CreateFromDirectory(saveDir, partial, CompressionLevel.Fastest, includeBaseDirectory: false);
                File.Move(partial, target);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
            PruneOldBackups(backupDir);
            return target;
        }

        public static string RestoreBackup(string zipPath)
        {
            GameLauncher.RequireGameClosed();
            if (SaveDir is not { } saveDir)
                throw new InvalidOperationException(
                    "Could not locate the My Winter Car save folder. Launch the game once so its (Proton) save folder exists.");
            return RestoreBackup(zipPath, saveDir, BackupDir);
        }

        internal static string RestoreBackup(string zipPath, string saveDir, string backupDir)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("Backup file not found.", zipPath);
            string parent = Path.GetDirectoryName(Path.GetFullPath(saveDir))!;
            string transaction = Path.Combine(parent, ".wintermp-restore-" + Guid.NewGuid().ToString("N"));
            string staged = Path.Combine(transaction, "new");
            string previous = Path.Combine(transaction, "previous");
            Directory.CreateDirectory(staged);
            bool moved = false, committed = false;
            try
            {
                // Validate/extract before touching the live save or pruning backups.
                // In particular, the selected oldest backup may be pruned by the safety backup.
                ZipFile.ExtractToDirectory(zipPath, staged);
                if (Directory.GetFiles(staged, "*", SearchOption.AllDirectories).Length == 0)
                    throw new InvalidDataException("The backup contains no save files.");
                string? safety = Directory.Exists(saveDir) ? CreateBackup(saveDir, backupDir) : null;
                if (Directory.Exists(saveDir))
                {
                    Directory.Move(saveDir, previous);
                    moved = true;
                }
                try { Directory.Move(staged, saveDir); }
                catch
                {
                    if (moved) Directory.Move(previous, saveDir);
                    moved = false;
                    throw;
                }
                committed = true;
                string safetyNote = safety != null ? $"Previous save backed up to {Path.GetFileName(safety)}."
                    : "No previous save to back up.";
                return $"Restored save from {Path.GetFileName(zipPath)}. {safetyNote}";
            }
            finally
            {
                if (!moved || committed)
                    try { Directory.Delete(transaction, recursive: true); } catch { }
            }
        }

        public static void OpenBackupFolder()
        {
            Directory.CreateDirectory(BackupDir);
            Platform.Current.OpenInShell(BackupDir);
        }

        private static void PruneOldBackups(string backupDir)
        {
            var files = new DirectoryInfo(backupDir).GetFiles("save-*.zip");
            if (files.Length <= MaxBackups) return;

            // Oldest-first by name (timestamp-encoded), matching ListBackups — see note there.
            Array.Sort(files, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < files.Length - MaxBackups; i++)
                files[i].Delete();
        }
    }
}
