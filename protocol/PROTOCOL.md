# WinterMP wire protocol

Protocol version: **8** (`ProtocolInfo.Version` in `src/WinterMP.Net/Protocol.cs`).
Any breaking change to framing, message layout or semantics bumps the version;
hosts refuse mismatched clients during handshake.

## Transport & framing

Datagram transports (Steam Networking Sockets P2P; loopback for dev). Per packet:

```
[1 byte channel]     -- only on the Steam transport (sockets have no channels)
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
  |<----- TimeSync ---------------|   clock + weather (also re-broadcast every 30 s)
  |<-----> Chat / PlayerTransform / ItemTransform / FsmStateEnter ...
```

Topology is a star: guests only talk to the host; the host relays chat and
transforms to other guests and is authoritative for all world state.

## Message ids

| Id | Message | Channel | Notes |
|---|---|---|---|
| 1 | HandshakeRequest | 0 | versions, catalog hash, player name |
| 2 | HandshakeResponse | 0 | accepted + playerId, or refusal reason |
| 3 | Ping | 0 | nonce + sender time |
| 4 | Pong | 0 | echoes nonce |
| 5 | Disconnect | 0 | human-readable reason |
| 10 | Chat | 0 | senderPlayerId + text |
| 20 | PlayerSpawn | 0 | playerId, steamId, name |
| 21 | PlayerDespawn | 0 | playerId, reason |
| 22 | PlayerTransform | 1 | playerId, seq, pos, rot, moveState |
| 23 | PassengerState | 0 | playerId, vehicleId, seatIndex (0 front passenger, 1 rear left, 2 rear right, 255 none); re-broadcast every ~8 s while seated; same-seat races resolved by lowest player id |
| 40 | FsmStateEnter | 0 | netId + state name; receiver replays via injected MP_* global transition (doors, ignitions, vehicle controls, engine run/stall on SORBET/CORRIS Starter FSMs) |
| 41 | FsmRawEvent | 0 | netId + event name; receiver whitelists (TIGHTEN/UNTIGHTEN on bolts) |
| 42 | ItemTransform | 1 (final: 0) | itemId, ownerPlayerId, seq, flags, pos, rot — items *and* vehicles |
| 43 | TimeSync | 0 | host -> guests: hour (1-24), minutes, forecast temps, snowing, forecast index |
| 60 | VehicleState | 1 | vehicleId, ownerPlayerId, seq, flags (bit 0 engine on, bit 1 ACC/electrics on, bit 2 blinker left, bit 3 blinker right), rpm, speedTenthsKmh, fuelLevel (0-255) — ~4 Hz from whoever is driving *or* left the engine/ACC running locally; receivers replay Electricity ON/OFF FSM, push rpm/speed/fuel into gauge variables (CORRIS angle gauges included), apply blinker stalk/events, and synthesize engine audio (pitch from RPM), stopping after 2 s without packets |
| 61 | VehicleClimate | 1 | vehicleId, ownerPlayerId, seq, frost (0-255), flags (bit 0 window heater on, bit 1 glass defrosting), heaterTemp, heaterBlower, heaterDirection (each 0-255) — ~2 Hz from driver, passenger, or anyone near a parked car; receivers replay window-heater On/Off FSM, pulse GlassFrosting DEFROST + CarTemp Defrost, push frost into GlassFrosting/Freezing cutoffs every LateUpdate; included once per vehicle in join snapshot |
| 120 | WorldSnapshotRequest | 0 | guest -> host once its first world scan completes; carries the guest's id hash (diagnostic only) |
| 121 | WorldDoorSnapshot | 0 | host -> guest: (netId, stateName) pairs for doors/ignitions/controls/starters the host saw change; chunked (≤60/message) |
| 122 | WorldItemSnapshot | 0 | host -> guest: (itemId, pos, rot) for every item/vehicle; chunked (≤40/message); unknown ids are parked until scanned |

`ItemTransform.flags`: bit 0 = **final** (at-rest pose, sent reliable; receiver
restores physics and sleeps the body), bit 1 = **driver** (sender's player sits
in this vehicle; driver claims beat proximity claims, ties broken by lowest
player id). A seated driver never releases on stillness — it keeps the vehicle
with ~2.5 Hz keepalives until the player leaves the seat, and receivers block
the vehicle's drive trigger while a remote driver holds it.

`TimeSync` semantics: guests jump their sun/cloud hour FSMs only when total
drift exceeds 10 game-minutes; forecast variables are overwritten on every
message (host's weather wins). Day-of-week is not yet synced.

### Reserved ranges

- 44–59 world events (consumption/destruction, switches) — M3
- 61–79 vehicles (attachment, fuel/damage) — M4/M5
- 80–99 economy (wallet transactions, shop intents) — M5
- 100–119 NPCs/jobs — M6
- 123–139 snapshot/bulk transfer control (save data) — M3+

Rules: never reuse a retired id; new fields are appended only together with a
protocol version bump (no silent format drift).
