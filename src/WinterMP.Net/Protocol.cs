namespace WinterMP.Net
{
    /// <summary>Protocol-wide constants. Bump <see cref="Version"/> on every breaking wire change.</summary>
    public static class ProtocolInfo
    {
        // v2: world sync messages 40-42 (doors, bolt events, item transforms).
        // v3: time/weather sync (43) and join snapshot messages (120-122).
        // v4: passenger seats (23).
        public const ushort Version = 4;
    }

    /// <summary>
    /// Logical channels. The transport maps these to its native reliability modes
    /// (see PROTOCOL.md for the contract each channel guarantees).
    /// </summary>
    public enum Channel : byte
    {
        /// <summary>Events, RPCs, FSM transitions, economy, chat. Guaranteed, in order.</summary>
        ReliableOrdered = 0,

        /// <summary>Transform streams. Best effort; receivers drop stale packets via sequence numbers.</summary>
        UnreliableSequenced = 1,

        /// <summary>Join snapshots, save data, large transfers. Guaranteed, may be chunked.</summary>
        ReliableBulk = 2,
    }
}
