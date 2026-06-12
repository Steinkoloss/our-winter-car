using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace WinterMP.Core.Diagnostics
{
    /// <summary>
    /// Ring buffer of structured multiplayer sync events (M7). Dump with F7 or after
    /// a fatal sync error — feeds launcher bug-report zips alongside diagnostics.log.
    /// </summary>
    internal static class SyncEventLog
    {
        private const int MaxEntries = 256;

        private static readonly Queue<string> Entries = new Queue<string>(MaxEntries);
        private static readonly object Gate = new object();

        public static void Record(string category, string detail)
        {
            if (string.IsNullOrEmpty(category)) category = "?";
            if (detail == null) detail = string.Empty;

            string line = $"{Time.unscaledTime,8:0.0}s  {category,-10}  {detail}";
            lock (Gate)
            {
                while (Entries.Count >= MaxEntries)
                    Entries.Dequeue();
                Entries.Enqueue(line);
            }
        }

        public static void DumpToLog()
        {
            var sb = new StringBuilder(Entries.Count * 64 + 64);
            sb.AppendLine($"=== WinterMP sync event log ({Entries.Count} entries) ===");
            lock (Gate)
            {
                foreach (string line in Entries)
                    sb.AppendLine(line);
            }

            WinterMPPlugin.Log.LogInfo(sb.ToString());
        }

        public static void DumpToFile()
        {
            string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
            string path = Path.Combine(dir, "sync-events.log");
            var sb = new StringBuilder();
            sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            lock (Gate)
            {
                foreach (string line in Entries)
                    sb.AppendLine(line);
            }

            sb.AppendLine();

            try
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(path, sb.ToString());
                WinterMPPlugin.Log.LogInfo($"Sync event log written to {path}.");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"Could not write sync event log: {e.Message}");
            }
        }

        public static void Clear()
        {
            lock (Gate)
                Entries.Clear();
        }

        /// <summary>Recent in-memory events for launcher diagnostics (even if not flushed to disk).</summary>
        public static string GetSnapshot()
        {
            var sb = new StringBuilder(Entries.Count * 64 + 32);
            sb.AppendLine($"=== sync events ({Entries.Count} in memory) ===");
            lock (Gate)
            {
                foreach (string line in Entries)
                    sb.AppendLine(line);
            }

            return sb.ToString();
        }
    }
}
