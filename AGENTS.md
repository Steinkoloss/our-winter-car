# AGENTS.md — working on Our Winter Car

Read this before you touch anything. This whole project is vibecoded by AI
agents; you are the next one. The only thing standing between this repo and an
unmaintainable mess is *you* keeping it tidy. Treat that as part of the job, not
a chore you skip.

If anything here conflicts with what the user explicitly asks for in the moment,
the user wins. Otherwise, follow this.

---

## 1. What this is (30-second version)

A co-op multiplayer mod for the Unity game **My Winter Car (MWC)**. One player
hosts (owns the savefile and the world); friends join via the Steam friends
list. Money, cars, parts, items, time and weather are synced.

- **Topology:** host-authoritative listen server. *Host always wins.* Guests
  send intents; the host validates, applies, and broadcasts results.
- **Transport:** classic Steam P2P (no port forwarding), plus a loopback/UDP
  transport for dev.
- **Sync mechanism:** the game is driven by **PlayMaker FSMs**, so syncing means
  intercepting FSM events/variables via Harmony — *not* calling C# game methods.

`PLAN.md` is the source of truth for architecture, the sync model, and the
roadmap. **Read it first** for anything non-trivial. `protocol/PROTOCOL.md` is
the source of truth for the wire format.

---

## 2. Repo map

| Path | What it is | Builds without the game? |
|---|---|---|
| `src/WinterMP.Net` | Engine-independent protocol: messages, serialization, channels. `net35;netstandard2.0`. **Unit-tested.** | yes |
| `src/WinterMP.Net.Tests` | xUnit tests for the protocol layer. Runs in CI. | yes |
| `src/WinterMP.Core` | The BepInEx 5 plugin: Steam, session, world-sync subsystems. `net35`. | **no** (needs game DLLs) |
| `src/WinterMP.Tools` | Dev plugin: F9 dumps the FSM/object catalog. `net35`. | **no** |
| `src/WinterMP.FastBoot` | Dev plugin: save-safe fast boot (ES2 load skip + menu accelerator). `net35`. | **no** |
| `src/WinterMP.Launcher` | .NET 8 Avalonia desktop app (Windows + Linux): install/repair, save backups, host/join. | yes |
| `src/WinterMP.Launcher.Tests` | xUnit tests for launcher policies (FastBoot safe profile). Runs in CI. | yes |
| `catalog/` | Generated per-game-build sync catalogs (FSM descriptors). | — |
| `protocol/PROTOCOL.md` | Wire protocol spec — keep in lockstep with code. | — |
| `docs/BUILDING.md` | Build, deploy, dev-loop instructions. | — |
| `docs/PLAYERS.md` | Player-facing install/host/join guide. | — |
| `docs/CODEMAP.md` | **Where to edit what** — subsystem → file routing. | — |
| `docs/AGENT-RECIPES.md` | Step-by-step checklists (messages, catalog, FSM hooks, debug). | — |
| `tools/*.ps1` | Build/release/installer scripts. | — |

---

## 3. Build & test

```powershell
dotnet build     # builds everything; Steam transport compiles only when game DLLs are present
dotnet test      # protocol unit tests (WinterMP.Net.Tests)
```

- `WinterMP.Net`, its tests, and the launcher build **anywhere**.
- `WinterMP.Core` / `WinterMP.Tools` compile against the **game's own** DLLs
  (Unity 5.0 `UnityEngine.dll`, `PlayMaker.dll`, embedded Steamworks). They need
  a real MWC install. Copy `Directory.Build.props.user.example` →
  `Directory.Build.props.user` and set `MwcGamePath`. That file is per-machine
  and gitignored — never commit it.
- If you cannot build `Core`/`Tools` (no game on this machine), **say so**. Don't
  fake a green build. Make your protocol/logic changes in `WinterMP.Net` where
  they can be tested, and lean on the unit tests.

Dev loop inside the game (see `docs/BUILDING.md` for the full list): **F8**
loopback session, **F9** catalog dump, **T** chat, hold **TAB** for the player
list. Logs at `<game>\BepInEx\LogOutput.log`.

---

## 4. Hard constraints — break these and the game crashes

1. **`.NET 3.5` profile for everything that runs in-game.** `WinterMP.Net`
   (shared) and `WinterMP.Core`/`WinterMP.Tools` (in-game) target `net35`
   against legacy Mono. **No modern BCL**: no `System.ValueTuple`, no `Span`, no
   `System.Threading.Tasks` niceties, no C# 8 nullable runtime helpers, no
   `record`, no string interpolation features that need newer runtime support.
   `LangVersion` is `latest` so the *syntax* compiles, but anything that needs a
   newer **runtime** will explode at load time in the game. The launcher
   (`.NET 8`) is the only place modern APIs are safe.
2. **Nullable reference types are on** (`<Nullable>enable</Nullable>`). Keep it
   clean; don't paper over warnings with `!` unless you genuinely know better.
3. **MSCMP is GPLv3 prior art** (same license as us — see `LICENSE`). Prefer
   clean-room; read MSCMP for *concepts*, not blind copy-paste (bugs, game
   differences). If you port MSCMP code, keep attribution and stay GPLv3.

---

## 5. Protocol discipline (this is where desyncs are born)

The wire format is shared between host and guests running possibly different
mod versions. Get this wrong and players silently desync or fail to connect.

- The protocol version lives in `src/WinterMP.Net/Protocol.cs`
  (`ProtocolInfo.Version`). **Any** change to framing, message layout, or
  message semantics **must bump it**. Hosts refuse mismatched clients on
  handshake — that's the safety net; keep it honest.
- **Never reuse a retired message id.** New fields are append-only and land
  *together with* a version bump. No silent format drift.
- When you change anything on the wire, **update `protocol/PROTOCOL.md` in the
  same change.** The doc and the code must never disagree.
- Respect the channel model: reliable-ordered (0) for events/economy/handshake,
  unreliable-sequenced (1) for transforms, reliable-bulk (2) for snapshots.

---

## 6. Keeping it tidy (the part you'll be tempted to skip — don't)

This codebase grows by accretion. Fight entropy actively:

- **Max ~1000 lines per file.** When a class outgrows that, split it with
  **partial classes** by responsibility, following the existing convention:
  `FsmWorldSync.cs` + `FsmWorldSync.Remote.cs` + `FsmWorldSync.Snapshots.cs` …,
  `VehicleWorldSync.cs` + `VehicleWorldSync.Engine.cs` + `.Climate.cs`,
  `ItemWorldSync.cs` + `ItemWorldSync.Spawn.cs` + `.Cargo.cs`,
  `SessionManager.cs` + `SessionManager.Messages.cs` + `.Handlers.cs`. One
  concern per partial; name the file `Type.Concern.cs`.
- **Prefer editing existing files over adding new ones.** Match the folder it
  belongs to (`Sync/`, `Steam/`, `Session/`, `Catalog/`, `Diagnostics/`, `UI/`,
  `Util/`).
- **No narrating comments.** Don't write `// increment the counter`. Comment
  only non-obvious *intent*, trade-offs, or game-specific gotchas (FSM quirks,
  ownership edge cases, why a hack exists). The game is weird; that context is
  gold. Mechanical narration is noise.
- **Clean up after yourself.** Delete dead code you replace. Don't leave
  commented-out blocks "just in case" — git remembers.
- **Don't commit build artifacts or generated junk.** `bin/`, `obj/`, `dist/`,
  `dumps/`, `*.log`, `Directory.Build.props.user` are gitignored — keep them
  out of commits. If you see them staged, unstage them.
- **One-off scripts:** throwaway helpers belong in `tools/` with a clear name,
  not scattered at the repo root. If a script is genuinely single-use, delete it
  when done.

---

## 7. Engineering values for this project (from PLAN.md §4.7)

Priority order: **correctness/completeness → ease of use → stability →
everything else.**

- **Crash containment.** A bug in one sync subsystem must log and disable *that
  subsystem*, not kill the player's game. Wrap mod callbacks (see
  `Util/SafeApp.cs`). The host's session and savefile are sacred.
- **Host-authoritative, always.** When in doubt about conflict resolution, the
  host's state wins and guests reconcile.
- **Self-healing over hard failure.** Prefer targeted soft-resync of an object
  group to tearing down the session.
- **Structured, ring-buffered logging** for intents/transitions — it feeds the
  launcher's bug-report zip. Useful logs > silent code.
- **Test protocol logic first.** Anything that can live in `WinterMP.Net` should
  land with a test in `WinterMP.Net.Tests`. That's the only loop that runs in CI
  without the game.

---

## 8. Catalog & game updates

MWC is Early Access and patches break FSM names/paths regularly. Sync
descriptors live in `catalog/` keyed by game build. When the game updates: dump
a fresh catalog (F9), diff against the old one to see what broke, regenerate,
re-test. Don't hand-hardcode against one build — keep it data-driven.

---

## 9. Shipping (delegated to a rule)

Releases follow `.cursor/rules/github-releases.mdc`: version bumps across
`WinterMP.Core.csproj`, `WinterMP.Launcher.csproj`,
`src/WinterMP.Launcher/Assets/wintermp-compat.json`, and
`installer/WinterMP.iss`, then `tools/build-installer.ps1`, then a tagged GitHub
release. **Only do this when the user actually asks to commit/push/ship.** Don't
self-trigger releases, and never commit unless asked.

---

## 10. When you're unsure

- Architecture / sync behavior → `PLAN.md`.
- Wire format → `protocol/PROTOCOL.md`.
- **Where is the code for X?** → `docs/CODEMAP.md`.
- **How do I add a message / catalog rule / FSM hook?** → `docs/AGENT-RECIPES.md`.
- How to build/deploy/test → `docs/BUILDING.md`.
- Still unclear and it's a real fork in the road → ask the user. Don't guess on
  anything that could corrupt a save or desync a live session.

Cursor index skips build output and raw `catalog/dump-*.json` (see `.cursorignore`);
use `tools/extract_fsm_details.py` to query dumps.

Leave the campsite cleaner than you found it.

---

## Cursor Cloud specific instructions

The cloud VM is **Linux** with the **.NET 8 SDK** preinstalled (system-wide at
`/usr/bin/dotnet`). The update script runs `dotnet restore src/WinterMP.Net.Tests`
on startup. What this means for what you can actually run here:

- **Buildable/testable on this VM:** `WinterMP.Net` (the protocol library;
  `net35;netstandard2.0`), `WinterMP.Net.Tests`, and the Avalonia launcher
  (`WinterMP.Launcher`, `net8.0`) with `WinterMP.Launcher.Tests`. That is the
  same set CI builds and tests.
- **NOT buildable here:** `WinterMP.Core`, `WinterMP.Tools`, and
  `WinterMP.FastBoot` reference the game's own Unity/PlayMaker DLLs (set via
  `Directory.Build.props.user` / `MwcGamePath`) and need a real My Winter Car
  install. Don't fake a green build for these; make protocol/logic changes in
  `WinterMP.Net` and lean on its tests (see §3, §5).

- **Gotcha — no working solution file for the CLI:** the repo ships `WinterMP.slnx`
  (no `.sln`), and SDK 8.0.128 does **not** understand `.slnx`. So bare
  `dotnet build` / `dotnet test` at the repo root (and `dotnet build WinterMP.slnx`)
  fail. Always target a project path. Canonical commands here:
  - Tests (CI loop): `dotnet test src/WinterMP.Net.Tests`
  - Launcher tests: `dotnet test src/WinterMP.Launcher.Tests`
  - Build protocol lib: `dotnet build src/WinterMP.Net/WinterMP.Net.csproj`
- There is no separate lint step; the build is the check (nullable refs are on,
  see §4.2). Treat a clean `dotnet build` of `WinterMP.Net` plus green tests as the
  bar before committing protocol changes.
