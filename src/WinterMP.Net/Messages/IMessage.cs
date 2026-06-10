namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Wire message ids. Never reuse a retired id; append within the reserved range
    /// of each subsystem (see PROTOCOL.md).
    /// </summary>
    public enum MessageId : ushort
    {
        // 1-9: session lifecycle
        HandshakeRequest = 1,
        HandshakeResponse = 2,
        Ping = 3,
        Pong = 4,
        Disconnect = 5,

        // 10-19: social
        Chat = 10,

        // 20-39: players
        PlayerSpawn = 20,
        PlayerDespawn = 21,
        PlayerTransform = 22,
        PassengerState = 23,

        // 40-59: world events (doors, switches, pickables) — M3
        FsmStateEnter = 40,
        FsmRawEvent = 41,
        ItemTransform = 42,
        TimeSync = 43,

        // 120-139: snapshots/bulk — M3 join snapshot
        WorldSnapshotRequest = 120,
        WorldDoorSnapshot = 121,
        WorldItemSnapshot = 122,

        // 60+: reserved for future subsystems:
        //   60-79 vehicles
        //   80-99 economy
        //   100-119 NPCs
    }

    public interface IMessage
    {
        MessageId Id { get; }
        void Write(NetWriter writer);
        void Read(NetReader reader);
    }
}
