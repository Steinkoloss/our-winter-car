using System;
using System.IO;

namespace WinterMP.Core.Util
{
    /// <summary>
    /// Written as soon as the host-local test instance releases Unity's
    /// single-instance mutex so <c>tools/Local2PTest.bat</c> can poll instead of
    /// sleeping for a fixed interval.
    /// </summary>
    internal static class HostLocalReadySignal
    {
        public const string FileName = "hostlocal-ready.flag";

        public static void MarkReady()
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, FileName);
                File.WriteAllText(path, DateTime.UtcNow.Ticks.ToString());
                WinterMPPlugin.Log.LogInfo($"HostLocal ready signal written: {path}");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"HostLocal ready signal not written: {e.Message}");
            }
        }

        public static bool IsPresent()
        {
            try
            {
                return File.Exists(GetPath());
            }
            catch
            {
                return false;
            }
        }

        private static string GetPath()
        {
            string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
            return Path.Combine(dir, FileName);
        }
    }
}
