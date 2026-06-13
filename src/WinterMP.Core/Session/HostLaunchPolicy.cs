namespace WinterMP.Core.Session
{
    /// <summary>
    /// FastBoot sets this from <c>Boot.BypassHostContinueWait</c>. Host session start
    /// reads it instead of always waiting for a guest on the main menu.
    /// </summary>
    internal static class HostLaunchPolicy
    {
        public static bool BypassPlayerGate { get; set; } = true;
    }
}
