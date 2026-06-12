using System;
using System.IO;
using System.Text;

namespace WinterMP.Core.Util
{
    /// <summary>
    /// Crash-proof breadcrumb trail: every call appends to
    /// &lt;game&gt;\WinterMP\boot-trace.log. BepInEx's disk logger buffers
    /// writes (~5s flush timer), so a native crash during early boot leaves a
    /// 0-byte LogOutput.log. These crumbs survive and pinpoint the last step
    /// reached. A single kept-open writer avoids per-crumb open/close overhead.
    /// </summary>
    internal static class BootTrace
    {
        private static StreamWriter? _writer;

        public static void Crumb(string message)
        {
            try
            {
                EnsureWriter();
                _writer!.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                _writer.Flush();
            }
            catch
            {
                // tracing must never take the process down
            }
        }

        private static void EnsureWriter()
        {
            if (_writer != null) return;

            string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "boot-trace.log");
            _writer = new StreamWriter(path, true, Encoding.UTF8);
            _writer.WriteLine($"---- new process {DateTime.Now:yyyy-MM-dd HH:mm:ss} ----");
            _writer.Flush();
        }
    }
}
