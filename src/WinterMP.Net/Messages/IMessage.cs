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
        /// <summary>Host -> joining guest: feet position + yaw after join snapshot.</summary>
        GuestSpawn = 24,
        /// <summary>Guest -> host: hunger/fatigue/thirst/urine snapshot.</summary>
        PlayerNeedsReport = 25,
        /// <summary>Host -> guests: sleep / time-skip consent round.</summary>
        SleepConsentRequest = 26,
        /// <summary>Guest -> host: sleep consent answer.</summary>
        SleepConsentResponse = 27,
        /// <summary>Host -> guests: sleep consent round finished.</summary>
        SleepConsentResult = 28,
        /// <summary>Any player -> host: local death started.</summary>
        PlayerDeathReport = 29,
        /// <summary>Host -> all: player died or permadeath wipe.</summary>
        PlayerDeathEvent = 30,
        /// <summary>Player -> all: non-permadeath respawn complete.</summary>
        PlayerRespawn = 31,
        /// <summary>Any player -> host -> others: worn clothing (stage/type) changed.</summary>
        PlayerClothingState = 32,

        // 40-59: world events (doors, switches, pickables) — M3
        FsmStateEnter = 40,
        FsmRawEvent = 41,
        ItemTransform = 42,
        TimeSync = 43,
        BoltState = 44,
        PartState = 45,
        ItemDespawn = 46,
        WorldStateChecksum = 47,
        WorldResyncRequest = 48,
        WorldObjectStateRequest = 49,
        /// <summary>Host -> all: shared heat source lit/fuel/heat/sauna-temp (§4.8).</summary>
        HeatSourceState = 50,
        /// <summary>Guest -> host: light / feed wood / grill / löyly on a heat source.</summary>
        HeatSourceIntent = 51,
        /// <summary>Host -> guests: manifest of items a container FSM (grocery bag) just spawned.</summary>
        ItemSpawn = 52,
        /// <summary>Guest -> host: "I opened a grocery bag; you spawn it authoritatively".</summary>
        SpawnIntent = 53,

        // 60-79: vehicles — M4
        VehicleState = 60,
        VehicleClimate = 61,
        VehicleCargo = 62,

        // 80-99: economy — M5
        WalletState = 80,
        PurchaseIntent = 81,

        // 100-119: NPCs — M6
        NpcTransform = 100,

        // 120-139: snapshots/bulk — M3 join snapshot
        WorldSnapshotRequest = 120,
        WorldDoorSnapshot = 121,
        WorldItemSnapshot = 122,
        WorldBoltSnapshot = 123,
        WorldPartSnapshot = 124,
        WorldItemDespawnSnapshot = 125,

        // Reserved ranges for future subsystems:
        //   63-79 vehicles (attachment, fuel/damage)
        //   82-99 economy
        //   101-119 NPCs/jobs (NpcTransform = 100)
        //   126-139 snapshot/bulk transfer control
    }

    public interface IMessage
    {
        MessageId Id { get; }
        void Write(NetWriter writer);
        void Read(NetReader reader);
    }
}
