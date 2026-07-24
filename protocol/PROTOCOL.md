# WinterMP wire protocol

Protocol version: **80** (`ProtocolInfo.Version` in `src/WinterMP.Net/Protocol.cs`).
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
messages may use channel 0 for a final or snapshot state. Channel 2 is currently
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
  |<----- WorldBoltSnapshot * n --|   bolt tightness for every non-loose Screw FSM
  |<----- WorldPartSnapshot * n --|   installed/tightness/wear for every non-default car part
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
| 23 | PassengerState | 0 | playerId, vehicleId, seatIndex (0 front passenger, 1 rear left, 2 rear right, 255 none), seq (**appended v52**); re-broadcast every ~8 s while seated. The host accepts only an authenticated player's next sequence: an exit must carry vehicle id 0, while a seat claim must name one of the three discovered passenger anchors and be within 2 m of it from a fresh player pose. The host owns canonical occupancy and resolves same-seat races by lowest player id; malformed/stale/lost claims receive a self-addressed `SeatNone` correction. |
| 24 | GuestSpawn | 0 | host -> joining guest after snapshot: host position + rotation, last saved position + rotation, flags (bit 0 = last position valid, bit 1 = saved needs valid, **bit 2 = saved dirtiness valid**), hunger/fatigue/thirst/urine/**bodyTemp** floats (bodyTemp appended v28), **stress/drunk** floats (appended v30, restored with the others; bodyTemp 0 = absent and is skipped on restore), then **dirtiness** (appended v55, `PlayerDirtiness`, restored only when bit 2 is set so legacy profiles cannot falsely clean a guest), then **`PlayerAlco`** (**v78**, restored only when **bit 3** = saved-alco is set). Bit 0 is set only for **returning** guests (known to the host's sidecar before this connection). Guest shows spawn picker when bit 0 is set; otherwise snaps to host immediately. When guest picks last position and bit 1 is set, local need globals are restored from the sidecar values. |
| 25 | PlayerNeedsReport | 0 | guest -> host every ~12 s: playerId, hunger, fatigue, thirst, urine, **bodyTemp** (v28, `PLAYER/BodyTemp.Temperature` — the 5th need), **stress** (v30, `Stress` global — food/coffee/smoking move it), **drunk** (v30, `DrunkCurrent` on the FPS-camera "Drunk Mode" FSM), seq, then **dirtiness** (v55, `PlayerDirtiness`; appended after seq), then a **`HasDirtiness` byte** (**v56**), then **`PlayerAlco` + a `HasAlco` byte** (**v78**, persistent BAC). The host stores only the authenticated player’s next finite report in the `wintermp-guests.json` sidecar (16-column rows; legacy 12–15-column rows retain missing trailing needs, and specifically omit dirtiness rather than falsely restoring clean). **Since v56** the report no longer waits on the `PlayerDirtiness` global — the other seven needs report regardless — and `HasDirtiness=false` (global not yet resolved) makes the host record dirtiness as unknown (15-column row), so a guest whose handle bound late is never restored to clean. |
| 26 | SleepConsentRequest | 0 | host -> all guests when host enters a sleep/time-skip FSM state: requestId, initiatorPlayerId |
| 27 | SleepConsentResponse | 0 | guest -> host: requestId, playerId, accepted (byte 0/1) — host waits for all guests (90 s timeout); aborts sleep if any decline or timeout |
| 28 | SleepConsentResult | 0 | host -> all guests: requestId, accepted (byte 0/1) — dismisses guest prompt; on accept guests reset fatigue locally; host sends ACTIVATE to proceed and pushes TimeSync when the sleep FSM reaches Calc rates |
| 29 | PlayerDeathReport | 0 | any -> host: playerId, cause, seq — host rebroadcasts PlayerDeathEvent. Cause bytes are append-only (`DeathCause`): 0 unknown, 1-13 fatigue/hunger/thirst/urine/stress/run-over/drown/fire/electrocute/hypothermia/murder/train/accident, **14-21 appended v32**: sewage/carbon-monoxide/PTO/cutter-blade/jail/piss-TV/burn/smoking (the death FSM's remaining cause bools) |
| 30 | PlayerDeathEvent | 0 | host -> all: playerId, cause, flags (bit 0 = permadeath group wipe) — hides avatars; wipe triggers local Systems/Death on every client (accident maps to the RUNOVER screen — State 3 has no crash transition; burn → FIRE; smoking → FATIGUE) |
| 31 | PlayerRespawn | 0 | respawning player -> all: playerId, pos, rot, seq — non-permadeath only; avatar visible again |
| 32 | PlayerClothingState | 0 | any -> host -> other guests (v28): playerId, clothingStage (FsmInt `ClothingStage` on `PLAYER/BodyTemp`, warmth tier), clothingType (FsmInt `ClothingType` on the FPS-camera `Piss` FSM, outfit variant). Owner-authoritative: reported on change, host relays; drives remote-avatar visual (best-effort shirt tint) and informs local warmth math. Never written onto the owning player's own FSM. **Because it is change-only (no keepalive, not in the chunked snapshot), the host also sends a joining guest one per already-connected player right after GuestSpawn** — otherwise a joiner renders already-dressed players in default clothing until each next changes clothes | **v79: appends WinterGarment byte (0 none/1 jacket/2 coverall).**
| 40 | FsmStateEnter | 0 | netId + state name; receiver replays via injected MP_* global transition (doors, ignitions, vehicle controls, car parts Bolted/Unbolted/Stop/Install/Remove, shop Buy/CashRegister Purchase/Cashier/Add, Peräpörtti restaurant Cashier/State 1, inspection Pay, post-package Close box/Remove order, post-office Spawn, phone-order Spawn package, Fleetari Pending cost/State 3, service brochure Fleetari 2, engine run/stall on SORBET/CORRIS Starter FSMs, and **v51** home/yard/apartment shower tap plus valve ON/OFF controls) |
| 41 | FsmRawEvent | 0 | netId + event name; receiver whitelists (TIGHTEN/UNTIGHTEN on bolts) |
| 42 | ItemTransform | 1 (vehicle/final: 0) | itemId, ownerPlayerId, seq, flags, pos, rot [, velocity when flags bit 3] — items *and* vehicles |
| 43 | TimeSync | 0 | host -> guests: hour (1-24), minutes, forecast temps, snowing, forecast index, daysPassed, dayOfWeek (0=Mon..6=Sun, 255 unknown) |
| 44 | BoltState | 0 | netId, boltTightness, screwInt — sent after each wrench turn settles (Set pos); receivers overwrite Screw FSM vars and replay Set pos |
| 45 | PartState | 0 | netId, flags (bit 0 installed), tightness (0-255), wear (0-255) — sent when a car part settles (Stop/Bolted/Unbolted); receivers overwrite Data FSM Installed/Tightness/Wear |
| 46 | ItemDespawn | 0 | itemId — pickable eaten/destroyed (Destroy, Check drink → State 2/5/6); receivers delete their local rigidbody |
| 47 | WorldStateChecksum | 0 | host -> guests every ~20 s: walletCrc, worldCrc (FSM + part + bolt vars), itemCrc (resting pickables), vehicleCrc (engine/fuel/climate), seq — guests compare and may request soft resync |
| 48 | WorldResyncRequest | 0 | guest -> host: flags (bit 0 wallet, bit 1 FSM states, bit 2 parts, bit 3 bolts, bit 4 items, bit 5 vehicles), checksumSequence — flags must name at least one listed group; host replies with targeted snapshot chunks and accepts at most one request per guest every 15 s |
| 49 | WorldObjectStateRequest | 0 | guest -> host: netId — host replies with the best single-object state it has (final ItemTransform, VehicleState/Climate, FsmStateEnter, PartState, or BoltState); guests auto-request after a pending FSM event expires (5 s cooldown per id), while the host admits at most four requests per second per guest |
| 50 | HeatSourceState | 0 | host -> all (v28): sourceId (scene-path hash of the source container), flags (bit 0 = lit/embers), fuel (0-255 firewood), heatOutput (0-255), saunaTemp (sauna heat ×100, ushort; 0 for non-sauna). Host-authoritative shared state for the cabin woodstove (`CABIN/Cabin/woodstove/Fireplace`), sauna kiuas (`COTTAGE/Stuff/Sauna/Stove` — `SaunaHeat`/`StoveHeat`), and cottage/living-room fireplaces. Broadcast on change + ~20 s keepalive from whoever hosts; guests write the values back onto their local FSMs so each client's own position-derived body-temp calc warms consistently |
| 51 | HeatSourceIntent | 0 | guest -> host (v49): sourceId, action (0 light, 1 feed wood, 2 grill, 3 löyly/steam), authenticated player id, sequence — anyone-triggers. The guest emits this when it locally enters the source's lighting/feeding/grilling/steam FSM state (debounced 0.75 s); the host accepts only the sender's fresh next sequence while their pose is within 8 m of the exact source, then fires the matching game event (`USE`/`WOOD`/`SAUSAGE`/`STEAM`) on its authoritative FSM. Invalid source/action, stale/replayed packet, and distant player reports are dropped; the resulting HeatSourceState carries accepted progression back. |
| 52 | ItemSpawn | 0 | host -> guests (v29): containerNetId, epoch, ownerPlayerId, stateName, count (≤32) + entries (netId, templateName, pos, rot), **flags (appended v31: bit 0 = replay)**, **offerSeq (appended v32: echoes SpawnIntent.seq when answering an offer, else 0 — two quick same-bag offers are indistinguishable by container+state alone; an offer whose entries all failed is answered with an EMPTY manifest so the guest releases its parked clones immediately)** — manifest for items a container FSM (grocery bag "Spawn all"/"Spawn one") just spilled; runtime clones exist in neither save, so the host is sole authority for their identity. `ownerPlayerId` is the **spiller** (host, or the guest whose offer produced this manifest — v32): the spiller binds its own captured clones to these ids by template name and streams them as owner; every other peer **materializes** each entry — adopt an untracked same-named clone within 3 m of the pose, steal a stale scanner-registered clone (replay / own-offer only: the id must be provably host-unnamed), else instantiate from an exact- or base-name-matched scene template ("shopping bagx" ↔ "shopping bag(itemx)": masters carry an `x` suffix, live instances `(itemx)`; the clone is renamed to the manifest's templateName) — and holds them owner-followed/kinematic until a final ItemTransform rests them. **No receiver ever fires a bag FSM (v32)** — the bag's "Confirm" state bounces remote entries back to "Wait player" without a live player interaction, and the spiller's bag-consumption despawn destroys replica bags anyway. Deduped by (containerNetId, epoch); excluded from the world checksum. **Replay manifests (v31)** ride the join snapshot: the host re-sends its session's spills live-refreshed (eaten entries dropped, poses updated) so late joiners get the items |
| 53 | SpawnIntent | 0 | guest -> host (v29): playerId, containerNetId, stateName, seq, **count + entries (templateName, pos, rot) (appended v32)** — the guest's own bag spilled **naturally** (no abort since v32); after a ~0.3 s-stable capture of the clones near its bag it offers their names + poses. The host accepts only a fresh next sequence from the authenticated player, with 1–32 finite/template-backed entries within 12 m of that player, then materializes matching copies from its own scene templates at those poses, mints ids under a fresh epoch (one per-container epoch counter serves host spills and guest offers alike), and answers with the ItemSpawn manifest (52) echoing `seq`. Runtime bag ids and contents are deliberately peer-local, so the host cannot re-derive the exact inventory; these bounds prevent malformed/replayed/map-wide offers without inventing a false inventory check. The offering guest pairs the answer to the exact offer by that echo, binds its parked clones to the minted entries by template name, and keeps streaming them as owner. Unanswered offers release their clones back to the local scanner after 6 s (local-only fallback, never lost). Host spills are captured **regardless of connected peers** — a host shopping before the friend joins still mints, so the retained manifest can replay at join |
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
| 64 | VehicleDamage | 0 | owner -> host -> peers (**v60**): vehicleId, ownerPlayerId, damageMask (uint, bit per `PartBreakages` breakage: bearing1-5, crankshaft, headgasket, piston1-4, oilpan, timingbelt, seize, block, camfail), seq. Engine breakage is **owner-authoritative**: the driver's FSM rolls, others zero their local `Chance` and apply this mask by firing the matching breakage event. Re-sent on change + 15 s keepalive so a late joiner converges; host validates the sender is the current owner and relays. |
| 65 | VehicleCondition | 0 | owner -> host -> peers (**v61**): vehicleId, ownerPlayerId, seq, tirePressure (byte, bar*100), drivetrainDamage (byte, GearboxDamage `DamageType`), per-wheel health FL/FR/RL/RR (byte), flags (bit0-3 puncture, bit4-7 rim per wheel). **Owner-authoritative** drivetrain wear + tire condition: non-owners write health/pressure and fire PUNCTURE/RIM/FIXED on each wheel FSM. Re-sent on change + 20 s keepalive; host validates owner + relays. |
| 66 | CarRadioState | 0 | host -> guests (**v81**): seq, radioId (0 corris, 1 sorbet), tune (float), volume (byte, knob ×100). Host owns the in-car radio tuning + volume; guests apply. `tune` replaced a byte `channel` in v81 — no radio Knob FSM has a float named Channel (the only Channel is a *string* on the CD player), so the old field bound null and the station never synced. Bound to `StockRadio0/ButtonsRadio/Volume :: Knob`, which carries both `Tune` and `Volume`. Unquantized: the per-station windows are not knowable from the catalog dump. In the join snapshot. |
| 80 | WalletState | 0 | money (float mk), seq — host -> guests every ~2 s and on join; guests overwrite the PlayMaker global `Money` + HUD (host wins) |
| 81 | PurchaseIntent | 0 | guest -> host only: playerId, netId (Buy/CashRegister/Use/Data/Button buy FSM), eventName (USE/PURCHASE/DEPURCHASE/PAY/PAYMENT/BUY/CLICK/**ACTIVATE**), seq — guest aborts local buy guard and restores wallet; host fires the event and broadcasts resulting FsmStateEnter + WalletState. **Since v48**, the host accepts only the authenticated player’s next monotonic sequence while that player has a fresh pose within the buy target’s interaction radius, and only for a catalogued entry-guard event; unknown, distant, non-entry, duplicate, and stale packets do not reach the FSM. `ACTIVATE` is used by the host-validated Fines record (v41) to enter its normal money check without trying to replay a remote player's police collision. **v50** adds the exact `PriceMoneyRace` and `PriceMoneyRally` `USE` paths, so race prize collection executes only on the host and converges through the shared wallet. |
| 82 | PoliceState | 0 | host -> guests (v41): target player id, offence flags, active flag, sequence, stable checkpoint id, and host-validated fine. The host writes the fine to its own Fines record and peers mirror that durable record; payment still goes through the normal guarded `PurchaseIntent` path, so the shared wallet remains host-owned. When the host's Fines price clears (fine paid/reset), it broadcasts the record once more with the active flag cleared and drops it from join snapshots, so late joiners never inherit a phantom fine. A guest advances its report-dedup baseline only on the host's accepting echo, re-sending each interval until then. Offence bits: alcohol, fuel, helmet, inspection, radar, registration plates, seatbelts, speeding (low to high bit). |
| 83 | PoliceIntent | 0 | guest -> host (v41): player id, locally observed checkpoint offence bits, sequence, stable checkpoint id, and the locally generated fine. This is a report rather than authority: the host accepts only a fresh authenticated player transform close to that exact known checkpoint and a nearby vehicle currently delegated to that player; stale/replayed sequences, invalid flags, and non-finite/out-of-range fines are discarded. |
| 84 | HomeStereoState | 0 | host -> guests (v42): fixed home stereo power/channel flags, sequence, volume and bass. These are the durable audio/electricity inputs, rather than fragile presentation FSM states; sent on host change and in join snapshots. |
| 85 | HomeStereoIntent | 0 | guest -> host (v42): authenticated player id, requested power/channel flags, sequence, volume and bass. The host accepts only finite normalised values while the sender's fresh pose is within 6 m of the exact home-stereo switch, then writes and broadcasts the authoritative scalar state. |
| 86 | RallyState | 0 | host -> guests (v43): player id, SS stage (1-3), phase (idle/racing/finished), last accepted checkpoint, sequence and host-clocked elapsed centiseconds. Sent on each accepted crossing, every second while racing, once when a host-driven record reaches finished (its elapsed clock freezes at the finish), and in join snapshots. |
| 87 | RallyIntent | 0 | guest -> host (v43): authenticated player id, SS stage, start (checkpoint 0) or numbered checkpoint (1-6), and sequence. The host validates the exact stage marker, fresh player pose, nearby vehicle delegated to that driver, monotonic intent sequence, and strict checkpoint ordering; elapsed time always comes from the host clock. |
| 88 | IceRaceState | 0 | host -> guests (v44): player id, inferred time-trial/lap-race start mode, accepted checkpoint phase, completed laps, sequence and host-clocked elapsed centiseconds. Sent on accepted markers, every second while active, and in join snapshots. |
| 89 | IceRaceIntent | 0 | guest -> host (v44): authenticated player id, marker (start, checkpoint 1, checkpoint 2, finish) and sequence. The host infers the start mode only from the driver’s proximity to one of the two fixed start/finish markers, then requires checkpoint 1 → checkpoint 2 → that mode’s finish marker for each lap with fresh pose and nearby delegated vehicle validation. |
| 90 | IceRaceEventState | 0 | host -> guests (v47): grid-ready/on-track/player-registered flags, sequence, car-limit/current-car/on-track counts, heat stage, lane, qualifying/final race distances, starter count, event time, selected car id/reference, then race stage. This mirrors `RACES/ICERACE/TrackFunctions :: Data` plus `TrackFunctions/LINEUPS :: Logic` on change and in join snapshots, so registration/grid/heat presentation uses the host event configuration. |
| 91 | IceRaceResultsState | 0 | host -> guests (v46): sequence plus up to six ordered result rows (driver name, number, model, UA). The rows come from the host-generated `Stats/ResultsRace/Data/{0..5}` records and are mirrored on change and in join snapshots so every result board shows the same ranking rather than each client rerunning leaderboard generation. |
| 92 | RadiatorThermostatState | 0 | host -> guests (v54): thermostat Knob FSM `netId` + its game-owned `Rotation` float after the host applies a turn. Guests emit only the +/- knob `FsmStateEnter` intent; the host applies it and broadcasts this absolute settled value so receivers and joiners *set* the rotation rather than re-applying a relative increase/decrease. Sent on change and in join snapshots; a state whose target Knob FSM has not registered yet is held as a pending apply until it does. |
| 92 | RadiatorThermostatState | 0 | host -> guests (v54): thermostat Knob FSM netId, absolute game-owned `Rotation` float after the host applies a turn. Sent on host change and in join snapshots; receivers write the value onto their local Knob FSM. FSM state replay alone cannot carry this — an Increase/Decrease transition is a *delta*, so a joiner needs the resulting absolute rotation. |
| 93 | GamblingState | 0 | host -> guests (**v57**): shared gambling device (slot machine / Ventti). machineId (scene-path hash), kind (0 slot / 1 ventti), flags (bit0-2 reel1-3 locked, bit3 active), credit (float), bet (byte), V1/V2/V3 (byte — slot reel symbols; ventti player/house total + outcome), payout (int mk). Sent on change + keepalive + join force-broadcast. The **host** runs the spin/deal RNG + payout so reels and the shared wallet agree; guests display these and let money ride `WalletState`. **Slots:** guests also spin locally, on purpose — every value here overwrites theirs and nothing durable is at stake. **Ventti:** the guest's resolver FSM (`Table/GameManager :: Use`) is disabled for the session, so these totals are display-only and are ignored unless that suppression is confirmed active; a car/house transfer on the host does **not** yet propagate (COVERAGE-ROADMAP R2.11), and `V3` is reserved — currently always 0 (R2.13). |
| 94 | GamblingIntent | 0 | guest -> host (**v57**): machineId, action (0 pay,1 bet,2 start,3-5 lock reel1-3,6 cashout; 7-11 ventti bet/deal/hit/stand/wager-car), playerId, seq. Host validates fresh nearby pose (≤6 m) + monotonic per-player seq, then forces the matching button state on its authoritative FSM via `FsmHook.FireRemoteEntry` so the real stake/RNG runs host-side. |
| 95 | UtilityBillState | 0 | host -> guests (**v58**): meter (0/1 electricity, 2/3 phone), unpaidBills (float), flags (bit0 power-on = electricity `MainSwitch` / phone `PhonePaid`). Host owns the bill ledger + blackout and broadcasts on change + keepalive + join; guests write it back so both homes cut power together. Bill payment is **not** here — the Pay buttons are catalogued `buys[]`, so paying debits the shared wallet via the host purchase path. |
| 99 | ApplianceState | 0 | host -> guests (**v75**): applianceId (scene-path hash), kind (0 oven), flags (bit0 fire, bit1 fuse-ok), heat1-4 (byte, hotplate heat). Host owns each oven/stove; guests apply the heats + fuse (fire hazard is deterministic from heats). In the join snapshot. |
| 96 | LotteryDrawState | 0 | host -> guests (**v59**): round (int), nationalPot (int), winningNumbers (string `UTNational7`), flags (bit0 draw-done). Host owns the national lottery draw and broadcasts on change + keepalive + join; guests write it onto `Systems/Lottery :: Numbers` so every ticket is judged against the same numbers (and don't re-roll). Lotto/Megaveto ticket buy-in routes through the catalogued Pay buttons. |
| 97 | FleetariOrderState | 0 | host -> guests (**v62**): flags (bit0 order active), seq, jobTotalCost (float), carPaintColor + rimPaintColor (packed RGBA), jobs/orderCode/paintCode/axleCode/tireCode (strings). The shared repair-shop order record; broadcast on change + join so observers/joiners agree. The host's Work FSMs apply it to the host-owned shop car. |
| 98 | FleetariOrderIntent | 0 | guest -> host (**v62**): the exact `OrderFleetari` record the guest configured (same fields + playerId), captured when it confirms so the host pairs it to that guest's next `PurchaseIntent(PAYMENT/PAY)` and applies the actual jobs. Mirrors `MailOrderIntent` capture-and-pair; payment rides the existing catalogued OrderFleetari buy. |
| 100 | NpcTransform | 1 (final: 0) | netId, seq, flags, pos, rot — host-only stream for TRAFFIC/, NPC_CARS/, HUMANS/, and ice-race opponent rigidbodies; guests pin kinematic and ease toward pose (also freezing race-driving FSMs); distance tiers ~8 Hz (≤80 m), ~3 Hz (≤200 m), off beyond; moving bodies still stream until a reliable **final** at-rest packet |
| 101 | FleaSaleState | 0 | host -> guests (**v64**): seq, moneyTotal (float), rentDays (ushort), flags (bit0 rented). Host owns the flea sale table + runs the day-timed sale RNG; guests suppress their local `SaleTable :: Sell` FSM and apply proceeds/rent. In the join snapshot. Per-item placement/pricing stays local (dynamic refs). |
| 102 | FleaSaleIntent | 0 | guest -> host (**v64**): action (0 rent, 1 collect), playerId, seq. Host validates fresh nearby pose + monotonic seq and fires the real RENT/MONEY event so the shared wallet moves once. |
| 103 | TaxiJobState | 0 | host -> guests (**v65**): seq, jobStage (int), money (float), kmsDriven (float), flags (bit0 employed). Host owns the taxi job lifecycle + earnings; guests apply. In the join snapshot. Fare payout reaches the shared wallet via the host's payment logic; the taxi vehicle streams through the normal vehicle path. |
| 105 | BrewState | 0 | owner -> host -> peers (**v66**): itemId (tracked bucket), ownerPlayerId, seq, flags (bit0 finished, bit1 lid on), alcohol (float), brewTime (float). Kilju fermentation carried like `FluidContainerState` — whoever holds the bucket streams it, others apply; the stream also carries ingredient-add effects. In the join snapshot. |
| 107 | WelfareState | 0 | host -> guests (**v68**): seq, unemployDays (int), paidAmount (float), weekly (float), flags (bit0 claiming). Host owns the Kela unemployment claim + benefit calc; guests apply. In the join snapshot. Weekly benefit credits the shared wallet via host logic. |
| 106 | HitchhikerState | 0 | host -> guests (**v69**): seq, drunkStage (int), movingStage (int), money (int), flags (bit0 paid, bit1 KiljuMurderer, bit2 suicide, bit3 active). Host owns the hiker variant + stage; guests apply. Body pose streams over NpcTransform (ScriptedMover). In the join snapshot. |
| 108 | PhoneCallEvent | 0 | host -> guests (**v76**): callId (ushort, monotonic), topic (string). Host decides an incoming call and broadcasts it; guests set the phone Topic + fire the matching ring event. Discrete one-shot (not in snapshot). |
| 109 | PissAreaState | 0 | host -> guests (**v80**): seq, scale1-5 (byte, *20). Host owns the five persistent yard piss-stain scales; guests apply. In the join snapshot. |
| 120 | WorldSnapshotRequest | 0 | guest -> host once its first world scan completes; carries the guest's id hash (diagnostic only). Host accepts at most one request per guest every 10 s. |
| 121 | WorldDoorSnapshot | 0 | host -> guest: (netId, stateName) pairs for doors/ignitions/controls/starters the host saw change; chunked (≤60/message) |
| 122 | WorldItemSnapshot | 0 | host -> guest: (itemId, pos, rot) for every item/vehicle; chunked (≤40/message); unknown ids are parked until scanned |
| 123 | WorldBoltSnapshot | 0 | host -> guest: (netId, boltTightness, screwInt) for every non-loose bolt; chunked (≤80/message); unknown ids are parked until scanned |
| 124 | WorldPartSnapshot | 0 | host -> guest: (netId, flags, tightness, wear) for every installed or non-default car part; chunked (≤80/message); unknown ids are parked until scanned |
| 125 | WorldItemDespawnSnapshot | 0 | host -> guest: itemIds consumed/destroyed during this session; guests delete local copies (parked until scanned); chunked (≤80/message) |
| 140 | WantedState | 0 | host -> guests (**v70**): seq, manslaughter/attemptedManslaughter/policeEvasion/trafficFatality/daysFines/sentence/daysInJail (int), flags (bit0 cousin). Host owns the shared group wanted level; guests apply. In the join snapshot. |
| 141 | CrimeReport | 0 | guest -> host (**v70**): playerId, seq, crimeType (0 manslaughter..4 daysFines), delta (int, ≤32). A guest whose local `PlayerWanted` counter rose reports the delta; host validates identity + monotonic seq and adds it to its authoritative counter. |
| 142 | JailState | 0 | host -> guests (**v71**): seq, daysLeft (int), sentence (int), flags (bit0 jailed). Host owns the JAIL day-countdown; guests apply it so the sentence agrees and everyone is released together. Confinement position rides the player transform stream. In the join snapshot. |
| 143 | PursuitState | 0 | host -> guests (**v72**): seq, flags (bit0/1 cop car 1/2 chasing, bit2/3 cop car 1/2 siren). Host owns the pursuit; guests apply the sirens (lights) **only** — the chase bits are observability, deliberately not mirrored onto the guest's CopPassenger FSM (that would make each guest raise its own duplicate fine). Cop-car pose streams over NpcTransform. In the join snapshot. |
| 150 | RallyResultsState | 0 | host -> guests (**v73**): seq, timeSS1/2/3 (int stage times), playerTimeTotal (float), playerClassLevel (int), timePenalty (float), flags (bit0 raceOver, bit1 winner, bit2 registered, bit3 secondDay). Host owns the rally results ledger + enroll + parc-fermé penalty; guests apply. In the join snapshot. Reward rides the existing host-gated race price triggers. |
| 151 | JokkisRaceState | 0 | host -> guests (**v74**): seq, laps (int), timeCentiseconds (int), flags (bit0/1 checkpoint 1/2). Host owns the JOKKIS banger race lap/time/checkpoint; guests apply. In the join snapshot. |

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

`ItemSpawn` net ids are minted by the host as `hash("spawn:" + containerNetId +
":" + epoch + ":" + ordinal)` — for its own spills from the clones captured
within ~2 s / 6 m of its bag (already-tracked bodies excluded), for guest offers
from the offered entry list. Container ids never need to match across peers
(bags are runtime clones with salted per-peer ids); they only scope the local
capture and key the (containerNetId, epoch) manifest dedup. Once bound, spawned
items behave as ordinary synced pickables (claim/cargo/despawn).

`VehicleState` / `VehicleClimate` sequence (v26): the live stream uses a
per-stream `Sequence` that receivers dedup against (stale/duplicate dropped).
`Sequence == 65535` (`SnapshotSequence`) is reserved as a join/resync sentinel —
receivers apply it *without* dedup, so a freshly joined guest (whose last-seen
sequence is still 0) does not discard the Sequence-0 snapshot of a parked car
and miss its engine/electrics/climate state. The live stream never emits 65535
as a real pose (at worst one un-deduped packet after a full ushort wrap, ~4.5 h).

### Reserved ranges

- 54–59 world events — M3+
- 63–79 vehicles (attachment, fuel/damage) — M4/M5
- 82–99 economy (phone orders, deliveries) — M5
- 100–119 NPCs/jobs — M6
- 123–139 snapshot/bulk transfer control (save data) — M3+

Rules: never reuse a retired id; new fields are appended only together with a
protocol version bump (no silent format drift).
