# Our Winter Car — Multiplayer Mod for *My Winter Car*

A full co-op conversion of My Winter Car (MWC) with a standalone launcher.
One player **hosts** and owns the savefile; everyone else joins as guests.
Money, cars, parts, items, doors, NPCs, time, weather, body warmth and heating,
and all game-relevant events are synchronized.

**Design priorities (in order):** correctness/completeness → **stability** → ease of use → everything else.

---

## 1. Ground truth (what we know about the game)

*(updated after M0 recon against the real install, build 23268598)*

| Fact | Value | Consequence |
|---|---|---|
| Steam AppID | `4164420` | We can use the game's own Steam identity for lobbies/invites |
| Engine | **Unity 5.0** (verified: exe 5.0.0.6002871) — same generation as MSC | Legacy APIs only (no SceneManager, no `Application.buildGUID`); IMGUI fine |
| Scripting runtime | Legacy Mono, **.NET 3.5 profile** (mscorlib 3.0.40818) | All in-game assemblies target `net35`; no tuples/modern BCL in shared code |
| Gameplay logic | PlayMaker **1.7.7.6** (verified in Managed/) | Syncing = intercepting FSM events/variables, not C# method calls |
| Steam integration | **Steamworks.NET (classic API) embedded in `Assembly-CSharp-firstpass.dll`** + `CSteamworks.dll`; no modern SteamNetworkingSockets | We reference the game's own wrapper → shared callback dispatcher, same AppID session; transport uses classic `SteamNetworking` P2P |
| Mod loader standard | BepInEx 5 (`5.4.23.x`) x64, entrypoint set to `MonoBehaviour` | Our mod ships as a BepInEx plugin |
| Save format | ES2 / Easy Save 2 (`ES2.dll` verified; MSCEditor partially reads saves) | Host-side save manipulation is feasible; guests get sidecar profiles |
| Game status | Early Access, frequent updates | Sync catalog must be regenerable per game build; version pinning required |
| Prior art | MSCMP (dead, GPLv3), BeerMP → WreckMP (active, MSC only) | Proven concepts: host/client, rigidbody ownership, launcher + injection. MSCMP is license-compatible (also GPLv3); still prefer clean-room for correctness |

---

## 2. Architecture decision

### 2.1 Topology: host-authoritative listen server

The host's game instance is the single source of truth:

- Host owns the savefile, the world state, the economy, all NPC/AI simulation,
  time and weather.
- Guests are thin-ish clients: they simulate locally what they're interacting
  with (their character, the vehicle they drive, items in their hands) and
  receive everything else from the host.
- **No dedicated server.** A dedicated server would require re-hosting the
  entire PlayMaker world outside Unity — effectively reimplementing the game.
  Listen server is the only realistic option and also the easiest UX
  ("the friend with the save clicks Host").

Conflict resolution is always "host wins". Guests send *intents* (pick up item,
buy part, start engine); the host validates, applies, and broadcasts results.
For high-frequency physics (your own body, the car you're driving) guests get
**delegated ownership** so their inputs feel local and lag-free (see §4.3).

### 2.2 Transport: classic Steam P2P (connect-by-SteamID)

*(Revised after recon: the game's embedded Steamworks.NET is 2015-era and has
no `ISteamNetworkingSockets`. The classic `ISteamNetworking` P2P API keeps all
the properties we chose Steam for.)*

Use `SteamNetworking.SendP2PPacket` / `ReadP2PPacket` against the game's own
Steamworks wrapper:

- **Zero configuration** — no port forwarding, no IP sharing, no lobby-code
  copy/paste. This is the single biggest "ease of use" win.
- NAT traversal with automatic relay fallback through Steam's backbone;
  sessions are authenticated against SteamIDs for free.
- Reliable and unreliable send modes per packet, with native per-channel
  multiplexing (`nChannel`) — maps 1:1 onto our three logical channels.
- Because we use the **game's own** `Assembly-CSharp-firstpass.dll`, our
  callbacks share the game's Steam callback dispatcher and session — no
  double-init, no AppID tricks needed at all in-game.

### 2.3 Matchmaking & friends: Steam lobbies + Rich Presence

- Host creates a `ISteamMatchmaking` lobby (friends-only by default,
  invite-only and public as options).
- Invites flow through the **Steam overlay** (Shift+Tab → invite friend) and
  the friends list ("Join Game"), via lobby invites and the rich presence
  `connect` key.
- When a friend clicks Join, Steam launches MWC with `+connect_lobby <id>` —
  and since BepInEx is installed *into the game directory*, our mod loads and
  reads that argument **even if the launcher isn't running**. Joining a
  friend is literally one click in the Steam friends list.

### 2.4 The Steam "hack job" — how we use the game's AppID

The game already initializes Steamworks under its own AppID in-process. The
plan, in order of preference:

1. **Piggyback (primary):** the mod binds Steamworks.NET against the already
   loaded `steam_api64.dll` inside the game process. Calling `SteamAPI_Init`
   a second time in the same process is a safe no-op; we share the game's
   Steam session. Lobbies, P2P, invites and avatars all just work under
   AppID 4164420.
2. **Self-init (fallback):** if the game ever stops initializing Steam itself,
   the mod ships `steam_appid.txt` / sets the `SteamAppId` environment
   variable and initializes Steamworks on its own.
3. **Launcher-side Steam:** the launcher runs as a *separate* process and
   initializes Steamworks under AppID 4164420 via the `SteamAppId` env var
   trick — only for pre-game UX (friends list, who's hosting, accepting an
   invite before the game is booted). It releases its Steam session before
   the game starts to avoid two-process AppID contention. If this fights with
   Steam in practice, the launcher degrades gracefully: all Steam
   functionality lives in-game, the launcher becomes pure setup/update UI.

Risk note: using a game's AppID from a mod is the same gray zone every Steam
MP mod lives in (WreckMP, MSCMP, Lethal Company mods, etc.). No VAC concerns —
MWC has no VAC. We never touch the Steam client files; this is API usage only.

### 2.5 Why not alternatives

- **Raw UDP/ENet + manual hole punching:** worse NAT success rate, no relay,
  we'd own all the security/encryption — fails the ease-of-use bar.
- **Unity NGO / Mirror / Photon:** designed to be compiled *into* a game, not
  injected into a PlayMaker game we don't own; Photon adds third-party servers,
  costs and accounts.
- **Old `ISteamNetworking` (what MSCMP used):** deprecated; Sockets is the
  supported, more reliable API.

---

## 3. Components

```
our-winter-car/
├── src/
│   ├── WinterMP.Core/        # BepInEx 5 plugin — game hooks, sync subsystems
│   ├── WinterMP.Net/         # shared: protocol, serialization, channels (netstandard2.0)
│   ├── WinterMP.Launcher/    # .NET 8 desktop app (Avalonia or WPF)
│   └── WinterMP.Tools/       # FSM dumper, sync-catalog generator, save analyzer
├── catalog/                  # generated per-game-build sync descriptors (JSON)
├── protocol/                 # message schema definitions (source of truth)
├── docs/
└── PLAN.md
```

### 3.1 Launcher (`WinterMP.Launcher`)

Purpose: **installation, updates, hosting UX, diagnostics.** Not required for
joining (Steam "Join Game" works without it), which keeps the critical path
simple.

Features:

- Detect MWC install (Steam library parsing), detect game build/version.
- One-click install/repair of BepInEx 5 + the mod (incl. the
  `[Preloader.Entrypoint] Type = MonoBehaviour` config fix MWC needs).
- Auto-update of the mod with channel selection (stable/beta); refuses to
  launch on game-build/mod-version mismatch with a clear message.
- **Host flow:** "Host game" → automatic save backup (timestamped, with
  one-click restore) → launch game with `-wintermp host [friends|invite|public]`.
- **Join flow:** friends-who-are-hosting list (when Steam-in-launcher works),
  or just "your friend invites you via Steam overlay".
- Diagnostics: collects BepInEx + mod logs, connection quality stats, and
  bundles them into a shareable zip for bug reports.
- Tech: .NET 8, Avalonia (or WPF if Windows-only is acceptable — MWC is
  Windows-only, so WPF is fine and simpler). Facepunch.Steamworks for the
  optional launcher-side Steam session.

### 3.2 Core mod (`WinterMP.Core`)

BepInEx 5 plugin targeting the game's Mono/.NET profile (Steamworks.NET for
in-game Steam, HarmonyX for patching — both proven inside BepInEx).

Layers, bottom-up:

1. **Steam layer** — session piggyback, lobby lifecycle, connection management,
   `+connect_lobby` boot handling.
2. **Transport layer** — channels over Steam Sockets:
   - `Reliable-Ordered`: events, RPCs, FSM transitions, economy, chat.
   - `Unreliable-Sequenced`: transforms (players, vehicles, active rigidbodies).
   - `Reliable-Bulk`: join snapshot, save data, large transfers (chunked).
3. **Replication layer** — object identity, ownership, snapshots, delta state.
4. **Game-sync subsystems** (§4) — the actual MWC integration.

### 3.3 Tooling (`WinterMP.Tools`) — built FIRST

Because MWC is PlayMaker-driven and Early Access (changes every patch), we do
not hand-write hooks against one build. We build tools that generate a
**sync catalog** from the game itself:

- **FSM dumper:** in-game tool that walks every `PlayMakerFSM`, dumps states,
  events, transitions and variables to JSON.
- **Object catalog generator:** enumerates every persistent/pickable object,
  assigns deterministic IDs (stable scene-path hash + save-key cross-reference).
- **Catalog diff:** when a game update lands, diff old vs new dump → exactly
  see what broke, regenerate, re-test. This is the EA survival strategy.
- **Save analyzer:** maps the savefile keys to world objects so host saves and
  join snapshots agree.

---

## 4. Synchronization model (the heart of the project)

### 4.1 Object identity

Every synced thing gets a stable 32-bit network ID:

- **Static world objects** (doors, switches, shop shelves, building FSMs):
  hash of full scene path, validated by the catalog at startup.
- **Dynamic/spawned objects** (purchased parts, consumables): host-assigned
  ID at spawn time, broadcast with a spawn message that includes the prefab
  catalog reference.
- *Empirically validated against a real GAME-scene dump (8641 FSMs, 1956
  rigidbodies): path-hash is unique for static objects; all 302 colliding IDs
  came from duplicated `*(itemx)` item clones and `*(VINXX)` part bolts —
  exactly the dynamic class that gets host-assigned IDs instead.*
- A startup **handshake checksum** of the catalog ensures host and guests run
  identical game builds + mod versions; mismatch = clean refusal, not desync.

### 4.2 PlayMaker FSM sync — the generic engine

Nearly all MWC gameplay is FSM state changes. Strategy:

- A Harmony patch layer on PlayMaker's `Fsm.Event` / state-enter intercepts
  transitions on *cataloged* FSMs.
- Each cataloged FSM has a **sync descriptor** (generated + hand-curated):
  - `mode: host-only` — only the host runs it (NPC AI, economy ticks, weather);
    guests receive resulting state/variable updates and have local execution
    suppressed.
  - `mode: anyone-triggers` — any player can fire it (door open, light switch,
    pump fuel); the trigger is sent as an intent, the host validates and
    rebroadcasts the transition; everyone replays it.
  - `mode: owner-only` — runs on whoever owns the parent object (vehicle
    engine FSMs run on the driver's machine).
  - `synced variables` — list of FsmFloat/Int/Bool/String variables mirrored
    (e.g. fuel level, wear values, dirt, temperatures), with per-variable rate
    and threshold.
- Curated descriptors live in `catalog/` per game build. Curation is the bulk
  of the project's long-tail work; tooling makes it tractable.

### 4.3 Physics & ownership

- Every synced rigidbody has exactly **one simulating owner** at a time;
  everyone else interpolates (snapshot interpolation with ~100–150 ms buffer,
  velocity-extrapolated).
- Default owner: host. Ownership transfers to a guest when they grab/enter/
  touch-dominate an object (WreckMP-proven model). Host arbitrates conflicts
  (two players grabbing the same part → first intent wins, loser gets a
  correction).
- Vehicles: the **driver owns the whole vehicle** (chassis + powertrain FSM +
  attached parts as one ownership unit). Passengers are parented locally.
  Collisions between two player-owned cars: the host's physics result is
  authoritative; brief visual divergence is acceptable, positions reconcile.
- Sleeping objects send nothing. With thousands of pickables (~200 car parts
  alone), only *moving + owned* objects stream transforms; everything else is
  event-synced (picked up / attached / dropped at X).

### 4.4 Subsystem checklist

Status: ✅ done · 🚧 partial · ⬜ not started. Target milestone in parens.

| Subsystem | Sync model | Status |
|---|---|---|
| Player avatars | Custom rig (head/hands/body) streamed ~20 Hz, name tags. Remote players *visual only* (no physics pushing) v1. **v53 sanitizes each received pose before relay/storage** (finite bounded coordinates, plausible quaternion, defined move flags), which also protects the fresh-pose proximity proof used by guest intents. | ✅ |
| Player animation | Derived state machine (walk/run/crouch/carry/drive) — low bandwidth | ✅ |
| Player needs (hunger/fatigue/thirst/urine) | Per-player, reported to host every ~12 s, saved in `wintermp-guests.json` sidecar. **Stress + Drunk appended at v30** (`Stress` global; `DrunkCurrent` on the FPS-camera "Drunk Mode" FSM) — same report/sidecar/rejoin-restore path, so a rejoining guest keeps intoxication instead of resetting sober. **Dirtiness appended at v55** (`PlayerDirtiness` global) follows the same authenticated report/profile/rejoin path; legacy sidecars leave it untouched rather than falsely restoring clean | 🚧 (needs two-player/soak validation; rest ✅) |
| **Body temperature / cold** | Per-player **5th need** (`BodyTemp`); reported to host + sidecar like other needs. Ambient temp shared via `TimeSync`; `ColdArea`/`ColdMultiplier` are position-derived (computed locally from the same world). Synced as the 5th need at **v28** (`PLAYER/BodyTemp.Temperature` → host + sidecar, restored on rejoin) — see §4.8 | 🚧 (v28, not soak-tested) |
| **Clothing** | Per-player `ClothingStage`/`ClothingType` (`CLOTHESHOME`/`CLOTHESWORK`) — drives insulation (warmth math) **and** the remote-avatar visual. Synced at **v28** (`PlayerClothingState`: `ClothingStage`+`ClothingType`, owner-authoritative + host-relayed; avatar shirt tint best-effort). Join-sync closed: the host bursts every already-connected player's current outfit to a joining guest (clothing is change-only, so without it a joiner saw everyone in default clothing/warmth tier) | 🚧 (v28, not soak-tested) |
| Text / voice chat | Text chat done; positional voice via Steam Voice later | 🚧 (M11) |
| Money/economy | **Shared cash and bank balances** owned by host. Purchases use validated intents. **v90** corrects the cash binding to `PlayerMoney`, adds bank/income snapshots and acknowledged ATM transfers. **v92** replaces inactive-host slot replay with host ledgers, leased controls, separate credit/winnings and seeded native reels; timeout/disconnect settle once. **v93** adds VideoPoker with private host decks, holds/redraws, doubling and acknowledged settlements using native presentation. **v94** adds revisioned debt-letter quotes and acknowledged cash settlement, including an inactive host sheet and relocated mailbox. **v97** mirrors host Ventti property keys and cabin access to guests/joiners without replaying wager actions; teardown restores the guest's original access. **v98** sends full-width Ventti stakes/hand totals and native result observations, retaining state across late bindings and restoring guest variables on teardown. **v99** connects the Ventti engine to acknowledged host commands, escrow, private decks, native card meshes and once-per-round native property outcomes with duplicate accounting removed. Native host save handling stays active and guest controls cannot debit or draw independently. Static evidence, Core builds and protocol tests pass; two-player LOD, save/teardown, property and NPC presentation checks remain in the coverage roadmap. | 🚧 (two-player economy verification pending) |
| Shops & cash registers | `anyone-triggers` purchase intents; host executes, spawns goods, applies money | ✅ |
| **Classifieds parts ordering** | Magazine listings (`JOBS/ADs` advert pile) + their **periodic refresh** are host-authoritative shared state (synced RNG/seed) — else peers see different parts for sale. Dialing a `CARPARTS/PARTSYSTEM/PhoneNumbers/*` seller now completes through the normal host purchase path: v37 captures the guest's exact populated `OrderAMIS`/`OrderYP` record before local payment, authenticates and pairs its next monotonic record sequence to that same guest's next `PAYMENT` intent, then makes the host spawn the selected mailed delivery. v33 mirrors `JOBS/ADs` scalar job state and Marketti issue/layout; v36 mirrors complete pending-order data to observers and joiners. Runtime listing generation is still local presentation, but it can no longer cause the host to deliver a different selected part. *The in-game computer is an MSC-import toy — not this; parity backlog* | 🚧 (M8, v37 needs two-player runtime confirmation) |
| **Jobs / flea market** | Firewood delivery (+ tractor wood-splitter PTO), sewage, factory punch-clock shift (`JOBS/FACTORY` TimeClock), flea market. Accept/progress/payout host-validated; reward → shared wallet. v33 forwards factory punch-in/out to the host and mirrors factory employment/package/paycheck state. v34 forwards sewage/firewood customer payout clicks to the host and mirrors every house's active-order + level/surplus state. v35 additionally mirrors the GIFU tank/pump/hose state for snapshot and observer convergence; the KEKMET dashboard PTO switch and GIFU dump lever now use reliable cataloged control replay. Flea **buying** uses the generic host purchase pipeline and its MoneyFlea envelope collection is catalog-routed to the host; flea **selling** is now host-owned via `FleaSaleSync` (v64: host runs the day-timed sale RNG + broadcasts proceeds/rent, guests suppress their local Sell FSM). Taxi job (`TaxiJobState` v65), kilju fermentation (`BrewState` v66), farm job (`JobSiteState` kind 4 v67), Kela welfare (`WelfareState` v68), hitchhiker (`HitchhikerState` v69) are also host-owned now. Phone-order acceptance is now paired to the caller’s next payment intent. Guest-operated hose, cutter attachment, and flea-table item placement remain deliberately open: their local FSMs carry dynamic picked-object/implement references, so generic remote replay would be unsafe; they need dedicated validated intents. | 🚧 (M8, v37 partial) |
| Car assembly (bolts/parts) | Persistent native part IDs; v114 guest bolt intents execute on host and return absolute save-array, pose and parent-tightness results. Bolt-settle wear synced; `VehicleDamage` v91 carries current fitted engine-part wear and concrete failure outcomes, clears repaired parts and gates non-owner random damage. Parked cars use the host. Drivetrain wear + tire pressure/puncture via `VehicleCondition` (v61), gear via `VehicleState` (v63). | 🚧 (missing fitted guest graphs, separate adjustments and two-player/save verification remain open) |
| Vehicle state (Sorbet, Corris, +) | Engine `owner-only` (driver owns whole vehicle); rpm/fuel/coolant/lights/blinkers + cabin **climate** (frost/fog/defrost/heater) synced. Host relays only streams whose owner/player id matches the authenticated sending peer (also enforced for item/cargo/player/passenger/clothing streams), closing spoofed-owner writes before world state is touched. **v52 also makes passenger seating host-validated:** a guest's next sequence must describe an exact discovered seat within 2 m of its fresh pose; exits require vehicle id 0, the host owns occupancy, and same-seat races resolve by lowest player id. **v95** preserves accepted seats across moving-car keepalives without repeating entry proximity checks; rejected new claims clear occupancy for every peer and join snapshot, while stale requests are ignored. | ✅ |
| **Fuel / jerrycan / pumps** | Refuel as `anyone-triggers` intent; fuel level already rides in `VehicleState`. v33 additionally syncs tracked jerrycan/container `FuelLevel` + pouring state under transform ownership and applies remote vehicle fuel to the actual tank (not just its gauge). The Peräpörtti fuel-station monitor presets/pump selection are cataloged reliable controls and its cash trigger is a host purchase intent, so payment and wallet changes converge. **v39 closes live nozzle use for parked vehicles:** the guest reports only an actively dispensing local nozzle's target tank level; the host requires fresh player/nozzle/vehicle proximity, a stationary target, monotonic bounded growth, then writes its real tank and reconciles all peers with `VehicleState`. Dynamic hand/pistol references are still never replayed. | 🚧 (M8, v39 needs two-player runtime confirmation) |
| World items (pickables / cargo / consumables) | Event-synced motion/cargo/despawn. **Shopping bags v120:** persistent host factory/native IDs, isolated guest bag views, atomic one/all opening requests (190–192), host-only native inventory consumption, direct factory-output capture and chunked ItemSpawn manifests. Replica name lookup uses native prefab display-name actions; missing items retry rather than waiting for another player to spill. Guest offers53 retired. Bag pickup guards/release reconcile hand ownership. Native part products without an adapter are guarded before opening. | 🚧 (implemented; two-player acceptance pending) |
| Doors / switches / controls | `anyone-triggers` events (incl. lights, wipers, hazards, handbrake) | ✅ |
| **Home heating & cooking** | Cabin woodstove (`CABIN/Cabin/woodstove/Fireplace`: `SetFire`/`WoodTrigger`/`SausageTrigger`), sauna kiuas (`StoveHeat`/`SaunaStove`), cottage/living-room fireplaces. Host-owned progression (lit, fuel, heat output, sauna temp); feed/light/grill = `anyone-triggers`. Synced at **v28** (`HeatSourceState`/`HeatSourceIntent`: woodstove/sauna/fireplaces; host reads authoritative signals + broadcasts, guests apply locally, light/feed/grill/löyly intents fire real game events on the host). **v49 binds each guest action to an authenticated player id + monotonic sequence and requires that player’s fresh pose within 8 m of the exact source**, so remote peers cannot feed/light/grill/steam a distant home. Joining guests get a forced full re-broadcast with the world snapshot (no 20 s cold wait). See §4.8 | 🚧 (v49, not soak-tested) |
| **Home appliances** | TV (`TVSwitch`), house/apartment lights and fuse main switches are cataloged `anyone-triggers`; v40 additionally catalogs fridge doors and the home-stereo radio/CD power switch. **v42 makes the home stereo's power, radio channel, volume and bass host-owned scalar state**: guests submit bounded settings only while near the stereo, and the host rebroadcasts/snapshots the applied values. **v51 mirrors the grounded home, yard, and apartment shower tap/valve transitions**, so the shared fixture state agrees before the game’s local hygiene logic runs. **v55 preserves each guest's resulting `PlayerDirtiness` across reconnects when they choose their saved return position**; it does not replay hygiene actions. CD track/disc, appliance consumption, individual fuse insertion, and full hygiene/wash interaction authority remain to do. | 🚧 (M11, v55 partial; needs two-player runtime confirmation) |
| NPCs & traffic | `host-only` sim; transform + FSM streaming with distance-based rates | ✅ |
| **Police / cops** | v41 turns a guest's local checkpoint result into a host-validated shared fine record: the host requires authenticated fresh player pose, exact known checkpoint, nearby delegated vehicle, monotonic sequence and bounded fine before it updates Fines and all peers. Fine payment routes through the existing host purchase guard/shared wallet. Arrest and impound still need dedicated host-owned flows. | 🚧 (M9, v41 partial; needs two-player checkpoint validation) |
| **Animals / moose** | Host-sim AI + authoritative collision. **Position sync shipped (no wire change)**: host streams the moose root transform over `NpcTransform` (`ScriptedMover` path in `NpcTrafficSync` — the moose has no live root rigidbody); guests freeze the local `Move` AI FSM so per-peer RNG stops desyncing its position, restore on stream end/final. **The collision→dead transition is now synced** via `NpcTransform.FlagDead` (v77): the host broadcasts the death edge and guests activate their own corpse ragdoll. Moose-meat spawn (a `SPAWNITEM` spawner) is the documented residual of the 7.2 spawner audit — real-time materialization on connected peers needs a SPAWNITEM→ItemSpawn hook (late-join covers it via the item snapshot). | 🚧 (M9 position + death edge ✅; meat spawn = SPAWNITEM residual) |
| **Inspection / registration (katsastus)** | Inspection order/payment already use host purchase handling. v38 mirrors the host-evaluated standard inspection record: every pass/failure checkmark, stamp/museum flags, and next-inspection renewal data are applied to peers and joiners. v40 additionally mirrors host-generated standard/museum registration plate text plus the matching physical plate-pair availability, so peers never reroll registration IDs or disagree about displayed plates. Individual plate pickup/installation still travels through the existing item/part state path and needs two-player validation. | 🚧 (M9, v40 partial) |
| **Racing (Suvi-Sprint, Ice Rally)** | v43 adds host-validated Suvi-Sprint stage progress; v44 adds ice-track marker/lap authority; v45 mirrors host grid/heat configuration; **v47 also mirrors host-owned lineup registration/race stage**. v46 mirrors the six host-generated ice-race result rows (name/number/model/UA) to all guests and join snapshots, closing divergent leaderboard presentation. The sixteen `RACES/ICERACE/Cars*/Disable/*` AI opponents now stream from the host through `NpcTransform`; guests freeze their local navigation/drive FSMs while the host stream is live. **v50 routes the exact Suvi-Sprint and ice-race price triggers through the host purchase gate**, so prize collection and the shared wallet converge instead of each peer executing the opaque action locally. The **rally** `ResultsWeekend` scoring/reward ledger, `RegisterRally` enroll, and `ParcFerme` penalty are now host-owned via `RallyResultsState` (v73); the **JOKKIS** banger-race lap/time/checkpoint is host-broadcast via `JokkisRaceState` (v74); the rally `PartsSalesman` vendor is catalogued (v72 catalog). Two-player runtime confirmation remains. | 🚧 (M10, all racing slices landed; needs two-player runtime confirmation) |
| Time/weather/calendar | Host clock is law; guests slave FSM time vars; periodic hard correction | ✅ |
| Sleeping / time skip | Consent: all players confirm → host advances time | ✅ |
| Death / respawn / permadeath | Per-player death (hypothermia/drown/fire/electrocute/…); world keeps running; respawn replicated. Full cause table since v32 (sewage/carbon-monoxide/PTO/cutter-blade/jail/piss-TV/burn/smoking — the death FSM's remaining bools); permadeath flag sync re-arms between sessions and retries outside scene changes (loopback); respawn-watch timeout self-heals instead of wedging future death reports | ✅ |
| Computer (toy) | MSC-save-import only, non-core — **optional parity, post-1.0** | ⬜ (backlog) |

### 4.5 Join-in-progress & reconnection

- Guest connects → version/catalog handshake → host serializes a **full world
  snapshot** (object positions, FSM states, synced variables, economy, time) →
  compressed chunked transfer over the bulk channel → guest applies behind a
  loading screen → switches to live delta stream.
- Disconnected guests can rejoin a running session; their player profile
  (position, inventory, needs) is retained host-side for the session and in
  the save sidecar.
- Host quits = session ends (clean "host ended session" UX for guests, with
  autosave). Host migration is explicitly **out of scope** — it would require
  transferring full world authority and the savefile mid-session.

### 4.6 Saving

- **Only the host saves**, through the vanilla save flow plus a
  `wintermp-guests.json` sidecar storing each guest's SteamID → profile
  (position, inventory, clothing, needs, personal stats).
- Launcher backs up the save before every hosted session and keeps N rotations.
- Guests joining a save they've been in before resume their profile; new
  guests spawn at a defined spawn point with starter state.
- **Implemented after 0.1.32:** Core installs ES2 persistence guards before game
  loading and requires all nine bindings before guest admission. Guest launch/join
  protects saves, temporary files, renames and deletions for the rest of the process,
  including disconnect, session reset and quit. Host and solo boots retain normal
  saves; ordinary PlayerPrefs settings and memory serialization remain available.
  Returning to a personal world or hosting after joining currently requires a game
  restart. The overlay explains this; the guest-profile sidecar also requires host
  authority. An isolated Unity/Wine probe passes 21 checks against build 23268598's
  ES2 library. Full in-world save/quit/permadeath and two-player checks remain pending.

### 4.7 Stability engineering (explicit, budgeted work — not an afterthought)

- **Desync detection:** periodic lightweight checksums over critical state
  groups (economy, part attachment table, door states). On mismatch → targeted
  **soft resync** of that object group from host (no session restart).
  **v96 (unreleased)** adds parked-car concrete damage and tire/drivetrain condition,
  using live fitted-part reads to clear repaired failures and stable tire-pressure
  rounding. Active ownership, RPM, climate and continuous wear stay excluded to avoid
  sampling-driven mismatch loops. Unit coverage is in place; R2.4b's two-player
  convergence/no-repeat-resync check remains a shipping gate.
- **Self-healing streams:** object-level "request full state" path any client
  can invoke when it sees impossible data.
- **Connection quality:** Steam Sockets stats surfaced in the TAB overlay
  (ping, loss, relay vs direct); auto-pause of physics ownership transfers on
  bad links.
- **Bandwidth budget:** `NetTrafficMeter` counts egress per channel + ingress at
  the send/receive chokepoints; 10 s rolling rate in the TAB overlay and the
  log, warns when steady-state exceeds the M7 budget (64 kB/s/client).
- **Crash containment:** all mod callbacks wrapped; an exception in one sync
  subsystem logs + disables that subsystem rather than killing the game.
- **Telemetry-in-logs:** structured log lines for every intent/transition,
  ring-buffered, dumped on error — feeds the launcher's bug-report zip.

### 4.8 Winter survival & heating (the defining loop)

Body temperature is MWC's most lethal mechanic, yet today it is **only synced at
death** (hypothermia is a `DeathCause`) — the need itself and every heat source
are unsynced. For co-op that is a *correctness* problem, not just missing
content: guests freeze on independent local clocks and the host cannot persist
guest warmth. Folded into the M7 gate:

- **Body temp = the 5th need.** Extend `PlayerNeedsSync` + the
  `wintermp-guests.json` sidecar with `BodyTemp`. Each player stays authoritative
  over their own body temp (like hunger/thirst) and reports it to the host, which
  saves and restores it on rejoin (mirrors the `GuestSpawn` needs restore).
  Ambient temperature is already shared via `TimeSync` forecast temps;
  `ColdArea`/`ColdMultiplier` are position-derived, so each client computes them
  locally from the same world — no new stream beyond the need value.
- **Clothing** (`ClothingStage`/`ClothingType`, `CLOTHESHOME`/`CLOTHESWORK`):
  per-player synced state driving both insulation (warmth math) and the
  remote-avatar visual (other players wear the right outfit).
- **Heat sources are shared world state.** The cabin woodstove
  (`SetFire`/`WoodTrigger`/`SausageTrigger`), the sauna kiuas
  (`StoveHeat`/`SaunaStove`) and the cottage/living-room fireplaces become
  host-owned progressions (lit?, fuel level, heat output, sauna temp). Feeding
  wood / lighting / grilling are `anyone-triggers` intents; the host advances the
  burn and broadcasts heat output so everyone warms — and cooks — off the same
  fire. Warm cars are already covered (cabin temp in `VehicleClimate`).

Net effect: "freeze in the same lake, thaw at the same sauna" becomes actually
consistent across peers.

---

## 5. Milestones

**Status (2026-07-19).** The original M0–M6 are substantially landed at **protocol
v25** (shipped through v0.1.24): transport/Steam/launcher, players + avatars +
needs, generic FSM world sync, items/cargo/parts/bolts, vehicles (Sorbet + Corris
incl. climate), shared wallet + shops + orders, time/weather, NPC traffic,
sleep/death/permadeath, join snapshot + checksums/soft-resync. Launcher
install/update/backup/diagnostics (the original M8) is largely done — now
cross-platform Avalonia (Windows installer + Linux AppImage). Since then the
**feature work for M7–M10 has landed on main at protocol v55** (unreleased):
winter survival (BodyTemp/clothing/heat sources/stress/drunk/dirtiness), the
host-authoritative bag-spawn manifest rework, jobs & economy (classifieds, mail
orders, sewage/firewood/factory job sites, guarded refueling), hazards (police
fines, moose streaming, inspection/registration), racing (rally + ice-race
lifecycle incl. event grid and results), radiator thermostats, and session
admission hardening. The wire/unit gate is green (149 protocol tests incl. a
reflection round-trip over every registered message), but **no M7+ exit
criterion is met yet** — all of it awaits real 2-player/soak verification,
which is the current bottleneck. Beyond that verification pass, what remains is
the gameplay **long tail** + M11 polish, re-scoped below.

**Target: v1.0 = pragmatic-complete co-op.** Full parity (computer toy, exhaustive
FSM coverage, full race-grid fidelity) is an explicit *post-1.0* backlog, not a
v1.0 blocker. Stability is the top priority — M7 gates everything after it.

| # | Milestone | Contents | Exit criteria |
|---|---|---|---|
| **M0–M6** | ✅ Foundation | Tooling, transport, players, world FSM sync, vehicles, building & economy, NPCs, sleep/death — see §4.4 | Shipped: protocol v25 / v0.1.24 |
| **M7** | **Stability & winter-survival parity** *(gate)* — 🚧 *landed on main (→v55), verification pending* | Land the security/correctness backlog (`.car-sync-*` findings); **`BodyTemp` as the 5th synced need** + clothing (warmth + visual); **home heating & cooking** as host-owned progression (§4.8); bandwidth budget (< 64 kB/s/client steady); reconnection hardening; multi-hour soak | 4-player multi-hour winter session: zero hard desyncs **and** zero hypothermia/heat divergence across 10 soak runs |
| **M8** | Jobs & economy depth — 🚧 *landed on main (→v55), verification pending* | **Classifieds ordering** (listings + periodic refresh host-authoritative; phone-dial intents; mailed delivery); **jobs** (firewood delivery + tractor wood-splitter PTO, sewage, factory punch-clock); fuel/jerrycan; flea market | Two players run a full work loop — order parts by phone, earn from a job — money **and** listings always consistent |
| **M9** | World hazards — 🚧 *police/moose/inspection landed, verification pending* | Police (speeding/DUI → fines to shared wallet, arrest/impound); moose/animal collisions + host AI; vehicle inspection/registration | A guest can be ticketed, hit a moose, and pass inspection — all consistent across peers |
| **M10** | Co-op racing — 🚧 *rally + ice-race lifecycle landed, verification pending* | Suvi-Sprint rally + Ice Track Rally: enroll/grid/start/lap-timing/finish/payout; opponent streaming; frozen-lake ice track | Two players race the ice rally together with consistent standings + payout |
| **M11** | Beta & polish — ⬜ *(home stereo + dirtiness already landed)* | Positional voice chat; home appliances (TV/radio/fuses); hygiene/dirt/wash; docs; public beta; Nexus release | Non-technical user: download → playing with a friend in < 5 min |
| — | Parity backlog *(post-1.0)* | Computer toy (MSC-import), full race-grid fidelity, exhaustive long-tail FSM curation | As demanded, per game build |

Ship order: **M7 is the gate** — stability and survival-parity before new content.
After **M8** the cooperative work loop is complete (build the Corris, earn money,
survive winter); **M9–M10** add the headline winter content; **M11** ships it.

**Gameplay long-tail + parity backlog → `docs/COVERAGE-ROADMAP.md`.** A 2026-07-21
full-coverage audit (every game FSM vs. the sync surface) found ~30 shared-state systems
still unsynced — most never listed here (gambling, utility bills/blackout, repair-shop
service results, engine/tire wear, wanted/jail, side jobs, the moose death→meat chain).
The roadmap decomposed them into prioritized, self-contained, AI-iterable tasks.
**The first pass implemented all 35 roadmap tasks (protocol v56→v80), but later
audits reopened gaps; see roadmap §1b for the current unfinished work.** That pass
covered economy integrity
(gambling/Ventti/bills/lottery), shared-car integrity (breakage/wear/tires/Fleetari/gear),
income & jobs (flea/taxi/kilju/hitchhiker/farm/welfare), crime & consequence
(wanted/jail/impound/pursuit), racing (rally results/enroll + JOKKIS + parts), home &
survival (oven/sauna/phone/appliances) and player polish (BAC/winter-garment/piss/car-radio).
Documented residuals: the SPAWNITEM spawner manifest (moose meat / parts / trophies real-time
materialization on connected peers — late-join covered) and various avatar/visual details.
**Everything below still needs a two-player playtest** — none of it is runtime-verified.
Keep the §4.4 statuses in sync with the roadmap as playtests confirm each slice.

**Lotto follow-up (v103, unreleased):** the complete v102 draw now has dedicated
host-issued tickets, captured paid rows and acknowledged native cash/bank claims.
Host ticket IDs/save handling persist outstanding tickets; guest replicas use the
normal item motion path. Lotto save/LOD/two-player validation and bank statement/
achievement presentation remain open, as does the separate Megaveto transaction
ledger. R2.21 and roadmap 1.4 are therefore still incomplete.

**Hockey follow-up (v104, unreleased):** the earlier one-match scalar stream did
not contain Megaveto's actual six odds tables or result lists. Message 160 now
includes those collections, upcoming/previous pairings, scores, games played and
standings text. Guests preserve and pause their season/odds FSMs, apply complete
boards and restore local data on disconnect. Runtime round/teletext/reconnect
validation, individual-player scoring and Megaveto ticket transactions remain open.

**Guest alternator hand adjustment (shipped in 0.1.33; introduced in protocol 119):** empty-handed
guests can scroll at either catalogued alternator's pivot after loosening its
adjusting bolt. New part operations 2/3 share fitting/removal's immutable receipt
ledger; the host checks the observed revision, settled mount, fresh proximity,
native bolt/hand readiness and cooldown before executing one native half-degree
turn within 0–7 degrees. Both saved part and engine mount must receive the result;
guests receive the existing absolute scalar/pivot update without native HandRotate
execution. Catalog bindings and native actions are validated; 1,036 protocol/policy
tests, 18 launcher tests and 46 isolated Unity/Wine checks pass. Real two-player
scroll/tool/fit/reconnect checks and the remaining engine adjustments are pending.

**Guest part/mount isolation (shipped in 0.1.33; no protocol change):** saved
originals from the 30 boxed replacement families are retained in an inactive scene
group before guest FSM/item registration. Their identities are released for host
copies, including matching native IDs; settled occupied mounts are paused without
rewriting vanilla references. Fitting previews use accepted host attachment state.
Passive PartState views preserve host checksum observations without replaying native
actions or modifying saved originals. Disconnect cleanup destroys owned copies,
restores original transforms/physics and resumes FSMs without initialization replay.
Unexpected nested assemblies or unrelated subsystems remain preserved and defer.
The catalogued leaf structure is verified against all 30 game prefabs; 1,014
protocol/catalog/policy tests and 28 isolated Unity/Wine checks (7 restoration,
21 save-library) pass. Full multiplayer placement, native engine
references and save/LOD/reconnect acceptance remain pending; see BUILDING.md.

**Guest replacement bolt controls (shipped in 0.1.33; no protocol change):**
owned copies now bind the native spanner/ratchet interface for integer-step bolts.
Their input-only graphs send existing host intents and apply absolute host replies;
no guest native bolt delta, BOLTING, alternator or timing branch is enabled. Tool
triggers require a fitted attachment and a fresh host bolt observation after every
attachment change. Deferred replacement scalars share bolt receipt ordering to avoid
rolling back the latest parent total. Static game evidence validates 55 controls
across 24 replacement families; 1,008 protocol/catalog tests pass and Core/Net builds
are clean. Native multiplayer acceptance remains pending. Remaining priorities are
multiplayer mount validation, continuous adjustments/full engine behavior and non-box
part creation; see the new BUILDING.md checklist.

**Guest multi-slot fitting (v118, shipped in 0.1.32):** pistons, main bearings and rockers
now use the game's native array-slot installer for guest requests. A click includes
the observed slot index; retries cannot change it, and host selection of another
slot rejects the click. Selection preserves native nearest-slot and tie behavior,
including occupied slots. The host guards native selection and prerequisite checks,
then confirms the same slot in the same frame. Unconfirmed previews cancel
immediately; committed installs wait for the exact slot's settled attachment.
These families also no longer fail replacement creation for lacking the fixed-part
Installed scratch flag; their native AssemblyID supplies assembly state.
Static evidence covers all 17 slots across the three families. 983 protocol/catalog
and 18 launcher tests pass; Core and both Net targets build cleanly. Native two-player/save verification, guest
mount/save isolation, operational replica bolt/engine graphs and non-box part
creation remain unfinished.

**Guest replacement removal (v117, unreleased):** fitted guest-created replacement
copies can request removal with the normal right click while using empty hands.
The host validates the observed revision, fresh living-player proximity, native
tightness, collider availability and actual mount ownership, then runs native
Remove/REMOVE once. Settled loose state precedes the acknowledged outcome; retries
and stale clicks cannot remove a later refit. Guest selection reads the disabled
native box geometry without enabling physics or local assembly/save actions.
Extracted bindings validate all 30 replacement families, including fitted pistons,
main bearings and rockers. 960 protocol/catalog and 18 launcher tests pass; Core
and both Net targets build cleanly. v118 adds multi-slot installation;
guest mount/save isolation, operational replica bolts/engine effects and native
two-player/save verification remain open.

**Guest replacement fitting (v116, unreleased):** save-isolated loose guest copies
can request installation with the normal left click at a fixed fitting point.
The host checks the observed revision, living/fresh player proximity, item ownership,
mount occupancy and native tolerance, then traverses the native ASSEMBLING checks.
Acknowledged requests cannot repeat a native install on retry. State 185 publishes
the settled fitted result; guest installation/save/bolt actions remain disabled.
Generic guest replacement-part install/remove state replay is now refused.
930 protocol/catalog and 18 launcher tests pass. Extracted native bindings validate
27 fixed-mount families; pistons, main bearings
and rockers use v118's installation slot selection. v117 adds guest removal; occupied guest-mount isolation,
operational replica bolts/engine effects and two-player/save validation remain open.

**Fitted replacement presentation (v115, unreleased):** missing boxed-content copies
can now attach to a ready, unoccupied guest mount using host-observed parent identity
and relative pose/scale. They follow the parent hierarchy, leave item/cargo physics
while fitted, and restore loose tracking and current host pose on removal. Missing,
inactive or occupied parents defer; existing guest-save occupants are preserved.
Retirement/reconnect detach owned copies before deletion. Parent changes participate
in revision checks; parent world movement does not cause reliable part broadcasts.
896 protocol/catalog and 18 launcher tests pass; Core and both Net targets build
cleanly. Native references/actions were inspected for all 30 replacement factory
families, including the dynamic piston/bearing/rocker mounts. This is presentation
only: new copies still have no native installation/bolt authority or engine effects.
Full guest mounting, original save isolation, non-box parts and two-player tests
remain unfinished.

**Native bolt reconciliation (v114, unreleased):** guest turns execute on the host;
guest scalar reports only request the host's settled result. BoltState/bolt snapshots
now include the parent tightness total. Guests restore the native integer save array,
correctly scaled bolt position and absolute total without adding another turn.
Zero bolts are included in join/resync; delayed sibling/part updates preserve the
latest parent total. Derived Bolted/Unbolted/Stop states no longer bypass authority.
Identity detection covers all 192 native Data prefabs, including 36 without the
optional Installed scratch bool. Native action/array inspection validates 489
ordinary bolt bindings; MUDFLAPa0 needs a runtime ThisPart reference, and three
continuous drain/alignment controls need separate adapters. Extra crank-pulley and
camshaft timing adjustments are blocked for guests, while normal turns are supported.
872 protocol/catalog tests and 18 launcher tests pass; Core builds cleanly against
installed game DLLs. Full guest fitting, missing mounted graphs, timing/adjustment
replication, original guest-save isolation and two-player/save checks remain open.

**Native fitting lifetime (v113, unreleased):** fitting destroys a part's Rigidbody,
while Data and its save identity survive. The item layer now distinguishes that
transition from disposal, discovers saved fitted Data objects, and rebinds the new
body on removal. Fitted/transitioning parts cannot accept item movement or cargo
pins. Replacement state remains available without a body, including targeted
resync; its Installed flag now derives from AssemblyID rather than the native
occupancy-query scratch bool. Full guest mount/bolt reconstruction is still open.
Core builds cleanly; 838 protocol/catalog and 18 launcher tests pass. Native action
inspection confirms the alternator mount destroys/recreates Rigidbody and all 30
replacement Data FSMs use positive AssemblyID for fitting and reset it on removal.
Two-player fitting/removal/save checks have not run.

**Guest box opening (v112, unreleased):** guests can request an opening on all 30
standard parts boxes. The host checks the authenticated player, fresh proximity,
current box revision/quantity, item ownership and a ready one-output contents
factory. One native opening is reserved at a time; host clicks are guarded too.
Repeated requests return the original outcome. Acceptance requires one native
quantity decrement and the exact next part identity after initialization; receipts
replay current box/part state or retirement. Guest quantities come from host state,
and opening audio plays once on acceptance. Core builds cleanly; 822 protocol/catalog
and 18 launcher tests pass. All 30 native opening bindings match build 23268598.
Two-player opening/save verification and complete fitted-part/bolt reconstruction
remain unfinished; roadmap 7.2 is still open.

**Loose replacement parts (v111, unreleased):** all 30 boxed-content factories now
capture their native outputs, including saved parts and every iteration of the
piston/bearing/rocker loops. ReplacementPartState (185) supplies persistent identity,
current pose, assembly status and native saved condition/adjustments on creation,
join and resync. Guests create missing loose parts from the exact prefab, skip local
save reads/writes, and participate in normal item carrying. Copies do not run native
assembly/bolt FSMs; host installation hides and unregisters the loose copy until the
part becomes loose again. Missing already-installed parts remain deferred. Native
host disposal retains save-key cleanup; guest disposal waits for a host echo. Core
builds cleanly; 797 protocol/catalog tests and 18 launcher tests pass. Static native
bindings match build 23268598. **Still unfinished:** guest box opening, reconstruction
of fitted parts and their complete mount/bolt/save graph, and two-player verification.

**Native part identities (v110, unreleased):** part bodies now use their native
Data/ID save identity; part, bolt and child-control FSMs use paths relative to that
part. Reparenting into an engine, identical display names and different peer scan
orders no longer select another instance. Matching native save keys guard readiness
and identity, and generic grocery cloning excludes part graphs. Generic FSM teardown
also removes owned hooks/registration marks so reconnect can register again. The
save-key pattern was verified across 156 native part prefabs. Missing contents
creation, installation/save graph replication and native multiplayer tests remain
open (7.2).

**Parts packages (v109, unreleased):** all 30 standard box factories now capture
host-created boxes and share their exact prefab, stable native identity and remaining
quantity. Join/resync and targeted object replies create missing guest replicas;
movement and disposal use existing item streams. Guests preserve local saved boxes
separately, pause their native box factories and disable replica persistence. Host
disposal retains native save cleanup. Guests are directed to ask the host to unpack
boxes. Opening intents, replacement-part contents/installation and native multiplayer/
save verification remain unfinished (7.2).

**Car-part condition correction (v107, unreleased):** part-settle and snapshot messages
now append full native tightness/wear floats. The old 0–1 clamp turned healthy native
90–99 wear into 1 and hid those differences in CRCs. Guests now request a deferred
host observation; their report cannot overwrite host wear. Zero-valued records,
late-binding retries and object-state replies restore actual native values. Fitted
engine wear keeps its existing vehicle authority path. Two-player assembly, repair
and save/reload checks remain open (R2.28); replacement-part contents spawning remains unfinished.

**Native factory spawning (v106, unreleased):** all 15 trophy factories now capture
host output by native persistent identity, independent of identical display names.
Existing saved awards and new awards join the normal item movement/removal stream
and replay on join/resync. Guest factory generation pauses after load; replicas have
native persistence disabled, while local saved trophies are hidden, paused and
restored on disconnect. Bindings and factory/prefab action layouts were checked
against installed build 23268598. Two-player save/reconnect tests remain open, as do
moose meat, replacement-part contents and spray-can adapters (roadmap 7.2).

**Shared-item recovery (v105, unreleased):** host-authorized removals now survive
deferred guest creation, and item-group resync carries removals plus refreshed
spawn manifests. Replays recover missing live replicas while preserving consumed
items as terminal IDs. Existing bodies remain untouched; session teardown resets
the lifecycle. Two-player timing/template/reconnect checks remain open (R2.27).

---

## 6. Risks & mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Early Access updates break FSM names/paths every patch | **High, certain** | Catalog tooling + diff (M0); launcher pins supported game builds and warns; sync descriptors versioned per build |
| PlayMaker long tail bigger than expected ("fully synced" is a marathon) | **High** | Generic FSM engine makes each new FSM a data problem, not a code problem; ship incrementally (M4 alpha); community-curatable catalog format |
| Physics divergence between peers (icy roads!) | Medium | Single-owner model avoids dual simulation; host-authoritative collision resolution; soft resync |
| Steam AppID usage friction (two processes, init quirks) | Medium | Launcher Steam session is optional by design; all critical Steam work happens in-game |
| Game switches to IL2CPP or breaks BepInEx | Low | BepInEx 6/Il2CppInterop migration path exists; monitor dev announcements |
| Amistech objects to the mod | Low (MSC mods tolerated for years) | Non-commercial, no piracy enablement (every player needs the game), takedown-compliant |
| Save corruption | Medium | Host-only writes through vanilla flow; launcher auto-backups; sidecar never touches vanilla keys |
| Licensing / MSCMP prior art (GPLv3) | Low | **Our license: GPLv3-or-later** (see `LICENSE`). Clean-room preferred; MSCMP code is license-compatible if ever needed |

---

## 7. Tech stack summary

| Piece | Choice | Why |
|---|---|---|
| Mod loader | BepInEx 5 x64 | MWC community standard, Mono, Harmony built in |
| Patching | HarmonyX | PlayMaker interception, vanilla flow hooks |
| In-game Steam | Steamworks.NET | Works inside Unity Mono profile, raw API access for Sockets/Lobbies/Rich Presence |
| Launcher | .NET 8 + Avalonia (cross-platform; Windows installer + Linux AppImage) | Fast to build, easy installer story, runs where the players are |
| Launcher Steam | Facepunch.Steamworks | Pleasant API, fine on .NET 8 |
| Serialization | Hand-rolled binary writers per message (codegen from `protocol/`) | Tiny, allocation-free, version-tagged; MSCMP validated the codegen approach |
| Compression | LZ4 for snapshots | Fast, good-enough ratio |
| CI | GitHub Actions: build plugin + launcher, run protocol tests, package release zip | |

### Dev/test workflow notes

- **Two-instance problem:** Steam allows one game instance per account. Dev
  loop uses (a) a `loopback` transport that runs host+guest protocol in one
  process for fast iteration, and (b) a second Steam account / second PC for
  real Steam-path testing. CI runs protocol-level tests headlessly.
- **Headless protocol tests:** `WinterMP.Net` is engine-independent
  (netstandard2.0) so message framing, snapshots and channel logic get real
  unit tests.

---

## 8. Player experience (the contract we're building to)

**Host:** install launcher → it sets up everything → click *Host* → game opens
→ friends get invited via Steam overlay. Save is backed up automatically.

**Guest:** install launcher once (sets up BepInEx + mod) → from then on,
clicking **"Join Game" in the Steam friends list is all it takes.** No codes,
no IPs, no ports.

**In game:** shared money, one world, one save. Build the project car
together, split up jobs, drive separate cars, freeze in the same lake. If a
guest crashes, they rejoin and continue where they were. Only the host's save
matters and it's always protected by backups.
