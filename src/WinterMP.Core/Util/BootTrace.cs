using System;
using System.IO;

namespace WinterMP.Core.Util
{
    /// <summary>
    /// Crash-proof breadcrumb trail: every call synchronously opens, appends and
    /// closes &lt;game&gt;\WinterMP\boot-trace.log. BepInEx's disk logger buffers
    /// writes (~5s flush timer), so a native crash during early boot leaves a
    /// 0-byte LogOutput.log. These crumbs survive and pinpoint the last step
    /// reached. Cheap enough to keep enabled permanently during development.
    /// </summary>
    internal static class BootTrace
    {
        private static string? _path;

        public static void Crumb(string message)
        {
            try
            {
                if (_path == null)
                {
                    string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
                    Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, "boot-trace.log");
                    File.AppendAllText(_path, $"---- new process {DateTime.Now:yyyy-MM-dd HH:mm:ss} ----{Environment.NewLine}");
                }

                File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
                // tracing must never take the process down
            }
        }
    }
}
