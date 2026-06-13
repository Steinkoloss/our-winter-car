namespace WinterMP.Net
{
    /// <summary>Protocol-wide constants. Bump <see cref="Version"/> on every breaking wire change.</summary>
    public static class ProtocolInfo
    {
        // v2: world sync messages 40-42 (doors, bolt events, item transforms).
        // v3: time/weather sync (43) and join snapshot messages (120-122).
        // v4: passenger seats (23).
        // v5: vehicle engine state (60).
        // v6: VehicleState gains speedTenthsKmh; ignition uses electricity FSM replay.
        // v7: vehicle climate stream (61) — frost + heater knobs.
        // v8: VehicleState gains fuelLevel + blinker flags.
        // v9: VehicleClimate gains fog + cabinTemp (interior window fogging).
        // v10: VehicleState gains hazard flag + coolantTemp gauge.
        // v11: TimeSync gains daysPassed/dayOfWeek; bolt tightness snapshot + BoltState.
        // v12: shared wallet (WalletState) + car part Bolted/Unbolted sync.
        // v13: part assembly states (Stop/Install/Remove) + PartState + WorldPartSnapshot.
        // v14: host-authoritative shop purchases (PurchaseIntent).
        // v15: pickable food/consumables + ItemDespawn when eaten or destroyed.
        // v16: drink despawn states + WorldItemDespawnSnapshot for mid-session joiners.
        // v17: periodic world-state checksums + guest soft-resync requests.
        // v18: checksums gain item + vehicle CRCs; soft resync covers item/vehicle groups.
        // v19: per-object state requests + ownership-transfer pause on bad links (client-side).
        // v20: GuestSpawn (24) — host tells joiners where to place PLAYER after snapshot.
        // v21: GuestSpawn carries host + last saved pose; guest picks locally.
        // v22: GuestSpawn adds saved needs; PlayerNeedsReport + sleep consent messages.
        // v23: SleepConsentResult (28) — host notifies guests when sleep round completes.
        // v24: HandshakeResponse sessionFlags; PlayerDeathReport/Event/Respawn (29-31).
        // v25: NpcTransform (100) — host streams NPC/traffic rigidbody poses to guests.
        public const ushort Version = 25;
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
