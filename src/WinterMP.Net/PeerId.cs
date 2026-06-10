using System;

namespace WinterMP.Net
{
    /// <summary>
    /// Identifies a remote peer. For the Steam transport this is the peer's SteamID64;
    /// for the loopback transport it is a small synthetic value.
    /// </summary>
    public readonly struct PeerId : IEquatable<PeerId>
    {
        public readonly ulong Value;

        public PeerId(ulong value) => Value = value;

        public bool Equals(PeerId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PeerId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(PeerId a, PeerId b) => a.Value == b.Value;
        public static bool operator !=(PeerId a, PeerId b) => a.Value != b.Value;
    }
}
