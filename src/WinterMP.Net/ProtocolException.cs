using System;

namespace WinterMP.Net
{
    /// <summary>
    /// Raised on malformed/unknown wire data. Callers must treat this as a recoverable,
    /// per-packet failure (log + drop packet, optionally disconnect the peer) — never crash the game.
    /// </summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message)
        {
        }
    }
}
