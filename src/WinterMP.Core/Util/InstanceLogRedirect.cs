using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace WinterMP.Core.Util
{
    /// <summary>
    /// Local two-instance test: when <c>WINTERMP_LOG_ROLE</c> is set (host/guest),
    /// swap BepInEx's disk log to <c>LogOutput-{role}.log</c> so both processes
    /// keep separate logs under <c>BepInEx/</c>.
    /// </summary>
    internal static class InstanceLogRedirect
    {
        public const string RoleEnvVar = "WINTERMP_LOG_ROLE";

        private static bool _configured;

        public static string? Role { get; private set; }

        public static void TryConfigure()
        {
            if (_configured) return;
            _configured = true;

            string? raw = Environment.GetEnvironmentVariable(RoleEnvVar);
            if (string.IsNullOrEmpty(raw)) return;

            string role = SanitizeRole(raw);
            if (role.Length == 0) return;

            Role = role;
            ReplaceDiskListener("LogOutput-" + role + ".log");
            WinterMPPlugin.Log.LogInfo("Instance log: BepInEx\\LogOutput-" + role + ".log");
        }

        private static string SanitizeRole(string role)
        {
            var chars = new char[role.Length];
            int n = 0;
            for (int i = 0; i < role.Length; i++)
            {
                char c = role[i];
                if ((c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '-'
                    || c == '_')
                {
                    chars[n++] = c;
                }
            }

            return n == 0 ? string.Empty : new string(chars, 0, n);
        }

        private static void ReplaceDiskListener(string localPath)
        {
            var listeners = Logger.Listeners;
            ILogListener? existing = null;

            foreach (ILogListener listener in listeners)
            {
                Type t = listener.GetType();
                if (t == typeof(DiskLogListener)
                    || string.Equals(t.Name, "DiskLogListener", StringComparison.Ordinal))
                {
                    existing = listener;
                    break;
                }
            }

            if (existing != null)
            {
                listeners.Remove(existing);
                if (existing is IDisposable disposable)
                    disposable.Dispose();
            }

            string absolutePath = Path.Combine(BepInEx.Paths.BepInExRootPath, localPath);
            // LogLevel is a FLAGS mask here, not a threshold: passing LogLevel.Debug
            // wrote ONLY [Debug] lines and silently dropped Info/Warning/Error from
            // the per-role logs (the guest log was typically 0 bytes).
            listeners.Add(new DiskLogListener(absolutePath, LogLevel.All, false, true));
        }
    }
}
