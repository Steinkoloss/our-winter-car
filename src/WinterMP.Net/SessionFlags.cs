namespace WinterMP.Net
{
    /// <summary>Bit flags on <see cref="Messages.HandshakeResponse.SessionFlags"/>.</summary>
    public static class SessionFlags
    {
        /// <summary>Host save has permadeath enabled — guests mirror this; one death ends the session for everyone.</summary>
        public const byte PermadeathEnabled = 1;
    }
}
