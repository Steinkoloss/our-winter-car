namespace WinterMP.Core.Session
{
    /// <summary>
    /// Launcher passes <c>-wintermp-fast</c> on host/join boots: start Steam and session
    /// setup as early as possible instead of waiting for main-menu settle timers.
    /// </summary>
    internal static class SessionLaunchPolicy
    {
        public static bool FastSessionLaunch { get; set; }
    }
}
