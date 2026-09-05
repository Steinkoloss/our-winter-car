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
        // v26: VehicleState/Climate snapshot sentinel (SnapshotSequence = ushort.MaxValue) to bypass dedup on join.
        // v27: VehicleCargo (62) — live vehicle-local cargo pose streaming replaces the
        //      kinematic cargo weld; ItemTransform gains optional velocity (FlagHasVelocity)
        //      for receiver-side dead reckoning of moving vehicles.
        // v28: M7 winter survival — PlayerNeedsReport gains BodyTemp (5th need);
        //      PlayerClothingState (32) syncs worn clothing; HeatSourceState (50) +
        //      HeatSourceIntent (51) make home heat sources host-owned shared state;
        //      VehicleClimate (61) gains Ice — exterior window ice split from interior
        //      frost so parked cold cars stop force-frosting observers' interiors.
        // v29: ItemSpawn (52) + SpawnIntent (53) — host-authoritative grocery-bag
        //      content spawning: host mints net ids for container-spawned clones and
        //      broadcasts the manifest; guests route their own bag opens via the host.
        // v30: PlayerNeedsReport + GuestSpawn gain Stress (global need float) and
        //      Drunk (FPSCamera "Drunk Mode" DrunkCurrent) — synced + persisted so a
        //      rejoining guest keeps intoxication/stress instead of resetting sober.
        // v31: ItemSpawn gains trailing Flags (bit 0 = replay). The host re-sends
        //      live-refreshed spill manifests with the join snapshot; the replay bit
        //      tells the joiner to adopt/instantiate from scene templates instead of
        //      firing a local bag (which would spend an unrelated unopened one).
        // v32: SpawnIntent gains Items (name + pose per spilled clone) and ItemSpawn
        //      gains OfferSequence (echoes the answered offer). Bag spills are
        //      captured by the peer that opened the bag; every other peer
        //      materializes from the manifest. No peer fires another peer's bag
        //      FSM anymore (its player-interaction checks made that impossible).
        //      DeathCause values 14-21 (sewage..smoking) also land with this bump.
        // v33: FluidContainerState (54) streams tracked jerrycan/container contents;
        //      WorldProgressState (55) mirrors host-owned classifieds, factory, and
        //      market listing progress for guests and joiners.
        // v34: JobSiteState (56) mirrors all sewage and firewood site progress:
        //      work-order active flag plus the site-specific level/surplus values.
        // v35: JobSiteState kind 3 and its kind-specific flags mirror the GIFU
        //      sewage-truck pump/tank, including hose and active-pumping state.
        // v36: MailOrderState (57) mirrors the saved AMIS/Yellow Pages pending-order
        //      records, including opaque package data required for deliveries/rejoins.
        // v37: MailOrderIntent (58) carries a guest's exact selected phone-order
        //      record to the host before host-authoritative package payment/spawn.
        // v38: InspectionState (59) mirrors host-authoritative inspection pass,
        //      checklist, stamp and renewal-date state for observers and joiners.
        // v39: VehicleFuelIntent (63) lets a guest refuel a nearby parked vehicle
        //      through a host-validated Peräpörtti nozzle/tank reconciliation.
        // v40: InspectionState gains host-generated standard/museum registration
        //      plate text and availability flags, closing observer plate divergence.
        // v41: PoliceIntent/PoliceState (82/83) turn local police-checkpoint
        //      observations into host-validated shared fine records.
        // v42: HomeStereoIntent/HomeStereoState (84/85) make the fixed home
        //      radio/CD player's power, channel, volume and bass host-owned.
        // v43: RallyIntent/RallyState (86/87) establish host-validated rally
        //      stage/checkpoint ordering and host-clocked progress records.
        // v44: IceRaceIntent/IceRaceState (88/89) establish host-validated
        //      ice-track marker order and host-clocked lap records.
        // v45: IceRaceEventState (90) mirrors the host ice-race grid/heat
        //      controller configuration to observers and joiners.
        // v46: IceRaceResultsState (91) mirrors host-ranked ice-race board rows.
        // v47: IceRaceEventState gains the host lineup's registration flag and race stage.
        // v48: PurchaseIntent acceptance requires a fresh authenticated pose near a
        //      registered entry guard and a monotonic per-guest sequence (replay-safe).
        // v49: HeatSourceIntent gains authenticated player id + sequence so the host
        //      can require fresh source proximity and reject replayed heat actions.
        // v50: exact ice-race and Suvi-Sprint price triggers join the host-authoritative
        //      PurchaseIntent pipeline; their shared-wallet result is now host-owned.
        // v51: home, yard, and apartment shower tap/valve controls are catalogued
        //      reliable FSM transitions, keeping shared water/hygiene presentation aligned.
        // v52: PassengerState gains a monotonic sequence; host validates guest claims
        //      against fresh position, registered seat geometry, and canonical occupancy.
        // v53: PlayerTransform relays reject non-finite/out-of-map positions, invalid
        //      rotations, and unknown movement bits before proximity checks use them.
        // v54: RadiatorThermostatState (92) mirrors a host-owned fixed-radiator
        //      thermostat's absolute Rotation on change and in join snapshots
        //      (Increase/Decrease turn transitions only carry deltas). Session
        //      admission rejects packets from untrusted peers, guest world
        //      traffic before handshake completion, and off-contract channels;
        //      bounded snapshot/spawn collections and host request limits contain
        //      malformed or abusive packets before they reach game state.
        // v55: PlayerDirtiness is appended to per-guest needs reports and rejoin
        //      profiles, preserving the locally simulated hygiene result.
        // v56: PlayerNeedsReport gains a trailing HasDirtiness byte. Needs reporting
        //      no longer waits on the PlayerDirtiness global (a fragile/late binding
        //      would have blocked all seven other needs), and HasDirtiness=false makes
        //      the host record dirtiness as unknown so a late-binding guest is never
        //      restored to clean.
        // v57: GamblingState/GamblingIntent (93/94). Shared slot machines (pub + station)
        //      are host-authoritative — the host runs the spin RNG + payout and broadcasts
        //      reels/credit; guests relay button presses as intents. Closes the shared-wallet
        //      flip-flop where each client rolled its own reels and win/loss.
        // v58: UtilityBillState (95). Host owns the electricity/phone bill ledger and blackout;
        //      guests apply the unpaid total + MainSwitch so both homes cut power together.
        //      Bill payment routes through the catalogued Pay buttons (host purchase path).
        // v59: LotteryDrawState (96). Host owns the national lottery draw (round + winning
        //      numbers + pot) so every ticket is judged against the same numbers; ticket
        //      buy-in routes through the catalogued Lotto/Megaveto Pay buttons.
        // v60: VehicleDamage (64). Engine part-breakage is owner-authoritative — non-owners
        //      zero their PartBreakages roll and apply the owner's accumulated breakage mask,
        //      so a part breaks once and both peers + late joiners agree on the broken set.
        // v61: VehicleCondition (65). Drivetrain wear + per-wheel tire condition (pressure,
        //      health, puncture/rim) is owner-authoritative — non-owners apply the streamed
        //      condition so a flat tire and wear agree across peers + join.
        // v62: FleetariOrderState/Intent (97/98). Repair-shop order is capture-and-paired to
        //      the guest's payment (host applies the actual jobs) and the record is broadcast
        //      so observers/joiners agree; payment already rode the catalogued OrderFleetari buy.
        // v63: VehicleState gains a trailing Gear byte (gear+1; 0=reverse, 1=neutral) so an
        //      observer's gear indicator matches the driver's selected gear.
        // v64: FleaSaleState/Intent (101/102). Flea-market sale table is host-owned — the host
        //      runs the day-timed sale RNG + broadcasts proceeds/rent, guests suppress their
        //      local Sell FSM; rent/collect relay as intents so the shared wallet moves once.
        // v65: TaxiJobState (103). Host owns the taxi-job lifecycle (stage/employment/earnings)
        //      and broadcasts it; the taxi vehicle streams via the normal vehicle path (2.4).
        // v66: BrewState (105). Kilju bucket fermentation streams as tracked-item scalar state
        //      (Alcohol/BrewTime/finished/lid) like FluidContainerState, so both peers brew the
        //      same quality; the stream also carries ingredient-add effects.
        // v67: JobSiteState gains kind 4 (farm) — the farm job stage (int JobStage) + Done
        //      mirror through the existing host-owned job-site path to the shared wallet.
        // v68: WelfareState (107). Host owns the Kela unemployment claim + benefit calc and
        //      broadcasts it; the weekly benefit credits the shared wallet via host logic.
        // v69: HitchhikerState (106). Host owns the hiker variant (ordinary/KiljuMurderer/
        //      suicide) + stage + payout and broadcasts it; the body streams via the
        //      NpcTransform ScriptedMover path (hiker added to NpcTrafficSync MoverDefs).
        // v70: WantedState/CrimeReport (140/141). Host owns the shared wanted level (crime
        //      counters + sentence); a guest reports its own crime deltas so its crimes reach
        //      the group total instead of being erased by the broadcast. Range 140-159 opened.
        // v71: JailState (142). Host owns the jail-sentence countdown (JAIL DaysLeft + sentence)
        //      so a jailed player is jailed on all machines and released together.
        // v72: PursuitState (143). Host owns the police pursuit chase-active + siren flags per
        //      cop car (cars already NPC-streamed); DUI arrest escalates via the wanted/jail
        //      records. Vehicle impound (4.3) rides existing vehicle transform reconciliation.
        // v73: RallyResultsState (150). Host owns the rally results ledger + enroll + parc-fermé
        //      penalty (stage times, placement, class, Registered) so standings + enroll agree;
        //      reward rides the existing host-gated race price triggers.
        // v74: JokkisRaceState (151). JOKKIS banger-race lap/time/checkpoint is host-broadcast
        //      (identical structure to CORRIS) so both see the same standings without
        //      refactoring the CORRIS-coupled IceRaceSync.
        // v75: ApplianceState (99). Host owns each oven/stove (hotplate heats + fire + fuse) so
        //      an unattended stove fire and cooking state agree; fire hazard converges from the
        //      synced heats. Fuse flag also covers the 6.4 per-appliance fuse.
        // v76: PhoneCallEvent (108). Host decides an incoming phone call (topic + call id) and
        //      broadcasts it so both phones ring for the same call instead of per-client RNG.
        // v77: NpcTransform gains FlagDead (bit 1). The host broadcasts the moose death edge so
        //      guests activate their own corpse ragdoll — the collision→dead transition no
        //      longer fires only on the hitting client. (Meat spawn = 7.2 SPAWNITEM audit.)
        // v78: PlayerNeedsReport + GuestSpawn gain PlayerAlco (persistent BAC that drives DUI
        //      checkpoints + sobering) + a HasAlco gate, mirroring the v55/v56 dirtiness add, so
        //      a rejoining guest keeps their blood-alcohol instead of resetting sober.
        // v79: PlayerClothingState gains WinterGarment (0 none / 1 jacket / 2 coverall) so a
        //      worn winter jacket/coverall shows on peers (its ClothType is separate from
        //      ClothingStage/ClothingType).
        // v80: PissAreaState (109) mirrors the five host-owned yard piss-stain scales, and
        //      CarRadioState (66) mirrors the in-car (CORRIS/SORBET) radio channel + volume.
        // v81: CarRadioState's byte "Channel" becomes a float "Tune". No radio Knob FSM has a
        //      float named Channel (the only Channel is a *string* on the CD player), so the
        //      old field bound null and the station never synced. The real tuner value is the
        //      "Tune" float on StockRadio0/ButtonsRadio/Volume :: Knob; its per-station
        //      windows are not knowable from the catalog dump, so it rides unquantized.
        // v82: NpcDeathReport (110) — a guest whose car killed the moose reports it; the host
        //      replays the CarHit death entry so FlagDead becomes authoritative for everyone
        //      (before this, a guest kill left a corpse only on the killer's client).
        // v83: JailState (142) gains JailedPlayerId and becomes two-way: the countdown runs
        //      only on the jailed client, so that client reports it and the host adopts +
        //      relays. Before this the host keepalived its own idle DaysLeft (0), stomping a
        //      jailed guest's sentence within 20 s.
        // v84: WelfareState (107) grows from Kela-only to the whole Systems/Expenses record:
        //      rentDebt, rentPerWeek, asumistukiPerWeek + an Evicted flag. The weekly rent
        //      debit and the KICKOUT eviction (furniture destruction) ran per-client; guests
        //      now suppress their Rent/Livingsupport FSMs and replay the host's eviction.
        // v85: ApplianceState (99) gains FireCount + FirePlate. Oven ignition commits in
        //      one-frame "Start fire N" states a level-sample can never see (the old
        //      FlagFire was almost always false) — the host now edge-hooks the commits and
        //      guests replay the igniting plate's state when the count moves.
        // v86: WorldScalarsState (104 — the range's virgin gap) mirrors the daily scrap
        //      price, the bank prime interest rate, and Database/Keys progression
        //      (UncleStage/GIFU/Conline number); each re-rolled or progressed per-client.
        // v87: TaxiJobState (103) gains FareCost — the customer's per-ride meter. The
        //      customer became a host-authoritative ScriptedMover in the same change, so
        //      the fare accrues host-side off the guest's synced taxi and the PayMoney
        //      press (now catalogued) passes the host's proximity gate.
        // v88: HockeyBettingState (160, opening the economy-2 range) mirrors the hockey
        //      betting round (matchup ids, odds, result, KurPaWins) — every payout-deciding
        //      input of the per-client season sim. The standings TABLE stays per-client
        //      (ES2 array save keys; no FSM variable carries it).
        // v89: ApplianceFireReport (161) — a guest's own oven sim can roll an ignition
        //      (FireHazard RNG is per-client even over synced heats); the guest reports it
        //      and the host replays the plate's commit state, single-sourcing house fires
        //      (the moose-kill report pattern).
        // v90: WalletState appends optional bank balance + net income. Cash binds to
        //      PlayerMoney. ATM transfers (162/163) are authenticated and acknowledged;
        //      wallet resync checks all available economy balances.
        // v91: VehicleDamage appends a known-parts mask and 16 current wear values.
        //      Repair clears concrete damage; SEIZE/CAMFAIL selector bits are retired
        //      so peers never reroll the owner's random outcome.
        // v92: SlotMachineState/Result/Intent (164/165/166) replace the slot use of
        //      93/94. Host ledgers keep credit and accumulated winnings separately,
        //      lease controls, deduplicate settlements and draw weighted reel stops.
        // v93: VideoPoker (167–169) uses host-owned decks, held-card redraws,
        //      private high/low cards and acknowledged wallet settlements.
        // v94: Debt-letter quotes and acknowledged payments (170–172) use the
        //      host's rent debt and fees, including inactive/relocated envelopes.
        public const ushort Version = 94;
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

        /// <summary>Reserved for future reliable, chunked bulk transfers.</summary>
        ReliableBulk = 2,
    }
}
