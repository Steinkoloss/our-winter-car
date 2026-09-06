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
        /// <summary>Owner -> host -> others: liquid amount in a tracked container.</summary>
        FluidContainerState = 54,
        /// <summary>Host -> guests: shared classifieds, factory, or market progress.</summary>
        WorldProgressState = 55,
        /// <summary>Host -> guests: current sewage/firewood site state.</summary>
        JobSiteState = 56,
        /// <summary>Host -> guests: pending AMIS/Yellow Pages mail-order record.</summary>
        MailOrderState = 57,
        /// <summary>Guest -> host: selected AMIS/Yellow Pages order data before payment.</summary>
        MailOrderIntent = 58,
        /// <summary>Host -> guests: inspection result checklist and renewal state.</summary>
        InspectionState = 59,

        // 60-79: vehicles — M4
        VehicleState = 60,
        VehicleClimate = 61,
        VehicleCargo = 62,
        /// <summary>Guest -> host: validated fuel-station transfer into a parked vehicle tank.</summary>
        VehicleFuelIntent = 63,
        /// <summary>Host -> guests: in-car radio power/channel/volume (CORRIS/SORBET).</summary>
        CarRadioState = 66,
        /// <summary>Owner -> host -> peers: accumulated engine part-breakage mask of one vehicle.</summary>
        VehicleDamage = 64,
        /// <summary>Owner -> host -> peers: drivetrain wear + per-wheel tire condition of one vehicle.</summary>
        VehicleCondition = 65,

        // 80-99: economy — M5
        WalletState = 80,
        PurchaseIntent = 81,
        /// <summary>Host -> guests: validated active police fine/checkpoint record.</summary>
        PoliceState = 82,
        /// <summary>Guest -> host: locally observed police checkpoint offence for validation.</summary>
        PoliceIntent = 83,
        /// <summary>Host -> guests: home stereo power/channel/knob scalar state.</summary>
        HomeStereoState = 84,
        /// <summary>Guest -> host: requested home stereo scalar update.</summary>
        HomeStereoIntent = 85,
        /// <summary>Host -> guests: validated rally-stage progress record.</summary>
        RallyState = 86,
        /// <summary>Guest -> host: local rally start/checkpoint crossing for validation.</summary>
        RallyIntent = 87,
        /// <summary>Host -> guests: validated ice-race lap/checkpoint record.</summary>
        IceRaceState = 88,
        /// <summary>Guest -> host: ice-race start/checkpoint/finish marker crossing.</summary>
        IceRaceIntent = 89,
        /// <summary>Host -> guests: ice-race event/grid configuration.</summary>
        IceRaceEventState = 90,
        /// <summary>Host -> guests: ordered ice-race result-board rows.</summary>
        IceRaceResultsState = 91,
        /// <summary>Host -> guests: exact fixed-radiator thermostat rotation.</summary>
        RadiatorThermostatState = 92,
        /// <summary>Retired display layout: slots moved in v92, Ventti in v98. Never reuse.</summary>
        GamblingState = 93,
        /// <summary>Retired in v99. Kept decodable for diagnostics; never reuse.</summary>
        GamblingIntent = 94,
        /// <summary>Host -> guests: utility meter (electricity/phone) unpaid total + power/line state.</summary>
        UtilityBillState = 95,
        /// <summary>Host -> guests: shared kitchen-appliance (oven/stove) cooking + fire + fuse state.</summary>
        ApplianceState = 99,
        /// <summary>Host -> guests: national lottery draw (round + winning numbers + pot).</summary>
        /// <summary>Retired incorrect Lotto string layout. Never reuse.</summary>
        LotteryDrawState = 96,
        /// <summary>Host -> guests: shared repair-shop (Fleetari) order record + service results.</summary>
        FleetariOrderState = 97,
        /// <summary>Guest -> host: configured Fleetari order captured before payment.</summary>
        FleetariOrderIntent = 98,

        // 100-119: NPCs — M6
        NpcTransform = 100,
        /// <summary>Host -> guests: shared flea-market sale-table proceeds + rent.</summary>
        FleaSaleState = 101,
        /// <summary>Guest -> host: flea sale-table action (rent / collect).</summary>
        FleaSaleIntent = 102,
        /// <summary>Host -> guests: shared taxi-job lifecycle + earnings.</summary>
        TaxiJobState = 103,
        /// <summary>Owner -> host -> peers: kilju bucket fermentation state (tracked item).</summary>
        BrewState = 105,
        /// <summary>Host -> guests: shared Kela welfare / unemployment claim state.</summary>
        WelfareState = 107,
        /// <summary>Host -> guests: shared hitchhiker variant + stage + payout.</summary>
        HitchhikerState = 106,
        /// <summary>Host -> guests: an incoming phone call (topic + call id).</summary>
        PhoneCallEvent = 108,
        /// <summary>Host -> guests: misc host-owned world scalars (scrap price, prime interest, player keys).</summary>
        WorldScalarsState = 104,
        /// <summary>Host -> guests: yard piss-stain scales (persistent world marks).</summary>
        PissAreaState = 109,
        /// <summary>Guest -> host: reporter's local copy of a streamed animal died (moose kill).</summary>
        NpcDeathReport = 110,

        // 120-139: snapshots/bulk — M3 join snapshot
        WorldSnapshotRequest = 120,
        WorldDoorSnapshot = 121,
        WorldItemSnapshot = 122,
        WorldBoltSnapshot = 123,
        WorldPartSnapshot = 124,
        WorldItemDespawnSnapshot = 125,

        // 140-149: crime & consequence (the 93-99 economy range filled, so crime extends here)
        /// <summary>Host -> guests: shared wanted level (crime counters + sentence).</summary>
        WantedState = 140,
        /// <summary>Guest -> host: a locally-observed crime to add to the host's wanted counters.</summary>
        CrimeReport = 141,
        /// <summary>Host -> guests: shared jail-sentence countdown.</summary>
        JailState = 142,
        /// <summary>Host -> guests: police pursuit chase-active + siren flags.</summary>
        PursuitState = 143,
        // 150-159: racing completeness
        /// <summary>Host -> guests: rally results ledger + enroll + penalties.</summary>
        RallyResultsState = 150,
        /// <summary>Host -> guests: JOKKIS banger-race lap/time/checkpoint state.</summary>
        JokkisRaceState = 151,

        // 160-183: economy round 2 (93-99 and 104 are full)
        /// <summary>Host -> guests: complete hockey odds/results, pairings and standings.</summary>
        HockeyBettingState = 160,
        /// <summary>Guest -> host: the sender's own oven sim rolled an ignition (fire report).</summary>
        ApplianceFireReport = 161,
        /// <summary>Guest -> host: an ATM cash deposit or withdrawal.</summary>
        BankTransferIntent = 162,
        /// <summary>Host -> guests: acknowledgment of one player's ATM request.</summary>
        BankTransferResult = 163,
        SlotMachineState = 164,
        SlotMachineResult = 165,
        SlotMachineIntent = 166,
        PokerState = 167,
        PokerResult = 168,
        PokerIntent = 169,
        DebtLetterState = 170,
        DebtPaymentIntent = 171,
        DebtPaymentResult = 172,
        VenttiPropertyState = 173,
        VenttiTableState = 174,
        VenttiLedgerState = 175,
        VenttiRequest = 176,
        VenttiReceipt = 177,
        VenttiSceneState = 178,
        VenttiSoundCue = 179,
        LottoDrawState = 180,
        LottoTicketRequest = 181,
        LottoTicketReceipt = 182,
        LottoTicketState = 183,

        // Standard parts-package identity, creation and quantity.
        PackageState = 184,
        ReplacementPartState = 185,
        PackageOpenRequest = 186,
        PackageOpenReceipt = 187,
        PartFitRequest = 188,
        PartFitReceipt = 189,

        // Reserved ranges for future subsystems:
        //   63-79 vehicles (attachment, fuel/damage)
        //   93-99 economy/appliances (FULL)
        //   111-119 NPCs/jobs (NpcTransform = 100, WorldScalarsState = 104, NpcDeathReport = 110)
        //   126-139 snapshot/bulk transfer control
        //   140-159 crime & consequence + racing completeness
    }

    public interface IMessage
    {
        MessageId Id { get; }
        void Write(NetWriter writer);
        void Read(NetReader reader);
    }
}
