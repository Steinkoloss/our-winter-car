# Code map — where to edit what

Task routing for agents and humans. Architecture lives in `PLAN.md`; wire format in
`protocol/PROTOCOL.md`. Step-by-step workflows in `docs/AGENT-RECIPES.md`.

## Entry points

| Component | Boot / wiring |
|-----------|----------------|
| BepInEx plugin | `src/WinterMP.Core/WinterMPPlugin.cs` — creates persistent `WinterMP` GameObject and subsystems |
| FastBoot (save gate) | `src/WinterMP.FastBoot/SessionGate.cs` |
| Launcher | `src/WinterMP.Launcher/MainWindow.axaml.cs` (Avalonia) |
| Headless install (used by installers) | `src/WinterMP.Launcher/Services/CliInstallRunner.cs` (`--install-mod`) |
| Universal installer (one file, Win+Linux) | `installer/ape/ourwintercar-installer.c` + `tools/build-ape-installer.sh` (APE) |
| Protocol (engine-independent) | `src/WinterMP.Net/` — **unit-tested, builds without the game** |

## Session & transport

| Concern | Files |
|---------|--------|
| Host/join, handshake, chat, relay | `Session/SessionManager.cs` |
| Remote player bookkeeping | `Session/RemotePlayer.cs`, `Session/DevLoopbackClient.cs` |
| Launch modes / CLI | `LaunchOptions.cs`, `Session/SessionLaunchPolicy.cs` |
| Guest sidecar (pose + needs) | `Session/GuestProfileStore.cs` |
| Session admission / channel contract | `WinterMP.Net/SessionMessagePolicy.cs` — enforced in `Session/SessionManager.cs` + `Steam/SteamLobbyManager.cs` |
| Bandwidth meter (TAB overlay + 10 s log) | `Session/NetTrafficMeter.cs` |
| Transport quality / ownership-transfer pausing | `Session/ConnectionQuality.cs` |
| Host main-menu gate | `Session/HostLaunchPolicy.cs`, `UI/MainMenuHostGate.cs` |
| Steam lobby / P2P | `Steam/SteamBootstrap.cs`, `SteamLobbyManager.cs`, `SteamP2PTransport.cs` |
| Loopback / UDP dev transport | `Session/SessionManager.cs` (`StartDevLoopback`, `StartHostLocal`) |

## Player presence (M2)

| Concern | Files |
|---------|--------|
| Pose stream in/out | `Sync/PlayerSyncManager.cs`, `Sync/PlayerPoseReader.cs` |
| Remote bodies | `Sync/RemoteAvatar.cs`, `Sync/NpcCharacterFactory.cs`, `Sync/RemoteCharacterAnimator.cs` |
| Move-state bitfield | `Sync/PlayerMoveStateReader.cs`, `Sync/PlayerMoveState.cs` |
| Guest spawn picker | `UI/GuestSpawnPrompt.cs`, `Sync/GuestSpawnRelocator.cs` |
| Needs report (hunger/fatigue/thirst/urine/bodytemp/stress/drunk/dirtiness) → host sidecar | `Sync/PlayerNeedsSync.cs` |
| Clothing stage/type + remote shirt tint | `Sync/ClothingSync.cs` |
| Vehicle seats | `Sync/PassengerController.cs`; host occupancy/keepalive decisions: `WinterMP.Net/Sync/PassengerSeatLedger.cs` |

## World sync (M3–M5)

| Concern | Files |
|---------|--------|
| Orchestrator | `Sync/WorldSyncManager.cs` (+ partials), `Sync/WorldSyncBridge.cs`, `Sync/WorldSyncTypes.cs` |
| Generic FSM sync engine | `Sync/FsmWorldSync.cs`, `.Registry.cs`, `.Hooks.cs`, `.Local.cs`, `.Remote.cs`, `.Snapshots.cs`, `.Checksum.cs` (+ `.Tests.cs` in-game self-test) |
| FSM hook helpers | `Sync/FsmHook.cs` |
| Curated rules loader | `Catalog/SyncCatalog.cs`, `catalog/sync-catalog.json` |
| Doors, shops, bolts, parts | Registered via `FsmWorldSync.Registry.cs` from catalog |
| Native part / bolt / child-control identities | `Sync/NativePartIdentity.cs`, `ItemWorldSync.Parts.cs`, `FsmWorldSync.Registry.cs`, `ScenePath.cs`; catalog `partIdentity`; `WinterMP.Net/Sync/PartIdentity.cs`, `ScenePathCache.cs`; `WinterMP.Net.Tests/PartIdentityTests.cs`, `ScenePathCacheTests.cs` |
| Generic FSM teardown / reconnect registration | `Sync/FsmWorldSync.Hooks.cs`, `FsmHook.cs`, `FsmWorldSync.cs` |
| Native part condition / deferred host reports / resync | `Sync/FsmWorldSync.Local.cs`, `.Remote.cs`, `.Snapshots.cs`, `.Checksum.cs`; `WinterMP.Net/Sync/PartStatePolicy.cs`; `WinterMP.Net.Tests/PartStatePolicyTests.cs` |
| Host bolt turns / absolute save-array and pose repair / deferred parent totals | `Sync/FsmWorldSync.Bolts.cs`, `.Local.cs`, `.Remote.cs`, `.Snapshots.cs`, `.Checksum.cs`, `Session/SessionManager.Messages.cs`; `WinterMP.Net/Sync/BoltStatePolicy.cs` (including `PartTightnessReceipts`); messages 44/123; `WinterMP.Net.Tests/BoltStatePolicyTests.cs` |
| Items / pickables | `Sync/ItemWorldSync.*` |
| Native trophy factories / saved award identities | `Sync/ItemWorldSync.Factories.cs`; catalog `trophyFactories`; `WinterMP.Net/Sync/FactoryItemIdentity.cs`; `WinterMP.Net.Tests/FactoryItemTests.cs` |
| Loose replacement parts / native factory outputs | `Sync/ItemWorldSync.PartFactories.cs`, `.PartReplicas.cs`, `.Parts.cs`; catalog `replacementParts`; `Catalog/SyncCatalogJson.Factories.cs`; `WinterMP.Net/Sync/ReplacementPartReplica.cs`; `ReplacementPartState` (185); `WinterMP.Net.Tests/ReplacementPartTests.cs` |
| Fitted replacement presentation / parent resolution / loose reattachment lifecycle | `Sync/ItemWorldSync.PartAttachments.cs`, `.PartReplicas.cs`, `.Parts.cs`, `.Despawn.cs`, `ScenePath.cs`; catalog `replacementParts` mount variables; `WinterMP.Net/Sync/PartAttachmentPolicy.cs`, `ReplacementPartReplica.cs`, `ScenePathCache.cs`; `WinterMP.Net.Tests/PartAttachmentTests.cs`, `ScenePathCacheTests.cs` |
| Native fitting / removal / Rigidbody lifetime | `Sync/ItemWorldSync.Parts.cs`, `.Local.cs`, `.PartReplicas.cs`, `NativePartIdentity.cs`; catalog `partIdentity.consumedVariable`; `WinterMP.Net/Sync/PartStatePolicy.cs`; `PartStatePolicyTests.cs`, `ReplacementPartTests.cs` |
| Guest box opening / duplicate-request protection | `Sync/ItemWorldSync.PackageOpening.cs`, `.PackageReplicas.cs`, `.Packages.cs`, `.PartFactories.cs`; catalog `partsPackages`; `WinterMP.Net/Sync/PackageOpenLedger.cs`; `PackageOpenRequest` / `PackageOpenReceipt` (186–187); `WinterMP.Net.Tests/PackageOpenTests.cs` |
| Guest replacement fitting / fixed native mounts / acknowledged fitting clicks | `Sync/ItemWorldSync.PartFitting.cs`, `.PartFitting.Bindings.cs`, `.PartReplicas.cs`, `.Remote.cs`; `FsmWorldSync.Remote.cs`, `WorldSyncBridge.cs`; `UI/DebugOverlay.cs`; catalog `replacementParts.fit*`; `WinterMP.Net/Sync/PartFitLedger.cs`; `PartFitRequest` / `PartFitReceipt` (188–189); `WinterMP.Net.Tests/PartFitTests.cs` |
| Guest piston/main-bearing/rocker fitting / native array slot selection | `Sync/ItemWorldSync.PartSlots.cs`, `.PartFitting.cs`, `.PartFitting.Bindings.cs`; catalog `replacementParts.slot*` and factory `slotReference` / `slotCount`; `WinterMP.Net/Sync/PartSlotPolicy.cs`, `PartFitLedger.cs`; `PartFitRequest` / `PartFitReceipt` (188–189, SlotIndex); `WinterMP.Net.Tests/PartSlotTests.cs` |
| Guest fitted replacement removal / native tightness and collider gating / ray selection | `Sync/ItemWorldSync.PartRemoval.cs`, `.PartFitting.cs`, `.PartReplicas.cs`; catalog `replacementParts.remove*`; `WinterMP.Net/Sync/PartRemovalPolicy.cs`, `PartFitLedger.cs`, `ReplacementPartReplica.cs`; `PartFitRequest` / `PartFitReceipt` (188–189), `ReplacementPartState` (185); `WinterMP.Net.Tests/PartRemovalTests.cs` |
| Parts-package creation / quantities / native disposal | `Sync/ItemWorldSync.Packages.cs`, `.PackageFactories.cs`, `.PackageReplicas.cs`, `.Scan.cs`, `.Spawn.cs`, `.Despawn.cs`, `.Snapshots.cs`; catalog `partsPackages`; `Catalog/SyncCatalogJson.Factories.cs`; `WinterMP.Net/Sync/PackageIdentityResolver.cs`, `PackageReplica.cs`; message `PackageState` (184); `WinterMP.Net.Tests/PackageIdentityTests.cs`, `PackageStateTests.cs` |
| Missing spawned-item recovery / terminal removals | `Sync/ItemWorldSync.Spawn.cs`, `.Despawn.cs`, `.Snapshots.cs`; `Sync/WorldSyncManager.Snapshots.cs`; `WinterMP.Net/Sync/ItemSpawnLifecycle.cs` |
| NPC traffic / ice-race opponents | `Sync/NpcTrafficSync.cs` |
| Jerrycan / liquid contents | `Sync/FluidContainerSync.cs` |
| Home heating / sauna / fireplaces (lit, fuel, heat, sauna temp + intents) | `Sync/HeatSourceSync.cs` |
| Fixed radiator thermostats (absolute rotation, id 92) | `Sync/FsmWorldSync.*` radiator paths + catalog knob rules |
| Vehicles / host-validated station refueling | `Sync/VehicleWorldSync.*` (including `.Fuel.cs`), `Sync/ItemWorldSync.Vehicle.cs` |
| Classifieds / factory / Marketti progress | `Sync/WorldProgressSync.cs` |
| AMIS / Yellow Pages pending mail orders | `Sync/MailOrderSync.cs` |
| Sewage / firewood delivery sites and GIFU tank/pump state | `Sync/JobSiteSync.cs` |
| Vehicle inspection result / renewal record | `Sync/InspectionSync.cs` |
| Police checkpoint reports / validated fine record | `Sync/PoliceSync.cs`, `Sync/FsmWorldSync.Local.cs` (guarded fine payment) |
| Home stereo scalar controls | `Sync/HomeStereoSync.cs` |
| Rally stage authority / acknowledged crossings / timing records | `Sync/RallySync.cs`; `WinterMP.Net/Sync/RallyProgressLedger.cs` (including recent host crossing evidence), `RallyProgressReplica.cs`; `WinterMP.Net.Tests/RallyProgressTests.cs`; catalog `rallyProgress` |
| Ice-race marker/lap authority | `Sync/IceRaceSync.cs` |
| Ice-race registration/event/grid configuration | `Sync/IceRaceEventSync.cs` |
| Ice-race result-board rows | `Sync/IceRaceResultsSync.cs` |
| Time / weather | `Sync/TimeWeatherSync.cs` |
| Shared cash/bank/income + ATM transfers | `Sync/WalletSync.cs`, `.Banking.cs`; `WinterMP.Net/BankTransferPolicy.cs`; catalog `banking` |
| Scene paths / net IDs | `Sync/ScenePath.cs`; per-scan sibling/path index: `WinterMP.Net/Sync/ScenePathCache.cs` |

## Coverage sprint (protocol v57–v88)

| Concern | Files |
|---------|--------|
| Slot machines (host RNG + intents) | `Sync/GamblingSync.cs` |
| Ventti card table / host property keys and cabin access | `Sync/VenttiSync.cs`, `.Table.cs`, `.Properties.cs`; `WinterMP.Net/Sync/VenttiTableReplica.cs`, `VenttiPropertyReplica.cs`; catalogs `venttiTable`, `venttiProperty` |
| Ventti betting, committed cards and native outcomes | `Sync/VenttiSync.Game.cs`, `.Presentation.cs`, `.Effects.cs`; `WinterMP.Net/VenttiLedger.cs`, `VenttiRules.cs`, `Sync/VenttiGameReplica.cs`, `Messages/VenttiGameMessages.cs`; `WinterMP.Net.Tests/VenttiLedgerTests.cs`, `VenttiGameTests.cs`; catalog `venttiTable` |
| Ventti NPC poses, thrown furniture and host-selected speech | `Sync/VenttiSync.Reactions.cs`, `.Sounds.cs`; `WinterMP.Net/Messages/VenttiReactionMessages.cs`, `Sync/VenttiSceneReplica.cs`, `VenttiSoundQueue.cs`; `WinterMP.Net.Tests/VenttiReactionTests.cs`; catalog `venttiTable.reactions` |
| VideoPoker hands, holds, doubling and cash-out | `Sync/PokerSync.cs`, `.Presentation.cs`; `WinterMP.Net/PokerLedger.cs`; catalog `videoPoker` |
| Pub/station slot accounting + local reel animation | `Sync/GamblingSync.cs`, `.Presentation.cs`; `WinterMP.Net/SlotMachineLedger.cs`; catalog `slotMachines` |
| Electricity/phone bills → blackout | `Sync/UtilityBillSync.cs` |
| Lotto draw, prizes and teletext | `Sync/LotterySync.cs`, `WinterMP.Net/Sync/LottoDrawReplica.cs`, `Messages/LotteryMessages.cs`, catalog `lottoDraw`; Lotto tickets below; Megaveto remains R2.21 |
| Lotto selected rows, persistent tickets and cash/bank claims | `Sync/LottoTicketSync.cs`, `.Bindings.cs`, `.Tickets.cs`; `WinterMP.Net/LottoTicketLedger.cs`, `Sync/LottoTicketReplica.cs`, `Messages/LottoTicketMessages.cs`; `WinterMP.Net.Tests/LottoTicketTests.cs`; catalog `lottoTickets` |
| Fleetari order capture-and-pair | `Sync/RepairShopSync.cs` |
| Flea-market sale table | `Sync/FleaSaleSync.cs` |
| Taxi job + per-ride fare meter | `Sync/TaxiJobSync.cs` (customer is a ScriptedMover in `NpcTrafficSync`) |
| Kela + rent/eviction + housing benefit | `Sync/WelfareSync.cs` |
| Debt-letter quotes and shared-cash payments | `Sync/WelfareSync.DebtLetter.cs`; `WinterMP.Net/DebtPaymentLedger.cs`; catalog `debtLetter` |
| Hitchhiker variant/stage/payout | `Sync/HitchhikerSync.cs` |
| Yard piss stains | `Sync/PissAreaSync.cs` |
| Kilju brew (held or lid-flip-claimed buckets) | `Sync/KiljuSync.cs` |
| In-car radio tune/volume | `Sync/CarRadioSync.cs` |
| Oven/stove heats + edge-carried ignition | `Sync/ApplianceSync.cs` |
| Incoming phone calls | `Sync/PhoneSync.cs` |
| Wanted level + guest crime reports | `Sync/WantedSync.cs` |
| Jail countdown (jailed client owns it) | `Sync/JailSync.cs` |
| Police pursuit sirens/chase flags | `Sync/PursuitSync.cs` |
| Rally results ledger | `Sync/RallyResultsSync.cs` |
| JOKKIS banger race | `Sync/JokkisRaceSync.cs` |
| Engine part wear, breakage and repair (owner-authoritative) | `Sync/VehicleWorldSync.Damage.cs`; `WinterMP.Net/VehicleDamagePolicy.cs`; catalog `vehicleDamage` |
| Tire/drivetrain condition (owner-authoritative) | `Sync/VehicleWorldSync.Condition.cs` |
| Parked-vehicle damage/condition checksums | `Sync/ItemWorldSync.Checksum.cs`, `Sync/VehicleWorldSync.cs`; `WinterMP.Net/VehicleChecksum.cs`, `VehicleConditionPolicy.cs` |
| Scrap price / prime interest / player keys | `Sync/WorldScalarsSync.cs` |
| Hockey odds/results, matchups and standings | `Sync/HockeyBettingSync.cs`, `.Bindings.cs`; `WinterMP.Net/Sync/HockeyBettingReplica.cs`; catalog `hockeyBetting` |
| Moose/hitchhiker/Reijo/farmer/taxi-customer movers + guest kill reports | `Sync/NpcTrafficSync.cs` |

## Long-tail player systems (M6)

| Concern | Files |
|---------|--------|
| Sleep consent | `Sync/PlayerSleepHook.cs`, `Sync/SleepConsentManager.cs`, `UI/SleepConsentPrompt.cs` — FSM paths in `catalog/README.md` |
| Death / respawn | `Sync/PlayerDeathHook.cs`, `Sync/DeathSyncManager.cs` |
| Permadeath settings | `Sync/PermadeathSettings.cs` |

## Protocol layer (change here first when possible)

| Concern | Files |
|---------|--------|
| Version | `WinterMP.Net/Protocol.cs` |
| Message IDs | `WinterMP.Net/Messages/IMessage.cs` |
| Message types | `WinterMP.Net/Messages/*.cs` |
| Registry | `WinterMP.Net/Messages/MessageRegistry.cs` |
| Spec (must match code) | `protocol/PROTOCOL.md` |
| Tests | `WinterMP.Net.Tests/` — especially `WorldMessagesTests.cs`, `PacketCodecTests.cs` |

## UI & dev tooling

| Concern | Files |
|---------|--------|
| In-game debug overlay (TAB) | `UI/DebugOverlay.cs` |
| Main menu join browser | `UI/MainMenuJoinBrowser.cs`, `UI/MainMenuUiFactory.cs`, `UI/MainMenuUiButton.cs` |
| F9 catalog dump (0.3.0: + PlayMaker globals) | `WinterMP.Tools/FsmDumperPlugin.cs` → `<game>/WinterMP/dumps/` |
| Catalog curation scripts | `tools/extract_fsm_details.py`, `tools/analyze_catalog.py`, `tools/catalog_diff.py` |
| Class-D binding audit (rerun after every dump) | `tools/check_fsm_bindings.py` — exit 0 = clean |
| Offline Unity 5 FSM/action/global evidence | `tools/extract_fsm_assets.py` — reads installed assets; supplements the runtime F9 dump |

## Partial-class convention

Large types split by concern, e.g. `FsmWorldSync.cs` + `FsmWorldSync.Remote.cs`.
When a file approaches ~1000 lines, add a new partial — don't grow a monolith.
