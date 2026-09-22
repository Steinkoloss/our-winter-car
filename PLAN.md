# Our Winter Car — Multiplayer Mod for *My Winter Car*

A full co-op conversion of My Winter Car (MWC) with a standalone launcher.
One player **hosts** and owns the savefile; everyone else joins as guests.
The target is to synchronize money, cars, parts, items, doors, NPCs, time,
weather, body warmth/heating inputs and all shared game-relevant events.

**Current target (2026-09-14):** a broad experimental alpha with a plausible shared
implementation for every normal gameplay loop, while accepting that testing will
find bugs. The [full sync-scope audit](docs/SYNC-SCOPE-AUDIT.md) is the current
implementation/readiness inventory. It identifies missing guest actions, outputs
and rejoin paths that older subsystem checkmarks overstate. The bounded home-stove
and Corris ATF refill slices now have controlled native evidence (protocol235).
The bounded guest flea-market chips journey now has native evidence through v237:
paid rental, shared pricing, exact sales, expiry returns, collection and persistence.
Other flea item families remain open. Protocol238 now adds the native five-fuse box
journey, shared loose fuse identities/counts, retirement and host save/rejoin. Other
contents families remain open; household holders are covered below. Protocol239 adds the
Corris ignition-to-fuse-box connection: guest/host installation, shared cable,
native host destruction, saved reload and guest restoration have controlled evidence.
Other wires, physical steering-column fitting and complete engine operation remain
open. The taxi lifecycle audit is recorded; native host pickup recognizes an
accepted guest taxi driver. Protocol240 adds host-owned taxi availability, guest
incoming-call answer/hangup, shared route/customer presentation and reconnect
restoration. Protocol241 adds guest duty/meter controls, shared native fare/LCD/
lights and accepted guest-speed input for host distance charging. Protocol242 adds
shared arrival, guest terminal quote/cash collection, unpaid-offer reconnect and
native collected-income save/reload evidence. Protocol243 adds shared receipt
printing, physical carrying and customer handoff, including guest-aware customer
departure and controlled native save/reload of receipt/distance totals. The selected
one-fare journey now includes protocol244 host-selected luggage: three suitcases,
a beer case and a mattress share identities, carrying, cargo and reset. Protocol245
adds the shared native salary report/read flag and verifies bank settlement, ledger
reset and saved reload (20 controlled native checks). The connected guest call →
customer/luggage → fare/receipt → earned payday → save/reload now passes 21 further
controlled native checks on v245, including a fix for late taxi packets during menu
return. This closes the selected fare integration task; J06 remains Partial.
Protocol246 now implements **H04 household fuse replacement in both homes**:
shared insertion, holder fitting/removal, tightening, blown-fuse/circuit state,
exclusive pickup and host save/rejoin. Controlled native restart testing found and
fixed loose holders being mistaken for fitted ones after load. H04 is Candidate;
physical input, automatic overload/electrocution and Steam/two-PC remain unverified.
Protocol247 now adds the **tractor-trailer coupling journey**: native automatic
attachment and validated guest release, one physics owner for the tractor and all
three trailer bodies, rejoin/restoration and native host save/reload. Twenty-four
controlled checks pass on one production payload. The trailer chassis is excluded
from the generic mass-based vehicle fallback; parked coupled tractors retain host
ownership until a driver takes over. V13 is Partial: towing ropes, other implements,
loaded trips and physical/Steam acceptance remain open.
Protocol248 adds **I09 shared sausage-package conversion**: eight native trigger
graphs, four identified outputs, host food state, exclusive conversion, successive
native package identities and rejoin. Consumed packages remain gone after native
save/reload; loose sausages have no vanilla persistence. I09 is Candidate and I08
is Partial; unopened package/pizza freshness remains open.
Protocol249 adds **human taxi passengers** in the front-right and rear-left seats,
reserving the customer's rear-right seat. Controlled two-game checks cover both
driver roles, short travel, boarding alongside the fare, conflict correction,
reconnect and tutorial/exit recovery. V04 remains Partial for other vehicles;
physical input, camera comfort, Steam/two-PC and saved mid-fare progress remain open.
Protocol250 adds **home coffee preparation and drinking**: the host owns the
household pot, finite grounds packets, brewing and conserved cup transfer. Only
the accepted drinker receives native personal effects. Controlled two-game tests
cover both lid controls, packet creation, water/grounds/brewing, guest filling and
drinking, replay rejection and reconnect after carrying the cup. Native host
save/reload and host drinking also pass; the bounded slice has 27 native checks. H10 is Partial: vendor/vending
coffee and physical/Steam acceptance remain open. The household coffee slice is
closed; vendor variants remain in the wider backlog.
Protocol251 adds **W02 shared train motion and collision/reset state**: host route,
eleven collider shapes, waits, lights/horn counters and reconnect. Twenty controlled
native checks pass, including actual guest-player train death and host native
comparison entry; dynamic guest physics is required for native player contacts.
Train cold starts follow vanilla because there are no native train save tags.
W02 is Candidate; physical crossing/car crashes, audible horns, full respawn and
Steam/two-PC remain acceptance gaps. [Evidence and limits](docs/BUILDING.md#shared-train-and-collision-lifecycle-protocol-251-unreleased).
Protocol252 adds **I05 light-bulb boxes and shared loose bulbs**: native one-bulb
opening, exact host output identity/condition, guest purchase/bag unpacking,
native hand pickup, replay/retirement and reconnect. Twenty-one controlled
checks include native save/reload: unopened boxes persist, consumed boxes stay
gone, and loose bulbs do not persist because vanilla has no loose-bulb save routine.
[Evidence and limits](docs/BUILDING.md#light-bulb-boxes-and-shared-contents-protocol-252-unreleased).
I05 remains Partial for R20 batteries; bulb fitting/light operation is separate.
Protocol253 adds **J09 shared advert delivery**: one pile and exact sheet identities,
exclusive carrying, host-native mailbox completion, day resets/pay, rejoin and
native saved progress. Mailbox roots remain available independently of the host's
house LOD. Loose sheets follow vanilla and do not survive a cold reload.
[Evidence and limits](docs/BUILDING.md#advert-delivery-and-native-payday-protocol-253-unreleased).
The selected delivery/payout slice is closed. Protocol 256 now supplies the missing
guest telephone enrolment (checkpoint below), making J09 Candidate. Physical
input, a full route and Steam/two-PC acceptance remain open. The broad inventory
contains 52 Partial rows. Protocol254 adds the required **motor-oil container foundation**:
all three native grades share durable saved identities, material/viscosity, remaining
fluid, empty state, pickup ownership and reconnect. Guest purchases use the host's
separate factory. Guest originals restore without restarting their saved Use FSM.
[Evidence and limits](docs/BUILDING.md#motor-oil-containers-protocol-254-unreleased).
Protocol255 now adds **Corris engine-oil cap access and conserved refilling**:
the host validates the installed head/pan, actor and bottle ownership, proximity,
angle and overlap; both players see cap changes and the resulting oil quantities.
Contamination/viscosity and mounted/saved pan fields update together. The native
empty-bottle save getter is protected against restoring the prefab's four litres
after its trigger was destroyed. Native startup also prevents a nearby pour trigger
from interrupting Load before the saved root-to-source handoff. [Evidence and limits](docs/BUILDING.md#corris-engine-oil-refill-protocol-255-unreleased).
**I04/I12 remain Partial** for other supplies/transfers and detached assemblies.
This bounded maintenance journey passes 45 controlled native checks, including
repeated cold saves. The next selected task, **J09 guest outgoing telephone
enrolment**, is now implemented in v256. Protocol257 now closes **I05 R20
battery-box contents**, including four persistent loose cells, guest purchase and
native save/rejoin (32 controlled checks). The inventory is 27 Candidate / 52 Partial /
0 Missing / 3 Review. Next: **V11 guest window scraping**, beginning with the native
tool/contact and frost-ownership audit. Prior bag/stutter acceptance and full physical assembly/Steam tests
remain in the wider queue.
The ordinary co-op journey and four-player soak are evidence/gates within that wider scope.

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

Historical subsystem summary: ✅ indicates the originally described slice landed,
not full native feature coverage. Use the [2026-09-13 scope audit](docs/SYNC-SCOPE-AUDIT.md)
for current Candidate / Partial / Missing / Review decisions. Target milestone in parens.

| Subsystem | Sync model | Status |
|---|---|---|
| Player avatars | Custom rig (head/hands/body) streamed ~20 Hz, name tags. Remote players *visual only* (no physics pushing) v1. **v53 sanitizes each received pose before relay/storage** (finite bounded coordinates, plausible quaternion, defined move flags), which also protects the fresh-pose proximity proof used by guest intents. | ✅ |
| Player animation | Derived state machine (walk/run/crouch/carry/drive) — low bandwidth | ✅ |
| Player needs (hunger/fatigue/thirst/urine) | Per-player, reported to host every ~12 s, saved in `wintermp-guests.json` sidecar. **Stress + Drunk appended at v30** (`Stress` global; `DrunkCurrent` on the FPS-camera "Drunk Mode" FSM) — same report/sidecar/rejoin-restore path, so a rejoining guest keeps intoxication instead of resetting sober. **Dirtiness appended at v55** (`PlayerDirtiness` global) follows the same authenticated report/profile/rejoin path; legacy sidecars leave it untouched rather than falsely restoring clean | 🚧 (needs two-player/soak validation; rest ✅) |
| **Body temperature / cold** | Per-player **5th need** (`BodyTemp`), corrected in local **v189** to native global `PlayerTemp`. Reports and reconnect offers carry explicit availability, including known zero; old sidecar air-temperature samples are retired. Local **v190** feeds available current cabin temperature into seated passengers' native body reads. Body simulation stays local. See §4.8. | 🚧 (source/persistence and passenger input checks pass; full seat flow and live soak pending) |
| **Clothing** | Per-player `ClothingStage`/`ClothingType` (`CLOTHESHOME`/`CLOTHESWORK`) — drives insulation (warmth math) **and** the remote-avatar visual. Synced at **v28** (`PlayerClothingState`: `ClothingStage`+`ClothingType`, owner-authoritative + host-relayed; avatar shirt tint best-effort). Join-sync closed: the host bursts every already-connected player's current outfit to a joining guest (clothing is change-only, so without it a joiner saw everyone in default clothing/warmth tier) | 🚧 (v28, not soak-tested) |
| Text / voice chat | Text chat done; positional voice via Steam Voice later | 🚧 (M11) |
| Money/economy | **Shared cash and bank balances** owned by host. Purchases use validated intents. **v90** corrects the cash binding to `PlayerMoney`, adds bank/income snapshots and acknowledged ATM transfers. **v92** replaces inactive-host slot replay with host ledgers, leased controls, separate credit/winnings and seeded native reels; timeout/disconnect settle once. **v93** adds VideoPoker with private host decks, holds/redraws, doubling and acknowledged settlements using native presentation. **v94** adds revisioned debt-letter quotes and acknowledged cash settlement, including an inactive host sheet and relocated mailbox. **v97** mirrors host Ventti property keys and cabin access to guests/joiners without replaying wager actions; teardown restores the guest's original access. **v98** sends full-width Ventti stakes/hand totals and native result observations, retaining state across late bindings and restoring guest variables on teardown. **v99** connects the Ventti engine to acknowledged host commands, escrow, private decks, native card meshes and once-per-round native property outcomes with duplicate accounting removed. Native host save handling stays active and guest controls cannot debit or draw independently. Static evidence, Core builds and protocol tests pass; two-player LOD, save/teardown, property and NPC presentation checks remain in the coverage roadmap. | 🚧 (two-player economy verification pending) |
| Shops & cash registers | `anyone-triggers` purchase intents; host executes, spawns goods, applies money | ✅ |
| **Classifieds parts ordering** | Magazine listings (`JOBS/ADs` advert pile) + their **periodic refresh** are host-authoritative shared state (synced RNG/seed) — else peers see different parts for sale. Dialing a `CARPARTS/PARTSYSTEM/PhoneNumbers/*` seller now completes through the normal host purchase path: v37 captures the guest's exact populated `OrderAMIS`/`OrderYP` record before local payment, authenticates and pairs its next monotonic record sequence to that same guest's next `PAYMENT` intent, then makes the host spawn the selected mailed delivery. v33 mirrors `JOBS/ADs` scalar job state and Marketti issue/layout; v36 mirrors complete pending-order data to observers and joiners. Runtime listing generation is still local presentation, but it can no longer cause the host to deliver a different selected part. *The in-game computer is an MSC-import toy — not this; parity backlog* | 🚧 (M8, v37 needs two-player runtime confirmation) |
| **Jobs / flea market** | Firewood delivery (+ tractor wood-splitter PTO), sewage, factory punch-clock shift (`JOBS/FACTORY` TimeClock), flea market. Accept/progress/payout host-validated; reward → shared wallet. v33 forwards factory punch-in/out to the host and mirrors factory employment/package/paycheck state. v34 forwards sewage/firewood customer payout clicks to the host and mirrors every house's active-order + level/surplus state. v217 closes firewood payment duplication by reserving each live offer on the host and suppressing guest cash/income additions and snapshot payment replays; the prepared-offer test passes. v220 mirrors all four buyers' visibility, native offer labels and world poses, includes nearby guests in native LOD checks, and retains customer 1's identity across relocation/reconnect. Actual delivery and signed penalty reconciliation remain unverified/incomplete. v35 additionally mirrors the GIFU tank/pump/hose state for snapshot and observer convergence; the KEKMET dashboard PTO switch and GIFU dump lever now use reliable cataloged control replay. Flea rental/proceeds use dedicated host receipts in v236: rental selection changes only the local cart, native checkout pays once, and collection consumes the available expired-rental envelope once. Generic checkout/envelope replay is excluded. Host mixed merchandise remains native; guest checkout currently requires a rent-only basket. Guest table simulation pauses and restores; shared selling item identities, prices, retirement and listing persistence remain incomplete. Taxi job (`TaxiJobState` v65), kilju fermentation (`BrewState` v66), farm job (`JobSiteState` kind 4 v67), Kela welfare (`WelfareState` v68), hitchhiker (`HitchhikerState` v69) are also host-owned now. Phone-order acceptance is now paired to the caller’s next payment intent. Guest-operated hose, cutter attachment, and flea-table item placement remain deliberately open: their local FSMs carry dynamic picked-object/implement references, so generic remote replay would be unsafe; they need dedicated validated intents. | 🚧 (M8, v37 partial) |
| Car assembly (bolts/parts) | Persistent native part IDs; v114 guest bolt intents execute on host and return absolute save-array, pose and parent-tightness results. v122 adds host-validated oilfilter hand tightening; v125 adds distributor SparkAngle adjustment through existing part operations. v219 accepts guest fitting/removal of the persistent cylinder head through the native host prerequisites and shared part receipts; guest saved head data stays protected, while fastening bolts remain open. Bolt-settle wear synced; `VehicleDamage` v124 uses host authority regardless of driver. Guests retain accepted damage state for checksums, with native damage rolls/replay and mount Wear writes suppressed. Drivetrain/tire `VehicleCondition` (v61) is partial: native pressure units and stream ownership/order are corrected, but parked publication and native wheel/gearbox inputs remain open (roadmap 2.2). Gear uses `VehicleState` (v63). | 🚧 (guest engine operation, wear under guest driving, physical failure presentation, remaining adjustments and two-player/save verification remain open) |
| Vehicle state (Sorbet, Corris, +) | Engine `owner-only` (driver owns whole vehicle); rpm/fuel/coolant/lights/blinkers + cabin **climate** (frost/fog/defrost/heater) synced. Host relays only streams whose owner/player id matches the authenticated sending peer (also enforced for item/cargo/player/passenger/clothing streams), closing spoofed-owner writes before world state is touched. **v52 also makes passenger seating host-validated:** a guest's next sequence must describe an exact discovered seat within 2 m of its fresh pose; exits require vehicle id 0, the host owns occupancy, and same-seat races resolve by lowest player id. **v95** preserves accepted seats across moving-car keepalives without repeating entry proximity checks; rejected new claims clear occupancy for every peer and join snapshot, while stale requests are ignored. | ✅ |
| **Fuel / jerrycan / pumps** | Refuel as `anyone-triggers` intent; fuel level already rides in `VehicleState`. v33 additionally syncs tracked jerrycan/container `FuelLevel` + pouring state under transform ownership and applies remote vehicle fuel to the actual tank (not just its gauge). The Peräpörtti fuel-station monitor presets/pump selection are cataloged reliable controls and its cash trigger is a host purchase intent, so payment and wallet changes converge. **v39 closes live nozzle use for parked vehicles:** the guest reports only an actively dispensing local nozzle's target tank level; the host requires fresh player/nozzle/vehicle proximity, a stationary target, monotonic bounded growth, then writes its real tank and reconciles all peers with `VehicleState`. Dynamic hand/pistol references are still never replayed. | 🚧 (M8, v39 needs two-player runtime confirmation) |
| World items (pickables / cargo / consumables) | Event-synced motion/cargo/despawn. **Shopping bags v120:** persistent host factory/native IDs, isolated guest bag views, atomic one/all opening requests (190–192), host-only native inventory consumption, direct factory-output capture and chunked ItemSpawn manifests. Replica name lookup uses native prefab display-name actions; missing items retry rather than waiting for another player to spill. Guest offers53 retired. Bag pickup guards/release reconcile hand ownership. v121 adds native fan belts/oil filters via replacement state 185; unknown part products remain guarded before opening. v218 adds host-owned loose milk condition and spoiled presentation, preserving native host warm/fridge decay and suppressing guest decay. Other food and fridge power changes remain open. | 🚧 (implemented; two-player acceptance pending) |
| Doors / switches / controls | `anyone-triggers` events (incl. lights, wipers, hazards, handbrake) | ✅ |
| **Home heating & cooking** | Cabin woodstove (`CABIN/Cabin/woodstove/Fireplace`: `SetFire`/`WoodTrigger`/`SausageTrigger`), sauna kiuas (`StoveHeat`/`SaunaStove`), cottage/living-room fireplaces. Host-owned progression (lit, fuel, heat output, sauna temp); feed/light/grill = `anyone-triggers`. Synced at **v28** (`HeatSourceState`/`HeatSourceIntent`: woodstove/sauna/fireplaces; host reads authoritative signals + broadcasts, guests apply locally, light/feed/grill/löyly intents fire real game events on the host). **v49 binds each guest action to an authenticated player id + monotonic sequence and requires that player’s fresh pose within 8 m of the exact source**, so remote peers cannot feed/light/grill/steam a distant home. Joining guests get a forced full re-broadcast with the world snapshot (no 20 s cold wait). See §4.8 | 🚧 (v49, not soak-tested) |
| **Electric home stoves** | Protocol233: guest turns on both homes, native host knob/heat simulation, revisioned grill/burn/light/smoke results and guest restoration. Existing meat state210 supplies one shared cooked result and joining. Sausage conversion, other food and complete house fires remain separate gaps. [Evidence and limits](docs/BUILDING.md#guest-home-stove-cooking-2026-09-13-unreleased-v233). | 🚧 (bounded alpha candidate) |
| **Fuse boxes / loose fuses** | v238 adds five-fuse boxes to the existing host-authoritative package opening ledger. Native fuse IDs, shared outputs, remaining count, retirement and host save/rejoin are covered by a bounded two-player fixture. Light-bulb/R20 boxes and fuse-holder installation/electrical effects remain separate gaps. [Evidence and limits](docs/BUILDING.md#shared-fuse-boxes-and-loose-fuses-2026-09-14-unreleased-v238). | 🚧 (I05 partial) |
| **Home appliances** | TV (`TVSwitch`), house/apartment lights and fuse main switches are cataloged `anyone-triggers`; v40 additionally catalogs fridge doors and the home-stereo radio/CD power switch. **v42 makes the home stereo's power, radio channel, volume and bass host-owned scalar state**: guests submit bounded settings only while near the stereo, and the host rebroadcasts/snapshots the applied values. **v51 mirrors the grounded home, yard, and apartment shower tap/valve transitions**, so the shared fixture state agrees before the game’s local hygiene logic runs. **v55 preserves each guest's resulting `PlayerDirtiness` across reconnects when they choose their saved return position**; it does not replay hygiene actions. CD track/disc, appliance consumption, individual fuse insertion, and full hygiene/wash interaction authority remain to do. | 🚧 (M11, v55 partial; needs two-player runtime confirmation) |
| NPCs & traffic | `host-only` sim; transform + FSM streaming with distance-based rates | ✅ |
| **Police / cops** | v41 turns a guest's local checkpoint result into a host-validated shared fine record: the host requires authenticated fresh player pose, exact known checkpoint, nearby delegated vehicle, monotonic sequence and bounded fine before it updates Fines and all peers. Fine payment routes through the existing host purchase guard/shared wallet. Arrest and impound still need dedicated host-owned flows. | 🚧 (M9, v41 partial; needs two-player checkpoint validation) |
| **Animals / moose** | Host streams live AI poses. v228 fixes native death detaching the corpse and destroying the streamed mover: independent `MooseCorpseState` carries 11 ragdoll poses and both chop counters. Guest CarHit reports before local destruction; guest axe checks send count-bound intents to the host. Native host spawning and v224 meat IDs give both peers one copy per accepted cut, including reconnect. Physical axe/vehicle collisions, native meat save/reload, natural cooking/spoilage and Steam acceptance remain open. | 🚧 (native state-entry co-op checks; physical acceptance open) |
| **Inspection / registration (katsastus)** | Inspection order/payment already use host purchase handling. v38 mirrors the host-evaluated standard inspection record: every pass/failure checkmark, stamp/museum flags, and next-inspection renewal data are applied to peers and joiners. v40 additionally mirrors host-generated standard/museum registration plate text plus the matching physical plate-pair availability, so peers never reroll registration IDs or disagree about displayed plates. Individual plate pickup/installation still travels through the existing item/part state path and needs two-player validation. | 🚧 (M9, v40 partial) |
| **Racing (Suvi-Sprint, Ice Rally)** | v43 adds host-validated Suvi-Sprint stage progress; v44 adds ice-track marker/lap authority; v45 mirrors host grid/heat configuration; **v47 also mirrors host-owned lineup registration/race stage**. v46 mirrors the six host-generated ice-race result rows (name/number/model/UA) to all guests and join snapshots, closing divergent leaderboard presentation. The sixteen `RACES/ICERACE/Cars*/Disable/*` AI opponents now stream from the host through `NpcTransform`; guests freeze their local navigation/drive FSMs while the host stream is live. **v50 routes the exact Suvi-Sprint and ice-race price triggers through the host purchase gate**, so prize collection and the shared wallet converge instead of each peer executing the opaque action locally. The **rally** `ResultsWeekend` scoring/reward ledger, `RegisterRally` enroll, and `ParcFerme` penalty are now host-owned via `RallyResultsState` (v73); the **JOKKIS** banger-race lap/time/checkpoint is host-broadcast via `JokkisRaceState` (v74); the rally `PartsSalesman` vendor is catalogued (v72 catalog). Two-player runtime confirmation remains. | 🚧 (M10, all racing slices landed; needs two-player runtime confirmation) |
| Time/weather/calendar | Host clock is law; guests slave FSM time vars; periodic hard correction | ✅ |
| Sleeping / time skip | Consent: all players confirm → host advances time | ✅ |
| Death / respawn / permadeath | v226 releases passenger seats at native death start and preserves pending normal recovery through newspaper/MainMenu/load. Respawn requires restored movement and completed guest spawn selection. v227 fixes the native permadeath binding and keeps guest runtime flags aligned through loading/reconnects without saving them locally (21 live setting checks). v229 fixes duplicate native group-death entry and keeps wiped runs terminal through menu loading, stale respawns and reconnect attempts. 26 local checks verify either role starting a wipe, unchanged guest saves and normal recovery. Physical death causes, interruption, Steam/four-player acceptance and uninterrupted normal host-world operation remain open. [Wipe evidence](docs/BUILDING.md#native-permadeath-group-wipe-2026-09-13-unreleased-v229). | 🚧 |
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
  ES2 library. Protocol 214 adds a native SAVEGAME broadcast observer so guest
  pose/needs publication stops before teardown can reset the player. Nineteen local
  save/restart/rejoin checks now pass with separate disposable profiles; their guest
  world files stay byte-identical. Physical save-button input, permadeath and real
  Steam/two-PC acceptance remain open.

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
  The bounded [S09 LateUpdate slice](docs/S09-LATEUPDATE-CONTAINMENT.md) isolates
  vehicle/trailer/Ventti callbacks with local retries/quarantine while preserving
  the global fallback and session cleanup. The subsequent bounded
  [vehicle Update slice](docs/S09-UPDATE-CONTAINMENT.md) applies independent
  budgets to ten vehicle stream callbacks while keeping healthy Update siblings
  running. The [Train FixedUpdate slice](docs/S09-FIXEDUPDATE-CONTAINMENT.md)
  contains escaping train coordinator errors with that same local budget.
  The [Train dispatch slice](docs/S09-TRAIN-DISPATCH-CONTAINMENT.md) contains
  receive/preparation and snapshot-creation escapes without replaying packets
  or abandoning later snapshot chunks after a train creation failure.
  The [Train snapshot-send slice](docs/S09-TRAIN-SEND-CONTAINMENT.md) contains
  per-message TrainState encoding/transport failures in snapshot request fanout,
  preserving later chunks and diagnostics without retries or cached results.
  Portable callback tests and net35 compilation are distinct from native
  acceptance; nonselected Update, handlers, live broadcast/other send fanout and
  native teardown boundaries remain open.
- **Telemetry-in-logs:** structured log lines for every intent/transition,
  ring-buffered, dumped on error — feeds the launcher's bug-report zip.

### 4.8 Winter survival & heating (the defining loop)

Winter survival requires shared heat inputs and correct persistence of each
player's own warmth. Local v189 corrects the needs source to native PlayerTemp,
retiring the air-temperature sample previously stored under BodyTemp. Local v190
feeds available current cabin temperature into seated passengers' native body
reads. Heating and reconnect persistence still need live thermal/soak acceptance
through the full seat/door and loading flows as part of the M7 gate:

- **Body temp = the 5th need.** Extend `PlayerNeedsSync` + the
  `wintermp-guests.json` sidecar with `BodyTemp`. Each player stays authoritative
  over their own body warmth (like hunger/thirst) and reports native PlayerTemp
  to the host with explicit availability. The last-position reconnect choice
  restores known warmth, including zero; legacy ambient samples remain unknown.
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

**Current development checkpoint (2026-09-12):** prove one ordinary co-op session:
join → buy/unpack groceries → drive together → sleep → host save/reload → guest
rejoin. The [coverage roadmap](docs/COVERAGE-ROADMAP.md#current-playable-checkpoint--one-ordinary-co-op-session-2026-09-12)
tracks each pass condition, observed evidence and the next bounded task. This is
an intermediate step toward M7; its winter-survival and four-player soak exit
criteria below still apply. Distinguish implemented behavior from local native
verification and real Steam/two-PC acceptance. Sleep cancellation and a successful
three-hour sleep now pass a local two-game check on development protocol 211;
physical input and real Steam acceptance remain open. Protocol 212 removes cabin
proximity from driver ownership: a local two-game replay now lets the guest take
the seat after the host exits. Protocol 213 closes the resulting local Sorbet
engine-handoff blocker: exact temperature and native engine speed follow the new
driver, and stale stopped-engine RPM cannot restart it. The final isolated two-game
run passed 24 checks, including powered travel/passenger following in both roles,
running handoff both ways, stable parked idle, OFF/ACC-only handoffs and clean exits.
That build also passed 4,325 protocol tests and 1,511 native checks. This
remains local Sorbet evidence; other cars, physical input and real Steam acceptance
remain open. See `build/engine-handoff-audit/` for the driving attempts and tested
payload hashes. Protocol 214 now protects guest profiles across loading, spawn
choice and native save teardown, restores needs with either spawn location, and
reconstructs saved loose grocery replicas after a host restart. Validation and
limits are recorded in [save and resume](docs/BUILDING.md#save-and-resume-2026-09-13-unreleased-v214).
Protocol 215 adds a dedicated float-valve control adapter: host-authoritative
turns and guest display updates preserve guest saved tuning. The local loose-head
round trip passes for all eight controls. Native host fitting exposed the separate
physical head-placement gap. Protocol 216 closes that gap for the matching native
head: host fitting/removal moves the guest head and its child mounts, and nearby
guest turns work on all eight fitted valves. Protocols 219/222 add guest fitting,
removal and indexed fasteners. The v223 no-save-record check verifies native head
initialization and reuse across different saved data; actually destroyed scene
heads, full child-part parity and Steam/physical-input acceptance remain open.
See [head placement](docs/BUILDING.md#cylinder-head-placement-2026-09-13-unreleased-v216)
and [absent save records](docs/BUILDING.md#missing-cylinder-head-save-records-2026-09-13-protocol-v223).


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

**Direct bag engine parts (unreleased; protocol 121):** fan belts and oil filters
join replacement-state creation, native save identity and guest-original isolation.
Mixed bag spills count parts separately from grocery ItemSpawn entries; the same
Data/ID survives fitting, body replacement and retirement. A failed part binding
fails its opening without holding the global bag lock. Fanbelt fitting retains its
native alternator-angle prerequisite. At protocol 121, Core/Net built cleanly and
1,089 protocol tests, 18 launcher tests and 94 isolated native checks passed.
Full guest engine behavior and actual two-player acceptance remain pending;
oilfilter hand tightening and fanbelt presentation are implemented below.

**Vehicle engine handoff (unreleased; protocol 126):** engine state 60 follows
the current physics owner; an unowned vehicle uses host authority. A parked owner
keeps streaming while ignition is active, releases the driver's seat immediately,
and sends the final engine state reliably before its final pose. A nearby seated
driver can take over from a simulator that has left the seat. Host validation
rejects stale or non-owner engine reports before relay; per-sender sequence history
survives handoffs and live sequences skip the snapshot sentinel. Join/resync uses
fresh accepted driver state, including gear, and cannot overwrite a local driver
or release live ownership through a pose repair. Native RPM takes precedence over
a stale dashboard reading; cold snapshots bind gear before applying, and delayed
Power bindings retry ON/OFF without another packet. A seated player is protected
from remote timeout before claiming ownership, and expired ownership cannot make
the host republish residual remote ACC. This establishes a safer engine state
handoff; full guest engine simulation, native wear under guest driving and
saved-part reference projection remain separate work. The [corrected native engine audit](build/vehicle-rpm-smoke/native-audit.json)
identifies Starter's enabled global RPM outputs; dashboard values cannot safely
replace that simulation input. At v126, 1,377 protocol/catalog/policy tests,
18 launcher tests and 253 isolated game checks pass, including 33 vehicle-state
checks. Both Net targets, Core (Debug/Release) and the probe build without
warnings/errors. [Native results and tested hashes](build/vehicle-state-smoke/result.json)
record zero failures and Wine exit 0. Actual two-player driving, handoff and
full engine/wear behavior still need the acceptance sequence in BUILDING.md.

**Native guest engine write protection (local/unreleased, protocol 126):** the
`guestEngineProtection` catalog identifies 65 persistent scalar writes across 11
native FSMs, plus the distributor's saved mesh rotation. Selective action guards
preserve the surrounding calculations, delegated fuel/electrical consumption and
runtime scratch fields. The destructive PartFallings graph is paused separately
because it mutates saved bolt arrays and sends BREAKOFF. Protection remains tied
to the guest save latch through disconnect; failed bindings defer saved-part
isolation and ignition wake while retaining successful guards. A malformed profile
reports its own error without discarding unrelated catalog rules. Guest admission
prepares guards immediately after setting the save latch, stopping already-active
writers in that call; failure retains the latch. After a repaired binding, only
the blocked state entry completes, without replaying stale exit actions or events.
Inactive graphs wait for activation before that completion. This closes a
local saved-reference protection gap. All 1,428 protocol/catalog/policy tests,
18 launcher tests and 276 isolated game checks pass, including 51 new protection
catalog cases and 23 new native checks. Both Net targets, Core (Debug/Release) and
the probe build without warnings/errors. The [native results and tested hashes](build/guest-engine-smoke/result.json)
record zero failures and Wine exit 0. Actual two-player controls, full guest combustion and authoritative
wear while a guest drives remain unverified.

**Native Corris RPM source (local/unreleased, still protocol 126):** the optional
`vehicleEngineRpm` catalog binds eight enabled Starter outputs to the registered
Corris root's native CarDrivetrain component and global RPM. This includes
StarterSpeed while cranking and the enabled Wait reset, without treating disabled
stopped-state actions as producers. Sampling returns zero for a disabled, inactive
or unstarted source, retries changed/missing references, and rejects foreign
components or a local variable shadowing the global output. Dashboard updates
cannot write through an alias of that output, even after binding failure; active
ACC retains ownership while RPM discovery waits. This corrects engine readiness
and publication without writing simulation inputs, projecting guest mount data or
establishing host wear progression. State 60 and protocol 126 are unchanged.
All 1,459 protocol/catalog/policy tests, 18 launcher tests and 301 isolated game
checks pass, including 31 new catalog cases and 25 new native RPM checks. Both Net
targets, Core (Debug/Release) and the probe build without warnings/errors. The
[native results and tested hashes](build/vehicle-rpm-smoke/result.json) record
zero failures and Wine exit 0. Full guest engine behavior and actual two-player
cranking, stopping and handoff still need acceptance. Isolated native mounts retain the guest's ActivePart, Installed
and scalar values; host replacement state does not yet supply all required inputs,
including InertiaFactor, ValveTolerance and rocker Bolted. Runtime reference
projection beyond the distributor remains a prerequisite for an operational guest engine.

**Guest distributor engine inputs (local/unreleased, protocol 127):** four native
Cylinders reads use a separate inert Data object populated from the host's accepted,
fully applied VIN131 replica. Installed follows its unique attachment; Wear and
SparkAngle follow state 185, and Tightness respects newer bolt receipts. Pending,
loose, retired, conflicting or unavailable copies supply an absent part. Reader-local
targets leave saved Data, shared mount references and ActivePart untouched. The
live factory/reader reference survives block reparenting. Existing write protection
pauses the consumer when signatures change; recovery replaces the whole proxy when
its cached Data disappears. Admission prepares inputs before engine guards resume.
Teardown restores original targets after saved parts, retaining protection if
restoration fails. Native scratch calculations remain untouched: running inputs
refresh on the normal roughly one-second cycle, and a failed start still needs
normal ignition restart. This semantic bump changes no message layout. Other engine
inputs, host wear while guests drive, full operation and two-player acceptance remain open.
All 1,505 protocol/catalog/policy tests, 18 launcher tests and 334 isolated native
checks pass, including 46 new catalog cases and 33 new native input checks. Both
Net targets, Core (Debug/Release), the probe and Launcher Debug build cleanly.
[Tested payload and native results](build/guest-engine-input-smoke/result.json)
record the controlled Debug run with zero failures and Wine exit 0; Release Core
was built separately. Changes remain local and unreleased.

**Guest starter engine inputs (local/unreleased, protocol 128):** VIN130 state 185
appends actual host part Durability after Wear/Tightness. The starter's three native
Installed/Wear/Durability readers now use a separate inert proxy under the same
applied-revision, identity and attachment gates as the distributor. Both catalog
entries are required and have distinct consumers. Normal wiring/wear decisions and
durability calculations remain native; saved guest data and write targets stay
unchanged. Missing, removed or unavailable host starters remain absent. New data
waits for the next native read without altering an in-progress attempt or forcing
ignition replay. Wiring, battery, block/flywheel/gearbox references, broader engine
operation and host wear during guest driving remain unfinished.
Validation passes 1,514 Net tests, 18 launcher tests and 351 isolated native
checks (all 334 prior checks plus 17 starter checks). Nine new catalog cases cover
the required starter profile and published Durability. Both Net targets, Core
Debug/Release, the probe and Launcher Debug build without warnings/errors.
[Native results and tested hashes](build/starter-engine-input-smoke/result.json)
record the controlled Debug run and matching launcher payload; Release Core was
built separately. Changes remain local and unreleased.

**Guest water-pump engine inputs (local/unreleased, protocol 129):** VIN126 state
185 appends actual host Durability/Efficiency after Wear/Tightness. Seven native
readers across Oil and Cooling use independent inert proxies for the same accepted
pump identity and attachment. All four audited consumer entries are now required.
Native seizure, wear arithmetic, circulation, belt/RPM gates and loose-pump leak
calculation remain active, with saved guest part/mount writes protected. Tightness
uses newer accepted bolt receipts. Pending state/removal/disconnect closes both
consumers, and a changed reader pauses only its own graph. Inputs wait for normal
native reads and never force a state replay. Other cooling/engine dependencies,
full guest engine operation and host wear during guest driving remain open.
Validation passes 1,528 Net tests, 18 launcher tests and 378 isolated native
checks: all 351 prior checks plus 27 water-pump checks. Fourteen new catalog cases
cover both required pump consumers and appended fields. Net, Core Debug/Release,
the probe and Launcher Debug build without warnings/errors. The controlled native
run uses Debug; Release Core was built separately. Changes remain local and
unreleased.
[Native results and tested hashes](build/waterpump-engine-input-smoke/result.json)
record the run and matching launcher payload.

**Guest stock/racing fuel-pump inputs (local/unreleased, protocol 130):** both
VIN125 and FUELPUMP0 append actual Durability/OutputRate to state 185. FuelLine's
three native pump readers and Wearing's one durability reader use independent
proxies accepting either audited factory at their shared mount. All six consumer
profiles are required. Both factory references must agree; accepted pending or
conflicting variants remain unavailable until exactly one current applied replica
is ready. Stock/racing replacement keeps the same proxy and waits for normal native
reads. Native starvation/capacity decisions and durability arithmetic stay active;
saved wear targets and native scratch stay protected. Scalar-only consumers retain
an internal installed gate without inventing a native bool reader. Remaining fuel,
engine and host wear progression dependencies still prevent full guest operation.
Validation passes 1,551 Net tests, 18 launcher tests and 409 isolated native
checks, preserving all 378 prior checks and adding 31 fuel-pump checks. Twenty-three
new catalog cases cover both variants and their required consumers. Both Net
targets, Core Debug/Release, the probe and Launcher Debug build with zero warnings
or errors. Native execution uses Debug; Release Core was built separately.
[Native results and tested hashes](build/fuelpump-engine-input-smoke/result.json)
record zero failures and the matching launcher payload. Changes remain local and
unreleased.

**Guest oil-pump inputs and shared consumers (local/unreleased, protocol 131):**
VIN132 appends actual host Durability after Wear/Tightness in state 185. Oil reads
Installed/Wear; Wearing reads Durability. Both native FSMs now combine independent
part sources: water/oil pumps in Oil and fuel/oil pumps in Wearing. Eight required
source entries cover six consumers, with unique source references/action slots.
Each source retains its own stable proxy and identity/attachment gates; shared
native scratch updates only at normal reads. A native graph resumes only when all
sources validate; broken sources pause that graph while other consumers remain
active. Recovery rebuilds only affected proxies, and partial teardown retains
protection until all reader targets restore. Native circulation/starvation and
wear arithmetic remain active with saved guest part/mount data protected. Broader
engine inputs and guest-driving host wear progression remain unfinished.
Validation passes 1,563 Net tests, 18 launcher tests and 436 isolated native
checks, preserving all 409 prior checks and adding 27 oil-pump checks. Twelve new
catalog cases cover the added sources and cross-source collisions. Both Net
targets, Core Debug/Release, the probe and Launcher Debug build without warnings
or errors. Native execution uses Debug; Release Core was built separately.
[Native results and tested hashes](build/oilpump-engine-input-smoke/result.json)
record the final run and matching launcher payload. Changes remain local and
unreleased.

**Guest camshaft inputs (local/unreleased, protocol 132):** all five stock/tuned
camshaft families append Durability/ValveTolerance to state 185 and publish actual
CamProfile as a new trailing string. Native Cylinders, Wearing and Valves consume
the uniquely attached, fully applied host part through typed inert proxies.
Eleven required sources cover seven consumers, including multi-source combustion
and wear graphs. The nested cylinder-head mount remains valid after head movement.
CamProfile is gameplay state, with strict eight-digit format and revision rules;
absent input has a parseable zero profile. Native wear thresholds, valve tolerance
and power/torque profile conversion remain active without saved guest Data writes.
Broader engine operation, host wear while guests drive and two-player acceptance
remain unfinished.
Validation passes 1,597 Net tests, 18 launcher tests and 477 isolated native checks, preserving all
436 prior checks and adding 41 camshaft checks. The 34 new Net cases cover catalog
completeness, all variants, profile framing and gameplay revision behavior. Both
Net targets, Core Debug/Release, the probe and Launcher Debug build cleanly;
launcher payload hashes match the native-tested Debug build.
[Native results and tested hashes](build/camshaft-engine-input-smoke/result.json)
record the isolated Debug run; changes remain local and unreleased.

**Host wheel-health inputs (local/unreleased, protocol 209):**
Message 205 delivers exact host `ThisTire::Data.TireHealth` inputs for all four
wheels with independent availability and host revisions. Read-only capture
publishes parked changes, joins and vehicle resync without borrowing driver
condition or executing native repair/wear actions. Guarded guest native health
readers consume these values even while driving; missing sources and temporary
unregistration pause/recover the affected consumer without saved writes.
The old condition layout remains, but its health bytes no longer supply native
CORRIS health readers. Physical tyre repair/type/grip, guest-driving durable
tyre wear, pressure refill publication and full failure/lifecycle validation
remain roadmap 2.2 work. Next free ID is 206.
[Validation and limits](docs/BUILDING.md#host-wheel-health-inputs-protocol-209-unreleased).

**Wheel rim presentation (local/unreleased, protocol 208 unchanged):**
Accepted rim flags now enter the cataloged native two-action rim state on each
wheel, applying radius and rolling friction independently of the observer's
saved tyre type. Native puncture sound is stopped without entering the flat
state's saved-health writer. Invalid or late bindings retain a pending apply;
accepted keepalives repair physical drift. Native checks cover host saved-health
preservation and guest replay. This does not complete rim-to-tyre repair,
rim-to-flat transitions, durable tyre wear or parked repair publication; roadmap
2.2 stays partial. Wire layouts, meanings and next free ID 205 are unchanged.
[Validation and limits](docs/BUILDING.md#wheel-rim-presentation-protocol-208-unreleased).

**Guest gearbox failure wear (local/unreleased, protocol 208):**
The actual guest driver reports the damaged-gearbox native kick-out callback
with message 204. The host validates current ownership, ordering, a vehicle
budget and the installed saved gearbox's DamageType 1–3, then runs the native
0.0525 wear subtraction once. Host observer callbacks cannot double-charge;
message 202 publishes the updated saved wear. Guest saved data remains
protected, and local gear/sound failure actions stay native. Full physical
failure reconciliation, fitted-part lifecycle, tyre wear, parked wheel repair
and live two-player save/reload acceptance remain open; roadmap 2.2 stays
partial. Next free ID is 205.
[Validation and limits](docs/BUILDING.md#guest-gearbox-failure-wear-protocol-208-unreleased).

**Guest automatic gearbox oil use (local/unreleased, protocol 207):**
The actual guest driver reports each protected native State 1/3 oil-use entry
with message 203. The host validates the current owner and mounted automatic
gearbox, then uses its current wear and native calculation/subtraction helpers
to update saved oil once. Duplicate/stale events and changed bindings are
rejected; driver handoffs retain the vehicle's abuse budget. Host observer
callbacks cannot double-charge delegated oil use, and message 202 publishes
the result. Guest saved oil stays protected. Roadmap 2.2 remains partial for
full automatic physics/failures, fitted-part lifecycle and live two-player
save/reload acceptance. Next free ID is 204.
[Validation and limits](docs/BUILDING.md#guest-automatic-gearbox-oil-use-protocol-207-unreleased).

**Host gearbox oil input (local/unreleased, protocol 206):**
The automatic gearbox now calculates shift RPM and stall ratio from the host's
current saved oil level. Message 202 appends exact OilLevel and explicit
availability (28 bytes total); oil-only refills advance its host revision through
live, join and repair paths. Invalid oil withdraws only that input; the automatic
consumer pauses until it returns while Transmission wear readers can continue.
Native arithmetic and local Stallspeed writes remain active, with saved guest
oil drains guarded. Full automatic driving/failure, delegated host oil-loss
progression, fitted-part lifecycle and live two-player acceptance remain open.
Roadmap 2.2 stays partial; next free ID remains 203.
[Validation and limits](docs/BUILDING.md#host-gearbox-oil-input-protocol-206-unreleased).

**Guarded guest drivetrain wear consumers (local/unreleased, protocol 205):**
Four audited native readers now consume exact host driveshaft, gearbox and
rear-axle wear, including while the guest drives or the vehicle is parked.
Transmission DamageType writes and BREAKOFF dispatch, both automatic gearbox
oil drains, and external saved-gearbox integer/float writes are guarded.
Only native reader outputs change. Missing/withdrawn host data, missing metadata
or changed bindings pause the affected consumer until valid input and protection
return. Host/solo readers remain native; saved guest guards survive disconnect.
Message 202 is unchanged and next free ID remains 203. Physical failure effects,
fitted-part lifecycle, tyre wear and live two-player acceptance remain open;
roadmap 2.2 is partial.
[Validation and limits](docs/BUILDING.md#guarded-guest-drivetrain-wear-consumers-protocol-205-unreleased).

**Host drivetrain wear publication (local/unreleased, protocol 204):**
Reliable message 202 publishes exact driveshaft, gearbox and rear-axle saved
wear to every guest, including the driver. Host revision history survives
handoff, parking, capture failure and rediscovery. Read-only snapshot capture
validates the native graph/targets without replaying blocked wear. Joins and
targeted repairs include current state; unavailable data withdraws all three
values. Guests retain copied results without saved writes or failure replay.
Native consumer protection, fitted-part lifecycle and two-player validation
remain open. Next free message ID is 203; roadmap 2.2 remains partial.
[Validation and limits](docs/BUILDING.md#host-drivetrain-wear-publication-protocol-204-unreleased).

**Host periodic drivetrain wear (local/unreleased, protocol 203):**
Fresh accepted driver differential speed now feeds the host's audited native
comparison and divisions for driveshaft, gearbox and rear-axle wear. Native
saved writes and the two-second cadence remain intact. Full graph, canonical
saved-target and writer-cache validation pauses only this FSM when unsafe;
missing/stale input contributes zero, and a mod budget rejects input exceeding
one Wear point per target per cycle. Local driving, body replacement, repaired
bindings and teardown recover native operation. No wire layout or ID changes.
Result publication, complete fitted-part lifecycle, tyre wear and gearbox
failure wear remain open; roadmap 2.2 is still partial.
[Validation and limits](docs/BUILDING.md#host-periodic-drivetrain-wear-protocol-203-unreleased).

**Native differential-speed telemetry (local/unreleased, protocol 202):**
VehicleState now carries explicit availability plus the signed native
differentialSpeed float, separately from RPM, wheel speed and movement speed.
Validated Wear.State 1 GetProperty metadata binds a unique same-body drivetrain;
capture reads the current native field without replaying the getter or changing
scratch/saved data. Live/final sends, accepted copies and fresh owner snapshots
preserve it. Missing, disabled or changed producers withdraw availability and
recover. This supplies the driver input prerequisite; native host wear consumption,
mounted-target authority and resulting condition publication remain open.
VehicleState is 30 bytes including ID; next free message ID is still 202.
[Validation and limits](docs/BUILDING.md#native-differential-speed-telemetry-protocol-202-unreleased).

**Guest periodic drivetrain write protection (local/unreleased, protocol 201):**
The installed `Drivetrain::Wear` graph also subtracts Wear from the driveshaft,
gearbox and rear axle every two seconds. All three writes now use existing guest
admission/entry protection, preserving native rate math and host/solo writers.
The native input is signed differentialSpeed, then abs and a >1 threshold;
its divisors are 42300/31500/22800. Shared host wear during guest driving still
needs validated differential-speed telemetry, native target authority and result
publication. No wire layout changes; next free ID remains 202.
[Validation and limits](docs/BUILDING.md#guest-periodic-drivetrain-write-protection-protocol-201-unreleased).

**Host-confirmed condition release (local/unreleased, protocol 200):**
The host now sends release confirmation 201 to the former guest driver, paired
with the accepted condition and final-pose sequences. Matching same-body pending
releases become approved parked inputs only after local ownership ends. Stale or
forged confirmations, new claims/competing motion, newer state and teardown cannot
revive old condition. Host snapshots/checksums use the approved parked record,
while guest native drift remains detectable. No pose is replayed and no local
saved part is written. New wear, ordinary parked native repair publication and
full flat/rim effects remain open. Next free message ID is 202.
[Validation and limits](docs/BUILDING.md#host-confirmed-condition-release-protocol-200-unreleased).

**Guest condition claim inputs (local/unreleased, protocol 199):**
An actual guest physics claim now copies eligible accepted tyre-health and gearbox
damage before the old owner/incoming state is cleared. Same-body approved parked
state can also seed the claim. The native guarded readers and outgoing captures
use the copy for the claiming player's current body/lease; missing retained inputs
remain unavailable in reports. Saved parts and native caches remain untouched,
and release, competing accepted motion, body replacement and teardown retire it.
Pressure and discrete wheel transitions remain native. This covers claims with
previously received inputs; initial missing-input claims, pre-claim lookup,
post-release host confirmation, durable host wear and full flat/rim repair still
need work. Layouts/IDs are unchanged; both peers require protocol 199.
[Validation and limits](docs/BUILDING.md#guest-condition-claim-inputs-protocol-199-unreleased).

**Observer gearbox condition input (local/unreleased, protocol 198):**
GearboxDamage's native DamageType read now uses the accepted current-owner or
approved parked condition for registered guest observers. Known zero and partial
availability retain their meaning. Reader validation is paired with the Reverse
saved-wear guard; source/cache and saved Data stay intact, invalid bindings pause
and recover, and local/pre-claim drivers and hosts keep native reads. Native
branch decisions now follow the shared byte. Validation passes 3,842 Net tests,
18 launcher tests and 3,806 native checks (29 new; all 3,777 previous retained).
Layouts/IDs are unchanged; driver bootstrap, durable host wear and full
flat/rim/repair behavior remain open.
[Validation and limits](docs/BUILDING.md#observer-gearbox-condition-input-protocol-198-unreleased).

The subsequent tyre-reader safety fix keeps changed healthy/flat readers paused
through repeated preparation, including renamed/reparented consumers and missing
or aliased outputs. Validation also covers native fallback without shared state;
condition capture/application exclude Health that aliases the referenced saved
tyre. This repairs existing protection without changing protocol 198.
[Regression evidence and remaining work](docs/BUILDING.md#tyre-reader-safety-still-protocol-198-unreleased).

**Condition availability and late discovery (local/unreleased, protocol 197):**
Finding pressure no longer freezes wheel discovery. Six explicit availability
bits distinguish missing native inputs from known zero; discovery retries, mask
changes publish, and observers reconcile newly ready targets from eligible
accepted or approved parked state. Missing fields neither overwrite native
condition nor feed shared readers. Availability-aware checksums ignore fields
withdrawn by the authority while still detecting declared native drift. State 65
now has a 17-byte packet; ownership and sender history are unchanged. Validation
passes 3,807 Net tests, 18 launcher tests and 3,777 native checks (30 new, all
3,747 previous retained). Native wear, driver bootstrap, complete wheel physics/repair and parked publication
remain unfinished.
[Validation and limits](docs/BUILDING.md#condition-availability-and-late-discovery-protocol-197-unreleased).

**Native wheel pressure application (local/unreleased, protocol 196):**
Accepted observer pressure now reaches all four Wheel components through eight
audited native property actions. The catalog records vanilla's enabled FL pair
and disabled other six writes. Shared TIRES replay enables them for one entry,
then restores original flags and active-action scheduling in finally. Source,
event, target and operand validation precedes every write; missing/changed
bindings retry without a new packet. Local/pre-claim drivers remain excluded,
and pressure-simulation enablement is unchanged. Validation passes 3,667 Net
tests, 18 launcher tests and 3,747 native checks (28 new; all 3,719 previous
retained). Full driving/stiffness behavior, driver bootstrap, tyre wear and
flat/rim/repair remain open.
[Validation and limits](docs/BUILDING.md#native-wheel-pressure-application-protocol-196-unreleased).

**Parked observer tyre health (local/unreleased, protocol 195):**
An established owner's accepted final pose retains a copied condition record for
guest wheel reads after ownership clears. The record belongs to that live body;
host corrections and new claims retire it. Former-driver cleanup preserves the
approved result, while session teardown removes it. Timeout or unowned final
poses cannot approve a record. A new local claim sends a fresh condition baseline
without resetting its sequence history. Validation passes 3,630 Net tests,
18 launcher tests and 3,719 native checks (18 new; all 3,701 previous retained),
including replay of actual sender claim/final packets. Ordinary parked host
publication, driver bootstrap, host wear and full wheel physics remain open.
[Validation and limits](docs/BUILDING.md#parked-observer-tyre-health-protocol-195-unreleased).

**Observer tyre health inputs (local/unreleased, protocol 194):**
Eight catalogued native wheel reads now consume accepted condition for a guest
observing the current vehicle owner. Healthy and flat states keep shared Health
through native ticks and grip arithmetic without writing saved TireHealth or
rewiring the native source/cache. Synchronous PUNCTURE entry sees the validated
in-flight application. Admission validates reader signatures alongside existing
writer protection; changed/missing readers pause their graph until repaired.
Driver/pre-claim input, host operation, missing state, ownership changes and
disconnect retain native lookup. Validation passes 3,602 Net tests, 18 launcher
tests and 3,701 native checks (26 new; all 3,675 previous retained). Driver
bootstrapping, host wear, physical flat/rim/repair and parked publication remain
open. [Validation and limits](docs/BUILDING.md#observer-tyre-health-inputs-protocol-194-unreleased).

**Guest tyre and gearbox write protection (local/unreleased, protocol 193):**
The catalog now protects nine additional native saved-part writes: continuous
TireHealth wear and flat-entry zeroing on all four Corris wheels, plus reverse-gear
Wear subtraction. Existing guest admission/state-entry guards retire active
writers, preserve native readers and contain changed signatures to their graph.
Solo/host writes remain enabled. Protocol 193 requires peers with this guest
simulation behavior; message layouts/IDs are unchanged. Validation passes 3,575
Net tests, 18 launcher tests and 3,675 native checks (28 new; all 3,647 previous
retained). Native condition inputs, host wear under guest driving, physical
flat/rim effects and parked publication remain open.
[Validation and limits](docs/BUILDING.md#guest-tyre-and-gearbox-write-protection-protocol-193-unreleased).

**Condition stream ownership (local/unreleased, protocol 192):**
Tyre/drivetrain reports require the authenticated current vehicle owner and a
fresh sequence from that sender. Unowned claims, former drivers, duplicate zero
and stale packets cannot write native state or relay. Per-sender history survives
handoffs; readmission/session clear resets it. Guest-owned host snapshots copy
accepted condition rather than local scratch and reserve sequence 65535 without
advancing live baselines. Reliable final condition precedes item ownership release.
Validation passes 3,570 Net tests, 18 launcher tests and 3,647 native checks (21 new;
all 3,626 previous retained). Parked publication and native wear/flat/rim
consumer/writer isolation remain open.
[Validation and limits](docs/BUILDING.md#condition-stream-ownership-protocol-192-unreleased).

**Native tyre pressure units (local/unreleased, still protocol 191):**
VehicleCondition capture/apply now respects native TirePressure.Data kPa values:
190 stays 190 across the existing bar×100 wire representation. The previous
boundary saturated native 190 to wire 255 and wrote back 2.55. The native audit
also reopens roadmap 2.2: strict stream ownership/parked publication, wheel-health
and gearbox scratch bindings, and safe flat/rim effects still need implementation.
Validation passes 3,527 Net tests, 18 launcher tests and 3,626 native checks (13 new;
all 3,613 previous retained).
[Validation and limits](docs/BUILDING.md#native-tyre-pressure-units-still-protocol-191-unreleased).

**Passenger vehicle discovery recovery (local/unreleased, still protocol 191):**
Passenger geometry now follows the current registered vehicle body. Removed or
replaced bodies discard old anchors and safely release a local rider; unchanged
cars keep their bindings. Accepted remote occupancy/history survives geometry
refresh so anchors can rebind to the same logical vehicle. Host seat validation
refreshes the registry before checking new-entry proximity or continuing-seat
existence. Missing native anchors retry on later scans. Validation passes 3,509
Net tests, 18 launcher tests and 3,613 native checks (16 new; all 3,597 previous
retained). Supported player seats remain Corris/Sorbet; taxi seating is not enabled.
Full scene reconstruction, physical seat/door and live two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#passenger-vehicle-discovery-recovery-still-protocol-191-unreleased).

**Passenger death and respawn (local/unreleased, protocol 191):**
Local death/respawn releases seat parenting before further seat pins. Host death
and respawn retire canonical occupancy; observer events remove avatar anchors,
and group death clears every seat. Seat message history survives retirement so
dead or delayed claims cannot revive an occupied seat. A new living claim is
required after respawn. Native destroyed controllers and new respawn parenting
remain intact. Full native death/save/load, physical seat/door and two-player
survival acceptance remain open.
[Validation and limits](docs/BUILDING.md#passenger-death-and-respawn-protocol-191-unreleased).

**Passenger cabin heating (local/unreleased, protocol 190):**
Seated local passengers use their car's available current cabin sample in both
native body-temperature reader branches. The native calculation retains body
progression, clothing, sweat and cadence. VehicleClimate adds availability flag 8
without changing its 23-byte layout; unavailable data cannot manufacture heat.
Host accepted seats, existing guest optimistic seating, physical parenting,
current owner and the three-second remote hold gate use of the sample. Native
heat sources, reader operands, PlayerIn and ownership stay intact. Full physical
seat/door, loading/respawn and two-player winter-survival acceptance remain open.
[Validation and limits](docs/BUILDING.md#passenger-cabin-heating-protocol-190-unreleased).

**Native body warmth persistence (local/unreleased, protocol 189):**
Reports and reconnect restoration now use the PlayerTemp global updated by the
native body calculation. The previous local Temperature source is air/heat-source
input; old profile samples are retired without discarding other needs. Explicit
availability preserves zero and deferred restoration. The extracted pure profile
codec preserves optional dirtiness/BAC/warmth independently. Validation passes
3,467 Net tests and 3,508 native checks (13 new; all 3,495 previous retained).
Passenger heat delivery, full spawn/seat/door flow and live winter-survival
acceptance remain open; this change is local and unreleased.
[Validation and limits](docs/BUILDING.md#native-body-warmth-persistence-protocol-189-unreleased).

**Passenger condensation inputs (local/unreleased, protocol 188):**
Player poses now carry available native sweat (39 bytes). The established climate
producer combines local occupancy with fresh, living remote passengers accepted
in that car, retaining the game's minimum per-person and maximum cabin fogging
rate. Scoped native readers preserve local entry, global sweat and vehicle
ownership; empty cabins use the native default. Validation passes 3,429 Net tests,
18 launcher tests and 3,495 native checks (56 new; all 3,439 previous retained).
Passenger body warmth, full seat/door flow and live rendered thermal/ownership
acceptance remain open. This change is local and unreleased.
[Validation and limits](docs/BUILDING.md#passenger-condensation-inputs-protocol-188-unreleased).

**Native condensation presentation (local/unreleased, protocol 187):**
Condensation now updates native/material transparency directly. White glass tint
can no longer become full fog or overwrite native sweat-rate calculations, and
received cabin degrees no longer overwrite the already divided defrosting rate.
The redundant Fog byte is retired in place; Frost remains the actual accumulated
condensation. Native tint, shader cutoff and exterior panes stay independent.
Validation passes 3,391 Net tests, 18 launcher tests and 3,439 native checks
(45 new; all 3,394 previous retained), with clean builds.
Passenger sweat inputs follow in v188 above; warmth and live rendered climate
acceptance remain open.
[Validation and limits](docs/BUILDING.md#native-condensation-presentation-protocol-187-unreleased).

**Vehicle climate occupancy isolation (local/unreleased, protocol 186):**
Received cabin occupancy stays separate from native local entry, so a timed-out
climate hold cannot turn an observer into a driver. Reports include accepted
passenger seats for that car; roster departure stops contributing immediately,
and reused player slots clear their old seat. Native entry/reset and driver
parenting remain local. Validation passes 3,391 Net tests, 18 launcher tests and
3,394 native checks (51 new; all 3,343 previous retained), with clean builds.
This does not complete passenger sweat/condensation or
warmth, full seat/door flows, or live two-player acceptance.
[Validation and limits](docs/BUILDING.md#vehicle-climate-occupancy-isolation-protocol-186-unreleased).

**Vehicle climate ownership (local/unreleased, protocol 185):**
Climate now follows the established vehicle owner; passengers, nearby guests and
unowned native ignition cannot compete with that source. The host resumes parked
cars after release. Stale/invalid reports neither refresh holds nor relay, and
per-sender history survives driver changes. Final climate travels reliably before
the release pose; host snapshots copy accepted guest state rather than local drift.
Ownership changes stop old presentation immediately, while readmission/session
cleanup resets the corresponding state. Validation passes 3,381 Net tests, 18
launcher tests and 3,343 native checks (22 new; all 3,321 prior retained), with clean
builds. Full native climate behavior, passenger-only occupancy, scraping intents
and live driving/parking/join/reconnect acceptance remain open.
[Validation and limits](docs/BUILDING.md#vehicle-climate-ownership-protocol-185-unreleased).

**Independent window ice (local/unreleased, protocol 184):**
VehicleClimate 61 now carries all six exterior cutoff values with availability,
so scraping one pane or heating the rear window no longer paints every pane with
the windshield's result. Missing/nonfinite readings omit only the affected pane;
interior frost stays independent. The shipped catalog restores verified taxi
climate paths alongside Corris and Sorbet. Existing climate authority and controls
remain unchanged. Validation passes 3,337 Net tests, 18 launcher tests and 3,321
native checks (66 new; all 3,255 prior retained), with clean builds. Full climate
authority, scraping intents and live driving/join/handoff acceptance remain open.
[Validation and limits](docs/BUILDING.md#independent-window-ice-protocol-184-unreleased).

**Guest rear-window heating element (local/unreleased, protocol 183):**
HeaterState 200 appends the host body's independent heating-element option, making
it 12 bytes. Native guest rear defrosting now checks host wiring and element state
plus the existing switch before demand and heat additions. Body option, VIN and
saved heater/battery fields stay intact. Capture waits for native body/save states;
rear-only updates use existing revisions, snapshots, keepalive and cleanup.
Validation passes 3,305 Net tests, 18 launcher tests and 3,255 native checks (46 new;
all 3,209 prior retained), with clean builds. Full climate, physical glass-strip
presentation and live driving/reconnect/handoff acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-rear-window-heating-element-protocol-183-unreleased).

**Guest heater hoses and coolant gate (local/unreleased, protocol 182):**
Native heater inlet/outlet reads now consume the host's existing state-195 hose
installation bits. Saved hose assemblies and native read timing/cache behavior
are preserved. The existing host radiator projection supplies Cooling.WaterLevel;
native clamp/removal logic and the heater's 0.5 threshold plus two-hose gate decide
whether heating proceeds. No wire fields or message IDs change; next ID is 201.
Validation passes 3,279 Net tests, 18 launcher tests and 3,209 native checks (35 new;
all 3,174 prior retained), with clean builds. Rear-window heating-element inputs,
physical replication, full climate and live driving/reconnect/handoff acceptance
remain open. [Validation and limits](docs/BUILDING.md#guest-heater-hose-inputs-protocol-182-unreleased).

**Guest heater and rear-defroster wiring (local/unreleased, protocol 181):**
Host WiringState 193 now includes independent heater-control, heater-unit and
rear-window circuits (source IDs 9–11). Native guest reads consume host connection
state while preserving saved wiring and original callback timing. The heater's
native supply gate requires both host circuits and heater installation; the rear
defroster retains its separate circuit gate. Join/resync, revision handling and
session cleanup cover all eleven sources. Existing wire layouts are unchanged;
next message ID remains 201. Validation passes 3,269 Net tests, 18 launcher tests
and 3,174 native checks (34 new; all 3,140 prior retained), with clean builds.
Hose/heating-element inputs, physical heater/battery/wire replication and live
driving/handoff acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-heater-and-rear-defroster-wiring-protocol-181-unreleased).

**Guest heater condition and saved-part protection (local/unreleased, protocol 180):**
Host-only HeaterState 200 now supplies settled heater installation and mounted
wear independently of the driver. Guest native heater readers consume the host
state without overwriting saved mount/part fields; unseeded/removal state is
false/zero. Native wear/broken-blower arithmetic remains active while saved heater
Data and external scalar writes are protected. Join/resync, revision conflicts,
keepalive and session cleanup follow the existing battery pattern. Existing wire
layouts remain unchanged; next ID is 201. Validation passes 3,243 Net tests,
18 launcher tests and 3,140 native checks (48 new; all 3,092 prior retained), with
clean builds. Other heater wiring/pipe prerequisites, physical heater/battery
replication, remaining accessory demand and full live starting/driving/handoff
acceptance remain open. [Validation and limits](docs/BUILDING.md#guest-heater-condition-protocol-180-unreleased).

**Guest starter wear (local/unreleased, protocol 179):**
Native Fuel Mixture cranking duration now reaches the host through request 199.
The guest observes the original per-second callback without changing saved starter
wear. Host ownership/replay/time-budget and native battery/part/wiring checks gate
its own rate × mounted durability × reported duration. Native helper operands are
restored, and the native mount publishes wear back to physical part Data. Final
ownership release flushes pending time; invalid/rejected work cannot replay after
repair. Existing state layouts remain unchanged; next ID is 200. Validation and
remaining two-player/physical handoff limits are recorded in
[BUILDING.md](docs/BUILDING.md#guest-starter-wear-protocol-179-unreleased).

**Guest starter battery draw (local/unreleased, protocol 178):**
Five native Starter draw callbacks now count guest cranking without writing the
guest's saved battery. Reliable request 198 batches loaded/unloaded counts; the
host validates authenticated current ownership, replay/work limits, its settled
battery, starter/wiring/flywheel and native action shape, then applies its own
native rate. Batches flush before final ownership release. Rejected native work
cannot replay after repair. Existing state layouts stay unchanged; next ID is 199.
Validation passes 3,182 Net tests, 18 launcher tests and 3,055 native checks (39 new;
all 3,016 previous retained), with clean builds and matching local payloads. Other
accessory demand, starter wear and live electrical/physical handoff remain open.
[Validation and limits](docs/BUILDING.md#guest-starter-battery-draw-protocol-178-unreleased)

**Guest accessory battery inputs (local/unreleased, protocol 177):**
Host BatteryState 194 now includes the settled mount's ChargeMax (15 bytes with ID).
Native guest accessory, fan, wiring and current/power reads use host Installed,
Charge and ChargeMax in local consumer variables, preserving the saved battery.
Unseeded/removal inputs provide false/zero; native caching, unrelated sources and
host/solo behavior remain intact. The catalog requires all three read fields and
existing write protection. Validation passes 3,154 Net tests, 18 launcher tests and
3,016 native checks (71 new; all 2,945 previous retained), with clean builds and
matching local payloads. Guest electrical demand and live acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-accessory-battery-inputs-protocol-177-unreleased)

**Guest battery external-write protection (local/unreleased, protocol 176):**
Native current-meter, accessory, fan, radio and wiring routines can write to the
saved battery mount even while its own Data FSM is paused. A catalog destination
guard now blocks external native float writes to that mount, preserving consumer
calculations and other destinations. It follows native target caching and retains
known battery identities through movement, metadata loss and disconnect; newly
discovered consumers/targets are covered at dispatch. Battery input admission
requires this protection. Wire layouts are unchanged. Validation passes 3,134 Net
tests, 18 launcher tests and 2,945 native checks (83 new; all 2,862 prior retained),
with clean builds. Guest electrical demand reaching the host remains unfinished.
[Validation and limits](docs/BUILDING.md#guest-battery-external-writes-protocol-176-unreleased)

**Host battery charging/drain from guest RPM (local/unreleased, protocol 175):**
All three native Corris Electrics RPM reads now use the accepted guest driver's
RPM: the >400 running gate, charging rate and battery drain. Host native wiring,
installation, alternator damage/condition, temperature and charge/wear writers
remain authoritative. The independent group binds during host updates before the
first report and retries changed bindings; missing/stale/mismatched inputs use
native zero-RPM behavior. Message 60 stays 25 bytes. Native operands restore on
nesting/exceptions; no global RPM overwrite or extra simulation tick is added.
Validation passes 3,125 Net tests, 18 launcher tests and 2,862 native checks (111 new;
all 2,751 previous retained), with clean Debug/Release builds. Native fixture checks
include actual battery Charge/ChargeMax and alternator Wear writes against controlled
sources. Electrical loads, starter draw, full physical/thermal handoff and live
two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#host-electrical-rpm-inputs-protocol-175-unreleased)

**Guest electrical temperature (local/unreleased, protocol 174):**
The main electrics and interior-light battery checks now use host engine heat for
their native cold penalty and voltage decision. Guest charging calculations use
the same host heat for the native temperature-dependent limit. These three scoped
reads preserve native arithmetic, local RPM and existing battery/alternator input
authority; they never overwrite stored charge or global heat. State 197 stays
19 bytes. The electrical group validates and recovers independently of fuel/oil
and cabin inputs. Validation passes 3,101 Net tests, 18 launcher tests and 2,751
native checks (89 new; all 2,662 previous retained), with clean Debug/Release builds.
Protocol 175 adds host charging/drain from guest-driver RPM. Remaining thermal
consumers and live electrical acceptance remain open. [Validation and limits](docs/BUILDING.md#guest-electrical-temperature-protocol-174-unreleased)

**Guest cabin and heater temperature (local/unreleased, protocol 173):**
Corris cabin temperature limits now consume host engine heat, and the native
heater calculation consumes host coolant heat, including while a guest drives.
The native temperature/blower/direction calculations and cabin cooling remain
active. Scoped reads preserve native sources and saved data; late discovery,
unavailable heat, driver changes, cleanup and malformed binding recovery are
covered. State 197 remains 19 bytes. Validation passes 3,074 Net tests, 18 launcher
tests and 2,662 native checks (66 new; all 2,596 previous retained), with clean
Debug/Release builds. Existing climate reports retain their behavior; full cabin
and frost authority, other thermal consumers and live two-player acceptance remain
open. [Validation and limits](docs/BUILDING.md#guest-cabin-and-heater-temperature-protocol-173-unreleased)

**Guest fuel/oil engine temperature (local/unreleased, protocol 172):**
Host state 197 now carries native EngineTemp alongside coolant (19 bytes including
ID). Six scoped guest reads use this temperature for fuel priming, mixture density,
oil viscosity and oil pressure. Native thresholds, clamps, subsequent arithmetic,
RPM and part/fluid inputs remain in charge. Both temperatures share availability
and revisions; source failure retires both. Reads restore original operands on
nested dispatch or exceptions, with no global or saved-part writes. Validation
passes 3,049 Net tests and 2,596 native checks (55 new; all 2,541 prior labels/counts
retained), with clean Debug/Release builds. Other thermal consumers/writers, full
engine handoff and live two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-engine-temperature-inputs-protocol-172-unreleased)

**Host coolant dashboard (local/unreleased, protocol 171):**
Corris publishes the host's native coolant temperature as reliable state 197,
independent of the current physics owner. Guests, including their local driver,
consume these degrees through the existing native gauge read hook; driver packets
cannot replace the host's dashboard with guest heat. The 15-byte message retains
negative temperatures and values above 120°C. Readiness, unavailable sources,
vehicle/session revisions, join/resync, late scene discovery and cleanup are
explicit. This is dashboard authority; shared thermal consumers and physical
engine-temperature handoff remain unfinished. Validation passes 3,014 Net tests,
18 launcher tests and 2,541 native checks (23 new; all previous 2,518 retained),
with clean builds and matching local launcher/disposable-game payloads.
[Validation and limits](docs/BUILDING.md#host-coolant-dashboard-protocol-171-unreleased)

**Host cooling RPM inputs (local/unreleased, protocol 170):**
The host now uses accepted guest RPM for Corris pump circulation, mechanical fan
cooling and the running leak check. These join movement speed in one validated
five-reader cooling group. Native belt/part installation, pump wear/efficiency,
fan modifier and temperature calculations stay in charge; missing or expired
samples use native stopped behavior. The local catalog is now `vehicleCooling`,
and cooling hooks share one implementation separate from heat/wear Harmony state.
VehicleState 60 stays 25 bytes including ID. Validation passes 2,984 Net tests,
18 launcher tests and 2,518 native checks (77 new; every prior 2,441 label/count
retained), with clean builds. Thermal replication to the guest,
complete handoff, remaining damage paths and live two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#host-cooling-rpm-inputs-protocol-170-unreleased)

**Host cooling movement speed (local/unreleased, protocol 169):**
VehicleState 60 appends actual movement speed and availability, keeping dashboard
wheel speed separate (25 bytes including ID). Corris captures native
Measurements/GetSpeed and its km/h conversion. The host supplies the accepted
guest movement sample to native Cooling airflow and the stationary-temperature
check; ambient/part inputs, rates and thermal writers remain native. Missing or
expired movement uses stationary speed; ownership, seating, guest save protection
and disconnect retain native inputs. Validation passes 2,968 Net tests, 18 launcher
tests and 2,441 native checks (34 new; every prior 2,407 label/count retained), with
clean builds. Thermal replication to the guest, complete
handoff, remaining damage paths and live two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#host-cooling-movement-speed-protocol-169-unreleased)

**Host engine heating under guest driving (local/unreleased, protocol 168):**
VehicleState 60 now appends native torque availability/value (22 bytes including
ID). The host uses the accepted RPM/torque pair in native HeatGeneration while
retaining its friction, rate limits, per-second temperature writer and start/stop
hysteresis. Missing/stale/unavailable load follows native stop behavior; local
control, guest mode and disconnect restore native inputs. Heat hooks have their
own Harmony state, avoiding the native crash found when sharing the wear hook's
declaring type. Validation passes 2,944 Net tests, 18 launcher tests and 2,407
native checks (34 new; all 2,373 previous labels/counts retained), with clean builds.
Cooling/speed authority, temperature replication to the guest driver, complete
thermal handoff and live two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#host-engine-heating-inputs-protocol-168-unreleased)

**Host oil contamination under guest driving (local/unreleased, protocol 167):**
Accepted driver RPM now feeds native Oil/Oil contamination alongside the six
pressure/mechanical-wear readers. Host filter condition, the native minimum
contamination rate, persistent writers and 1.1-second cadence remain in charge;
packets add no ticks. All seven bindings validate together, and protected guest
writers remain blocked through disconnect. VehicleState layout is unchanged.
Validation passes 2,918 Net tests, 18 launcher tests and 2,373 native checks
(21 new; all 2,352 previous labels/counts retained), with clean builds and
matching Debug payloads. A native heat audit confirms separate torque/friction
inputs and start/stop thresholds; shared heat, thermal handoff, other damage
paths and live two-player driving acceptance remain open.
[Validation and limits](docs/BUILDING.md#host-oil-contamination-input-protocol-167-unreleased)

**Host oil-pressure and mechanical-wear inputs (local/unreleased, protocol 166):**
Accepted guest VehicleState RPM now feeds six native Pressure/Wearing operands
on the host, covering fourteen native part-wear targets. Part state, temperature,
oil and wear arithmetic stay host-owned. Scoped operands restore after native
execution or exception; missing/stale/mismatched telemetry supplies zero RPM
while the car remains delegated. Local seating/ownership and disconnect return
to native reads. Any changed member disables the complete input group before
substitution. VehicleState remains 17 bytes including ID; protocol 166 changes
semantics, not layout. Validation passes 2,916 Net tests, 18 launcher tests and
2,352 native checks (21 new; all previous 2,331 labels/counts retained), with clean
builds and matching Debug payloads. Full shared heat progression, thermal handoff,
other damage/wear paths and live two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#host-oil-pressure-and-wear-inputs-protocol-166-unreleased)

**Vehicle temperature source and dashboard correction (local/unreleased, still protocol 165):**
VehicleState 60 now reads actual coolant degrees before dashboard clamps for
Corris, Sorbet and Machtwagen. A catalog-selected native reader supplies each
car's division/clamp mapping. A scoped read hook keeps accepted observer degrees
in place between packets while native gauge actions continue, and yields to a
local driver, ownership changes, timeout and disconnect. Physical cooling state
is never written by presentation. Validation passes 2,883 Net tests, 18 launcher
tests and 2,331 native checks (57 new; every previous 2,274 label/count retained),
with clean builds. Existing 0–120 °C wire quantization and ownership are unchanged;
full thermal handoff, host wear during guest driving and live two-player testing
remain open. [Validation and limits](docs/BUILDING.md#vehicle-temperature-sources-and-gauges-still-protocol-165-unreleased)

**Cooling ambient inputs (local/unreleased, protocol 165):** EngineBlockState
195 supplies the host RoofCheck's shelter-adjusted temperature to Cooling/Reset.
Native air-cooling math uses that input; guest RoofCheck and arrival scratch stay
intact. Missing temperature pauses only Cooling. Verified pending input permits
admission so a connection can deliver the snapshot, while malformed bindings
still reject admission and simulation readiness stays false. Valid proxies are
reused during the wait. The current profile has 102 scalar sources/182 reads,
one valve array/eight reads, nine consumers and 22 paused saved graphs.
Validation passes 2,855 Net tests, 18 launcher tests and 2,274 native checks
(49 new; all 2,225 earlier label/counts preserved), with clean builds. All native
Cooling GetFsm reads now have input projections. Dynamic engine temperatures,
host wear during guest driving, physical reconstruction and live two-player
acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-cooling-ambient-inputs-protocol-165-unreleased)

**Cooling airflow inputs (local/unreleased, protocol 164):** EngineBlockState
195 supplies grille, winter cover, stock bonnet and fiberglass bonnet installation
plus three mounted airflow modifiers to seven native Cooling reads. Native Reset
and branch order remain intact: cover effect requires a grille, and the stock
bonnet takes priority over fiberglass. Four fixed saved mount Data graphs remain
paused through disconnect. The v164 profile has 101 scalar sources/181 reads,
one valve array/eight reads, nine consumers and 22 paused graphs. Validation passes
2,813 Net tests, 18 launcher tests and 2,225 native checks (125 new; every earlier
2,100 label/count retained), with clean builds. Physical body assembly, the RoofCheck
temperature read, dynamic thermal state, guest-driven host wear and live two-player
acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-cooling-airflow-inputs-protocol-164-unreleased)

**Coolant hose leak inputs (local/unreleased, protocol 163):** EngineBlockState
195 supplies all four hose installation bits/clamp totals and mounted
carburettor tightness to six native Cooling reads. Host values drive the
missing-bottom-hose and combined coolant-leak decisions while shared scratch,
normal reset/accumulation and saved guest Data remain intact. Fixed hoses are
independent of radiator/block/head; carburettor tightness joins its atomic
mounted intake group. All four saved hose Data graphs stay paused through
disconnect, preventing wear copies, clamp resets and detachment. The v163 profile
has 97 scalar sources/174 reads, one valve array/eight reads, nine consumers
and 18 paused graphs. Validation passes 2,746 Net tests, 18 launcher tests and
2,100 native checks (121 new; all 1,979 earlier checks retained), with clean
builds. Physical hose reconstruction, clamp/leak presentation, grille/hood
airflow, remaining thermal inputs, guest-driven host wear and live two-player
acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-coolant-hose-leak-inputs-protocol-163-unreleased)

**Radiator cooling inputs (local/unreleased, protocol 162):** EngineBlockState
195 supplies radiator installation, mounted wear/coolant, cap pressure and
electric-fan efficiency to five native Cooling reads. The fixed mount is
independent of the engine block/head and supports stock plus both upgraded
radiators. Native coolant clamps, pressure branches and fan hysteresis use
host inputs while arrival scratch and saved guest Data remain intact. The
radiator's eleven-state Data graph stays paused through disconnect, blocking
active wear/coolant copies and removal. The v162 profile has 92 scalar sources/
168 reads, one valve array/eight reads, nine consumers and 14 paused graphs.
Validation passes 2,672 Net tests, 18 launcher tests and 1,979 native checks
(74 new; all 1,905 earlier checks retained), with clean builds. Physical
radiator reconstruction, cap/filling controls, hoses, grille/hood airflow,
remaining thermal inputs, guest-driven host wear and live two-player
acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-radiator-cooling-inputs-protocol-162-unreleased)

**Rocker-cover leak input (local/unreleased, protocol 161):** EngineBlockState
195 carries cover installation and mounted bolt tightness from the host's
installed head. The native Oil/Valve Cover read and leak arithmetic use host
values without replacing shared references, arrival scratch or saved guest
scalars. Cover Data protection follows the moving head and blocks native wear
copying, cap hiding and detachment through disconnect. Missing/malformed covers
clear independently of other engine inputs. The v161 profile has 91 scalar sources/
163 reads, one valve array/eight reads, nine consumers and 13 paused graphs.
Validation passes 2,618 Net tests, 18 launcher tests and 1,905 native checks
(47 new; all 1,858 earlier checks preserved), with clean builds. Physical cover
reconstruction, oil-cap/filling controls, remaining engine dependencies and live
two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-rocker-cover-leak-input-protocol-161-unreleased)

**Oilpan engine inputs (local/unreleased, protocol 160):** EngineBlockState 195
now carries mounted oilpan condition, tightness, oil quantity, contamination and
viscosity. Capture follows the installed native block independently of its head;
invalid or transitional sources clear the group atomically. Seven native reads
in Oil/Wearing/Cylinders use host values without changing saved guest scalars,
shared references or scratch on arrival. Saved mount protection follows block
movement and prevents native fluid copying/removal through disconnect. There
are 90 scalar sources/162 reads plus one valve array/eight reads, nine consumers
and 12 paused graphs. Validation passes 2,570 Net tests, 18 launcher tests and
1,858 native checks (67 new; all 1,791 prior checks retained), with clean builds.
Physical oilpan reconstruction, filling/draining controls, remaining engine
inputs, delegated-driving host wear and live two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-oilpan-inputs-protocol-160-unreleased)

**Valve adjustment engine inputs (local/unreleased, protocol 159):**
EngineBlockState 195 carries eight ordered valve settings and availability from
the host's installed head. Missing or malformed arrays clear atomically; unrelated
engine inputs survive. Eight native ArrayListGet readers use owned arrays while
preserving native scratch, arithmetic, tolerance decisions, saved head data and
wear protection. Arrival never replays calculations. Native owners restore before
proxy cleanup; action replacement, disconnect and destroyed consumers are covered.
There are 87 scalar sources/155 scalar reads plus one valve-array source/eight
reads across nine consumers; paused/writer/factory counts are unchanged. Validation
passes 2,511 Net tests, 18 launcher tests and 1,791 native checks (55 new; all 1,736
previous checks preserved). Builds are clean; native tests use Debug and Release
is build-only. Valve controls subsequently gained the limited v215 adapter above;
full fitted-head interaction, thermal/oil and other engine
inputs, delegated-driving host wear and live two-player acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-valve-adjustment-inputs-protocol-159-unreleased)

**Exhaust performance inputs (local/unreleased, protocol 158):**
EngineBlockState 195 appends four independent native exhaust performance
triplets and a section mask. Headers follow the same installed head as intake;
front/rear pipes and muffler use their native vehicle mounts independently of
the block/head. Supported stock and upgraded identities are catalogued per
section. Twelve Valves reads feed the normal native power/torque additions
without replaying calculations on packet arrival. All four saved mount Data
graphs remain protected through disconnect, including headers after head
movement. Invalid sources clear only their section. The profile has 87 sources,
155 reads, nine consumers, 11 paused graphs and unchanged 75 scalar guards in
11 graphs and 38/31 factories. Validation passes 2,456 Net tests, 18 launcher
tests and 1,736 native checks (98 new; all 1,638 previous checks preserved).
Builds are clean; Debug is native-tested and Release is build-only. Physical
exhaust reconstruction/sound, separate racing-front/sidepipe/tip routing,
other engine inputs, delegated-driving host fuel/wear and live two-player
acceptance remain open.
[Validation and limits](docs/BUILDING.md#guest-exhaust-inputs-protocol-158-unreleased)

**Intake filtration and performance (local/unreleased, protocol 157):**
EngineBlockState 195 appends six carburettor/air-cleaner performance floats and
adds AirCleanerInstalled=32. Both mounts are captured from the same installed
head/block observation; each remains independent of the other. Host filter
installation selects the native FuelLine filtration branch. Valves reads the
host's DataPower, DataTorque and DataPowerAdd for both intakes and performs the
normal additions into engine power/torque totals. Invalid/transitional sources
clear only dependent fields; packet arrival preserves native scratch and events.
Saved air-cleaner Data stays protected after head movement and disconnect,
preventing removal wear, performance clearing and detachment. The profile has
83 sources/143 reads across nine consumers, seven paused graphs, 75 scalar
guards in 11 graphs and unchanged 38/31 factories. Validation passes 2,337 Net
tests, 18 launcher tests and 1,638 native checks (48 new; all 1,590 prior checks
preserved). Builds are clean; native verification uses Debug and Release is
build-only. Physical intake reconstruction, exhaust/other engine inputs,
host fuel/wear during guest driving and live two-player starting remain open.
[Validation and limits](docs/BUILDING.md#guest-intake-inputs-protocol-157-unreleased)

**Carburettor fuel and mixture inputs (local/unreleased, protocol 156):**
EngineBlockState 195 appends FuelChamber, CarbReserve and SettingMixture and
adds CarburettorInstalled=16. The host captures the live mount beneath the same
installed head/block observation, accepting stock, two-barrel and four-barrel
native identities. Missing/invalid/transitional sources clear only dependent
carburettor fields. FuelLine and Mixture use four native reads with their
original clamps, arithmetic and cadence. The guest's saved fuel, tuning and
assembly remain untouched. Saved carburettor Data protection follows a moved
head; paused saved-part graphs now drain previously active work as well as
blocking future ticks and entries through disconnect. The profile has 80
sources/136 reads across nine consumers, six paused graphs, 75 scalar guards
in 11 graphs and unchanged 38/31 factories. Validation passes 2,252 Net tests,
18 launcher tests and 1,590 native checks (54 new; all 1,536 previous checks
retained). Core Debug/Release, both Net targets, probe and Launcher build cleanly;
native checks use Debug. Physical carburettor reconstruction, other engine
inputs, host fuel/wear simulation during guest driving and live two-player
starting remain open.
[Validation and limits](docs/BUILDING.md#guest-carburettor-inputs-protocol-156-unreleased)

**Cylinder-head combustion input (local/unreleased, protocol 155):** Existing
EngineBlockState 195 now carries HeadInstalled=8, with no new fields or message
IDs. The host captures it only from the settled head mount beneath its installed
native block and a valid attached native head. Missing/foreign/loading head
sources clear that flag independently of block installation/condition. Cylinders
Powertrain reads host installation through the original native gate; packet
arrival preserves scratch and the guest's saved head never supplies a fallback.
Guest head Data stays paused through disconnect, with protection following the
VIN101 block after movement/renaming. This prevents removal wear copies and
detachment of the saved head. Catalog source selection also rejects mixed source
kinds. There are 78 sources/132 reads across nine consumers, 75 scalar guards in
11 graphs, five paused graphs and unchanged 38/31 factories. Validation passes
2,194 Net tests, 18 launcher tests and 1,536 native checks (39 new; all 1,497 prior
checks preserved); builds are clean, with native verification on Debug. Physical
block/head reconstruction, valve arrays, thermal/oil dependencies, other engine
inputs and live two-player starting remain open.
[Validation and limits](docs/BUILDING.md#guest-cylinder-head-input-protocol-155-unreleased)

**Gearbox starter input (local/unreleased, protocol 154):** GearboxState 196
supplies the host mount's native Type to Starter Check automatic. The exact
integer read and manual/automatic threshold remain native; the driver retains
SimAutomatic's local P/N selector check. Missing/transitional observations pause
Starter rather than becoming a manual default. Valid input lets the existing
protection guard resume a blocked native attempt; receipt alone never replays it.
Host capture waits for settled native attachment or Idle and retains vanilla's
idle Type. Ordered revisions, 5 s keepalives and join/vehicle resync preserve live
changes. Guest gearbox mount Data stays paused through disconnect, protecting
active wear/oil/integer-damage copies and removal detachment. The profile has
77 sources/131 reads, nine consumers, 75 scalar guards in 11 graphs, four paused
graphs and unchanged 38/31 factories. Validation passes 2,154 Net tests, 18 launcher
tests and 1,497 native checks (48 new; all 1,449 prior checks preserved), with
clean Debug/Release, Net, probe and launcher builds. Native verification uses
Debug. Physical transmission reconstruction, ratios and other gearbox inputs,
head and other engine dependencies, guest-driving host wear and live two-player
starting remain open.
[Validation and limits](docs/BUILDING.md#guest-gearbox-starter-input-protocol-154-unreleased)

**Engine block inputs (local/unreleased, protocol 153):** EngineBlockState 195
supplies native installed/condition/damage observations to Starter, Oil and
Cooling. The host waits for the block mount's settled Update/Idle boundary and
valid active part attachment. Guest direct starter targets are redirected
without changing their original literal references; the running read remains
every-frame. Native comparisons, scratch timing and named db_Block references
are preserved. Guest block mount Data stays paused through disconnect so removal
cannot copy wear back or detach the saved engine. Its disabled continuous wear
copy stays disabled. Ordered revisions, keepalives and join/vehicle resync
preserve updates. The profile has 76 sources/130 reads across nine consumers,
75 scalar guards across 11 graphs and three paused graphs. Factories remain
38/31. Physical block reconstruction, gearbox/head inputs, guest-driving host
wear and full two-player operation remain open. Validation passes 2,117 Net
tests, 18 launcher tests and 1,449 native checks (53 new; all 1,396 earlier
checks retained). Core Debug/Release, Net, probe and launcher builds are clean;
native checks use Debug.
[Validation and limits](docs/BUILDING.md#guest-engine-block-inputs-protocol-153-unreleased)

**Battery engine inputs (local/unreleased, protocol 152):** BatteryState 194
publishes the host mount's installed/charge inputs to three native Electrics
reads. Capture waits for stable native attachment and valid ActivePart Data;
missing/loading sources are unavailable and recover. Ordered revisions, 5 s
keepalives and join/vehicle resync preserve pending live updates. Guests redirect
only the reads, pause the native battery mount and guard ten battery writes in
Starter/Electrics, keeping saved battery values and references intact through
disconnect. Native cold-power and voltage math remains unchanged. The input
profile has 73 sources/126 reads across nine consumers; protection covers 75
scalar writes in 11 graphs plus two paused graphs. Physical battery changes,
guest-driver draw reaching the host, other electrical writers, remaining engine
dependencies and full two-player operation remain open. Validation passes 2,076
Net tests, 18 launcher tests and 1,396 native checks, retaining all 1,357 earlier
checks and adding 39 battery checks. Debug/Release builds are clean; native
checks use Debug.
[Validation and limits](docs/BUILDING.md#guest-battery-engine-inputs-protocol-152-unreleased)

**Starter flywheel prerequisite (local/unreleased, protocol 151):** The native
Starter Check Flywheel Installed read now shares the accepted/applied host
attachment used by Cylinders. VIN120, FLYWHEELa0, FLYWHEELb0 and VIN138 all use
the same live native mount under the engine block. The starter can no longer
borrow an installed flywheel from a different guest save. Missing, pending,
removed, conflicting or mismatched replicas select No Flywheel; a unique applied
host copy selects Prepare starting. Native scratch timing, shared db_Flywheel and
saved Data remain intact. This installation gate adds no wear or bolt threshold.

The profile has 72 sources/123 reads across nine consumers, with 38 replacement
and 31 package factories unchanged. Message layouts and IDs are unchanged;
protocol 151 is required for this input behavior. Native battery charge/draw,
block/gearbox prerequisites, cylinder-head assembly placement and live two-player
engine acceptance remain open. [Validation and limits](docs/BUILDING.md#guest-starter-flywheel-input-protocol-151-unreleased)
record the bounded starter decision checks. Changes remain local and unreleased.
Validation passes 2,040 Net tests (19 new), 18 launcher tests and 1,357 isolated
native checks (32 new, preserving all 1,325 earlier checks). Core Debug/Release,
both Net targets, the probe and Launcher build with zero warnings/errors. The
launcher payload matches the native-tested Debug files; Release is build-only.
[The result record](build/starter-flywheel-engine-input-smoke/result.json) identifies
the tested files and retained regressions.

**Engine wiring inputs (local/unreleased, protocol 150):** Host-only WiringState
193 supplies eight native wiring databases to thirteen bool reads through eleven
bindings in Cylinders, Starter and Electrics. The profile now has 71 sources and
122 reads across nine consumers. Exact catalog wire identities and native load
boundaries are validated; standard wires wait for Basic state, battery terminals
for Set bolt. Installed and Bolted retain their different native meanings.
Unavailable sources publish zero flags. Ordered updates, five-second keepalive,
join snapshots and vehicle resync share per-source revisions without consuming
pending broadcasts. Guests reject stale/conflicting revisions and use inert bool
proxies, preserving saved wiring Data, shared source references and native scratch
timing. Session teardown clears accepted wiring.

This covers wiring inputs used by protected engine consumers. The audit finds 35
native connections plus cable meshes, bolt/assembly tools, shock and fire graphs;
those other systems are not replayed or declared synchronized. Battery state,
guest wiring actions/presentation, cylinder-head assembly placement and live
multiplayer engine acceptance remain open. See
[validation and limits](docs/BUILDING.md#guest-engine-wiring-inputs-protocol-150-unreleased).
Validation passes 2,021 Net tests (41 new), 18 launcher tests and 1,325 isolated
native checks (47 new, preserving all 1,278 earlier checks). Core Debug/Release,
both Net targets, the probe and Launcher build with zero warnings/errors. The
launcher payload matches the native-tested Debug files; Release is build-only.
[The local result record](build/wiring-engine-input-smoke/result.json) preserves
source hashes and the initial fixture failures. Changes remain unreleased.

**Ignition-coil replicas and input (local/unreleased, protocol 149):** VIN212
has an explicit native factory profile publishing Wear/Tightness in state 185.
Fresh host creation retains the game's random wear initialization and persistent
identity. Actual guest copies keep published host wear across initialization,
vehicle fitting and removal. Cylinders Ignition #0 now reads installation from
the unique accepted/applied host attachment at Assemblies/VINP_IgnitionCoil on
the registered car. Missing, pending, conflicting and mismatched copies remain
absent; the native coil/distributor/wiring gate controls starting. Only the coil
reader moves to an inert proxy, preserving saved mount Data and ActivePart.
There are 60 sources/109 reads across nine consumers, 38 replacement factories
and 31 unchanged package factories. Wiring remains a separate dependency;
cylinder-head placement, full host fitting physics and two-player acceptance
are unfinished.
Validation passes 1,980 Net tests, 18 launcher tests and 1,278 isolated native
checks (19 new Net and 41 new native cases; all 1,237 previous checks retained).
Core Debug/Release, both Net targets, probe and Launcher build cleanly. The
[local validation record](build/ignition-coil-engine-input-smoke/result.json)
identifies the native-tested Debug payload and matching launcher files; Release
is build-only. Changes remain local and unreleased.

**Rev-limiter engine inputs (local/unreleased, protocol 148):** REVLIMITER0
already publishes Tightness/SettingRPM in state 185. Native Cylinders Limiter
now reads Installed and SettingRPM from its unique applied host copy on the
registered car body. The existing vehicle-relative attachment path supplies
the proxy; factory and consumer must agree on the live mount. Pending, absent,
conflicting and removed copies disable the native limiter without overwriting
maxRPM. The host's actual knob setting reaches the native engine on its next
read, including values produced above the saved-part initialization clamp.
Saved guest Data/ActivePart remain unchanged. The profile has 59 sources and
108 reads across nine consumers; all 37 replacement and 31 package factories
are unchanged. Guest knob requests/presentation and full engine operation remain
open alongside cylinder-head placement, ignition-coil/wiring and two-player testing.
All 1,961 Net tests, 18 launcher tests and 1,237 isolated native checks pass,
including 26 new Net cases and 37 new native cases; all 1,200 prior checks remain.
Core Debug/Release, both Net targets, probe and Launcher build cleanly. The
[local validation record](build/revlimiter-engine-input-smoke/result.json) identifies
the tested Debug payload and matching launcher files; Release is build-only.

**Flywheel/flexplate engine inputs (local/unreleased, protocol 147):** stock
VIN120, lightweight FLYWHEELa0/FLYWHEELb0 and automatic VIN138 now have explicit
factory profiles publishing Wear/Tightness/InertiaFactor in state 185. Native
Cylinders Flywheel reads use the unique accepted/applied attachment, then update
Drivetrain.engineInertia and the native .04/inertia shake calculation. All four
factories share the live block-relative mount. Pending, missing, conflicting and
removed replicas remain absent; the native stop branch precedes division.
Nonpositive/nonfinite inertia and division overflow are rejected at host capture
and replica admission. Saved mount Data and ActivePart remain unchanged. There
are 58 input sources and 106 reads across nine consumers. These parts spawn
directly; the 31 existing package bindings are unchanged. Native tests exercise
factory creation, save identity, actual guest materialization, fitted/loose body
presentation, variant swaps, native calculations, fault containment and restoration.
All 1,935 Net tests, 18 launcher tests and 1,200 native checks pass (40 new
Net cases and 53 native cases; all 1,147 previous checks retained). Core
Debug/Release, both Net targets, probe and Launcher build without warnings/errors.
[The local test record](build/flywheel-engine-input-smoke/result.json) records
the native-tested Debug payload and matching launcher files.
Full host fitting/removal physics, player controls, complete engine operation,
host wear while guests drive and live two-player acceptance remain open.

**Cylinder-head prerequisite audit:** VIN1110 is a persistent assembly with nested
cam, rocker, plug and thermostat mounts, plus a saved Valves array; it has no
replacement factory in the installed native/aftermarket spawner catalog. Its
VIN1010/VINP_Cylinderhead mount supplies Cylinders Powertrain #0 Installed.
Existing replacement isolation deliberately accepts leaf parts and rejects nested
Data graphs. Head support therefore needs authoritative assembly placement and
restoration before input projection; adding a leaf factory or reading a guest's
saved installation flag would be incorrect.
[Installed asset evidence](build/cylinderhead-audit/scene.json) preserves the Data,
mount and children. Wiring inputs can be audited independently of that assembly
work; v148 covers rev-limiter input and v149 covers ignition-coil input.

**Spark-plug cylinder inputs (local/unreleased, protocol 146):** all four SPRKPLUG0
slots now supply Installed/Wear/Tightness/Durability to native Cylinders reads.
The native array's reversed cylinder order is preserved. Each source requires a
unique accepted and applied host attachment, matching live identity and slot,
and the expected native mount/reader signatures. Pending, removed or conflicting
plugs supply inert values; saved guest part/mount writers stay protected.
Native firing/efficiency, tightness/wear misfire eligibility and durability math
use the host values. Random outcomes remain native/local; deterministic complete
engine simulation and host wear during guest driving are not established.
The profile has 57 sources and 104 reads across nine consumers. State 185 keeps
its existing scalar order and layout; matching protocol 146 peers are required.
[Evidence and tested hashes](build/sparkplug-engine-input-smoke/result.json)
record the isolated run. All 1,895 protocol tests, 18 launcher tests and 1,147
native checks pass (29 new catalog/protocol cases and 71 native cases; all 1,076
earlier checks preserved). Core Debug/Release, both Net targets, probe and
Launcher build without warnings or errors. Physical tool selection, live two-player acceptance,
cylinder-head and remaining engine inputs are still open.

**Spark-plug fitting/removal lifecycle (local/unreleased, protocol 145 unchanged):**
The isolated Unity probe now runs the complete native physics cycle across real
frames in all four sockets. It checks deferred body destruction, attachment,
one-time engine mass changes, fitted wear propagation, tightening/removal gates,
and restoration of world pose, car velocity, collision mode and pickup physics.
The same persistent part receives a fresh loose ownership lease after removal.
Recorded host states drive the actual guest materialization/update path; one
replica survives repeated fitting/removal, preserves condition, clears pickup
ownership and ignores delayed fitted states. This adds regression coverage;
runtime behavior, catalog and wire semantics are unchanged. Results are in
[sparkplug-lifecycle-smoke](build/sparkplug-lifecycle-smoke/result.json).
All 1,076 native checks and 1,866 protocol tests pass; the 44 new lifecycle
checks preserve all 1,032 previous native checks. Core Debug and the probe build
with no warnings or errors.
Physical tool selection, cylinder effects and live two-player acceptance remain
open. The sound/UI Functions FSM is a fixture boundary, and P2P is not exercised.

**Spark-plug wrench controls (local/unreleased, protocol 145):** tool operations
6/7 use the existing revisioned request/receipt ledger. The host checks the
native array socket, fitted mount/pose, proximity, integer tightness and ratchet
cooldown, then runs one native turn. Replicas replace native Screw writers with
intents and receive absolute tightness/pose. Layer-12 tool triggers require fully
applied fitted state, RepairMode and no pending operation. A registration-based
name hook fixes the wrench's old clone-name comparison while preserving its
native size and ratchet paths. Saved socket identity uses the part's array index,
not the mount's stale installer counter. Evidence is in
[sparkplug-tool-smoke](build/sparkplug-tool-smoke/result.json). The suite passes
1,866 Net tests, 18 launcher tests and 1,032 native checks (21 Net and 31 native checks added; all
1,001 earlier native checks retained). The later lifecycle probe above covers
native fitting/removal physics; cylinder effects and actual two-player acceptance remain open.

**Spark-plug socket/removal checks (local/unreleased, protocol 144):** the native
Screw pose sets SPRKPLUG0 to tool layer 12, while other replacement parts use
layer 19. A per-factory `removalLayer` now validates the native mouse picks and
current host collider layer; guest removal ray obstruction includes those layers.
Loose fitted plugs can publish RemovalAllowed through the existing host-owned
state. Native BOLTING, mount occupancy, collider/parent checks and removal
prerequisites remain required. The installed Sparkplugs array and all four mount
entry graphs are checked with reversed cylinder mapping. This verifies candidate
selection and removal readiness, not the full fitting/removal physics lifecycle.
Guest wrench input, complete lifecycle acceptance, engine inputs and two-player
acceptance remain open. All 1,845 Net tests, 18 launcher tests and 1,001 native
checks pass (11 new Net cases, 25 native checks; all 976 prior checks preserved).
Core Debug/Release, both Net targets, the probe and Launcher build cleanly.
Evidence: [native results and tested hashes](build/sparkplug-fitting-smoke/result.json).

**Spark-plug boxes and outputs (local/unreleased, protocol 143):** sparkplugbox0
now uses the shared host-owned package quantity and acknowledged opening path.
Its fixed four-plug capacity, literal contents factory reference and local
Sparkplug.SpawnPoint assignment are validated separately from standard boxes.
Individual SPRKPLUG0 outputs use replacement state with Wear/Tightness/Durability,
retaining the native four-slot family identity without inventing Installed.
Guests preserve local box saves and materialize the host's box/plug identities;
replayed requests do not create another plug. A native test caught and fixed a
box startup race that could clear a newer quantity update when an empty replica
had not yet started. All 1,834 Net tests, 18 launcher tests and 976 native checks pass (10 new Net
cases and 18 native checks; all 958 prior checks preserved).
[Results and tested hashes](build/sparkplug-opening-smoke/result.json) record
the isolated Debug run and separate build validation. Spark-plug screw-tool
controls, complete slotted fitting/removal, cylinder engine inputs and two-player
acceptance remain open. Changes remain local and unreleased.

**Guest radiator-fan power and cooling inputs (local/unreleased, protocol 142):**
VIN137 supplies host installation to native Valves/Radiator fan and Cooling/Fan.
The v210 live audit corrected the original VIN127 pulley mapping and registered
the separate RadiatorFan137 factory. See `docs/PERFORMANCE.md` for the correction
and current validation; the v142 evidence below predates that integration fix.
Two independent bool sources bring the profile to fifty-three sources and
eighty-eight reads across nine consumers. The factory VINP and consumer targets
must identify the same nested WaterpumpParent/VINP_RadiatorFan mount. Existing
Wear/Tightness state order is unchanged. Native belt/fan power-load and cooling
arithmetic run from accepted, applied host attachments; saved Data stays intact.
All 1,824 Net tests, 18 launcher tests and 958 isolated native checks pass (16 new catalog cases,
38 fan checks; all 920 earlier checks preserved).
[Results and tested hashes](build/radiator-fan-engine-input-smoke/result.json)
record the Debug payload and separate build validation. Full downstream cooling,
remaining engine inputs and two-player acceptance remain open.

The spark-plug audit led to v143 box/individual factories, v145 guarded tool
requests and isolated full fitting/removal lifecycle checks, then v146 cylinder
inputs. The native Sparkplugs array reverses cylinder order (slots 1–4 map to
cylinders 4–1). Physical selection, full engine operation and two-player
acceptance remain separate checks on the coverage roadmap.

**Guest piston combustion and smoke inputs (local/unreleased, protocol 141):**
all four VIN103 slots now supply host installation/wear to Cylinders and wear to
Mixture. Eight independent sources add twelve reads, bringing the profile to
fifty-one sources across nine consumers. Slot validation requires the accepted
and applied assembly identity; native slotted Data has no Installed bool, so
installation comes from the validated attachment. Wear/Tightness order stays
unchanged. Native firing, efficiency and Oil smoke decisions use host condition;
guest saved oil and contamination writers stay blocked.
All 1,808 Net tests, 18 launcher tests and 920 isolated native checks pass (31 new
catalog cases, 50 piston checks; all 870 previous native checks preserved).
[Results and tested hashes](build/piston-engine-input-smoke/result.json) record
the Debug run and separate clean builds. Cylinder-head, spark-plug and other
remaining inputs, smoke/failure presentation, full engine operation, host wear
during guest driving and two-player acceptance remain unfinished. Changes are
local and unreleased.

**Guest main-bearing oil-pressure inputs (local/unreleased, protocol 140):** all
five VIN104 slots now supply accepted, applied host Wear to native Wearing reads.
The input projection validates the MainBearings slot table, template, assembly
index, replica identity and parent independently for each slot. Wear-only slots
share the existing slot infrastructure while rocker Bolted derivation retains
its native validation. Wear/Tightness arrays stay unchanged; the profile now
contains forty-three sources and seventy-four reads across eight consumers.
All 1,777 Net tests, 18 launcher tests and 870 isolated native checks pass (23 new
catalog cases, 44 bearing checks; all 826 previous native checks preserved).
Native accumulation, RPM clamp and final PressureLeak output use host crank and
bearing condition while saved Data stays protected.
[Results and tested hashes](build/bearing-engine-input-smoke/result.json) record
the Debug run and separate clean builds. Cylinder-head and other component inputs,
redlining/failure effects, full downstream pressure/flow behavior, actual host
wear while guests drive and two-player acceptance remain unfinished. No release
or deployment was made.

**Guest head gasket, thermostat and oil-filter inputs (local/unreleased, protocol 139):**
VIN134 supplies combustion installation/wear and Oil installation; VIN129 supplies
Cooling installation/wear, VIN128 housing tightness, and OILFILTR0 filter
tightness/dirt. Five sources add eight native reads, bringing the profile to
thirty-eight sources across eight consumers. Existing scalar order is unchanged.
Native gasket, thermostat, leak and contamination decisions use applied host
state; saved guest Data and writer targets remain protected.
All 1,754 Net tests, 18 launcher tests and 826 isolated native checks pass (23 new
catalog cases, 99 fluid-input checks; all 727 earlier native checks preserved).
[Results and tested hashes](build/fluid-engine-input-smoke/result.json) record
the Debug run and separate clean builds. The fixture stops before physical gasket
failure, downstream cooling flow and oil-leak animation. Full engine operation,
cylinder-head/main-bearing and remaining electrical inputs, rotation/failure
presentation, actual host wear while guests drive and two-player acceptance
remain unfinished. Changes are local and unreleased.

**Guest crankshaft and auxiliary-drive inputs (local/unreleased, protocol 138):**
VIN102 crankshaft, VIN105 crank pulley, VIN109 auxiliary sprocket and VIN110
auxiliary shaft now feed eight native reads through seven independent input
sources. Cylinders follows host installation and crank wear; Oil/Wearing consume
crank condition, and FuelLine consumes auxiliary-shaft wear. Existing scalar
arrays remain unchanged. Native failure boundaries and pressure arithmetic stay
active, with saved-part writers protected and unavailable host inputs neutral.
Thirty-three sources span eight consumers. All 1,731 Net tests, 18 launcher tests
and 727 isolated native checks pass (25 new catalog cases, 90 powertrain checks;
all 637 earlier checks preserved). [Results and tested hashes](build/powertrain-engine-input-smoke/result.json)
record the Debug run and clean Net, Core Debug/Release, probe and launcher builds.
Remaining work includes cylinder-head and main-bearing inputs, rotation/failure
presentation, radiator-fan/battery/wiring dependencies, other engine inputs,
actual wear while guests drive and live two-player acceptance.

**Guest timing-belt combustion inputs (local/unreleased, protocol 137):** native
Cylinders installation and wear reads now consume the unique applied host VIN107.
Wear/Tightness scalar order remains unchanged. Native Powertrain, Wear <1 failure
and cam ValveTolerance <=1 decisions retain their original behavior, while guest
wear writers remain protected. Twenty-six sources span eight consumers.
All 1,706 Net tests, 18 launcher tests and 637 isolated native checks pass (13 new
catalog cases, 25 timing-belt checks; all 612 earlier native checks preserved).
[Results and tested hashes](build/timingbelt-engine-input-smoke/result.json)
record the Debug payload and clean Net, Core Debug/Release, probe and launcher
builds. TimingData/RotateEngine and failure presentation, radiator-fan inputs,
battery/wiring, remaining engine inputs, host wear while guests drive and
live two-player acceptance remain unfinished.

**Guest fan-belt inputs (local/unreleased, protocol 136):** six native bool reads
across Oil, Valves, Cooling and Electrics consume the uniquely attached, applied
host FANBELT0. Four independent sources bring the profile to twenty-five across
eight consumers. Native belt load/power, water-pump circulation, radiator-fan
cooling and charging gates now follow host removal/refitting. Appearance-only
receipts no longer queue an already applied belt for gameplay reapplication;
identity, body, attachment and pending-repair checks still apply. Saved mounts
and native wear writers remain protected.
All 1,693 Net tests, 18 launcher tests and 612 isolated native checks pass (17 new
catalog cases, 56 belt checks; all 556 prior native checks preserved).
[Results and tested hashes](build/fanbelt-engine-input-smoke/result.json) record
the tested Debug payload and clean Net, Core Debug/Release, probe and launcher
builds. TimingData/RotateEngine belt readers, the separate timing belt,
radiator-fan installation, battery/wiring, other engine inputs, host wear while
guests drive and two-player acceptance remain unfinished.

**Guest alternator electrical inputs (local/unreleased, protocol 135):** stock
and upgraded alternators append actual Durability/Efficiency and a trailing
optional mount-damage byte in state 185. Host capture requires a coherent fitted
attachment, factory reference, ActivePart and AssemblyPoint; unknown or ambiguous
mount data cannot become a healthy flag. Damage/repair advances gameplay revision.
Seven native Electrics reads use an owned proxy, including both Installed and
Damaged checkpoints. Missing/unapplied/conflicting sources expose absent/damaged
inputs. Repeated readers share typed proxy values while retaining distinct native
actions and normal scratch updates. Saved mount data and native wear writers stay
protected; malformed readers pause only their consumer.
The profile has twenty-one sources across eight native consumers. All 1,676 Net
tests, 18 launcher tests and 556 isolated native checks pass (29 new Net cases, 26 electrical checks,
all 530 earlier native checks preserved).
[Results and tested hashes](build/alternator-electrical-input-smoke/result.json)
record the controlled Debug run and matching launcher payload. Net, Core
Debug/Release, probe and launcher builds have no warnings or errors. Battery, wiring, belt integration, other engine
inputs, host wear while guests drive and two-player acceptance remain unfinished.

**Guest alternator mechanical inputs (local/unreleased, protocol 134):** stock
VIN133 and upgraded ALTERNATOR0 append actual Friction after the existing
Wear/Tightness/SettingRotation scalars. Three Oil reads use the current uniquely
attached, applied host variant through an independent proxy. Oil now has three
part sources; twenty sources span seven native consumers. Native installation,
Wear <=5 seizure and current-based resistance calculations remain active while
saved Data/ActivePart and scratch are preserved. Friction is read natively into
AlternatorFrictionRate, which this game build does not use in subsequent Oil
arithmetic; the mod does not invent an effect for it.
All 1,647 Net tests, 18 launcher tests and 530 isolated native checks pass (17 new catalog cases,
21 alternator checks; all 509 earlier native checks preserved).
[Results and tested hashes](build/alternator-mechanical-input-smoke/result.json)
record the controlled Debug run and matching launcher payload. Net, Core
Debug/Release, probe and launcher builds have no warnings or errors. Electrical Efficiency/Durability and mount-owned
Damaged reads still need host projection, alongside belt/wiring dependencies,
full engine integration, guest-driving host wear and two-player acceptance.

**Guest rocker inputs (local/unreleased, protocol 133):** eight VIN117 slot
sources extend the input profile to nineteen sources across seven consumers.
Cylinders receives independently gated Bolted inputs from the exact native
Rockers table slots under the cylinder head. Accepted attachment, host/applied
assembly index and replica ArrayReference must agree. Latest bolt receipts supply
Tightness; the native template's comparison and bool writers validate the >=1
derivation. Slot-table and signature failures pause only the affected consumer;
saved mounts, arrays and calculation scratch remain protected. Moving the head,
pending/removal, conflicting assignments, source repair and cleanup retain the
existing host authority and saved-original boundaries.
Validation passes 1,630 Net tests, 18 launcher tests and 509 isolated native checks, preserving all
477 earlier checks and adding 32 rocker checks. Thirty-three new Net cases cover
slot metadata, independent assignments and threshold behavior.
[Native results and tested hashes](build/rocker-engine-input-smoke/result.json)
record the Debug run and matching launcher payload. Net, Core Debug/Release,
probe and launcher builds have no warnings or errors. Full engine operation, guest-driving host wear progression
and two-player acceptance remain unfinished. Changes remain local and unreleased.

**Guest distributor timing (unreleased; protocol 125):** a separate VIN131
distributorTiming profile adapts root HandRotate without changing alternator
bindings. Empty-handed guests aim within 1 m after loosening the distributor bolt
below Tightness 8. Operations 2/3 request native ±0.2-degree SparkAngle changes
within 0–20, preserving fractional saved settings and the 0.01-second cooldown.
Wheel down increases timing; wheel up decreases it, matching native input.
The host checks current revision, mount, proximity and readiness, seeds the mesh
from authoritative Data before native GetRotation, and verifies saved part, mount
scalar and child mesh agree. Guest native HandRotate stays disabled; presentation
rotates only Pivot/mesh, preserving the authored parent pose. Fresh loose parts
retain the native mesh pose; fitting applies SparkAngle and removal retains the
resolved angle. The existing state 185 scalar carries results; all 32 factory IDs
remain unchanged. Scroll controls wait until the latest accepted revision has
fully reached the visible part, including when other materialization is deferred.
At v125, 1,328 protocol/catalog/policy tests, 18 launcher tests and 220 isolated
native checks pass, including 43 distributor checks. Both Net targets, Core
(Debug/Release) and the probe build without warnings/errors.
[Native results and tested hashes](build/distributor-timing-smoke/result.json)
record the final Debug run. Actual two-player controls, fitting/save/rejoin and
guest engine effects still need acceptance.

**Engine damage authority (unreleased; protocol 124):** the host publishes
VehicleDamage regardless of the driver and rejects guest damage reports. Guests
retain accepted live/snapshot condition for checksums; their native Damages graph
is paused, including while driving and after disconnect until restart. A forced
scan precedes part isolation so late-loaded damage graphs are paused before saved
parts move; failed suppression defers isolation. They do not replay PartBreakages
events or write mount Wear. Native damage references can retain a guest's saved ActivePart,
and PISTON failures can randomly cause OILPAN/BLOCK failures, so replay could mutate
preserved local parts and reroll consequences. This fixes damage authority and
saved-reference integrity; other vehicle streams retain their existing authority.
Full guest engine operation, correct native wear while a guest drives and visible
physical failures on guests are not established. Protocol-124 validation has passed
1,242 protocol/catalog/policy tests, 18 launcher tests and 177 isolated native
checks, including 23 damage checks. Both Net targets, Core (Debug/Release) and the
probe build without warnings/errors. [Native results and tested hashes](build/vehicle-damage-smoke/result.json)
record the final Debug run; see BUILDING.md for the two-player acceptance sequence.

**Fitted fanbelt presentation (unreleased; protocol 123):** replacement state 185
adds optional BeltVisual visibility, running state, flutter scale, pitch, volume
and texture scroll speed. Independent PresentationRevision orders cosmetic changes
without invalidating physical fitting requests. The host captures native damaged-belt squeal and
BeltAnimation RPM × AnimMultiplier. Guests own a mesh-only skinned hierarchy,
internal bones, material and audio source; only flutter/UV phase runs locally.
Native guest Jumping wear/breakoff logic is paused and its separate renderer/audio
hidden/muted, even at an empty guest mount. Original hierarchy and mount fields
remain intact; cleanup removes copies before restoring flags. A failed visual binding
preserves an existing fitted guest belt. Fitted replicas hide their loose mesh and
restore it on removal; latest loose/absent visuals or changed parents stop old views
even while other replica creation is deferred. At protocol 123, both Net targets,
Core (Debug/Release) and the probe built without warnings/errors; 1,205
protocol/catalog/policy tests, 18 launcher tests and 154 isolated native checks
passed, including 34 belt checks.
Two-player presentation/save/rejoin checks, rotating pulleys
and full guest engine behavior remain unverified; see BUILDING.md.

**Oilfilter hand tightening (unreleased; protocol 122):** empty-handed guests can
scroll within 1 m of a fitted filter. Dedicated operations 4/5 in messages 188–189
request one integer Tightness step within 0–8. The host checks revision, nearby
player, settled mount, native readiness and the 0.2-second cooldown, then executes
the native Screw/BOLTING chain. It refreshes raw Tightness before native pose code
reuses that scratch variable. Guests apply pose from resolved authoritative
Tightness with native Screw disabled. At protocol 122, both Net targets, Core
(Debug/Release) and the probe built without warnings/errors; 1,145 protocol/catalog/
policy tests, 18 launcher tests and 120 isolated native checks passed, including
26 hand-control checks.
Actual two-player controls, fitting, save/rejoin and the
remaining engine behavior still need acceptance; see BUILDING.md.

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

**Cylinder-head placement (2026-09-13, unreleased v216):** the placement blocker
from v215 is closed for the persistent VIN1110 head and VIN1010 block. Host native
fitting/removal publishes message 207; guests move the existing head and its child
mounts, leave fitted pickup motion, and restore loose motion on removal. Guest
Data and saved fastener controls are paused, with exact original pose/physics
restored at cleanup. Guest saved AssemblyID, condition and valve settings remain
unchanged. All eight guest valves work at the fitted location with the existing
host proximity gate. Validation: 4,365 protocol tests, 140 native checks and 25
local two-game assertions. Guest head-fitting/removal intents and fastening bolts,
missing-head reconstruction, full child-part parity and Steam/physical-input
acceptance remain open. Car assembly and M7 therefore remain incomplete.
The next bounded task rotates to shared firewood delivery/payday acceptance.
See [validation and limits](docs/BUILDING.md#cylinder-head-placement-2026-09-13-unreleased-v216).


### Firewood payment collection checkpoint (2026-09-13, unreleased v217)

The local acceptance audit reproduced one 500 mk firewood offer paying 5,000 mk
when ten guest requests arrived before its hand animation finished. A catalogued
firewood payment now reserves its live host offer before native entry. Inactive,
empty and repeated requests cannot become delayed collections. The guest never
runs the native cash/net-income additions, including host event replay; shared
balances arrive through WalletState. Payment controls are omitted from join-state
replay. No new messages or fields were needed; protocol 217 records the changed
semantics. Original credit-action flags are restored when the session clears.

Validation: 4,381 Net tests, 61 native checks (all four customer payment graphs)
and 9 final local two-game assertions. The driver prepares a 500 mk offer and
enters the native collection state, so this is payment acceptance only. Actual
delivery, job completion, buyer/offer visibility, signed penalties, physical
input and Steam/two-PC validation remain open; M8 is not complete.

After closing this bounded payment fix, the next task rotates to authoritative
milk condition/spoilage, a concrete survival gap found in the grocery audit.
See [validation and limits](docs/BUILDING.md#firewood-payment-2026-09-13-unreleased-v217).


### Milk condition checkpoint (2026-09-13, unreleased v218)

The same saved milk carton previously decayed independently on host and guest.
Message 208 now carries the host condition and native spoiled phase with revision
ordering. Host warm/fridge decay remains native. Guests wait for a seed, suppress
both local decay branches, retain native fresh-milk drinking prerequisites and
enter the native Bad presentation for spoiled milk. Periodic updates, keepalives,
join snapshots and item/object resync repair divergence. Session cleanup restores
the guest's original actions, condition and name without changing its save key.

This closes loose milk condition only. Other foods, cooking, changed fridge power,
physical input and Steam/two-PC acceptance remain open. The next bounded outcome
rotates to guest cylinder-head fitting/removal, because shared head placement is
implemented but guests cannot yet perform that part of building the car together.
See [validation and limits](docs/BUILDING.md#milk-condition-2026-09-13-unreleased-v218).


### Guest cylinder-head interaction (2026-09-13, unreleased v219)

Guests can request native fitting/removal of VIN1110 using the existing part
receipt ledger (188/189) and observed head revision (207). The host requires a
fresh nearby living player, an idle operation, actual pickup ownership for
fitting, native mount tolerance and unbolted tightness for removal. Native mount
prerequisites run before same-frame confirmation; refused previews are cancelled
and committed operations settle without replay. Guest Data and fastening arrays
remain suppressed, while the existing reversible attachment view carries the
result. No new message layout or ID; protocol 219 records changed semantics.

The next bounded task rotates to firewood buyer/offer presentation, needed to
collect the existing host-owned payment naturally. Head fastening, missing-head
reconstruction, physical input and Steam acceptance remain open; M7/M8 and overall
assembly are not complete. See [validation and limits](docs/BUILDING.md#guest-cylinder-head-fitting-2026-09-13-unreleased-v219).


### Firewood buyer presentation checkpoint (2026-09-13, unreleased v220)

All four customers mirror host buyer/offer visibility, payment label and NPC pose.
The host's native visibility/departure checks include fresh living guest poses;
guests can collect while the host is elsewhere. Native job/buyer references keep
customer 1's identity stable when he moves from CarPos to WoodPos. Guest native
decisions stay paused, and v217's once-only payment reservation remains active.

The final matching payload passes 4,436 Net tests, 109 native checks and 28 local
two-game assertions, including guest-only visits, all four prepared payments,
stale/equal-state recovery and relocated customer 1's reconnect and fresh payment.
Personal files remain untouched and test processes/probes are cleaned up.
Physical mouse input, actual wood delivery, signed penalties and Steam acceptance
remain open. See [validation and limits](docs/BUILDING.md#firewood-buyer-visibility-and-offers-2026-09-13-unreleased-v220).

After reassessing gameplay, reliability, usability and validation gaps, the next
bounded task is the ordinary local join/shop/drive/sleep/save/rejoin journey on one
build. This closes a known cross-system evidence gap instead of extending jobs by
inertia; implementation follows only if that journey exposes a concrete blocker.
No release, commit or personal deployment was made.

### Combined local journey (2026-09-13, unreleased v220)

The ordinary join/shop/drive/sleep/save/rejoin sequence now has evidence on one
unchanged production payload: 38 distinct local observations and 4,436 Net tests
pass. Both bag-holder and driver/passenger roles were exercised. Native save and
process restart preserve the shopping result, actual pre-save car position,
advanced time and returning guest state; personal and guest world files remain
protected. Only the opt-in development probe's dispatch changed so its existing
command channels could run together.

Clean acceptance remains open: a 13.718 m parked Sorbet displacement occurred
between guest driver exit/sleep and saving, before an otherwise correct reload.
A short follow-up at the later saved location stayed within millimetres and does
not explain the original transition. One diagnostic guest startup timed out; its
single retry succeeded. The next bounded task is to reproduce the original
movement from the captured starting save and classify its cause before changing
production behavior. This follows observed reliability evidence, not topic
inertia. Physical input, guest checkout in this sequence, Steam/two-PC, four-player
soak, M7/M8 and overall completion remain open. See
[validation and limits](docs/BUILDING.md#combined-local-journey-2026-09-13-unreleased-v220).

### Sorbet parking brake setting (2026-09-13, unreleased v221)

The original-world motion trace exposed timed lever replay producing different
native braking strengths: 0.8207586 on the host versus 1.0 on the guest. Sorbet now
shares the exact normalized setting through the existing vehicle climate/control
stream, with authenticated simulator ownership, ordered final release and host
snapshots. The catalog validates the native variable/clamps, and generic relative
FSM replay is suppressed. Guests restore their original scalar on cleanup.

4,451 Net tests, 30 distinct local gameplay assertions and three motion checks
pass. Partial/full settings, release, reconnect and native saving were exercised
on copied worlds; personal and guest world files remain protected. The exact
original 13.718 m displacement remains unproven. In-place native exit still caused
impulses despite equal brakes; a repeat with the guest clear of the cabin showed
slow native creep with both cars within 3 cm and no large impulse. Physical input,
other vehicles/terrain, Steam/two-PC and full M7 acceptance remain open.

The bounded brake mismatch is closed. Rotate next to guest cylinder-head fastening,
a concrete missing step in shared engine assembly. See
[validation and limits](docs/BUILDING.md#sorbet-parking-brake-setting-2026-09-13-unreleased-v221).

### Guest cylinder-head fastening (2026-09-13, unreleased v222)

All ten persistent head fasteners accept guest native tool events. Requests carry
the native array slot and observed head revision through the existing part-operation
ledger. The host checks the settled fitted mount, fresh nearby player, bounds and
cooldown, executes the native turn, and publishes exact bolt values and aggregate
with head placement. Guest saved bolt/assembly data stays local; removal/refit and
reconnect rebuild usable controls from accepted state.

4,463 Net tests and 40 local two-game checks pass, including both directions on
every slot, stale/distant rejection, bounds, host turns, removal/refit, reconnect,
native saving and fresh-process reload. Core/probe and both Net targets build
cleanly. Personal and guest native save files remain unchanged. Physical tool
selection/mouse input, originally fitted guest saves, missing/different heads,
full child-part parity and Steam/two-PC acceptance remain open. See
[validation and limits](docs/BUILDING.md#guest-cylinder-head-fastening-2026-09-13-unreleased-v222).

This bounded fastening task is closed. Next verify fridge power and shared milk
freshness, rotating to household gameplay after reassessing remaining bugs,
coverage, usability and acceptance gaps.

### Electricity cutoff and fridge milk (2026-09-13, unreleased v223)

Native bill cutoff leaves MainSwitch on while cutting effective HouseElectricity.
The old stream consequently kept guests powered. Message 95 now separates supply,
physical switch and bill visibility with exact debt. Loaded guest electricity
meters pause reversibly; pending host state survives discovery and disconnect
restores original values/execution. Native fridge cooling and host milk decay are
unchanged, including the cooling-area latch until a door cycle after power loss.

4,476 Net tests and 46 local two-game checks pass across both homes, warm/cooled
milk, guest door cycles, cutoff/restoration, different guest power states on rejoin
and terminal spoilage. Core/probe and both Net targets build cleanly. All 18
protected personal files and 12 copied guest native text files remain unchanged;
test games and probe cleanup are complete. No release, commit or personal deployment.
Physical input, natural bill/payment flow, cutoff save/reload, phone/fuse changes,
other food, arbitrary prior fridge cooling states on join and Steam/two-PC remain
open. See [evidence and limits](docs/BUILDING.md#electricity-cutoff-and-fridge-milk-2026-09-13-unreleased-v223).

The observed supply mismatch is closed. After reassessing user bugs, missing
functionality and acceptance gaps, next verify cylinder-head reconstruction for a
guest whose save lacks the host's head. Different personal inventories should not
prevent joining a shared engine-building session. Prior performance/launcher work
stays closed; full mod and M7 acceptance are not claimed.

### Missing cylinder-head save records (2026-09-13, protocol v223)

Removed all six head records from a copied guest carparts file. Native initialization
still supplies the scene head, and existing adapters reuse it for the host's fitted
head, fasteners and valves. No new reconstruction path or production change was
needed. Twenty-four local two-game checks pass, including one instance throughout,
21 aligned child mounts, eight valve displays, guest tool/fit/remove/adjustment
operations, resync and rejoin. Guest defaults and missing records remain local;
all 18 personal files and 12 prepared guest native text files are unchanged.
Core/probe builds pass and tested production hashes match the existing v223 build.

Preparation initially ran too early for native ES2; the opt-in fixture now waits
for GAME. One later guest boot timed out before a usable probe reply, then a normal
retry passed. That startup uncertainty, destroyed/corrupt scene heads, alternative
variants, full child-part parity, physical input and Steam acceptance remain open.
See [evidence and limits](docs/BUILDING.md#missing-cylinder-head-save-records-2026-09-13-protocol-v223).

The absent-record task is closed. Reassessment selects host-chopped moose-meat
spawning next: an identified missing factory/manifest path that affects shared
wildlife gameplay. Prior performance and launcher tasks stay closed. Test games
and probe cleanup are complete; no release, commit or personal deployment occurred.

### Moose-meat output (2026-09-13, protocol v224)

The previously unregistered native meat factory now supplies exact output identity,
creation pose and host food state through message 210. Guests reuse no personal
meat: separate replicas bypass native save reads/writes and cooking/spoilage, retain
native cooked-food eating input, and disappear on retirement/disconnect. Local native
objects restore afterward, including overlapping IDs. Body-less replica tombstones
are also cleaned up. Existing item transforms handle movement.

Validation: 4,492 Net tests and 20 final local two-game checks pass; Core/probe build
cleanly, with 18 personal files and 12 guest native text files unchanged. Earlier
consumption attempts failed to converge; traced/final runs pass with confirmed
spawn/pose readiness. Physical inputs, guest chopping, natural cooking/spoilage,
native meat save/reload and Steam acceptance remain open. See
[evidence and limits](docs/BUILDING.md#moose-meat-output-2026-09-13-unreleased-v224).

The host-output/late-join task is closed. Next is native utility-bill payment as one
shared transaction, an open economy acceptance gap. Prior performance/launcher
checkpoints stay closed. This remains local/unreleased; no full-mod completion is
claimed.


### Electricity invoice payments (2026-09-13, protocol v225)

Both electricity sheets now submit the displayed host invoice revision and await
a receipt. The host validates the actual envelope's position, reserves the debt,
debits shared cash once and runs native Pay bills without opening its own sheet.
Date is presentation-only; observers never replay accounting. UtilityBillState 95
appends revision, and 211/212 carry electricity payment intent/result. The native
meter still clears cutoff timing and restores effective supply independently of
MainSwitch. Reconnect restores host invoice state and safely resets player receipt
sequences; disconnect restores the guest's original meter and payment actions.

4,505 Net tests and 20 final local two-game checks pass, including both meters,
repeated requests, host and guest payments, rejected funds/distance, switch-off
settlement, resync, conflicting guest data and payment after reconnect. Core/probe
build cleanly; 18 personal files and 12 copied guest native text files are unchanged.
No commit, release or personal deployment occurred. Installed game 23268598 ships
both envelope Use FSMs disabled; test fixtures explicitly enable copied controls.
Natural bill access, physical inputs, phone charges, native cutoff/payment save-
reload and Steam/two-PC remain open. See
[evidence and limits](docs/BUILDING.md#electricity-bill-payments-2026-09-13-unreleased-v225).

The prepared electricity transaction checkpoint is closed. Next is live passenger
death/respawn recovery: old seats release, movement returns and fresh seat entry
works. This rotates from shared economy to an unresolved ordinary-session recovery
outcome, with existing v191 behavior inspected before any new implementation.


### Native passenger recovery checkpoint (2026-09-13, unreleased v226)

The v191 lifecycle hook ran too late: native State 3 destroyed movement while
passengers stayed parented until Take photo. Its 120-second timeout also reported
a respawn from an inactive newspaper player. v226 binds the inactive graph before
activation, retires the seat at State 3, retains death through MainMenu/load and
requires restored movement plus completed guest spawn selection before reporting
life. Packet layouts/IDs stay unchanged; next free ID is 213, mod remains 0.1.33.

18 final native two-game checks, 4,505 Net tests and 18 launcher tests pass. Both
roles release seats, reload, walk and re-enter; a separate same-payload newspaper
hold remains dead beyond 135 seconds. Personal files and copied guest native text
saves are unchanged. This completes the bounded normal-death fixture task;
[evidence and limits](docs/BUILDING.md#native-passenger-recovery-2026-09-13-unreleased-v226)
include the car-obstructed first walk, native menu/load boundary and remaining
moving-vehicle/physical/Steam acceptance.

Reassessment found a concrete blocker: session=false and native PlayerPermaDeath=
true disagree in copied saves. Tests explicitly set only the disposable native
flag false. Correct that binding next, including load/join/reconnect and guest
save protection. Permadeath/group wipe remains unaccepted. Native host death
still saves/reloads the world rather than keeping it continuously running.


### Native permadeath setting checkpoint (2026-09-13, unreleased v227)

The session/native flag disagreement is resolved. Native Continue reads
`savefile.txt?tag=PlayerPermaDeath`; the old code used an unrelated tag as a
filename and had an invalid generic save-method lookup. The host now reads the
native saved tag or loaded GAME variable. A matching guest LoadBool is overridden
before Finish; guest runtime correction also survives global replacement and
reconnect. No guest save-setting write or synthetic achievement event remains.
Native host LoadBool/SaveBool publishes SessionSettings 213 to connected guests;
only an accepted host may send it, with one validated flags byte. Protocol and
manifest become 227, next free ID is 214, mod remains 0.1.33, unreleased.

21 local two-game setting checks, 4,515 Net tests and 18 launcher tests pass.
Both flag values survive load/join/reconnect while guest personal native saves
remain unchanged. Native binding startup failure prevents guest admission;
full permadeath group death and save-deletion acceptance remains open. See
[evidence and limits](docs/BUILDING.md#native-permadeath-settings-2026-09-13-unreleased-v227).
The setting task is closed. Reassessment rotates next to guest moose chopping,
connecting guest axe hits to the already-tested authoritative host meat output.

### Guest moose chopping (2026-09-13, unreleased v228)

The native CarHit path detaches the ragdoll then destroys the live moose root;
NpcTransform death snapshots could therefore miss the corpse entirely. A local
v227 reproduction showed an active host corpse and inactive guest corpse.
The corpse now has a separate host identity, 11 body poses and two native piece
counts. Guest CarHit is intercepted before local destruction and reports to the
host; the guest's original animal is preserved for disconnect. The native axe
comparison/sound remains, but its Pieces gate sends an intent with the observed
count. The host requires a fresh nearby player, matching corpse/count and idle
native section/factory; it runs native Pieces with a section-local spawnpoint.
Exact retries and competing requests cannot spend the same piece twice.

Final acceptance passes 24 native local two-game checks and 4,559 automated
tests. The 450 kg corpse is excluded from vehicle push/cargo ownership.
Messages 214/215 and protocol/compatibility 228 are unreleased; mod stays 0.1.33.
See [acceptance and limits](docs/BUILDING.md#guest-moose-chopping-2026-09-13-unreleased-v228).
After closing this gameplay gap, reassessment selects two-player permadeath group
wipe acceptance in disposable saves. Correction to the initial task description:
the existing rule is one death ends everyone's permadeath run, not last-survivor
elimination. Verify native host-world deletion and unchanged guest personal saves;
settings and normal passenger recovery alone do not establish that consequence.


### Native permadeath group wipe (2026-09-13, unreleased v229)

The existing one-death-ends-everyone rule is retained. A v228 native reproduction
showed double State 3 entry during remote group death, a stale respawn reviving a
dead guest, and reconnects accepted after world deletion. The trigger now sets its
cause before native activation and never re-enters the destructive state. A
terminal session policy records the first wipe, ignores repeats, rejects respawns
and further handshakes, and preserves death across menu/loading. Only transport
reset clears it for a new session. Packet layouts/IDs are unchanged; protocol and
compatibility are 229, next free ID is 216, mod remains 0.1.33 and unreleased.

26 final local two-game checks pass on one payload: either role starting native
death, both host deletion stages once, seven host files deleted, twelve guest
native saves unchanged, repeated reports/stale respawn, obituary/menu, reconnect
refusal and a normal-death Continue/recovery/reconnect control. The build is clean;
4,545 Net plus 18 launcher tests pass and 18 personal files are unchanged. See
[evidence and limits](docs/BUILDING.md#native-permadeath-group-wipe-2026-09-13-unreleased-v229).
This closes the bounded group-wipe task; physical death causes, interruption,
Steam/four-player play and uninterrupted normal host-world operation remain open.
Reassessment rotates to phone bill settlement: validate native charge calculation,
one shared payment and matching PhonePaid/cash after retries and reconnects.


### Shared phone bill settlement (2026-09-13, unreleased v230)

The native v229 reproduction showed a 128 mk guest quote against the host's
140 mk quote; guest payment only cleared its local meter. Phone invoices now
carry host usage and tariffs, separate from the native UnpaidBills accumulator.
The host validates the displayed revision and nearby living payer, debits once
and runs native Pay bills; both roles receive line/debt/envelope/usage results.
Native quote calculation resets CostFinal, and guests restore original meter
state on disconnect. Host payment also works after the last guest leaves.

25 final local two-game checks pass for both phone meters/roles, retries,
competition, funds/distance refusal, reconnect/restoration/resync, both electricity
controls and host-alone settlement. 4,557 Net plus 18 launcher tests pass; the
Release build is clean and 18 personal files/12 copied guest native saves are
unchanged. Protocol/compatibility 230 extends state 95 and payment semantics
211/212; next ID remains 216, mod is 0.1.33 and unreleased. See
[evidence and limits](docs/BUILDING.md#shared-phone-bill-payments-2026-09-13-unreleased-v230).
Natural invoice access, physical input, real call accrual, native bill save/reload
and Steam acceptance remain open. This bounded task is closed; reassessment
selects the repeated guest hockey ArrayList errors on native reload next, to
improve shared-world resume reliability while preserving personal saves.

### Hockey scene cleanup (2026-09-13, unreleased, still v230)

Native menu reload destroyed the guest's hockey proxies before the world manager
called cleanup, producing `Hockey state restore failed: Hockey live ArrayList
unavailable.` Cleanup now skips destroyed proxies and their owning FSM variables;
surviving collections/settings still restore before native generators resume.
Display refresh requires both source FSMs to survive. Live collection validation
and its warnings remain intact. No packet layout or meaning changes; protocol and
compatibility stay 230, next free ID 216, mod 0.1.33 and unreleased.

13 local two-game checks pass, including two native menu/Continue cycles, new
host odds updates, full resync and original guest board restoration after reload.
The Release game build is clean and all 4,557 Net tests pass. All 18 personal files
and each copied profile's 12 native text saves are unchanged. Generic native index
logs remain unattributed; natural round generation, visible teletext, Steam and
full shared-world acceptance remain open. See
[evidence and limits](docs/BUILDING.md#hockey-scene-cleanup-2026-09-13-unreleased-v230).
This closes the specific cleanup warning. Reassessment selects the guest firewood
delivery-to-payment flow with the host away: buyers and payments have separate
local evidence, but native delivery and the resulting shared offer still need it.

### Shared firewood unloading (2026-09-13, unreleased v231)

The v230 reproduction left a host trailer loaded after the guest independently
emptied its copy; the guest also lost the native negative early-delivery penalty.
The new catalog-bound delivery adapter shares the host's load, mass and complete
ground-pile manifest. Nearby guests send epoch/sequence-bound start/stop intents;
the native host consumes wood, calculates the offer and completes the order.
Pausing and resuming retains the same pile. Signed firewood adjustments now reach
guests, and existing guarded collection still pays once. Messages 216/217 and
protocol/compatibility 231 are unreleased; next ID is 218, mod remains 0.1.33.

26 final local two-game checks pass on one payload: shared piles/offers, signed
adjustments, host-away guest collection, duplicate/stale/distant refusal, partial
resume, unpaid reconnect/resync, a fresh post-rejoin request, native hatch entry
and collection by the host. Release build is clean; 4,582 Net plus 18 launcher
tests pass. All 18 personal files and both copied profiles' 12 native saves remain
unchanged. See [evidence and limits](docs/BUILDING.md#shared-firewood-unloading-2026-09-13-unreleased-v231).
This closes unloading through payment from a host-supplied load. Guest cutting/
loading, tractor travel, native delivery save/reload and physical/Steam acceptance
remain open. Reassessment selects a native Corris puncture during guest driving,
including host handoff and parked rejoin: component checks alone do not establish
consistent wheel handling through those player actions.

### Guest Corris punctures (2026-09-13, unreleased v232)

The v231 native guest puncture immediately returned to healthy: protected saved
writes left its reader consuming the host's unchanged health. The host never saw
damage. A validated guest driver flat-state entry now sends request 218. The host
checks driver identity, freshness, ordering and the current tyre lifecycle before
executing the audited native zero-health write. Message 205 publishes the result
with per-wheel epochs; repairs/replacements retire old requests independently.
The guest's saved part health remains protected.

18 local two-game checks pass: controlled native rolling, host handoff, parked
rejoin/resync, concurrent punctures, repair and stale/spoofed/observer/replay
refusal. Release builds are clean; 4,592 Net and 18 launcher tests pass. Protocol
and compatibility are 232, next ID 219, mod remains 0.1.33 and unreleased. See
[evidence and fixture limits](docs/BUILDING.md#guest-corris-punctures-2026-09-13-unreleased-v232).
The disassembled disposable car required native tyre prefabs, prepared mounts/
seat, nearby native entry events and velocity-driven rolling. Engine-powered
driving, physical controls, tyre save/reload and full assembly remain open.

This closes the reproduced puncture-loss bug. Reassessment selects the Corris
instability observed before puncture preparation when peers loaded different
native worlds. Matching disposable world files isolated this puncture test but
did not fix that separate blocker to ordinary joining/driving; diagnose it before
extending vehicle features. Personal saves/install remain untouched.

### Corris join stability (2026-09-13, unreleased, still v232)

Joining from a different native saved world moved the guest Corris while leaving
its world-connected parking FixedJoint at the old pose. The native solver pulled
it back, the LOD respawn amplified the displacement and connected bodies ran away.
Removing only that parking joint isolated the cause; permanently removing it let
the parked car settle away from the host. The final catalog-bound adapter rebuilds
that lock at the accepted pose, preserving native settings and FSM references.
Connected part joints remain intact. Kinematic streams retain the same lock;
final packets, timeout, local claim and disconnect rebase before restoring physics.
Changed/missing native bindings defer the unsafe move with a bounded diagnostic.

15 final local two-game assertions and 18 native checks pass with the original
differing disposable saves, without tyre/seat preparation. Release Net/Core/probe
builds are clean; 4,600 Net and 18 launcher tests pass. All 18 protected personal
files and each profile's 12 native text saves remain unchanged. Protocol and
compatibility stay 232, next ID 219, mod 0.1.33, with no commit or release.
See [evidence and limits](docs/BUILDING.md#corris-join-stability-2026-09-13-unreleased-v232).
Full assembly, engine-powered Corris travel, physical controls and Steam remain
open. This closes the reproduced joining blocker. Reassessment rotates to guest
cooking on the home stove: existing appliance code still needs control-to-food
acceptance, including one shared cooked result and guest rejoin.


### Guest home-stove cooking (2026-09-13, unreleased v233)

Both home stoves now route guest knobs through host-validated native operations.
Full native temperatures replace the old 0–100 truncation for cooking; guest
simulation is paused while authoritative heat, knobs, triggers, light and smoke
are applied. Cleanup restores native actions/values, including hazard counters.
The homes retain their distinct native ignition settings. Existing shared meat
state210 provides the cooked item and reconnect identity.

Release Core/Net35/probe build cleanly; **4,625 Net and 18 launcher tests pass**.
**29 final local two-game assertions pass**, including natural cooking, smoke,
rejoin/resync, native-control restoration and request rejection. Both games closed
normally, and protected personal/disposable native saves are unchanged. Fixtures,
startup retries and remaining limits are in [BUILDING.md](docs/BUILDING.md#guest-home-stove-cooking-2026-09-13-unreleased-v233).
This is the bounded stove slice, not sausage conversion, every food or the full
fire lifecycle. Protocol/compatibility are 233, next ID 220, mod 0.1.33; unreleased.

Reassessment selects guest ATF refill of the Corris gearbox. Include opening the
native filler cap, finite bottle depletion, host gearbox gain, gauge presentation
and reconnect. This advances practical car maintenance without extending the
completed engine-reader/performance work. After that outcome, rotate to a guest
earning loop from the full sync-scope audit.


### Guest Corris ATF refill (2026-09-13, unreleased v235)

The named maintenance slice is implemented: nearby guest cap inputs, shared bottle
identity/remainder/empty state, host-conserved source-to-gearbox transfer, local
gauge, rejoin and native persistence. The host's independent engine movement now
supplies the transient filler pose relative to Corris. Accepted bottle transforms
control remote contact, independent of display smoothing. Guest native cap pose,
gauge descendants, actions and saved bottles restore on cleanup.

**39 final two-game assertions pass across acceptance and full restart**, alongside
**4,755 Net and 18 launcher tests**, with clean Release Core/Net/probe builds.
Native saved partial/empty bottles and gearbox oil return unchanged. The final
finite-transfer fixture stabilizes the incomplete disposable host engine and uses
native hand/cap state entry with controlled aiming; physical controls, normal
assembled-car handling and Steam remain acceptance gaps. The earlier contact and
gauge failures are retained. All 18 protected personal files and the guest's 12
native saves remain untouched; both final stages exit normally and temporary
runners/probe are cleaned up. [Full evidence and limits](docs/BUILDING.md#guest-corris-atf-refill-2026-09-13-unreleased-v235).
Protocol/compatibility are 235, next ID 223, mod 0.1.33; no commit or release.
I12/V10 remain Partial because other maintenance supplies/transfers are open.

Reassessment selects **guest flea-market selling**: paid rental, listing/pricing
one native-accepted shared item, unsold removal or host sale, exact sold-item
retirement and once-only proceeds, including rejoin/native persistence. Existing
FleaSaleSync rent/proceeds state leaves the listing inputs and item manifest local.
This adds a missing earning loop and rotates away from maintenance; sewage/taxi
have larger physical dependencies. Audit native price/rental/collection boundaries
first. After that complete journey, reassess and rotate to ordinary supply creation.


### Flea-market payment dependency (2026-09-13, unreleased v236)

The active J04 audit found and fixed unpaid rental forwarding and repeatable
MoneyFlea cash replay before adding shared listings. Native checkout and envelope
inputs now use host quotes/receipts, fresh nearby poses and finite cash transfers;
retries cannot repeat a debit or credit. Inactive guest table simulation and native
finance/cart restoration prevent local day/sale drift. The generic checkout overlap
found during testing is removed. See the [recorded native findings](docs/SYNC-SCOPE-AUDIT.md#flea-market-native-audit-and-payment-dependency-2026-09-13-v236-unreleased)
and [payment validation](docs/BUILDING.md#flea-rental-and-proceeds-2026-09-13-unreleased-v236).

This closes a financial dependency, not the complete selling journey. Next remains
one supported shared listing with price/identity, native sale or unsold retrieval,
exact retirement and host listing persistence; do not expand financial polish or
return to engine/performance work. J04 stays Partial. Rotate to ordinary supply
creation after the complete earning journey closes.


### Advert-job telephone enrolment (protocol 256, local/unreleased)

Both players can start the advert route through native 08231206 calls on the
apartment, old-house or taxi phone. One host reservation gates the 72-second
conversation, shared household charges and native CALLED/JOB result. Premature
completion, service/range loss, replay and departure cannot enrol. Guest native
billing/result writes are bypassed only for this number; host native save tags
and existing advert job state retain the result. Vanilla's initial 600-second
wait and schedule remain. See [evidence and limits](docs/BUILDING.md#advert-telephone-enrolment-protocol-256-unreleased).
J09 is Candidate, not manual/Steam accepted. Next: I05 R20 battery-box contents,
rotating from this completed job entry point to missing ordinary supplies.

### R20 battery boxes and loose contents (protocol 257, local/unreleased)

Both roles open four-cell R20 boxes through the existing shared package ledger.
Native factory output IDs and remaining quantities agree, and loose cells retain
native persistence across host saves. Catalog retirementVariable distinguishes
R20 Consumed deletion from fitted-fuse Destroy deletion without new wire fields.
Guest purchase/bag unpacking, competing pickup, retirement/replay, restoration and
cold reload pass 32 controlled checks. I05 is Candidate; H12 battery fitting and
appliance use remain separate. [Evidence and limits](docs/BUILDING.md#r20-battery-boxes-and-persistent-cells-protocol-257-unreleased).
After reviewing user reports and missing gameplay, next is V11 guest window
scraping. No release or installed deployment was made.
