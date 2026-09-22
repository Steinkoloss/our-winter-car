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
                _writer!.WriteLine($"[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] pid={System.Diagnostics.Process.GetCurrentProcess().Id} role={InstanceLogRedirect.Role} {message}");
                _writer.Flush();
            }
            catch
            {
                // tracing must never take the process down
            }
        }

        public static void Step(string name, Action action)
        {
            Crumb(name + " begin");
            try
            {
                action();
                Crumb(name + " end");
            }
            catch (Exception error)
            {
                Error(name, error);
                throw; // Diagnostics must not turn a failed initialization into ready.
            }
        }

        public static void Error(string name, Exception error)
        {
            try
            {
                string detail = error.ToString();
                if (detail.Length > 2048) detail = detail.Substring(0, 2048);
                Crumb(name + " error " + detail.Replace("\r", " ").Replace("\n", " "));
            }
            catch { }
        }

        private static void EnsureWriter()
        {
            if (_writer != null) return;

            string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
            Directory.CreateDirectory(dir);
            string fileName = string.IsNullOrEmpty(InstanceLogRedirect.Role)
                ? "boot-trace.log"
                : "boot-trace-" + InstanceLogRedirect.Role + ".log";
            string path = Path.Combine(dir, fileName);
            _writer = new StreamWriter(path, true, Encoding.UTF8);
            _writer.WriteLine($"---- new process {DateTime.Now:yyyy-MM-dd HH:mm:ss} ----");
            _writer.Flush();
        }
    }
}
