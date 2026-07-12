# WinterMP wire protocol

Protocol version: **28** (`ProtocolInfo.Version` in `src/WinterMP.Net/Protocol.cs`).
Any breaking change to framing, message layout or semantics bumps the version;
hosts refuse mismatched clients during handshake.

## Transport & framing

Datagram transports (classic Steam P2P via `SteamNetworking.SendP2PPacket`; loopback for dev). Per packet:

```
[1 byte channel]     -- only on the Steam transport (classic P2P has per-channel send)
[2 bytes messageId]  -- little-endian ushort
[payload]            -- message-specific, see below
```

All integers little-endian. Strings are UTF-8 with ushort byte-length prefix
(empty == null). Blobs are int32 length + raw bytes. Floats are raw IEEE 754.

## Channels

| Channel | Guarantees | Used for |
|---|---|---|
| 0 ReliableOrdered | reliable, ordered | events, FSM transitions, economy, chat, handshake |
| 1 UnreliableSequenced | best-effort; *receiver* drops stale packets via per-stream sequence numbers | transforms |
| 2 ReliableBulk | reliable; large, chunked | join snapshots, save data |

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
| 22 | PlayerTransform | 1 | playerId, seq, pos, rot, moveState |
| 23 | PassengerState | 0 | playerId, vehicleId, seatIndex (0 front passenger, 1 rear left, 2 rear right, 255 none); re-broadcast every ~8 s while seated; same-seat races resolved by lowest player id |
| 24 | GuestSpawn | 0 | host -> joining guest after snapshot: host position + rotation, last saved position + rotation, flags (bit 0 = last position valid, bit 1 = saved needs valid), hunger/fatigue/thirst/urine/**bodyTemp** floats (bodyTemp appended v28, restored with the others). Bit 0 is set only for **returning** guests (known to the host's sidecar before this connection). Guest shows spawn picker when bit 0 is set; otherwise snaps to host immediately. When guest picks last position and bit 1 is set, local need globals are restored from the sidecar values. |
| 25 | PlayerNeedsReport | 0 | guest -> host every ~12 s: playerId, hunger, fatigue, thirst, urine, **bodyTemp** (v28, `PLAYER/BodyTemp.Temperature` — the 5th need), seq — host stores in `wintermp-guests.json` sidecar (13-col line; legacy 12-col lines load bodyTemp=0) |
| 26 | SleepConsentRequest | 0 | host -> all guests when host enters a sleep/time-skip FSM state: requestId, initiatorPlayerId |
| 27 | SleepConsentResponse | 0 | guest -> host: requestId, playerId, accepted (byte 0/1) — host waits for all guests (90 s timeout); aborts sleep if any decline or timeout |
| 28 | SleepConsentResult | 0 | host -> all guests: requestId, accepted (byte 0/1) — dismisses guest prompt; on accept guests reset fatigue locally; host sends ACTIVATE to proceed and pushes TimeSync when the sleep FSM reaches Calc rates |
| 29 | PlayerDeathReport | 0 | any -> host: playerId, cause, seq — host rebroadcasts PlayerDeathEvent |
| 30 | PlayerDeathEvent | 0 | host -> all: playerId, cause, flags (bit 0 = permadeath group wipe) — hides avatars; wipe triggers local Systems/Death on every client |
| 31 | PlayerRespawn | 0 | respawning player -> all: playerId, pos, rot, seq — non-permadeath only; avatar visible again |
| 32 | PlayerClothingState | 0 | any -> host -> other guests (v28): playerId, clothingStage (FsmInt `ClothingStage` on `PLAYER/BodyTemp`, warmth tier), clothingType (FsmInt `ClothingType` on the FPS-camera `Piss` FSM, outfit variant). Owner-authoritative: reported on change, host relays; drives remote-avatar visual (best-effort shirt tint) and informs local warmth math. Never written onto the owning player's own FSM |
| 40 | FsmStateEnter | 0 | netId + state name; receiver replays via injected MP_* global transition (doors, ignitions, vehicle controls, car parts Bolted/Unbolted/Stop/Install/Remove, shop Buy/CashRegister Purchase/Cashier/Add, Peräpörtti restaurant Cashier/State 1, inspection Pay, post-package Close box/Remove order, post-office Spawn, phone-order Spawn package, Fleetari Pending cost/State 3, service brochure Fleetari 2, engine run/stall on SORBET/CORRIS Starter FSMs) |
| 41 | FsmRawEvent | 0 | netId + event name; receiver whitelists (TIGHTEN/UNTIGHTEN on bolts) |
| 42 | ItemTransform | 1 (vehicle/final: 0) | itemId, ownerPlayerId, seq, flags, pos, rot [, velocity when flags bit 3] — items *and* vehicles |
| 43 | TimeSync | 0 | host -> guests: hour (1-24), minutes, forecast temps, snowing, forecast index, daysPassed, dayOfWeek (0=Mon..6=Sun, 255 unknown) |
| 44 | BoltState | 0 | netId, boltTightness, screwInt — sent after each wrench turn settles (Set pos); receivers overwrite Screw FSM vars and replay Set pos |
| 45 | PartState | 0 | netId, flags (bit 0 installed), tightness (0-255), wear (0-255) — sent when a car part settles (Stop/Bolted/Unbolted); receivers overwrite Data FSM Installed/Tightness/Wear |
| 46 | ItemDespawn | 0 | itemId — pickable eaten/destroyed (Destroy, Check drink → State 2/5/6); receivers delete their local rigidbody |
| 47 | WorldStateChecksum | 0 | host -> guests every ~20 s: walletCrc, worldCrc (FSM + part + bolt vars), itemCrc (resting pickables), vehicleCrc (engine/fuel/climate), seq — guests compare and may request soft resync |
| 48 | WorldResyncRequest | 0 | guest -> host: flags (bit 0 wallet, bit 1 FSM states, bit 2 parts, bit 3 bolts, bit 4 items, bit 5 vehicles), checksumSequence — host replies with targeted snapshot chunks (15 s guest cooldown) |
| 49 | WorldObjectStateRequest | 0 | guest -> host: netId — host replies with the best single-object state it has (final ItemTransform, VehicleState/Climate, FsmStateEnter, PartState, or BoltState); guests auto-request after a pending FSM event expires (5 s cooldown per id) |
| 50 | HeatSourceState | 0 | host -> all (v28): sourceId (scene-path hash of the source container), flags (bit 0 = lit/embers), fuel (0-255 firewood), heatOutput (0-255), saunaTemp (sauna heat ×100, ushort; 0 for non-sauna). Host-authoritative shared state for the cabin woodstove (`CABIN/Cabin/woodstove/Fireplace`), sauna kiuas (`COTTAGE/Stuff/Sauna/Stove` — `SaunaHeat`/`StoveHeat`), and cottage/living-room fireplaces. Broadcast on change + ~20 s keepalive from whoever hosts; guests write the values back onto their local FSMs so each client's own position-derived body-temp calc warms consistently |
| 51 | HeatSourceIntent | 0 | guest -> host (v28): sourceId, action (0 light, 1 feed wood, 2 grill, 3 löyly/steam) — anyone-triggers. The guest emits this when it locally enters the source's lighting/feeding/grilling/steam FSM state (debounced 0.75 s); the host fires the matching game event (`USE`/`WOOD`/`SAUSAGE`/`STEAM`) on its authoritative FSM and the resulting HeatSourceState carries the progression back |
| 60 | VehicleState | 1 | vehicleId, ownerPlayerId, seq, flags (bit 0 engine on, bit 1 ACC/electrics on, bit 2 blinker left, bit 3 blinker right, bit 4 hazard), rpm, speedTenthsKmh, fuelLevel (0-255), coolantTemp (0-255 → 0-120 °C) — ~4 Hz from whoever is driving *or* left the engine/ACC running locally; receivers replay Electricity ON/OFF FSM, push rpm/speed/fuel/coolant into gauge variables (CORRIS angle gauges included), apply blinker/hazard stalk/events + hazard button replay, and synthesize engine audio (pitch from RPM), stopping after 2 s without packets |
| 61 | VehicleClimate | 1 | vehicleId, ownerPlayerId, seq, frost (0-255 exterior ice), flags (bit 0 window heater, bit 1 glass defrosting, bit 2 player in cabin), heaterTemp, heaterBlower, heaterDirection, fog (0-255 interior condensation), cabinTemp (0-255 → 0-40 °C) — ~2 Hz; receivers split frost (Frost + Freezing cutoffs + color.a) from fog (SweatRate + color.rgb + InteriorTemp + PlayerIn), replay window-heater On/Off, pulse DEFROST; LateUpdate keeps visuals pinned; included in join snapshot |
| 62 | VehicleCargo | 1 (empty set: 0) | vehicleId, ownerPlayerId, seq, count (≤24), entries (itemId, localPos, localRot in vehicle-root space) — sent by the vehicle's transform owner at the vehicle send rate while it moves; the owner's physics simulates the cargo and streams its live vehicle-local poses. Each packet is the COMPLETE cargo set: receivers pin listed items kinematically (composed against their own smoothed vehicle pose, colliders untouched) and release tracked items that are no longer listed, seeding them with the pin's observed world motion (a rider inherits ~the car's velocity, an item merely shielded at its resting pose stays at rest; the car's velocity is the fallback when no fresh sample exists). The non-empty → empty transition is sent reliably; per-vehicle wrap-aware seq dedup per owner |
| 80 | WalletState | 0 | money (float mk), seq — host -> guests every ~2 s and on join; guests overwrite the PlayMaker global `Money` + HUD (host wins) |
| 81 | PurchaseIntent | 0 | guest -> host only: playerId, netId (Buy/CashRegister/Use/Data/Button buy FSM), eventName (USE/PURCHASE/DEPURCHASE/PAY/PAYMENT/BUY/CLICK), seq — guest aborts local buy guard and restores wallet; host fires the event and broadcasts resulting FsmStateEnter + WalletState |
| 100 | NpcTransform | 1 (final: 0) | netId, seq, flags, pos, rot — host-only stream for TRAFFIC/, NPC_CARS/, HUMANS/ rigidbodies; guests pin kinematic and ease toward pose; distance tiers ~8 Hz (≤80 m), ~3 Hz (≤200 m), off beyond; moving bodies still stream until a reliable **final** at-rest packet |
| 120 | WorldSnapshotRequest | 0 | guest -> host once its first world scan completes; carries the guest's id hash (diagnostic only) |
| 121 | WorldDoorSnapshot | 0 | host -> guest: (netId, stateName) pairs for doors/ignitions/controls/starters the host saw change; chunked (≤60/message) |
| 122 | WorldItemSnapshot | 0 | host -> guest: (itemId, pos, rot) for every item/vehicle; chunked (≤40/message); unknown ids are parked until scanned |
| 123 | WorldBoltSnapshot | 0 | host -> guest: (netId, boltTightness, screwInt) for every non-loose bolt; chunked (≤80/message); unknown ids are parked until scanned |
| 124 | WorldPartSnapshot | 0 | host -> guest: (netId, flags, tightness, wear) for every installed or non-default car part; chunked (≤80/message); unknown ids are parked until scanned |
| 125 | WorldItemDespawnSnapshot | 0 | host -> guest: itemIds consumed/destroyed during this session; guests delete local copies (parked until scanned); chunked (≤80/message) |

`NpcTransform.flags`: bit 0 = **final** (at-rest pose, sent reliable; receiver
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

### Reserved ranges

- 50–59 world events — M3+
- 63–79 vehicles (attachment, fuel/damage) — M4/M5
- 82–99 economy (phone orders, deliveries) — M5
- 100–119 NPCs/jobs — M6
- 123–139 snapshot/bulk transfer control (save data) — M3+

Rules: never reuse a retired id; new fields are appended only together with a
protocol version bump (no silent format drift).
