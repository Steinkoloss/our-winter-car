using UnityEngine;
using WinterMP.Net;

namespace WinterMP.Core.Session
{
    /// <summary>
    /// State of one remote player in the current session.
    /// M2 adds the avatar GameObject + interpolation; for now this is bookkeeping
    /// for the player list and transform stream.
    /// </summary>
    public sealed class RemotePlayer
    {
        public byte PlayerId;
        public PeerId Peer;
        public ulong SteamId;
        public string Name = "?";

        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public byte MoveState;
        public ushort LastTransformSequence;
        public float LastTransformTime;

        /// <summary>Round-trip time in milliseconds, measured by the ping loop.</summary>
        public int PingMs = -1;
    }
}
