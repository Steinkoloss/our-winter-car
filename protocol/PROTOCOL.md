# WinterMP wire protocol

Protocol version: **120** (`ProtocolInfo.Version` in `src/WinterMP.Net/Protocol.cs`).

v120 adds BagState (190), BagOpenRequest (191) and BagOpenReceipt (192).
Shopping bags use the host's persistent factory/native identity. Guests request
one/all openings against a revision; only the host consumes inventory and creates
outputs. SpawnIntent (53) is retired and ignored; its ID is never reused. ItemSpawn
(52) keeps its layout, with host ownership and offerSequence=0 for bag spills.

v119 adds RotateIncrease=2 and RotateDecrease=3 to the existing part-operation
request/receipt (188–189), with slot zero. The two catalogued alternators use
native half-degree hand rotation within 0–7 degrees. Identity/revision, settled
mount ownership, fresh guest proximity, the loosened adjusting bolt and native
readiness gate each request; retries only recover its receipt. Absolute scalar
and pivot updates continue through ReplacementPartState (185). Layouts are unchanged.

v118 appends SlotIndex to PartFitRequest/PartFitReceipt (188–189) and adds guest
fitting for the piston, main-bearing and rocker arrays. The requested native slot
is immutable; the host rejects a changed nearest slot before entering installation.
Zero retains fixed-mount fitting and removal; nonzero slots are catalog-bound.
No new IDs; ReplacementPartState (185) retains its v117 layout.

v117 appends an install/remove operation to PartFitRequest/PartFitReceipt (188–189)
and a strict RemovalAllowed flag to ReplacementPartState (185). Guests can request
native removal of fitted replacement copies using the same immutable request ledger.
The host validates the current attachment, tightness and native interaction readiness
before removal; old clicks cannot remove a subsequently refitted part. No new IDs.

v116 adds PartFitRequest/PartFitReceipt (188–189). Guests request fitting of an
observed replacement part; the host selects its fixed native mount and traverses
the native occupancy, prerequisite and distance checks. Retries recover the same
outcome. Generic guest state-40 replay for catalogued replacement parts is refused.

v115 appends stable parent identity, relative path and local pose/scale to
ReplacementPartState (185). Missing fitted replacement copies can attach to a ready,
unoccupied guest mount and follow its hierarchy; removal restores loose-item motion.
Their native installation, bolt and save actions remain disabled. No new message IDs.

v114 appends the native parent-part tightness total to BoltState (44) and
WorldBoltSnapshot (123). Raw bolt turns (41) are guest intents executed only by the
host; guest scalar reports cannot change host values. Host results restore the bolt
save-array entry, native position scale and absolute parent total, including zeros.
Derived part states (40: Bolted/Unbolted/Stop) are no longer relayed. Native part
identity also covers parts without the optional Installed scratch bool. No new IDs.

v113 preserves native part lifetime when fitting removes its Rigidbody. Message 185
keeps its layout, but Installed now means AssemblyId > 0; contradictory pairs are
rejected. Data.Installed is native occupancy-query scratch state. Fitted parts remain
available in replacement snapshots/targeted replies without a loose item body;
physics-component removal must not produce ItemDespawn. No new message IDs.

v112 adds PackageOpenRequest/PackageOpenReceipt (186–187). Guests request an
opening; the host validates and executes one native operation, confirms the exact
part output and acknowledges it. Retries cannot consume another quantity or mint
another part. Complete fitted-part reconstruction remains deferred.

v111 adds ReplacementPartState (185) for native boxed-content factory outputs and
isolated loose-part copies. Accepted ItemDespawn requests are echoed to their sender;
new replacement copies wait for that acceptance before removal. Installed-part graph
reconstruction remains deferred; v112 adds guest opening intents.

v110 routes native part bodies and their registered FSMs by persistent save identity.
Part/bolt/control IDs remain stable when parts are renamed, installed or scanned in
different orders. Existing message layouts and message IDs are unchanged.

v109 adds PackageState (184) for host-created standard boxes, their quantities and
join/resync materialization. Guests create isolated replicas, preserving their own
saved boxes. Guest opening is implemented by 186–187 in v112; replacement-part assembly support remains incomplete.

v108 introduced stable package identities and native host disposal/save cleanup.

v107 appends full native tightness/wear floats to PartState (45) and WorldPartSnapshot
(124). Legacy byte fields remain on the wire but are not applied. Guest part reports
request a deferred host observation instead of overwriting host scalars. Part resync
includes zero-valued records, retries late bindings and hashes native condition.

v106 adds catalog-backed trophy factories to ItemSpawn (52), using flag bit 1 and
persistent native item IDs. Existing framing/layout and all message IDs are unchanged.
Connected guests and late joiners create the host's awards from the exact native
prefab; guest save objects are preserved separately.

v105 preserves message layouts and strengthens shared-item recovery: item resync
includes session removals and refreshed spawn manifests, removals remain terminal
until session teardown, and replay manifests repair missing live replicas.

v104 appends the complete hockey betting collections to HockeyBettingState (160):
six matchups, all 18 odds, previous matchups/results/odds/scores, games played and
standings text. Guests preserve and pause their native season/odds generators;
complete host boards survive delayed scene binding. Megaveto ticket transactions
remain separate unfinished work.

v103 adds host-issued Lotto tickets, selected rows and acknowledged cash/bank
claims (181–183). Generic Lotto Pay replay is removed; host ticket IDs persist
through the native ticket save lifecycle. Megaveto transactions remain separate.

v102 replaces the incorrect Lotto string state (96, retired) with complete native
draw lists, prize tiers, pots, rounds and results visibility (180). Guest native
draw generation pauses and late-bound/joining guests retain the latest host draw.

v101 appends connection-scoped report identity to RallyIntent (87) and exact
acknowledgments plus per-player revisions to RallyState (86). Native crossing
edges retry until acknowledged; duplicates cannot restart the host's race clock.

v100 adds host-observed Ventti NPC/furniture poses (178) and live sound cues (179).
Guests apply poses and the host's chosen native sound variation without entering
native outcome FSMs. Sound cues are never included in join or resync snapshots.

v99 adds host-owned Ventti hands (175), authenticated commands (176), and cached
receipts (177). Legacy control replay (94) is retired. The host alone debits the
shared wallet, draws its private deck, and settles native property effects.

v98 added `VenttiTableState` (174) for full-width native observations before the
host adopts the table. It stops once 175 is available; guests ignore 174 after
receiving 175. Legacy `GamblingState` (93) remains retired.

v97 adds `VenttiPropertyState` (173) for host-owned key flags and cabin access.

v96 preserves all message layouts. `WorldStateChecksum.vehicleCrc` now includes
parked-car damage and tire/drivetrain condition (id 47 below). `VehicleCondition`
tire pressure (id 65) rounds to the nearest hundredth of a bar before clamping to
0–255, and decodes as `byte / 100f`; encoding that decoded float returns the same
byte, including across ownership handoffs. Non-finite local pressure maps to 0
for NaN/negative infinity and 255 for positive infinity.
Any breaking change to framing, message layout or semantics bumps the version;
hosts refuse mismatched clients during handshake.

## Transport & framing

Datagram transports: classic Steam P2P via `SteamNetworking.SendP2PPacket`,
the UDP local-test transport, and loopback for dev. `PacketCodec` receives this
frame after the transport has supplied its channel metadata:

```
[2 bytes messageId]  -- little-endian ushort
[payload]            -- message-specific, see below
```

The channel is not part of the `PacketCodec` frame. Steam carries it in native
`nChannel`; the UDP test transport carries it in its outer control envelope; and
loopback carries it alongside the queued payload.

All integers little-endian. Strings are UTF-8 with ushort byte-length prefix
(empty == null). Blobs are int32 length + raw bytes. Floats are raw IEEE 754.

## Channels

| Channel | Guarantees | Used for |
|---|---|---|
| 0 ReliableOrdered | reliable, ordered | events, FSM transitions, economy, chat, handshake |
| 1 UnreliableSequenced | best-effort; *receiver* drops stale packets via per-stream sequence numbers | transforms |
| 2 ReliableBulk | reliable; large, chunked | reserved for a future bulk transfer message |

Session admission is host-authoritative too: before a peer completes its
handshake, a host accepts only that peer's reliable-ordered `HandshakeRequest`.
While connecting, a guest accepts only its selected host's reliable-ordered
`HandshakeResponse`; it cannot apply host world state until that response is
accepted. Afterwards the host accepts only registered peers, while a guest
accepts packets only from its selected host. Every current non-transform message
uses channel 0; `PlayerTransform` uses channel 1, and item/vehicle/NPC transform
messages, including `VenttiSceneState`, may use channel 0 for a final or snapshot state. Channel 2 is currently
reserved. Packets that break those rules are dropped.

## Session flow

```
client                          host
  |------ HandshakeRequest ------>|   protocol/mod/game version + catalog hash
  |<----- HandshakeResponse ------|   accept (playerId) or refuse (reason)
  |<----- PlayerSpawn * n --------|   existing players
  |<----- PlayerSpawn (self) -----|   broadcast to all incl. newcomer (clients filter own id)
  |                               |
  |   (guest loads the GAME scene and finishes its first world scan)
  |------ WorldSnapshotRequest -->|   guest's id hash (diagnostic)
  |<----- WorldDoorSnapshot * n --|   doors the host has seen change
  |<----- WorldItemSnapshot * n --|   current pose of every item/vehicle
  |<----- WorldBoltSnapshot * n --|   every ready fitted Screw FSM, including zero tightness
  |<----- WorldPartSnapshot * n --|   native installed/tightness/wear for every readable car part
  |<----- world-state msgs * n ---|   current state of every host-authoritative subsystem
  |                               |   (vehicles/climate 60-61, radio 66, police 82, stereo 84,
  |                               |   races 86-92, economy 93-99, world 101-109, crime 140-143,
  |                               |   race results 150-151 — see the message table)
  |<----- TimeSync ---------------|   clock + weather + calendar (also re-broadcast every 30 s)
  |<----- WalletState ------------|   shared wallet (also re-broadcast every ~2 s while balance changes)
  |<----- PassengerState * n -----|   current vehicle seat occupancy (also in join snapshot)
  |<----- GuestSpawn -------------|   host pose + optional last saved pose + needs; guest picks locally
  |<----- PlayerClothingState * n-|   current outfit of every other player (change-only otherwise)
  |<-----> PlayerNeedsReport -----|   guest -> host every ~12 s (needs sidecar)
  |<-----> SleepConsent * --------|   host sleep attempt -> guest accept/decline
  |<-----> PlayerDeath * ---------|   death report -> host event; permadeath wipes all clients
  |<-----> Chat / PlayerTransform / ItemTransform / FsmStateEnter ...
```

**Mid-session rejoin (PLAN.md §4.5):** when a guest disconnects, the host keeps their
`playerId` slot and writes pose (and needs, when reported) into `wintermp-guests.json`.
The same SteamID reconnecting gets the same `playerId`, a fresh world snapshot, and
`GuestSpawn` with last saved pose/needs — chat shows `reconnected` instead of `joined`.

Topology is a star: guests only talk to the host; the host relays chat and
transforms to other guests and is authoritative for all world state.

## Message ids

| Id | Message | Channel | Notes |
|---|---|---|---|
| 1 | HandshakeRequest | 0 | versions, catalog hash, player name |
| 2 | HandshakeResponse | 0 | accepted + playerId + hostPlayerName + sessionFlags (bit 0 = host permadeath enabled), or refusal reason |
| 3 | Ping | 0 | nonce + sender time |
| 4 | Pong | 0 | echoes nonce |
| 5 | Disconnect | 0 | human-readable reason |
| 10 | Chat | 0 | senderPlayerId + text |
| 20 | PlayerSpawn | 0 | playerId, steamId, name |
| 21 | PlayerDespawn | 0 | playerId, reason |
| 22 | PlayerTransform | 1 | playerId, seq, pos, rot, moveState. **Since v53**, relays reject non-finite/out-of-map positions, non-unit rotations, and unknown move-state bits before storing, relaying, persisting, or using the pose as a proximity proof for a guest intent. |
| 23 | PassengerState | 0 | playerId, vehicleId, seatIndex (0 front passenger, 1 rear left, 2 rear right, 255 none), seq (**appended v52**); re-broadcast every ~8 s while seated. The host accepts only an authenticated player's next sequence: an exit must carry vehicle id 0, while a new or changed seat claim must name one of the three discovered passenger anchors and be within 2 m of it from a fresh player pose. **v95:** keepalives for an already accepted exact seat verify the vehicle/seat still exists without rechecking world-space entry proximity (moving cars and delayed poses must not eject occupants). Duplicate/older requests are silently ignored; new rejected requests consume their sequence and clear canonical occupancy with a `SeatNone` broadcast to every peer, including the claimant, using the request sequence. A claimant applies a self-addressed correction only if it matches its latest request. Same-seat races still resolve by lowest player id; the winner's broadcast evicts any conflicting local/remote occupant, and the losing guest also receives a `SeatNone` with its last accepted sequence. Join snapshots contain only current accepted occupants. |
| 24 | GuestSpawn | 0 | host -> joining guest after snapshot: host position + rotation, last saved position + rotation, flags (bit 0 = last position valid, bit 1 = saved needs valid, **bit 2 = saved dirtiness valid**), hunger/fatigue/thirst/urine/**bodyTemp** floats (bodyTemp appended v28), **stress/drunk** floats (appended v30, restored with the others; bodyTemp 0 = absent and is skipped on restore), then **dirtiness** (appended v55, `PlayerDirtiness`, restored only when bit 2 is set so legacy profiles cannot falsely clean a guest), then **`PlayerAlco`** (**v78**, restored only when **bit 3** = saved-alco is set). Bit 0 is set only for **returning** guests (known to the host's sidecar before this connection). Guest shows spawn picker when bit 0 is set; otherwise snaps to host immediately. When guest picks last position and bit 1 is set, local need globals are restored from the sidecar values. |
| 25 | PlayerNeedsReport | 0 | guest -> host every ~12 s: playerId, hunger, fatigue, thirst, urine, **bodyTemp** (v28, `PLAYER/BodyTemp.Temperature` — the 5th need), **stress** (v30, `Stress` global — food/coffee/smoking move it), **drunk** (v30, `DrunkCurrent` on the FPS-camera "Drunk Mode" FSM), seq, then **dirtiness** (v55, `PlayerDirtiness`; appended after seq), then a **`HasDirtiness` byte** (**v56**), then **`PlayerAlco` + a `HasAlco` byte** (**v78**, persistent BAC). The host stores only the authenticated player’s next finite report in the `wintermp-guests.json` sidecar (16-column rows; legacy 12–15-column rows retain missing trailing needs, and specifically omit dirtiness rather than falsely restoring clean). **Since v56** the report no longer waits on the `PlayerDirtiness` global — the other seven needs report regardless — and `HasDirtiness=false` (global not yet resolved) makes the host record dirtiness as unknown (15-column row), so a guest whose handle bound late is never restored to clean. |
| 26 | SleepConsentRequest | 0 | host -> all guests when host enters a sleep/time-skip FSM state: requestId, initiatorPlayerId |
| 27 | SleepConsentResponse | 0 | guest -> host: requestId, playerId, accepted (byte 0/1) — host waits for all guests (90 s timeout); aborts sleep if any decline or timeout |
| 28 | SleepConsentResult | 0 | host -> all guests: requestId, accepted (byte 0/1) — dismisses guest prompt; on accept guests reset fatigue locally; host sends ACTIVATE to proceed and pushes TimeSync when the sleep FSM reaches Calc rates |
| 29 | PlayerDeathReport | 0 | any -> host: playerId, cause, seq — host rebroadcasts PlayerDeathEvent. Cause bytes are append-only (`DeathCause`): 0 unknown, 1-13 fatigue/hunger/thirst/urine/stress/run-over/drown/fire/electrocute/hypothermia/murder/train/accident, **14-21 appended v32**: sewage/carbon-monoxide/PTO/cutter-blade/jail/piss-TV/burn/smoking (the death FSM's remaining cause bools) |
| 30 | PlayerDeathEvent | 0 | host -> all: playerId, cause, flags (bit 0 = permadeath group wipe) — hides avatars; wipe triggers local Systems/Death on every client (accident maps to the RUNOVER screen — State 3 has no crash transition; burn → FIRE; smoking → FATIGUE) |
| 31 | PlayerRespawn | 0 | respawning player -> all: playerId, pos, rot, seq — non-permadeath only; avatar visible again |
| 32 | PlayerClothingState | 0 | any -> host -> other guests (v28): playerId, clothingStage (FsmInt `ClothingStage` on `PLAYER/BodyTemp`, warmth tier), clothingType (FsmInt `ClothingType` on the FPS-camera `Piss` FSM, outfit variant). Owner-authoritative: reported on change, host relays; drives remote-avatar visual (best-effort shirt tint) and informs local warmth math. Never written onto the owning player's own FSM. **Because it is change-only (no keepalive, not in the chunked snapshot), the host also sends a joining guest one per already-connected player right after GuestSpawn** — otherwise a joiner renders already-dressed players in default clothing until each next changes clothes | **v79: appends WinterGarment byte (0 none/1 jacket/2 coverall).**
| 40 | FsmStateEnter | 0 | netId + state name; receiver replays via injected MP_* global transition (doors, ignitions, vehicle controls, car parts Install/Remove, shop Buy/CashRegister Purchase/Cashier/Add, Peräpörtti restaurant Cashier/State 1, inspection Pay, post-package Close box/Remove order, post-office Spawn, phone-order Spawn package, Fleetari Pending cost/State 3, service brochure Fleetari 2, engine run/stall on SORBET/CORRIS Starter FSMs, and **v51** home/yard/apartment shower tap plus valve ON/OFF controls). **v114:** part Bolted/Unbolted/Stop are derived from authoritative tightness, excluded from state replay/snapshots. |
| 41 | FsmRawEvent | 0 | guest -> host: netId + whitelisted TIGHTEN/UNTIGHTEN intent on a ready fitted bolt; authenticated, fresh pose within 3 m; executed immediately on host, never queued or relayed to guests. Unsupported at-limit timing adjustments are rejected. Host results use 44. |
| 42 | ItemTransform | 1 (vehicle/final: 0) | itemId, ownerPlayerId, seq, flags, pos, rot [, velocity when flags bit 3] — items *and* vehicles |
| 43 | TimeSync | 0 | host -> guests: hour (1-24), minutes, forecast temps, snowing, forecast index, daysPassed, dayOfWeek (0=Mon..6=Sun, 255 unknown) |
| 44 | BoltState | 0 | netId:u32, boltTightness:u16 (0–8), screwInt:u16 (reserved zero), then v114 partTightness:f32; host absolute result restores native save array/pose/parent total. Guest report requests host correction only. |
| 45 | PartState | 0 | netId, flags (bit 0 installed), legacy tightness:u8, legacy wear:u8; v107 appends tightnessValue:f32, wearValue:f32. Host observations overwrite guest Data scalars without unit clamping. Guest reports request a fresh host observation after native interactions settle; their values are never applied on the host. |
| 46 | ItemDespawn | 0 | itemId — pickable eaten/destroyed (Destroy, Check drink → State 2/5/6); receivers remove their local rigidbody. Since v105, host-authorized removal is retained for the session even before the body exists, preventing deferred creation or replay from reviving it. Standard packages run native GARBAGE on the host; v109 guests remove their isolated replica while preserving their local saved boxes. |
| 47 | WorldStateChecksum | 0 | host -> guests every ~20 s: walletCrc, worldCrc (FSM + part + bolt vars), itemCrc (resting pickables), vehicleCrc, seq — guests compare and may request soft resync. **v96 vehicle fold:** vehicles sorted by id, excluding local/remote ownership and unavailable vehicle systems. Starting at `StableHash.OffsetBasis`, combine each id, engine/accessory/blinker/hazard flags, fuel byte, concrete damage mask, tire-pressure byte, drivetrain-damage byte, wheel health FL/FR/RL/RR bytes, then wheel puncture/rim flags, using `StableHash.Combine` for each value. Damage uses live fitted-part wear to reconcile `LiveDamageMask \| AppliedDamageMask`; known repairs clear stale failures and unknown parts retain prior state. Missing damage/condition systems contribute zero. RPM, climate, continuous part wear, binding bookkeeping, ownership ids and stream sequences are excluded. Reads do not advance send sequences or change baselines. A vehicle mismatch requests the existing complete vehicle resync (state, climate, damage and condition). |
| 48 | WorldResyncRequest | 0 | guest -> host: flags (bit 0 wallet, bit 1 FSM states, bit 2 parts, bit 3 bolts, bit 4 items, bit 5 vehicles), checksumSequence — flags must name at least one listed group; host replies with targeted snapshot chunks and accepts at most one request per guest every 15 s |
| 49 | WorldObjectStateRequest | 0 | guest -> host: netId — host replies with the object state it has (final ItemTransform, VehicleState/Climate, FsmStateEnter, PartState, or BoltState); v107 part replies include both the known FSM state and native scalar state; guests auto-request after a pending FSM event expires (5 s cooldown per id), while the host admits at most four requests per second per guest |
| 50 | HeatSourceState | 0 | host -> all (v28): sourceId (scene-path hash of the source container), flags (bit 0 = lit/embers), fuel (0-255 firewood), heatOutput (0-255), saunaTemp (sauna heat ×100, ushort; 0 for non-sauna). Host-authoritative shared state for the cabin woodstove (`CABIN/Cabin/woodstove/Fireplace`), sauna kiuas (`COTTAGE/Stuff/Sauna/Stove` — `SaunaHeat`/`StoveHeat`), and cottage/living-room fireplaces. Broadcast on change + ~20 s keepalive from whoever hosts; guests write the values back onto their local FSMs so each client's own position-derived body-temp calc warms consistently |
| 51 | HeatSourceIntent | 0 | guest -> host (v49): sourceId, action (0 light, 1 feed wood, 2 grill, 3 löyly/steam), authenticated player id, sequence — anyone-triggers. The guest emits this when it locally enters the source's lighting/feeding/grilling/steam FSM state (debounced 0.75 s); the host accepts only the sender's fresh next sequence *for that source* (per-player-per-source latch; guests count intents per source) while their pose is within 8 m of the exact source, then fires the matching game event (`USE`/`WOOD`/`SAUSAGE`/`STEAM`) on its authoritative FSM. Invalid source/action, stale/replayed packet, and distant player reports are dropped; the resulting HeatSourceState carries accepted progression back. |
| 52 | ItemSpawn | 0 | host → guests: containerNetId (uint32), epoch (uint16), ownerPlayerId (byte), stateName (string), count (byte, ≤32), entries (netId uint32, templateName string, position vec3, rotation quat), flags (byte: bit0 replay, bit1 catalog trophy factory), offerSequence (uint16, zero for bags in v120). Exact native host outputs; duplicate receipts cannot create another item. Missing templates retry, and join/resync replays refresh live entries. |
| 53 | SpawnIntent — retired v120 | — | Former guest spill offers. Ignored by the session dispatcher; ID remains reserved. Guests use BagOpenRequest (191). |
| 54 | FluidContainerState | 0 | owner -> host -> others: tracked fuel-container item id, owner player id, sequence, flags (bit 0 pouring), fuel level, capacity. The host accepts and relays a guest update only while its item-transform ownership still names that guest, its owner-local sequence advances, and flags/level/capacity are finite and in range; rejected packets never reach other peers. |
| 55 | WorldProgressState | 0 | host -> guests: compact scalar mirror of a host-owned M8 system. `kind` 1 = classifieds (`phase` job stage; primary delivered; secondary sheets; tertiary day; value salary); 2 = factory (employment stage; empty/total packages; paychecks; worked minutes); 3 = Marketti magazine (layout type; day/index/issue). |
| 56 | JobSiteState | 0 | host -> guests: stable FSM-path id, kind, flags, sequence, primary and secondary values. Kind 1 sewage site: bit 0 `Called`, `ShitLevel`, `BasePrice`; kind 2 firewood site: bit 0 `Order`, `Surplus`, `Penalty`; **kind 3 (v35) GIFU sewage truck**: bit 0 `PumpRunning`, bit 1 `HoseAttached`, bit 2 `HoseInShit`, bit 3 `Sucking`, `ShitLevel`, `PumpEfficiency`. Every registered site and the truck pump are included in join snapshots. | **v67: kind 4 = farm (Primary=int JobStage, Active=Done).**
| 57 | MailOrderState | 0 | host -> guests (v36): persisted mail-order record: kind (1 AMIS, 2 Yellow Pages, 3 hidden Yellow Pages), flags (bit 0 active, bit 1 consumed, bit 2 saved position), seq, price, wait time, price integer, and the game's three opaque saved package strings. It is sent on change and in join snapshots so a pending host order retains its delivery descriptor on every peer. |
| 58 | MailOrderIntent | 0 | guest -> host (v37): player id, exact `OrderAMIS`/`OrderYP` data-FSM id, kind, flags, seq, price/wait/price-int, and the three selected-listing package strings. The host authenticates player id, accepts only that player’s next monotonic order sequence, holds the record for 5 seconds, and applies it only when the same player’s immediately following `PurchaseIntent(PAYMENT)` targets that exact order FSM; otherwise the payment is rejected. This makes the host spawn the guest’s actual generated phone selection rather than its own divergent local listing. |
| 59 | InspectionState | 0 | host -> guests (v38; appended v40): flags (host inspection record: inspected/stamp/museum registration/pass/issued stamp/museum order), seq, next inspection day, renewal intervals, two bitmasks with 38 result-sheet check slots (36 bind on the current game build — `ShockRL`/`ShockRR` have no Results-FSM bool there and their bits stay 0; the slots are kept so a future build adding rear-shock checks binds without a bit reshuffle), then plate availability flags and the host-generated standard/museum plate strings. Peers write those exact generator values and activate the matching physical plate pairs; no client reruns the random plate FSM. Sent on change and in join snapshots; inspection order/payment already use the host purchase path, so the host car’s evaluated result is authoritative. |
| 60 | VehicleState | 1 (repair/reconciliation: 0) | vehicleId, ownerPlayerId, seq, flags (bit 0 engine on, bit 1 ACC/electrics on, bit 2 blinker left, bit 3 blinker right, bit 4 hazard), rpm, speedTenthsKmh, fuelLevel (0-255), coolantTemp (0-255 → 0-120 °C), **gear** (v63: gear+1, 0=reverse/1=neutral) — ~4 Hz from whoever is driving *or* left the engine/ACC running locally; receivers replay Electricity ON/OFF FSM, push rpm/speed/fuel/coolant into gauge variables (CORRIS angle gauges included), apply blinker/hazard stalk/events + hazard button replay, and synthesize engine audio (pitch from RPM), stopping after 2 s without packets |
| 61 | VehicleClimate | 1 (snapshot/repair: 0) | vehicleId, ownerPlayerId, seq, frost (0-255 **interior** glass frost, `GlassFrosting.Frost`), flags (bit 0 window heater, bit 1 glass defrosting, bit 2 player in cabin), heaterTemp, heaterBlower, heaterDirection, fog (0-255 interior condensation), cabinTemp (0-255 → **-40 to +40 °C**, ranged quantization so a cold parked cabin isn't floored at 0 °C), **ice (0-255 exterior window ice, `Freezing.CutoffWindshield`; appended v28)** — ~2 Hz; receivers apply frost → GlassFrosting.Frost + FrostGlass material + color.a, ice → the Freezing cutoff windows, fog → SweatRate + color.rgb + InteriorTemp + PlayerIn, replay window-heater On/Off, pulse DEFROST; LateUpdate keeps visuals pinned; included in join snapshot. **Interior frost and exterior ice are separate fields** — a parked cold car is iced outside yet clear inside, so they must not be collapsed (was one `max()` value pre-v28, which force-frosted observers' interiors). |
| 62 | VehicleCargo | 1 (empty set: 0) | vehicleId, ownerPlayerId, seq, count (≤24), entries (itemId, localPos, localRot in vehicle-root space) — sent by the vehicle's transform owner at the vehicle send rate while it moves; the owner's physics simulates the cargo and streams its live vehicle-local poses. Each packet is the COMPLETE cargo set: receivers pin listed items kinematically (composed against their own smoothed vehicle pose, colliders untouched) and release tracked items that are no longer listed, seeding them with the pin's observed world motion (a rider inherits ~the car's velocity, an item merely shielded at its resting pose stays at rest; the car's velocity is the fallback when no fresh sample exists). The non-empty → empty transition is sent reliably; per-vehicle wrap-aware seq dedup per owner |
| 63 | VehicleFuelIntent | 0 | guest -> host (v39): vehicle id, Peräpörtti nozzle FSM id, authenticated player id, sequence, requested tank level (0-255 of that tank's capacity). A guest emits it only while its local nozzle is actively dispensing into a nearby non-owned vehicle. The host accepts only monotonically increasing, rate-limited fuel while the player's fresh pose, the static nozzle, and the stationary vehicle are co-located; it writes the real host tank and returns a reliable `VehicleState` reconciliation. |
| 64 | VehicleDamage | 0 | owner -> host -> peers (**v91**): vehicleId (uint), ownerPlayerId (byte), damageMask (uint), seq (ushort), **knownPartsMask (uint), wear (16 floats, fixed length)**. Slots 0–4 bearing1–5, 5 crankshaft, 6 headgasket, 7–10 piston1–4, 11 oilpan, 12 timingbelt, 14 block. Slots 13 (SEIZE) and 15 (CAMFAIL) are **retired random selectors**, always absent from both masks; no unknown bits are allowed. Known wear must be finite and its damage bit must match wear ≤ 0. Unknown slots preserve prior condition. The driver owns the simulation; the host owns parked cars. Non-owners gate local damage states and replay only concrete failures, then apply exact part wear. Repairs clear bits from live fitted-part reads. Change broadcasts plus 15 s keepalives, joins and targeted vehicle resyncs include healthy parts too. Host validates sender ownership (an ownerless car tolerates the reliable packet arriving before the unreliable claim) and values; receivers dedup sequences per sender, rebasing on ownership changes/rejoin. |
| 65 | VehicleCondition | 0 | owner -> host -> peers (**v61**): vehicleId, ownerPlayerId, seq, tirePressure (byte, bar*100), drivetrainDamage (byte, GearboxDamage `DamageType`), per-wheel health FL/FR/RL/RR (byte), flags (bit0-3 puncture, bit4-7 rim per wheel). **Owner-authoritative** drivetrain wear + tire condition: non-owners write health/pressure and fire PUNCTURE/RIM/FIXED on each wheel FSM, diffing against the wheel FSM's *actual* state so keepalives heal local drift. Re-sent on change + 20 s keepalive; host validates owner + relays. **v82:** also in the join snapshot (host reads its live FSMs for every vehicle, parked ones included) with per-sender sequence rebase as for id 64. |
| 66 | CarRadioState | 0 | host -> guests (**v81**): seq, radioId (0 corris, 1 sorbet), tune (float), volume (byte, knob ×100). Host owns the in-car radio tuning + volume; guests apply. `tune` replaced a byte `channel` in v81 — no radio Knob FSM has a float named Channel (the only Channel is a *string* on the CD player), so the old field bound null and the station never synced. Bound to `StockRadio0/ButtonsRadio/Volume :: Knob`, which carries both `Tune` and `Volume`. Unquantized: the per-station windows are not knowable from the catalog dump. In the join snapshot. |
| 80 | WalletState | 0 | money (float mk), seq (ushort), bankBalance (float), netIncome (float), flags (byte; bit0 bank available, bit1 income available). The final three fields were appended in **v90**. Host -> guests on change, every ~2 s, on join and wallet resync. Binds the verified globals `PlayerMoney`, `PlayerBankAccount`, `PlayerNetIncome`. Missing optional bindings never block cash or overwrite a valid remote balance with zero. Guests retain the latest accepted state for delayed bindings and local drift correction; wrapping sequences reject stale state. Wallet CRC now includes cash, bank and income rounded to whole mk. Guest bank interest/ledger FSM is suppressed for the session and restored on exit. |
| 81 | PurchaseIntent | 0 | guest -> host only: playerId, netId (Buy/CashRegister/Use/Data/Button buy FSM), eventName (USE/PURCHASE/DEPURCHASE/PAY/PAYMENT/BUY/CLICK/**ACTIVATE**), seq — guest aborts local buy guard and restores wallet; host fires the event and broadcasts resulting FsmStateEnter + WalletState. **Since v48**, the host accepts only the authenticated player’s next monotonic sequence while that player has a fresh pose within the buy target’s interaction radius, and only for a catalogued entry-guard event; unknown, distant, non-entry, duplicate, and stale packets do not reach the FSM. `ACTIVATE` is used by the host-validated Fines record (v41) to enter its normal money check without trying to replay a remote player's police collision. **v50** adds the exact `PriceMoneyRace` and `PriceMoneyRally` `USE` paths, so race prize collection executes only on the host and converges through the shared wallet. |
| 82 | PoliceState | 0 | host -> guests (v41): target player id, offence flags, active flag, sequence, stable checkpoint id, and host-validated fine. The host writes the fine to its own Fines record and peers mirror that durable record; payment still goes through the normal guarded `PurchaseIntent` path, so the shared wallet remains host-owned. When the host's Fines price clears (fine paid/reset), it broadcasts the record once more with the active flag cleared and drops it from join snapshots, so late joiners never inherit a phantom fine. A guest advances its report-dedup baseline only on the host's accepting echo, re-sending each interval until then. Offence bits: alcohol, fuel, helmet, inspection, radar, registration plates, seatbelts, speeding (low to high bit). |
| 83 | PoliceIntent | 0 | guest -> host (v41): player id, locally observed checkpoint offence bits, sequence, stable checkpoint id, and the locally generated fine. This is a report rather than authority: the host accepts only a fresh authenticated player transform close to that exact known checkpoint and a nearby vehicle currently delegated to that player; stale/replayed sequences, invalid flags, and non-finite/out-of-range fines are discarded. |
| 84 | HomeStereoState | 0 | host -> guests (v42): fixed home stereo power/channel flags, sequence, volume and bass. These are the durable audio/electricity inputs, rather than fragile presentation FSM states; sent on host change and in join snapshots. |
| 85 | HomeStereoIntent | 0 | guest -> host (v42): authenticated player id, requested power/channel flags, sequence, volume and bass. The host accepts only finite normalised values while the sender's fresh pose is within 6 m of the exact home-stereo switch, then writes and broadcasts the authoritative scalar state. |
| 86 | RallyState | 0 | host -> guests (v43, extended **v101**). Wire order: playerId/stage/phase/checkpoint (bytes), sequence (ushort), elapsedCentiseconds (uint); appended revision (uint), reportToken (ulong), reportSequence (ushort), flags (byte: bit0 HasReport). Stage is 1–3, checkpoint 0–6, phase 1 racing or 2 finished; phase 0 remains reserved/decodable but is not published or applied. Finished requires checkpoint >0. HasReport requires a nonzero token; otherwise token and reportSequence must be zero. Other flags and player id 255 are invalid. Revision is per player, with forward uint delta 1..2³¹−1, including wrap; the old sequence remains on the wire but no longer gates application. HasReport echoes the most recently accepted guest command for that record; snapshots and racing keepalives can acknowledge it too. An exact duplicate command returns current progress with the same acknowledgment, without replaying the crossing. Elapsed time uses the host clock and freezes at finish, including a finish at time zero. Sent on acceptance/duplicate acknowledgment, every second while racing, once for a host-local finish, and in join/resync snapshots. Snapshots do not consume an unpublished finish. Packet length 27 bytes including id. |
| 87 | RallyIntent | 0 | guest -> host (v43, extended **v101**). Wire order: playerId/stage/checkpoint (bytes), sequence (ushort); appended reportToken (ulong, nonzero, freshly generated per guest connection). Session authenticates playerId. Stage 1–3, start 0 or checkpoint 1–6 within the completely bound native stage. Guest retains up to 21 crossing edges in checkpoint order, sends only the head, and retries the exact command every 0.25 s until 86 echoes player/stage/checkpoint/token/sequence. A fresh native start abandons old pending edges; preexisting flags at initial bind/reconnect are only a baseline. Ten seconds without acknowledgment clears pending reports and asks the player to restart the stage. Host checks marker proximity from its own recent driver evidence (below), then strict next-checkpoint progression and a forward ushort delta 1..32767. Only accepted reports advance the sequence/token baseline. Same sequence with different stage/checkpoint, or another token before readmission, is rejected. Admission drops cached report identity and proximity evidence while retaining the race and elapsed clock. Packet length 15 bytes including id. |
| 88 | IceRaceState | 0 | host -> guests (v44): player id, inferred time-trial/lap-race start mode, accepted checkpoint phase, completed laps, sequence and host-clocked elapsed centiseconds. Sent on accepted markers, every second while active, and in join snapshots. |
| 89 | IceRaceIntent | 0 | guest -> host (v44): authenticated player id, marker (start, checkpoint 1, checkpoint 2, finish) and sequence. The host infers the start mode only from the driver’s proximity to one of the two fixed start/finish markers, then requires checkpoint 1 → checkpoint 2 → that mode’s finish marker for each lap with fresh pose and nearby delegated vehicle validation. |
| 90 | IceRaceEventState | 0 | host -> guests (v47): grid-ready/on-track/player-registered flags, sequence, car-limit/current-car/on-track counts, heat stage, lane, final race distance then qualifying race distance (wire order), starter count, event time, selected car id/reference, then race stage. This mirrors `RACES/ICERACE/TrackFunctions :: Data` plus `TrackFunctions/LINEUPS :: Logic` on change and in join snapshots, so registration/grid/heat presentation uses the host event configuration. |
| 91 | IceRaceResultsState | 0 | host -> guests (v46): sequence plus up to six ordered result rows (driver name, number, model, UA). The rows come from the host-generated `Stats/ResultsRace/Data/{0..5}` records and are mirrored on change and in join snapshots so every result board shows the same ranking rather than each client rerunning leaderboard generation. |
| 92 | RadiatorThermostatState | 0 | host -> guests (v54): thermostat Knob FSM `netId` + its game-owned `Rotation` float after the host applies a turn. Guests emit only the +/- knob `FsmStateEnter` intent; the host applies it and broadcasts this absolute settled value so receivers and joiners *set* the rotation rather than re-applying a relative increase/decrease. Sent on change and in join snapshots; a state whose target Knob FSM has not registered yet is held as a pending apply until it does. |
| 93 | GamblingState (retired) | 0 | Retired in **v98**; no current sender or game-state handler. Old layout stays decodable for diagnostics: machineId (uint), kind/flags (bytes), credit (float), bet/V1/V2/V3 (bytes), payout (int). Slots moved to 164–166 in v92; Ventti table observations now use 174, property keys/access use 173. Never reuse id 93 or its retired fields. |
| 94 | GamblingIntent (retired) | 0 | Retired in **v99**, with no sender or game-state handler. Diagnostic layout remains machineId (uint), action (byte), playerId (byte), sequence (ushort). Slot actions 0–6 and Ventti actions 7–11 remain reserved forever. Replaced by 165 for slots and 176 for Ventti. |
| 95 | UtilityBillState | 0 | host -> guests (**v58**): meter (0/1 electricity, 2/3 phone), unpaidBills (float), flags (bit0 power-on = electricity `MainSwitch` / phone `PhonePaid`). Host owns the bill ledger + blackout and broadcasts on change + keepalive + join; guests write it back so both homes cut power together. Bill payment is **not** here — the Pay buttons are catalogued `buys[]`, so paying debits the shared wallet via the host purchase path. |
| 99 | ApplianceState | 0 | host -> guests (**v75**, grown **v85**): applianceId (scene-path hash), kind (0 oven), flags (bit0 fire, bit1 fuse-ok), heat1-4 (byte, hotplate heat), fireCount (byte, wrapping), firePlate (byte 1-4, 0 = none — both appended v85). Host owns each oven/stove; guests apply the heats + fuse. Ignition is edge-carried: the `Start fire N` commit states are one-frame transients (which is why the level-sampled bit0 is nearly always false), so the host hooks them and bumps fireCount; a guest replays that plate's commit state once per bump. The first count a guest ever receives only seeds its baseline — a fire that predates the join is not re-ignited hours later. In the join snapshot. |
| 96 | LotteryDrawState (retired) | 0 | Retired in **v102**; diagnostic decoding only, no live sender/game-state handler. Legacy layout remains round (int), nationalPot (int), winningNumbers (string), flags (byte). The old string binding `UTNational7` is the save key `Lotto7`, not winning numbers. Never reuse id 96. Replaced by 180. |
| 97 | FleetariOrderState | 0 | host -> guests (**v62**): flags (bit0 order active), seq, jobTotalCost (float), carPaintColor + rimPaintColor (packed RGBA), jobs/orderCode/paintCode/axleCode/tireCode (strings). The shared repair-shop order record; broadcast on change + join so observers/joiners agree. The host's Work FSMs apply it to the host-owned shop car. |
| 98 | FleetariOrderIntent | 0 | guest -> host (**v62**): the exact `OrderFleetari` record the guest configured (same fields + playerId), captured when it confirms so the host pairs it to that guest's next `PurchaseIntent(PAYMENT/PAY)` and applies the actual jobs. Mirrors `MailOrderIntent` capture-and-pair; payment rides the existing catalogued OrderFleetari buy. |
| 100 | NpcTransform | 1 (final: 0) | netId, seq, flags, pos, rot — host-only stream for TRAFFIC/, NPC_CARS/, HUMANS/, and ice-race opponent rigidbodies; guests pin kinematic and ease toward pose (also freezing race-driving FSMs); distance tiers ~8 Hz (≤80 m), ~3 Hz (≤200 m), off beyond; moving bodies still stream until a reliable **final** at-rest packet. Also carries the transform-driven *scripted movers* (no rigidbody; guest AI FSM frozen while the stream is live): the moose, the hitchhiker, Reijo the janitor, and the farm-job farmer's Walker (the last so a guest's payday press passes the host's PayMoney proximity gate). Flags: bit0 final, bit1 dead (moose corpse, see id 110). |
| 101 | FleaSaleState | 0 | host -> guests (**v64**): seq, moneyTotal (float), rentDays (ushort), flags (bit0 rented). Host owns the flea sale table + runs the day-timed sale RNG; guests suppress their local `SaleTable :: Sell` FSM and apply proceeds/rent. In the join snapshot. Per-item placement/pricing stays local (dynamic refs). |
| 102 | FleaSaleIntent | 0 | guest -> host (**v64**): action (0 rent; 1 collect is reserved-unused — envelope collection rides the catalogued MoneyFlea control via `FsmStateEnter`, so no client emits it and the host rejects it), playerId, seq. Host validates fresh nearby pose + monotonic seq and fires the real RENT event so the shared wallet moves once. |
| 103 | TaxiJobState | 0 | host -> guests (**v65**, grown **v87**): seq, jobStage (int), money (float — the employment payday account, `TaxiFunctions :: Payments`), kmsDriven (float), flags (bit0 employed), fareCost (float, appended v87 — the customer's live per-ride meter, `Customer1/TaxiWalker :: Logic` Cost). The customer is a host-authoritative ScriptedMover (id 100), so the fare accrues host-side off the driver's synced taxi, guests' frozen meters show the synced value, and the hand PayMoney press (catalogued control) passes the host's proximity gate — the payout rides `WalletState`. In the join snapshot. |
| 104 | WorldScalarsState | 0 | host -> guests (**v86**): seq, scrapPriceMKkg (float), scrapChange (float), primeInterest (float), uncleStage (byte), conlineNumber (int), flags (bit0 GIFU key). Misc host-owned world scalars that each re-roll or progress per-client: the daily scrap-metal price, the bank prime interest rate (the *rate* half of the bank gap — the balance itself is a PlayMaker global, gated on a fresh dump), and `Database/Keys :: PlayerKeys` progression. Guests write the values back; their own daily re-rolls get stomped on the next tick. On change + 30 s keepalive + join snapshot. |
| 105 | BrewState | 0 | owner -> host -> peers (**v66**): itemId (tracked bucket), ownerPlayerId, seq, flags (bit0 finished, bit1 lid on), alcohol (float), brewTime (float). Kilju fermentation carried like `FluidContainerState` — whoever holds the bucket streams it, others apply; the stream also carries ingredient-add effects. Since v82+ a guest lid-flip on a resting bucket claims the bucket so the interaction streams (see KiljuSync). In the join snapshot. |
| 107 | WelfareState | 0 | host -> guests (**v68**, grown **v84**): seq, unemployDays (int), paidAmount (float), weekly (float), flags (bit0 claiming, bit1 evicted), rentDebt (float), rentPerWeek (float), asumistukiPerWeek (float — the last three appended v84). Host owns the whole `Systems/Expenses` record (Kela claim + weekly rent debit + housing benefit); guests apply the scalars, suppress their own Rent/Livingsupport FSMs for the session (their local weekly ticks are throwaway divergence — a guest plays in the host's world), and replay the terminal `Kick out` eviction state once when bit1 appears (furniture destruction + relocation happen everywhere). In the join snapshot. |
| 106 | HitchhikerState | 0 | host -> guests (**v69**): seq, drunkStage (int), movingStage (int), money (int), flags (bit0 paid, bit1 KiljuMurderer, bit2 suicide, bit3 active). Host owns the hiker variant + stage; guests apply. Body pose streams over NpcTransform (ScriptedMover). In the join snapshot. |
| 108 | PhoneCallEvent | 0 | host -> guests (**v76**): callId (ushort, monotonic), topic (string). Host decides an incoming call and broadcasts it; guests set the phone Topic + fire the matching ring event. Discrete one-shot (not in snapshot). |
| 109 | PissAreaState | 0 | host -> guests (**v80**): seq, scale1-5 (byte, *20). Host owns the five persistent yard piss-stain scales; guests apply. In the join snapshot. |
| 110 | NpcDeathReport | 0 | guest -> host (**v82**): netId (scripted mover), playerId, seq. The reporter's local copy of a streamed animal died — the moose `CarHit` FSM is deliberately left live on guests, so a guest's car kills only its own copy. Host validates the authenticated reporter has a fresh pose within 60 m of its copy, then replays the vanilla `CarHit` death entry so the kill becomes authoritative and `NpcTransform` `FlagDead` streams to everyone (including at-rest movers via a one-shot death announce). Idempotent on the host; the guest re-sends every 3 s until FlagDead echoes back, so there is no send-latch to lose. |
| 120 | WorldSnapshotRequest | 0 | guest -> host once its first world scan completes; carries the guest's id hash (diagnostic only). Host accepts at most one request per guest every 10 s. |
| 121 | WorldDoorSnapshot | 0 | host -> guest: (netId, stateName) pairs for doors/ignitions/controls/starters the host saw change; chunked (≤60/message) |
| 122 | WorldItemSnapshot | 0 | host -> guest: (itemId, pos, rot) for every item/vehicle; chunked (≤40/message); unknown ids are parked until scanned |
| 123 | WorldBoltSnapshot | 0 | host -> guest: count:u16, complete (netId:u32, boltTightness:u16, screwInt:u16) entry block, then v114 partTightness:f32 for each entry in order. Every ready fitted bolt, including zero; ≤80/message; unready IDs wait for binding. |
| 124 | WorldPartSnapshot | 0 | host -> guest: count:u16, legacy (netId:u32, flags:u8, tightness:u8, wear:u8) entries; v107 appends one (tightnessValue:f32, wearValue:f32) pair per entry, in the same order, after the complete legacy block. All readable car parts, including zero values; ≤80/message; delayed bindings retry. |
| 125 | WorldItemDespawnSnapshot | 0 | host -> guest: itemIds consumed/destroyed during this session; guests retain terminal removals and delete existing or subsequently scanned/materialized copies; chunked (≤80/message). Included in join and, since v105, item-group resync before replay manifests. |
| 140 | WantedState | 0 | host -> guests (**v70**): seq, manslaughter/attemptedManslaughter/policeEvasion/trafficFatality/daysFines/sentence/daysInJail (int), flags (bit0 cousin). Host owns the shared group wanted level; guests apply. In the join snapshot. |
| 141 | CrimeReport | 0 | guest -> host (**v70**): playerId, seq, crimeType (0 manslaughter..4 daysFines), delta (int, ≤32). A guest whose local `PlayerWanted` counter rose reports the delta; host validates identity + monotonic seq and adds it to its authoritative counter. |
| 142 | JailState | 0 | two-way (**v71**, reshaped **v83**): seq, daysLeft (int), sentence (int), flags (bit0 jailed), jailedPlayerId (byte, 255 = nobody; appended v83). The arrest→jail flow is offender-local, so the day-countdown runs ONLY on the jailed client — that client owns the record: while its local DaysLeft is positive it sends this guest → host (change + 5 s keepalive + one 0-report on release); the host validates the authenticated sender IS the claimed player, adopts the record (30 s TTL against a vanished reporter) and relays it host → guests with its own sequence. With nobody or the host jailed, the host broadcasts its own FSM. Non-jailed clients write DaysLeft for presentation; the jailed client ignores broadcasts about itself. Confinement position rides the player transform stream. In the join snapshot. |
| 143 | PursuitState | 0 | host -> guests (**v72**): seq, flags (bit0/1 cop car 1/2 chasing, bit2/3 cop car 1/2 siren). Host owns the pursuit; guests apply the sirens (lights) **only** — the chase bits are observability, deliberately not mirrored onto the guest's CopPassenger FSM (that would make each guest raise its own duplicate fine). Cop-car pose streams over NpcTransform. In the join snapshot. |
| 160 | HockeyBettingState | 0 | host -> guests (**v88**, appended **v104**): legacy scalar prefix followed by gamesPlayed, upcoming/previous pairings, six 1/X/2 odds tables, six result symbols and odds, scores and standings text. Whole completed boards broadcast on change + 30 s keepalive + join. See Hockey v104 below; this does not authorize Megaveto ticket payments. |
| 161 | ApplianceFireReport | 0 | guest -> host (**v89**): applianceId, plate (1-4), playerId, seq. The sender's own oven sim rolled an ignition — the FireHazard RNG runs per-client even over synced heats, so without this a guest's house fire stayed invisible to everyone else. Host validates the authenticated sender + monotonic per-player seq (reset on rejoin), then replays the plate's ignition-commit state on its authoritative oven; the shared fire streams back via id 99's fireCount. Guest-side replays of the host's own ignition are suppressed from re-reporting (echo guard), and reports pace at one per 30 s per oven. |
| 162 | BankTransferIntent | 0 | guest -> host (**v90**): playerId (byte), sequence (ushort), amount (signed int16). +100 deposits one note; -100/-200/-300/-500/-800/-1000 withdraw. Host requires the authenticated sender, a fresh live-player pose within 6 m of the ATM, an allowed denomination, sufficient funds, finite balances and sub-cent conservation. Guests settle deposits per inserted note and withdrawals on cash collection; only the corresponding vanilla money actions are gated, preserving local ATM controls. One outstanding request per guest, retried each second with the same sequence. Missing bindings/pose are transient; insufficient funds and distant requests are terminal rejections. |
| 163 | BankTransferResult | 0 | host -> guests (**v90**): playerId (byte), sequence (ushort), accepted (bool byte). Only the named requester consumes it. Host broadcasts a fresh WalletState before this acknowledgment. Accepted and rejected receipts are cached per player: duplicate requests re-acknowledge without moving money again; older sequences are dropped. Admission resets that player's receipt. Guest dequeues only on a matching result; disconnect clears pending work. Transfers do not affect taxable income. Bank statement history remains host-local; the shared numeric balances are authoritative. |
| 164 | SlotMachineState | 0 | host -> guests (**v92**), fields in wire order: machineId, revision, round (uint each); playerId (byte, 255 = no lease); spinning (bool byte); bet (byte 1–5); holdMask (byte, bits0–2, at most two); canHold (bool byte); credit, winnings, lastWin (int each); reel1–3 (byte each, raw stops 1–9; 0 only before a first result). Credit and accumulated winnings are separate, bounded 0–1,000,000 mk. LastWin is the current round's predetermined payout while spinning, otherwise the last completed payout, bounded 0–5000. Spinning/canHold require three nonzero stops; held reels require canHold. A host ledger draws from catalogued weighted reels without activating host UI. Local native animations use those exact stops and skip their RNG actions. Revision is compared modulo uint (forward delta ≤2³¹−1); change + 5 s keepalive + join force-broadcast. State is retained for inactive/late-bound machines. |
| 165 | SlotMachineResult | 0 | host -> guests (**v92**): machineId (uint), playerId (byte), sequence (ushort), result (byte: 0 accepted, 1 busy, 2 funds/capacity, 3 invalid, 4 distant), cashDelta (signed int). Host sends machine state and WalletState first. Only the matching outstanding requester dequeues; cached receipts retain both result and cashDelta. Delta is negative for inserted money, positive for cash-out, zero otherwise; it is informational and never applied a second time to the wallet. An accepted cash-out of at least the catalogued threshold triggers the requester's native slot achievement. |
| 166 | SlotMachineIntent | 0 | guest -> host (**v92**): machineId (uint), playerId (byte), sequence (ushort), action (byte: 0 insert current bet, 1 cycle bet 1–5, 2 spin, 3–5 toggle hold, 6 cash-out, 7 animation finished), round (uint; zero except action 7). Host's own controls use the same ledger. Authenticated guests require a fresh live pose within 6 m. Per-machine/player receipts deduplicate exact requests, reject changed payloads at the same sequence, and compare ushort sequence modulo 65536 (forward delta ≤32767). One outstanding request per machine retries every second; missing bindings/pose and an early finish are transient, all reported rejections terminal. A 15 s idle lease excludes other players; disconnect releases it. Spin debits credit if sufficient, otherwise winnings if sufficient, never combines insufficient sources. Cash-out pays accumulated winnings only. Completion credits the predetermined win once; minimum finish delay is 0.5 s per unheld reel, with a 10 s host timeout and settlement on disconnect/session end. Rejoin clears that player's receipts. Holds follow native eligibility and permit at most two; bet wrap 5→1 preserves holds as the game does. Cash mutations require finite, sub-cent-conserving floats. |
| 167 | PokerState | 0 | host -> guests (**v93**), wire order: machineId, revision, round (uint each); playerId (byte, 255 = unleased), phase (byte: 0 ready, 1 hold/redraw, 2 win offer, 3 high/low guess), bet (byte 1–5), holdMask (byte bits0–4, nonzero only in phase 1), hand (byte 0–9); credit, winnings, pendingWin (int each); five card bytes; doubleCard (byte). Cards encode suit×13+rank: suits spades/clubs/hearts/diamonds = 0/1/2/3, ace = 1 through king = 13. Zero is unset/covered. Main cards must be five distinct 1–52 values except an all-zero ready state. The private doubling card is always zero in phase 3, revealed only after a guess. Credit/winnings are separately bounded 0–9999; pendingWin is 0–998 and positive only in phases 2/3; winnings + pendingWin ≤9999. uint revision comparison accepts forward delta ≤2³¹−1. Change, 5 s keepalive and join broadcast; guests retain valid snapshots before the town/UI binds. |
| 168 | PokerResult | 0 | host -> guests (**v93**): machineId (uint), playerId (byte), sequence (ushort), result (byte: 0 accepted, 1 busy, 2 funds/capacity, 3 invalid, 4 distant), cashDelta (signed int), achievements (byte flags: bit0 royal flush, bit1 cash-out threshold). State and WalletState precede the receipt. Only a matching outstanding request dequeues and awards the native achievement; retries retain the original receipt including amount and flags. CashDelta is informational and is never applied to the wallet by the recipient. |
| 169 | PokerIntent | 0 | guest -> host (**v93**): machineId (uint), playerId (byte), sequence (ushort), action (byte: 0 insert current bet, 1 cycle bet, 2 deal/redraw/collect, 3 double, 4 low, 5 high, 6–10 hold cards 1–5, 11 collect/cash-out), round (uint matching the currently displayed round for every action). Authenticated fresh live pose within 6 m; host-local input follows the same ledger. Exact receipt deduplication and ushort sequence comparison (forward delta ≤32767); changed payload at the same sequence and older sequences are silently discarded. One outstanding request retries each second; unavailable bindings/pose are transient, reported rejections terminal. Controls are leased for 30 s while ready, 300 s during a hand; expiry/disconnect releases controls but preserves the exact hand, used cards and any committed secret double. Rejoining clears that player's receipts. |
| 170 | DebtLetterState | 0 | host -> guests (**v94**), wire order: revision (uint), debt (float principal), total (float payable), available (bool byte). Amounts must be finite/nonnegative; debt and total are either both zero or both positive, and available requires positive debt. Revision advances on any principal, rate, fee or envelope-availability change, even when a different fee composition produces the same total. uint forward delta ≤2³¹−1; change + 5 s keepalive + join snapshot. Guests retain valid state before the letter binds. |
| 171 | DebtPaymentIntent | 0 | guest -> host (**v94**): playerId (byte), sequence (ushort), revision (uint of the displayed quote). Host-local input uses the same ledger. Authenticated guests require a fresh live pose within 6 m of the envelope referenced by Rent/Letter, including after eviction relocates the mailbox. One outstanding payment retries every second. Missing bindings/pose are transient; reported rejections are terminal. Exact receipts are cached per player; changed revision at the same sequence and older sequences are silently discarded (ushort forward delta ≤32767). Admission/departure clears that player's receipts. |
| 172 | DebtPaymentResult | 0 | host -> guests (**v94**): playerId (byte), sequence (ushort), result (byte: 0 accepted, 1 quote changed, 2 unavailable, 3 distant, 4 funds/precision), paid (float: exact debit on success, zero on rejection). State, WalletState and WelfareState precede the receipt. Only the named player's matching outstanding request dequeues; exact retries return the original result and paid amount without charging again. Paid is informational and is never applied by the recipient. Internal stale code 255 is discarded, never sent. |
| 173 | VenttiPropertyState | 0 | host -> guests (**v97**), wire order: sequence (uint), keys (byte: bit0 Ruscko, bit1 Satsuma, bit2 Home), knownAccess (byte), access (byte). Access bits: 0 cabin Sleep activeSelf, 1 woodstove hatch Handle activeSelf, 2 Logwall Use enabled. All other bits are invalid; access must be a subset of knownAccess. All three key globals must bind and contain 0/1 before host publication. Missing access bindings are unknown, never a revocation. Guest requires a forward uint sequence delta in 1..2³¹−1 (including zero after wrap), retains the latest keys and merges known access bits for deferred application. Change + 20 s keepalive + join snapshot; joins do not advance the periodic change baseline. Guest applies only with its native wager resolver suppressed, retries deferred bindings, and captures/restores original local keys/access on teardown. No wager, money, stress, speech or save-point actions are replayed. Save-point activation remains host-local under the host-only save rule. Bindings live in catalog `venttiProperty`; the native Ruscko/Satsuma names are preserved. |
| 174 | VenttiTableState | 0 | host -> guests (**v98**), wire order: tableId (uint scene-path hash), sequence (uint), stake (float), playerTotal (int), houseTotal (int), outcome (byte: 0 none/reset, 1 cash win, 2 cash loss, 3 car win, 4 car loss, 5 cabin win, 6 home loss). Stake must be finite and nonnegative; totals must be nonnegative; unknown outcome codes and wrong table ids are rejected before sequencing. No stake or total is clamped to a byte. Outcome observes the native `LoseText.Status`, which can announce a result before settlement; it is never a payment instruction. Nonempty unmapped native status prevents publication and logs a diagnostic. All table variables must bind before publication/application. Change + 20 s keepalive + actual join snapshot; snapshots do not advance the live change baseline. Guest retains the latest complete observation across late bindings and accepts only forward uint deltas in 1..2³¹−1 (including zero after wrap), so stale snapshots cannot roll back live state. Original guest variables are restored at teardown. The native wager resolver must remain disabled before applying stake/totals/status; no result FSM, money, property, save or global interaction UI action is replayed. Bindings and the six native result strings live in `venttiTable`. In v99 this is a fallback observation only until the host ledger is adopted. Publication stops after adoption, and a guest that has accepted 175 ignores 174 for the rest of the session. |
| 175 | VenttiLedgerState | 0 | host -> guests (**v99**). Wire order: tableId (uint), revision (uint), round (uint), playerId (byte, 255 unleased), outcome (byte, same six result slots as 174), phase (byte: 0 betting, 1 playing, 2 resolved, 3 closed), wager (byte: 0 cash, 1 car, 2 house), stake/betMaximum/opponentLoss/pendingCash (four floats), propertyStage/playerTotal/houseTotal (three ints), playerCardCount (byte) + playerCardIds (bytes), houseCardCount (byte) + houseCardIds (bytes). Only revealed cards are sent; the remaining shuffled deck never leaves the host. Card ids 1–52 map through catalog rules, with no repeats across hands, correct totals and legal stop conditions. Decoder bounds each array to 52; state validation bounds each hand to 21 cards and totals to 33. Money fields must be finite; stake/pendingCash nonnegative, betMaximum at least one increment. Property stage is 0–2; wager/phase/outcome/card consistency is required. Stake is already paid escrow while betting/playing and historical after resolution. pendingCash is an owed return, never a new guest credit. Guest accepts only forward uint revision deltas 1..2³¹−1, including wrap, and retains a copied state before scene binding. Changes, 5 s keepalive, and join/resync snapshot; snapshot enumeration does not consume a pending live broadcast. |
| 176 | VenttiRequest | 0 | guest -> host (**v99**). Wire order: tableId/revision (uints), playerId (byte), sequence (ushort), action (byte: 0 increase, 1 decrease, 2 hit/deal, 3 stand, 4 next hand). Session authenticates playerId. Host checks table/action, fresh nearby live-player pose (≤6 m, remote pose ≤2 s), finite wallet/host time, revision and lease. Increase/decrease implement native stepped bets and property conversion; hit deals two player cards and one house card on the first press. One local command remains pending until its matching receipt; retry every second uses the exact same command. The host rate-limits processing per player to 0.1 s and caches the last exact command/receipt. A repeated sequence with different action/revision is stale. Sequence acceptance uses forward ushort deltas 1..32767. Lease expires after 15 s betting/resolved or 300 s playing; timeout/disconnect releases ownership without refunding or redealing the committed hand. |
| 177 | VenttiReceipt | 0 | host -> guests (**v99**). Wire order: tableId/revision (uints), playerId (byte), sequence (ushort), status (byte: 0 accepted, 1 busy, 2 funds, 3 invalid, 4 distant, 5 refresh, 6 stale, 7 finished), cashDelta (float), round (uint), outcome (byte). CashDelta is an audit value only: the shared WalletState is authoritative, and neither a receipt nor its retry moves money again. Rejections have zero cashDelta and outcome. An accepted command that resolves a hand carries its round/outcome; the acting peer applies its own native stress adjustment once when removing the matching pending command. State and wallet precede receipts on channel 0. A refresh may reissue the same action with the latest received revision and a new sequence, at most twice. Old/malformed/unmatched receipts do not settle a pending command. |
| 178 | VenttiSceneState | 1/0 | host -> guests (**v100**). Wire order: tableId/layoutId/sequence (uints), poseCount (byte, 3–56), then each pose: position (three floats), rotation XYZW (four signed int16 components), activeSelf (byte, 0/1). First three poses are world-space NPC/table/chair roots; remaining poses are parent-first local NPC descendants in catalog order. Quaternion components encode clamp(value, −1, 1) × 32767 rounded to nearest, ties to even; decode divides by 32767, with −32768 reserved/invalid. Quaternion squared length must be in [0.999, 1.001]; all components must be finite. Absolute position components are bounded to 100000 for world roots and 1000 for locals. Table/layout/count and pose validity are checked before accepting a forward uint sequence delta 1..2³¹−1, including wrap. Changed poses use channel 1, stationary keepalive every 2 s and join/resync snapshots use channel 0. Snapshot reads do not consume the live change baseline. See reaction integration below. |
| 179 | VenttiSoundCue | 0 | host -> guests (**v100**, live only). Wire order: tableId/layoutId/sequence (uints), sound (byte catalog variation index), world position (three floats), delay (float seconds). Exact selected host variation, never a request to choose a random clip or enter a native FSM. Validate table/layout, index within catalog, finite position components within ±100000 and finite delay 0–5 before accepting a forward uint sequence delta 1..2³¹−1. Guest queue holds at most 32 copied cues, dropping oldest on overflow; each becomes due at local receive time + delay and expires 2 s later. A delayed cue does not block subsequent immediate cues. Removed once for playback; duplicate sequences cannot replay. No historical audio in snapshots; reconnect clears the queue and sequence baseline. |
| 180 | LottoDrawState | 0 | host -> guests (**v102**). Fixed 77 bytes including id: sequence (uint32), round/ticketRound/nationalPot/nationalPotMin/nationalPotFull (five int32), numbers (7 bytes), bonus (3 bytes), prizes (5 int32), winners (5 int32), flags (byte: bit0 native DrawDone, bit1 teletext results visible). Tier order: 7, 6+bonus, 6, 5, 4. Whole completed native draws only; join and FSM-group resync included. No purchase, ticket claim or payout event. See Lotto v102 below. |
| 181 | LottoTicketRequest | 0 | guest -> host. playerId (byte), token (uint64), sequence (uint32), operation (byte: 0 buy, 1 claim), round (int32), lineCount (byte), ticketId (string), numbers (21 bytes, three seven-slot rows). 44 bytes plus UTF-8 ticketId including message id. Buy requires an empty id, 1–3 complete paid rows, zero unused rows and the host's current sales round. Claim requires a known host ticket id and zero round/lineCount/numbers. |
| 182 | LottoTicketReceipt | 0 | host -> peers. playerId (byte), token (uint64), sequence (uint32), result (byte: 0 accepted, 1 invalid, 2 round changed, 3 distant, 4 funds/precision, 5 unavailable, 6 already redeemed, 7 stale), ticketId (string), amount (float32), destination (byte: 0 cash, 1 bank). 23 bytes plus id text including message id. Stale receipts are not transmitted. Only the matching pending local operation consumes a receipt. |
| 183 | LottoTicketState | 0 | host -> guests. sequence (uint32), ticketId (string), round (int32), numbers (21 bytes), winnings (float32), retired (bool), position (vec3), rotation (quat). 66 bytes plus id text including message id. Complete ticket identity/content and initial pose; ongoing motion uses ItemTransform with FNV1a32("lotto:" + ticketId). Includes join and item-group resync; retirement is terminal. |
| 184 | PackageState | 0 | host -> guests (v109): revision (uint32), factoryId (uint32), nativeId (string), quantity (uint16), position (vec3), rotation (quat). Creates/repairs the exact standard box and mirrors remaining quantity; join/item resync and targeted replies included. |
| 185 | ReplacementPartState | 0 | host -> guests: revision:u32, factoryId:u32, nativeId:string, assemblyId:i32, installed:byte 0/1, scalarCount:byte (≤8), native f32 scalars, world position:vec3/rotation:quat; v115 appends parentKind:byte, parentId:u32, parentPath:string, local position:vec3/rotation:quat/scale:vec3; v117 appends removalAllowed:byte 0/1. Creates loose copies or fitted presentation at ready unoccupied mounts; join/item resync and targeted replies included. |
| 186 | PackageOpenRequest | 0 | guest -> host (v112): playerId (byte), token (uint64), sequence (uint32), itemId (uint32), expectedRevision (uint32). One requested native box opening. |
| 187 | PackageOpenReceipt | 0 | host -> guests (v112): playerId (byte), token (uint64), sequence (uint32), itemId (uint32), status (byte), producedItemId (uint32). Exact request outcome; only the matching guest acts on it. |
| 188 | PartFitRequest | 0 | guest -> host (v116; extended v117–v119): playerId (byte), token (uint64), sequence (uint32), itemId (uint32), expectedRevision (uint32), operation (byte: 0 install, 1 remove, 2 rotate increase, 3 rotate decrease), slotIndex (byte). One request to fit, remove or hand-adjust a catalogued replacement. |
| 189 | PartFitReceipt | 0 | host -> guests (v116; extended v117–v119): playerId (byte), token (uint64), sequence (uint32), itemId (uint32), status (byte), operation (byte: 0 install, 1 remove, 2 rotate increase, 3 rotate decrease), slotIndex (byte). Acknowledged outcome; only the matching guest acts on it. |
| 190 | BagState | 0 | host → guests: itemId (uint32), factoryId (uint32), nativeId (string), revision (uint32), remaining (uint16), condition (float32), position (vec3), rotation (quat). |
| 191 | BagOpenRequest | 0 | guest → host: playerId (byte), sequence (uint32), itemId (uint32), expectedRevision (uint32), openAll (bool). |
| 192 | BagOpenReceipt | 0 | host → guests: same fields/order as191, followed by status (byte: 0 Pending, 1 Applied, 2 Stale, 3 Unavailable, 4 Busy, 5 OutOfReach, 6 NotOwner, 7 Failed). |
| 150 | RallyResultsState | 0 | host -> guests (**v73**): seq, timeSS1/2/3 (int stage times), playerTimeTotal (float), playerClassLevel (int), timePenalty (float), flags (bit0 raceOver, bit1 winner, bit2 registered, bit3 secondDay). Host owns the rally results ledger + enroll + parc-fermé penalty; guests apply. In the join snapshot. Reward rides the existing host-gated race price triggers. |
| 151 | JokkisRaceState | 0 | host -> guests (**v74**): seq, laps (int), timeCentiseconds (int), flags (bit0/1 checkpoint 1/2). Host owns the JOKKIS banger race lap/time/checkpoint; guests apply. In the join snapshot. |

VideoPoker uses a host-owned 52-card deck without replacement, including discarded
cards on the second draw. It uses a separate 52-card deck for doubling, also without
replacement within a hand. Clients never send cards or payout amounts. Native resolver
FSMs are paused on both peers; their catalogued textures, materials, card objects,
texts, sounds and physical buttons present the shared state independently of the
host's town LOD. A running native host hand finishes before takeover.

VideoPoker insertion floors `PlayerMoney`, requires strictly more than the current
bet, and checks credit <500 before adding the bet (499+5 is allowed). The bet cycles
1–5, wrapping to 1 when the next bet is unaffordable. A first deal increments the
round and spends credit first, combining winnings for any remainder. Bets are blocked
above 9001 winnings to reserve room for the maximum possible doubled payout. Any
subset of cards may be held, including all five; the second deal resolves the hand.
Categories 0–9 are loss, jacks-or-better pair (including aces), two pairs, three of a
kind, straight, flush, full house, four of a kind, straight flush and royal flush;
installed-build multipliers are 0/1/2/3/5/7/10/15/30/50 times the bet. Both A2345 and
10JQKA are straights. Payouts and achievement thresholds are catalogued.

During a win offer, deal or take-win only transfers the pending win to winnings.
A subsequent take-win while ready pays **both** credit and winnings to shared cash.
Double commits a secret card before the guess: low accepts ranks 1–6, high accepts
8–13, and seven loses either guess. A correct guess doubles the offer; reaching
500 or more automatically collects it. No accepted action can silently clamp away
money, and wallet transfers require finite, sub-cent-conserving float arithmetic.
Session teardown stands on an unsubmitted five-card hand, forfeits a committed double
without a guess, collects any remaining offer and returns both banks once. If cash
cannot represent the refund, the settled banks are restored after the native menu's
reset, including when the town next activates. Native FSMs, input actions and visuals
are restored on teardown. Two-player gameplay verification remains required.

Debt-letter quotes use the host's `Rent/Debt` and the inactive sheet's own `Interest`,
`Cost1` and `Cost2` variables, read through the catalog's `debtLetter` bindings.
The calculation preserves the native single-precision action order: multiply the
principal by Interest, add Cost1, then add Cost2. Build 23268598 defaults are 1.29,
59 and 864; the bank prime rate is unrelated. Display formatting (principal `0`,
total `0.0`) does not round the actual charge. A payment must match the current
quote, find an available envelope and preserve cash to within half a cent. Accepted
payments subtract shared `PlayerMoney`, clear the host's rent debt and hide the
envelope once. Concurrent payments cannot clear or debit the same debt twice.
Bank balance, taxable income and an already completed eviction are unaffected.

Both peers replace the native calculation/request/debit actions while retaining
the local camera, hover and close flow. A confirmed receipt plays the native buy
sound and closes the letter; changed quotes require another press after review.
The envelope follows host availability, independently of the host's open sheet.
Payment binding failures disable the payment button without disabling welfare sync.
Teardown closes an outstanding payment screen before restoring native actions.
Two-player checks must cover concurrent payment, quote changes, relocation,
reconnects and a subsequent singleplayer payment (COVERAGE-ROADMAP R1.6).


`NpcTransform.flags`: bit 0 = **final**, bit 1 = **dead** (**v77**: moose collision→corpse; guests activate their own ragdoll).
Originally bit 0 = **final** (at-rest pose, sent reliable; receiver
restores original kinematic state and sleeps the body). Only the host sends;
guests never relay.

`ItemTransform.flags`: bit 0 = **final** (at-rest pose, sent reliable; receiver
restores physics and sleeps the body), bit 1 = **driver** (sender's player sits
in this vehicle; driver claims beat proximity claims, ties broken by lowest
player id), bit 2 = **vehicle** (stream is a registered vehicle root; sent
reliable), bit 3 = **hasVelocity** (payload appends the sender's rigidbody
velocity as a Vector3; set on moving-vehicle packets — receivers dead-reckon
toward pose + velocity·min(age, 0.3 s) instead of trailing the last pose, and
seed released bodies with it). A seated driver never releases on stillness — it
keeps the vehicle with ~2.5 Hz keepalives until the player leaves the seat, and
receivers block the vehicle's drive trigger while a remote driver holds it.

`VehicleCargo` interplay: while an item is pinned by a live cargo stream, a
world-space `ItemTransform` for it is accepted only from the *same* owner (the
authority handing it off: flung out, grabbed, or settled at rest) — third-party
streams wait until the pin goes stale (1 s without cargo packets). Items held
by the local player are never pinned, and a machine never applies cargo packets
for a vehicle it streams itself.

`TimeSync` semantics: guests jump their sun/cloud hour FSMs only when total
drift exceeds 10 game-minutes; forecast and `DaysPassed` variables are
overwritten on every message (host wins). When `dayOfWeek` changes, guests
broadcast the matching global weekday event (MONDAY…SUNDAY) so TV/HUD/job
schedulers stay aligned.

`ItemSpawn` net IDs are minted by the host as `hash("spawn:" + containerNetId +
":" + epoch + ":" + ordinal)`. For bags, the container ID is the shared persistent
bag item ID; outputs are observed directly at native factory completion before
ordinary scanning. Large spills use multiple ≤32-entry manifests with separate
epochs. Retries retain an epoch; already-bound bodies never receive a second ID.
Native package outputs keep their PackageState identity and are excluded from
these generic manifests. Spawned pickables use ordinary transform/cargo/despawn.

`VehicleState` / `VehicleClimate` sequence (v26): the live stream uses a
per-stream `Sequence` that receivers dedup against (stale/duplicate dropped).
`Sequence == 65535` (`SnapshotSequence`) is reserved as a join/resync sentinel —
receivers apply it *without* dedup, so a freshly joined guest (whose last-seen
sequence is still 0) does not discard the Sequence-0 snapshot of a parked car
and miss its engine/electrics/climate state. The live stream never emits 65535
as a real pose (at worst one un-deduped packet after a full ushort wrap, ~4.5 h).

Per-player dedup latches on the host are dropped when a player (re)handshakes into a
slot (v82+): the remote's counters restart with its session, and a surviving latch
would reject everything the returning player sends as "stale" until it out-counted
its previous life.

### Reserved ranges

Still-unassigned ids inside each themed range (everything else in 1–159 is
assigned in the table above):

- 67–79 vehicles
- 111–119 NPCs/jobs
- 126–139 snapshot/bulk transfer control (save data)
- 144–149 crime
- 152–159 racing
- 160–183 economy overflow is fully allocated; 184 is standard package state (see message table)
- 184+ unallocated

Rules: never reuse a retired id; new fields are appended only together with a
protocol version bump (no silent format drift).

### Rally v101 crossing reliability

The guest's report token is a correlation value, not authentication; session peer
identity still supplies authorization. Host observes registered guests' received
poses no older than 2 s, within 28 m of the requested static marker, with a vehicle
delegated to that driver within 14 m. It remembers each matching player/stage/marker
for at most 3 s from the original pose timestamp. Re-reading the same old pose
cannot extend that window. This permits a brief report/pose ordering delay without
accepting guest-supplied positions or clocks. An already accepted exact retry needs
no fresh proximity, so a missing acknowledgment can recover after the driver leaves.
New reports still need valid evidence; an extended outage cannot establish an
unobserved crossing. The clock begins when the host accepts the start, not from a
guest timestamp.

Catalog `rallyProgress` lists the three stage timing/start paths, ordered checkpoint
names, and native marker commit/completed states. Core validates the corresponding
Timing variables and waits for every marker before observation. The installed build
has 4/6/4 checkpoints for SS1/SS2/SS3. A marker's local `Checkpoint` bool is unused:
its `Set bool` state writes the Timing FSM's named flag and ends in terminal `Idle`.
Core observes that durable terminal state, because Timing clears its flags during
finish. Crossings are processed in number order; an incomplete scan cannot finish a
stage early. A scene rebind within the same connection preserves the report token
and sequence counter. Initial save flags/states do not reconstruct a race that
predates sync. A retained host record can
continue after readmission when native local progress agrees; reconstruction of
native local rally progress from a host record remains outside this adapter.

These records serve rally progress observers. They do not replay vanilla timing,
write prize money/results, or control opponent cars. Stage-aware opponent fleet
synchronization remains R2.10; two-player runtime verification is still pending.

### Ventti v99 host integration

Core adopts only a loaded, idle native manager with zero card stages, an empty
used deck and all 52 undealt native cards. A partly played native hand is never
reconstructed from totals. Both peers cut the four native betting/dealing FSMs'
actions before enabling mod input. The host leaves GameManager load/save handling
active; catalog-checked outcome states retain native keys, doors, cabin access,
dialogue and animations while their wallet/progression/stress actions are cut.
Each resolved round enters its outcome once. A cash return that cannot be added
precisely remains owed; the native terminal animation waits for that credit.
After the native resolver returns idle and a three-second result window expires,
the host resets a paid resolved hand without requiring a second player command.

Guests render the same native card textures (material slot 1, `_MainTex`), retain
state across late binding, and send intents from the native pick targets. Extra
plain meshes extend hands beyond the game's nine visual slots without copying
FSMs or deck objects. Property replication remains 173; guests never enter a
native outcome or activate a save point. Teardown refunds an undealt stake or
stands on the already committed deck, then restores native controls. An unusually
large wallet that cannot represent an exact final return leaves that amount as
already paid native stake, rather than discarding it. Guest variables, materials,
visibility and action flags are restored. The v100 reaction adapter below mirrors
host NPC effects. The two-player save/reconnect/LOD matrix still needs runtime
validation.

### Ventti v100 reaction integration

`venttiTable.reactions` defines the root path, ordered pose paths and ordered
native AudioSource paths. `layoutId` uses `StableHash.Fnv1a32` (UTF-16LE code-unit
bytes) over `"VenttiReaction/v1\n" + rootPath + "\n" + join(poses, "\n") + "\0" +
join(sounds, "\n")`. The current catalog has 46 poses and 21 sound variations.
A pose packet is 15 + 21 × count bytes including message id: 981 bytes currently,
1191 at the 56-pose cap, below the [classic Steam unreliable packet limit](https://partner.steamgames.com/doc/api/ISteamNetworking#EP2PSend)
of 1200 bytes. Sound packets are 31 bytes.

Host samples at 10 Hz when a guest with a fresh pose is within 80 m of a world
root, otherwise 1 Hz. Guest buffers state before binding, pauses native NPC FSM
actions and animations, makes replicated rigidbodies kinematic, and applies poses
in LateUpdate. It interpolates for 100 ms, snapping world moves of at least 25 m
or visibility changes. First received pose applies immediately, including after
late join. Current pose, rather than native outcome replay, also covers a thrown
table/chair and the NPC's subsequent crawl/walk. Original guest transforms,
visibility, physics flags/velocities, animation and action flags return on teardown.

Host sound hooks run immediately before catalog-validated MasterAudio actions,
after their native ArrayList variation choice. Guest binds the corresponding
AudioSource by full native path, including inactive sources, and plays its clip
from a temporary source at the received origin. Binding retries retain only
unexpired cues; playback skips elapsed audio and distant/missing listeners.
The native delay is relative to receipt, with no cross-peer clock correction.
Missing or changed audio bindings do not disable poses or betting. Native group
mixing and audiovisual timing still require in-game comparison. No reaction
packet writes money, keys, save points, stress, progression or native FSM events.

### Lotto v102 complete native draws

`LottoDrawState` is one global host-owned state for `Systems/Lottery::Numbers`.
The catalog fixes the scalar/list slot meanings and maps their native names.
Main and bonus numbers are each strictly ascending in 1..39, with no duplicates
across either list. All rounds, pots, prizes and winner counts must be nonnegative
int32; no float/ushort narrowing is permitted. Unknown flag bits and malformed
array shapes are rejected. Zero winner counts do not imply zero prizes: the
native calculation remains the authority. DrawDone is sampled data, **not** a
completion flag (vanilla sets it before drawing).

The host samples only after the native `Reset points`, `Day`, `Time` or save
terminal `State 35` is reached, with all four live ArrayLists at their full sizes
and the five winner variables matching the winner-count list. Intermediate RNG
and prize calculations are never published. Broadcasts compare all fields/list
slots every two seconds, with a thirty-second keepalive. Join/resync snapshots
use fresh sequence numbers without consuming an existing peer's change edge;
if the host is still calculating, the next completed broadcast supplies the draw.

Guests accept only the selected authenticated host. A forward uint sequence
delta in 1..2³¹−1 replaces a copied pending draw; invalid, duplicate, stale and
half-range updates do not consume that baseline. Pending state survives missing
scene bindings. Once native save loading/calculation has reached a stable state,
the guest preserves its local arrays/scalars/display visibility and pauses the
Numbers FSM with restart-on-enable disabled, even while waiting for host data.
Applying a snapshot updates the live ArrayList instances (numbers remain boxed
int32), scalar variables and DrawDone without sending `CHECKLOTTERY`, `RESULTS`
or `LOTTODRAW`. Teletext reads once on enable, so changed data refreshes its
Texts subtree after the whole draw is written; visibility follows the host,
including when the paused guest cannot exit `Reset points` to reveal it.
Teardown restores local data before resuming the native FSM. A subsystem error
retains suppression until teardown instead of running a partially applied draw.

This message does **not** establish shared ticket identity, purchases, claim
validation or exactly-once payouts. Megaveto uses hockey results and is separate.
Two-player native draw/display/save-restoration checks remain required.


### Standard package creation, quantities and disposal v109

PackageState (184), host -> guests on reliable ordered channel 0:

| Field | Wire type | Meaning |
|---|---|---|
| Revision | uint32 | Per-box content revision, compared with unsigned wraparound |
| FactoryId | uint32 | FNV1a32(factoryPath + "::" + factoryFsm) |
| NativeId | string | Exact native Use.ID, including its positive counter |
| Quantity | uint16 | Remaining parts, 0 through the cataloged capacity |
| Position | vec3 | Current host pose for creating a missing box |
| Rotation | quat | Finite approximately unit rotation |

The encoded message is 42 bytes plus UTF-8 NativeId bytes, including the message ID.
All 30 standard boxes under `Spawner/CreatePartsPackages` are cataloged. Capacities
are 4 for pistons, 5 for main bearings, 8 for rockers, and 1 for the other 27 types.
The stable item ID is `FNV1a32("factory:" + factoryIdDecimal + ":" + nativeId)`;
ItemTransform (42), item snapshots (122), cargo and removals use this same ID.
ItemSpawn's factory flag remains limited to trophies.

The counter suffix is a canonical positive Int32. `boxalternator01` is prefix
`boxalternator0` plus counter 1; timing belts use prefix `boxtimingbelt`. Live native
binding requires both Use.ID and the exact CreateItemsDB contents-factory path.
Unknown kinds, invalid counters, conflicting contents references, invalid quantities,
non-finite poses and identity collisions are rejected. Generic spill capture,
adoption and template cloning exclude structurally recognized boxes, whose common
`package(Clone)` / `empty(itemx)` names cannot identify their contents.

The host captures each factory's exact New output after native name assignment,
waits for Use initialization/load, and also discovers saved boxes. New boxes and
changed quantities are broadcast; joining, item resync and targeted object replies
include PackageState with a fresh creation pose. Snapshot observation never advances
the broadcast baseline owed to existing peers. Guests retain copied validated states
until the catalog factory is ready. Older revisions and equal revisions with changed
quantities are rejected; equal-revision replays may refresh the pose for repairing a
missing replica. Quantity updates do not teleport an existing body. Item snapshots
and transform streams continue to move existing boxes.

Guests pause each native box factory at idle after its saved-object load. Local
saved/generated boxes are hidden and preserved separately, including those sharing
the host's ID; a snapshot never overwrites their transforms. Replicas use the exact
cataloged prefab with its Use disabled before cloning, skip native load, and initialize
owner/ID/capacity/quantity before starting normal interaction. Their save/delete
callbacks cannot write guest saves. Since v112, attempted opening queues a host
request without decrementing quantity or invoking a guest native part spawner. Disconnect destroys replicas and restores original boxes
and factory FSMs without re-running their native load.

Accepted ItemDespawn/removal snapshots remain terminal for the session, including
before creation. Host disposal runs native GARBAGE, clears Quantity and removes
physical components while retaining Use for SAVEGAME -> Quantity <= 0 -> Delete(ID).
Guest disposal removes only its replica. Empty boxes remain physical until disposal.
Existing ownership/proximity gates and optimistic guest removal behavior remain.

Complete installation/bolt/save replication and native two-player/save verification
remain unfinished. v110 supplies part identities, v111 supplies loose boxed-content
creation, and v112 supplies guest opening intents. BrakeBiasRegulator is a direct part and Plugwires has null factory
references in this build; neither is one of these 30 boxes.

### Acknowledged guest box opening v112

PackageOpenRequest (186), 23 bytes including the message ID:

| Field | Wire type | Meaning |
|---|---|---|
| PlayerId | byte | Must match the authenticated guest, excluding 0 and 255 |
| Token | uint64 | Nonzero guest-generated token, renewed when its session state resets |
| Sequence | uint32 | Monotonic request sequence within that token |
| ItemId | uint32 | Stable standard-package body ID |
| ExpectedRevision | uint32 | Last host PackageState revision observed by the requester |

PackageOpenReceipt (187), 24 bytes including the message ID: PlayerId (byte), Token
(uint64), Sequence (uint32), ItemId (uint32), Status (byte), ProducedItemId (uint32).
Status is strictly 0 Pending, 1 Accepted, 2 Busy, 3 Unavailable, 4 Stale, 5 Empty,
6 TooFar, or 7 Failed. ProducedItemId names the confirmed native output on acceptance
and is zero otherwise. Receipt identity is compared by player/token/sequence/box ID.

The guest permits one outstanding request and retries its **unchanged** fields every
0.5 seconds until a matching terminal receipt arrives. Pending and Busy are
nonterminal. Quantity and native counters are never changed optimistically. The
host remembers one operation/outcome per guest. Duplicate exact requests return its
current outcome; same-sequence changed payloads, foreign tokens and stale sequences
cannot execute. A new sequence cannot replace an in-flight operation. Unsigned
sequence differences above Int32.MaxValue are stale; wraparound is supported.
Busy before reservation does not permanently consume the request sequence. Admission
forgets the old player's ledger; old receipts cannot clear a new client's token.

Before reserving an opening the host requires: a known live, nonretired box, matching
revision, positive quantity, a living authenticated guest with a pose no older than
two seconds within three meters, no different guest owning the item stream, and
ready native box/contents FSMs. The exact contents factory must be registered and
produce one part: the piston/bearing/rocker NumberOfProducts variables currently equal
**1** (their serialized action operands contain stale default 4 values and must not
be used as the live count). Other factories return directly to Idle. Counter overflow
and unsupported references fail closed. Guests also wait for their contents adapter.

One opening is reserved at a time. A head guard also controls native host clicks,
redirecting blocked interactions before any native actions run. On shipped PlayMaker
1.7.7.6, a self-event queues a state change and ActivateActions stops immediately.
The accepting operation uses the original Create Plug actions, including the Quantity
-1, PartSpawnPoint assignment, MinimumWear write and SPAWNITEM. The factory tail
captures the exact next prefix/counter identity; the box tail records its resulting
quantity. Acceptance waits for one decrement and one matching, initialized native
part. Partial/unconfirmed failures are terminal for that request and disable that
box's opening adapter; they are never automatically retried as another creation.

Capturing the quantity at the tail permits later legitimate disposal of the box.
If the created part was already consumed, confirmation publishes its retirement
instead of creating it again. Replies send fresh box and accepted output state (or
retirement) before the receipt. Retried accepted requests therefore repair a missing
copy while respecting terminal removals. Snapshot reads preserve delta publication
baselines owed to other guests. Only the requester plays native opening audio, once
on an accepted receipt; nonterminal retries do not play it. Host native audio remains.

Disconnect clears the pending client and ledger and removes only owned box hooks.
No opening is replayed as part of cleanup. This enables unpacking and loose-item
carrying; it does not enable the still-deferred full fitted-part/bolt graph. Native
two-player, save/reload and simultaneous host/guest interaction tests are pending.

### Guest replacement fitting/removal/adjustment v116–v119

PartFitRequest (188) is 25 bytes including its uint16 message ID; PartFitReceipt
(189) is 22 bytes. Field order is exactly the table above. Both use reliable ordered
channel 0. Requests contain no arbitrary mount path, pose, assembly state or condition.
SlotIndex is appended after Operation: zero for fixed-mount installation and all
removal/rotation requests, 1–32 for catalog array installation. Framing rejects larger values
and any nonzero removal/rotation slot. Application requires the family's actual slot count
and the host's nearest slot to match. Current arrays have 4, 5 and 8 slots.
Receipt statuses are Pending=0, Accepted=1, Busy=2, Unavailable=3, Stale=4,
NotLoose=5, TooFar=6, Blocked=7, Failed=8, NotFitted=9 and Bolted=10. Status bytes
above 10 and operation bytes other than Install=0/Remove=1/RotateIncrease=2/RotateDecrease=3 are invalid framing.

Rotation operations are admitted only for a catalogued `handRotation` binding.
The requested revision must still describe the same fitted part; its Data,
InstallPoint, actual parent and mount Part/Mpoint/Installed references must agree.
The host requires a fresh living guest within 3 m, an enabled native HandRotate
and pick collider, and adjusting-bolt tightness 0–7. Native Wait/turn states and
the 0.1-second cooldown are busy. An accepted request enters one native Clockwise
or Counterwise state; both part and mount SettingRotation and the pivot pose must
settle at the expected half-degree step (clamped to 0–7). No guest supplies an
absolute angle or changes mount references. A turn at a limit is Blocked; a
tightened adjusting bolt is Bolted. State 185 precedes the final receipt. The
guest's original HandRotate stays disabled, and its pose uses absolute host state.

The claimed player must match the authenticated sender and token must be nonzero.
Each admission retains one immutable request/outcome per player. A uint32 sequence
advances only when its unsigned difference is in 1..Int32.MaxValue. Equal sequence
retries must match token, part, observed revision, operation and slot. They return the cached pending
or final result without entering native fitting/removal again. A later request cannot replace
an unfinished operation. Terminal denials, including Busy, require a new click;
they are not automatically retried as future operations. The guest retries its
exact pending request every 0.5 seconds until a matching terminal receipt. Admission
and session cleanup reset the ledger; old tokens/receipts cannot complete new work.
Receipt matching includes the operation and slot. Installation and removal share one host
in-flight operation, and one client pending request.

Installation admission requires a tracked, active native replacement with the exact observed
revision, AssemblyID=0, and ready native Data/Stop and mount/Idle states. The requesting
living player's latest pose must be no older than 2 seconds and within 3 m of both
part and mount. Item authority must belong to that guest, or have been released by
that same guest within 0.5 seconds with no subsequent owner. A host-owned or other
guest-owned part is unavailable to the operation. Part-to-mount distance must be
strictly below the live native PartsAssemblyTolerance (finite, positive, at most 1 m).

For the 27 fixed-mount replacement families, the host verifies the factory-supplied
InstallPoint. It sends the native ASSEMBLING event, which selects
ActivePart and traverses Allow install?/Far/Near; only an unchanged, nearby, still
authorized candidate at Near receives PROCEED from its entry guard, in the same
frame as the prerequisite checks. Delayed Near entry cancels the preview instead
of confirming old prerequisite results. Native installation owns assembly
IDs, prerequisite effects, mass, Rigidbody removal and reparenting. One remote
fitting may be in progress at a time. The host waits up to 3 seconds for a fitted
state whose native mount and actual parent agree. It publishes state 185 before the
receipt. Failure after dispatch never automatically repeats installation, and an
unsettled commit disables fitting for that part for the session. Cancellation only
backs out this candidate's Far/Near preview, never another part or an installed state.

v118 adds the remaining three families using their native AssemblyDatabase arrays:
VIN103/Pistons (4), VIN104/MainBearings (5) and VIN117/Rockers (8). Each catalog rule
binds ArrayReference and the exact slot count. The adapter resolves the live native
array with a null index-zero sentinel and unique GameObject references. It preserves
holes and includes occupied/inactive slots in distance selection, matching the
native closest-object action; equal distances choose the later index. It never
skips an occupied nearest slot to choose a farther free one. Invalid geometry or
tolerance, missing/inactive selected mounts and unavailable arrays defer/reject.
The guest's normal held-part fitting click includes this selected slot. If the
host selects another slot, the request receives Stale and requires a new click.
These three native Data templates lack Installed; creation and initialization no
longer require that fixed-part scratch flag. AssemblyID remains the wire authority.

Slot admission also requires an idle shared Installer with cleared ActivePart,
AssemblyPoint and Index, plus a ready idle mount and loose Data/Stop. The native
part's ASSEMBLING → Far entry sets Installer.ActivePart and Reference and sends
CHECK. Before any Installer/Near action, a temporary guard rechecks the requested
slot, array reference, native selected index/point, current part revision, authority,
fresh player proximity and free mount. Native Near assigns the mount's AssemblyID
and AllowInstall and sends its CHECK. The mount's native prerequisites must reach
Near in that same frame; the existing fitting guard confirms only the same part,
slot and AllowInstall result. Native Install 1 reads the selected ActivePart,
sets its InstallPoint and AssemblyID, stops the Installer and sends INSTALL.
Native engine/mass/body/reparenting actions remain authoritative.

An unconfirmed slot selection cancels before the initiating call returns, clearing
only its preview's AllowInstall and backing out that mount before stopping the
still-owned Installer. It cannot wait for a later host mouse click. Once committed,
the operation is observed for up to 3 seconds; acceptance requires the requested
AssemblyID and actual InstallPoint, plus the existing settled attachment proof.
Failed committed operations are never reexecuted. Cleanup removes the selection
and mount guards. All 17 array mounts were inspected in build 23268598; runtime
two-player, slot movement, competing input and save/reload checks remain pending.

Held items keep their transform ownership alive even when stationary. Guest-created
loose copies display a left-click prompt while held at a free known
mount. Their installation, bolt and save actions remain disabled; the host's state
185 updates their presentation. The normal pickup click may release the body first.
Occupied guest-save mounts still defer presentation. Original guest-save isolation
and operational bolt/engine graphs for
these copies remain unfinished. Existing native guest parts retain their legacy
adapter, but their generic replacement-part install/remove states cannot bypass
the new host gate. These paths require native two-player/save verification.

For removal, the host resolves the part's current Data.InstallPoint, including
dynamic piston/bearing/rocker mounts; it does not use the factory's installation
default. Admission requires the exact observed revision, positive AssemblyID,
a validated attachment, finite native Tightness in [0, 1), no remaining Rigidbody,
and active, started part/mount FSMs. ActivePart, Installed and AssemblyPoint must
agree with the part and its actual parent. The native root BoxCollider must be
enabled and a trigger, with the part on layer 19 and untagged, and Data must be in
Mouse off/Mouse over. These checks preserve native blocker/collider gating.
The living guest's pose must be fresh within 2 seconds and within 3 m of both
part and mount. Fitted parts have no loose-item ownership requirement.

The published RemovalAllowed flag reflects this native readiness. The host checks
it again, together with revision and proximity, at a temporary guard before any
native Data/Remove action. The validated entry writes this part as ActivePart and
sends REMOVE to that mount's native Allow removal? flow. Native actions own mass,
dependent removals, UNINSTALL, assembly identity, body creation and detachment.
The host observes for at most 3 seconds and accepts only a loose zero-tightness
state with a new Rigidbody, Data/Stop, and no continued occupancy of the old mount
by this part. State 185 precedes the receipt. An unsettled commit disables further
removal for that part for the session without repeating the native operation.
Cancellation before commit derives fitted interaction through BOLTING only while
the part still owns that mount; otherwise it stops this Data entry without writing
the mount. Cleanup removes the temporary guard.

Guest-created fitted copies offer a right-click removal prompt only with the native
hand in PickUp/Look for object. Selection analytically intersects their existing,
disabled native root BoxCollider with the camera ray, using the native 1 m range
and layer-19 physics occlusion. A nearer fitted copy also occludes a farther copy.
Colliders remain disabled and no guest assembly or save action runs. Host loose
state restores normal item tracking and carrying. Native bindings were checked
for all 30 replacement families; gameplay and save verification remain pending.

### Replacement-part creation v111 and fitted presentation v115

`replacementParts` maps the 30 standard box-content factories to their exact native
prefab/save prefix, ordered Data save-float variables and factory-to-Data object
references (InstallPoint and optional PartBlocking). Factory ID is the usual
`FNV1a32(path + "::" + fsm)`; body ID remains v110's `FNV1a32("part:" + nativeId)`.
The native counter is canonical Int32 decimal, including zero for original parts.
A packet cannot choose an arbitrary prefab, state name or variable name.

ReplacementPartState (185) fields, in order:

| Field | Encoding |
|---|---|
| Revision, FactoryId | uint32 each |
| NativeId | uint16 UTF-8 byte length + bytes |
| AssemblyId | nonnegative int32 |
| Installed | byte, strictly 0 or 1; since v113 must equal AssemblyId > 0 |
| ScalarCount | byte, 0–8 on framing; exact catalog count required before application |
| Scalars | ScalarCount float32 values, in factory catalog order |
| Position, Rotation | vec3 + quaternion |
| ParentKind | v115 byte: 0 none/unresolved, 1 native part, 2 vehicle |
| ParentId | v115 uint32: persistent native part body ID or vehicle ID |
| ParentPath | v115 uint16 UTF-8 byte length + relative transform path, ≤512 bytes |
| LocalPosition, LocalRotation, LocalScale | v115 vec3 + quaternion + vec3 relative to that parent transform |
| RemovalAllowed | v117 byte, strictly 0 or 1; host native removal readiness |

The entire v115 prefix is preserved. Including the uint16 message ID, v117 size is
`94 + UTF8(NativeId).Length + UTF8(ParentPath).Length + 4*ScalarCount` bytes. This
message uses reliable ordered channel 0 for live publication, join and object resync.
Scalars/positions must be finite and quaternion squared norms between 0.9 and
1.1. Receipts own copies of arrays. Unsigned revision difference above Int32.MaxValue
is stale; equal revisions may refresh the world creation pose but cannot change
assembly, scalars, attachment or RemovalAllowed. A true RemovalAllowed requires a
fitted part, valid attachment and catalog Tightness in [0, 1); contradictory states
are rejected. Parent/path/local pose/scale and RemovalAllowed changes advance the
revision; motion of the parent through the world does not. Snapshot observation
never acknowledges a broadcast owed to other guests.
Body replacement also advances the revision, including a fit/remove cycle between
polls whose final saved values match. This does not reset native identity.
Session retirement rejects pending materialization and all later replays.

Every native Create product/Create tail captures its own New/ID; it does not wait
for factory Idle, which would lose earlier products in a multi-output loop. Binding
waits for the part's completed native save identity. Guest factories pause at Idle
after saved loading, and pending local outputs settle before materialization. Native
parts already present keep their existing adapter and are never replaced by this one.
Discovery and factory-output tracking retain the persistent Data FSM, including
saved fitted parts whose Rigidbody has already been destroyed by their mount.

Native lifetime uses Data existence, Consumed, AssemblyID and presence of the current
Rigidbody. Positive AssemblyID is fitted with or without a body; AssemblyID=0 with no
body is a transition, not disposal. Only destruction of Data or Consumed retires the
part. When removal supplies a new Rigidbody, loose-item tracking binds that body and
forgets the previous physics ownership/cargo state. Fitted/transitioning parts reject
item transforms, cargo pins and loose-item pose snapshots; fitted replacement state
continues publishing from Data's transform. The host's observed assembly phase,
rather than the native Data.Installed scratch variable, supplies the wire flag.

A missing loose part requires AssemblyId=0, Installed=false and native Tightness=0.
A missing fitted part requires a validated attachment and a ready parent. The exact
prefab's FSMs are disabled before Instantiate. The guest then
runs native identity/presentation initialization with host saved floats, replacing
the save-existence test and suppressing load/save/delete and assembly side effects.
Child assembly/bolt FSMs remain disabled and generic FSM registration excludes the
replica. Normal loose-item ownership, transforms and carrying use its stable body ID.
No change is made to the guest's saved same-ID object or factory counter.

The host resolves Data.InstallPoint to its native mount Data, requiring ActivePart
to reference the same part, Installed=true and AssemblyPoint to match the actual
parent transform. This avoids capturing the intermediate fitting frame before
native reparenting. The nearest enclosing native part or tracked vehicle supplies
ParentId; ParentPath uses the same indexed sibling segments as native FSM IDs.
The guest resolves within that root only: no absolute paths, traversal, backslashes,
colons or control characters. Ambiguous sibling paths do not bind. Unknown parent
kinds, self-parenting and known attachment cycles are rejected before replacing
accepted state. Local scale must be finite and strictly positive. None/unresolved
requires zero ParentId, empty path, zero local position, identity local rotation and
unit scale; a loose part cannot carry an attachment.

Ready fitted copies are parented at the host's local pose/scale, become untagged,
kinematic and non-colliding, and leave item/cargo authority. Only contained native
SetRotation presentation actions are replayed for saved adjustments (the two
alternator pivots in this build). No mount Installed/ActivePart/physics/engine values
are assigned and no native INSTALL is executed on the guest. This is fitted
presentation, not an operational reconstructed engine or guest installation.

An unresolved, inactive, missing or occupied mount stays pending; an existing copy
is detached, hidden and removed from item authority. A different fitted guest-save
part under the same parent is preserved rather than overlaid. Parent availability
is rechecked, including after local hierarchy loss. A later loose result restores
the copy's original scale/tag/collider settings, loose body tracking and current host
world pose without an old ownership/cargo lease. Retiring a parent first detaches
owned child copies; retiring a copy remains terminal. Disconnect detaches owned
copies before deleting them, leaving native parent objects intact.

Host garbage keeps Data alive and sets Consumed for native SAVEGAME deletion. Guest
replica garbage sends an ordinary ItemDespawn request, retaining its body until the
host accepts. The host checks existing ownership/fresh proximity plus a current loose
replacement state, applies native GARBAGE, and echoes accepted removal to **all** peers,
including the requester. Existing optimistic item adapters tolerate that idempotent
echo. Rejected requests leave the replacement intact. Disconnect removes owned hooks,
destroys temporary copies and restores factory FSMs without reloading native saves.

Operational guest mount references, original guest-save isolation, fitted bolts,
other non-box part creation, and native
two-player/save tests remain open. Guest opening is implemented in v112; v114
restores individual bolt arrays for native parts already present on both peers.

### Native bolt authority and reconciliation v114

Post-0.1.32 implementation note (still protocol 118): owned replacement copies use
these same 41/44/123 messages and stable part/child IDs for spanner/ratchet input.
They do not predict native turns or run parent/engine actions. Their presentation
arrays and poses change only from host absolute replies. Tool picks wait for a fresh
host observation after an attachment change; older queued observations are discarded
and the existing targeted object-state request obtains a current bolt reply. Receipt
ordering also includes accepted replacement-part revisions (185), preventing a later
application of an older replacement scalar from rolling back a bolt's parent total.
No fields, message IDs or wire authority semantics change in this implementation.

BoltState (44) preserves its eight-byte legacy payload and appends the parent
Data.Tightness float; including the message ID it is 14 bytes. WorldBoltSnapshot
(123) preserves its count and complete legacy entry block, then appends one float
per entry in entry order: `4 + 12 * count` bytes including the message ID, maximum
964 bytes for 80 entries. Both use reliable ordered channel 0. ScrewInt is reserved
zero; it is never applied as a relative direction. Tightness must be 0–8 and the
parent total finite. Duplicate IDs or any invalid entry reject an entire chunk
before writes. No retired message IDs are reused.

Authenticated guest turns (41) require a living player's pose at most two seconds
old, within three metres of a ready fitted bolt. The host executes the native
TIGHTEN/UNTIGHTEN action chain immediately and coalesces settled observations for
broadcast to all guests. An unready intent is discarded, not saved for a later
installation. Guest BoltState messages request the current host result and cannot
assign tightness, parent totals or turn direction. Host raw events are not sent to
guests, so a predicted turn is never incremented a second time by an echo.

On receipt, the adapter writes the native `Bolts[Index]` integer array and
BoltTightness, restores TightnessF using the divisor read from Calc pos (normally
−400), and assigns the absolute parent total. It enters Set pos and, for ordinary
bolts, invokes the native Data.BOLTING check to update mount tightness, Bolted and
collider effects. This never replays Screw or adds to the aggregate. Fully loose
zeros are real live/snapshot/targeted records. Applying an identical result does
not replay effects. Alternator adjustment bolts have no parent increment; the
clutch-plate variant has no divide/position action, so its divisor is one.

Unready entries retain complete values and retry; a newer successful result clears
an older pending result for that bolt. A session-local receipt order shared with
PartState/WorldPartSnapshot prevents an older delayed sibling bolt or part scalar
from rolling back a newer parent total. Receipt order is not an added wire field.
Pending values and receipt history clear at teardown. The world checksum includes
bolt ID, tightness and parent total rounded to 0.001; last turn direction is excluded.

Bindings validate the native action chain and live array reference before enabling
each bolt adapter. Build 23268598 has two at-limit ADJUST routes (crank pulley and
camshaft sprocket): normal turns remain supported, but extra guest tightening at
eight is stopped before the mount timing action, with the host also rejecting it.
Engine timing replication remains separate unfinished work. The MUDFLAPa0 prefab
has an unresolved ThisPart reference; its adapter fails closed if that reference is
still absent at runtime. Continuous VIN106 drain and VIN209 alignment controls do
not match this integer-step adapter and remain separate work. This does not create missing fitted guest graphs or isolate
pre-existing guest saves. Native two-player, LOD and save verification remain open.

### Persistent native part identities v110

A native assembly part is identified by its cataloged Data/ID and matching save
keys, not its display name or current position. The `partIdentity` bindings require
AssemblyID and Consumed variables, UTAssemblyID = ID + AID and UTPos = ID + POS.
Since v114 the optional Installed scratch bool is not an identity requirement;
this covers all 192 native Data prefabs, including 36 previously excluded parts.
Registration defers until native initialization/load has finished. IDs are opaque
ASCII alphanumeric save identifiers, starting with a letter and ending in a digit,
limited to 128 characters. Counter zero is valid for original parts. The whole
`ALTERNATOR01` is preserved; its factory prefix contains a zero. Missing proof,
changed IDs and duplicate live owners cannot fall back to position ordinals.

- Part body ID: `FNV1a32("part:" + nativeId)`.
- Part FSM ID: `FNV1a32("part-fsm:" + itemIdDecimal + ":" + relativePath + "::" + fsmName)`.
- `relativePath` excludes the native part root and all its ancestors. For the root
  Data FSM it is empty. For a child screw it may be `Bolts/BoltPM[1]`; normal sibling
  suffixes distinguish identical bolt names within one prefab. Nested part Data
  roots establish their own identity before looking at an enclosing assembly.

PartState (45), WorldPartSnapshot (124), generic FSM state/raw events (40/41),
BoltState (44), bolt snapshots (123), and registered child controls use these FSM
IDs. ItemTransform (42), item snapshots (122), cargo and removal use the body ID.
Non-part objects retain existing identity rules. Grocery spill capture, adoption,
stale-clone reuse and template cloning exclude native part graphs, including
uninitialized prefabs. Their native save/installation graphs need a dedicated
contents adapter and cannot be recreated by common display name.

The generic FSM registry now removes its own callbacks and registration marks on
session teardown. A failed partial registration is cleaned before retry. Native and
other-subsystem actions remain in place; reconnect can register parts, bolts and
other generic FSMs again without abandoned callbacks sending duplicate events.

This corrects routing for parts present on both peers. It does not materialize
missing fitted parts, synchronize complete installation references, or isolate
the guest's entire native part save graph. v111 adds loose creation, v112 guest box
opening, and v114 individual bolt arrays on existing native parts. Complete guest
assembly and native disposal/reconnect tests remain pending.

### Native car-part scalars v107

Part `Data.Wear` uses native condition values, often about 0–100: the installed
VIN133 alternator prefab initializes it with RandomFloat(90, 99). Treating that
value as 0–1 encoded every healthy part as 255 and then wrote **1** on the receiver.
The same saturation hid real wear differences from the world checksum.

PartState (45) retains `netId:u32, flags:u8, tightness:u8, wear:u8`, then appends
`tightnessValue:f32, wearValue:f32`. The legacy hints preserve their old encoding
but have no authority. WorldPartSnapshot (124) retains its count and complete
seven-byte legacy entry block, then appends two floats per entry in entry order.
Including the two-byte message ID, a PartState is 17 bytes and a full 80-entry
snapshot is 1,204 bytes. Both use reliable channel 0. No new IDs are allocated.

Native floats are applied directly, with no unit or percentage conversion. Only
finite values and known flags (bit 0 installed) are admitted; finite native values
slightly past thresholds are preserved. Snapshot IDs must be unique, and an invalid
entry rejects the whole snapshot before any scalar writes or pending replacement.
Zero wear/tightness and uninstalled parts are included, so resync can clear stale
nonzero guest state. Pending values retain full floats and retry after a registered
FSM becomes available. A newer successful application clears older pending data.
An object-state request for a part returns the known FSM state followed by scalars;
FSM state alone cannot restore wear.

A guest PartState is now an observation request for a nearby cataloged part with a
fresh player pose, not authority to assign Installed/Tightness/Wear. Existing native
install/bolt events execute on the host. Reports coalesce by part ID, and the host
samples after processing pending interaction events, then sends its current state
to **all** guests, including the sender. Local host settle hooks use the same deferred
sampling. Neither a guest's random initial wear nor its reported wear/tightness can
rewrite the host. Fitted-engine wear continues through the separately validated
vehicle damage authority path. Pending reports clear on session teardown.

The world checksum hashes native part flags, tightness and wear rounded to 0.001
for comparison only; wire/apply remains full precision. Signed zero is canonical.
This detects 95 versus 99 wear without treating tiny float noise as a desync.
Native two-player bolt/install/load/repair/save verification remains required.
This correction is a prerequisite for package contents; it does not add package
creation, opening or persistent car-part identity replication.

### Native trophy factory manifests v106

`ItemSpawn` (52, reliable ordered channel 0) gains `FlagFactory = 2`. No fields
are added. When set, `containerNetId` is FNV-1a32 of `factoryPath + "::" + fsmName`
from the matching catalog `trophyFactories` entry. Each entry's `templateName`
contains its **native persistent ID**, not a display name or fuzzy template hint.
`netId` is FNV-1a32 of `"factory:" + invariantDecimal(containerNetId) + ":" + nativeId`.
The native ID must be the catalog prefix followed by a canonical positive Int32
counter (no sign, padding, whitespace or non-ASCII digits). Gold, silver and bronze
awards from different race classes remain distinct despite shared visible names.
`stateName` is the catalog creation state; `offerSeq` must be zero and the manifest
must contain 1–32 entries. Guests reject unknown factories, incorrect prefixes or
hashes, and the ordinary malformed pose/flag/count checks before accepting a receipt.
Factory manifests are host output only; `SpawnIntent` cannot request a native award.

The host observes the factory's direct `New` output after its five creation/name
actions finish, waits for native initialization, and binds its persistent identity
to ordinary item movement. Existing saved trophies are discovered by native `Use.ID`,
so snapshots include awards loaded before this session. Live manifests use a minted
factory epoch; join/resync chunks set both factory and replay flags and epoch zero.
Multiple replay chunks can share that key. Factory receipts deduplicate by live
item identity, not epoch, so a wrapping epoch cannot drop a new native item;
missing live replicas can recover, and session removals always take precedence.

Guests wait for their local factory to finish loading and pause it at idle. They
retain descriptors until the matching factory binds, instantiate its exact prefab
with the persistence-only `Use` FSM disabled before cloning, and set display name,
scale, pose and normal item authority explicitly. This also covers the ice-race/rally
prefabs whose native initialization has no initial wait. The prefab component's
original enabled value is restored immediately after the synchronous copy. Guests
hide and pause their own saved trophies without rebinding them to host IDs; teardown
destroys replicas, restores those saved objects/FSMs and removes factory hooks.

This covers the 15 trophy factories under Amateur, Junior, Icerace, RallyAMA and
RallyJR. It does not add missing race outcome authority or support moose meat,
parts packages or spray cans. Those need separate contents/condition/save adapters.
Native two-player creation, save isolation, recovery and reconnect tests remain open.

### Shared-item recovery v105

No fields or IDs are added. On item-group resync, the host sends item poses,
session removal chunks (125), then refreshed `ItemSpawn` replays (52, flag bit 0).
It excludes retired IDs and destroyed bodies from poses/manifests. Both ordinary
ItemDespawn and removal snapshots establish terminal IDs on guests even if the
body has not been created. Deferred creation checks retirement at execution time;
old poses/replays cannot revive an ID. Lifecycle state clears on disconnect or
scene teardown; rejected guest removal requests do not establish host retirement.

Original bag-spill manifests remain deduplicated by container/epoch. Replays can repeat that
key: existing live bodies remain untouched, missing bodies may be adopted/created,
and destroyed registrations are discarded before recreation. Repeated pending
replays coalesce without extending the materialization deadline. A failed template
lookup can retry on the next item resync. This reuses the existing template lookup;
it does not add new game-event spawner coverage.

Before consuming a manifest key, guests validate flags, the existing 32-entry cap,
unique entry IDs, nonempty template names up to 128 characters without controls,
state name up to 128 characters, finite positions/quaternions and quaternion squared
norm within [0.9,1.1]. Materialization normalizes accepted quaternions. Empty
acknowledgments for unsuccessful guest offers remain valid.

### Hockey v104 complete betting board

Message 160 retains the v88 prefix in order: `sequence:u16`, `latestRound:i32`,
`gameIndex:i32`, `team1Id:i32`, `team2Id:i32`, `team1Odds:f32`, `team2Odds:f32`,
`tieOdds:f32`, `result:string`, `flags:u8` (only bit 0, native KurPaWins).
The team IDs, scalar odds, result string and cursor describe temporary native FSM
variables, not six games. Guests do not overwrite that paused calculation cursor.
`LatestRound` can be zero after loading; it is not a completion signal.

v104 appends, in this order, with no array counts:

| Field | Wire shape | Native source / order |
|---|---|---|
| gamesPlayed | i32 | Runkosarja GamesPlayed |
| pairs | 12 u8 | Runkosarja PairsNew, adjacent home/away IDs |
| previousPairs | 12 u8 | Runkosarja Pairs |
| odds | 18 f32 | Betting Hashtables 0–5, each in key order `1`, `X`, `2` |
| results | 6 u8 | Betting ResultsGame, ASCII `1`, `X`, or `2` |
| resultOdds | 6 f32 | Betting ResultsOdds for the previous results, independent of new odds |
| scores | 6 strings | Runkosarja ResultsPair, at most 16 characters each |
| standings | 48 strings | Four blocks of 12: Order, GamesString, GoalsString, PointsString; at most 80 characters each |

Total frame size is 273 bytes plus the UTF-8 bytes of the prefix result, scores
and standings. LatestRound/GamesPlayed are nonnegative. Upcoming pairs must be a
permutation of IDs 0–11; previous IDs must be in range but may repeat in vanilla's
initial history. Collection odds are finite and within [1,100]; scratch odds may
also be zero. Prefix gameIndex is 0–6 and team IDs 0–11. All text excludes control
characters and surrogate code units; the prefix result is at most 256 characters.
Malformed boards do not consume sequence numbers. Sequence uses unsigned 16-bit
forward half-range ordering, with an explicit unset state so zero after wrap is
neither an initial-state sentinel nor permission to accept stale traffic.

The host only samples when both native load/calculation pipelines are in their
catalogued stable states. A join during calculation can use the last complete
board, without consuming the next ordinary broadcast. Guests buffer an owned
copy, wait for their local pipelines to finish, preserve their collections and
pause both generators. They update the existing live collection instances and
refresh visible teletext pages 240/241/302 without replaying ROUND, ODDS,
CHECKMEGAVETO or SAVEGAME. Disconnect restores guest data before resuming native
FSMs. The individual-player scoring table remains outside this snapshot, as do
Megaveto ticket identity, selections, purchases and claims (R2.21).

### Lotto v103 tickets and collection

`lottoTickets` catalog bindings name the native Pay button, factory, prefab fields,
three ArrayLists and collection-box actions. Host and guests hold Pay/Check money
until a receipt, capturing completed rows before any native debit. Each paid row
contains seven distinct integers in 1..39. Unpaid rows are zeroed, including a
partially edited next row. The host derives the cost from the catalog (native
3 mk per row) and verifies the request's round against Numbers/CurrentRound and
a fresh player pose within 6 m of the native spawn point.

The host creates the factory's actual prefab with those captured rows, its own
next persistent ID and its own spawn pose. It preserves the host's open form.
Factory save loading must have reached Idle with SaveID initialized to the
prefab name before issuing anything. A missing/unready factory is retryable and
consumes neither money nor a receipt. Native ID counter and ticket Use save
handling remain host-owned; no guest-provided ID can mint a ticket. Existing
saved host tickets are discovered after their native loading completes.

Requests carry a nonzero connection-lifetime token and wrapping uint sequence.
The latest receipt per authenticated player stores a copy of the complete request.
An exact retry returns that receipt without spawning or paying again; changed
payloads at the same sequence, old sequences and an unadmitted token change are
stale. Admission clears command deduplication but **never** lifetime ticket
retirement. A new press after a decline uses a new sequence. An unready host
keeps the original request pending instead of guessing an outcome.

Collection intercepts only Lotto objects in the native TrashTrigger. The host
validates the player's fresh pose within 6 m of the box and its own ticket body
within 3 m, and waits for Data/State 16 before reading native Winnings. Guest
requests carry no prize amount. Values below zero yield no payment; prizes below
1,000 mk credit shared cash and prizes at or above 1,000 mk credit shared bank.
Unrepresentable float money changes are declined without consuming the ticket.
An accepted claim first records terminal retirement and sets the native deletion
sentinel (TicketRound=8888), then commits the balance and runs the native garbage
transition. This protects duplicate/concurrent claims and guest reconnects.
Ordinary ItemDespawn for a ticket also runs its native retirement on the host
instead of destroying the save-handling FSM; discarding pays nothing.
Native save/load supplies outstanding tickets across host restarts; save testing
must verify deletion of claimed ticket keys. Bank statement text and achievement
presentation are not yet replayed by this handler.

Guests retain copied per-ticket states until bindings exist. Sequences are checked
per ticket; round/rows cannot change for an existing ID and a retirement can never
be undone by a later live packet. Invalid IDs, numeric fields, array sizes or poses
do not consume the receive baseline. Pending and repeated states do not teleport
an existing body: the regular item ownership/transform/snapshot path moves it.
The latest host state recreates a missing guest body. Ticket keepalives run every
15 seconds; changes are checked twice a second, and snapshots never consume a
connected guest's change baseline.

Guest replicas use the actual native prefab, with local prize calculation and
all ticket load/save/delete actions removed or paused. The guest's own saved
tickets are hidden while replicas exist and restored on teardown; replicas are
destroyed. Main form and claim hooks are restored, including a pending request's
held state. Binding failures contain the fault to Lotto ticket interactions.
This is implemented but requires the two-player/save/LOD matrix in BUILDING.md;
Megaveto purchase selections and claims are not covered by messages 181–183.

### Host-authoritative shopping bags v120

Bag item IDs use `FactoryItemIdentity.ItemId(factoryId, nativeId)`, where factoryId
hashes the catalog factory path and FSM name. The bag's native `Use.ID` survives
pickup/reparenting and display-name changes. A BagState requires a matching derived
item ID, valid catalog prefix, finite pose and condition 0–100. Identity fields are
immutable; stale revisions and contradictory equal-revision inventory are ignored.
Join, item resync and targeted replies include bag states and terminal removals.
Guest save bags are preserved inactive; isolated replicas skip native save/loading
and inventory mutation while retaining native short/long use-button gestures.

The host authenticates the requester, validates the observed revision, remaining
inventory, a living guest pose ≤2 seconds old within 3 m, and current bag ownership.
Host and guest openings share one reservation. Native contents factories use shared
scratch globals, so spills are serialized until their native work and exact output
capture finish. The host sets `CurrentBag` explicitly and enters the validated
one/all spill state; the player-input `Confirm` state is never replayed remotely.
A duplicate request recovers its immutable receipt; busy/rejected sequences cannot
become successful later. Changed revisions cannot spend the same inventory twice.
The capture survives timeouts while native work remains active.

The scanner never names bag outputs by hierarchy or position ordinal. Native product
names are discovered from the factory prefab's display-name actions, including
`chips` → `potato chips(itemx)`. Remote materialization retries unresolved entries
at 0.5-second intervals, drops retired/live entries, and reports failures at most
once every 30 seconds. Pickup guards prevent a remotely held bag from attaching to
the local hand; accepted remote ownership/removal releases that exact held bag.

Unsupported native part products fail opening preflight before inventory mutation.
This adapter does not yet establish complete condition/state replication for every
possible product in a mixed shopping bag; see the building guide's acceptance matrix.
