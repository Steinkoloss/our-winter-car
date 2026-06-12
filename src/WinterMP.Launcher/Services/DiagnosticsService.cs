using System.IO;
using System.IO.Compression;
using System.Text;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher.Services
{
    public static class DiagnosticsService
    {
        public static string CreateBundle(GameInstall? game, string launcherLog)
        {
            string outDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinterMP", "diagnostics");
            Directory.CreateDirectory(outDir);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string zipPath = Path.Combine(outDir, $"wintermp-diag-{stamp}.zip");

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                AddText(zip, "launcher-log.txt", launcherLog);
                AddText(zip, "launcher-version.txt", ModPayload.LauncherVersion);

                if (game != null)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"GameDir: {game.GameDir}");
                    sb.AppendLine($"BuildId: {game.BuildId}");
                    sb.AppendLine($"BepInEx: {BepInExInstaller.GetStatus(game.GameDir)}");
                    sb.AppendLine($"Mod: {BepInExInstaller.GetInstalledModVersion(game.GameDir) ?? "missing"}");
                    AddText(zip, "install-status.txt", sb.ToString());

                    string winterMpDir = Path.Combine(game.GameDir, "WinterMP");
                    TryAddFile(zip, Path.Combine(game.GameDir, "BepInEx", "LogOutput.log"), "BepInEx-LogOutput.log");
                    TryAddFile(zip, Path.Combine(game.GameDir, "BepInEx", "config", "BepInEx.cfg"), "BepInEx.cfg");
                    TryAddFile(zip, Path.Combine(game.GameDir, "BepInEx", "plugins", "WinterMP", "WinterMP.Core.dll"), "WinterMP.Core.dll");
                    TryAddFile(zip, Path.Combine(game.GameDir, "BepInEx", "plugins", "WinterMP", "sync-catalog.json"), "sync-catalog.json");
                    TryAddFile(zip, Path.Combine(winterMpDir, "diagnostics.log"), "WinterMP-diagnostics.log");
                    TryAddFile(zip, Path.Combine(winterMpDir, "sync-events.log"), "WinterMP-sync-events.log");
                    TryAddFile(zip, Path.Combine(winterMpDir, "boot-trace.log"), "WinterMP-boot-trace.log");
                    TryAddRecentSyncEvents(zip, winterMpDir);
                }
            }

            return zipPath;
        }

        private static void AddText(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        private static void TryAddFile(ZipArchive zip, string path, string entryName)
        {
            if (!File.Exists(path)) return;
            zip.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
        }

        /// <summary>Tail of sync-events.log when the in-game ring buffer was not flushed (F7).</summary>
        private static void TryAddRecentSyncEvents(ZipArchive zip, string winterMpDir)
        {
            string path = Path.Combine(winterMpDir, "sync-events.log");
            if (!File.Exists(path)) return;

            try
            {
                string[] lines = File.ReadAllLines(path);
                int start = Math.Max(0, lines.Length - 128);
                var sb = new StringBuilder();
                sb.AppendLine("=== last 128 lines of sync-events.log ===");
                for (int i = start; i < lines.Length; i++)
                    sb.AppendLine(lines[i]);
                AddText(zip, "WinterMP-sync-events-recent.txt", sb.ToString());
            }
            catch
            {
                // Best-effort tail for bug reports.
            }
        }
    }
}
