# Our Winter Car — Multiplayer Mod for *My Winter Car*

A full co-op conversion of My Winter Car (MWC) with a standalone launcher.
One player **hosts** and owns the savefile; everyone else joins as guests.
Money, cars, parts, items, doors, NPCs, time, weather and all game-relevant
events are synchronized.

**Design priorities (in order):** correctness/completeness → ease of use → stability → everything else.

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

| Subsystem | Model |
|---|---|
| Player avatars | Custom rig (head/hands/body) streamed at 20 Hz, name tags, skin/clothing variants. Remote players are *visual only* (no physics pushing) v1 |
| Player animation | Derived state machine (walk/run/crouch/carry/drive) — low bandwidth |
| Voice/text chat | Text chat day one; positional voice via Steam Voice later (M7) |
| Money/economy | **Single shared wallet** owned by host. All transactions are intents → host validates → broadcast. No race conditions by construction |
| Shops, services, jobs | `anyone-triggers` FSM intents; host executes purchases, spawns parts, applies money |
| Car assembly (bolts/parts) | Attachment/detachment/bolt-tightness as reliable events keyed to part IDs; wear/tuning values as synced FSM variables |
| Vehicle state | Engine FSMs `owner-only`; fuel/temp/damage variables synced; key/ignition/lights/doors as events |
| World items (pickables) | Event-synced + ownership streaming when in motion |
| Doors/switches/appliances | `anyone-triggers` events |
| NPCs & traffic | `host-only` simulation; transform + FSM state streaming with distance-based rates; guests near NPCs get higher rates |
| Time/weather/calendar | Host clock is law; guests slave their FSM time variables; periodic hard correction |
| Player needs (hunger/fatigue/etc.) | Per-player, simulated locally, reported to host (needed for host-side saving of guest profiles) |
| Sauna/fireplaces/cooking | `anyone-triggers` + host-owned progression variables |
| Phone/orders/deliveries | Host-owned; results broadcast |
| Sleeping/time skip | Requires consent: all players confirm → host advances time |
| Death/respawn | Per-player death; world keeps running; respawn flow replicated |

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

### 4.7 Stability engineering (explicit, budgeted work — not an afterthought)

- **Desync detection:** periodic lightweight checksums over critical state
  groups (economy, part attachment table, door states). On mismatch → targeted
  **soft resync** of that object group from host (no session restart).
- **Self-healing streams:** object-level "request full state" path any client
  can invoke when it sees impossible data.
- **Connection quality:** Steam Sockets stats surfaced in the TAB overlay
  (ping, loss, relay vs direct); auto-pause of physics ownership transfers on
  bad links.
- **Crash containment:** all mod callbacks wrapped; an exception in one sync
  subsystem logs + disables that subsystem rather than killing the game.
- **Telemetry-in-logs:** structured log lines for every intent/transition,
  ring-buffered, dumped on error — feeds the launcher's bug-report zip.

---

## 5. Milestones

Estimates assume 1–2 experienced developers, part-time. PlayMaker curation
(M5–M6) is the long pole and parallelizes well across contributors.

| # | Milestone | Contents | Exit criteria | Est. |
|---|---|---|---|---|
| **M0** | Recon & tooling | FSM dumper, object catalog, save analyzer, ID scheme; decompile pass (dnSpy/ILSpy) over `Assembly-CSharp` + PlayMaker | Catalog generated for current game build; savefile fully mapped | 3–4 wk |
| **M1** | Transport skeleton | BepInEx plugin boots, Steam session piggyback, lobby create/join, overlay invites, `+connect_lobby`, text chat, TAB player list | Two machines chat in-game via Steam invite, zero config | 2–3 wk |
| **M2** | Players | Avatar rig, transform/animation streaming, name tags, interpolation layer | Two players see each other move smoothly around the home | 3 wk |
| **M3** | World basics | Generic FSM sync engine + first curated set: time/weather slave, doors, switches, item pickup/drop, join snapshot v1 | Guest joins mid-session into a consistent world; doors/items stay consistent | 4–6 wk |
| **M4** | Vehicles | Rigidbody ownership system, vehicle driving sync, passengers, engine FSM ownership, fuel/damage variables | Two players drive separate cars; passenger seat works; cars persist correctly | 5–6 wk |
| **M5** | Building & economy | Part attach/bolt sync across all ~200 parts, shared wallet, shops, phone orders, deliveries | Full car build performed by two players cooperatively; money always consistent | 6–8 wk |
| **M6** | NPCs, jobs & long tail | NPC/traffic streaming, jobs/events, sauna/needs/sleep-skip, death/respawn, guest save sidecar | "Everything synced" checklist (§4.4) green on current game build | 8–12 wk |
| **M7** | Stability pass | Desync checksums + soft resync, reconnection, perf (bandwidth budget < 64 kB/s per client steady-state), voice chat, soak tests | 4-player 3-hour session with zero hard desyncs across 10 soak runs | 4–6 wk |
| **M8** | Launcher polish & beta | Auto-update, save backup/restore UX, diagnostics zip, install repair, docs, Nexus/GitHub release, closed beta → public beta | Non-technical user goes from download to playing with a friend in < 5 min | 3–4 wk |

Ship order matters: after **M4** the mod is already a great co-op experience
(drive around together, basic world sync) and can go to closed alpha for
real-world feedback while M5/M6 land.

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
| Launcher | .NET 8 + WPF (Windows-only, like the game) | Fast to build, easy installer story |
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
