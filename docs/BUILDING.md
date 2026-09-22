# Building & developing WinterMP

## Inspect game logic without launching

`tools/extract_fsm_assets.py` reads the installed Unity 5 scene and PlayMaker globals,
including action parameters and referenced objects. It never writes game files or saves.
Use a separate Python environment with `UnityPy==1.25.3` and
`TypeTreeGeneratorAPI==0.0.10`, then run:

```bash
python tools/extract_fsm_assets.py "/path/to/My Winter Car" \
  --match Systems/BankAccount --match ATM --match Sheets/DebtLetter --match PartBreakages \
  --out /tmp/wintermp-economy.json
python tools/check_fsm_bindings.py --globals /tmp/wintermp-economy.json
python -m unittest discover -s tools/tests
```

The extract records a scene hash and labels values as asset defaults. It does **not**
prove runtime initialization, save-loaded values or multiplayer behavior. Keep the F9
dump for those checks. Generated extracts belong outside the repository.

The `banking`, `slotMachines` and `vehicleDamage` sections in `catalog/sync-catalog.json`
hold economy globals, ATM/slot action bindings, weighted reel/payout rules and fitted-part wear mappings. Update
them from fresh evidence after a game patch. Damage arrays have exactly 16 wire
slots: leave retired random-selector positions 13 and 15 empty; never renumber them.
Catalog parser tests reject invalid slots and ambiguous transfer bindings. The
handshake catalog hash requires host and guests to use the same definitions.
Slot reel weights must come from the three `PlayMakerArrayListProxy` components;
reel strings are rewritten during wildcard payout evaluation, so they are not a
source for the original odds. Compare all nine-symbol combinations against the
native payout states after updates. Static checks do not replace the two-player
slot acceptance cases listed in COVERAGE-ROADMAP R2.12.

## Prerequisites

- .NET SDK 8+ (builds everything; the plugins target net35 via reference assemblies)
- The game runs on Windows or through Proton on Linux; the launcher runs natively
  on Windows and Linux. Building the plugins on Linux works with installed game DLLs.
- A My Winter Car install — the plugins compile against the game's own
  `UnityEngine.dll` (Unity 5.0) and `Assembly-CSharp-firstpass.dll` (embedded
  Steamworks.NET). Set the path in `Directory.Build.props.user`.
  Only `WinterMP.Net` + tests + launcher build without the game.

## Build & test

```powershell
dotnet build src/WinterMP.Net -c Release
dotnet test src/WinterMP.Net.Tests
dotnet test src/WinterMP.Launcher.Tests
dotnet build src/WinterMP.Core -c Release -p:DeployToGame=false
dotnet build src/WinterMP.Tools -c Release -p:DeployToGame=false
dotnet build src/WinterMP.FastBoot -c Release -p:DeployToGame=false
dotnet build src/WinterMP.Launcher -c Release
```

Project paths also work with .NET 8, which cannot open the repository's `.slnx`
solution. `DeployToGame=false` keeps verification builds in the workspace; omit
it when intentionally installing a local build into the configured game.

`WinterMP.Net` + tests build anywhere; `WinterMP.Core`/`WinterMP.Tools` pull
BepInEx packages from the BepInEx NuGet feed (configured in `NuGet.config`)
and reference UnityEngine/Steamworks from the game install.

> Game profile (verified on build 23268598): Unity 5.0, legacy Mono with the
> .NET 3.5 profile → everything loaded in-game targets `net35`, and
> `WinterMP.Net` multi-targets `net35;netstandard2.0`.

## Deploying into the game

1. Install BepInEx 5 x64 (5.4.23+) into the game folder and apply the MWC
   entrypoint fix (`[Preloader.Entrypoint] Type = MonoBehaviour` in
   `BepInEx/config/BepInEx.cfg`). The launcher's *Install / Repair* button
   applies the config fix automatically once BepInEx is extracted.
2. Copy `Directory.Build.props.user.example` → `Directory.Build.props.user`
   and set `MwcGamePath`.
3. `dotnet build` — the plugins (and `WinterMP.Net.dll` / `Steamworks.NET.dll`)
   auto-deploy to `<game>\BepInEx\plugins\WinterMP\`.

## Dev loop

- **F8** in game: starts a loopback session (host + fake guest in one process)
  — exercises the full handshake/chat/ping path without Steam or a second PC.
- **F9** in game (Tools plugin): dumps the FSM/object catalog to
  `<game>\WinterMP\dumps\catalog-*.json`. Commit interesting dumps to
  `catalog/` (named by game build) and diff them across game updates.
- **T** in game: chat. **TAB** (hold): player list + session status.
- Logs: `<game>\BepInEx\LogOutput.log`.

### Local two-player test on Linux

Run `tools/install-desktop-local2p.sh` to prepare `build/local2p/game` and an
isolated Proton profile in `build/local2p/compatdata`, then update the **Local 2P
Test** desktop shortcut. The initial setup copies the installed game and its
current save profile; subsequent setup runs update the test mod while preserving
that test save. The normal Steam installation and personal saves are unchanged.
Use `--game-dir /path/to/game` for another installation or `--no-desktop` to only
prepare the copy. `WINTERMP_LOCAL2P_DIR` can choose another test directory.

Double-click the shortcut and keep its terminal open while playing. It starts
the host, waits for its readiness signal, then starts Guest over localhost UDP.
Both instances use identical mod files and the copied profile; current Core
blocks guest world writes. A lock prevents duplicate test launches and updating
the test mod while it is running. Close both windows when finished. FastBoot
waits for the native button to initialize and sends its hover/press events as
one gesture, briefly pausing the physical mouse poll that could cancel it.
The test uses the normal Continue loading sequence: menu wait skipping, async
preloading, forced GAME loading, ES2 tag skipping, whitelist and deferred
hydration remain disabled.

If Xwayland reports zero monitors, the tester automatically gives each player a
separate 1280×720 Wine virtual desktop window. This avoids Unity 5's empty
display-list failure and disabled player camera without changing desktop-wide
monitor settings or the normal game profile. `WINTERMP_LOCAL2P_DESKTOP=1` forces
this mode; `0` keeps direct launch and `auto` is the default. A connected monitor
or unavailable monitor detection keeps direct launch. Headless diagnostics
always skip the virtual desktop.

Logs are in `build/local2p/game/BepInEx`: `LogOutput-host.log`,
`LogOutput-guest.log`, and `launch-host.log`/`launch-guest.log` for Proton startup
errors. `WINTERMP_PROTON` can select an explicit Proton executable;
`WINTERMP_COMPAT_DATA_PATH` overrides the prefix for scripted launches. The
launcher tests run with `python3 -m unittest discover -s tools/tests -p
test_local2p_launcher.py`. The test setup requires the installed game, .NET,
Python 3, `unzip`, `flock`, and Steam/Proton.

For a fresh save copy or game update, close both windows, archive the existing
`build/local2p` directory and rerun setup. Do not use `-nographics` as proof that
MWC can load its full world: this Unity build crashed during the headless world
load while the windowed host and guest both reached GAME. See
[PERFORMANCE.md](PERFORMANCE.md) for the separate native discovery benchmark and
its limits.

The native Continue checks run with `--wintermp-menu-continue-probe` in the
marked, isolated probe copy described in PERFORMANCE.md. The probe compiles
the production `MenuContinue.cs` directly and verifies readiness, cancellation,
retry, native state transitions, restoration of input flags and one-time clicks.

## Launcher (release packaging)

```powershell
.\tools\build-launcher.ps1          # dev build → bin\Release\net8.0\
.\tools\publish-release.ps1         # self-contained win-x64 + dist\*.zip
.\tools\build-installer.ps1         # publish + OurWinterCar-Setup.exe (installs Inno Setup via winget if needed)
```

Release zips land in `dist/`:
`OurWinterCar-Launcher-win-x64.zip` (full launcher) and `OurWinterCar-payload.zip`
(attach to GitHub releases for in-launcher mod updates).

### Versioned tester kit — UNRELEASED LOCAL TEST PACKAGE

Local package: mod **0.1.33**, protocol **265**, channel **test**.

Target game build: 23268598. This is not the historical public 0.1.33 kit. Match
protocol and payload hashes, not only mod version. On Linux, build and verify a
separate attempt without deployment, native launch, Git operations or publication:

```bash
python3 tools/build-test-release.py \
  --run-dir "$RUN/package-attempt-01" \
  --game-path "/path/to/read-only/My Winter Car" --payload-only
```

`RUN` is the actual assigned round evidence directory. Every attempt path must be
new; existing paths/archives are refused, not overwritten. The builder always rebuilds
Net/Core/FastBoot, runs Net/Launcher/Python tests, validates source/docs/protocol and
uses the launcher's read-only `--verify-payload <directory>` entry point. No UI or
installer is entered. All dotnet builds use `--no-restore`, `DeployToGame=false` and
explicit `MwcGamePath`; missing cached build dependencies must be resolved separately.
`--skip-build` and `--skip-tests` reuse is no longer accepted. Logs, command/exit-code
receipts, source hashes and input assembly hashes stay under that fresh attempt;
its `package/` contains docs, validation, checksums and the exact five-file payload ZIP.
`OurWinterCar-local-test-kit.zip` wraps that directory for local handoff; `result.json`
records its hash. Raw build/test receipts remain alongside it, not inside the mod payload.

Independently recheck the generated outer/inner archives, docs, source hashes, raw
command log hashes and read-only assembly inputs, then exercise the production CLI
against deliberately invalid copies (protocol/version/hash/corrupt JSON/conflicting
install flag) without entering installation or UI:

```bash
python3 tools/verify-test-release.py --attempt "$RUN/package-attempt-01" \
  --evidence-dir "$RUN/package-audit-01" --game-path "/path/to/read-only/My Winter Car"
```

The audit directory must also be new. Temporary mutated payloads are scoped there
and removed; `audit.json` and raw stdout/stderr preserve each assertion and command.

Payload-only is an explicit partial deliverable, NOT an installable launcher kit.
Omit `--payload-only` to build both self-contained launchers; this requires
`vendor/BepInEx_win_x64_5.4.23.5.zip`, cached RID dependencies and a passing current
dependency advisory query. Missing vendor/tool inputs fail closed with a receipt.
Optional `--appimage`, `--inno-prefix` and `--cosmocc` keep their existing meanings;
Inno requires `drive_c/inno/ISCC.exe` in the dedicated prefix. No optional installer
is claimed unless built. Autonomous runs may not use native compiler prefixes outside
the disposable test rig. No source archive is inferred from Git in this directory
snapshot; `source-hashes.json` records the current source instead.

The payload manifest hashes Core/Net/FastBoot/catalog; validation also hashes the
compatibility JSON itself (no circular self-hash). The launcher reads the protocol
constant directly from Net metadata without loading game assemblies. Archives reject
game DLLs including ES2, developer probe binaries, secret paths and symlinks.
`targetGameBuildIds` records the build used for compilation/binding checks;
`testedGameBuildIds` stays empty until multiplayer testing is complete.
The builder records actual test counts, not earlier release totals. Native discovery,
ordinary input, different saves, fresh-player late join, native save/reload, Steam/two-PC
and four-player soak stay NOT_TESTED for this package unless separately proven.
The package builder never commits, pushes, tags or uploads a release.

### Universal installer (one file, Windows + Linux)

```bash
export COSMOCC=~/cosmocc/bin/cosmocc      # Cosmopolitan toolchain
./tools/build-ape-installer.sh            # -> dist/OurWinterCar-Installer.com
```

Compiles a single [Actually Portable Executable](https://justine.lol/ape.html)
that carries both self-contained launcher builds and installs the mod on either
OS (it detects the OS, extracts the matching launcher, and runs its
`--install-mod` path). Toolchain setup + the Wine `binfmt_misc` gotcha are in
[installer/ape/README.md](../installer/ape/README.md).

### Windows Setup.exe — on Linux, via Wine

The Inno Setup installer is **not** Windows-only. `ISCC.exe` compiles under Wine,
so the exact same `OurWinterCar-Setup.exe` builds on Linux:

```bash
./tools/build-setup-linux.sh              # -> dist/OurWinterCar-Setup.exe
```

It installs Inno Setup once into a throwaway Wine prefix (`~/.cache/ourwintercar/`,
never `~/.wine`) and runs `wine ISCC.exe WinterMP.iss`. With this,
`build-ape-installer.sh`, and `build-appimage.sh`, **every** release artifact is
Linux-buildable — only the `ship-release.ps1` orchestration still expects Windows/pwsh.

Player guide: [PLAYERS.md](PLAYERS.md).

Agent routing: [CODEMAP.md](CODEMAP.md), [AGENT-RECIPES.md](AGENT-RECIPES.md).

## Testing real multiplayer

Steam allows one running instance per account, so end-to-end tests need either
two machines/accounts, or the loopback transport for protocol-level work.
Protocol logic should always land with unit tests in `WinterMP.Net.Tests`
first — that's the loop that runs in CI.

## Command line

| Argument | Effect |
|---|---|
| `-wintermp host` | Create a friends-only lobby after boot (what the launcher's HOST does) |
| `-wintermp join <lobbyId>` | Join a specific lobby |
| `-wintermp-fast` | Launcher host/join fast path: zero splash grace, early Steam attach, lobby setup on splash when possible |
| `+connect_lobby <lobbyId>` | Set by Steam's invite/Join Game flow; honored automatically |
| `-wintermp hostlocal [port]` | Host a localhost UDP test session (no Steam) |
| `-wintermp joinlocal [addr:port]` | Join a localhost UDP test session |
| `-wintermp-playername <name>` | Display-name override (test tooling) |
| `-wintermp-autoload` | **Deprecated** — auto-load is always on via WinterMP FastBoot when a save exists |
| `-wintermp-doortest <sec>` | Self-test: auto open/close the WC door N seconds after world sync is ready; acks via chat |

### Join timeout and retry verification (2026-09-09, unreleased)

An active guest join now allows 60 seconds to reach its host, followed by 60
seconds for the mod handshake. Transport keepalives and duplicate connection
callbacks cannot extend the handshake deadline. The receive queue runs before
the deadline check, so a ready acceptance after a slow game frame wins. Accepted
sessions and hosts waiting for players are outside this join-only timeout.

Failure uses the existing deferred transport/lobby cleanup and preserves its
reason. A join started from the launcher friend picker restores that picker after
cleanup, without waiting for the 45-second automatic status reset. Steam invites
can still start another attempt. Guest world-save protection remains latched.
No message layouts, admission rules or protocol version changed (still 210).

Validation passed **4,300 protocol tests** and **32 native save/join checks**;
Core, Net and the developer probe built with zero warnings/errors. The native
probe exercised a real UDP host that kept its transport alive without replying
to the handshake: the guest failed at the 60-second deadline and joined a replying
host on its next attempt in the same game process. Other checks cover queued and
late acceptances, explicit refusals, cleanup, picker availability and protection.

Run `tools/GuestSaveProbe` only in an isolated game copy/profile, with
`--wintermp-guest-save-probe --wintermp-join-recovery-probe` and the marker
`wintermp-join-sandbox.txt` in that copy's root. It uses loopback fixtures plus a
UDP host on port 38948, takes about a minute and quits. Remove the probe and marker
afterward. Local results are under `build/join-recovery-audit/`.

Steam two-account callback timing, physical friend-picker interaction and slow
two-PC world loading still need acceptance. Initial game/Steam startup waiting
and disconnect detection for already-admitted players are separate flows.

### Save and resume (2026-09-13, unreleased v214)

The local checkpoint now preserves shared money, a partly full shopping bag,
its loose grocery, and the returning guest profile across host save/quit/reload.
Two fresh processes then open only the bag's remaining purchases and reconnect
again without duplicating the three groceries. Both spawn locations restore saved
needs. Initial reports wait for the host offer, a loaded player and completed
relocation; later world snapshots do not replace the offer or reopen the prompt.
Native SAVEGAME is observed before its broadcast recipients reset the player,
preventing teardown/menu poses from overwriting the last playable position.

Saved loose groceries lacked creation manifests after a process restart. The host
now rebuilds replay descriptors from registered native items with initialized IDs
and verified shopping-product templates. Existing live-spill identities, retired
items and consumed bodies remain excluded. Drinks without a Consumed boolean are
supported; milk uses a numeric sentinel and removes its Rigidbody on consumption.
This adds no fields or message IDs, but changes spawn/profile semantics, so protocol
214 is required on both peers. Guest-profile sidecar format is unchanged.

Validation uses `build/local2p/game` and **two separate disposable Proton prefixes**
under `build/persistence-audit/profiles`. `LiveBagProbe.Persistence.cs` requires both
`WINTERMP_LOCAL2P_PERSIST_TEST=1` and a marker in its own persistent-data folder. It
uses a fixed UDP client endpoint only in this probe, making one returning test
identity; production Steam/UDP identity code is unchanged. The driver enters the
actual native shop checkout, hand pickup, bag opening and save-to-menu states;
it does not write an artificial world save. Saved needs fixtures travel through
the ordinary guest report and host sidecar path. Physical mouse/keyboard input is
not exercised by this driver.

**19 two-game checks pass:** six in `final-first` (purchase/open-one, guest report,
both native save flows and retained guest position), thirteen in `verified-reload`
(saved item/bag/cash/profile, unanswered choice, last-position restore, repair
snapshot, remaining-only opening, and same-session reconnect/host-position restore).
The save stage preceded the final milk-filter correction; the final restart stage
uses the corrected Core. Their exact hashes are retained separately; this is not
claimed as the whole gameplay journey on one final build. The final Net binary is
identical across both stages. The disposable guest's 14 original save/config files,
including the sidecar, remain byte-identical, including after its own native save action.
All 14 protected personal-save files and four normal installed payload files are
also unchanged. Test DLLs and game-root markers are removed after each run.

The final build passes **4,330 protocol tests** (five new resume-policy cases),
**1,543 native checks** across the two affected suites, and clean Net/Core/probe
builds. The native run uses the same Core/Net/catalog as `verified-reload`.
Launcher code is unchanged.

`build/persistence-audit/summary.md` records attempts, checks, source/payload hashes,
file comparisons and cleanup. Failed attempts are retained: loading zeroed guest
needs, native save reset the guest pose, and milk's absent Consumed boolean blocked
reconstruction. One native fixture needed its inactive object's transform positioned
before activation. The optional probe scripts are archived in that audit's `drivers/`.

Real Steam identities, two physical PCs, physical save controls, long sessions,
permadeath and the complete join/shop/drive/sleep/save journey on one identified
build remain open. This test establishes grocery identity/count/visibility; detailed
per-item food condition and every product variant are not covered. The next bounded
gameplay investigation is the eight disabled native valve-adjustment Screw bindings
seen in both logs, rather than more persistence or performance work without a failure.

### Sorbet running-engine handoff (2026-09-13, unreleased v213)

The Sorbet now takes its engine state from the previous accepted simulator when a
new player occupies the native driver seat. The catalog opt-in validates its
starter/ignition graph before applying native activation, gear, RPM/integrator and
transient coolant temperature once. It excludes cranking audio, battery drain and
starter time. The former owner stops its native engine; raw Sorbet starter/ignition
state replay is retired. Corris and taxi retain their previous behavior.

The two-game investigation found two additional causes: a warm host engine was
handed to a guest still simulating at about -17 C, and inactive FuelLine scratch
retained 650 RPM after the engine stopped. VehicleState 60 now appends exact signed
handoff temperature (35 bytes including ID), and Sorbet RPM comes from its active
native drivetrain. Incoming packets store temperature without writing its producer;
only a validated local ownership claim restores it. OFF and ACC-only states never
request running-engine activation. The Corris host coolant stream is unchanged.

Core/Net/probe build with zero warnings/errors. **4,325 protocol/catalog tests and
1,511 isolated native checks pass.** The native suite covers the signed source,
unavailable/invalid heat, stale RPM rejection and live/final/snapshot temperature.
Use the existing vehicle-state probe fixture invocation described below.

The final isolated two-game run passed **24 checks**: native host start, powered
travel with an accepted passenger in each driver role, matching car poses, stable
idle after parking, running handoff host-to-guest and guest-to-host, exits restoring
both movement controllers, OFF remaining off on takeover, and ACC-only transfer
without starting. Both games closed with launcher exit 0; probes and markers were
removed. All 14 protected personal-save files and four installed mod files retained
their original hashes. Nothing was saved, committed or released by this test.

Local evidence, all attempts, exact payload hashes and the scoped change are under
`build/engine-handoff-audit/`. The first movement assertion was invalidated because
rolling with a stopped engine could pass it; the final driver requires a running
native engine as well as displacement and speed. A later accessories check exposed
the stale-RPM restart and was retained as a failed attempt. These checks use isolated
Linux/Proton virtual desktops, local UDP and injected native control inputs. They do
not establish physical keyboard/controller, Steam/two-PC, Corris/taxi handoff,
long-distance driving, moving-seat takeover or save/reload acceptance.

### Driver exit and takeover verification (2026-09-13, unreleased v212)

The native `GlassFrosting.PlayerIn` flag covers cabin proximity, including standing
beside the car after getting out. The old driver fallback treated that flag as
seated ownership and kept the other peer's driver trigger blocked. Driver claims
now follow the native player hierarchy, with passenger seats excluded. Cabin
heating still uses its own proximity flag. Protocol 212 updates ItemTransform (42)
driver semantics without changing its layout or IDs.

Core/Net/probe builds have zero warnings/errors; **4,300 protocol tests and 1,508
native checks pass**. The native vehicle probe now checks proximity, actual
seating and exit with proximity still true for Sorbet, Corris and taxi. It runs
through the existing `--wintermp-guest-save-probe --wintermp-vehicle-state-probe`
fixture set, including its extracted native JSON files.

The isolated two-game UDP replay reproduced the stuck driver seat before the fix.
On v212 it verified host travel, accepted passenger following, braking, exit and
guest takeover while the old driver's cabin flag remained true. It then failed
engine continuity: the guest owned the car but its native Starter stayed in
`Wait for start` with zero RPM. This was the next gameplay blocker at v212; see the v213 handoff validation above.
Manual guest ignition did start the engine. Subsequent guest movement trials
failed their distance/speed thresholds, although the two car poses agreed; the
cause is unresolved. Retain those failures and repeat guest travel after fixing
engine handoff, with a known clear route and native controls.
The reverse-role exit check passed: the guest released the driver's seat, the host's
driver entry became available again, and both players regained their movement controllers. The final
run recorded 11 passing assertions and four failed assertions (engine continuity
and three guest movement trials), then closed both games with launcher exit 0.

`LiveDriveProbe.cs` is opt-in developer tooling: deploy only to an isolated copy
with `WINTERMP_LOCAL2P_DRIVE_TEST=1` and `wintermp-live-drive-sandbox.txt`. Its
`live-drive/<role>-command.txt` interface takes a monotonically increasing sequence,
command and arguments separated by tabs; replies are `<role>-<sequence>.txt`.
It exercises native entry/exit and ignition, accepted passenger entry, and bounded
pedal input through `AxisCarController.GetInput`. Release the native parking lever
before driving. Player placement beside a seat is automated; vehicle position,
RPM, clutch and wheel physics are not fabricated. Never ship the probe; remove its
DLL and marker afterward. Local run logs, payload hashes, failed attempts and
drivers are preserved under `build/shared-drive-audit/`.

The successful rendered replay used `WINTERMP_LOCAL2P_DESKTOP=1`; ordinary window
attempts stalled/disconnected at startup, and a headless attempt lost its host
during GAME loading. These attempts are recorded separately. Physical input,
normal-window startup reliability and real Steam/two-PC acceptance remain open.

### Sleep consent verification (2026-09-12, unreleased v211)

The host now leaves Confirm through its native release destination when consent
is declined, expires or loses every requested guest. Releasing the bed retires
the round immediately, so late acceptance cannot restart it. Remaining approvals
are reevaluated on guest departure; only the first answer from each requested,
still-connected guest counts. Leaving a session also clears its unanswered prompt.
The consent contract changed in protocol 211; message layouts/IDs remain 26–28.

Static extraction of build 23268598's sleep FSMs showed the old cancellation was
wrong: STOP only transitions from Conditions?, while global ABORT enters Calc
rates, which applies wake-up needs. Native PlayMaker fixtures reproduced eight
failures before the change. The final Core/Net/probe build has no warnings/errors;
**4,300 protocol tests and 35 native checks pass** (21 save guards + 14 sleep).
The sleep checks include cancellation, timeout, guest departures, unrequested and
duplicate answers, retry, solo sleep, guest blocking and post-sleep completion.

Use `--wintermp-guest-save-probe --wintermp-sleep-consent-probe` with
`wintermp-sleep-sandbox.txt` in an isolated copy/profile. The probe quits after
writing `guest-save-probe/result.txt`. Never install the probe in the normal game.

A separate two-game local UDP run passed **12 checks** against the actual home
bed FSM. Decline, host release, timeout and disconnect cleared the wait/prompt;
accepted sleep ran the native animation, clock and wake-up sequence. Both clocks
advanced from 10:00 to 13:00, the host returned to State 1 with PlayerStop and
PlayerSleeps false, and both players were rested. Host fatigue reached -9 through
the native decrement; guests retain the existing accepted-result reset to zero.

For that opt-in driver, set `WINTERMP_LOCAL2P_SLEEP_TEST=1` and create
`wintermp-live-sleep-sandbox.txt` in the isolated copy. `LiveSleepProbe` accepts
sequenced commands in `live-sleep/<host|guest>-command.txt` and records snapshots.
It starts after the physical bed-use gesture and invokes the prompt's response
handler; it does not prove physical input or Steam behavior. Local evidence,
payload hashes and one-off drivers are in `build/sleep-consent-audit/`. The final
cleanup removes repeated waiting chat; the live-tested consent transitions are
unchanged. Remove probe DLLs and markers after testing.

Still open: real Steam/two-PC input and latency, other bed variants, alarm-limited
sleep, broader survival-needs parity and save/rejoin after sleeping. The live run
also logged Unity sound/array warnings before and during the checks; their cause
was not established by this focused test. This is a passing sleep-flow check,
not a claim of a warning-free world or completed M7 soak acceptance.

### VideoPoker verification (v93)

`PokerTests` exercises all 2,598,960 five-card combinations, held-card replacement,
private doubling, payment receipts, disconnects and teardown. `PokerSync` binds
native visual assets from the catalog's `videoPoker.assets` action references; the
asset extractor now resolves PlayMaker's shared material/texture object table.
Installed build 23268598 provides 44 visual assets and 12 physical input bindings.
Static evidence does not verify screen rendering or input timing in a running game.
Use the R1.3 two-player checklist in `COVERAGE-ROADMAP.md`, including an inactive host
LOD, leaving/rejoining mid-hand, and returning to singleplayer after ending a session.

### Passenger and recurring-freeze verification (v95, unreleased)

Use the current protocol (97) on both peers. The passenger authority changes began
in v95; mismatched protocol versions are refused at handshake.

- Ride as a guest passenger in the Sorbet and Corris for at least two minutes,
  including acceleration and turns across several eight-second seat keepalives.
  Confirm the passenger stays seated locally and on the host.
- Exit and re-enter, change seats, race another player for one seat, then reconnect.
  Confirm one occupant per seat and no seated avatar after an exit/rejection;
  a late joiner must see the same occupancy.
- Check frame pacing across repeated five-second world-discovery passes, both
  parked and driving. If a discovery pass still takes 50 ms or more, the log
  records `WorldSync: ... discovery took ... ms` (rate limited to ten seconds).
- Open a grocery bag and activate previously inactive world interactions to check
  that dynamic discovery still works. Object IDs retain the existing path format;
  the path index is discarded after each synchronous scan.

Automated regressions cover passenger authority, delayed/stale keepalives, rejected
claims, seat races, sequence wrap/reconnect, path/ID equivalence and linear sibling
lookup cost. Actual frame pacing and passenger attachment still need two-player
runtime confirmation.

### Parked-vehicle checksum verification (v96, unreleased)

`VehicleChecksumTests` covers damage/repair symmetry, condition drift and snapshot
recovery, sender/sequence independence, and all 256 tire-pressure bytes across repeated
handoffs. Since v124, the host checksum reads native engine damage and guests use
accepted host damage state; guest native mount Wear remains untouched. Condition
keeps its existing owner-authority path. Checksum reads do not advance send sequences
or change baselines. This does not verify native FSM
timing; R2.4b stays open until the following two-player check passes:

- Park a damaged Corris with one flat tire. Let both peers settle for at least three
  checksum periods (about one minute); verify matching damage, wheel condition and pressure.
- Repair the part and tire, then drive/exit on each peer to hand ownership back and forth.
  Reconnect and confirm the repaired state survives the join snapshot.
- In a disposable test session, introduce a different parked-car tire pressure or
  condition on the guest. Verify a `vehicles=True` checksum mismatch requests a soft
  resync, the condition converges, and the next three checksum periods stay quiet.
- Drive, park and wait while the engine cools and windows frost. Vehicle mismatch logs
  must not repeat merely because ownership, RPM or climate samples differ.

### Lottery form isolation verification (unreleased catalog fix)

With two players on the updated catalog, leave one player outside the shop and
have the other hover over BuyLotto, BuyMegaveto and a Lotto ticket. Hovering must
not produce `buy ... intent USE` traffic or open a form on either screen. A press
must open the form only for the interacting player, and closing/inspecting it
must leave the other player's camera and menu untouched. Repeat with host and
guest roles reversed and inspect a purchased/loaded ticket. Test both peers with
the same catalog hash; the handshake should reject an older catalog.

These checks validate form isolation only. The v103 Lotto purchase/claim path
has its own verification matrix below; Megaveto remains R2.21 work. The evidence
checker can catch premature input guards offline (see catalog/README.md).

### Lotto draw verification (v102, unreleased)

The protocol tests and game-DLL build do not prove the native gameplay path.
Use disposable save copies and two players on the same protocol/catalog:

1. Start with different local Lotto draws. Join with the TV off and again while
   teletext page 301 is open. Compare CurrentRound/TicketRound, all three pots,
   seven main numbers, three bonus numbers and all five winner/prize tiers.
   Host values must replace the guest lists without changing `UTNational7`.
2. Cross Saturday 21:00 and let the native host draw finish. During Draw/Draw2
   and prize calculation, no partial snapshot may publish. Both number lists
   must be sorted, unique and disjoint in 1..39. The guest Numbers FSM must stay
   paused and must not receive replayed CHECKLOTTERY/RESULTS/LOTTODRAW events.
3. Join during a draw and delay scene binding on a guest. The completed host
   state must apply after binding, including full jackpot values above 65535.
   Request FSM-group resync and confirm the same state; existing peers must
   still receive changes observed while a join snapshot was built.
4. Compare the Reset points teletext hide and its reveal after 21:00; leave the
   page open through the update. Texts must refresh without stale numbers or
   prizes. Change a host list/prize with the same round in a debug session and
   verify it propagates without waiting for another round.
5. Record the guest's original list contents, scalar values, DrawDone, display
   activeSelf and Numbers enabled/RestartOnEnable. Disconnect, leave the level,
   and repeat reconnect. Restore local contents before Numbers resumes; verify
   guest save tags/files did not acquire the host's lottery data. Check `lotto`
   ring-buffer entries and the subsystem error log for any failed binding/restore.

A matching teletext draw does not validate ticket purchases or claims. Verify
the v103 Lotto path below separately; Megaveto remains separate hockey betting.

### Rally crossing verification (v101, unreleased)

`RallyProgressTests` covers fixed packet bytes, truncation, exact retry/acknowledgment,
ordered crossings, invalid report rejection before sequencing, per-player revisions,
wrap, reconnect tokens, bounded proximity history, frozen finish times and join
snapshots. A 300-race seeded pass runs both 4- and 6-checkpoint stages through the
wire codec with transient rejections and missing acknowledgments.

Installed action evidence confirms SS1/SS2/SS3 have 4/6/4 numbered markers and the
matching native Timing bool variables. Catalog `rallyProgress` identifies each
binding. Marker `Checkpoint` bools are unused; `Set bool` writes Timing and ends in
`Idle`. The adapter observes that durable completion state even when Timing clears
its flags at finish. To refresh:

```bash
python tools/extract_fsm_assets.py /path/to/game --match RACES/RALLY --out /tmp/rally.json
```

Two-player checks remain pending:

- Start and finish all three stages. Compare host-received progress at every marker
  and the frozen finish time, including Timing clearing all its flags and SS2
  checkpoint 4 activating markers 5/6. Run different stages simultaneously; each player's
  record must advance independently.
- Delay player poses relative to reliable crossing reports, and interrupt delivery
  of an acknowledgment. A brief delay within the host's 3 s evidence window should
  recover; duplicate starts must not reset the clock, and duplicate finishes must
  preserve the recorded time. Check `rally-crossing` diagnostics.
- Join with existing native start/checkpoint flags, reconnect mid-stage, and restart
  a stage normally. Initial flags must not replay old crossings or restart the host
  record. A reconnect can continue when native progress agrees; the adapter does not
  reconstruct a fresh local race from the retained host record.
- Delay marker binding and make a report with no recent verified marker/driver pose.
  Incomplete stages must wait, and unsupported crossings must not be accepted. After
  10 s without confirmation, the guest gets one restart message; a fresh native
  stage start must clear the failed queue. Verify later single-player racing too.

Core builds against the installed game DLLs. These automated/static checks do not
validate native trigger timing, race payouts, or opponent fleet synchronization.

### Ventti property verification (v97, unreleased)

`VenttiPropertyTests` covers all valid key/access combinations, losses after wins,
partial bindings, stale snapshots, sequence wrap and reconnects. The installed
23268598 actions confirm three global key ints plus the cabin Sleep parent, hatch
Handle and Logwall Use component. Sleep's only FSM is on its SleepTrigger child;
discovery binds the parent actually toggled by the game. Home door opening checks
`PlayerKeyHome`; native closing already uses the catalogued door state path.

- With the guest away from the table, win and lose the car/house wagers on the host.
  Compare the native key flags and home/cabin access. Confirm money and stress are
  not applied a second time on the guest.
- Join after a transfer and visit the cabin with its LOD initially inactive. Check
  sleep, the woodstove hatch and logging, including previously granted access that
  the host has revoked.
- Disconnect and verify the guest's original local keys/access return, including
  controls that were already disabled before joining. Repeat a join/end cycle.

Property replication is separate from the v99 gameplay adapter below (R2.12).
Full-width table observations follow below. Save-point activation stays host-local;
the mod does not replay the native transfer states on guests. Full runtime checks
remain pending in R2.11.

### Ventti host-ledger verification (v99, unreleased)

`VenttiLedgerTests` exercises native rules, escrow, all six outcomes, leases,
committed decks, duplicate settlements and precise deferred returns, with a
500-hand conservation sweep. `VenttiGameTests` adds fixed wire bytes, malformed
packets, ordering/wrap, bounded refresh retries, and 900 seeded cash/car/house
hands through the actual state/receipt codec and deferred guest replica.
`CatalogBindingTests` covers native card slots and outcome/presentation bindings.
Core builds against the installed game's DLLs; this does not replace playtesting.

With two players and backed-up saves, verify:

- Let the native manager load before adoption; start hosting mid-native-hand and
  confirm adoption waits for its reset. With host controls inactive, let the guest
  bet, decrease, hit and stand using the normal table pick targets.
- Have both players click together. Only the leased player should spend/draw;
  disconnect/rejoin and let a lease expire without rerolling the existing hand.
- Compare every card texture and total, including >9-card low-value hands, then
  late-join during play and after each of the six outcome types. Repeat with LOD
  activation/deactivation and the host elsewhere in the world.
- Check the shared wallet once per accepted action, including retries; only the
  acting player's stress changes. Check key/access transfers, home doors, cabin
  save points on the host, NPC dialogue/animations on both peers, and closure.
- Save/load progression and leave while betting, playing, and resolving. An
  undealt cash stake is refunded; a dealt hand stands on its existing deck. Confirm
  native controls work afterwards and guest local values/materials are restored.
  For a wallet too large to represent an exact return, verify the owed value stays
  in native paid stake after reset and can be decreased or adopted next session.

The implementation is connected; this runtime matrix remains pending. Do not
mark coverage R2.12 complete based on protocol tests or a successful build alone.

### Ventti NPC reaction verification (v100, unreleased)

`VenttiReactionTests` checks pose packet budgets/quantization, malformed data,
layout and sequence rejection, deferred/late-join poses, interpolation, cue
expiration/delays/overflow and sender/channel admission. `CatalogBindingTests`
checks pose hierarchy, native sound variation slots and the stable layout hash.
The optional `--include-transforms` asset extractor output records inactive NPC
bones and furniture alongside the FSM/ArrayList evidence; see `catalog/README.md`.

Two-player checks remain pending:

- Compare cash wins/losses, card gathering, table/chair throws, hand colliders,
  crawl/walk and smoking. Move the host away, change LOD, and late-join during a
  throw and after the NPC has left its original parent. Pose snapshots must show
  current state without replaying past speech or property outcomes.
- Compare the exact spoken variation, gathering sound, delayed loss line, range,
  volume and animation timing. Repeat several rounds; each cue plays at most once.
  Introduce brief packet delay/loss: reliable final poses must recover, stale
  poses must not roll state back, and expired sounds must stay silent.
- Temporarily name a missing native sound state in a local test catalog on both
  peers, or delay source availability. Verify diagnostics, continued betting/pose
  sync, and absence of a burst of old audio when sources bind. Restore the catalog.
- Disconnect during a reaction and repeat host/join cycles. Guest original poses,
  visibility, velocities, kinematic flags, animation and FSM actions must restore;
  temporary AudioSources and installed host hooks must be removed. Check that
  native table controls and single-player NPC behavior work afterwards.

Core compiles against the installed Unity/PlayMaker DLLs; static evidence and Net
tests do not verify Unity animation/physics execution order or native audio mixing.

### Ventti table observation verification (v98, unreleased)

`VenttiTableTests` checks exact wire bytes, stakes above 255 mk and fractional
stakes, all six results, explicit reset, invalid values, wrong table ids, stale
snapshots, sequence wrap and reconnect. The installed build 23268598 binds `Bet`
as a float, both `Hand` values as ints and `LoseText.Status` as a string. Catalog
validation also rejects ambiguous result mappings and mismatched property resolvers.

- Play on the host with a guest observing. Compare stakes above 255 mk, player/house
  totals, all cash/car/house result strings and the empty reset. Check variables in
  the diagnostic view: result text replication does not activate the guest's global
  interaction UI. Results can be announced before native settlement.
- Join while the guest's table has never activated, then open it. Confirm the latest
  state applies after discovery; an older join snapshot cannot replace newer live state.
- Repeatedly join/end a session in the same scene. Verify one intent per hooked
  button entry and that original guest stake, totals and result text are restored.
  In v99, legacy control replay is retired; the four native draw/bet FSMs are cut.
- Confirm the resolver stays disabled even after local table activation, and that
  receiving a result causes no wallet, property, stress, save-point or UI action.

Core builds against the installed game DLLs; the protocol and launcher test suites
run without the game. Two-player checks are still pending. The v99 host adapter
above implements settlement, leased controls and native card/result presentation.
Host NPC outcomes run; guest NPC reactions are not mirrored by this adapter and
remain follow-up work in R2.12.

### Debt-letter verification (v94)

`DebtPaymentTests` covers native float quotes, concurrent payments, exact receipts,
changed bills, insufficient funds and sequence resets. Static extraction of build
23268598 verifies the 15 calculation/request/payment actions and their typed variable,
object and text targets. The host reads fees from the inactive sheet and validates
payments against the envelope referenced by `Rent/Letter`, which survives eviction's
mailbox relocation. Bindings live in the catalog's `debtLetter` section.
Use the R1.6 two-player checklist in `COVERAGE-ROADMAP.md` to verify input timing,
camera/menu closure, cash/debt/envelope convergence and later singleplayer payments.
Static evidence and unit tests cannot verify those running-game transitions.


### Guest alternator adjustment (shipped in 0.1.33; introduced in protocol 119)

The VIN133 and ALTERNATOR0 replacement families now accept guest hand adjustment.
With empty hands and the adjusting bolt below tightness 8, aim at the pivot within
one metre and scroll. Each scroll event requests one native half-degree turn,
clamped to 0–7 degrees. The original guest HandRotate and pivot collider stay
disabled; geometric sphere picking reads the owned copy without enabling physics.
Its adjusting bolt must have a fresh host observation for the current attachment.
Right-click removal remains available when the picks overlap.

PartFitRequest/Receipt operations 2/3 reuse the immutable per-player ledger and
carry the observed revision, with slot zero. The host refuses changed/loose parts,
incorrect mount ownership, distant or stale player poses, a tightened adjusting
bolt, inactive native input, or a busy 0.1-second native cooldown. It executes the
validated Clockwise/Counterwise graph and accepts only when the saved scalar,
engine-mount scalar and pivot angle agree. State 185 publishes the absolute result
before the receipt. A lost reply cannot rotate again, and an in-flight removal or
installation prevents a new adjustment. Invalid native bindings disable only that
part's adjustment. This requires protocol 119 on all peers.

The opt-in probe imports actual HandRotate actions from the installed assets and
runs them on disposable part/mount/pivot objects. In an isolated copy/profile,
extract its input using the existing asset tool, then add
`--wintermp-part-adjustment-probe` alongside `--wintermp-guest-save-probe`:

```bash
python tools/extract_fsm_assets.py /path/to/My\ Winter\ Car \
  --asset sharedassets3.assets --match VIN133/Pivot --match ALTERNATOR0/Pivot \
  --out /path/to/isolated-game/part-adjustment-probe.json
```

The probe checks both native turn directions, zero/upper clamping, the part and
mount scalar writes, pivot pose, preserved hierarchy and rejection of changed
step/target/space/deferred-write bindings. It supplements these multiplayer checks:

1. With different host/guest saves, fit both alternator variants, loosen only the
   adjusting bolt, and scroll both directions. Both players must see the same angle;
   the host engine mount's SettingRotation must track its saved part.
2. Tighten the adjusting bolt fully while a guest request is in flight. An old
   request must not turn it. Loosen it and verify a fresh scroll works. Other bolts,
   a held item/tool, another closer pick and a camera inside the sphere must not
   accidentally select this adjustment.
3. Race host/guest scrolls and two guests, and delay or duplicate receipts. Each
   accepted request turns at most once; rejected or stale requests leave host state
   intact. Refit/remove while a scroll is pending and check the revision gate.
4. Join late, resync, disconnect/rejoin and reload disposable saves. Angles and
   pivots must converge; native guest HandRotate and saved originals must stay inert.

These controlled checks do not establish full guest engine simulation or verify
real mouse-wheel/tool interaction in a two-player session.

Validation on build 23268598: **1,036** protocol/catalog/policy tests, **18** launcher
tests and **46** isolated Unity/Wine checks pass (18 adjustment, 7 restoration,
21 save-library). Core, both Net targets and the probe build without warnings.
`build/part-adjustment-smoke/result.json` records the report and tested assembly,
catalog and imported-action hashes; the tested assemblies match the current build.

### Vehicle engine handoff and replay (protocol 126, unreleased)

Engine state 60 now follows the vehicle's physics owner, with host publication
allowed when the vehicle is unowned. Leaving the seat clears driver occupancy
immediately; active parked ignition keeps the simulator's ownership and stream.
A fresh nearby seated driver may take over from that non-driving simulator.
The final engine state and final pose use the same reliable channel, in that
order, and the host preserves that channel when relaying accepted engine state.
Stale or non-owner reports are rejected before relay. Per-sender sequence history
survives a handoff; live sequence generation skips the reserved snapshot value.

Late-join/resync engine state comes from the current driver's fresh accepted
state, including gear. A snapshot cannot overwrite a local driver's state, and
targeted vehicle pose repairs use the snapshot path rather than a release packet.
Native RPM takes precedence over a dashboard value that can remain stale after
power is lost. Gear binds before a cold snapshot applies; delayed native Power
bindings retry the accepted ON/OFF state without waiting for another packet.
Remote timeout also protects a seated player before their first ownership claim.
When a remote transform stream expires, the host cannot turn residual remote ACC
into a new host engine report.

Full guest engine operation, host wear while a guest drives and safe operational
part references remain unverified; this change supplies the state handoff
prerequisite and does not replay native wear/damage on guests.
The [corrected native engine audit](../build/vehicle-rpm-smoke/native-audit.json) confirms
Starter has enabled global RPM producers. Dashboard updates do not supply that native
simulation input, and writing global RPM directly would race Starter and affect
combustion, oil, cooling and failures. That requires a separate simulation adapter.

At protocol 126, **1,377 protocol/catalog/policy tests**, **18 launcher tests**
and **253 isolated game checks** pass. The native run adds **33 vehicle-state
checks** to the previous 220, with zero failures and Wine exit 0. Both Net targets,
Core (Debug/Release) and the probe build with **zero warnings/errors**.
The [native result and hash record](../build/vehicle-state-smoke/result.json)
documents the controlled Debug run; Release Core was built separately. Launcher
validation reported only the existing optional missing FastBoot Release payload
warning; no installer was built.

Add `--wintermp-vehicle-state-probe` to the isolated GuestSaveProbe run with all
prior probe flags. `VehicleStateChecks.cs` uses controlled Core fixtures and needs
no runtime input JSON. Archive the report, assembly/catalog hashes and validation
summary at `build/vehicle-state-smoke/result.json`; native RPM and wear audit
files in that directory describe remaining engine integration dependencies.
Use an isolated game copy and Wine prefix, with deployment disabled for builds.

Two-player acceptance still needs disposable saves and these checks:

| Action | Expected result |
|---|---|
| Host starts a car, parks and gets out with ignition on; repeat with the guest | Both continue seeing engine/electrics state, and the driver's seat becomes available immediately |
| The other player takes the empty driver's seat while the first player's ignition stream continues | The new seated driver wins; delayed updates from the previous simulator cannot change the new state |
| Stop the engine, turn ignition off, leave the seat and wait for rest | Engine/electrics turn off before the final pose releases ownership; delayed ON packets do not revive them |
| Join late or request resync while another player drives; repeat during takeover | Gear and engine state agree, the current driver is not overwritten, and pose repair does not release their ownership |
| Join before native gear/Power bindings are ready; receive ON or OFF, then let the vehicle finish loading | Gear applies when bound and Power retries the accepted state without another packet |
| Enter the driver's seat as an old remote stream expires | Timeout does not turn off the seated player's ignition, and the host does not echo leftover remote ACC |
| Disconnect and rejoin into a reused player slot, then drive again | Fresh sequences are accepted and previous-session state is not replayed |
| Refuel a parked unowned vehicle through a guest's station nozzle | The host's bounded fuel result reaches peers without stale cached fuel replacing it |

### Native guest engine write protection (local/unreleased, still protocol 126)

The `guestEngineProtection` catalog identifies 65 exact persistent scalar writes
across 11 native FSMs, plus the distributor's saved mesh rotation. Selected actions
are disabled, and selected instances already active in PlayMaker are finished;
changing Enabled alone does not retire an existing active update. Calculations,
fuel-level and battery consumption, and runtime scratch fields remain live.
PartFallings is paused separately because it writes saved bolt arrays and can
send BREAKOFF, which a float-only guard cannot prevent.

Protection follows the guest save latch and remains active after disconnect until
restart. Admission prepares protection immediately after setting that latch, so
already-active writers stop during the same call; a preparation failure retains
the latch. Exact signature failures defer saved-part isolation and remote ignition
wake while retaining successful guards. After binding repair, only the exact
blocked state entry completes; stale OnExit callbacks and events are not replayed.
Completion waits while the graph is inactive and retries after activation.
Malformed profile metadata records a
subsystem error without discarding the rest of the catalog. The [native writer
audit](../build/engine-write-audit/audit-summary.json) records covered graphs and
excluded adjacent systems. For this additional local fix, **1,428
protocol/catalog/policy tests**, **18 launcher tests** and **276 isolated game
checks** pass. This includes **51 protection catalog cases** and **23 new native
checks**, with all previous 253 checks retained, zero failures and Wine exit 0.
Both Net targets, Core (Debug/Release) and the probe build with zero warnings/errors.
The latest Launcher Debug and FastBoot Debug/Release builds are also clean after
staging the matching optional payload; no installer was built. The 253 checks
above describe the preceding engine handoff validation.

For the additional isolated fixture, add `--wintermp-guest-engine-probe` with all
prior flags. Copy `build/engine-write-audit/native-engine-probe.json` into the
isolated game's root as `guest-engine-probe.json`. `GuestEngineChecks.cs` imports
the installed native actions and exercises active-action retirement, state entry,
targeted write blocking, destructive array/fall protection and binding recovery.
The [final native result and tested hashes](../build/guest-engine-smoke/result.json)
and [native report](../build/guest-engine-smoke/report.txt) cover the Debug game run;
Release Core was built separately. These controlled checks do not replace an
interactive two-player session.

Real two-player checks must use different disposable saves: start/crank the car,
trigger low oil/overheating and distributor movement, then disconnect and verify
the guest's saved scalars, mesh pose and bolt arrays remain unchanged. Repeat with
late-loaded graphs and a deliberately mismatched copied catalog signature; native
isolation/ignition must defer without disabling unrelated sync. Repair the copied
signature while its graph is inactive, then activate it: the blocked entry must
recover once without replaying old events or changing preserved data. Full guest engine
operation and host wear under guest driving still require separate acceptance.

### Native Corris RPM source (local/unreleased, still protocol 126)

Corris has no local `FuelLine.Revs` or `RotateEngine.Revs` variable. The optional
`vehicleEngineRpm` profile instead validates eight enabled Starter outputs against
the registered Corris root's native CarDrivetrain and global RPM. Four GetProperty
actions sample drivetrain rpm, three SetFloatValue actions publish StarterSpeed
while cranking, and Wait has an enabled zero reset. The [corrected native audit](../build/vehicle-rpm-smoke/native-audit.json)
confirms Wait for start#4/#5 and Wait#20 are disabled: there is no stopped
per-frame zero writer. These actions are excluded from the producer list.

The adapter reads that output without invoking a property action or writing
simulation values. Disabled, inactive or unstarted sources read zero. Changed,
missing or destroyed references retry; foreign drivetrain components and local
RPM shadows fail validation. Dashboard writes cannot alias the native output,
including after binding failure. Active ACC retains local ownership while native
RPM discovery waits. The existing state 60 layout and protocol 126 are unchanged.

All **1,459 protocol/catalog/policy tests**, **18 launcher tests** and **301
isolated game checks** pass, including **31 new RPM catalog cases** and **25 new
native RPM checks** alongside all 276 prior checks. The native run has zero
failures and Wine exit 0. Both Net targets, Core (Debug/Release) and the probe
build with zero warnings/errors. Add `--wintermp-vehicle-rpm-probe`
alongside all previous flags, with the imported Starter fixture at the isolated
game root as `vehicle-rpm-probe.json`. Archive the combined report and tested
assembly/catalog hashes under `build/vehicle-rpm-smoke/result.json`. The [final
native result and tested hashes](../build/vehicle-rpm-smoke/result.json) and
[report](../build/vehicle-rpm-smoke/report.txt) cover the controlled Debug run;
Release Core was built separately. Launcher Debug also rebuilt cleanly with the
current payload; no installer was built. Keep builds undeployed and use an
isolated game copy and Wine prefix.

Actual two-player acceptance must cover cranking, running, stopping, parked ACC
handoff and reconnect. Check that native cranking RPM reaches the observer and
that a stale dashboard cannot keep a stopped engine reported as running. Repeat
with delayed producer binding: ACC ownership must remain until the source loads.
This change does not establish guest engine operation or host wear while guests
drive. Isolated native mounts still retain the guest's ActivePart, Installed and
scalar values; host replacement state lacks required engine inputs such as
InertiaFactor, ValveTolerance and rocker Bolted. Safe runtime reference projection
must address those dependencies without modifying preserved guest originals.

### Guest distributor engine inputs (protocol 127, unreleased)

`guestEngineInputs` selects four native Cylinders readers for Installed, Wear,
Tightness and SparkAngle. Each uses an owned target wrapper to separate inert Data.
The host replica must have its latest accepted revision applied at the unique
matching attachment before Installed becomes true. Tightness respects later bolt
receipts. Pending, loose, retired, conflicting and unavailable copies stay absent.
Factory/consumer references identify the live mount even after the block moves.

Preparation precedes admission resume, saved-part isolation and ignition wake.
Signature errors pause the affected consumer through existing write-protection
recovery. Proxy rebuild replaces the entire GameObject because native readers cache
Data by target object. Cleanup restores original reader wrappers after saved parts;
failed restoration retains the proxy and consumer pause, including repeated cleanup.

Add `--wintermp-guest-engine-input-probe` to the existing native probe flags, with
`guest-engine-input-probe.json` in the isolated game root. The fixture warms real
native caches against a different saved distributor, then exercises the production
projection and native ignition/timing/damage branches. It models applied replica
readiness; full live-world clone, transport and two-player acceptance are separate.
The older writer-only fixture scopes out input readers to retain its independent
write-protection coverage. Build with `DeployToGame=false` and use only the
disposable game copy and Wine profile.

Two-player acceptance needs different distributor timing/condition on both saves,
host adjustment and bolt updates while a guest drives, removal/refitting, late join
and delayed attachment, repeated ignition attempts, disconnect/rejoin, and saved
original verification after restart. Running Cylinders reads new input on its
normal roughly one-second cycle. A failed start needs normal ignition restart once
the replica is ready; never overwrite shared scratch values or inject state reentry.
Other engine references and host wear under guest driving remain open.

Validation: **1,505 protocol/catalog/policy tests**, **18 launcher tests**, and
**334/334 isolated native checks**, preserving all 301 previous checks and adding
33 input checks. The catalog adds 46 validation cases. Both Net targets, Core
(Debug/Release), the probe and Launcher Debug build with zero warnings/errors.
The [final native result and tested hashes](../build/guest-engine-input-smoke/result.json)
and [report](../build/guest-engine-input-smoke/report.txt) cover the controlled Debug
run, with zero failures and Wine exit 0. Release Core was built separately. No
installer or release was made.

### Spark-plug wrench controls (protocol 145, unreleased)

Add `--wintermp-sparkplug-tool-probe` and `sparkplug-tool-probe.json`, keeping
the fitting fixture and all previous flags. The tool JSON contains native
PLAYER/…/2Spanner/Raycast Check and Raycast FSMs. The probe reuses the fitting
fixture's native plug, four sockets and array, and adds a controlled host
session, parent part identity, guest state and selected native tool actions.

Checks cover acknowledged turns and duplicate receipts in each socket, host
repair-mode independence, stale scratch/revisions, distant players, limits,
ratchet cooldown, conflicting slots/occupants, and saved socket identity.
Native tool recognition runs through the size gate, including tolerance edges.
Wrench and ratchet sends reach a safe guest graph that creates a pending request
without changing Data/mount tightness. Pending state, leaving repair mode,
failure and loose presentation gate the trigger; cleanup removes the name hook.
Changed native pose/turn bindings are refused. The original loose collider
setting is preserved before presentation state is captured.

Only selected native actions run. Tool wait/audio/material and ratchet-selection
states remain fixture boundaries; directional sends and size/name gates execute.
The fixture does not exercise mouse raycasting, physical tool selection, the
complete fitting/removal Rigidbody lifecycle or live P2P traffic. Guest state
is applied to controlled objects. The runtime uses Debug binaries; Release Core
is built separately. All 1,866 Net tests, 18 launcher tests and 1,032 native checks pass (21 Net and
31 native checks added; all 1,001 previous native checks retained).
Evidence and hashes: `build/sparkplug-tool-smoke/`.

### Spark-plug engine inputs (protocol 146, unreleased)

Add `--wintermp-sparkplug-engine-input-probe` and
`sparkplug-engine-input-probe.json` to the isolated run. Keep all previous
fixtures, including the rocker, bearing and piston templates: Cylinders now has
24 independent sources. Every earlier engine-input fixture also needs the new
spark-plug template file. It combines the full native Cylinders asset with
SPRKPLUG0 Data; source hashes and output are retained under
`build/sparkplug-engine-input-smoke/`. Run input fixtures after the base
guest-engine write-protection fixture, which temporarily substitutes a profile
that intentionally omits all readers.

`GuestEngineInputChecks.Sparkplugs.cs` checks sixteen native reads, four slot
proxies, cache replacement, pending/applied state, scalar order, conflicting
attachments, corrupted native arrays, changed readers, disconnect and recovery.
Native Reset/Cylinder/Add to power actions execute firing and efficiency math,
the Wear <1 firing boundary, Wear <10 and Tightness <8 misfire eligibility.
Random-event states are fixture boundaries; native Misfire consequences execute
through explicit entry. The test does not synchronize or claim deterministic
random outcomes. Plug-wear arithmetic uses the host Durability while both native
saved-wear writes remain disabled. Retained saved Data/ActivePart and scratch
outputs are checked throughout.

The fixture supplies controlled applied replicas and prerequisites. Full native
materialization/physics remains covered separately by the lifecycle probe.
Physical tool selection, full downstream engine operation, host wear during
guest driving and live P2P still need acceptance. Native runs use Debug; Release
Core is built separately. No installed game files or personal saves are modified.
All 1,147 native checks, 1,895 protocol tests and 18 launcher tests pass; 71 native
and 29 catalog/protocol cases are new, with all 1,076 earlier native checks
preserved. Core Debug/Release, both Net targets, probe and Launcher build cleanly.

### Spark-plug full physics lifecycle (protocol 145 unchanged, unreleased)

Add `--wintermp-sparkplug-lifecycle-probe` and `sparkplug-lifecycle-probe.json`
to the isolated run, retaining earlier fixtures and flags. The new input combines
the full native plug Data/Screw/Wear, four socket Data graphs, Installer and
Functions FSMs with the actual Sparkplugs array from `build/sparkplug-audit/`.
The native importer binds typed collider/body properties and the shared mass and
velocity variables to disposable fixture objects. It refuses unsupported property
types or nonliteral property values. No personal saves or installed game files
are used as write targets.

`SparkplugFittingChecks.Lifecycle.cs` runs after the synchronous suite, using real
Unity frames for DestroyComponent/NextFrameEvent and the Screw startup delay.
Each socket executes native fitting, wear propagation, tightening, loosening and
removal. Checks cover pending/accepted retries, mass balance, collider layer/tag,
world pose, inherited car velocity, Continuous collision mode, persistent ID and
fresh ownership after Rigidbody recreation. Host snapshots then pass through
`OnReplacementPartState` and `ProcessReplacementParts`, including actual prefab
cloning and native identity initialization, in `.ReplicaLifecycle.cs`. The guest
keeps one object/body across all cycles, restores loose interaction and rejects
delayed fitted snapshots without running native mount mass/installation writers.

Functions sound/UI states are explicit fixture boundaries. The test supplies
controlled socket transforms, ownership and requests; it does not exercise mouse
raycasting, physical tool selection, live P2P or cylinder firing/misfires. It adds
44 native checks to the previous 1,032; all 1,076 native checks and 1,866 protocol
tests pass. The opt-in probe is never shipped.
Evidence, fixture, full output and tested hashes: `build/sparkplug-lifecycle-smoke/`.
Core Debug and the probe build cleanly; protocol/runtime behavior is unchanged.

### Spark-plug socket selection and removal readiness (protocol 144, unreleased)

Add `--wintermp-sparkplug-fitting-probe` with `sparkplug-fitting-probe.json`,
retaining the earlier flags and fixtures. The JSON combines native SPRKPLUG0
Data/Screw/Wear, four VIN1110 spark-plug mount Data FSMs, the generic Installer
and the actual Sparkplugs array. Sources are in `build/sparkplug-audit/`.

The 25 new native checks cover all four reversed socket addresses, native entry
validation, occupied/inactive/duplicate socket handling, layer-12 removal picks,
and loose-versus-tight host readiness. Native Screw.Set changes the part layer
and pose; native TIGHTEN/UNTIGHTEN update Data and its mount through BOLTING.
Changed pick masks, wrong current layer, disabled/solid collider, mismatched
parent or occupant, empty mount and guest replicas cannot authorize removal.
A native Remove dispatch traverses the mount's blocking assembly prerequisite.

This fixture creates controlled fitted objects and imports selected native
actions. It does not execute the complete install/remove Rigidbody lifecycle,
guest tool input, cylinder firing or a networked two-player session. Unloaded
states remain fixture boundaries. The generic installer and native mount entry
bindings are validated; candidate selection runs against the real array proxy.
The array component must be awake before populating its runtime list.

All 1,845 Net tests, 18 launcher tests and 1,001 native checks pass (11 Net and
25 native checks added; all 976 previous native checks retained). Core
Debug/Release, both Net targets, the probe and Launcher build without warnings.
Results, logs and tested payload hashes: `build/sparkplug-fitting-smoke/`.
The isolated runtime uses Debug binaries; Release Core is built separately.

### Spark-plug boxes and outputs (protocol 143, unreleased)

Add `--wintermp-sparkplug-opening-probe` and `sparkplug-opening-probe.json`,
retaining every earlier flag/fixture. The JSON combines the installed Sparkplugs
and Sparkplug factory FSMs, sparkplugbox0::Use and SPRKPLUG0 Data/Screw/Wear.
The probe imports and serializes native prefab actions before cloning them;
Unity copies serialized ActionData rather than the runtime Actions array.
Only selected native actions run. Unloaded waiting/ID-iteration boundaries are
terminal, and box audio is excluded. Native save readers point at a disposable
nonexistent probe file; guest creation replaces Exists/Load before startup.

The 18 new checks cover fixed capacity without QuantityMax, exact direct spawn
bindings, rejection of changed reader/timing/capacity metadata, fresh box identity,
four real host-request openings, three duplicate requests per opening, empty-box
rejection, native saved plug creation, guest box/part materialization, independent
host condition and repeated state receipts. Native opening decrements once and
assigns Owner to Sparkplug.SpawnPoint before dispatch. Plug Init builds stable
save keys and native Wear in [90,99], with default Durability .75. Guest clones
keep host values instead of their own randomized initialization.

A test found that a newly created Empty replica could receive a newer quantity
before Unity called Start, then clear it during delayed startup. The materializer
now starts the guarded initial state synchronously, matching replacement-part
creation. The regression applies a newer quantity immediately and verifies that
guest opening/save/deletion entries cannot decrement it or spawn another plug.
The earlier box original retains its quantity throughout guest projection.

All 1,834 Net tests, 18 launcher tests and 976 native checks pass (all 958 earlier native checks
preserved). [Results and hashes](../build/sparkplug-opening-smoke/result.json)
and [native report](../build/sparkplug-opening-smoke/report.txt) record the Debug
payload. Net, Core Debug/Release, probe and launcher builds have zero warnings/errors.
Initial focused runs exposed fixture boundary/serialization omissions
and the actual box startup race; the final complete run has zero failures.
Native fitting/removal and Screw controls are outside this fixture (its imported
Data does not hydrate the Collider object reference or run those states).
Cylinder inputs and real two-player operation still require acceptance. Keep
`-p:DeployToGame=false`; no installer or release is produced by this probe.

### Guest starter battery draw (protocol 178, unreleased)

Guest cranking now reports native battery-draw operations to the host. The retained
Corris Starter audit identifies four loaded AddFsmFloat actions (Turn key #6,
Fuel Mixture #8, Start or not #7, Start engine #9) and one unloaded action
(No Flywheel #5). Each runs on entry and every-frame update, with perSecond=false;
the game defaults are -0.0094 and -0.0023 charge per callback. These are native
per-callback amounts, not invented per-second rates.

The five existing protection descriptors require `starterDraw: loaded/unloaded`.
Only on registered guest cars with a complete battery profile do those actions
stay enabled as observers: the patched native helper always skips their saved
write. Exact native target, local amount identity, finite nonpositive amount and
cadence are checked. Changed/stale callbacks pause that graph, and old callback
identities remain blocked until their FSM is destroyed. Other selected writes
stay disabled; host/partial-profile protected fixtures retain their old guards.

Only a connected guest owning the car reports counts. Reliable batches flush at
0.1 seconds, at the 512-count boundary, on load-kind change and before final
vehicle state/ownership release. Session clear drops pending work, and loss of
ownership discards unsent work. Request 198 has vehicle/player/sequence/kind/count
and is 12 bytes with ID. It carries no client charge or rate. Both peers require
protocol 178; existing state layouts and mod 0.1.33 stay unchanged. Next ID is 199.

The host authenticates the sender and current owner, rejects local-driver
conflicts and duplicate/stale ushort sequences, and bounds work with a 512-count
burst replenished at 2,048 counts/second. This protects against excessive callback
reports; unusually high native frame rates can exceed that bound. Accepted
sequence numbers are consumed even when native validation rejects the work.
Requests never become a backlog to replay after a battery/wiring repair.

Native application requires the settled installed host battery, enabled/started
native Starter, installed starter, starter/harness bolts, ground installation,
matching host flywheel mode, a validated battery destination and amount/cadence.
An already-cranking host does not also apply guest draw. The host runs only its
native AddFsmFloat helper once per accepted count using its own rate. Normal
BatteryState publication carries the result; no request is relayed or snapshotted.

Validation passes **3,182 protocol/catalog tests** (28 new), **18 launcher tests**,
and **3,055 native checks** (39 new; all 3,016 previous checks retained). Both Net
targets, Core Debug/Release, probe and launcher build without warnings or errors.
Native testing uses Debug; Release is build-only. Matching local launcher and
disposable payload hashes, native fixture, logs and results are retained in
`build/guest-starter-draw-smoke/`.

Native checks compare all five solo entry/update writers with protected guest
counts and host application using deliberately different host rates. They cover
kind ordering, maximum/periodic/final flushing, ownership/session cleanup,
disconnected observers, changed signature containment/repair, host replay rejection,
battery removal/inactivity, missing starter/wiring/flywheel, a native host crank
and changed destination/cadence. The first run lacked required battery fixture
states. The second passed the new checks but exposed observation activating in
protected host RPM fixtures; restricting observation to guests with a complete
battery profile restored every prior check in the final run.

This is an isolated native callback/transport capture test, not a live two-player
drive. Other accessory demand, starter wear, physical battery replication and full
handoff/electrical acceptance remain open. Live acceptance should compare host
charge during guest starts, key release, failed/no-flywheel cranking and ownership
handoff, then verify the guest save remains unchanged after disconnect/restart.
Changes remain local and unreleased; no personal saves or installed plugins were used.

### Guest accessory battery inputs (protocol 177, unreleased)

Native accessory readers now use host Installed, Charge and ChargeMax when their
resolved source is the protected saved battery Data. The retained Corris audit
contains 22 such reads across eight FSMs: interior lights, current/power, radiator
fan, main Electrics and wiring. Nineteen extend beyond the three existing engine
proxy reads. One additional control reads PowerON's own Amps FSM: its UseOwner
setting ignores the serialized battery reference, and it remains native.

BatteryState 194 appends mount ChargeMax after Charge, becoming 15 bytes including
ID. Both must be finite and zero unless installed; missing/invalid maximum makes
the host source unavailable. Maximum-only changes advance revisions and survive
snapshots. The catalog requires exactly Installed/Charge/ChargeMax in
`guestEngineInputs.battery.readVariables`, plus the existing write protection.
Both peers need protocol 177; mod version stays 0.1.33 and next free ID stays 198.

Native private GetFsmFloat/GetFsmBool boundaries supply only consumer-local outputs,
without writing saved source fields or globals. Unseeded, unavailable and removed
host batteries read false/zero. Cache updates preserve the native same-object FSM
name behavior and first-FSM fallback. Other scalars/sources remain native. Known
source identities survive movement and catalog loss; new sources bind at the read
boundary. Destroyed sources retire. Host, solo and disconnected reads remain native;
session clear removes host inputs while saved-write protection remains latched.
Unsafe saved/global output aliases log and skip the read.

Validation passes **3,154 protocol/catalog tests** (20 new), **18 launcher tests**,
and **3,016 native checks** (71 new; all 2,945 previous labels/counts retained).
Core Debug/Release, both Net targets, probe and launcher build without warnings or
errors. Native checks use Debug; Release is build-only. Local launcher and disposable
game payload hashes match. Evidence is in `build/battery-accessory-inputs-smoke/`.

The fixture imports each audited native getter at an unrelated consumer path and
checks native entry/update, host changes/removal, stale/conflicting records,
unseeded/rejoined sessions, caches/fallback, late sources, output alias rejection,
source movement and unchanged saved fields. Native light/fan FloatCompare actions
and current-meter FloatClamp plus its protected write also run. Host capture checks
use different mount/part maxima, maximum-only revisions and invalid/missing recovery.
The first run exposed two fixture assumptions: the UseOwner current-meter source
and a pre-admission cache warm-up performed with guest protection already active.
Correcting those fixtures produced a clean complete rerun; both outputs are retained.

These are isolated native action checks, not full accessory operation or a live
two-player test. Guest starter/accessory demand reaching the host, physical battery
replication and live electrical acceptance remain open. Join with differing saved
batteries, compare lights/fan/power behavior against host charge and removal, then
disconnect/rejoin and verify the guest save stayed unchanged. This change remains
local and unreleased; no installed-game deployment or personal saves were used.

### Guest battery external writes (protocol 176, unreleased)

Pausing the saved battery Data FSM and selecting ten Starter/Electrics writes did
not cover external consumers. The retained native Corris audit identifies **31**
SetFsmFloat/AddFsmFloat/SubtractFsmFloat writes to VINP_Battery::Data: ten already
selected and **21 additional** writes from radio/CD, interior lights, wipers,
current-meter charge clamping, headlights/markers/rear lights, heater blower,
turn signals, radiator fan, electrical sparks and the two battery terminal bolts.

The paused battery catalog rule now requires `blockExternalFloatWrites: true`.
Missing/disabled protection rejects battery input metadata and guest admission.
Three native private write boundaries check the actual destination, using native
GetOwnerDefaultTarget/GetGameObjectFsm behavior and the writer's goLastFrame/fsm
cache. Native AddFsmFloat misleadingly calls its helper DoSubtractFsmFloat; all
three signatures and cache fields are verified before admission. An unresolved
callback destination logs and skips that write.

All external native float writes to the protected battery Data are skipped,
including callbacks already active on admission, new or moved radio consumers,
owner-default targets, and a newly discovered battery before a scan or valid pause
binding. One-shot OnEnter still finishes normally; ongoing consumer actions and
unrelated calculations continue. Other FSMs on the same object and other objects
remain writable, including native same-object cache behavior when fsmName changes.
Remembered destination identities survive rename/reparent, catalog loss and a
latched protected disconnect. Destroyed identities are retired. Solo/unprotected
callbacks remain native. This guards the saved mount; it does not add host load
intents, repair physical battery replicas, or synchronize starter/accessory demand.
No wire layouts or IDs change; protocol is 176 and mod version stays 0.1.33.

Validation passes **3,134 protocol/catalog tests** (nine new), **18 launcher tests**,
and **2,945 native checks** (83 new; all 2,862 previous labels/counts retained).
Core Debug/Release, both Net targets, launcher and probe build with no warnings or
errors; native validation uses Debug, with Release build-only. Local launcher and
disposable game payloads match. Results, hashes, fixture and writer inventory live
in `build/battery-external-writes-smoke/`.

Each of the 31 imported native writers first changes a controlled battery scalar
without protection, then preserves it during protected native entry/update while
an independent native arithmetic sibling continues. Sources run at unrelated
fixture paths to exercise destination recognition, with controlled write amounts.
Lifecycle cases cover caches, target changes, first-FSM fallback, null targets,
consumer/target movement, metadata loss, disconnect, late targets and owner-default
resolution. Other native consumer actions and full electrical operation are not
run by this fixture. An initial nullable probe warning was corrected before native
runs. The first native run passed all 83 new checks but one older file-rename check
hit a destination retained from the previous test directory. Its full output is
preserved; a fresh disposable directory passed every check with identical binaries.
Always move/preserve the entire previous `guest-save-probe/` directory before a
rerun, so the next run starts with that directory absent.

Two-player acceptance: join with a different charged battery in the guest save;
operate lights, wipers, heater, radio, fan and battery terminals on the shared car.
Compare the guest's saved charge/charge maximum/degradation/terminal fields after
disconnect and restart. Verify consumer controls still operate and compare host
battery drain while either player drives. Host demand replication, full electrical
operation and live save-preservation acceptance remain open. No commit, push,
installer, installed-game deployment or personal-save writes were performed.

### Host electrical RPM inputs (protocol 175, unreleased)

All three native Corris Electrics RPM readers now use accepted guest-driver RPM on
the host: Engine running? #0 (>400 charging gate), Charge battery #1
(RPM / AlternatorEfficiency), and Run on battery #0 (RPM /60000 followed by the
native 0.00001–1 clamp). The third read was found while tracing the complete
charging fallback, extending the earlier two-reader charging audit. Message 60
remains 25 bytes including ID; no field or ID is added. Both peers require protocol
175; the local mod version remains 0.1.33.

The independent `vehicleElectrical` catalog group validates source identity,
absence of local RPM shadowing, unique FSMs/states, action arrays/cadence, running
threshold/events, native charge/drain operands/outputs and selected transitions.
Hosts bind during vehicle updates before the first report and again on accepted
state receipt. Native scoped reads use only a copied, valid, fresh sample from the
current delegated owner. Missing/stale/mismatched/snapshot/invalid samples supply
zero RPM through native calculations; optional torque/movement absence does not
suppress valid RPM. Local driving, guest mode, save protection and disconnected
sessions retain native inputs. Driver departure clears the sample. Changed graphs
retire/log/retry, and cleanup removes readers. The hooks restore operands on nesting
and exceptions; no global RPM write or additional simulation tick is introduced.

Native host battery/alternator installation, fanbelt/wiring checks, damage/condition,
engine heat, voltage thresholds, charging clamps and battery/alternator writes stay
active. Native Delay applies rates at its ordinary cadence; the previous calculated
rate can persist until the next native state cycle. Zero RPM retains vanilla's small
minimum battery drain. Full electrical loads, starter draw and physical/thermal
handoff are not completed by this change.

Validation passes **3,125 protocol/catalog tests** (24 new), **18 launcher tests**,
and **2,862 native checks** (111 new; all 2,751 prior labels/counts retained).
Core Debug/Release, both Net targets, probe and launcher build without warnings or
errors. Native validation is Debug; Release is build-only. Launcher and disposable
game payloads match. The first native run passed 2,861 checks; review then added
early binding and its regression, producing the final 2,862. The initial probe
compile caught a wrong optional-torque property name; it was corrected before any
native run. Results and hashes live in `build/host-electrical-rpm-smoke/`; complete
prior output and the first native pass/binaries are retained separately.

The fixture reuses the complete native Electrics/Consumption extraction and adds
12 native charging-path states, including actual GetFsmFloat/Bool, branching,
charge/voltage calculations, alternator wear, battery ChargeMax writes and Delay's
per-second Charge additions/subtractions. Part, battery and amps source FSMs are
controlled fixtures. Lights and starter writes are inert, and Delay's outgoing
transition is held to inspect one native cycle. Eighty-four RPM/part-condition/
temperature combinations compare exact native host and delegated-driver results,
including 399/400/401 RPM and failed alternator/fanbelt/wiring/regulator cases.
Additional checks cover actual charge/drain/wear writes at three alternator wear
values, early binding, copied/stale/invalid reports, optional telemetry absence,
driver changes/departure, native authority, save-protected hook abstention, nested
exceptions, 16 changed bindings, mutation detected during native dispatch and
cleanup/rebinding. Every previous native regression remains green.

Two-player acceptance: have the guest start and drive the host Corris while the
host observes battery charge, including cold/warm starts, low RPM and failed or
missing alternator/fanbelt/wiring. Compare charge/drain with the same host-driven
conditions, vary electrical loads, stop/swap drivers and disconnect/rejoin. Preserve
the guest save and verify host charge survives save/reload. Electrical loads and
starter current, remaining thermal/failure consumers and full two-player operation
still need acceptance. Changes remain local/unreleased; no commit, push, installer,
installed-game deployment or personal-save write was performed.

### Guest electrical temperature (protocol 174, unreleased)

Three Corris electrical reads now consume host EngineTemp on connected guests:
main Electrics/Battery #1 and InteriorLight/Electrics/Consumption/Battery #1
(SetFloatValue into BatteryTemp), plus main Electrics/Charge battery #2
(EngineTemp +51 into Math1). Native cold penalties, voltage decisions and charging
limits remain active. State 197 remains 19 bytes; no new field or ID is allocated.
Both peers require protocol 174; the local mod version remains 0.1.33.

The two battery states retain the native -20 to -0.1°C clamp, addition to local
Charge, and VoltageLimit decision. The charging state retains +51, /570 and the
0.0001–0.55 limit, with native RPM/alternator calculations. This supplies guest
thermal inputs; it does not establish host charging from guest-driver RPM or
change persistent battery authority. The existing host BatteryState continues to
supply charge. Neither projection writes saved charge or native EngineTemp.

The required `electricalInputs` catalog group binds three reads independently of
fuel/oil and cabin groups. It validates source/global identity, local shadow
rejection, paths, unique FSMs/states, action arrays, cadence, output variables,
cold-penalty arithmetic, voltage events/transitions and charging limit arithmetic.
Changed bindings retire the group and retry. Shared engine-temperature hook code
now handles native SetFloatValue OnEnter and OnUpdate, preserving separate reader
state and restoring operands after nesting or exceptions. Missing/unavailable
host state uses zero degrees through native calculations. Driving, handoff and
telemetry expiry do not reset shared heat. Host/disconnected sessions retain native
reads; save protection remains active and no additional native tick is introduced.

Validation passes **3,101 protocol/catalog tests** (27 new), **18 launcher tests**,
and **2,751 native checks** (89 new; all 2,662 previous labels/counts retained).
Core Debug/Release, both Net targets, probe and launcher build with zero warnings
and errors. The native run is Debug; Release is build-only. Launcher/disposable
payload hashes match. Evidence is retained in `build/host-electrical-temperature-smoke/`,
including the fixture, selected native-read audit, results and the complete
preceding `previous-cabin-temperature-output/` directory.

The fixture executes real native battery reads, assignment/clamp/add/comparison
and charging arithmetic using controlled charge and amps source FSMs. Fifty-eight
temperature/charge/RPM combinations cover cold-clamp boundaries, very cold and hot
heat, stopped/low/high RPM; nine additional voltage-limit boundary combinations
compare exact native host results and guest results with different local heat.
It also covers repeated battery reads without stored-charge accumulation, copied/
stale/conflicting/foreign states, availability, late discovery, ownership/expiry,
disconnect/reconnect, protected native helpers, nested exceptions, 19 changed
bindings, detection during native dispatch and cleanup. Native electrical lights,
installation/wiring gates, persistent battery writers and downstream states are
inert in this fixture; full electrical operation is not claimed. All previous
native save-protection, fuel/oil, cooling and cabin checks remain green.

Two-player acceptance: use a cold host Corris with a charged battery and a guest's
own car saved warm, then reverse the temperature difference. Compare ignition and
interior-light availability near low charge, start/warm the engine, vary electrical
loads, swap drivers and rejoin. Preserve the guest save and compare host charge
before/after driving. Protocol 175 adds guest-RPM inputs to all three native host
Electrics readers, including battery drain. Remaining cooling/failure/backfire and
auxiliary gauge thermal consumers, full engine operation and live two-player
acceptance remain open. Changes are local/unreleased; no installer, deployment,
commit, push or personal-save writes were performed.

### Guest cabin and heater temperature (protocol 173, unreleased)

The Corris now consumes the host's engine temperature at
CarTempCorris/Data/Data #4 (EngineTemp /5 → MaxTemp), retaining the following native
InteriorTemp clamp. HeaterUnit/Function/Calc defrosting #0 reads the host's coolant
degrees into its local scratch CoolantTemp before native heater arithmetic.
Subsequent settings, direction, blower efficiency, heating/defrosting rates and
coolant division remain native. The physical Cooling source and global EngineTemp
are never written by these projections. State 197 remains 19 bytes; no new field
or ID is allocated. Both peers must use protocol 173.

The required catalog `cabinInputs` group identifies both readers. Binding validates
source and output identities, paths/ancestry, unique FSMs/states, action arrays,
read target, action type/cadence, cabin arithmetic and clamp wiring. Changed
bindings retire both readers, log and retry; this group is independent of the
fuel/oil and gauge bindings. Only connected guests project heat, including local
drivers. Missing/unavailable host heat supplies zero degrees to native calculations.
Driver changes and engine telemetry expiry retain accepted heat; join/resync uses
the existing host thermal state. Cleanup removes readers and reconnect binds anew.
The scoped cabin operand restores on nested dispatch and exceptions. Guest save
protection remains active and host/disconnected reads remain native.

Validation passes **3,074 protocol/catalog tests** (25 new), **18 launcher tests**,
and **2,662 native checks** (66 new, every previous 2,596 label/count retained).
Both Net targets, Core Debug/Release, the native probe and launcher build without
warnings or errors. The native run is Debug; Release validation is build-only.
The launcher and disposable-game payloads match. Results, fixture and hashes are
retained in `build/host-cabin-heat-smoke/`; `previous-engine-temperature-output/`
preserves the entire preceding native output. No installed-game deployment,
personal-save write, commit, push, installer or release was performed.

The fixture uses the extracted native Cabin Data, Heater Function and Cooling
FSM definitions from the retained 511-FSM installed-asset audit. It executes real
selected native arithmetic, cabin cooling/clamping and the heater's GetFsmFloat.
Forty temperature/control/door-rate combinations compare native host results with
guest results using different local heat. Further cases cover independent engine
and coolant degrees, repeated coolant read/scaling, availability, replay/copy
isolation, late discovery, driver changes, disconnect/reconnect, protected native
helper reads, nested exception restoration, 15 changed bindings and cleanup.
External roof-temperature reads, heat-source/defrosting writes, waits and downstream
state effects are inert. It therefore verifies the thermal inputs and rates, not
complete cabin warming, windshield clearing, heater installation/electrical gates,
or a live two-player session. Existing climate authority is unchanged; simultaneous
occupant reports and physical warmth/frost convergence still require acceptance.

Two-player acceptance: with a guest driving and their own saved Corris at a
different temperature, compare cold-start and warmed-up heater behavior, vary
blower/temperature/direction, open/close doors, swap drivers and rejoin after
parking. Verify cabin warmth and window clearing on both machines and preserve
the guest save. Remaining cooling/electrical/failure/backfire consumers, full
thermal handoff and live engine operation stay open. Local version remains 0.1.33.

### Guest engine temperature inputs (protocol 172, unreleased)

State 197 appends native `EngineTemp` to the coolant snapshot, preserving the
existing 15-byte prefix and expanding the message to 19 bytes including ID. Both
values must be finite, both are zero when unavailable, and a change in either
advances the vehicle/session revision. Capture uses the host's global EngineTemp
and the validated Cooling.CoolantTemp source after native readiness. A local
EngineTemp shadow, missing/replaced global or nonfinite reading invalidates capture.

The host temperature catalog now declares the complete fuel/mixture/oil/pressure
input group. On connected guests, six scoped native operands consume host heat:

- FuelLine/Carburator #3: FuelChamber clamp minimum.
- FuelLine/Priming #0 and #1: >3°C bypass and FuelChamber clamp minimum.
- Mixture/Calculate density #3: EngineTemp +50 before native density calculation.
- Oil/Viscosity #0: EngineTemp +50 before native oil friction calculation.
- Pressure/Oil pressure #0: ModifierTemp minus EngineTemp; guest RPM is retained.

The six bindings validate as one group, including source identity, local-shadow
rejection, selected action types/references, arithmetic/output, every-frame flags,
priming events and destinations. Changed bindings retire the group and retry.
Nested native reads and exceptions restore each original operand at the correct
call depth. No packet writes native EngineTemp, coolant, RPM or saved part values;
no extra simulation tick is introduced. Missing/unavailable host state supplies
zero degrees through the same native calculation. Driver ownership and vehicle
telemetry expiry do not reset thermal authority. Host/disconnected sessions keep
native inputs; existing guest save/damage guards remain intact.

The installed-asset audit includes 511 Corris FSMs and records all 30 direct
EngineTemp parameter references. Six are selected here; the other 24 include both
remaining consumers and native thermal writers. This does not claim complete
thermal synchronization. In particular, Cooling thermostat/thermal calculations,
heater/cabin behavior, electrical-temperature behavior, backfire/failure inputs,
and remaining heat writers still require audit/integration and acceptance.

Validation passes **3,049 protocol/catalog tests**, **18 launcher tests**, and
**2,596 native checks** (55 new; all 2,541 previous labels/counts retained).
Core Debug/Release, both Net targets, probe and launcher build cleanly. The native
run is Debug; Release is build-only. Matching launcher/disposable-game payloads
and hashes are recorded in `build/host-engine-temperature-smoke/result.json`.

The controlled native fixture executes actual fuel clamps/comparisons, density,
viscosity and pressure arithmetic. It compares host-native and guest-projected
results over 32 temperature/fuel combinations, including 2.9/3/3.1°C and -25/220°C,
and four RPMs. Part reads and downstream effects are inert; it does not execute
complete engine operation or persistent damage. Additional cases cover joint host
capture, invalid globals, availability, revisions, handoff, disconnect, protected
helper execution, nested/exception restoration, ten changed bindings and cleanup.
The unchanged protection suite continues to exercise native saved-part guards.

Two-player acceptance: start the same Corris from cold, normal and hot host saves
with a guest driving and their own car saved at a different temperature. Check
priming/start behavior, mixture and oil pressure, swap drivers, park/rejoin and
repeat. Compare both players' results and preserve the guest save. Full thermal
handoff and live driving acceptance remain open. Changes are local/unreleased,
mod 0.1.33 and protocol 172; no installer or installed-game deployment is produced.

### Host coolant dashboard (protocol 171, unreleased)

`VehicleCoolantState` (197) carries host coolant degrees independently of the
Corris driver. It is 15 bytes including ID, reliably ordered, sampled at 2 Hz on
change with a 5-second keepalive. The existing `vehicleTemperature` source now
declares authority and its native `Coolant temp 2` readiness state. The host waits
through Cooling's native 8-second Init delay; after the first observed thermal
cycle, a stopped source retains its heat. Native restart, lost/replaced source,
nonfinite values or removed vehicle publish unavailable.

The host keeps its native dashboard. Guest drivers and observers project accepted
host degrees into the native GetFsmFloat gauge output each frame; normal scaling,
clamping and needle actions follow. An unseeded/unavailable guest view uses the
native cold stop. No physical coolant, engine-temperature global or saved part
value is overwritten. Sorbet and Machtwagen retain their existing driver-owned
temperature presentation.

The native checks reuse the retained real Cooling and three-car gauge definitions
in `vehicle-temperature-probe.json`. Cooling's complete extracted definition
matches the later protocol-170 audit. Only actual gauge read/scale/clamp actions
run here; source state transitions are controlled and downstream simulation is
inert. These checks exercise source readiness, native gauge units, host/guest
roles, revision replay, driver/seat changes, expired engine streams, late scene
discovery, unavailable/recovery, snapshot/live publication, keepalive and cleanup.
They do not simulate the complete physical heating/cooling loop.

Validation passes **3,014 Net tests**, **18 launcher tests** and **2,541 native
checks** (23 new; all 2,518 previous labels/counts retained). Both Net targets,
Core Debug/Release, the probe and Launcher Debug build with zero warnings/errors.
The native run uses Debug; Release is build-only. Launcher and disposable-game
payload hashes match. Changes remain local/unreleased on mod 0.1.33, protocol 171.

Local evidence is retained in `build/host-coolant-state-smoke/`. The initial run
caught fixture setup errors: host scenarios inherited save protection, and Corris
RPM binding requirements leaked into later scenarios. The fixture now explicitly
selects host protection state and restores those temporary bindings; production
save and engine guards remain intact. A second retained run exposed fixture
transitions immediately skipping the readiness state; the controlled source now
holds its states and uses the native Init startup name.

Two-player acceptance: heat Corris with the host driving, then swap to a guest
whose own saved car has a different temperature. Both dashboards should keep the
host's temperature. Repeat cold, normally warm and above 120°C; park with the
ignition off, join late, disconnect/rejoin and swap drivers again. Confirm the
guest's personal save remains unchanged. Shared physical thermal consumers,
engine-temperature handoff, remaining damage paths and live driving remain open.

### Host cooling RPM inputs (protocol 170, unreleased)

The host's Corris cooling graph now uses accepted driver RPM for pump circulation,
mechanical fan rate and the running leak check. These inputs join v169 movement
speed in one five-reader group, consolidated in `.CoolingInputs.cs` and
`.CoolingReads.cs` under the `vehicleCooling` catalog profile. VehicleState 60
stays **25 bytes including ID**; protocol 170 changes the meaning of its existing
RPM field. No fields, flags, IDs or channels were added. Mod 0.1.33 stays local
and unreleased.

Native Water Pump 2 closes below 100 RPM, when the belt or pump is absent, or
when pump wear is below 7. Equality at either threshold passes. Native Open/Closed
states still set circulation and thermostat state from host efficiency and limits.
Fan uses accepted RPM divided by the host CoolingFanModifier only after native
fan/belt installation checks. Motor on? takes the housing-leak branch at >=200
RPM and the ordinary water-leak branch below it. Host part inputs and downstream
thermal/saved writers remain native. Packets add no ticks and write no global
RPM, circulation, fan rate, temperature or saved part value.

Valid RPM does not require optional movement/torque availability. Expired, missing,
wrong-owner or snapshot samples project stopped RPM. Local ownership/seating,
guest mode, disconnect and protected-save mode retain native operands. All five
read bindings validate together, including source identities, action references,
operand signatures, timing and native transitions. Changed references clear the
whole group and can repair on later telemetry; nested/exception restoration uses
a distinct Harmony declaring type from wear and heat.

Run the same 54-flag suite with the retained `vehicle-speed-probe.json` fixture.
The new checks run inside `--wintermp-vehicle-state-probe`, importing native
Water Pump 2, Open, Closed, Fan and Motor on? actions in addition to the earlier
movement/temperature branches. Controlled Data sources supply belt/fan/pump
installation, wear and efficiency; downstream leak/thermal branches are held at
inert boundaries. The 60 native comparison cases cover RPM 0, 99, 100, 199, 200,
201, 800, 2500, 6500 and 65535 across healthy, missing belt/fan/pump, wear below 7
and wear exactly 7. Other checks cover native efficiency/modifier changes, no
arrival ticks, missing data, handoff, save protection, nested/exception restoration,
malformed references, global/local aliasing and cleanup.

The first run retained every prior check but its new handoff check reused driver
2's sequence zero from the earlier movement scenario. The real replay guard
correctly rejected it. The RPM scenario now starts with a fresh test stream;
production sequence handling is unchanged. That complete failed output and its
logs are retained. The native definitions are unchanged from the retained v169
audit; its whole-scene integrity limits still apply.

Validation passes **2,518 native checks, zero failures, Wine exit 0**, preserving
all 2,441 previous labels/counts. The 77 new native checks include the 60 RPM/part
comparison cases. Net tests pass **2,984** (16 new), launcher tests **18**.
Core Debug/Release, both Net targets, the probe and Launcher Debug build cleanly
without warnings or errors. Debug is native-tested; Release is build-only.
Launcher and disposable-game payload hashes match. The renamed v169 source files
are retained under this slice's `predecessor-source/` for auditability.

[Results, retained output and tested hashes](../build/host-cooling-rpm-smoke/result.json)
record validation. This covers native pump/fan/leak decisions with controlled part
inputs, not the full cooling/leak loop or live driving physics. Shared temperature
replication, complete thermal handoff, remaining damage paths and live two-player
acceptance remain open. No installed-game plugin deployment, personal-save write,
installer, commit, push or release was performed.

### Host cooling movement speed (protocol 169, unreleased)

The host now supplies accepted Corris guest movement to native cooling airflow
and the stationary-temperature check. VehicleState 60 appends a strict movement
availability byte and uint16 speed in tenths of km/h after torque, increasing
its size from 22 to **25 bytes including ID**. Unavailable requires zero. The
existing wheel-speed field keeps its dashboard meaning, so spinning wheels do
not create airflow. Mod version 0.1.33 remains local/unreleased; peers require 169.

The native Measurements producer reads Rigidbody velocity magnitude into global
SpeedKMH, then multiplies by 3.6. Its following differential-speed read must retain
its separate local output. Capture validates this source and its native references, requires an enabled/started producer in its measurement state, and
rejects negative/nonfinite output. Finite speed clamps to the wire range. Other
vehicles currently send unavailable. Copied telemetry and snapshots preserve
movement, wheel speed, RPM and torque as one accepted sample.

On the active unprotected host of a guest-owned Corris, two scoped helpers feed
Cooling/Air cooling #1 and Check temp #1. Native ambient adjustment, absolute
value, rate division/clamps and additional fan/heater cooling stay intact. The
hot stationary branch retains its >2 km/h bypass, with equality still stationary.
Missing, stale, unavailable or wrong-owner samples use native stationary speed.
Local ownership/seating, guest mode and disconnect retain native reads. Packets
write no temperature, global speed or cooling rate and add no simulation ticks.
All source/read references validate together; malformed bindings clear and retry.
Each helper restores its operand on normal return, exception or nested entry.
Harmony state belongs to a distinct nested callback type, separate from heat/wear.

Run the full 54-flag suite with `vehicle-speed-probe.json` alongside the existing
fixtures in the disposable game root. Checks run inside
`--wintermp-vehicle-state-probe`. The fixture imports the native Measurements
producer, Air cooling and Check temp actions, and holds their outgoing branches
at inert boundaries. It compares native local/delegated calculations at 0, 2,
2.1, 20, 80 and 6553.5 km/h with ambient −20, 30 and 45 °C, including wheelspin,
thresholds, timeout, handoff, ownership, source capture, malformed bindings,
exception/nesting restoration and cleanup. It uses a disabled native drivetrain
and controlled Rigidbody velocity/ambient/part inputs. This does not exercise
the complete cooling loop, shared thermal state or live driving physics.

Validation passes **2,441 native checks, zero failures, Wine exit 0**, retaining
all 2,407 prior labels/counts. The 34 new native checks also retain all 33 checks
from the passing run before the final source-output guard. Net tests pass
**2,968** (24 new), launcher tests **18**. Core Debug/Release, both Net targets,
the probe and Launcher Debug compile without warnings or errors. Debug is
native-tested; Release is build-only. Launcher and disposable payload hashes match.

Three diagnostic runs are retained (16, 2 and 1 failed assertions). Fixture fixes
reset the native accumulated cooling output, enter the measurement state, and
hold downstream cooling branches at inert boundaries. The unit expectation now
uses exact integer cases to avoid legacy Mono intermediate-expression precision.
The save-protection check verifies hook abstention; it does not expect the real
guest writer guard to permit cooling transitions. Production protection remains
intact. A passing run preceded final source-output validation and its extra check.

[Results, retained diagnostic runs and tested hashes](../build/host-cooling-speed-smoke/result.json)
record validation. The new audit includes Measurements, Cooling and the stock
speedometer, with native GetSpeed decompiled from the installed managed assembly.
Earlier scene-integrity discrepancies remain recorded in the v167 artifacts.
Thermal replication to the guest driver, complete thermal handoff, remaining
damage and live two-player acceptance remain open. No installer, commit, push,
release, installed-game plugin deployment or personal-save write was performed.

### Host engine heating inputs (protocol 168, unreleased)

The host now advances Corris HeatGeneration using the established guest driver's
accepted RPM and native drivetrain torque. VehicleState 60 appends a strict
availability byte and finite float torque after gear, increasing its size from
17 to **22 bytes including ID**. Unavailable torque must be zero; available zero
and negative torque are valid. Protocol 168 rejects older peers. Mod version
0.1.33 remains unchanged; this is local and unreleased.

Torque capture uses the audited active HeatGeneration GetProperty torque output
Power, preserving its signed float value. It requires the native source,
drive reference and complete graph to validate. Stopped, missing, changed or
nonfinite sources send unavailable/zero. Accepted and snapshot copies preserve
RPM and torque together; stale or foreign reports cannot replace either input.

On the active unprotected host, scoped native helpers substitute both RPM-square
operands, the torque operand and the two RPM comparisons. All heating actions
and identities validate before substitution. The original operands restore on
completion or exception, including comparisons that enter another state before
returning. Native host EngineFriction, heat arithmetic, rate clamp and per-second
EngineTemp writer remain in charge. Receiving a packet adds no heat tick and
writes no temperature, drivetrain property, currentPower/fuel input or guest save.

The native start threshold is RPM >400; the running graph stops below 100.
Equality preserves the current state. Expired, missing, unavailable or wrong-owner
samples supply zero inputs and follow native stop behavior, including its final
running heating tick. Local seating/ownership, guest mode, disconnect and stream
cleanup retain native inputs. Native heat-rate clamps contain extreme finite
load, and changed bindings recover after repair.

The first native run crashed: heat and wear prefixes/finalizers were declared
on the same partial class while both patching FloatOperator. Harmony keys
`__state` by declaring type, allowing the heat read object to reach the wear
finalizer. The heat callbacks now live on their own nested type; the corrected
full run passes. The crashed game's output, core log, stdout and crash dump are
retained under `build/host-engine-heat-smoke/`; its isolated Wine prefix was
stopped after the crash. No installed-game process was targeted.

Validation passes **2,407 native checks, zero failures, Wine exit 0**, retaining
all 2,373 previous label/counts. The 34 new checks compare native local and
delegated heat exactly at 0, 180, 400, 401, 3000 and 6500 RPM across negative,
zero and positive torque. They cover load/friction effects, extreme clamps,
threshold hysteresis, sample loss/expiry, copied data, sequencing/handoff, local
seating/ownership, guest protection, disconnect, source capture, changed graphs,
exception restoration and cleanup. Net tests pass **2,944** (26 new), launcher
checks **18**; Core Debug/Release, both Net targets, the probe and launcher build
with zero warnings/errors. Debug is native-tested; Release is build-only.

Use the same full 54-flag suite, with this slice's `vehicle-heat-probe.json` beside
the existing fixtures in the disposable game root. Heat checks run inside
`--wintermp-vehicle-state-probe`. The fixture imports the full native heating
graph and uses a disabled native drivetrain plus controlled host friction; this
validates heating calculations and writer cadence, not complete driving physics.
The native definitions come from the retained v167 engine audit. That turn's
intermittent scene-file hash discrepancies remain recorded separately.

[Results and hashes](../build/host-engine-heat-smoke/result.json) and the
[native threshold helper](../build/host-engine-heat-smoke/FloatCompare.cs) record
the tested payload and evidence. Cooling/speed authority, thermal replication to
the guest driver, full thermal handoff, remaining wear/damage and live two-player
acceptance remain open. No installer, commit, push, release, installed-game
plugin deployment or personal-save write was performed.

### Host oil-contamination input (protocol 167, unreleased)

The host now uses accepted guest RPM for native `Oil/Oil contamination #2`,
which divides RPM by 250000 into `OilContaminationRate`. This extends the existing
pressure/mechanical-wear input group to seven readers on Corris. All seven must
validate before any input is substituted; a changed oil operand also disables
pressure/wear projection, and repaired bindings recover on subsequent telemetry.
VehicleState 60 remains 17 bytes including ID. Protocol 167 changes its semantics;
mod version 0.1.33 is unchanged.

Native filtering, contamination writers and the 1.1-second Oil wait stay in
charge. The host filter's Dirt greater than 100 disables filtering; exactly 100
still filters. The contamination-rate clamp has a native minimum of 0.01, so
zero/stale RPM retains that minimum rather than forcing contamination to zero.
A healthy filter offsets it; a clogged filter still permits the native minimum
increase. Packets never execute the writers or add extra simulation ticks.
Host oil quantity and temperature remain untouched by the RPM projection.

Validation runs the imported native Oil filter → contamination → cool-engine
friction → Wait chain with controlled host oilpan/filter Data targets, alongside
the existing complete mechanical-wear fixture. Native local and delegated
results must match exactly at 0, 180, 2500, 6500 and 65535 RPM, with filter Dirt
50, 100 and 101. Independent expected-value checks verify a single subtraction
and addition, with a 0.00001 tolerance for legacy Mono expression/storage
precision. Native-versus-native comparisons remain exact. Checks also cover
packet cadence, expiry/missing/foreign-owner samples, local ownership,
disconnect, changed oil divisor/group repair, and protected guest writers
through disconnect while native Oil math continues.

The first native run and a diagnostic rerun each retained all earlier checks
but failed fifteen independent exact-expression assertions. Diagnostic values
confirmed matching native results; only the expected-expression comparison
needed floating-point tolerance. Both reports and entire output directories
are retained under `build/host-oil-contamination-smoke/`.

Final validation passes **2,373 native checks, zero failures, Wine exit 0**,
including all previous 2,352 labels/counts and 21 new oil checks. Net tests pass
**2,918** and launcher tests **18**. Core Debug/Release, both Net targets, the
probe and Launcher Debug build with zero warnings/errors. The tested Debug
assemblies/catalog/compatibility metadata match the launcher and disposable-game
payloads; Release is build-only. Scene integrity reads produced intermittent
level2 hash/byte discrepancies with unchanged timestamps. Re-extracted engine
FSM definitions and globals match the original audit; later installed/disposable
hashes matched the baseline. The cause remains unresolved, so this is not a claim
that scene bytes were stable throughout validation. `installed-scene-integrity.json`
records all observations; no scene asset was rewritten. Assembly-CSharp and
sharedassets3 matched the original hashes.
[Results and hashes](../build/host-oil-contamination-smoke/result.json),
[native engine/oil audit](../build/host-oil-contamination-smoke/scene-audit.json),
and [native contamination writer](../build/host-oil-contamination-smoke/AddFsmFloat.cs)
record the evidence.

Use the same full 54-flag suite. Replace `vehicle-wear-probe.json` in the disposable
game root with this slice's audited Oil/Pressure/Wearing fixture; checks remain
inside `--wintermp-vehicle-state-probe`. All builds disable game deployment.

The accompanying native HeatGeneration audit identifies why heat is separate:
State 2 waits for RPM >400; State 1 uses native drivetrain torque, RPM squared
and EngineFriction.FrictionToHeat to calculate HeatingRate, clamps it to 1.1–7,
then adds it to global EngineTemp per second. It returns to State 2 below
100 RPM. PowerCurrent is a separate drivetrain currentPower read also consumed
by FuelLine. Projecting RPM alone or copying a guest's unseeded temperature
would not establish authoritative thermal progression or handoff.

Full shared heat, remaining damage/consumption paths, physical failure
presentation and live two-player driving acceptance remain open. This fixture
uses isolated native graphs and controlled part targets, not a complete driving
session. No installed-game plugin, personal save, installer or release is changed.

### Host oil-pressure and wear inputs (protocol 166, unreleased)

The host's native Pressure and Wearing FSMs read global RPM, which belongs to its
local simulation. While a guest owns the car simulation, this can leave pressure
and wear calculations at the host's stopped/stale RPM. `vehicleWearInputs` now
binds six native FloatOperator inputs on Corris `Simulation/Engine/Oil`: one in
Pressure and five in Wearing. The native loop still computes pressure and wear
from the host's temperature, oil quantity/quality, condition and durability.

Only an active host outside the protected-guest-save latch substitutes inputs.
The established guest owner supplies the already authenticated and sequenced
VehicleState RPM. The accepted sample must be fresh, match vehicle and owner,
and not use the snapshot sentinel. Missing, expired or mismatched input supplies
zero RPM while ownership is still delegated, preventing stale local RPM from
adding wear. Local seating or ownership, owner release and disconnect use native
inputs. Parked guest-owned ignition retains the same simulator authority.

A prefix/finalizer around native FloatOperator.DoFloatOperator substitutes an
owned scalar in the selected action field for that call and restores its original
global reference even when native execution fails. It never writes global RPM,
EngineTemp or saved guest data. All six bindings are checked before any one is
substituted; changed state/action/timing/operation, operand, output, source or
catalog identity removes the whole group, with retry on later telemetry. This
keeps the ordinary native arithmetic and 1.1-second wait intact. Network packets
do not directly lower part condition or add extra wear ticks.

Native writers cover five main bearings, crankshaft, auxiliary shaft, four
pistons, oil pump, camshaft and fuel pump: fourteen separate targets. Driver
telemetry does not supply wear deltas, oil quantity, temperature or replacement
part identity. Engine heat remains host-authoritative; using an unseeded guest
save's temperature here would be unsafe. Shared thermal progression and handoff
need a separate implementation. This scope also does not inject RPM into fuel,
combustion, oil contamination, clutch, overrev, cooling or electrical simulation.

Protocol **166** is a semantic change to VehicleState 60. Its existing **17-byte
layout including message ID**, flags, fields, channels and ownership gates are
unchanged. The handshake rejects mismatched protocol versions; mod version
0.1.33 remains unchanged and no release was created.

Validation in the disposable build-23268598 game passes **2,352 native checks,
zero failures, Wine exit 0**. All 2,331 preceding label/counts are retained.
The 21 new checks execute the native pressure chain and entire Wearing loop
against fourteen controlled host part Data targets. Delegated results exactly
match local native results at 0, 180, 2,500 and 6,500 RPM. Checks cover host heat
and oil authority, packet-versus-native cadence, stale/forged reports, handoff,
local seating/ownership, guest protection, disconnect, exception restoration,
changed sibling bindings, duplicates, movement, repair and stream cleanup.

The initial run's six failures are retained: the fixture started its native
Wearing graph while guest-save protection was still active, so its host writers
were disabled. Native startup now occurs after entering host mode, with an
explicit check that all fourteen writers are enabled. A subsequent passing
2,351-check run is retained; final review added the complete-group check at the
native boundary and its regression, producing the final 2,352 checks.

Net tests pass **2,916** (33 new policy/catalog cases); launcher tests pass **18**.
Both Net targets, Core Debug/Release, the probe and Launcher Debug build without
warnings/errors. The tested Debug assemblies match the launcher and disposable
game payloads; Release is build-only. Use the previous full 54-flag suite and
copy `vehicle-wear-probe.json` alongside the temperature and earlier fixtures to
the disposable game root. Wear checks run within `--wintermp-vehicle-state-probe`.

[Results and hashes](../build/host-wear-input-smoke/result.json),
[native Oil/Pressure/Wearing audit](../build/host-wear-input-smoke/scene-audit.json),
and [native arithmetic helper](../build/host-wear-input-smoke/FloatOperator.cs)
record the evidence. The source graphs and native writers execute with controlled
fixture parts/inputs; real driving physics, physical part failure, the complete
thermal/consumption model and live two-player acceptance remain unverified.
Changes are local/unreleased; no installed-game plugin or personal-save deployment.

### Vehicle temperature sources and gauges (still protocol 165, unreleased)

The generic −220° temperature mapping was not valid for any of the three audited
cars. Native Corris `StandardGaugesData::Temp.Angle` holds **degrees**, clamped to
50–130. Machtwagen `Rotation` divides degrees by −0.75, clamping to −160…−50;
Sorbet divides by 2.4100000858306885, clamping to 0–73. Source temperatures are
Corris `Systems/Cooling::Cooling.CoolantTemp` and the other cars' respective
`CarTempTaxi`/`CarTempSorbet/Radiator::Cooling.Temp` values.

`vehicleTemperature.sources` selects each fixed source and dashboard. Runtime
binding validates the native `Speed` state's first three actions: every-frame
GetFsmFloat, optional enabled FloatOperator/Divide, and enabled FloatClamp. The
literal divisor and bounds come from those native actions. The Corris divide is
disabled. Targets, local output identity, unique paths, current action/state
references and finite literal bounds must match; changed bindings stop only this
projection and retry after the normal systems-probe delay. Source and display
variables must be distinct. A stopped source retains its last finite heat.

Capture reads source degrees directly, independently of the gauge's current
scratch or power state, then uses the existing 0–120 °C byte conversion. Clamping
before multiplication prevents finite extreme values overflowing the conversion;
NaN/infinities use zero. The former angle fallback and guessed negative display
scale are removed. Physical engine and coolant values are never updated from the
observer stream.

After a valid observer packet, a narrowly scoped postfix on native
`GetFsmFloat.DoGetFsmFloat` supplies accepted degrees to that dashboard reader.
The following native division, clamp and visual actions still run every frame.
The hook does not apply to any other GetFsmFloat action. Local ownership or
seating, a different remote owner, expiry, disconnect and stream cleanup yield
to native reads. Signature checks also run at this boundary; full tree identity
checks occur when capturing or applying a packet, not on every native read.

Validation on installed build 23268598 (v.260516-01), in the disposable game and
Wine prefix, passes **2,331 native checks, zero failures, Wine exit 0**. All 2,274
previous labels/counts are retained; 57 new checks exercise all three native gauge
chains at seven temperatures, cold/overheat quantization, invalid and extreme
values, inactive producers, signature/target changes, duplicates, movement,
variable replacement and recovery. Sorbet exercises live/final/snapshot packets,
authenticated observer reception, repeated native updates, duplicate rejection,
seating/ownership changes, timeout, disconnect and cleanup.

Net tests pass **2,883** (28 new catalog cases), and launcher tests pass **18**.
Both Net targets, Core Debug/Release, the probe and Launcher Debug build with no
warnings or errors. Debug is native-tested; Release is build-only. Launcher and
disposable-game payload hashes match the tested Debug build.

Reproduce using the previous ambient-input suite's 54 flags, with the additional
`vehicle-temperature-probe.json` copied to the disposable game root. Temperature
checks run inside `--wintermp-vehicle-state-probe`. The fixture merges the audited
three dashboard graphs and three cooling graphs, importing the three gauge
actions; physical needle writes and heat progression are quiet, and temperatures
are controlled fixture values. The first run's five fixture failures are retained:
variable-replacement checks expected immediate rebuilding after invalidation,
ignoring the retry delay, which also affected the two following stream checks.
The fixture now explicitly advances through invalidation and rebinding. During
review, the every-frame native read overwrite was separately fixed and covered
by the final native tests.

[Results and hashes](../build/vehicle-temperature-smoke/result.json),
[dashboard audit](../build/vehicle-temperature-smoke/scene-audit.json),
[source audit](../build/vehicle-temperature-smoke/source-audit.json), and
[native reader implementation](../build/vehicle-temperature-smoke/GetFsmFloat.cs)
record the evidence. Protocol remains 165: this corrects reading/presentation of
the existing degree-valued VehicleState field, without layout, channel, ownership
or semantic changes. No version bump, installer, release or installed-game
plugin deployment was performed. The full dynamic EngineTemp/CoolantTemp state,
thermal handoff, host wear during guest driving, physical needle rendering and
live two-player acceptance remain unverified.

### Guest cooling ambient inputs (protocol 165, unreleased)

Add `--wintermp-cooling-ambient-input-probe` to the complete v164 run and copy
`build/cooling-ambient-input-smoke/cooling-ambient-input-probe.json` into the
disposable game root. The fixture retains previous native graphs and adds
CORRIS/Functions/RoofCheck::Raycast from installed build 23268598 (v.260516-01).
Its TempCar output integrates shelter temperature rather than directly copying
the weather forecast. Keep `-p:DeployToGame=false` on builds/tests and preserve
the entire probe output directory before every run.

EngineBlockState 195 appends strict CoolingAmbientAvailable (0/1) and finite
CoolingAmbientTemperature, giving 209 bytes. Unavailable temperature must be zero;
available zero and negative temperatures are valid. This source is independent
of all installed parts. Host capture requires a unique active, enabled, started,
initialized Raycast with all four audited states and a finite TempCar, currently
in Cast ray, Check roof, Under roof or Under sky. Invalid/missing sources clear
only ambient availability and temperature. Existing revisions, snapshots,
keepalives and live publication include the appendage.

Cooling/Reset #1 reads host TempCar from an owned inert Raycast proxy into native
TempArea on entry. Shared RoofCheck references, local shelter/rain behavior and
arrival scratch remain intact. Missing ambient data has no safe invented numeric
fallback, so Cooling pauses and later completes its blocked entry. Admission
can proceed when all bindings/writers validate and the only outstanding input
is verifiably paused pending data; runtime readiness remains false. Validation
continues after a pending source, so reordering it before a malformed binding
cannot bypass admission protection. Waiting reuses the valid proxy. Disconnect
restores original owners while saved-part write protection persists.

The fixture runs native Check roof, Under roof and Under sky actions with a
controlled roof hit/temperature. Sheltered warming adds HeatChange and clamps
between AmbientTemperature and roof Temp; outdoor cooling subtracts HeatChange
and clamps between AmbientTemperature and 25. Physical raycasts, their eight-second
schedule and rain presentation are outside this fixture. Native Air cooling uses
the actual global SpeedKMH wrapper, computes abs(speed * (TempArea - 30)) divided
by the body airflow modifier, clamps to 0.03..2.1 and adds/clamps combined rates.
Tests cover seven temperatures and three speeds, source initialization/faults,
revision copying, cold joins, stale/conflicting recovery, exact read signatures,
moved sources, destroyed proxies, disconnect, and actual GuestSaveGuard admission.
The local RoofCheck keeps running while host input drives Cooling.

Validation passes **2,855 Net tests** (42 new), **18 launcher tests**, and
**2,274 isolated native checks** (49 new; all 2,225 earlier labels/counts retained).
Core Debug/Release, both Net targets, probe and Launcher build with zero warnings
or errors. Native testing uses Debug; Release is build-only.
[Results and tested hashes](../build/cooling-ambient-input-smoke/result.json)
identify the matching disposable-game and launcher payloads.

Earlier reports are retained. The first native run exposed fixture errors:
ambient prerequisite revisioning prevented existing head seeding, SpeedKMH was
incorrectly treated as local, and older disconnect assertions expected Cooling
readiness despite the new ambient wait. The corrected fixture seeds ambient at
revision zero, uses the native global speed wrapper and asserts a paused Cooling
consumer before cleanup. An accidentally repeated old probe is recorded separately.
The subsequent admission review found and fixed the pre-connection wait cycle;
actual admission and reordered-invalid-binding checks now pass. A final check
also verifies proxy reuse while waiting. Four initial Net failures arose in JSON
serialization/parsing in unrelated tests; all 226 affected-suite tests and the
complete 2,855-test rerun passed unchanged. Their cause was not established.

The current profile has 102 scalar sources/182 reads, one valve array/eight reads,
nine consumers, 22 paused saved graphs, 75 scalar guards in 11 graphs and 38/31
replacement/package factories. The native Cooling GetFsm coverage audit now has
zero unprojected reads. This does not cover global/dynamic thermal state, physical
car reconstruction or host wear while a guest drives. Real two-player operation
with different saves remains unverified. No personal save, installed-game
deployment, installer or release is involved.

### Guest cooling airflow inputs (protocol 164, unreleased)

Add `--wintermp-cooling-airflow-input-probe` to the complete v163 run and copy
`build/cooling-airflow-input-smoke/cooling-airflow-input-probe.json` into the
disposable game root. The fixture retains previous native graphs and adds four
fixed airflow mount Data graphs plus the scene BLOCKOFF0 part from installed
build 23268598 (v.260516-01). Scene/prefab audits verify all four grille families,
stock/fiberglass bonnet fields and the cover's native Init identity. Keep
`-p:DeployToGame=false` and preserve the whole probe output directory before each run.

EngineBlockState 195 appends four installation bits (grille, cover, stock bonnet,
fiberglass bonnet) followed by three float modifiers (grille, stock bonnet,
fiberglass bonnet), giving 204 bytes. Modifiers must be finite and zero when
absent. Mounts are independent of the block/head/radiator and each other; native
Cooling applies the dependencies. Host capture requires unique enabled, started,
initialized, Installed Data at Update 2 and an active attached AssemblyID=1 part.
The grille accepts original/counter VIN413/B/C/D identities; stock bonnet VIN411;
fiberglass bonnet canonical HOODa0 factory counters. The scene cover's native
GetOwner/GetName actions capture the exact BLOCKOFF0 identity before display
renaming; it has no float fields. Grille prefabs also lack the modifier read by
Install 2, so capture validates their Tightness field and reads the live mount
modifier. Missing, transitional or invalid sources clear independently.

Seven entry-only native Cooling reads use owned inert Data while retaining
shared db references and scratch until execution. The native Reset #3 initializes
CoolingAirRateModifier to 2900. An installed grille adds its modifier, with another
4000 when the cover is present; no grille means the cover is ignored. The stock
bonnet adds its modifier if installed, otherwise an installed fiberglass bonnet
adds its own. Native stock-bonnet priority is retained when both bits are set.
All four saved mount Data graphs remain paused through disconnect, preventing
native removal and detachment.

Validation passes **2,813 Net tests** (67 new), **18 launcher tests**, and
**2,225 isolated native checks** (125 new; every previous 2,100 label/count retained).
Core Debug/Release, both Net targets, probe and Launcher build with zero warnings
or errors. Native verification uses Debug; Release is build-only.
[Results and tested hashes](../build/cooling-airflow-input-smoke/result.json)
identify the matching disposable-game and launcher payloads. The first full native
run passed. An initial Net run exposed one stale Cooling source-count assertion;
it was updated from 12 to 16, and the final suite passes.

Checks cover all sixteen installation masks, stock/fiberglass priority, cover
conditionality, negative/zero/fractional modifiers, native accumulator reset,
warm caches and arrival scratch, stale/conflicting refits, changed action
signatures, relocated mounts, destroyed proxies and disconnect. Host checks cover
initialization, all install transitions, nonfinite values, all grille variants,
canonical and malformed identities, exact cover identity, source/part liveness,
attachment, duplicates, missing fields and copied revision/snapshot publication.
Native boolean installation, bonnet modifier copying, parenting and removal run
before admission and stay blocked afterward. Physical joints, hinge/mesh behavior,
repair and global assembly events are outside this fixture; no personal save is
loaded or written. Live two-player behavior remains unverified.

The current profile has 101 scalar sources/181 reads plus one valve array/eight
reads across nine consumers, 22 paused graphs, 75 scalar guards in 11 graphs and
38/31 replacement/package factories. The remaining unprojected Cooling GetFsm
read is Reset #1, RoofCheckFSM.TempCar into TempArea, recorded in
`build/cooling-airflow-input-smoke/remaining-cooling-reads.json`. Ambient/dynamic
thermal state, physical reconstruction, host wear while a guest drives and
live testing with different saves remain separate work.

### Guest coolant hose leak inputs (protocol 163, unreleased)

Add `--wintermp-coolant-hose-input-probe` to the complete v162 native run and
copy `build/coolant-hose-input-smoke/coolant-hose-input-probe.json` into the
disposable game root. The fixture retains prior native graphs and adds the
four exact hose Data graphs from installed build 23268598 (v.260516-01).
The scene audit includes the four factories and mounts; the prefab audit
confirms VIN202, VIN203, VIN216 and VIN217 identity, assembly and scalar fields.
Cooling and carburettor graphs are retained from the earlier native fixtures.
Preserve the entire probe output directory before each run and retain
`-p:DeployToGame=false` on every build/test.

EngineBlockState 195 appends four installed hose bits, four Tightness values
in top/bottom/inlet/outlet order, and CarburettorTightness, giving 191 bytes.
Hoses use their independent fixed CORRIS/Assemblies mounts; radiator hoses
settle at Update 2, heater hoses at Update. Unique Data must be enabled,
initialized/started and Installed, with an active attached canonical original/
counter part, AssemblyID=1 and Tightness. Missing, invalid or transitional
hoses clear only their own bit/value. All values are finite, without invented
clamps. Carburettor clamp capture joins its existing atomic mounted input group
under the installed head and supports the stock and both upgraded variants.

Six entry-only Cooling reads use inert action-local Data: bottom-hose
Installed and the four hose clamps plus carburettor clamp. Shared db references,
native scratch and saved Data stay intact on arrival. The native Reset actions
#2/#6 clear WaterLeakRate/TightnessTotal before each loop. Hoses #5 adds the five
clamps; #6 compares with 104. State 5 clamps lower totals to 1..104 and adds
0.2 / total to WaterLeakRate. A missing bottom hose enters Empty coolant while
its saved-radiator write remains blocked. The four saved hose Data graphs stay
paused through disconnect, preventing removal's wear copy, clamp reset and
detachment. Native continuous hose wear is already disabled and stays disabled.

Validation passes **2,746 Net tests** (74 new), **18 launcher tests**, and
**2,100 isolated native checks** (121 new; every previous 1,979 label/count
preserved). Core Debug/Release, both Net targets, probe and Launcher build with
zero warnings/errors. Native verification uses Debug; Release is build-only.
[Results and tested hashes](../build/coolant-hose-input-smoke/result.json) identify
the matching disposable-game and launcher payloads.

The first full native run and a focused diagnostic run exposed fixture errors:
repeated Hoses entries skipped the native Reset, so FloatAddMutiple accumulated
old TightnessTotal; the saved carburettor fixture also started with Tightness=0,
not the assumed 8. The final fixture executes native Reset #2/#6 before each
calculation, checks leak values within 0.0000001, and uses the actual saved
baseline. The extracted native FloatAddMutiple/FloatOperator implementations
and both earlier reports are retained with the results. Production input
projection did not need changing to resolve these test failures. The first Net
run required updating the paused-graph bound test and keeping the earlier
fuel/mixture catalog assertion scoped to its two consumers; the final suite passes.

Checks cover warmed caches, unchanged arrival scratch, all sixteen installation
masks, each clamp independently below/at/above the threshold, negative totals,
carburettor removal, delayed/conflicting refits, changed action signatures,
relocated fixed mounts, destroyed proxies and disconnect. Host checks cover
both native ready-state names, transitions, each nonfinite clamp, original/
counter identities, attachment and missing-field faults, independent hose
clearing, copied revisions/snapshots, head removal/refit and all three
carburettor identities. Native install/removal actions run before admission,
then remain blocked through disconnect. The profile has 97 scalar sources/
174 reads plus one valve array/eight reads across nine consumers, 18 paused
graphs, 75 scalar guards in 11 graphs and 38/31 replacement/package factories.

This verifies controlled native input and saved-part memory protection. Body
creation, global assembly events, leak animation, physical clamp controls and
disk save operations are excluded. Physical hose reconstruction, cap/filling
controls, grille/hood airflow, remaining thermal inputs and host wear while a
guest drives still need work. Two-player acceptance must compare different
saved hose/clamp states, host loosening and removal/refit, late join/resync/
reconnect, and the guest's saved parts after restart. No release is produced.

### Guest radiator cooling inputs (protocol 162, unreleased)

Add `--wintermp-radiator-input-probe` to the complete v161 native run and copy
`build/radiator-input-smoke/radiator-input-probe.json` into the disposable game
root. The fixture retains the earlier graphs and adds the exact Cooling and
VINP_Radiator Data graphs from installed build 23268598 (v.260516-01). The scene
and cooling audits cover the factory, radiator mount, cap/filling and cooling
readers; the prefab audit confirms VIN201, RADIATORa0 and RADIATORb0 Data.
Preserve the entire probe output directory before each run and retain
`-p:DeployToGame=false` on every build/test.

EngineBlockState 195 appends radiator installation and four mounted values:
wear, coolant, pressure-cap rating and electric-fan efficiency. The payload is
170 bytes; the fixed radiator does not depend on block/head installation. Its
unique Data must be enabled, initialized/started, Installed and settled at
Update 2. The active attached part needs a canonical original/replacement or
upgraded radiator identity, AssemblyID=1 and Coolant. Capture reads all four
live mount values atomically; missing, transitional or invalid inputs clear
only the radiator group. Wire values remain finite without invented clamps.

Five entry-only Cooling reads use an inert action-local proxy while the shared
db_Radiator reference, native scratch and saved Data remain intact. The native
installed/missing-radiator branch, coolant clamp to 0..25, pressure limit
`PressureCap * 1.3 + 100`, and electric-fan hysteresis at 80/92 degrees use host
inputs. Arrival does not replay those calculations. The saved radiator's entire
eleven-state Data graph stays paused through disconnect, draining active
continuous wear/coolant writes and preventing native removal. Existing Cooling
coolant write guards remain in place.

Validation passes **2,672 Net tests** (54 new), **18 launcher tests**, and
**1,979 isolated native checks** (74 new; every previous 1,905 label/count
preserved). Core Debug/Release, both Net targets, probe and Launcher build with
zero warnings/errors. Native verification uses Debug; Release is build-only.
[Results and tested hashes](../build/radiator-input-smoke/result.json) identify
the matching disposable-game and launcher payloads. The complete native run
passed on its first attempt. The first Net run needed the expected Cooling
source count updated from six to seven; the final suite passes.

Checks cover warmed caches, unchanged arrival scratch, all five native reads,
empty/negative/overfull coolant, damaged-radiator coolant protection, both native
cap ratings, electric-fan hysteresis and fanless stock input. Removal rejects
stale/conflicting refits; changed signatures and relocated mounts pause and
recover; destroyed proxies rebuild and disconnect restores original owners.
Host checks cover readiness, all three radiator identities, attachment/identity
faults, each nonfinite field, mounted values independent of saved part data,
revision changes, pending snapshots and removal/refit. Native mount checks run
live wear/coolant copying, clamping, installation and removal before admission,
then verify protection of coolant, wear, cap visibility and attachment through
disconnect. The profile has 92 scalar sources/168 reads plus one valve array/
eight reads across nine consumers, 14 paused graphs, 75 scalar guards in 11
graphs and 38/31 replacement/package factories.

This verifies controlled native cooling calculations and saved-part memory
protection. Body creation, electric-fan mesh parenting, hose/global removal
events, cap/filling controls and disk save operations are excluded. Physical
radiator reconstruction, hose tightness/installation, grille/hood airflow,
remaining thermal inputs and host wear while a guest drives still need work.
Two-player acceptance must compare different saved radiator variants/coolant,
host filling/draining and removal/refit, late join/resync/reconnect, and the
guest's saved radiator after restart. No installer or release is produced.

### Guest rocker-cover leak input (protocol 161, unreleased)

Add `--wintermp-rocker-cover-input-probe` to the complete v160 native run and
copy `build/rocker-cover-input-smoke/rocker-cover-input-probe.json` into the
disposable game root. The fixture preserves all previous native graphs and adds
the exact VINP_RockerCover Data from installed build 23268598 (v.260516-01).
The scene audit also includes its Spawn factory, BoltMasker, cap and oil-filling
graphs; the prefab audit confirms VIN118 Data and its saved identity/scalars.
Preserve the entire probe output directory before each run and retain
`-p:DeployToGame=false` on every build/test.

EngineBlockState 195 appends RockerCoverInstalled and RockerCoverTightness,
giving a 153-byte payload. Capture follows the same installed VIN111 head used
by intake/header inputs to its direct VINP_RockerCover child. Its unique Data
must be enabled, initialized/started, Installed and settled at Update 2. The
active attached VIN118 part must have native original/counter identity,
AssemblyID=1 and Tightness. The captured value comes from the mounted bolt total,
not the saved part's scalar. Missing, invalid or transitional sources clear only
this group. Cover installation requires a head but is independent of the oilpan.

The native Oil/Valve Cover #0 GetFsmFloat reads the host value through an inert
action-local proxy. The original db_Rockercover1 and shared Tightness scratch
remain intact; arrival does not run the leak calculation. Native arithmetic
remains `(64 - Tightness) / 60000`, including its unclamped finite range. The
saved cover Data stays paused after head movement and disconnect, preventing
native removal from copying wear, hiding its cap or detaching the saved part.
Its already-disabled continuous wear action stays disabled.

Validation passes **2,618 Net tests** (48 new), **18 launcher tests**, and
**1,905 isolated native checks** (47 new; all previous 1,858 labels/counts
preserved). Core Debug/Release, both Net targets, probe and Launcher build with
zero warnings/errors. Native verification uses Debug; Release is build-only.
[Results and tested hashes](../build/rocker-cover-input-smoke/result.json) identify
the matching disposable-game and launcher payloads. The complete native run
passed on its first attempt. The initial Net build required adding the new
parser to the test project's explicit linked-source list; the final suite is green.

Checks cover warmed caches, absent/removed/stale/conflicting state, native leak
values around the fully tightened boundary, unchanged arrival scratch, signature
faults, destroyed proxies, moved/invalid head identities and restored owners.
Host checks cover readiness, attachment/identity ambiguity, missing/nonfinite
inputs, live mounted tightness, copied snapshots, head/block removal and refit.
Native install/removal checks execute wear copies, cap visibility and detachment,
then verify guest protection through disconnect. The profile now has 91 scalar
sources/163 reads plus one valve array/eight reads across nine consumers,
13 paused graphs, 75 scalar guards in 11 graphs and 38/31 factories.

This verifies controlled engine input and saved-part memory protection. The
fixture excludes body creation, global assembly notifications, oil-cap controls,
filling triggers and disk save operations. Physical cover reconstruction,
remaining thermal/fluid dependencies and host wear under guest driving still
need work. Two-player acceptance must compare different saved cover bolt totals,
host loosening/tightening and removal/refit, late join/resync/reconnect, and the
guest's saved cover after restart. No installer or release is produced.

### Guest oilpan inputs (protocol 160, unreleased)

Add `--wintermp-oilpan-input-probe` to the complete v159 native run and copy
`build/oilpan-input-smoke/oilpan-input-probe.json` into the disposable game root.
The fixture combines the previous native graphs with freshly extracted Oil,
Wearing and VINP_Oilpan Data from installed build 23268598 (v.260516-01).
Scene, factory and saved-part prefab audits are retained alongside it. Numeric
exponents are normalized without changing parsed values for the legacy reader.
Preserve the entire probe output directory before every run and retain
`-p:DeployToGame=false` on every build/test.

EngineBlockState 195 appends an installation byte and five floats: oilpan wear,
tightness, oil quantity, contamination and viscosity. The payload is 148 bytes.
Capture follows the installed native block to VINP_Oilpan, then requires a
unique settled Update 2 Data and attached VIN106 part. The cylinder head is
independent. Read all five finite mounted scalars before publishing any;
transitions, missing/ambiguous objects or invalid scalars clear the group.
Absent groups are zero; revisions, snapshots and keepalives use the existing
host-only reliable stream. Snapshot observation does not consume pending live
publication.

Seven GetFsmFloat action owners in Oil, Wearing and Cylinders read inert host
proxies. Native shared db_Oilpan references and calculation scratch remain
intact; arrival never fires events or rewrites saved Data. Exact signatures and
moving block identity guard reads. The saved oilpan mount is paused, its active
actions drained, and protection survives disconnect. This prevents its native
Update copies to OilLevel/OilDirt and removal's fluid clearing/detachment.

Validation passes **2,570 Net tests** (59 new), **18 launcher tests**, and
**1,858 isolated native checks** (67 new; all previous 1,791 labels/counts
preserved). Core Debug/Release, both Net targets, probe and Launcher build with
zero warnings/errors. Native verification uses Debug; Release is build-only.
[Results and tested hashes](../build/oilpan-input-smoke/result.json) identify the
matching disposable-game and launcher payloads. The initial complete run found
seven older source-count assertions; adding the three oilpan sources required
updating those counts. The final corrected run has zero failures.

The native checks cover seven warm caches, absent/removed/stale/conflicting
inputs, arrival scratch, the wear=1 starvation boundary, clamped bolt leaks,
oil-dependent friction/wear, contamination divisors and spark-plug scaling.
They also cover action-signature changes, proxy repair, moved blocks, restored
owners, five nonfinite fields, native install/update/removal scalar effects,
identity/assembly ambiguity, and protection through disconnect. The profile has
90 scalar sources/162 reads plus one valve array/eight reads across nine
consumers, 12 paused graphs, 75 scalar guards in 11 graphs and 38/31 factories.

This verifies controlled native engine inputs and saved-part memory protection.
The fixture excludes rigidbody creation, global assembly notifications and disk
save operations. Physical oilpan reconstruction, filling/draining controls,
remaining thermal/fluid inputs, complete running-engine behavior and host wear
while a guest drives remain open. Two-player acceptance must compare different
saved oil quantities/condition, host oilpan removal/refit, late join/resync and
reconnect, then confirm the guest's saved pan remains unchanged. No installer
or release is produced.

### Guest valve adjustment inputs (protocol 159, unreleased)

Add `--wintermp-valve-adjustment-probe` to the complete previous native run and
copy `build/valve-adjustment-smoke/valve-adjustment-probe.json` into the disposable
game root. Preserve the entire probe output before each run and keep
`-p:DeployToGame=false` on all builds/tests. The installed-build scene audit
includes the native head's eight-float Valves ArrayList, its saved/randomized
values and adjustment writers. This fixture reuses the actual Valves graph and
settled block/head mount graphs from the preceding exhaust fixture.

EngineBlockState 195 now appends availability plus eight float settings, giving
127 payload bytes. Settings alternate intake/exhaust for cylinders 1–4. Capture
uses the same installed head as intake/header inputs and requires one complete,
finite, float-valued native array. Invalid or missing sources atomically clear
availability and all eight values without discarding other engine inputs.

Eight native ArrayListGet owners target an owned in-memory array; the original
Cylinderhead variable and Get cam profile #8 head-reference read remain intact.
Normal Data scratch, addition/subtraction and loose/normal/tight comparisons
remain native. Arrival does not run calculations, overwrite scratch, edit saved
settings or create physical parts. Exact reader signatures, named references,
array order and output type are guarded. Unavailable/disconnected input reads
zero, matching an unadjusted native array. The existing saved-part wear guards
remain active. Repair and disconnect restore native owners before proxy removal.

Validation passes **2,511 Net tests** (55 new), **18 launcher tests**, and
**1,791 isolated native checks** (55 new; all previous 1,736 labels/counts
preserved). Core Debug/Release, both Net targets, probe and Launcher build cleanly.
Native verification uses Debug; Release is build-only.
[Results and tested hashes](../build/valve-adjustment-smoke/result.json) record
exit 0 and matching disposable-game and launcher payloads. The first complete
run exposed a fixture attempting to rebind after clearing the whole session;
the corrected destruction test uses an active session. The final run has zero
failures. Older camshaft checks now supply their 6.5 valve setting through the
host record before testing native tolerance, instead of seeding shared scratch.

The native checks cover all eight slots at loose/normal/tight settings, warm
saved caches, unknown host data, ordered values, unchanged scratch, five foreign
reader fields, stale/conflicting updates, disconnect, proxy/action replacement
and destroyed consumers. Host checks cover eight independent scalar changes,
nonfinite and wrong-type array entries, missing/duplicate/short/long arrays,
transitional/detached/removed heads, block removal, renamed heads and defensive
snapshot copies. The profile retains 87 scalar source entries/155 scalar reads,
plus one valve-array source/eight array reads across the same nine consumers;
11 paused graphs, 75 scalar guards and 38/31 factories remain unchanged.

This validates engine inputs and controlled native arithmetic, not complete
running-engine behavior. Native misfire sound, subsequent full power integration,
physical valve-adjustment controls and host wear under guest driving remain
separate work. Two-player acceptance must use different saved head tuning, change
each host valve, compare engine behavior, and repeat after head removal/refit,
late join/resync/reconnect and guest restart. Confirm the guest's saved tuning
is unchanged. No installer or release is produced.

### Guest exhaust inputs (protocol 158, unreleased)

Add `--wintermp-exhaust-input-probe` to the complete previous native run and copy
`build/exhaust-input-smoke/exhaust-input-probe.json` into the disposable game root.
The fixture retains the native intake/block/head graphs and adds the four exhaust
mount Data graphs. Scene and stock/upgraded prefab audits come from installed
build 23268598 (v.260516-01). Preserve the entire disposable probe output before
each run and keep `-p:DeployToGame=false` on every build/test command.

EngineBlockState 195 appends a section mask and four fixed performance triplets
(headers, front pipe, rear pipe, muffler), for 94 payload bytes. Each triplet
contains DataPower, DataTorque and DataPowerAdd. Header availability depends on
the installed head; the three car-mounted sections are independent of the engine
assembly. Missing, transitional, malformed or nonfinite input clears only its
own contribution. Guest arrival updates inert input proxies without touching
native scratch or replaying engine calculations.

The fixture executes all twelve native GetFsmFloat reads and twelve FloatAdd
actions, from Headers through Exhaust front, Exhaust rear and Muffler to the
Calculations boundary. All 16 section combinations preserve the native totals.
The 98 new native checks cover cold/warm caches, shared scratch, absent host data,
head removal, copied/stale/conflicting arrays, moving heads, five foreign reader
signatures, fixed-mount relocation, proxy repair and disconnect restoration.
Host capture verifies four startup/settling paths, eleven stock/upgraded
identities (including aftermarket parts without Wear), twelve scalar changes
and nonfinite recovery, 36 invalid-source cases, ambiguous header mounts,
invalid heads and independent section/head/block removal.

All four native Update 2 #0 Wear actions are disabled and remain inert. Native
Remove part clears DataPower/DataTorque/DataPowerAdd at #0/#2/#3 for headers and
#1/#2/#3 for the other sections; #7 copies Wear, #14 detaches and #15 clears
Installed. These effects execute before protection and stay blocked after guest
admission and disconnect, including a renamed/moved header assembly. Physics/body
creation, actual installation, global assembly notifications and disk save/load
are bounded out of these controlled mount fixtures. Part Data is controlled;
identity and field surfaces are checked against the installed prefabs.

Validation passes **2,456 Net tests** (119 new), **18 launcher tests** and
**1,736 isolated native checks** (98 new; all previous 1,638 passing labels and
counts preserved). Core Debug/Release, both Net targets, probe and Launcher
build with zero warnings/errors. Native tests use Debug; Release is build-only.
[Results and tested hashes](../build/exhaust-input-smoke/result.json) record
process exit 0 and matching disposable-game and launcher payloads. The first
focused fixture exposed the manifold's distinct DataPower action index; the
complete corrected run has zero failures. Catalog shape is now 87 sources,
155 reads across nine consumers, 11 paused graphs and 75 scalar guards across
11 writer graphs. Replacement/package factory counts remain 38/31.

Live two-player acceptance remains pending: use different guest exhaust parts,
remove/refit each host section, compare engine performance, and repeat after
late join, resync, reconnect and head/block removal. Check the guest save after
restarting. Physical exhaust reconstruction, exhaust sound, separate racing-front,
sidepipe and tip routing, other engine inputs and host fuel/wear while a guest
drives remain unfinished. The separate racing-front/sidepipe/tip mounts are not
read by these four native Valves performance states. No installer or release
is produced by this check.

### Guest intake inputs (protocol 157, unreleased)

Add `--wintermp-intake-input-probe` to the complete existing native run. Copy
`build/intake-input-smoke/intake-input-probe.json` into the disposable game root.
It contains native FuelLine, Mixture, Valves, block/head/carburettor mount Data
and air-cleaner mount Data. The separate installed-game scene and VIN135 prefab
audits establish air-cleaner identity and fields. Preserve the entire disposable
probe output before each run and build with `-p:DeployToGame=false`.

The fixture executes FuelLine Airfilter's GetFsmBool and BoolTest, selecting
Starting or Dirt accumulation from host installation. Existing persistent wear
writers remain guarded. The SymptomsEngine Stuck Throttle activation and its
subsequent behavior are bounded out. Valves executes all six intake reads and
the normal six additions, from Carburettor through AirFilter to the Headers
boundary. Exhaust reads/calculations beyond that boundary remain separate work.
The four carburettor/filter installation combinations verify independent
contributions and filtration decisions, with unchanged original mount data.

The 48 new native checks cover warm saved caches, unavailable host inputs,
arrival without scratch/event mutation, shared scratch ownership, stale and
conflicting records, five altered read signatures, moved/renamed heads, invalid
parent identity, proxy repair and disconnect. Host capture verifies filter
initialization, six transient states, native original/counter identities, six
independent scalar changes and nonfinite recovery, ten invalid filter sources,
separate intake removal and whole head/block removal. Invalid carburettor power
clears its fuel/tuning fields too; invalid filter power preserves the valid
carburettor. Join snapshots cannot consume pending performance broadcasts.

Native air-cleaner Update 2 #0 Wear is disabled and remains inert. Remove part
#1/#2/#3 clear performance, #7 copies Wear, #14 detaches and #15 clears Installed.
These removal actions execute before protection and stay blocked after admission
and disconnect, including when the saved head has moved. Native physics/body
creation, actual installation, global assembly notifications and disk save/load
are outside these controlled fixtures.

Validation passes **2,337 Net tests** (85 new), **18 launcher tests** and
**1,638 isolated native checks** (48 new; all previous 1,590 passing labels and
counts preserved). Core Debug/Release, both Net targets, probe and Launcher
build with zero warnings/errors. Native tests use Debug; Release is build-only.
[Results and tested hashes](../build/intake-input-smoke/result.json) record
process exit 0 and matching disposable-game and launcher payloads. Older catalog
source counts, moving-mount selectors and message fixtures were updated for the
new entries and 45-byte payload; both focused and full native runs pass.

Live two-player acceptance is pending: use differing guest intake parts, remove
and refit the host's filter and carburettor separately, compare engine behavior,
and repeat after late join/resync/reconnect and head/block removal. Inspect the
guest save after restarting. Physical intake reconstruction, exhaust performance,
other engine dependencies and host fuel/wear while a guest drives still need
implementation. Changes remain local and unreleased.

### Guest carburettor inputs (protocol 156, unreleased)

Add `--wintermp-carburettor-input-probe` to the full existing probe run. Copy
`build/carburettor-input-smoke/carburettor-input-probe.json` into the disposable
game root. It contains installed-game FuelLine, Mixture, block, head and
carburettor mount Data. The supporting spawner and sharedassets3 prefab audits
verify stock, two-barrel and four-barrel native identities. Preserve the whole
disposable probe output before every run; build with `-p:DeployToGame=false`.

The fixture executes the actual Carburator installation gate, chamber/reserve
clamps and Not Ok 4 starter shutoff, plus Calculate density and Calculate mixture
2 arithmetic. Native global EngineTemp references are explicitly bound; throttle
and random RatioMultip are controlled inputs. It tests host packet arrival
without event replay or scratch mutation, unavailable/removal records, stale and
conflicting updates, five altered reader signatures, moving saved-head identity,
proxy repair and disconnect. Saved fuel, mixture, wear, native identity and
attachment are checked throughout.

Host capture executes the real block/head/carburettor readiness states. Eight
intermediate carburettor states stay unavailable; all three native carburettor
families are accepted. Each scalar change advances the shared revision without
join snapshots consuming pending live updates. Thirteen invalid sources clear
only the appropriate dependent fields and recover. Head/block removal also
clears carburettor installation and all three scalars.

Native Update 2 #0 copies Wear every frame; Remove part #1/#2 clear reserve and
chamber, #9 copies Wear, #16 detaches and #17 clears Installed. These actions run
before protection to establish the baseline, then stay inert after admission
and disconnect, including a queued Fsm.Update. The first focused run exposed
queued wear work surviving a pause; protection now finishes existing actions in
paused saved-part graphs as well as blocking new ticks/entries. It also exposed
a detached EngineTemp wrapper in the controlled fixture, now explicitly bound.
Native body creation, global assembly notifications, disk load/save and actual
physical carburettor installation are bounded out of these tests.

Validation passes **2,252 Net tests** (58 new), **18 launcher tests** and
**1,590 isolated native checks** (54 new; every previous 1,536 passing label and
count preserved). Core Debug/Release, both Net targets, probe and Launcher build
with zero warnings/errors. Native checks use Debug; Release is build-only.
[Results and tested hashes](../build/carburettor-input-smoke/result.json) record
process exit 0 and the matching disposable-game and launcher payloads.

Live two-player acceptance remains pending: use different saved carburettors
and settings, test stock/two-barrel/four-barrel host parts, remove/refit the host
carburettor/head/block, test guest starting, repeat after late join/resync and
reconnect, and inspect the guest save after restart. Physical carburettor
reconstruction, air-filter and other engine inputs, and host fuel/wear while a
guest drives still need implementation. Changes remain local and unreleased.

### Guest cylinder-head input (protocol 155, unreleased)

Add `--wintermp-cylinder-head-input-probe` to the complete existing probe run.
Copy `build/cylinder-head-input-smoke/cylinder-head-input-probe.json` into the
disposable game root. It contains installed-game Cylinders, block-mount Data and
the native cylinder-head mount Data from the VIN1010 block audit. Preserve the
whole disposable probe output before every run and build with
`-p:DeployToGame=false`.

The fixture executes the actual Powertrain GetFsmBool and seven-part BoolAllTrue,
then native crank/start-stop decisions. Explicit host prerequisites supply the
other engine parts. Missing head or missing block reaches Not Ok and shuts down
native starting; refitting permits the gate. Block damage does not invent head
removal. Packet arrival preserves calculation scratch until the normal read.
Saved owner wrappers remain intact and are restored on disconnect.

The 39 new native checks cover saved caches, missing/unavailable host state,
stale/conflicting revisions, all head/block installation combinations, five
foreign read signatures, three invalid parent identities, relocated/renamed
blocks, proxy repair and disconnect. Host capture checks native initialization,
five intermediate states, independent block damage, join/live publication
baselines, ten invalid head sources with recovery and removal of the whole
block. Invalid head state clears only its flag; the block remains available.

Native head Remove part #4 copies Wear and #11 detaches its ActivePart before
protection. After guest admission, the moving-mount guard prevents copies from
differing mount values and detachment through disconnect. It recognizes the
saved block by native scene name or ID; input validation requires the unique
parent Data identity. Full body creation, global assembly notifications, native
part installation, disk load/save and physical head reconstruction are outside
this fixture. Valve arrays and thermal/oil dependencies are not projected here.

Validation passes **2,194 Net tests** (40 new), **18 launcher tests** and
**1,536 isolated native checks** (39 new; every previous 1,497 passing label and
count retained). Core Debug/Release, both Net targets, probe and Launcher build
with zero warnings/errors. Native checks use Debug; Release is build-only. The
disposable game and launcher payloads match the tested files.
[Results and tested hashes](../build/cylinder-head-input-smoke/result.json)
record the complete run and game process exit 0. The first focused run exposed a
missing ID field in the controlled block fixture; the final full run seeds that
identity explicitly and passes. Existing source-count expectations were updated.

Two-player acceptance remains pending: use different saved cylinder heads,
remove/refit the host head and whole block, check guest starting, then repeat
with late join/resync/reconnect and inspect the guest save after restart. This
slice only supplies cylinder-head installation. Physical block/head/transmission
reconstruction, valve arrays, thermal/oil inputs, remaining starting dependencies
and host wear while a guest drives still need work. Changes remain local and
unreleased; personal saves, live installation and mod version are unchanged.

### Guest gearbox starter input (protocol 154, unreleased)

Add `--wintermp-gearbox-starter-input-probe` to the full existing probe run. Copy
`build/gearbox-starter-input-smoke/gearbox-starter-input-probe.json` into the
disposable game root. It contains the installed game's gearbox mount Data,
Starter, and automatic 3 speed FSMs. Preserve the entire probe output before each
run and use `-p:DeployToGame=false`; use the isolated Wine prefix and game copy.

The fixture executes the actual Starter Check automatic actions: integer Type
read, IntCompare, local GearLetter read, and P/N StringCompare decisions. Native
selector SetStringValue actions provide P/N/D/R/1/2. All 18 combinations of the
two manual types (0/1) and automatic type (2) with these positions pass. Manual
types follow the native bypass; automatic permits only P/N. The original
SimAutomatic target and local selector remain intact. Other starting dependencies
and subsequent physics/state effects stay bounded by the fixture.

Checks cover saved reader caches, missing/unavailable host observations, packet
arrival without scratch or selector replay, stale/conflicting revisions, five
foreign action signatures, missing integer fields, proxy reconstruction and
disconnect restoration. A blocked native attempt does not execute while its
host input is unavailable. Once protection validates a fresh observation, the
existing deferred-entry recovery resumes the attempt through the native P/N
gate. Packet receipt itself never enters the state. A damaged proxy also blocks
native entry before repair; protection may rebuild it within the same scan.

Host capture checks initialization, installation/removal boundaries, native idle
Type retention, snapshot/live publication independence and eight source failures
with recovery. Native Update 2 actions actively copy Wear, OilLevel and integer
DamageType to the part; Remove part copies wear and detaches it. The fixture
first proves those actions run, then verifies that guest admission prevents
copies from differing mount values and prevents detachment through disconnect.
Full physics creation, global assembly notifications, disk save/load and
physical transmission reconstruction are not executed.

Validation passes **2,154 Net tests** (37 new), **18 launcher tests** and
**1,497 isolated native checks** (48 new, preserving every earlier 1,449 passing
label and count). Core Debug/Release, both Net targets, probe and Launcher build
with zero warnings/errors. Native checks use Debug; Release is build-only. The
disposable game and launcher payloads match the tested files.
[Results and tested hashes](../build/gearbox-starter-input-smoke/result.json)
record the complete run and game process exit 0. Initial fixture failures came
from restoring an obsolete proxy wrapper after foreign-owner containment and
assuming repairs require a later scan; final checks restore the native owner and
exercise the actual entry guard. An initial launch used the preceding binary
because a copy step had the wrong working directory; it is excluded from results.

Two-player acceptance remains pending: use different saved transmission types,
join with protocol 154, test automatic P/N versus engaged gears and both manual
types while the guest drives, then repeat after repair, installation, late join,
resync and reconnect. Inspect the guest save after restarting. This slice only
supplies the starter's gearbox type; transmission ratios, other gearbox
condition/selector setup dependencies, head and remaining engine inputs,
guest-driving host wear and complete starting still need work. No personal
saves, live installation, release or mod version were changed.

### Guest engine block inputs (protocol 153, unreleased)

Add `--wintermp-engine-block-input-probe` to the full existing probe run. Copy
`build/engine-block-input-smoke/engine-block-input-probe.json` into the disposable
game root. It contains actual installed block-mount Data, Starter, Oil and Cooling
FSM definitions; `build/engine-block-authority-audit/part.json` records the native
VIN1010 part and nested graphs. Preserve the entire disposable probe output before
each run and use `-p:DeployToGame=false`.

The fixture executes the native Motor installed read/BoolTest, and Running's
every-frame read/BoolTest. The latter sees host removal and reaches Stall engine
on the next native update without a synthetic packet-triggered stall. Direct
literal object wrappers stay intact. Oil retains its strict Wear <1 comparison;
Cooling retains its actual Damaged branches (true→State 4, false→Water leak),
without interpreting the labels or inventing condition thresholds. Other engine
prerequisites and later physics/state effects remain controlled fixture boundaries.

Checks cover saved caches, unavailable/removed state, stale/conflicting updates,
independent wear/damage flags, cadence, foreign direct targets, twelve signature
failures/repairs, proxy reconstruction, shared Starter/Oil/Cooling observations
and disconnect restoration. Host capture waits for native initialization and the
Update/Idle boundaries; missing/foreign/invalid attachments publish unavailable
and recover. Join snapshots preserve pending live condition revisions.

The native block's Update #1 continuous wear-copy action is **disabled** in this
game build. The fixture retains that flag and confirms updates leave the saved
part untouched. Native Remove part #3 copies Wear and #9 detaches the owned fixture
assembly before protection. Guest admission then pauses mount Data; subsequent
removal and BREAKOFF attempts cannot write or detach the saved block, including
after disconnect. Full body creation, assembly database notifications, disk
load/save and physical block reconstruction are not executed.

Validation passes **2,117 Net tests** (41 new), **18 launcher tests** and
**1,449 isolated native checks** (53 new, preserving all 1,396 earlier passing
labels and counts). Core Debug/Release, both Net targets, the probe and Launcher
build with zero warnings/errors. The native run uses Debug; Release is build-only.
The disposable game and launcher payloads match the tested files.
[Results and tested hashes](../build/engine-block-input-smoke/result.json) record
zero failures and game process exit 0.

The first three focused runs exposed an incorrect expectation that the native
continuous wear action was enabled. The corrected focused run preserves the
actual disabled flag and validates enabled removal actions instead. The first
full run required two older Oil source-count assertions to include the new block
source. Native tests retain all earlier labels and conditions.

Two-player acceptance remains required: use different saved blocks, join with
protocol 153, remove/refit and damage/repair the host block, test starting and
running on the guest, repeat after late join/resync, and check the guest save
after restart. Physical block reconstruction, gearbox/head and other engine
inputs, guest-driving host wear, and complete starting remain separate work.
No personal saves, live installation, release or mod version were changed.

### Guest battery engine inputs (protocol 152, unreleased)

Add `--wintermp-battery-engine-input-probe` to the full existing probe run and
copy `build/battery-engine-input-smoke/battery-engine-input-probe.json` to the
disposable game root. The fixture contains the installed battery mount Data,
Starter and Electrics definitions. Preserve the entire disposable probe output
before each run and build with `-p:DeployToGame=false`.

The fixture executes actual Electrics GetFsmBool/GetFsmFloat reads, the Wiring
BoolAllTrue gate, native cold-temperature clamp/add/voltage comparison and native
voltage division. Missing/removed battery records supply zero inputs; charge
updates affect scratch only when the original native reads execute. It checks
cached saved references, delayed/stale/conflicting records, proxy destruction,
four signature failures and repair, and persistent protection after disconnect.

Host capture uses real battery Data fields and state names with controlled
attachment, readiness and part fields. Physical joint creation/installation and
disk loading are omitted. The actual Delay 2 drain and Died battery degradation
actions run once against owned fixture objects before guest admission, proving
native writes reach the saved part. Guest admission then pauses that Data graph;
direct state entry cannot resume drain, degradation or removal, including after
disconnect. All ten Starter/Electrics Charge/ChargeMax writes are retained in the
existing native write-protection fixture. The RPM-only fixture now obtains its
protected native actions from the catalog so new battery guards cannot turn its
partial Starter into an invalid graph.

Validation passes **2,076 Net tests** (36 new), **18 launcher tests** and
**1,396 isolated native checks** (39 new battery checks, preserving all 1,357
previous passing labels and counts). Core Debug/Release, both Net targets, the
probe and Launcher build with zero warnings/errors. Native checks use Debug;
Release is build-only. The launcher and disposable game payloads match the
current tested files. [Results and tested hashes](../build/battery-engine-input-smoke/result.json)
record the run. The first focused fixture needed large floating-point literals
kept as decimals and an explicit AssemblyID field; the first full run exposed
omitted guarded actions in the RPM fixture. The corrected full run has zero
failures and game process exit 0.

Two-player acceptance remains required with different saved battery condition:
join on protocol 152, fit/remove/deplete the host battery, check power decisions
and cold behavior from the guest, then repeat after late join/resync and verify
the guest save after restart. This is input/save protection coverage. Physical
battery installation/removal, guest-driver draw reaching the host, other
electrical writers, remaining block/gearbox/head dependencies and full starting
are separate work. No personal save, live installation or release was changed.

### Guest starter flywheel input (protocol 151, unreleased)

Add `--wintermp-starter-flywheel-engine-input-probe`, retaining every previous
flag and fixture. Copy
`build/starter-flywheel-engine-input-smoke/starter-flywheel-engine-input-probe.json`
into the disposable game root. It contains the installed Starter and Cylinders
FSM definitions. Build with `-p:DeployToGame=false` and preserve the disposable
probe output before each run.

The fixture executes the native Starter Check Flywheel GetFsmBool and BoolTest.
It observes Prepare starting versus No Flywheel and stops at those boundaries;
battery charge/draw, sound, engine enabling and the complete starting sequence
are not exercised. The existing starter wear writer remains protected. The
comparison deliberately imposes no flywheel wear or tightness threshold.

Checks cover cold state against a fitted saved flywheel, all four factory
variants, pending application, zero wear/tightness, removal, delayed fitted
packets, unchanged scratch until the next attempt, replica identity/visibility/
revision/ownership, wrong attachments, conflicting copies, four changed action
signatures, factory mount disagreement, proxy repair, moved engine blocks and
disconnect restoration. A second native Cylinders reader shares the same accepted
part while retaining independent scratch; both consumers agree across variant
replacement and removal. Saved mount data and shared object references are
checked throughout. Earlier starter fixtures now supply an explicit applied host
flywheel rather than reading an unrelated saved placeholder.

Two-player acceptance remains required: start with different saved flywheels,
join on protocol 151, install/remove each host flywheel or flexplate and attempt
to start from the guest. Repeat after late join/resync and confirm the guest's
original save survives disconnect. Battery state, block/gearbox prerequisites,
cylinder-head placement and full engine operation remain separate work.

Validation passes 2,040 Net tests (19 new), 18 launcher tests and 1,357 isolated
native checks (32 new, preserving all 1,325 earlier checks). Core Debug/Release,
both Net targets, the probe and Launcher build with zero warnings/errors. The
launcher payload matches the native-tested Debug files; Release is build-only.

[The local result record](../build/starter-flywheel-engine-input-smoke/result.json)
identifies the tested files and previous regressions retained.

### Guest engine wiring inputs (protocol 150, unreleased)

Add `--wintermp-wiring-engine-input-probe`, retaining all earlier flags and
fixtures. Copy `build/wiring-engine-input-smoke/wiring-engine-input-probe.json`
into the disposable game root. It contains the installed game's Cylinders,
Starter and Electrics graphs and eight native wiring Data definitions. Build with
`-p:DeployToGame=false` and preserve the disposable probe output before each run.
The source audit is `build/wiring-authority-audit/scene.json` (35 connections plus
wiring status, assembly, bolt, shock and fire graphs).

The wiring probe warms and replaces all thirteen actual GetFsmBool caches, then
checks absent state, independent Installed/Bolted combinations, late arrival,
stale updates, unchanged calculation scratch and saved Data. It runs the native
ignition decision, all eight starter terminal combinations and all four charging
connection combinations. The ground deliberately supplies Installed to Starter
and Bolted to Electrics. Earlier coil and engine fixtures now seed explicit host
wiring records rather than relying on saved wiring placeholders.

Host capture uses isolated copies of the eight Data graphs. Native initialization
runs before importing calculation actions; only Tightness?, Bolted and Unbolted
execute native actions. Disk load/save, cable activation, battery joints and fire
are omitted from this capture fixture. Tests explicitly enter the native load and
settled boundaries, execute native terminal comparisons, then verify availability,
revision changes, snapshot/live publication independence and source recovery.
Thus these checks establish the captured engine inputs, not complete electrical
assembly behavior or real save loading. Guest input checks do not mutate retained
saved wiring values. No test enters a personal world or writes personal saves.

Two-player acceptance remains required: use different saved wiring, connect both
players on protocol 150, and have the host disconnect/reconnect the ignition,
starter and charging wires. The guest must follow the host's start/charge decisions,
including terminal tightness, after late join/resync. Guest saved wiring must
survive disconnect and a fresh singleplayer load. Battery state, guest wiring
tools, cable/bolt visuals, shock/fire, cylinder-head placement and complete engine
operation remain separate work.

Validation passes 2,021 Net tests (41 new), 18 launcher tests and 1,325 isolated
native checks (47 new, preserving all 1,278 earlier checks). Core Debug/Release,
both Net targets, the probe and Launcher build with zero warnings/errors. The
launcher payload matches the native-tested Debug files; Release is build-only.

[Results and tested hashes](../build/wiring-engine-input-smoke/result.json) identify
the local unreleased payload and the isolated checks.

### Guest ignition-coil replicas and input (protocol 149, unreleased)

Add `--wintermp-ignition-coil-engine-input-probe`, retaining every earlier flag
and fixture. Copy `build/ignition-coil-engine-input-smoke/ignition-coil-engine-input-probe.json`
into the isolated game root. It combines the installed Cylinders consumer,
VIN212 prefab FSMs, native IgnitionCoil212 factory Spawn, vehicle mount Data
and separate WiringCoilHarness Data. The latter is audit evidence: the ignition
test controls wiring availability and does not claim host wiring replication.
Build with `-p:DeployToGame=false` and preserve the disposable save directory
before each run.

The fixture exercises all eight coil/distributor/wiring combinations through
native Ignition actions. Coil and distributor inputs use accepted/applied host
parts; the wiring boolean is a controlled prerequisite. The native gate requires
all three and has no coil wear/tightness threshold. Missing, pending, conflicting,
hidden or mismatched coils cannot borrow saved installation. Checks also cover
registered vehicle identity/attachment kind/path, moved cars, native reader
signature validation, cache repair and disconnect restoration. Saved guest mount
Data and ActivePart are checked throughout. Earlier fixtures now provide an
explicit healthy host coil wherever they previously controlled that prerequisite.

Factory checks validate the native template, both output signatures and fitting
mount/entry, then execute fresh creation and Init for two distinct IDs. Native
MinimumWear=95 produces condition in [95,99] and identity save keys ending WEA.
Serialized native Data then backs real guest materialization with host Wear=57.125,
overriding local random initialization. The resulting copy fits the registered
car, satisfies the native ignition gate and returns loose with the same Rigidbody
and host condition. Host condition capture advances gameplay revisions and its
packet/scalar replay preserves wear exactly. This does not exercise full native
host fitting/removal physics or physical mouse/tool input.

Two-player acceptance: host and guest start with different saved coils, join,
then have the host install/remove a coil while the guest observes. With matching
wiring/distributor prerequisites, the guest must follow the host's installation;
rejoin and check that the guest's original save remains intact. Wiring authority,
cylinder-head placement and complete engine operation remain separate work.

Validation: 1,278 native checks pass, including all 1,237 previous checks and
41 new coil cases. All 1,980 Net tests (19 new) and 18 launcher tests pass. Core
Debug/Release, both Net targets, probe and Launcher build without warnings/errors.
The launcher payload matches the native-tested Debug files; Release is build-only.
[Results, source evidence and hashes](../build/ignition-coil-engine-input-smoke/result.json)
remain local and unreleased.

### Guest rev-limiter inputs (protocol 148, unreleased)

Add `--wintermp-revlimiter-engine-input-probe`, retaining all earlier flags and
fixtures. Copy `build/revlimiter-engine-input-smoke/revlimiter-engine-input-probe.json`
to the isolated game root. It combines the installed Cylinders graph,
CORRIS/AssembliesTuning/VINP_Revlimiter Data, Fleetari factory Spawn and the
REVLIMITER0 prefab FSMs. Keep native property references bound to the actual
Revlimiter/RevlimitRPM scratch variables when importing Drivetrain.revLimiter
and maxRPM writes. Build with `-p:DeployToGame=false` and preserve the previous
disposable save directory before each run.

The probe exercises a registered vehicle body and native Limiter decisions,
including the absent-part early exit before maxRPM assignment. Host native knob
Calc/Knob actions run against separate host Data, checking forward/inverse
calculation, capture revisions and packet/scalar replay. Calc can produce nearly
10,000 RPM (Angle 265); Data Status only clamps to 9,500 during initialization.
Do not silently clamp a live host setting to that initialization limit. Numeric
calculation comparisons allow .002 RPM for single-precision intermediate rounding;
the captured, serialized and applied setting must remain exactly equal.

Serialized native Data backs real guest materialization, vehicle fitting and
removal checks, including the same Rigidbody and engine input. Other cases cover
pending/conflicting copies, registration loss, wrong attachment kind/id/path,
identity/revision/visibility failures, native reader signature validation, cache
repair and disconnect restoration. Saved guest mount values and ActivePart are
checked throughout. The isolated Drivetrain is disabled, so these checks do not
exercise road driving, full host fitting physics, the guest knob interaction or
automatic knob presentation. Test those with two players: host changes the limiter
while the guest observes/drives, then removes it and has the guest rejoin.

Validation: 1,237 native checks pass, preserving all 1,200 prior checks and adding
37 limiter cases. All 1,961 Net tests (26 new) and 18 launcher tests pass. Core
Debug/Release, both Net targets, probe and Launcher build with zero warnings/errors.
The launcher payload matches the native-tested Debug files. Release is build-only.
[Results, source evidence and hashes](../build/revlimiter-engine-input-smoke/result.json)
remain local and unreleased.

### Guest flywheel/flexplate inputs (protocol 147, unreleased)

Add `--wintermp-flywheel-engine-input-probe` after the earlier engine-input probes,
retaining their flags and fixture files. Copy
`build/flywheel-engine-input-smoke/flywheel-engine-input-probe.json` into the
isolated game root. It combines the installed Cylinders graph, shared mount,
four factory Spawn FSMs and actual VIN120/FLYWHEELa0/FLYWHEELb0/VIN138 Data
prefabs. The native default inertias are .1, .085, .072 and .11 respectively.
The complete spawner audit confirms these are the four factories using that
mount; none uses CreatePartsPackages. The existing 31 package profiles stay intact.

The fixture executes native Flywheel installation, inertia assignment and
.04/inertia shake calculations. Its controlled Drivetrain and MotorShake targets
are isolated from vehicle simulation; Ignition is the downstream boundary.
Accepted/applied replicas supply host inputs, including values different from
prefab defaults. Native fresh factories and Init run separately; serialized
native Data actions then back actual MaterializeReplacement calls. The resulting
guest copies keep host identity/inertia and the same Rigidbody across fitted and
loose presentation. Both fitting and removal bindings are validated. The imported
Unbolted action retains the game's MeshCollider property metadata with its actual
BoxCollider reference. This is not a full host physics or player-control test.

The probe retains every earlier check. It exercises absence/pending state,
conflicting stock/aftermarket attachments, moved block mounts, stale identity and
revision, changed reader signatures, proxy cache repair and disconnect restoration.
Protocol tests additionally reject nonpositive/nonfinite inertia and values that
overflow the native division, preserving the last accepted state. Saved mount
values and ActivePart references are checked throughout. Results and payload
hashes are kept in [result.json](../build/flywheel-engine-input-smoke/result.json).
All 1,935 Net tests, 18 launcher tests and 1,200 native checks pass, including
40 new Net and 53 new native checks. All 1,147 previous native cases are retained.
Core Debug/Release, both Net targets, probe and Launcher build cleanly; the
launcher payload matches the native-tested Debug files. Release is build-only.
Build with `-p:DeployToGame=false`. Live two-player acceptance, complete engine
operation, host wear during guest driving and cylinder-head assembly work remain.

### Guest radiator-fan power and cooling inputs (protocol 142, unreleased)

Add `--wintermp-radiator-fan-engine-input-probe` and
`radiator-fan-engine-input-probe.json`, retaining every earlier flag and fixture.
The JSON contains the actual Valves and Cooling consumers; the
[native audit](../build/radiator-fan-engine-input-smoke/native-audit.json) also
retained VIN127 Data/Paint, which the later live audit identified as the wrong
part family. Protocol 210 uses VIN137 and RadiatorFan137; retain its actual Spawn
and prefab Data records in `native-bag-part-probe.json`. The fan and pulley
mounts must be distinct in fixtures. Current validation and live evidence are in
`docs/PERFORMANCE.md` and `build/engine-admission-audit/`. Normalize
numeric exponent tokens to decimal for the legacy JSON reader, without changing
quoted strings or action parameters.

The 38 added checks cover warmed saved caches, absent/pending/applied receipts,
every fan/belt fit combination, revision/identity/parent/ownership failures,
inactive replicas, competing pending copies, nested mount movement, independent
consumer proxies, reader validation/repair, selective cache rebuild and disconnect
restoration. Native Valves adds 0.13 PowerAdd with no belt, 0.09 with a belt but
no fan, and zero with both. Native Cooling first scales the existing fan rate by
WaterLevel, then replaces it with RPM/CoolingFanModifier only if both parts are
fitted. Each case controls the initial rate, water level, modifier and native
RPM action input; it stops at Carburettor/Flect boundaries. All saved part Data,
ActivePart and protected writer targets remain untouched. Earlier belt checks
now receive an explicit accepted/applied host fan prerequisite.

All 1,824 Net tests, 18 launcher tests and 958 native checks pass (16 new catalog cases; all 920
earlier native checks preserved). [Results and hashes](../build/radiator-fan-engine-input-smoke/result.json)
and [native report](../build/radiator-fan-engine-input-smoke/report.txt) distinguish
the isolated Debug payload from separate Release/build validation. Net, Core
Debug/Release, the probe and launcher built with zero warnings/errors. Continue to
use `-p:DeployToGame=false`; the probe is never shipped. Controlled bindings and
application receipts do not establish actual two-player fitting, complete engine
operation, overheating or fan presentation. Changes remain local and unreleased.

### Guest piston combustion and smoke inputs (protocol 141, unreleased)

Add `--wintermp-piston-engine-input-probe` and `piston-engine-input-probe.json`,
retaining every earlier flag and fixture. The JSON is also required by earlier
probes that create Cylinders, because the four piston sources are mandatory.
It contains the native Cylinders/Mixture graphs and VIN103 Data defaults. The
[native audit](../build/piston-engine-input-smoke/native-audit.json) also retains
VIN103 Wear, Spawn, all four mounts and the actual Pistons array.

The 50 added checks cover eight independent sources, warm caches, absent/pending/
applied state, shared scratch ownership, native firing/efficiency/smoke boundaries,
per-slot removal/refit, wrong assembly/identity/family/revision/parent, competing
attachments, invalid slot tables, consumer-scoped reader failure/recovery, pivot
movement, cache replacement, per-slot host capture/revision/replay and disconnect
restoration. Each cylinder runs at wear 9.999/10/10.001/90. Native Reset reads,
cylinder installation/comparison gates, efficiency addition/division and firing
flags run unchanged; Mixture's native Pistons comparisons choose Oil smoke below
10 and Wait 2 otherwise. Saved oil and contamination writes remain disabled.

Spark-plug condition and BaseEfficiency are controlled prerequisites, with
accepted/applied host rockers. Add-to-power states stop before spark-plug leak/
misfire transitions, and smoke rendering plus downstream mixture behavior are
omitted. Earlier rocker tests now supply accepted/applied pistons. The shared
fixture supports Pistons, Rockers and MainBearings in one native AssemblyDatabase.
This does not establish full engine operation or host wear while a guest drives.

All 1,808 Net tests, 18 launcher tests and 920 native checks pass (31 new catalog
cases; all 870 earlier native checks preserved). Net, Core Debug/Release, probe
and launcher builds have zero warnings/errors. Keep `-p:DeployToGame=false` on
in-game builds. [Results and hashes](../build/piston-engine-input-smoke/result.json)
and [native report](../build/piston-engine-input-smoke/report.txt) distinguish
the tested Debug payload from the separately built Release. No installer or
release was made.

### Guest main-bearing oil-pressure inputs (protocol 140, unreleased)

Add `--wintermp-bearing-engine-input-probe` and `bearing-engine-input-probe.json`,
retaining every earlier flag and fixture. The bearing JSON is also required by
earlier probes that create Wearing, because its five new sources are mandatory.
The new checks use the existing `powertrain-engine-input-probe.json` Wearing
graph. [Native evidence](../build/bearing-engine-input-smoke/native-audit.json)
includes that graph, VIN104 Data/Wear prefabs, Spawn, all five mounts and the
native MainBearings array.

The 44 added checks cover five independent warmed caches, absent/pending/applied
state, shared Condition scratch ownership, per-slot removal/refit, wrong assembly
indices and array families, stale revisions, moved/hidden replicas, competing
attachments, malformed native slot tables, changed template/readers, block
movement, selective cache rebuild, per-slot host capture/revision/replay and
disconnect restoration. Wearing's native accumulation/division/clamp runs with
distinct host crank/bearing values at RPM 0/800/6400/8000, plus a maximum-clamp
case. Its native final SetFsmFloat writes to a controlled Pressure FSM with the
real PressureLeak field. The separate downstream pressure/flow graph is not run.

The fixture generalizes the existing native-array setup so rockers and bearings
can coexist in the same AssemblyDatabase. Earlier powertrain checks now use
accepted/applied bearings with controlled wear instead of saved-mount stand-ins.
Neither fixture injection nor scalar capture establishes actual host wear
progression during guest driving or two-player acceptance.

All 1,777 Net tests, 18 launcher tests and 870 native checks pass (23 new catalog
cases; all 826 earlier native checks preserved). Net, Core Debug/Release, probe
and launcher builds have zero warnings/errors. Keep `-p:DeployToGame=false` on
in-game builds. [Results and hashes](../build/bearing-engine-input-smoke/result.json)
and [native report](../build/bearing-engine-input-smoke/report.txt) distinguish
the tested Debug payload from the separately built Release. No installer or
release was made.

### Guest head gasket, thermostat and oil-filter inputs (protocol 139, unreleased)

Add `--wintermp-fluid-engine-input-probe` and `fluid-engine-input-probe.json` to
the isolated suite, retaining every earlier flag and fixture. The
[native audit](../build/fluid-engine-input-smoke/native-audit.json) includes
Cylinders/Oil/Cooling and each family's factory, Data and mount. VIN134, VIN129
and VIN128 retain Wear/Tightness; OILFILTR0 retains Dirt/Tightness.

The 99 added checks cover five sources: warm caches, absent/pending/applied state,
competing attachments, identity/parent/revision/presentation changes, native-reader
repair, host scalar capture/revision/replay, scratch ownership, destroyed-cache
recovery and disconnect restoration. Gasket wear runs at .999/1/1.001. Thermostat
wear runs at 6.999/7/7.001/14.999/15/15.001 with temperature below/equal/above a
controlled opening threshold, plus the absent-part open branch. Housing tightness
runs at 15.999/16/16.001; oil-filter tightness runs at 0/4/8 and dirt at
99.999/100/100.001. Latest accepted tightening receipts override older snapshot
tightness at the engine-input boundary.

The fixture executes the actual native comparisons, oil-filter leak arithmetic,
filter-rate assignment and closed-thermostat circulation assignment. Cooling
stops at Water Pump 2, Pump tightness or State 3 as appropriate; oil-leak animation
output and physical gasket failure effects are omitted. Other parts are explicit
controlled prerequisites. This does not establish full fluid-system operation,
host wear during guest driving or two-player acceptance.

All 1,754 Net tests, 18 launcher tests and 826 native checks pass (23 new catalog
cases; all 727 earlier native checks preserved). Net, Core Debug/Release, probe
and launcher builds have zero warnings/errors. Keep `-p:DeployToGame=false` on
in-game builds. [Results and hashes](../build/fluid-engine-input-smoke/result.json)
and [native report](../build/fluid-engine-input-smoke/report.txt) distinguish the
tested Debug payload from the separately built Release. No installer or release
was made.

### Guest crankshaft and auxiliary-drive inputs (protocol 138, unreleased)

Add `--wintermp-powertrain-engine-input-probe` and
`powertrain-engine-input-probe.json`, retaining every earlier flag and fixture.
[The audit](../build/powertrain-engine-input-smoke/native-audit.json) includes
Cylinders/Oil/Wearing/FuelLine plus native Spawn, Data and mounts for VIN102,
VIN105, VIN109 and VIN110. Existing Wear/Tightness arrays are unchanged.

The 90 added checks cover seven sources and eight native reads: warm caches,
missing/pending/applied states, installation/removal/refit, competing copies,
changed identities/parents/readers, host capture and revisions, scratch ownership,
cache rebuilding and disconnect cleanup. Native threshold boundaries are checked
at .999/1/1.001 for crank failure, 4.999/5/5.001 for Oil's shake branch, and
1.999/2/2.001 for auxiliary-shaft fuel delivery. The pressure graph accumulates
actual host crank condition alongside five controlled bearing inputs, then runs
the native division and RPM-based clamp. Its final output to the separate Pressure
FSM is omitted in this fixture. Failure branches stop before physical side effects.

Companion shafts, belts, cam and fuel-pump prerequisites are explicitly accepted
and applied where needed; cylinder-head and bearing prerequisites remain
controlled fixture data. No full engine/pressure-system operation or live-driving
acceptance is claimed. All 1,731 Net tests, 18 launcher tests and 727 native checks
pass (25 new catalog cases; all 637 earlier native checks preserved). Net, Core
Debug/Release, probe and launcher builds have zero warnings/errors, using
`-p:DeployToGame=false` for in-game builds. [Results and hashes](../build/powertrain-engine-input-smoke/result.json)
and [native report](../build/powertrain-engine-input-smoke/report.txt) identify the
tested Debug payload and separately built Release.

### Guest timing-belt combustion inputs (protocol 137, unreleased)

Add `--wintermp-timingbelt-engine-input-probe` and
`timingbelt-engine-input-probe.json` to the isolated suite, retaining every prior
flag and fixture. [The audit](../build/timingbelt-engine-input-smoke/native-audit.json)
contains the actual Cylinders graph, VIN107 Data/mount and TimingBelt107 Spawn.

The 25 added native checks cover installation/removal/refit; pending, conflicting,
hidden and mismatched replicas; Wear .999/1/1.001 crossed with host cam tolerance
.999/1/1.001; unchanged host capture/scalar order; native wear arithmetic and
suppressed saved-part writes; reader failure/recovery, cache replacement and
cleanup. The companion cam is an accepted applied host state. Other powertrain
prerequisites are controlled fixture inputs. Exported RPM placeholders share one
controlled value for both wear calculations, as the native global does.
The isolated failure graph exercises the original comparisons and transitions;
its outgoing PartBreakages event is omitted. Existing damage-protection checks
remain in the full suite; this is not physical failure or live-driving acceptance.

All 1,706 Net tests, 18 launcher tests and 637 native checks pass (13 new catalog
cases, all 612 earlier native checks preserved). Net, Core Debug/Release, probe
and launcher builds have no warnings/errors, with `-p:DeployToGame=false` on
in-game builds. [Results and hashes](../build/timingbelt-engine-input-smoke/result.json)
and [native report](../build/timingbelt-engine-input-smoke/report.txt) identify the
tested Debug payload and separately built Release. TimingData/RotateEngine,
remaining engine inputs, host wear under guest driving and live two-player
acceptance remain open.

### Guest fan-belt inputs (protocol 136, unreleased)

Add `--wintermp-fanbelt-engine-input-probe` and
`fanbelt-engine-input-probe.json` to the isolated suite. Keep every earlier flag
and fixture. The new JSON contains the audited Oil, Valves, Cooling and Electrics
native graphs; FANBELT0's factory and Data evidence are retained in
[the native audit](../build/fanbelt-engine-input-smoke/native-audit.json).

The 56 added checks cover all six native reads, absent/pending/applied/removal/refit
states, native load/power arithmetic, circulation, fan cooling and both electrical
gates. Companion host pumps/alternators are explicitly accepted and applied;
radiator-fan and wiring prerequisites are controlled fixture inputs. The appearance
receipt checks explicitly disable display to verify its independence from engine
readiness. Duplicate receipts cannot bypass pending repair, revision, identity,
body or parent checks. Native reader signature failure, cache rebuilding,
disconnect and saved-original restoration are included.

All 1,693 Net tests, 18 launcher tests and 612 native checks pass (17 new catalog
cases and all 556 earlier native checks preserved). Net, Core Debug/Release,
probe and launcher builds have zero warnings/errors; every in-game build uses
`-p:DeployToGame=false`. [Results and tested hashes](../build/fanbelt-engine-input-smoke/result.json)
and [native report](../build/fanbelt-engine-input-smoke/report.txt) distinguish the
tested Debug payload from the separately built Release. Preserve/move the isolated
`guest-save-probe` output folder before each run: native rename checks require a
fresh fixture directory. These checks do not establish full engine operation or
two-player acceptance. TimingData/RotateEngine, timing belt, radiator-fan
installation, battery/wiring and host wear while guests drive remain separate work.

### Guest alternator electrical inputs (protocol 135, unreleased)

Add `--wintermp-alternator-electrical-input-probe` and
`alternator-electrical-input-probe.json` to the isolated suite. Keep
`DeployToGame=false` and the separate game/Wine profile. Native Electrics/Oil
definitions supply the real readers and calculations.

`GuestEngineInputChecks.Electrical.cs` tests all seven warmed reader caches,
damage/repair for both variants at charging and running checkpoints, native
durability/voltage/charging math, prerequisite gates, pending/conflicting or
misassigned state, repeated-reader failure containment, proxy rebuild and cleanup.
`.ElectricalCapture.cs` tests actual host part/mount publication, packet round-trip,
revision changes, unresolved fitting, missing bools, mismatched factory mounts and
ambiguous Data. Guest saved Data and original wear targets remain intact.
All 1,676 Net tests, 18 launcher tests and 556 native checks pass: 29 new Net cases and 26 electrical
checks, preserving all 530 earlier native checks.
[Results and tested hashes](../build/alternator-electrical-input-smoke/result.json)
and [native report](../build/alternator-electrical-input-smoke/report.txt) record
the controlled Debug run and matching launcher payload. Net, Core Debug/Release,
probe and launcher builds have no warnings or errors. Release was built separately
and was not native-tested.

The fixture controls applied replica readiness, battery/belt/wiring eligibility,
RPM and temperature. It does not prove complete electrical energy integration,
host wear progression under guest driving or two-player acceptance. Test stock
and upgraded alternators with different guest saves, damage/repair at constant
wear, fit/remove/swap, delayed state, running replacement and reconnect. Confirm
both electrical damage checkpoints follow host state and saved originals survive.

### Guest alternator mechanical inputs (protocol 134, unreleased)

Add `--wintermp-alternator-mechanical-input-probe` and
`alternator-mechanical-input-probe.json` to the existing isolated suite. The file
contains native Oil/Wearing definitions; the shared fixture supplies all three
Oil sources. Keep `DeployToGame=false` and the isolated game/Wine profile.

`GuestEngineInputChecks.Alternator.cs` tests warmed native caches, absent/pending
parts, stock/upgraded swaps, actual host publication and replica scalar application,
wear immediately below/at/above 5, native seizure/current arithmetic, conflicting
attachments, applied identity/revision/parent gates, moved factory references,
three-source scratch isolation, scoped reader recovery, proxy rebuild and cleanup.
All 1,647 Net tests, 18 launcher tests and 530 native checks pass: 17 new catalog cases and 21 new
alternator checks, preserving all 509 earlier native checks.
[Results and tested hashes](../build/alternator-mechanical-input-smoke/result.json)
and [native report](../build/alternator-mechanical-input-smoke/report.txt) record
the controlled Debug run and matching launcher payload. Net, Core Debug/Release,
probe and launcher builds have no warnings or errors. Release was built separately
and was not native-tested.

Applied replica readiness and the Amperes input are controlled in the fixture.
This does not establish full charging, belt/wiring integration, alternator
materialization, native hand rotation or two-player acceptance. Test both variants
with differing guest saves, fit/remove/swap, loosen/rotate/retighten, delayed state,
reconnect and native seizure/repair. Electrical Efficiency/Durability and the
mount-owned Damaged flag still require host projection; guest-driving host wear
and full engine behavior remain unfinished.

### Guest rocker inputs (protocol 133, unreleased)

Add `--wintermp-rocker-engine-input-probe` and `rocker-engine-input-probe.json`
to the existing isolated suite. That file contains actual VIN117 Data and the
native Rockers array ordering. The shared fixture now supplies all eight native
slot sources when testing any Cylinders consumer, retaining the database global
for restoration on disposal. Keep `DeployToGame=false` and use the isolated
game/Wine profile only.

`GuestEngineInputChecks.Rockers.cs` compares actual native Tightness?/Bolted/
Unbolted actions with the projected threshold, exercises every cylinder's two
rocker gates, pending/removal, newer bolt receipts, wrong assembly/array/parent,
conflicting attachments, head movement, malformed/duplicate slot tables,
native-threshold changes, scoped reader recovery, proxy rebuild and cleanup.
All 1,630 Net tests, 18 launcher tests and 509 isolated native checks pass, preserving all 477 prior
checks and adding 32 rocker checks. Thirty-three new Net cases validate slot
metadata, threshold boundaries and unavailable/misassigned parts.
[Results and tested hashes](../build/rocker-engine-input-smoke/result.json) and
[native report](../build/rocker-engine-input-smoke/report.txt) record the Debug run
and matching launcher payload. Net, Core Debug/Release, probe and launcher builds
have no warnings or errors; Release was built separately and was not native-tested.

The controlled fixture provides applied replica readiness and unrelated piston/
spark-plug inputs. These checks do not establish full engine operation, native
rocker materialization, all slot-install behavior or two-player acceptance. Test
different saved rocker/bolt states, moving the head, guest tightening/removal,
delayed slot attachments and reconnect while confirming saved-original preservation.
Host wear while guests drive and other engine dependencies remain unfinished.

### Guest camshaft inputs (protocol 132, unreleased)

All stock/tuned cams publish Durability, ValveTolerance and actual CamProfile.
The isolated test adds `--wintermp-camshaft-engine-input-probe` and
`camshaft-engine-input-probe.json` to the existing suite. That JSON contains the
audited full native Cylinders, Wearing and Valves graphs. Use the existing
`DeployToGame=false` build workflow and isolated game/Wine profile only.

`GuestEngineInputChecks.Camshaft.cs` runs six real native reader slots, profile
substring/int/float conversions, wear and broken-belt thresholds, valve tolerance,
all four upgrades, pending/conflicting attachments, nested head movement, host
publication/packet round-trip/owned replica application, signature recovery,
selective proxy rebuild, removal, disconnect and saved-target cache restoration.
Shared fixtures now retain the actual native root and relative nested mount for
each source. Valve screw values and unrelated powertrain dependencies remain
controlled; these checks do not test full engine operation or cam materialization.

Validation passes 1,597 Net tests, 18 launcher tests and 477 isolated native checks, preserving all
436 prior native checks and adding 41 camshaft checks. Thirty-four new Net cases
cover the three required consumers, every factory variant, string format,
publication/copying and gameplay ordering. [Results and tested hashes](../build/camshaft-engine-input-smoke/result.json)
and [native report](../build/camshaft-engine-input-smoke/report.txt) record the
Debug run. Both Net targets, Core Debug/Release, the probe and Launcher Debug
build without warnings/errors. Release Core is built separately; the launcher
payload matches the native-tested Debug files.

Two-player acceptance still needs different saved cams and valve settings,
stock/upgraded swaps, broken belts, worn cams, delayed head attachment/rejoin,
guest driving and saved-original preservation. Broader engine inputs and host
wear progression remain unfinished. Changes are local and unreleased.

### Guest oil-pump inputs and shared consumers (protocol 131, unreleased)

VIN132 appends actual part Durability to state 185. Oil and Wearing now combine
multiple independent part inputs in one native FSM. Each source owns a separate
proxy and retains the applied-revision, identity and attachment gates. Native
entry resumes only after all sources validate; one broken source pauses that
consumer while other consumers continue. Shared calculation locals remain native.

Add `--wintermp-oilpump-engine-input-probe` and `oilpump-engine-input-probe.json`
to the isolated suite. Shared fixtures now provide complete source profiles for
Oil/Wearing, including the pre-existing water/fuel pump cases. Checks cover warmed
reads, pending/removal/refit, shared scratch, starvation at Wear < 13, native
Durability arithmetic, actual host publication/packet round-trip, first/second
source failures, blocked native entry, selective proxy rebuild, cross-consumer
recovery, disconnect and partial cleanup with retained protection. Applied replica
readiness and non-pump wear inputs remain controlled. Use `DeployToGame=false` and
the isolated game/profile only.

Two-player acceptance still needs differing saved oil pumps, running with removed
or worn pumps, delayed attachment/resync, guest driving and preserved guest saves.
These checks do not run the full engine or retest pump materialization. Remaining
engine inputs and host wear progression while a guest drives are still open.

Validation passes 1,563 Net tests, 18 launcher tests and 436 isolated native
checks, preserving all 409 prior checks and adding 27 oil-pump checks. Twelve new
catalog cases cover the added sources and cross-source collisions. Both Net
targets, Core Debug/Release, the probe and Launcher Debug build without warnings
or errors. Native execution uses Debug; Release Core was built separately.
[Native results and hashes](../build/oilpump-engine-input-smoke/result.json) and
[report](../build/oilpump-engine-input-smoke/report.txt) document zero failures,
Wine exit 0 and matching launcher payload. Additional fixture Data now defers Awake
until its identity variables exist, and explicit readiness assertions verify native
initialization and attachment. No installer or release was made.

### Guest stock/racing fuel-pump inputs (protocol 130, unreleased)

VIN125 and FUELPUMP0 append actual Durability/OutputRate to state 185. The native
FuelLine pump reads and Wearing durability read use independent inert proxies for
either accepted factory at the shared mount. Both factories must bind correctly;
exactly one accepted, applied, owned replica can supply input. Both readers remain
neutral during conflicts, pending materialization, removal and disconnect.

Add `--wintermp-fuelpump-engine-input-probe` and `fuelpump-engine-input-probe.json`
to the isolated suite. `GuestEngineInputChecks.cs` contains the scenarios;
`GuestEngineInputChecks.Fixture.cs` holds the shared disposable fixtures. Checks
cover warmed reads, native installation/shutdown, starvation thresholds, stock and
racing capacity, durability math, real host publication/packet round-trip, pending
variant conflicts, swaps in both directions, changed factory references, identity
recovery, block movement, simultaneous consumers and restored native caches.
Applied readiness and non-pump power/wear inputs are controlled; this does not run
the entire fuel system or retest factory materialization. Use `DeployToGame=false`
and the isolated game/profile only.

Two-player acceptance still needs different saved pumps, stock/racing swaps,
delayed snapshots, guest driving, disconnect/rejoin and preserved guest saves.
Native starvation is Wear < 5; capacity is Power > PumpRate. Other fuel and engine
inputs, and host wear progression while a guest drives, remain unfinished.

Validation passes 1,551 Net tests, 18 launcher tests and 409 isolated native
checks, preserving all 378 prior checks and adding 31 fuel-pump checks. Twenty-three
new catalog cases cover both variants and their required consumers. Both Net
targets, Core Debug/Release, the probe and Launcher Debug build with zero warnings
or errors. Native execution uses Debug; Release Core was built separately.
[Native results and hashes](../build/fuelpump-engine-input-smoke/result.json) and
[report](../build/fuelpump-engine-input-smoke/report.txt) document zero failures,
Wine exit 0 and a matching launcher payload. No installer or release was made.

### Guest water-pump engine inputs (protocol 129, unreleased)

VIN126 appends actual part Durability and Efficiency to state 185. Seven native
Oil/Cooling readers use independent inert proxies, preserving saved mount/part
Data and native write protection. Both require the current applied host identity
and unique live attachment. New state waits for ordinary native reads.

Add `--wintermp-waterpump-engine-input-probe` and
`waterpump-engine-input-probe.json` to the existing isolated native suite. It tests
warmed native caches, pending/removal/refit/disconnect, wear thresholds, durability
arithmetic, circulation efficiency, native belt/RPM gates, bolt-receipt leak math,
real host publication/packet round-trip, reader repair, cleanup and simultaneous
consumers with independent failure containment. As with the earlier input probes,
applied replica readiness is controlled; this does not retest pump materialization.
Use `DeployToGame=false` and the disposable game/profile only.

Two-player acceptance still needs different host/guest pump condition, removal and
refitting, guest driving, delayed attachment/resync, block movement and preserved
guest saves. Oil seizes at Wear <= 5; Cooling closes below Wear 7. Its tightness
threshold is 32 even though mount TightnessMax is 24; the fixture deliberately
checks this native behavior. Other cooling and engine inputs remain unfinished.

Validation passes 1,528 Net tests, 18 launcher tests and 378 isolated native
checks: all 351 prior checks plus 27 water-pump checks. Fourteen new catalog cases
cover both required pump consumers and appended fields. Net, Core Debug/Release,
the probe and Launcher Debug build without warnings/errors. The controlled native
run uses Debug; Release Core was built separately. Changes remain local and
unreleased.
[Native results and tested hashes](../build/waterpump-engine-input-smoke/result.json)
and [report](../build/waterpump-engine-input-smoke/report.txt) document zero failures,
Wine exit 0 and a matching launcher payload. The final fixture supplies the native
RPM comparison input directly, since RPM is global rather than local to Cooling.

### Guest starter engine inputs (protocol 128, unreleased)

The starter family VIN130 appends Durability to its Wear/Tightness state 185
scalars. The actual part supplies this value, matching the native mount's Install 2
copy from ActivePart. The guest Starter graph reads Installed, Wear and Durability
from an owned inert proxy, preserving saved mount/part data and existing write
protection. Only a current applied replica at the unique matching attachment can
supply an installed starter. Removal, pending state and disconnect close that gate.

Add `--wintermp-starter-engine-input-probe` and `starter-engine-input-probe.json`
to the existing isolated native suite. It runs real warmed GetFsm readers, Wiring
and Starter damage branches, and durability arithmetic against different saved
and host values. Applied replica readiness is controlled by the fixture. The
independent RPM fixture scopes out part projection because it omits native part
factories/readers; its original producer and protection checks remain active.

Two-player acceptance needs healthy versus worn starters on different saves,
replacement/removal while starting, delayed join/attachment, normal restart after
refitting, disconnect/rejoin and saved-part verification. The native healthy branch
requires Wear > 25. Inputs update at normal native reader entries, without changing
shared scratch during an existing attempt or injecting ignition replay. Remaining
wiring, battery, block/flywheel/gearbox and combustion dependencies still prevent a
claim of complete guest starting or engine behavior. Use `DeployToGame=false` and
the disposable game/profile only.

Validation passes **1,514 Net tests**, **18 launcher tests**, and **351 isolated
native checks**: all 334 previous checks plus 17 starter checks. Nine new catalog
cases validate the added profile. Both Net targets, Core Debug/Release, the probe
and Launcher Debug build with zero warnings/errors. The [native result and exact
tested hashes](../build/starter-engine-input-smoke/result.json) and
[report](../build/starter-engine-input-smoke/report.txt) document the Debug run,
zero failures and Wine exit 0. The launcher stages that same tested payload;
Release Core was built separately. No installer or release was made.

### Guest distributor ignition timing (protocol 125, unreleased)

VIN131 now has a separate distributor timing profile. With an empty hand and the
distributor bolt below Tightness 8, aim at its root pick sphere within one metre
and scroll. Each accepted request changes SparkAngle by a native 0.2-degree step,
clamped to 0–20, with the native 0.01-second cooldown. Native initialization picks
any float from 1–19; saved values are preserved rather than rounded to a step grid.
Wheel down increases timing and wheel up decreases it, matching native input.

Operations 2/3 in PartFitRequest/Receipt use the shared receipt ledger and the
distributor policy selected by the host's catalogued family. The host checks the
observed revision, native mount ownership, fresh nearby player, loosened bolt and
control readiness. It seeds the child mesh from authoritative Data before native
GetRotation, executes Clockwise/Counterwise and Wait, and verifies that saved
Data.SparkAngle, mount SparkAngle and mesh local Z agree. State 185 carries the
absolute result; stale or duplicate requests cannot add another turn.
The scroll prompt also waits until the latest accepted revision has fully applied
to the visible part. Delayed materialization cannot combine a new revision with an
old pose or tightness. Overlapping timing/removal picks preserve right-click
removal, while the distributor's box prevents selecting controls behind it.

Native guest HandRotate remains disabled. Presentation rotates only the owned
Pivot/mesh child, leaving its parent's authored -10-degree offset intact. Fresh
loose meshes keep their native +10-degree pose; fitting applies SparkAngle and
removal retains the resolved angle. Existing alternator controls and all 32
replacement factory identities remain unchanged.

Protocol-125 validation passed **1,328 protocol/catalog/policy tests**, including
63 distributor catalog cases and 23 distributor policy cases, plus **18 launcher
tests**. The final combined native suite passed **220/220 checks**, adding **43
distributor checks** to the previous 177, with zero failures and Wine exit 0.
Both Net targets, Core (Debug/Release) and GuestSaveProbe built with **zero
warnings/errors**. The [native result and hash record](../build/distributor-timing-smoke/result.json)
matches the final Debug Core/Net/probe/catalog outputs; Release Core was built but
not native-tested. The launcher test build reported the expected optional FastBoot
Release payload warning. These checks do not establish actual two-player scroll,
live save/rejoin behavior or operational guest engine effects.

Add `--wintermp-distributor-timing-probe` to the isolated GuestSaveProbe run with
`--wintermp-guest-save-probe` and the existing fixture flags. Supply
`distributor-timing-probe.json` containing installed build 23268598's VIN131
Data/HandRotate and VINP_Distributor Data evidence; retain the root SphereCollider
and child-transform evidence alongside it. The fixture imports native turn/pose
actions and checks gates, fractional settings, changed bindings and presentation
lifecycle. Record results and tested binary/evidence hashes in
`build/distributor-timing-smoke/result.json`. Use an isolated game copy and Wine
profile with `DeployToGame=false`; fixture inputs and game assemblies are not
release files.

| Two-player case | Expected result |
|---|---|
| Fit a distributor with a fractional saved SparkAngle; loosen the bolt and scroll both directions | Wheel down increases and wheel up decreases in native 0.2-degree steps; the fractional baseline, saved part, host engine mount and guest mesh agree |
| Reach 0/20, tighten the bolt to 8, hold an item/tool or aim beyond 1 m | Bounds and native bolt/tool/proximity gates refuse invalid input |
| Host and guest scroll together; repeat/delay a receipt or remove/refit during a request | Each accepted request turns once; stale revisions and changed attachments do not mutate the part |
| Delay applying a newer part state | Stale views offer no scroll input until the latest revision finishes applying |
| Aim through the distributor's removal box outside its timing sphere | The box blocks another control behind it; right-click removal remains available when allowed |
| Observe a fresh loose distributor, fit it, then remove it | Fresh native pose is preserved; fitted angle applies to the child mesh and remains after removal; root/Pivot transforms stay unchanged |
| Late join/resync, disconnect/rejoin and host save/reload | Authoritative SparkAngle and fitted pose return; guest saved originals remain intact |
| Repeat alternator adjustment after distributor use | Alternators keep their existing half-degree steps, 0–7 bounds and bolt gates |

Engine response to host timing changes while a guest drives still requires a
separate acceptance run; scalar and pose agreement alone does not prove it.

### Guest part/mount isolation (shipped in 0.1.33; no protocol change)

Saved originals from the 30 catalogued boxed replacement families are parked in an
inactive scene group before guest item/FSM registration. Their stable identities
can then belong to host-created copies, including when both saves use the same ID.
Native part and mount graphs retain their original variables/references; owned
copies receive the host's revisioned attachment/scalars. Pending local originals
cannot consume host item transforms or bolt/part updates while their factory loads.
An unsettled mount defers until its Part/Mpoint/Installed values agree with the
actual fitted hierarchy. Unknown nested assemblies or other registered subsystems
are preserved and disable only that replacement factory.

Fitting previews accept paused known mounts and consult host attachment occupancy,
including reports whose copies are still pending. Native authority checks on the
host are unchanged. Passive PartState views retain scratch Installed/checksum
observations without executing native guest transitions; owned copies use ordered
tightness receipts and replacement wear. Missing views request host state and
retirement clears them. Original mount Part/Installed references stay intact for
restoration; connecting those references to an operational guest engine is still
separate work.

On disconnect, owned copies detach and are removed before saved originals return.
Parent-relative pose, scale, active state, body settings/velocity and FSM enable/
restart flags are restored. Enabling the object before restoring its paused FSMs
avoids rerunning native initialization. Repeated restoration is inert. If an
original parent disappeared, its orphan stays inactive until scene teardown rather
than reappearing at the scene root. The existing save guard remains armed until
restart, so this is not a supported return to personal singleplayer in the same run.

Validation: **1,014** protocol/catalog/policy tests pass; Core and Net build cleanly.
Static build-23268598 evidence verifies all **30** replacement prefabs have exactly
one Data FSM at their root and no nested part/mount Data. Occupancy tests cover
pending copies, exact parent/slot identity, relocation, stale replay, removal,
retirement, conflicting in-flight occupants and reconnect clearing.

The opt-in `tools/GuestSaveProbe` also accepts `--wintermp-part-isolation-probe`
alongside `--wintermp-guest-save-probe`, in the same isolated copy/profile described
below. It uses disposable Unity objects with active PlayMaker entry counters to
check parking, accidental activation, restoration without initialization replay,
physics/relative pose, repeated restoration/rejoin, and a vanished original mount.
All **7** restoration checks and **21** save-library checks passed in Unity/Wine
against build 23268598, with active FSM entry counters and zero failures. The
local report and tested assembly hashes are in `build/part-isolation-smoke/result.json`;
the tested Core, Net and probe assemblies match the current build outputs.
This controlled probe does not replace the following native two-player checks:

1. Use deliberately different guest/host saves: same-ID parts in different places,
   different replacement IDs occupying the same mount, and extra guest-only boxed
   parts. Only the host copies should be visible and pickable after the snapshot.
2. Fit, tighten, remove and refit each fixed-mount and multi-slot family. Pause
   snapshot delivery between attachment messages: no duplicate occupants, stale
   bolt input, or unintended farther-slot selection may occur.
3. Confirm initial host state requests settle, ID/checksum reports converge, and
   repeated resync does not reintroduce saved objects or alter host wear/tightness.
4. Disconnect/rejoin repeatedly with both loose and fitted copies. Originals must
   restore without native Init/Load replay, and must be isolated again on rejoin.
   Unload a parent/scene while copies are pending and check for orphan resurrection.
5. Exercise native save/quit/permadeath with disposable saves and verify unchanged
   guest file bytes. Guest engine-reference reconstruction, continuous adjustments,
   other part families and complete two-player acceptance remain open.

### Guest save protection (shipped in 0.1.33; no protocol change)

`Session/GuestSaveGuard` installs nine Harmony prefixes against the game's ES2
library during Core initialization. Guest launch modes arm protection before
loading; Steam invite and UDP join entry points also require the complete guard.
ES2 writer saves, file/PlayerPrefs stream storage, file/tag/folder deletion and
rename/move are suppressed. LowMemory write streams use disposable memory buffers,
so even existing `<save>tmp` files are preserved. Ordinary native PlayerPrefs
settings and memory-only serialization remain available. Core sidecar writes require
host authority. No packet layout, message semantics or protocol version changed.

Protection stays active through connection failure, shutdown, scene teardown and
quit. A protected process cannot start hosting; restart the game to play or host a
personal world. The overlay explains this. Occupied guest part mounts still defer;
this persistence barrier does not reconcile or restore their in-memory state.

Validation on game build **23268598**:

- Core and both Net targets build cleanly; **1,011** protocol/catalog/policy tests pass.
- **21 native save-library checks pass** under Unity 5/Wine in an isolated game copy
  and Wine profile: host save/overwrite/read/rename/delete, guest raw/tagged writes,
  low-memory temp preservation, new-file prevention, all seven native permadeath
  filenames, rename/move/folder deletion, ES2 PlayerPrefs storage, native settings,
  memory serialization, shutdown protection and refusal to host afterward.
- ES2.dll SHA-256: `863733f06a0d988f9e71db3a5d7cf5db5de8108b40f106f5673d303018b02d24`.
  The checked death actions are `Systems/Death::Activate Dead Body`, states
  `Delete saves` / `Delete saves 2`. Ordinary save buttons broadcast `SAVEGAME`;
  the persistence guard leaves that native exit flow running.
- The reusable opt-in probe is `tools/GuestSaveProbe`. Build its project with
  `-p:DeployToGame=false`. Copy its DLL and current Core/Net/catalog files **only into
  an isolated game copy with a separate profile**, then start that copy with
  `--wintermp-guest-save-probe` and no host/join flags. It writes disposable files
  and `guest-save-probe/result.txt` beside that copy's executable, then quits.
  Start with an empty probe-output directory when rerunning. Never ship the probe.
  This run's report is `build/guest-save-smoke/game/guest-save-probe/result.txt`.

Still required in two real game instances with disposable saves:

1. Join through Steam and local UDP; try a native save point and quit. Compare all
   guest save-file bytes before/after, while the host saves and reloads the session.
2. Disconnect, let failure recovery return to Idle, and trigger native save/quit or
   permadeath. Guest files must remain unchanged and hosting must require restart.
3. Restart into a personal world; verify native saving and hosting work again.
4. Repeat with replacement parts fitted/removed and after scene/LOD transitions.
   Existing occupied-mount deferral remains expected until part isolation is built.

The probe exercises the actual save library and Core guard, not native save-button
transitions, a complete death sequence, Steam connectivity, or multiplayer gameplay.
The isolated offline menu emitted native Steam-not-initialized errors; these checks
do not establish Steam acceptance.

### Guest replacement bolt controls (shipped in 0.1.33; no protocol change)

`FsmWorldSync.ReplicaBolts` binds integer-step bolts on owned replacement copies.
The existing stable part/child IDs, FsmRawEvent intents and absolute BoltState /
WorldBoltSnapshot replies are unchanged. No wire layout or authority semantics changed.

Static evidence from build 23268598 validates **55 controls across 24 replacement
families**: each has the native visual/index/size bindings, a valid integer-array
slot and a layer-12 SphereCollider trigger. The local spanner/ratchet Raycast Check
FSM reads `Screw.Boltsize` and sends TIGHTEN / UNTIGHTEN to that collider's Screw FSM.
The `replacementParts.replicaRepairVariable` binding names global RepairMode.
Game arrays are initialized by PlayMakerArrayListProxy.Awake, before these bindings.

Owned copies retain only five presentation/input states. Native initialization,
array increments, parent BOLTING, alternator enable/disable and at-limit timing
branches cannot execute on the guest. Tools send one existing host intent; only
host absolute results change the replica's array, tightness and local bolt pose.
A fitted copy remains kinematic, with physical colliders disabled; only the checked
bolt triggers become raycastable while repair mode is active. Its loose collider
settings return after removal. Each changed attachment invalidates bolt readiness,
drops older parked observations and requests fresh targeted bolt state before picking.
Replacement scalar receipts share the bolt/part receipt ordering, so deferred
presentation or duplicate replacement packets cannot roll back a later bolt total.

Verification: 1,008 protocol/catalog tests pass, including native index bounds,
first-seed/refit gates, unsupported directions, timing limits and deferred parent
receipts. Core and both Net targets compile cleanly. Static evidence and unit tests
are not a completed runtime acceptance pass. Run on disposable/backed-up host saves:

1. Fit a newly opened replacement absent from the guest save. On the guest, select
   the correct spanner size or ratchet and turn each supported bolt in both directions.
   Compare host/guest array entries, bolt positions, parent total and removal allowance.
   The host alone performs native mount/engine effects; no duplicate increment occurs.
2. Join after fitting and tightening. Delay the targeted reply: tool selection must
   remain unavailable until a fresh absolute state arrives, including tightness zero.
   Wrong tool size and ordinary hand mode must not operate the controls.
3. Operate simultaneous host/guest turns, remove/refit the part, and move it to another
   eligible slot. Earlier parked bolt state must not seed a new attachment. After
   reconnect or a destroyed/recreated guest copy, the controls must resynchronize.
4. Test both alternator bolt groups, piston/main-bearing bolts and camshaft/crankshaft
   bolts. A ninth tightening step must not adjust timing. Alternator child EnableFSM
   effects must remain host-local. Continuous rocker/mixture/drain/alignment adjustments
   remain separate adapters; these are not covered by the 0–8 integer bolt model.
5. Move the car and check nested part copies. Only bolt trigger picks may be enabled
   while fitted; pickup/cargo, solid collisions and guest save writes remain disabled.
   Hide/lose the parent, retire a copy, then reconnect: no stale collider, input callback
   or bolt registry entry may survive. Inspect F7 logs from both peers for rejection
   or binding failures.

Guest mount isolation, full engine/adjustment behavior, non-box part creation
and native two-player/save validation remain open.

### Guest piston/main-bearing/rocker fitting verification (v118, shipped in 0.1.32)

Automated: **983 protocol/catalog and 18 launcher tests pass**. Both Net targets
build cleanly; the launcher retains its existing missing FastBoot Release payload
warning. Core builds against the installed
game DLLs with no warnings and `DeployToGame=false`; no installation or game run
is performed by these checks. `PartSlotTests` cover the appended 188/189 slot byte,
invalid/removal slots, immutable retries and receipts, concurrent slot clicks,
movement to another slot, native later-index distance ties, occupied-nearest
rejection, sparse arrays, strict tolerance, malformed geometry and catalog bindings.

Build 23268598 extraction validates the three part ASSEMBLING entries, the shared
`CORRIS/AssembyDatabase::Installer` and all 17 array mounts: Pistons/4,
MainBearings/5 and Rockers/8. Source hashes are in the v115 section below.
The native ArrayListGetClosestGameObject action runs on entry, includes inactive
and occupied points and resolves equal distances to the later index. VIN103's
initialized ArrayReference is Pistons despite stale MainBearings action snapshots.
The slot adapter reads the live reference and preserves this native selection.
These three Data templates contain Consumed but no Installed boolean. The earlier
replacement-template requirement incorrectly disabled their factories. v118 requires
that scratch flag only for fixed-mount families and skips its absent initialization
on array-slot replicas; their native AssemblyID still determines fitted state.

Before a guest operation, the shared Installer must be idle with cleared candidate,
point and index. A guard precedes its Near writes; a second pair guards the mount's
Allow install?/Near path. The same candidate and slot must pass native prerequisites
in one frame. Unconfirmed previews cancel before the initiating call returns,
clearing only their AllowInstall/preview and stopping their Installer selection.
Native Install 1 and subsequent mass/body/parent effects execute once; the observed
result must agree with the requested AssemblyID and actual InstallPoint.

**Still required in two running game instances; not performed here:**

1. Open piston, main-bearing and rocker boxes as a guest. Fit each loose replica
   at every native slot using the normal held-part left click. Check the same slot,
   native ID, condition, assembly ID, mass, destroyed host Rigidbody and final
   parent on both peers. The host may be elsewhere; the guest must be close to
   the part and mount. Repeat with the supporting block/head moved or rotated.
2. Hold the part closer to an occupied slot than a free one, between equally close
   slots, or near an inactive mount. Verify native selection is preserved and a
   blocked nearest slot never turns into an installation at another location.
   Check native prerequisites, including the referenced blocking engine part.
3. Move the held part from one slot to another after sending a click. The host
   must reject the old selection without changing either slot; a new click may
   choose the new slot. Repeat with changed host poses, missing array entries,
   unavailable parent bindings and packet loss around the native pickup release.
4. Compete with two guests, perform native host fitting at the same time, and
   delay or duplicate requests/receipts. The shared Installer must not be taken
   from an existing preview. One accepted operation must fit once; a changed slot
   cannot reuse a sequence or settle another pending click. Retry an old request
   after removal/refitting and verify it cannot install again or move the refit.
5. Make a prerequisite fail or native selection fail to reach the guarded mount
   immediately. Check the Installer returns to idle, AllowInstall is cleared only
   for that preview, and later host clicks cannot complete the rejected fitting.
   After a committed operation times out, the part's fitting adapter must disable
   without repeating native installation or stopping the host session.
6. Remove fitted parts using the v117 path, refit at a different slot, reconnect,
   request item resync and save/reload the host. Check stable part IDs, correct slot
   indices, attachment and native save data. Guest replicas must not write saves.
   Occupied guest-save mounts and replica parents with disabled child mount FSMs
   still defer; full guest mount/save isolation and engine reconstruction remain open.

### Guest replacement removal verification (v117, unreleased)

Automated: **960 protocol/catalog tests and 18 launcher tests pass**. Core builds
against the installed game DLLs with `DeployToGame=false`; both Net targets build
with no warnings. The launcher test build retains its existing missing FastBoot
Release payload warning. These checks do not install or run the mod.
`PartRemovalTests` cover exact extended 185/188/189 layouts, invalid operation,
status and readiness bytes, immutable install/remove identity, duplicate pending
and settled outcomes, failed removal retries, simultaneous requests, stale clicks
after refitting, readiness revisions, tightness boundaries and invalid scalars,
proximity/readiness gates, and scaled ray/box intersection with range/occlusion.

Build 23268598 asset inspection validates removal bindings for all 30 replacement
families: Data.Collider is a BoxCollider on the part root; Tightness? compares
Tightness against 1; Mouse off/Mouse over use a 1 m layer-19 ray and right click;
Remove sets the current InstallPoint's ActivePart and sends REMOVE to its Data FSM.
VIN130 includes a native FloatClamp before its comparison. The audit includes
pistons, main bearings and rockers; removal uses their actual fitted mount.
Source hashes are in the v115 section below. Native Allow removal? transitions
alone do not establish a blocker veto, so live admission also requires the native
enabled trigger collider and mouse-ready state. Guest selection reads the same
box geometry while leaving all fitted-replica colliders disabled.

**Still required in two running game instances; not performed here:**

1. Fit a replacement whose guest copy is mod-created, loosen its bolts on the host,
   and aim at it with empty hands on the guest. Check that the right-click prompt
   appears only within the native 1 m range. Right-click once: the host must run
   native Remove/REMOVE once, the guest must become a loose carryable part at the
   host pose, and the same native ID, condition and saved adjustments must remain.
   Repeat across the 30 families, including fitted piston, bearing and rocker slots.
2. Exercise native part blockers, bolts at and above tightness 1, disabled removal
   colliders and an unfinished/reassigned mount. The prompt must follow the host's
   readiness and the host must reject a click based on old state. Change a blocker,
   tighten bolts or move the player after publication and before entry. Verify
   cancellation cannot affect a different part occupying the old mount.
3. Use two guests and delayed/duplicated requests or receipts. Exactly one native
   operation may run at a time; Busy requires a new click. Repeat an accepted or
   failed request, then refit the part and replay the old click. Neither replay may
   remove the refit or change engine mass. Install receipts cannot settle removal
   requests and removal receipts cannot settle install requests.
4. Check nearer layer-19 objects and fitted copies occlude farther targets. Test
   rotated/scaled parent hierarchies, multiple visible parts, camera movement and
   normal hand/tool changes. Holding or throwing an item, using a tool, opening
   chat, pausing or being dead must not trigger this prompt or issue a removal.
5. Remove parent parts with native dependent-removal effects. Verify host mass,
   mount occupancy, native UNINSTALL, child attachments and replacement body
   identity settle correctly; no loose-item movement or cargo lease survives from
   before fitting. A commit that fails to settle within 3 seconds must report
   failure and disable only that part's removal adapter without repeating it.
6. Move the guest beyond 3 m of the part or mount, stop fresh player poses, die or
   disconnect before entry. Reject the operation. Disconnect after native commit,
   reconnect and request item resync: the host's final state must win with no replay.
7. Save/reload the host after removal and after refitting. Confirm native identities,
   bolt totals, condition and assembly state remain correct. Inspect guest save
   files: the new replica must not write them. Occupied guest-save mounts still
   defer presentation. Session teardown must remove temporary entry guards and
   replica objects while retaining original guest-save objects.

v118 adds multi-slot installation. Guest mount/save isolation, operational replica bolt and
engine graphs, non-box part creation and these native gameplay checks remain open.

### Guest replacement fitting verification (v116, unreleased)

Automated: **930 protocol/catalog tests and 18 launcher tests pass**.
`PartFitTests` cover the exact 188/189 wire layouts, invalid framing/statuses,
authenticated identity and token checks, immutable retries after success/failure,
concurrent clicks, Busy outcomes, revision changes, ownership/release ordering,
native strict distance tolerance, invalid geometry and reconnect/sequence wraparound.
Core builds against the installed game DLLs with `DeployToGame=false`; the build does
not install or run the mod. Both Net targets and the launcher tests are also checked.

Native build 23268598 extraction validates all 27 fixed-mount replacement families:
Data/Another part? reads Installed, selects ActivePart and sends CHECK to its own
factory-supplied InstallPoint. The mount traverses Allow install?/Far/Near before
PROCEED enters the native Install 1 path. Temporary entry guards confirm at Near
only in the same frame as Allow install?; teardown removes both guards. Its
GetDistance compares the part with the
mount, not the host player's position. VIN103, VIN104 and VIN117 need a separate
multi-slot adapter. Source hashes are listed in the v115 section below. The native
`PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand::PickUp` Part picked
state maps left click to Drop part: it detaches PickedObject and releases its joint.
The fitting prompt therefore retains the immediately preceding held candidate;
host ownership also accepts that guest's final transform for 0.5 seconds. While
held still, items retain ownership through periodic transform keepalives.

Required two-player checks, **not yet run**:

1. Open a supported replacement box as a guest with no matching native save part.
   Hold the new part still for several seconds: ownership must stay with that guest.
   Move it to a free fitting point; the **Left click to fit this part** prompt must
   select only the held part. Click once, verify native host installation, one
   mass change and one fitted guest copy. Chat and paused gameplay must not fit it.
2. Repeat for a basic alternator, performance alternator, starter, timing belt and
   clutch parts with their native prerequisites. A missing prerequisite must leave
   the part loose. The host need not stand near the engine. Check fitting/removal
   still works normally for the host while no remote request is active.
3. Delay or drop receipts and resend the same request: exactly one native fitting
   may occur. Change its part/revision/token under the same sequence: no operation.
   Two guests competing for one mount must not replace the winning part or double
   engine mass. Busy must require another click after the original request ends.
4. Move the held part outside native tolerance, move the guest beyond 3 m, kill or
   disconnect the guest, or transfer the part to another owner before confirmation.
   Reject/cancel only that preview. Verify the native left-click release arriving
   before/after the request still works, and a release older than 0.5 seconds fails.
5. Fit while the host clicks elsewhere, changes a prerequisite or picks another
   candidate. Verify a canceled preview cannot install later. If a native commit
   cannot settle within 3 seconds, it must not be retried and that part's fitting
   adapter must disable while the session continues.
6. Save/reload the host, reconnect guests and request object resync. Verify the fitted
   identity, condition and attachment are unchanged; guest saves are not written by
   the new replica. Occupied guest-save mounts must still defer the replica. Generic
   guest replacement Install 1/Install 2/Remove state packets must not mutate the host.

This adds fitting requests for guest-created copies at fixed mounts. v117 adds
removal requests, and v118 adds piston/main-bearing/rocker slot selection. Existing guest-save part isolation and
operational bolt/engine graphs on these copies remain unfinished.

### Fitted replacement presentation verification (v115, unreleased)

Automated: 896 protocol/catalog and 18 launcher tests pass. Core builds against the
installed game DLLs with `DeployToGame=false`; Net builds for net35 and
netstandard2.0. `PartAttachmentTests` cover the appended wire layout, invalid
parents/paths/poses, stale attachment revisions, fit/remove motion gating, deferred
mounts, parent cycles and terminal retirement. `ScenePathCacheTests` also verify
relative mount lookup with repeated siblings and ambiguous bracket names.

Native build 23268598 evidence: the 26 directly referenced factory mounts expose
ActivePart, AssemblyPoint and Installed, and reparent ActivePart to AssemblyPoint.
Additional level2 extraction confirms the rev limiter and the dynamic piston,
main-bearing and rocker mounts. In all 30 replacement prefab Status layouts, only
VIN133/ALTERNATOR0 have SetRotation actions, both targeting their own Pivot. Runtime
bindings validate those targets before adding the replica-only presentation state.
Sources: level2 SHA-256
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`,
sharedassets3 SHA-256
`4212b819e589e084c9fb0b26a6790e3760a27433d56c696e231aaf607b976b43`.

Required two-player checks, **not yet run**:

1. With a common original save, have the host replace an alternator with a newly
   opened part that does not exist in the guest save. After fitting, exactly one
   temporary copy must appear on the guest's now-vacant mount. Drive/rotate the car:
   the copy must stay attached without extra reliable part messages for car movement.
2. Join with that part already fitted. Repeat with a bearing, piston, rocker and
   rev limiter to check dynamic slots. Change the alternator setting and verify
   its Pivot follows the host value without native mount/installation events.
3. While fitted, confirm no root or child collider participates in loose physics,
   no pickup/cargo ownership is granted, and the copy cannot send a disposal intent.
   Remove on the host: the same native ID must become loose at the latest host pose,
   restoring its original tag/scale/collider settings without an old ownership lease.
4. Delay or deactivate the parent before receiving the fitted state. The copy must
   stay pending/hidden, then attach once the exact indexed path exists. A different
   fitted guest-save occupant under that parent must be preserved; the new copy
   waits rather than overlaying it. Full guest mount isolation is still required.
5. Retire a parent with an owned child copy waiting on its state. The child must
   detach/hide, not be silently deleted by Unity's recursive parent destruction.
   A locally lost temporary graph must not send an authoritative host disposal.
   A retired copy must never return from a delayed snapshot.
6. Disconnect/reconnect repeatedly, including nested replicas. Temporary copies
   must be detached before deletion; native parent objects, original guest parts
   and saved files must remain intact. Compare original guest save files before
   and after the session; broader original native-part save isolation is unfinished.

This slice adds fitted presentation for missing boxed-content parts on available
mounts. New copies do not execute native installation or engine logic. The post-0.1.32
bolt adapter above enables checked tool triggers and host-result poses; other child
FSMs remain disabled. v116–v117 add fixed-mount installation
and removal requests; v118 adds multi-slot installation. Operational mount references,
non-box parts, occupied-save reconciliation and
native two-player/save verification remain open.

### Native bolt reconciliation verification (v114, unreleased)

Automated: 872 protocol/catalog and 18 launcher tests pass. Core builds against the
installed game DLLs with `DeployToGame=false`; the Net library builds for net35 and
netstandard2.0. `BoltStatePolicyTests` cover appended wire layouts, malformed chunks,
host correction, concurrent predictions, all nine tightness values, zero resync,
absolute array/pose repair, and delayed sibling/part aggregate ordering.

Build 23268598 `sharedassets3.assets` inspection (SHA-256
`4212b819e589e084c9fb0b26a6790e3760a27433d56c696e231aaf607b976b43`)
finds 493 catalog-matching Screw FSMs: 489 validated integer bolt bindings, one
MUDFLAPa0 with unresolved ThisPart, and three unsupported continuous controls
(VIN106 drain and two VIN209 alignment adjustments). The 489 include 479 normal
parent-updating/−400-divisor bindings, two alternator pivot bindings without parent
increments, and eight clutch-plate bindings without a divide/position action. Two
of the normal bindings use extra at-limit turns for engine timing. All 192 native
Data prefabs have the identity fields; 36 omit the Installed scratch bool.

Required two-player checks, **not yet run**:

1. On an existing fitted native part shared by both saves, tighten/loosen from both
   peers, including simultaneous turns. Compare host/guest BoltTightness, the live
   Bolts[Index] integer, Data.Tightness, mount tightness/Bolted and collider state.
   One native host turn must produce one increment; predicted guest values and
   scalar reports must never rewrite the host or add another turn on an echo.
2. Join/resync with mixed zero/partial/eight-step bolts. A guest eight-step bolt
   must clear to host zero. Normal local Z must be `tightness / -400`, not raw
   tightness; repeated identical results must not replay Screw/BOLTING effects.
3. Delay one sibling bolt's activation while newer sibling and PartState totals
   arrive. On activation, restore its own array slot with the latest parent total.
   Then apply a newer zero result and ensure older pending entries cannot undo it.
   Request the bolt alone through targeted object resync, including at zero.
4. Send a guest intent from over three metres away, with an old/dead player pose,
   or while the part/bolt is unavailable. It must not run later after fitting or
   activation. Disconnect/reconnect and verify one hook/report per native turn.
5. Check the alternator pivot's enable/disable effect without adding parent
   tightness, and clutch-plate array/total repair without a position action. On the
   crank pulley/camshaft sprocket, ordinary 0–8 turns should reconcile; an extra
   guest tightening turn at eight must not enter the mount's ADJUST path. Timing
   replication remains unfinished, including host timing changes.
6. Save/reload a host with mixed bolt values, then reconnect. The persisted array
   and scalar must agree with the pre-save host result. Inspect MUDFLAPa0's actual
   initialized ThisPart; a missing reference must produce one bolt-disabled log
   and leave the rest of the session running.

This work repairs existing native bolt graphs. Missing fitted guest parts/mounts,
bolt interaction on the isolated v111 loose copies, separate adjustment controls,
and original guest-save isolation remain unfinished.

### Native fitting lifetime verification (v113, unreleased)

Automated: 838 protocol/catalog tests and 18 launcher tests pass; Core builds against
the installed game DLLs with deployment disabled. Tests cover fitting before/after
body destruction, the removal gap before AddComponent, permanent disposal, late
join, stale state and body replacement between publication polls. Extracted build
23268598 actions confirm the alternator mount's DestroyComponent/AddComponent pair
and all 30 replacement Data FSMs' AssemblyID fit/reset transitions.

Two-player checks still required:

1. Open an alternator box, record the native ID, and fit that part on the host.
   Its loose guest copy must stop being pickable; logs must contain no ItemDespawn
   for the fitting transition. Replacement state must still answer an object query.
2. Remove it on the host. The same native ID must become loose at the host pose,
   with a new Rigidbody and no old cargo/physics ownership. Repeat with a part that
   was already fitted in the host save before the guest joined.
3. Send movement/cargo updates while fitting is in progress. They must not move
   the mounted part. An attempted guest disposal must not delete a fitted host part.
4. Dispose of the detached part and reconnect. Its retirement must remain terminal;
   host save cleanup must still delete the native part key.

This verifies lifetime handling. Guest fitting and complete fitted visuals, mount
references, bolt arrays and original guest-save isolation remain unfinished.

### Native guest box opening verification (v112, unreleased)

Automated: 822 protocol/catalog tests and 18 launcher tests pass; Core Release builds
with zero warnings/errors against the installed DLLs, with deployment disabled.
Static extraction checks every opening action/field/contents target for all 30 boxes
in build 23268598. PlayMaker action-loop inspection verifies that the head guard's
self-event skips the remaining actions. None of this replaces a two-player run.

Use disposable host/guest save copies:

1. Have a guest open each of the 30 box types, including every remaining piston,
   bearing and rocker. Verify one decrement and one exact native part per click,
   matching wear/adjustments and correct pickup/drop on both peers. The guest must
   not execute its own contents factory or alter its own saved counter/quantity.
2. Delay/drop receipts and repeat the identical request while the part initializes
   and after acceptance. Exactly one native part must exist; the guest retries the
   same token/sequence/revision and plays opening audio only once on acceptance.
3. Let two guests open the final quantity simultaneously, and repeat with a host
   click racing a guest request. One operation reserves the native path; a stale
   observer gets the current state and can make a new explicit attempt. No negative
   quantity, shared spawn-location overwrite or duplicate part is permitted.
4. Exercise nearby, distant, dead and stale-pose guests, a box held by another guest,
   a busy contents factory and a modified NumberOfProducts. Unsafe requests must not
   decrement quantity. A transient Busy request should succeed after readiness returns
   if its observed box revision still matches. Do not read serialized default count 4.
5. Dispose of an emptied box immediately after opening, and dispose of the output
   before deferred confirmation. The accepted creation must remain exactly once;
   replay should send native retirement rather than resurrect either object.
6. Join/resync during opening and after moving/disposing the output. Both the original
   guest and joiner must receive correct state; snapshot reads must not consume delta
   publication owed to either peer. Save/reload the host and verify native key cleanup.
7. Disconnect/reconnect while a request is pending. A new token must ignore old
   receipts; host admission clears old sequence state. Only mod-owned hooks are removed
   and reopening after reconnect must produce once, without stale callbacks.
8. Deliberately break an opening binding in a test catalog. Only that opening adapter
   should report/disable; retain the session and save. An uncertain partial native
   failure must never automatically rerun the creation on request retry.

Fitting and bolt interaction for new guest replacement copies remain incomplete.
Keep roadmap 7.2 open until full assembly support and these native checks are complete.

### Native loose replacement-part verification (v111, unreleased)

Automated: 797 Net/catalog tests and 18 launcher tests pass. Core Release compiles
against the installed game DLLs with zero warnings/errors and deployment disabled.
Static action extraction verifies all 30 factory creation/reference bindings and
Init -> Status -> Idle -> Stop paths in build 23268598. These checks do not execute
a Unity scene or prove two-player behavior.

Use disposable host/guest save copies, including different local part inventories:

1. Have the host unpack each standard box. Confirm every native output appears once
   for the guest, with the same saved ID and pickup/carry/drop behavior. Exercise all
   four pistons, five bearings and eight rockers; also test factory multi-output calls.
2. Check same-named standard/performance alternators, tuned cams and carburetors.
   Match wear and saved settings (alternator rotation, distributor SparkAngle,
   2-barrel SettingMixture and revlimiter SettingRPM). Never normalize native wear.
3. Join after parts have moved; request item-group and targeted resync. Missing loose
   copies must reappear at the fresh host pose, with no duplication or identity drift.
   Simultaneous snapshot reads must not suppress another guest's pending update.
4. Confirm a saved same-ID native guest part is reused, not replaced. A newly created
   replica must never read/write/delete guest carparts keys or increment guest factory
   counters. Disconnect/reconnect: copies and hooks disappear, factories resume once.
5. Fit a new part on the host. Its guest loose copy must disappear and stop claiming
   item movement. Detach it: the copy reappears at the fresh pose. A join with that part
   already fitted must defer creation, without fabricating a loose duplicate or
   overwriting a guest mount. Fitted-part reconstruction is still incomplete.
6. Request garbage disposal from a nearby guest holding a loose replacement. The host
   must echo acceptance before the copy disappears, retain native Data long enough to
   delete its save record, and not resurrect it on reload. Reject distant/stale and
   fitted-part requests without deleting the guest copy. Replay removal before creation
   and while the copy is hidden; neither path may resurrect it.
7. Break one factory binding deliberately in a test catalog. Only that factory should
   disable and report its reason. Other box contents, the session and native save flow
   must remain usable. Restore the catalog and reconnect to verify hook cleanup.

Guest box opening, guest fitting/bolt interaction for new loose replicas, complete
installed-part reconstruction and full guest save-graph isolation remain unfinished.
Do not mark roadmap 7.2 complete based on compilation or these unit tests.

### Native part identity / reconnect verification (v110, unreleased)

`PartIdentityTests` and `ScenePathCacheTests` verify separate body/Data/bolt IDs,
original zero-counter IDs, matching save-key proofs, invalid identities, 4,500 IDs
across the 30 package-content types, shuffled message routing, and root rename /
reparenting with different peer inventories. Static inspection of build 23268598
verified 194 distinct part-factory prefab names and the ID+AID / ID+POS save-key
pattern in all 156 matching native part Data prefabs. Original scene parts were
also inspected. Asset hashes match the v106 evidence below. The Core build uses
the installed game DLLs; automated tests do not execute Unity.

Outstanding checks, using disposable saves:

1. Use multiple standard/performance alternators and main bearings with distinct
   native IDs but identical display names. Give the two peers different object
   positions and scan order. After joining, move and tighten each part: state must
   route to its exact saved instance. No collision may silently select a neighbor.
2. Install/uninstall a part and move its root between the garage and engine. Request
   targeted state and resync after each move. The body, Data and each Screw ID must
   remain stable; repeated BoltPM siblings and the alternator's second bolt group
   must remain distinct. Test a part nested under another native Data root.
3. Delay initialization/load and enable previously inactive bolt objects. Never
   register a transient prefab/display name or apply scalars during native load.
   Corrupt one save-key binding or duplicate an ID: log/reject that instance without
   falling back to position order. Correct the binding and rescan.
4. Unpack a part beside a grocery spill. The grocery manifest must leave its native
   graph untouched; adoption/template/stale-clone paths must not steal it. The new
   part still requires its own contents materialization adapter on missing peers.
5. Disconnect/reconnect repeatedly without changing scene. Doors, parts, bolts,
   controls and bag capture must register again and send one event per action.
   Native actions and hooks belonging to other subsystems must survive teardown.
   Repeat after a deliberately failed partial registration; retry must not double
   the callbacks. Destroy a bound object before teardown and check containment.

Missing contents, complete installation/bolt-array state, disposal/save graph
isolation and guest opening are still unfinished. This change must not be treated
as completing the native multiplayer/save checklist.

### Native parts-package creation/quantity verification (v109, unreleased)

`PackageIdentityTests` and `PackageStateTests` cover all 30 catalog entries, 6,000
native identities, capacity bounds, wire layout, stale/replayed states, terminal
removal, session reset and snapshot-versus-broadcast ordering. Static inspection of
build 23268598 verified all 30 factory creation action sequences, prefab names,
contents references, startup/opening/empty states, capacities and native save-delete
cleanup. Build/asset hashes match the v106 evidence below. These checks do not run
Unity or establish that a multiplayer session works.

Use disposable saves for these outstanding checks:

1. Start with different standard boxes and positions on each peer, including the
   same native ID with different quantities. Join: the guest must see the host's
   exact box types and quantities, while its own originals remain hidden. Disconnect
   and save/reload: original guest quantities, positions and factory counters must
   be unchanged. Repeat with nearby boxes of different types and repeated counters.
2. Create every type on the host, including beside a spilling grocery bag. Verify
   direct factory output capture, full quantities (pistons 4, main bearings 5,
   rockers 8, others 1), item movement/cargo and late joining. Generic spill manifests
   must never adopt, steal or clone a box. Ordinary groceries and trophies must work.
3. Host-unpack a multi-part box while another guest joins: every existing guest must
   receive the quantity reduction. Empty boxes retain physical bodies and their
   stable item ID. A guest's attempted opening must show the local notice, spawn
   nothing and preserve quantity. Replacement-part contents are not replicated yet.
4. Delay factory/item loading, request item resync and delete a guest replica locally:
   deferred state/replay must create one correct box at the fresh host pose. Updating
   quantity on a held or moving replica must not teleport it. An older revision or
   equal revision with different quantity must be rejected.
5. Dispose as host and as a nearby admitted guest. Host GARBAGE must retain Use for
   normal SAVEGAME -> Delete(ID), with no resurrection after reload. Guest disposal
   must destroy only the replica. Repeat with removal before materialization, before
   resync, and during cargo following. Existing optimistic rejection behavior needs
   native observation; a rejected guest disposal must be repairable by resync.
6. Save while connected, disconnect/reconnect and reload both saves. No replica may
   write or delete a guest save key. Native factories and hidden originals must
   resume without repeating GetName/load. Break one factory binding: log that type
   without stopping other sync subsystems or choosing a different box template.

Guest opening intents, exactly-once replacement-part creation, persistent part/
bolt identities and installation/save replication remain unfinished (roadmap 7.2).

### Native car-part condition verification (v107, unreleased)

The shipped VIN133 alternator prefab's Data/Init initializes Wear with
RandomFloat(90,99), confirming that the old 0–1 PartState encoding was incorrect.
`PartStatePolicyTests` checks exact float preservation across repeated live/snapshot
round trips, legacy prefix framing, full 80-entry chunks, zero values, malformed
scalars, host-authoritative replies and meaningful condition checksums.
The Core build uses the installed game DLLs. The following native tests remain open:

1. Record a host part's Data.Wear around 90–99. Join, bolt/unbolt it as host and
   guest, and request resync. Both must retain the actual wear value, never 1.
   Repeat with fractional tightness and a heavily worn part near zero.
2. Give the guest a different local wear value in a disposable debug run. Its part
   report must leave the host unchanged and reconcile the sender to the host reading.
   Confirm the host reads after queued bolt/install events, with no feedback loop.
3. Request part resync with Wear=0, Tightness=0 and Installed=false while the guest
   has stale nonzero values. Confirm the zero-valued record clears them. Corrupt one
   guest's healthy wear from 95 to 99 and verify checksum recovery detects it.
4. Deliver a part snapshot before registration and while a registered FSM is disabled.
   Enable it later: full native values must apply. Deliver a newer live observation
   before an older pending value retries; the old value must not overwrite it.
   Verify a targeted object-state reply restores both FSM state and scalar values.
5. Repair/replace a fitted part, drive as host and guest, disconnect/reconnect, and
   save/reload disposable saves. With v124, confirm host damage state stays authoritative
   and guest originals remain intact. Separately check whether native host wear responds
   correctly while the guest drives; that behavior is not established by passive damage sync.

Package recon used `tools/extract_fsm_assets.py --match Spawner`, plus
`--match CARPARTS/PARTSYSTEM/SPAWNERS` against level2. Actual prefabs were inspected
with `--asset sharedassets3.assets --match box` and `--match VIN133`. The factory's
PPtr 9282 resolves to VIN133; its boxed alternator prefab is boxalternator0 (PPtr
7609). The verified build/asset hashes match the v106 evidence below. Box creation
and quantities have a v109 adapter; guest opening and persistent replacement-part
contents replication remain unfinished.

### Native trophy factory verification (v106, unreleased)

Static inspection of build 23268598 verified all 15 factory creation/idle states
and each actual prefab's init/load/save/ready actions. `FactoryItemTests` checks
wire identity, counter bounds, duplicate names, malformed metadata, catalog rules,
repeated join chunks, missing replicas and retirement. The plugin compiles against
the installed game DLLs. These are not substitutes for the following native checks:

1. With a guest connected, create bronze/silver/gold awards through each of the
   five native race factories. Verify exactly one matching model per award on each
   peer, normal pickup/movement/drop, and no unrelated nearby item being rebound.
2. Join a host with previously saved awards and awards created before joining.
   Give the guest a different local trophy collection (including matching counters).
   Verify only host awards are shown, with correct models and current poses.
3. Delay guest factory binding/loading, then deliver a manifest repeatedly. It must
   materialize once after load. Test more than 32 awards from one factory to exercise
   multiple replay chunks. Lose one guest replica in a disposable debug run and
   request item resync: only the missing body should be recreated.
4. Retire an award before deferred creation and replay/resync afterward. The retired
   ID must stay absent for that session. Check existing nearby awards stay intact.
5. Disconnect/reconnect repeatedly, switch hosts and return to singleplayer. Guest
   replicas must disappear; the original local trophies must return with their
   original save IDs, transforms and enabled state. Save/reload disposable host and
   guest saves and verify neither collection was duplicated or overwritten.
6. In a controlled debug run, break one factory binding. Expect one factory-disabled
   log, with other factories, items, vehicles and the session still functioning.

Extraction: run `tools/extract_fsm_assets.py` with `--match Spawner` for factories
and separately `--asset sharedassets3.assets --match trophy` for actual prefabs.
Verified SHA-256: level2 `36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`;
sharedassets3.assets `4212b819e589e084c9fb0b26a6790e3760a27433d56c696e231aaf607b976b43`.
PlayMaker's installed `Awake` initializes data; disabled components do not enter the
native `Start`/`OnEnable` save-load flow. The six older trophy prefabs start with a
0.2-second wait; the nine ice-race/rally prefabs do not, so disabling after active
instantiation would be insufficient. Native race event authority remains separate.

### Shared-item recovery verification (v105, unreleased)

Use two disposable saves on matching protocol/catalog versions. The protocol
tests exercise removal before creation, lost guest bodies, repeated replay,
invalid manifests, empty offer acknowledgments and 200 seeded recovery sequences.
The following native tests remain required:

1. Spill a grocery bag and delay guest materialization. Consume one of its items
   on the host before the guest creates it. The guest must create only the live
   contents, including after replay and an item-group resync.
2. In a disposable debug run, destroy one guest replica while leaving the host
   item alive. Request item resync (or wait for an at-rest checksum mismatch).
   The missing item must return once; existing items must keep their bodies and
   positions. Repeat resync while materialization is pending and after it finishes.
3. Consume an item, resync, join another guest and repeat replay. The consumed ID
   must stay absent. Also remove an item before its save object is first scanned;
   it must be removed on discovery and cannot return on another scan.
4. Temporarily make a template unavailable in a controlled debug run. The first
   materialization may fail; after restoring the template, the next resync must
   recover that item without duplicating successfully bound siblings.
5. Disconnect/reconnect and switch hosts. Verify old removal IDs and manifest
   receipts do not suppress items in the new session. Repeat with an own bag
   offer pending and with an empty host acknowledgment.

This fixes recovery for existing container manifests; it does not add the
deferred moose-meat or replacement-part contents factories (roadmap 7.2). Trophy
and box factories have separate v106/v109 adapters and checklists above.

### Hockey betting board verification (v104, unreleased)

Use two disposable saves with different hockey progress and matching mod/catalog
versions. `HockeyBettingTests` covers fixed wire slots, every table's odds, native
collection types, malformed/incomplete packets, sequence wrap and pending-state
ownership. Static evidence from installed build 23268598 verifies collection
references, keys, completed states and teletext readers. Native timing remains
unverified until this checklist runs with two players.

1. Join before and after the guest's hockey save loads. Compare all six upcoming
   matches, all 18 odds, previous matches/results/odds/scores and league standings
   on teletext pages 240, 241 and 302. Keep a page visible while the host advances
   a round; it must refresh without requiring a channel/page change.
2. Advance a native round while another guest joins. Each peer must receive a
   complete previous board or complete new board, never a mixture of old odds and
   new results. A snapshot sent to the new guest must not prevent the existing
   guest from receiving the completed round. Change only an odds-table entry in
   a disposable debug run and confirm it broadcasts without a new round number.
3. Turn off the guest television, change the host round, then reopen each page.
   Repeat while moving away and returning to the house. Check the collection
   proxies still bind and visible texts update after delayed scene binding.
4. Disconnect the guest and compare its original pairings, odds/results, scores,
   standings, GamesPlayed and KurPaWins before allowing a new native local round.
   Verify local round generation resumes. Repeat across reconnects, switching
   hosts and scene reloads; no old board may survive into the next session.
5. Save/reload the host before joining and after a round. LatestRound can be zero
   after a native load; GamesPlayed and saved collection values must remain exact.
   Watch logs for binding/restore failures and confirm other world sync continues
   if this subsystem is disabled in a controlled debug run.

Do not use guest Megaveto ticket purchases/claims as proof of this board feature:
their host transaction ledger is still unfinished (R2.21). Guest native season and
odds calculation is paused; no CHECKMEGAVETO replay is sent to guest tickets.
Individual-player scoring (Pisteporssi, teletext 242) is outside this snapshot.

### Lotto ticket verification (v103, unreleased)

Use two peers on matching protocol/catalog and disposable copies of different
saves. The protocol suite exercises receipt replay, simultaneous spending, exact
row capture, terminal claims, admission, sequence wrap and a seeded 1,000-ticket
money-conservation loop. The Core build checks real game assembly compatibility;
neither proves native input/physics/save timing.

1. Buy one, two and three rows on each peer. Leave the other peer's form open
   with different selections. Verify 3/6/9 mk is charged once, only the buyer's
   completed rows appear on the ticket, both peers can inspect the same ticket,
   and the other form's selections remain intact. Start a partial extra row and
   pay: that unpaid row must be all zero on the purchased ticket.
2. Buy concurrently with enough money for only one ticket. Repeat presses and
   delay an acknowledgment. The host counter, native ticket count and shared
   balance must each advance once per accepted request. Retry after a decline
   with more money. Cross a draw-round boundary while the form is open: reject
   the stale round and require reopening it.
3. Buy with the host away from the shop, before and after its first visit in
   the session. An uninitialized factory must wait without charging; it must
   resume when native loading reaches Idle. Confirm inactive-host discovery,
   ticket creation and both peers' ability to carry/drop the resulting body.
4. Let a real host draw calculate tickets. Drop losing, <1,000 mk and >=1,000 mk
   tickets into VoittousArea's collection box. Check a zero/cash/bank payment
   respectively, the ticket's disappearance everywhere, and one shared balance
   change. Try concurrent collection reports and delayed poses/acknowledgments;
   if declined, the ticket must remain available to pick up and drop again.
5. Join before native ticket discovery, after a purchase, after moving a ticket
   and after a claim. Compare persistent IDs, all 21 slots, round and winnings.
   Trigger item-group resync while another peer is connected; both must retain
   the latest ticket content and poses. Remove a guest replica in a disposable
   debug run and confirm a host keepalive recreates it without changing money.
6. Save the host with unclaimed tickets, restart/load, then claim them. Save again
   after claiming and reload: the native claimed ticket keys must be deleted,
   and no paid ticket may reappear. Verify factory IDs continue above prior IDs.
   Also save during pending acknowledgment and during a native draw calculation.
   Discard a saved ticket through ordinary garbage on a guest, then save/reload
   the host: that ticket must be deleted without any payment, never resurrected.
7. Reconnect repeatedly and disconnect with Pay or collection waiting for a
   response, including while a form/LOD object is inactive. The guest's own saved
   tickets must return, replicas must disappear, and normal singleplayer Pay and
   claim actions must work. Check that the guest's ticket save keys/files did not
   acquire host ticket data. Inspect `lotto-ticket` logs for unique settlements.

Lotto bank statement text and native achievement presentation are not yet added
to the new claim path. Megaveto still needs a separate host purchase/claim ledger;
its native selection/payout flow is not validated by these tests. Keep R2.21 open
until its runtime checks and remaining functionality are complete.

The native scene and **actual spawned prefab** can be inspected separately:

```bash
python tools/extract_fsm_assets.py "/path/to/My Winter Car" \
  --match Voittous --match Sheets/LottoTicket --include-array-lists \
  --out /tmp/lotto-scene.json
python tools/extract_fsm_assets.py "/path/to/My Winter Car" \
  --asset sharedassets3.assets --match LotteryTicket --include-array-lists \
  --include-transforms --out /tmp/lotto-prefab.json
```

For installed build 23268598, the prefab is active, Use begins with a 0.2-second
initialization delay, and ArrayList proxies allocate their live lists in Awake.
The extractor's `meta.asset` identifies the selected file; its legacy
`levelSha256` field hashes that selected file, including when it is a prefab asset.
Static evidence establishes bindings and action arguments, not save-loaded state.

### Live grocery bag regression (2026-09-09, unreleased)

Two isolated game processes on build 23268598 reproduced the tester's missing
contents: the native contents FSM finishes in `Garbage`, without returning to
`Idle`, so publication stayed pending after the bag disappeared. Completion now
requires the finished terminal command for the exact consumed bag, plus drained
product factories. Native host pickup is released before the bag loses its body.

The retest also exposed milk disappearing during guest initialization. Its
`Use::State 2` is a startup delay, not consumption. Configured numbered drink
states now require a native action that removes the drink's own body/object.

Validation: **4,293 protocol tests**, **56 native save/bag/spill/pickup checks** and
**19 live two-process gameplay checks** passed. Core and the probe built against
the installed Unity/PlayMaker assemblies with zero warnings/errors. Host-all,
guest-one/all and guest-last-one openings produced the same nine unique groceries
on both peers (three chips, four milk, two sugar), with correct quantities, single
checkout charges, exclusive pickup and released hands. Freshly falling bags retain
host motion ownership until settled; the guest pickup test waits for that release.

`tools/GuestSaveProbe/LiveBagProbe.cs` is developer-only: opt in with
`WINTERMP_LOCAL2P_BAG_TEST=1` and an isolated-root
`wintermp-live-bag-sandbox.txt` marker. Commands and snapshots use `live-bag/`.
It moves players to the shop and enters native interaction states; item selection
must enter `Wait button` before `Check inventory` to refresh native Carried counts.
The checkout, bag guards, opening requests, native product actions and replication
then run normally. This is local UDP acceptance, not physical mouse interaction,
Steam latency, mixed engine parts, save/reload or reconnect acceptance. Milk's
time-based condition/spoilage replication remains a separate roadmap gap.

Local evidence, before/after payload hashes and commands are in
`build/shop-bag-live-audit/`. Test games were closed and developer plugins/markers
removed. Personal saves and the normal installed mod were preserved; no release
was made. These fixes retain protocol 210 and require the matching updated catalog.

### Native engine parts in shopping bags (protocols 121–124, unreleased)

Protocol-121 validation passed **1,089 protocol/catalog/policy tests**,
**18 launcher tests** and **94 isolated native Unity/PlayMaker checks**. Core, the
probe and both Net targets built without warnings/errors. The native pass includes 22 checks imported
from both installed part factories/Data/mounts and 15 mixed-bag spill checks. The
retired-body regression exposed and now verifies the Unity 5 destroyed-object hash
behavior. Results and exact binary/evidence hashes are recorded locally in
`build/native-bag-parts-smoke/result.json`; no live installation or personal save
was modified. Real multiplayer acceptance is still pending.

This extends the 0.1.33 bag fix to fan belts and oil filters, using the host's
native `FANBELT0` and `OILFILTR0` identities. Replacement state 185 carries their
Wear/Dirt and Tightness; neither enters the temporary grocery ItemSpawn manifest.
Both fresh and saved Create outputs are captured. Existing boxed factory IDs are
unchanged. Guest originals use the existing save/part isolation path.

The captured Data object survives fitting and Rigidbody replacement. Capture lookups
use managed reference identity: Unity 5 changes a destroyed Rigidbody hash to zero,
so native-object dictionary keys cannot survive fitting unchanged. A part already
retired by the host completes capture without respawning. If a native part factory
fails after consumption, its host object is retained, the affected opening reports
failure, valid outputs still publish and unrelated bags can open again. No invalid
part manifest is guessed from a nearby body.

Fanbelt fitting validates and retains the native `Alternator` prerequisite between
Allow install? and Far: the referenced alternator's SettingRotation must exceed 6.
Oilfilter uses the normal fixed mount. Its separate Screw hand-turn input and pose
are now adapted in protocol 122, as described below.

Use matching protocol-124 builds and disposable/backed-up host and guest saves:

| Case | Expected result |
|---|---|
| Buy groceries, two belts and two filters together; host opens one/all | Each product appears once on both peers, including repeated parts |
| Guest opens a fresh mixed bag; competing/repeated inputs | One host inventory decrement per accepted opening; no extra parts |
| Carry, drop, fit/remove a newly spilled part quickly | Stable identity and a completed bag opening despite Rigidbody replacement |
| Fit belt with alternator angle at/below 6, then above 6 | Native prerequisite refuses the first attempt and permits an otherwise valid second attempt |
| Host and guest tighten/loosen oilfilter, then remove only when permitted | Shared integer Tightness, native pose and removal permission; see the hand-control checklist below |
| Guest already has saved belts/filters, including a fitted one | Saved originals remain isolated; one host copy occupies the correct mount |
| Partly open mixed bag, late join/reconnect, host save/reload | Matching contents and stable part IDs; no return of retired parts |
| Simulate a failed part binding after output creation in the probe | Host output survives, request fails and the global bag opening lock can clear |

`tools/GuestSaveProbe` adds `--wintermp-native-bag-part-probe` alongside the bag,
spill, pickup, save, part-isolation and alternator flags. Its
`native-bag-part-probe.json` input is extracted from build 23268598's CreateItems,
FANBELT0/OILFILTR0 Data and their native mount graphs. The probe executes installed
PlayMaker actions for factory IDs, save-key initialization and the alternator gate;
changed action bindings are refused. Run only in an isolated game copy and Wine
profile with `DeployToGame=false`; never package game assemblies or fixture inputs.
These checks do not replace the actual shop, Steam two-player, engine or save/rejoin
acceptance sequence above.

### Oilfilter hand tightening (protocol 122, unreleased)

Protocol-122 validation passed **1,145 protocol/catalog/policy tests**,
**18 launcher tests** and **120 isolated native checks**. Both Net targets, Core
(Debug/Release) and GuestSaveProbe built with **zero warnings/errors**. The native run added 26
hand-control checks to the previous 94: real turn/mount/pose updates, stale scratch,
limits and fractional values, changed bindings, guest control caching before native
triggers are suppressed, and unchanged loose-item motion. The launcher test build
reported its optional FastBoot Release payload missing; no installer was built.

Guests can scroll within 1 m of a fitted filter with an empty hand and no wrench.
Dedicated operations 4/5 in PartFitRequest/Receipt (188–189) request integer ±1
Tightness steps within 0–8. The host validates the observed revision, fresh nearby
player, settled native mount, control readiness and 0.2-second cooldown before
executing the real Screw/BOLTING chain. Duplicate or competing stale requests
cannot add another turn. Native pose code converts its Tightness scratch into an
offset; remote entry refreshes the raw scalar from Data before the next bound check.
Guest Screw remains disabled. Resolved host Tightness controls local Z rotation
(20 degrees per step) and position (-Tightness / 400), including deferred snapshots.

| Two-player case | Expected result |
|---|---|
| Guest fits a new filter, scrolls 0→8→0; repeat with host control | Both see one step per accepted input, matching pose and mount Tightness |
| Try scrolling beyond 0/8, rapidly, or with a wrench/item held | Bounds, native cooldown and empty-hand controls are preserved |
| Host and guest scroll together; retry an old request | One authoritative result; stale/repeated requests do not apply another step |
| Loosen completely, remove/refit, then tighten again | Removal allowance, fresh attachment state and controls agree |
| Scroll from beyond 1 m or while the mount is unsettled | No guest control; host rejects invalid readiness/proximity |
| Save/reload on host, reconnect guest after partial tightening | Host Tightness and pose return; guest originals remain unchanged |

Add `--wintermp-hand-screw-probe` to the isolated GuestSaveProbe run alongside
`--wintermp-guest-save-probe` and the existing fixture flags. Its
`hand-screw-probe.json` imports build 23268598's OILFILTR0 Data/Screw and
VINP_Oilfilter Data actions. Keep results and tested binary/evidence hashes in
`build/hand-screw-smoke/result.json`. Use an isolated game copy and Wine profile,
with `DeployToGame=false`; fixture inputs and game assemblies are not release files.
Actual two-player controls, fitting and save acceptance remain pending, together
with remaining guest engine behavior. Fanbelt presentation is implemented below.

### Fitted fanbelt presentation (protocol 123, unreleased)

Protocol-123 validation passed **1,205 protocol/catalog/policy tests** and
**18 launcher tests**. The combined native suite passed **154/154 checks**, adding
34 belt checks to the previous 120, with zero failures and Wine exit 0. Both Net
targets, Core (Debug/Release) and GuestSaveProbe built with **zero warnings/errors**.
Tested Core/Net/probe/catalog hashes matched that protocol-123 Debug build. The launcher
test build reported the expected optional FastBoot Release payload warning.

Optional BeltVisual in replacement state 185 carries Visible, Running, Scale,
Pitch, Volume and ScrollSpeed. Host observations retain native flutter amplitude
and damaged-belt squeal. Texture speed comes from the validated native
BeltAnimation global RPM × AnimMultiplier chain. Guests create an owned
SkinnedMeshRenderer hierarchy with internal bones, a private material and audio
source. UV phase is local; flutter alternates a 0.02–0.5-second wait and 0.05-second
pulse using host amplitude. Guest globals do not predict belt damage or sound.
Independent PresentationRevision orders cosmetic updates separately from physical
part revisions, so changing RPM or sound cannot stale an in-flight fitting request.
Complete packet freshness is still checked before applying a newer presentation.

The separate native guest Jumping wear/breakoff graph is paused, its renderer
hidden and audio muted even if the guest has no saved fitted belt. Original
hierarchy and mount fields remain unchanged; flags restore after owned copies
are removed. Failed visual binding preserves an existing fitted guest belt before
isolation can move it. A fitted replica hides its loose Mesh and restores it when
loose. Latest loose state, absent visuals or a changed parent stop the old view
immediately, even when unrelated replica creation remains queued.

| Two-player case | Expected result |
|---|---|
| Host fits a belt; guest mount starts empty or contains a saved belt | One fitted belt view; no loose mesh overlaid and no visible saved duplicate |
| Start/stop engine with a healthy belt | Both see the host's running/visibility state and texture direction/rate |
| Repeat with a damaged squealing belt or seized component | Host flutter amplitude and pitch/volume reach guests without guest wear/BREAKOFF execution |
| Remove/refit, break or retire a belt during presentation | Fitted view disappears or returns correctly; loose mesh and persistent identity agree |
| Change RPM while a fitting request is in flight; defer another replica | Cosmetics do not stale the request; a removed or reparented belt stops showing immediately |
| Reject a changed visual binding with an existing guest belt | Original fitted belt, mount and hierarchy remain preserved |
| Late join/reconnect while belt runs | Current host appearance arrives; private animation phase need not match exactly |
| Host save/reload, guest disconnect then restart in solo | Host belt state persists; guest original render/audio/FSM flags restore without mount or hierarchy changes |

Add `--wintermp-belt-visual-probe` to the isolated GuestSaveProbe run with
`--wintermp-guest-save-probe` and the existing fixture flags. Supply
`belt-visual-probe.json` extracted from build 23268598's FanBelt hierarchy,
Jumping and BeltAnimation graphs. Record output and exact tested binary/evidence
hashes in `build/belt-visual-smoke/result.json`. Use an isolated game copy and Wine
profile with `DeployToGame=false`; do not package fixture inputs or game assemblies.
These fixtures do not establish full guest engine behavior, rotating pulleys or
actual two-player visual/control/save acceptance.

### Engine damage authority and saved-part integrity (protocol 124, unreleased)

VehicleDamage 64 now comes only from the host, whether the host or guest drives.
The host rejects guest damage reports. Guests store accepted host condition for
snapshot/checksum comparison, including while locally driving; they do not write
native mount Wear or replay PartBreakages events. Their native Damages FSM is paused
until game restart, including after disconnect. `ClearDamageState` clears the
accepted condition cache without resuming native damage. Forced discovery runs
before part isolation, closing the normal three-second scan window for late-loaded
damage graphs before their saved ActivePart is moved. Failed suppression defers
isolation; errors in one damage graph are contained and retried.
VehicleCondition and other owner-authoritative streams keep their existing behavior.

Native evidence shows that damage references point to mount Data, whose ActivePart
may still be a preserved guest-local saved part. Guest publishing could therefore
send stale wear, while observer replay could act on that original. PISTON failures
also include random OILPAN/BLOCK consequences. Keeping guests passive prevents
those writes and repeat rolls; it does not reproduce physical failure on guest cars.

Protocol-124 validation passed **1,242 protocol/catalog/policy tests** and
**18 launcher tests**. The final combined native suite passed **177/177 checks**,
including **23 damage checks**, with zero failures and Wine exit 0. Both Net
targets, Core (Debug/Release) and GuestSaveProbe built with **zero warnings/errors**.
The [native result and hash record](../build/vehicle-damage-smoke/result.json)
matches the final Debug Core/Net/probe/catalog outputs; Release Core was built but
not native-tested. The launcher test build reported the expected optional FastBoot
Release payload warning. Full guest engine operation and correct native host wear
while a guest drives remain unestablished.

Add `--wintermp-vehicle-damage-probe` to the isolated GuestSaveProbe run with
`--wintermp-guest-save-probe` and the existing fixture flags. Supply
`vehicle-damage-probe.json` extracted from build 23268598's PartBreakages Damages
graph and native mount/part Data actions. The fixture executes native wear
propagation and random piston collateral, substituting only MasterAudio calls.
It also checks passive guest state, suppression readiness, late-loaded graphs and
protection after disconnect. Record output and tested binary/evidence hashes in
`build/vehicle-damage-smoke/result.json`. Use an isolated game copy and Wine
profile with `DeployToGame=false`; fixture inputs and game assemblies are not
release files. These checks do not establish live save/restore or two-player driving.

| Two-player case | Expected result or observation |
|---|---|
| Join with different saved fitted parts and wear; guest drives, parks and hands back control | Host remains the only damage publisher; accepted guest condition follows host state without altering guest originals |
| Trigger host PISTON failure in a disposable test; compare snapshots | Guests accept the resolved host condition without executing local damage rolls or mutating retained ActivePart references |
| Repair/replace on host; late join, reconnect and request a vehicle resync | Healthy state clears old accepted damage; parked damage checksums settle without reading guest mount Wear |
| Send a guest damage report in a controlled probe | Host drops it even if that guest drives; host condition remains unchanged |
| Guest drives under engine load, then host repeats the same run | Record whether native host wear responds correctly; do not count passive condition agreement as engine simulation acceptance |
| Disconnect/rejoin, then restart guest in solo | Damage stays paused after disconnect; accepted cache clears and refills on rejoin. Restart restores normal solo damage behavior without unwanted saved-original changes |

Visible physical failure on guests needs its own safe presentation/engine work and
is not an expected outcome of this integrity fix.

### Shopping bag duplicate-inventory regression (protocol 120, shipped in 0.1.33)

A two-player test of v0.1.32 found both players could hold and open separate copies
of a purchased bag. The host's contents could remain invisible until the guest
opened their copy. Source inspection found peer-local bag IDs/inventories, a generic
scanner racing radius capture, and native prefab names that did not match spilled
names (`chips` versus `potato chips(itemx)`).

The replacement uses persistent host bag identities, isolated guest bag views,
acknowledged host-only opening, native factory-output capture, native prefab name
mapping and retryable materialization. Large inventories are split into 32-entry
manifests. Pickup guards prevent attaching remotely owned bags and release the
exact held copy after a winning remote claim/removal. Saved guest bags are restored
on teardown; their native opening/save actions never operate on the host inventory.
A slow native spill keeps its capture reservation instead of timing out into a
second inventory. Failed chunk publication retries the same IDs.

Validation commands (always keep `DeployToGame=false` for development builds):

```sh
dotnet build src/WinterMP.Net/WinterMP.Net.csproj
dotnet test src/WinterMP.Net.Tests
dotnet test src/WinterMP.Launcher.Tests
dotnet build tools/GuestSaveProbe/GuestSaveProbe.csproj -p:DeployToGame=false
```

The opt-in native probe flags `--wintermp-bag-probe`,
`--wintermp-bag-spill-probe` and `--wintermp-bag-pickup-probe` accompany
`--wintermp-guest-save-probe` in an isolated game/profile copy. They exercise real
PlayMaker SendEventByName/CreateObject/SetParent/SetJointConnectedBody actions on
disposable fixtures. They cover both spawn guards, exact repeated-product capture,
unrelated-output exclusion, early prefab-name resolution, pickup ownership and
cleanup. These are controlled integration checks, not a complete running shop or
two-client Steam acceptance test. The existing save/isolation/adjustment flags can
run alongside them.

Required two-player acceptance:

1. Buy groceries including chips, macaroni boxes and mosquito spray; immediately
   pick up the bag before the next world scan. Both players must see one shared bag.
2. Try to pick it up simultaneously. Only the winning player retains it. Repeat
   with host and guest winning, and drop/reacquire between attempts.
3. Have the host open first, then repeat with the guest opening first. Every item
   appears without the other player opening another bag. Repeat one-item removal.
4. Try to open the same remaining revision simultaneously. Inventory decreases once;
   duplicate requests/retries must not mint extra contents.
5. Open a bag containing more than 32 items, two separate nearby bags, and a bag
   while another purchase finishes. Counts and bag identity must remain exact.
6. Join late/rejoin after spilling, remove an item during deferred creation, and
   disconnect while holding a bag. No resurrection, duplicate replicas or stuck hand.

In shipped 0.1.33, mixed bags containing native part products without a replica
adapter—including fanbelts and oil filters—were rejected before inventory mutation.
The unreleased protocol-121 adapter above covers those two families; unknown part
products remain guarded. Catalogued Fleetari
package outputs keep the separate package identity/replica path. Food condition and
all special product behaviors still require broader acceptance; this change is not
a claim that every store product is fully synchronized.

On 2026-09-06, protocol/policy tests passed **1,067/1,067** and launcher tests
passed **18/18**. The isolated native suite passed **66/66** (21 save, 7 part
isolation, 18 alternator adjustment, 5 bag guards, 9 exact spill, 6 pickup).
The native report and tested assembly/catalog hashes are retained locally in
`build/bag-smoke/result.json` and `build/bag-smoke/game/guest-save-probe/result.txt`.
Core and probe compile against the installed game assemblies with zero warnings
or errors. The two-player acceptance matrix above remains pending.

### Guest starter wear (protocol 179, unreleased)

Guest cranking now wears the host's starter. The audited Starter Fuel Mixture #10
multiplies native literal 0.197 by StarterDurability into StarterWear. Action #11
subtracts that amount from db_Starter::Data.Wear on entry and each update with
`everyFrame=true`, `perSecond=true`. Native SubtractFsmFloat multiplies each callback
by Time.deltaTime, unlike the per-callback battery draw added in v178. Other
cranking states have no wear writer.

The existing protection descriptor adds `starterWear: true`. On a registered
protected guest Corris with complete engine/battery metadata, the action remains
enabled as an observer. Its native helper skips saved wear writes and records only
the frame duration. The original local operand, destination and cadence must still
match. Changed/stale actions remain blocked and pause the affected graph. Native
local wear arithmetic remains active; saved guest starter fields are unchanged.

Only a connected guest owning the car sends time. Reliable request 199 contains
vehicle/player/sequence/seconds (13 bytes with ID), with each duration finite and
in (0, 1]. Batches flush after 0.1 seconds, before exceeding one second, and before
final vehicle state/ownership release. Zero/invalid frame durations are ignored.
Ownership loss/session clear drops unsent time. Requests carry no rate or part
condition and are never relayed or snapshotted. Both peers require protocol 179;
existing messages retain their layouts, mod version remains 0.1.33, next ID is 200.

The host authenticates sender/current owner, rejects local-driver conflicts and
replayed/stale ushort sequences, and bounds time with a one-second burst budget
replenished by one second per host unscaled second. Rejected work consumes its
sequence and cannot replay after repair. Native checks require a settled installed
battery, live started Starter, installed starter/flywheel, starter/harness/ground
wiring, original wear callback and validated durability-reader/rate-calculation
signatures. An active host crank rejects additional guest wear. Current host mount
Durability and the native host literal determine the applied amount, independent
of stale local scratch. Scoped native helper fields apply the integrated amount
once and restore afterward. Native mount Update 2 copies Wear to physical part
Data, which the existing ReplacementPartState publication reads.

Validation: **3,213 protocol/catalog tests** (31 new), **18 launcher tests**, and
**3,092 native checks** (37 new; all 3,055 previous check labels/counts retained).
Net net35/netstandard2.0, Core Debug/Release, probe and launcher builds have zero
warnings/errors. Native testing uses Debug; Release is build-only. Result record,
source delta, native audits, logs, and matching disposable/launcher payload hashes
are retained in `build/guest-starter-wear-smoke/result.json` and its directory.

The native fixture covers solo entry/update timing, protected guest reporting and
saved-field preservation, non-wearing crank states, bounded/timed/final flushing,
invalid time, ownership/session cleanup, disconnected observers, changed writer
containment/repair, different host rate/durability, duplicate rejection, native
physical-part publication, and host rejection/recovery for part/battery/wiring,
active cranking, changed signatures, invalid scalars and stale cached destination.
The first run passed all 36 initial new checks but failed two old assertions that
required the writer to be disabled. Those now call the guarded writer and assert
saved-state preservation; the final run also adds physical-part publication.

This remains an isolated native callback/transport test. Live acceptance should
compare repeated short/long guest cranking with host cranking, then test a worn or
replaced starter, flywheel/wiring removal, disconnect/rejoin and driver handoff.
Batched subtraction may round differently from individual frame subtractions.
Sustained duration reports above the host time budget (including accelerated time
or repeated entry callbacks), pauses and extreme frame intervals need playtesting.
Other accessory demand, physical battery replication and full starting/driving
acceptance remain open. No commit, push, release, installer, installed-game
deployment or personal-save test was performed.

### Guest heater condition (protocol 180, unreleased)

The Corris heater now reads the host's installation and wear while preserving the
guest's saved heater. A fresh static audit of installed build 23268598 captures
VINP_Heaterbox::Data and HeaterUnit::Function. Mount Install 2 imports ActivePart
Wear; Update 2 publishes wear every frame; Remove part writes it back while
removing the part. Heater Electrics? #4 reads Installed, Blower wear? #1 reads Wear,
and its native comparison sends BROKEN below 3. Blower ok #6 divides SettingBlower
by WearRateBlower, then #7 subtracts Math1 from mount Wear on entry.

The new required heater profile pins the mount, read fields and stable states.
Saved guest heater Data is paused, draining already-active work and blocking
native removal. Destination-based external scalar protection stops heater wear
writes before discovery as well as after binding. Native helper reads use host
state only on connected protected guests, keep native source caching/fallback,
and assign only safe consumer-local outputs. Saved/global aliases are blocked.
Known source identities survive movement, metadata loss and disconnect until
destruction. Unseeded, removed or unavailable input supplies false/zero. Native
blower calculation and its broken threshold remain active; host/solo simulation
retains normal native wear and physical-part publication.

Host-only HeaterState 200 is 11 bytes including ID: revision, availability/installed
flags and finite wear (zero unless installed). Capture waits for a live started
mount in Update 2, an active directly parented ActivePart with unique Data,
AssemblyID=1 and a Wear field. It reads current mounted wear, not a stale physical
copy. Stable empty Idle publishes absence; loading/removal/invalid sources publish
unavailable. State is independent of the physics owner, polled at 0.2 seconds with
ordered change publication, five-second keepalive and join/vehicle-resync copies.
Snapshots preserve pending live changes; revisions reject conflicts/stale state
and wrap correctly. Clearing discovery/session state clears the replica. Both
peers require protocol 180; mod remains 0.1.33 and existing message layouts stay
unchanged. Next free ID is 201.

Validation passes **3,243 protocol/catalog tests** (30 new), **18 launcher tests**,
and **3,140 native checks** (48 new, all 3,092 prior labels/counts retained). Net
net35/netstandard2.0, Core Debug/Release, probe and launcher builds are clean.
Native testing uses Debug; Release is build-only. The initial protocol run exposed
seven catalog assertions tied to the previous protected-source inventory; those
now include the heater and still check policies absent from raw metadata. The
native suite passed on its first run. Audits, source delta, logs, results and
matching disposable/launcher payload hashes are in `build/heater-input-smoke/`.

The native fixture covers load/attachment/condition rejection and repair, snapshots,
publication bookkeeping, native host wear propagation, guest pre-discovery write
blocking, mounted publication/removal suppression, host absence/condition updates,
read timing/caching/fallback, unrelated sources, alias rejection, source movement
and metadata loss, session cleanup, and actual native blower decisions around 3.
It uses inert objects and selected real game actions; it does not replay every
heater state or establish a live two-player session.

Live acceptance should compare heater settings and healthy/broken blower behavior
with different guest saves, then remove/refit the host heater, reconnect and hand
off the driver seat. Heater wiring/hose prerequisites, physical heater and battery
replication, remaining accessory loads and full driving acceptance remain open.
No commit, push, release, installer, installed-game deployment or personal-save
test was performed.


### Guest heater and rear-defroster wiring (protocol 181, unreleased)

The Corris heater now reads the host's two supply circuits and separate rear-window
circuit without changing the guest's saved wiring. A fresh audit of build 23268598
captures WiringHeatercontrolFusebox, WiringHeater and WiringFuseboxWindow Data
under CORRIS/Wiring/DatabaseWiring, plus HeaterUnit::Function. All three wires are
unbolted and settle at Basic state after load. They use source IDs 9–11 inside
existing WiringState 193, accepting flags 0/1/3. Loading, disabled, missing or
ambiguous sources publish unavailable. Existing independent revisions, five-second
keepalive and join/vehicle resync now cover eleven sources. Both peers require
protocol 181; message layouts and mod version 0.1.33 are unchanged. Next ID is 201.

The three new catalog sources require projectNativeReads=true; the original eight
retain their proxies. Connected protected guests' native GetFsmBool helpers read
host Installed into safe local outputs. Unseeded/unavailable circuits read false.
Native same-object source caching, missing-name fallback and entry-only scheduling
remain intact. Saved/global aliases are rejected. Known wire identities remain
recognized through movement and lost metadata until destruction, while session
teardown clears host values. Host/solo and unrelated sources retain native reads.
The existing 102 indexed input entries/182 reads, 23 paused graphs, 75 guarded
scalar writers and 38 replacement/31 package factories are unchanged.

Heater Electrics? #2/#3 read the control/unit supplies; the native BoolAllTrue #5
requires those and heater installation #4. Rear defrosting #0 reads its own circuit;
BoolTest #1 gates the remaining element/control checks and native consumption
addition. The fixture exercises all eight supply/installation combinations and
both rear-circuit states using actual native reads, gates and consumption math.
It also covers host load/capture, pending publication, unavailable recovery,
stale/conflicting updates, source cache/fallback, unsafe aliases, moved sources,
metadata loss, session reset and reconnect. The old wiring fixture was expanded
with the freshly audited wire rows so its host capture checks run for all eleven.

Validation passes **3,269 protocol/catalog tests** (26 new), **18 launcher tests**
and **3,174 native checks** (34 new; all 3,140 previous labels/counts retained).
Net net35/netstandard2.0, Core Debug/Release, probe and launcher builds have zero
warnings/errors. Native validation uses Debug; Release is build-only. The complete
native suite passed on its first run. Logs, audits, source delta, results and
matching disposable/launcher payload hashes are in `build/heater-wiring-smoke/`.

This uses inert fixtures and selected game actions, without a live two-player
session or complete heater graph replay. Rear heating-element and control checks
are bypassed in the fixture to isolate the circuit gate; physical window-heating
writes are omitted. Native production checks remain untouched. Heater hoses,
HeatingSprites, physical heater/battery/wire replication, complete climate and
remaining accessory-load authority still need work. Live acceptance should use
different guest wiring saves, disconnect/reconnect each host circuit, exercise
blower and rear defroster controls, then reconnect and hand off the driver seat.
No commit, push, release, installer, installed-game deployment or personal-save
test was performed.


### Guest heater hose inputs (protocol 182, unreleased)

Native Corris heater hose reads now use host inlet/outlet installation, preserving
the guest's saved hose mounts and parts. A fresh audit of installed build 23268598
captures both hose Data FSMs, HeaterUnit::Function and Cooling. Heater pipes? #2/#3
read VINP_HeaterHoseInlet/Outlet Data.Installed into Installed1/2 on entry; #4 uses
BoolAllTrue to proceed to Calc defrosting. The original preceding #0/#1 reads
Cooling.WaterLevel and rejects values below 0.5, including no-radiator zero.

Existing host EngineBlockState 195 already carries independent installed hose bits
(top=0, bottom=1, inlet=2, outlet=3) and radiator Coolant. No wire fields or message
IDs change; both peers require protocol 182, mod remains 0.1.33 and next ID is 201.
Host capture still waits for settled attached hose Data, publishing mount tightness
and installation with revisions, keepalive and join/vehicle resync. Native Cooling
reads host radiator Coolant through its existing proxy, clamps WaterLevel to 0–25,
and clears it without a radiator. The new heater reads complete the hose side of
that native coolant/pipe gate; no extra coolant message or local scratch overwrite
is needed.

The two heater hose catalog declarations require projectNativeReads=true; radiator
hoses must omit it or use false. Missing/malformed/foreign declarations reject full
input admission. Connected protected guests' native GetFsmBool helper projects the
actual hose source into safe consumer-local outputs. Unseeded/absent input is false;
host/solo and unrelated reads stay native. Source caching, fallback and callback
cadence remain intact. Saved/global aliases are rejected, including a saved hose
variable inserted into consumer locals. Known source identities survive movement
and metadata loss until destruction. Teardown clears accepted host state. Existing
102 indexed entries/182 reads, 23 paused graphs, 75 guarded scalar writers and
38 replacement/31 package factories are unchanged.

Validation passes **3,279 protocol/catalog tests** (10 new), **18 launcher tests**
and **3,209 native checks** (35 new; all 3,174 previous labels/counts retained).
Net net35/netstandard2.0, Core Debug/Release, probe and launcher builds have zero
warnings/errors. Native validation uses Debug; Release is build-only. Evidence,
audits, source delta and matching native/launcher payload hashes are retained in
`build/heater-hose-smoke/`.

The new fixture runs actual radiator reads/clamp/removal and all five Heater pipes?
actions. It checks all 16 hose masks, coolant values around 0.5 and the 0–25 clamp,
radiator absence, arrival timing, stale state, source-cache/fallback behavior,
unsafe outputs, moved sources, metadata loss and native read recovery after reset.
Saved hose/radiator scalars, identities, attachment, cap and pause state are checked.
The initial probe build had a fixture helper-name error and the initial unit build
a nullable warning; both were corrected. The first native run stopped during new
fixture setup because initialization discarded its actions. The second passed all
prior checks and 34 new ones, but expected synthetic part factories to remain after
ReleaseSession. The final fixture respects teardown and verifies fresh host hose
reads before factory rediscovery; all 35 new cases pass on the third full run.

These are inert fixtures with selected native actions, without a live two-player
session or complete heater graph replay. Full reconnect factory rediscovery is not
exercised by this fixture. Rear-window HeatingSprites, physical hose/heater/battery
replication, complete climate and remaining accessory-load authority remain open.
Live acceptance should compare different guest hose/radiator saves, remove/refit
each host heater hose, drain/refill coolant across the threshold, then disconnect,
rejoin and hand off driving. No commit, push, release, installer, installed-game
deployment or personal-save test was performed.


### Guest rear-window heating element (protocol 183, unreleased)

The Corris rear defroster now uses the host body's heating-element option. A fresh
audit of build 23268598 captures CORRIS/BODY::Save and HeaterUnit::Function.
Body Window Heater? reads VIN.WindowHeater into VINcode; native comparisons route
M and '-' to State 3 (HeatingSprites=false), and other values to State 2 (true).
Facelift selection then chooses settled State 1 or State 4. Rear defrosting #2
reads that boolean into its own HeatingSprites; #3 gates the rest of the state.

HeaterState 200 appends one independent RearWindowFlags byte after Wear, making
12 bytes including ID. Rear flags accept 0 unavailable, 1 absent and 3 present,
independently of blower availability/installation/condition. The host captures an
active, initialized, started, unique BODY Save source in State 1, State 4 or Save;
loading/transitional/disabled/missing/ambiguous sources publish rear flags 0.
Body failures leave blower state intact, and a missing blower leaves a ready body
option intact. Rear-only changes advance the existing revision; snapshot copies,
reliable change publication, five-second keepalive and join/vehicle resync retain
both fields. Both peers require protocol 183; mod remains 0.1.33, with no new IDs
and next free ID still 201.

Required rearWindow catalog metadata pins the source, variable and accepted states.
Connected protected guests' native GetFsmBool helper consumes the host option into
a safe consumer-local output. Unseeded/unavailable/absent host state reads false.
Host/solo and unrelated sources remain native. Source caching, missing-name fallback
and callback cadence stay intact. Saved/global aliases are blocked; known source
identities survive movement/metadata loss until destruction. Guest body Save is
not paused, and its option, VIN and strip visuals are not overwritten. The existing
102 indexed entries/182 reads, 23 paused graphs and 75 scalar writers are unchanged.

Validation passes **3,305 protocol/catalog tests** (26 new), **18 launcher tests**
and **3,255 native checks** (46 new; all 3,209 previous labels/counts retained).
Net net35/netstandard2.0, Core Debug/Release, probe and launcher builds are clean.
Native validation uses Debug; Release is build-only. The first protocol run exposed
the generic round-trip fixture's arbitrary value for the new flags byte; its valid
nonzero seed now tests the appended field. The complete native suite passed on its
first run. Audits, source delta, logs, results and matching native/launcher payload
hashes are recorded in `build/rear-window-heat-smoke/`.

The fixture covers host load boundaries and independent failure/recovery, native
VIN string comparisons/boolean assignments, rear-only revision publication,
read timing/cache/fallback, unsafe outputs, moved sources, metadata loss, stale
state and reset/reconnect. All eight circuit/element/switch combinations run the
actual native reads, gates and consumption addition. The actual CutoffRear heat
addition runs against an inert cabin sink and changes it only when all three gates
pass. Saved body/VIN and heater/battery values remain intact.

VIN hash-table lookup, save-property/material work and full body/heater state
progression are outside this fixture. The VIN comparison input is supplied directly;
body transitions skip the unrelated facelift/material work. There is no live
two-player session, full reconnect discovery or end-to-end frost visual acceptance.
Full climate authority, physical glass-strip presentation, remaining accessory-load
work and live driving/handoff remain open. Test different host/guest factory options,
wire disconnection, rear switch changes, late join and driver handoff with two players.
No commit, push, release, installer, installed-game deployment or personal-save test
was performed.

### Independent window ice (protocol 184, unreleased)

The previous climate stream captured only Freezing.CutoffWindshield and copied it
to all six remote panes. Rear-only heating and individual scraping results were
therefore overwritten. VehicleClimate 61 now retains the original windshield byte
and appends side-left, side-right, door-left, door-right, rear and an availability
mask: 23 bytes including ID. Both peers require protocol 184; mod version remains
0.1.33, with no new message IDs and next free ID 201.

A fresh audit of build 23268598 verifies the six native Freezing cutoff variables
for Corris, Sorbet and the taxi. State 1 assigns zero; sheltered Check roof assigns
one; individual scrape states add ScrapeEfficiency to their own pane. The native
rear heat addition is HeaterUnit Rear defrosting #6 on Corris and #1 on Sorbet/taxi.
Thus exterior cutoff zero means iced and one means clear; older protocol prose had
this direction reversed. Encoding retains that native direction and clamps only
the visual range. Missing/nonfinite sources omit their own pane, and an available
zero remains distinct from unavailable. Reserved mask bits and nonzero unavailable
values fail serialization, decoding and direct Core receipt before dedup or writes.
The loaded catalog was also missing the taxi paths already present in defaults;
those audited paths are now restored and covered by catalog and native tests.

Live and snapshot builders capture each pane without changing native cutoffs or
consuming the live sequence for snapshots. Immediate remote application and the
existing LateUpdate hold copy only available panes. A later unavailable reading
releases the old pane hold; it cannot borrow the windshield. Interior frost/fog,
controls and current delegated climate authority remain unchanged.

Validation passes **3,337 protocol/catalog tests** (32 new), **18 launcher tests**
and **3,321 native checks** (66 new; all 3,255 predecessor labels/counts retained).
Net net35/netstandard2.0, Core Debug/Release, probe and launcher builds are clean.
The native suite runs Debug in the disposable game copy with all 54 existing
flags; Release is build-only. Each of the three vehicles exercises real native
cutoff bindings, isolated scrape/reset/rear-heat actions, live packet capture,
snapshots, late presentation, missing/invalid inputs, stale/duplicate/malformed
packets, packet-copy isolation, owner guards and hold expiry. Initial native
failures exposed the missing taxi catalog paths, an over-strict float assertion,
and shared test-item metadata leaking into later engine checks. Dedicated test
items and a float tolerance isolate these fixtures; the next complete run passes.
Evidence, the scoped source delta and matching tested payload hashes are recorded
in `build/window-ice-smoke/result.json`.

This fixture does not run full climate graphs, the roof lookup, scrape sound/body
warmth effects, heater gates/load consumption or material rendering. It imports
only the relevant native scalar actions into inert fixtures. This is window-result
replication, not complete host climate authority or new scraping-intent handling.
Live two-player verification must compare separately scraped panes and rear heating
on all three cars during driving, parking, late join and driver handoff; verify
interior condensation remains independent and window materials actually converge.
No commit, push, release, installer, installed-game deployment or personal-save
use was performed.

### Vehicle climate ownership (protocol 185, unreleased)

Climate previously allowed passengers and nearby guests to send complete climate
reports. The host applied its sequence check locally but still relayed rejected
reports, allowing observers to diverge. Publication and receipt now follow the
established vehicle ownership used by the engine/pose streams. The local owner
publishes while delegated; the host publishes unowned cars. Nearby presence,
passenger seating and native ignition activity cannot grant a guest authority.

The engine-independent VehicleClimateStreamPolicy validates vehicle/source IDs,
known flags and v184 ice availability before ownership/dedup. Each vehicle/sender
keeps independent live history across handoffs. First zero is valid, live counters
skip snapshot sentinel 65535, and stale/duplicate/half-range/unauthorized reports
do not advance history. Core rejects disconnected delivery and protects locally
owned and seated pre-claim drivers. Failed acceptance stops the host relay and
cannot refresh the presentation timer. Held presentation rechecks current source
and local driving before each application, so old owners cannot continue painting
a car during its three-second hold.

Final climate now uses reliable channel 0, bypasses the periodic timer and travels
after final engine state but before the reliable release pose. The host relays the
same channel. Periodic climate remains 2 Hz on channel 1. Join/repair of a remotely
owned car copies the accepted current owner's complete report, without sampling
local native drift or consuming the outgoing/live-receive sequence. Missing,
expired or former-owner reports are omitted. Host snapshots retain owner 0 and
the sentinel, and apply to unowned/host-owned guests; an already observed guest
owner retains its live stream. Readmission clears that player's histories/held
report, and session teardown clears all histories, holds, cached copies and send
counters. VehicleClimate remains 23 bytes; both peers require protocol 185, mod
version stays 0.1.33, and next free message ID remains 201.

Validation passes **3,381 protocol/catalog tests** (44 new), **18 launcher tests**
and **3,343 native checks** (22 new; all 3,321 predecessor labels/counts retained).
Net net35/netstandard2.0, Core Debug/Release, native probe and launcher build cleanly.
The complete native suite passes on its first run with all 54 flags in the isolated
Wine prefix/disposable game. Native testing uses Debug; Release is build-only.
Source, launcher and disposable payload hashes and the scoped delta are recorded
in `build/climate-owner-smoke/result.json`.

The new fixture reuses the previous audited native Corris window graphs and tests
packet authentication/relay through SessionManager, ownership conflicts, stale
and invalid packets, sequence wrap, final channel/timing, accepted-state snapshots,
copy isolation, owner handoffs, local-driver protection, readmission and teardown.
Existing window tests now identify their simulated sender as the host and inspect
the replacement history/cache. The existing native item-release test also verifies
engine → climate → pose on the reliable channel. All previous checks remain.

This establishes one climate source, not complete native climate simulation or
new control/scraping intents. The transport is captured in-process; there are no
two live Steam clients, rendered window acceptance, full seat/door/occupancy flow
or reconnect discovery. Passenger-only occupancy/warmth, remaining native climate
input work, scraping and live driving/parking/join/handoff/reconnect still need
verification. In a two-player test, change seats while heating/scraping, leave the
engine running, then shut it off and park; both players should retain the same
per-window state without passenger or old-owner updates undoing it. Repeat with a
late join and reconnect. No commit, push, release, installer, installed-game
deployment or personal-save use was performed.

### Vehicle climate occupancy isolation (protocol 186, unreleased)

Remote climate used to write GlassFrosting.PlayerIn, which the local driver
fallback also reads. An occupied remote report could leave that flag true after
the owner released the car and the climate hold expired, allowing an on-foot
observer to be mistaken for its driver. Immediate climate application and
LateUpdate now retain shared occupancy separately and leave native local PlayerIn
untouched. Native entry/reset and local driver parenting still work; an empty
remote report cannot clear local entry. Accepted guest-owned snapshots retain
the reported occupancy, while fresh host capture after release reads current
local entry and seats rather than recycling the old received flag.

The source's cabin flag now includes accepted passenger seats even when the
native local PlayerIn variable exists but is false. The host queries its accepted
PassengerSeatLedger; guests query the remote seat view only for players still
present in their connected roster. Local passengers contribute to their own car.
Seat changes, rejection, final exit, departure and host ledger cleanup stop the
corresponding contribution. Slot readmission now clears the old remote seat and
anchor alongside its sequence history, avoiding a phantom passenger when a slot
is reused before periodic cleanup. Occupancy alone grants neither driver nor
climate publication authority; the v185 rules remain in effect.

VehicleClimate 61 keeps its 23-byte layout and existing flags/IDs/channels. Both
peers require protocol 186; mod version remains 0.1.33 and next free ID is 201.
No production catalog bindings change. The fresh build-23268598 CarTemp/PlayerTrigger
audit records 32 FSMs; the native fixture reuses the v184 Corris/Sorbet/taxi window
graphs and imports only each PlayerTrigger's Press return #8 and Player reset #2
SetFsmBool actions, targeting its inert GlassFrosting copy. It does not execute
mirror, Crouch, real-player parenting, save, material or temperature callbacks.

Validation passes **3,391 protocol/catalog tests** (10 new), **18 launcher tests**
and **3,394 native checks** (51 new; all 3,343 predecessor labels/counts retained).
The new checks cover immediate/LateUpdate local-flag isolation, expired holds,
native entry/reset, snapshot ownership, accepted/rejected/moved/exited seats,
roster departure, host passengers, local passengers, readmission, inactive views
and parenting fallback across all three cabins. Native seat decisions use the
existing ledger with a fixture validator, not a live proximity/seat entry flow.
Net net35/netstandard2.0, Core Debug/Release, native probe and launcher build with
zero warnings/errors. Native execution uses Debug in the disposable game and
isolated Wine prefix with all 54 existing flags; Release is build-only. Evidence,
source/payload hashes and the scoped delta are in
`build/climate-occupancy-smoke/result.json`. The first native run passed 3,391
checks; the final run adds the three readmission cases. The first probe build
needed a namespace import correction; final builds are clean.

This isolates entry and reports occupancy; it does not complete passenger-driven
condensation or warmth. Native GlassFrosting also reads PlayerSweat to compute
FrostingRate, and that remote input is not yet synchronized. Full seat/door flow,
rendered climate, scraping intents, reconnect discovery and live two-Steam-client
acceptance remain open. In a two-player check, have the guest drive then exit,
wait beyond the climate hold, and confirm the on-foot host does not acquire the
car. Repeat with roles swapped, passenger seat changes, a late join and a
reconnect. No commit, push, release, installer, installed-game deployment or
personal-save use was performed.

### Native condensation presentation (protocol 187, unreleased)

The cabin audit found two incorrect remote writes before adding passenger sweat
reporting. GlassFrosting uses white RGB tint with Frost as alpha; the old capture
took max(RGB, SweatRate), normally sending Fog=255 even on clear glass. The receiver
then wrote that back to SweatRate and tint, while direct material presentation
changed `_Cutoff` instead of `_Color.a`. Separately, Warm car divides Temp by 100
before using it in a per-second subtraction; received cabin degrees overwrote
that already divided scratch value during the active state.

Remote condensation now applies the accumulated Frost amount to the native float,
Color.a and material `_Color.a`. It preserves RGB tint, the interior material's
shader cutoff and all exterior pane values. Missing material still permits the
native scalar/color, cabin temperature and available panes to update. CabinTemp
samples/applies Data.InteriorTemp only; missing source data retains the neutral
0 °C encoding, without borrowing GlassFrosting.Temp. The discarded SweatRate/Temp
bindings and obsolete fog presentation helpers/cache were removed. Native growth,
defrosting rates, timing and local entry remain untouched.

VehicleClimate 61 keeps all 23 bytes. Fog is retired in place: local capture sends
0 and presentation ignores any received value. The codec/cache still preserve the
byte, so packet copies and snapshots keep their existing framing without giving
it native effects. No other field/ID/channel changes; both peers require protocol
187, mod version stays 0.1.33 and next free ID remains 201. No catalog rules change.

Validation passes **3,391 protocol/catalog tests**, **18 launcher tests** and
**3,439 native checks** (45 new; all 3,394 previous labels/counts retained). All
native checks pass on the first run. Net net35/netstandard2.0, Core Debug/Release,
native probe and launcher build with zero warnings/errors. Native execution uses
Debug in the disposable game/isolated Wine prefix with all 54 existing flags;
Release is build-only. There are no additional error/exception log lines relative
to v186. Evidence, hashes and the scoped source delta are recorded in
`build/climate-condensation-smoke/result.json`.

The fixture reuses the audited v186 CarTemp definitions and v184 exterior window
graphs for all three cars. It imports the actual Defrosting/Warm car actions into
inert FSMs, including FloatDivide, per-second subtraction, SetColorRGBA and
SetMaterialColor. Material parameters bind only to a new private Standard test
material; no installed/shared game material is modified. A separate read-only
asset audit confirms daily_frost_glass, corris_frost_glass and taxi_frost_glass
use alpha blending, white tint and a separate saved `_Cutoff`. Tests cover native
source bindings, white tint/rate capture, live packets, five alpha endpoints,
immediate/LateUpdate/material consistency, native redraw and warm-car arithmetic,
missing material/cabin source, stale packets, local ownership, expiry and handoff.
The renderer is not exercised in the headless run.

Remote PlayerSweat and passenger-only condensation/warmth were still unsynchronized
in v187; v188 below adds sweat inputs after these representation/scratch fixes.
Full seat/door flow, rendered material/thermal convergence, scraping
intents, reconnect discovery and live two-Steam-client acceptance remain open.
In a two-player check, compare clear glass, gradual fogging and heated defrosting,
then hand off the driver seat and repeat after parking/late join/reconnect. Both
players should see the same opacity without dark tint, altered exterior ice or
an abrupt native defrost-rate jump. No commit, push, release, installer,
installed-game deployment or personal-save use was performed.

### Passenger condensation inputs (protocol 188, unreleased)

The existing player-pose stream now reports native PlayerSweat availability/value
in five appended bytes (PlayerTransform 22 is 39 bytes). Available values must be
finite and within the audited 0–100 range; unavailable values must be zero. Host
identity checks, guest selected-host filtering, known-player lookup and existing
pose sequence ordering gate both storage and relay. An unavailable report clears
the previous sweat value. No new message ID, timer or guest-profile field is
introduced. VehicleClimate 61 remains 23 bytes; mod version stays 0.1.33, protocol
and compatibility manifest are 188, and next free ID remains 201.

Only the current climate producer combines local entry/driver/passenger presence
with living remote passengers accepted in that car. Host membership comes from
the authoritative seat ledger; guest membership comes from its accepted remote
seat map and connected roster. Existing local guest seating is optimistic.
Remote poses must be less than five seconds old, with valid non-future receive
times; dead/departed players, exited/rejected seats and other cars are excluded.
A fresh seated player with unavailable sweat contributes only the dry minimum.
For each occupant the mod adds clamp(sweat, 6, 30), capping the combined input at
30. Native division by 300 and the .02–.1 clamp remain intact: one dry occupant
gives .02, two dry occupants .04, dry plus sweat 15 gives .07, and wet crowds stay
at or below .1. The multiplayer sum is a deliberate mod policy bounded by the
native individual limits. No occupants means native DefaultRate, audited .0001.

The new optional catalog profile binds only the verified six-action Player in?
state on Corris, Sorbet and taxi. Scoped BoolTest/FloatOperator operands supply
occupancy and combined sweat; they never write local PlayerIn, global PlayerSweat
or saved values. Native rate copies, accumulation, defrosting, timing and existing
climate delivery/presentation remain in control. Observers and delegated hosts do
not borrow inputs. Changed action signatures log and disable only this binding,
with later retry; nested/exceptional calls restore operands. Stream/session/scene
cleanup removes bindings even when old vehicle metadata has left the item index.

Validation passes **3,429 protocol/catalog tests** (38 new), **18 launcher tests**
and **3,495 native checks** (56 new; all 3,439 v187 labels and multiplicities
retained). Net net35/netstandard2.0, Core Debug/Release, the native probe and
launcher build with zero warnings/errors. The native run uses Debug and all 54
existing flags in the disposable game/isolated Wine prefix; Release is build-only.
Source, launcher and disposable Core/Net/catalog/manifest payload hashes match.
Evidence, logs, fixture hashes and the scoped source delta are in
`build/passenger-condensation-smoke/result.json`.

The first probe build exposed unsupported five-argument Action syntax on net35;
a custom delegate fixes it. A staging guard then prevented copying that failed
build, but a separate launch mistakenly ran the old v187 disposable payload
against uncleared probe output. It reported one existing-file fixture failure,
not a v188 failure; that output/log is archived as `unintended-old-payload-*`.
The prior full output directory was overwritten by that run, while the confirmed
v187 result/log remain intact in their original evidence directory. After the
clean rebuild, output was archived, staged hashes verified, and the fresh v188
run passed with no failures or additional error lines relative to the v187 log.
Its preservation comparison uses the confirmed v187 result, not the unintended run.

A fresh read-only extraction of PLAYER/BodyTemp and CarTemp supplies 17 FSMs and
the PlayerSweat global definition. The new fixture imports all six Player in?
actions and restores the native FINISHED transition into existing inert climate
fixtures. It explicitly binds the real test-process PlayerSweat global and
restores its original value afterward. It exercises native minimum/ceiling and
per-second accumulation, host/guest/host-passenger paths, empty/missing/stale/dead
occupants, seat moves/rejection/exit, authenticated pose relay and sequence wrap,
owner isolation, normal climate capture, changed-signature recovery, nested and
exceptional operand restoration, and cleanup after item removal.

These are imported native actions, fixture seat decisions and captured in-process
transports, not a full player/avatar send loop, physical seat/door interaction,
rendered scene or two Steam clients. Passenger body warmth, remaining climate
inputs, scraping intents, reconnect discovery and live thermal/ownership
acceptance remain open. A live check should compare dry/wet passengers entering
and leaving a parked and driven car, then repeat with a guest driver and after
handoff/rejoin; both players should see the same gradual fogging and defrosting.
No commit, push, release, installer, installed-game deployment or personal-save
use was performed.

### Native body warmth persistence (protocol 189, unreleased)

The passenger warmth audit found that PlayerNeedsSync sampled/restored local
PLAYER/BodyTemp::Calculations.Temperature. This is environmental temperature:
Get temp reads the rain/roof-check source and Source reads the nearest heat
source. The actual native body quantity is global PlayerTemp. Calculate computes
(Temperature - 10) / AirSpeed, clamps the result to -20..20, then adds it to
PlayerTemp; movement, clothing, sweat and alcohol have their own native stages.
The global starts at 50 and is not a Celsius measurement. Restoring a former air
sample into either the body's warmth or the current environmental input is wrong.

The reporter and snapshot now bind PlayerTemp with explicit availability, leaving
ambient scratch untouched. A known zero or negative value is valid. Missing or
nonfinite globals report unavailable zero without blocking other needs. A known
host restore waits if the global is absent; reports retain that pending value so
another reconnect cannot erase it during initialization. Once bound, it restores
once and native progression continues. New valid unknown snapshots and Reset
cancel superseded pending warmth. The existing last-position spawn choice maps
the saved availability into this path; the spawn-at-host choice remains as before.

PlayerNeedsReport 25 appends one strict 0/1 HasBodyTemp byte after HasAlco, retaining
the 43-byte prefix and becoming 44 bytes. GuestSpawn 24 remains 95 bytes, assigning
bit 4 (16) to known body warmth in its existing float; that bit requires saved-needs
bit 1 (2). Known warmth must be finite and unknown warmth must be zero. Existing
host identity checks, reliable report channel and wrap-aware needs ordering apply.
Malformed optional BAC also rejects before consuming the sequence, matching the
profile store's finite-value requirement. Protocol/manifest become 189; mod stays
0.1.33, next free ID stays 201, and player-pose/climate layouts remain unchanged.

GuestProfile's pure codec and needs snapshot now live in WinterMP.Net; Core keeps
host-only file I/O behind the existing guest-save guard. The historical CSV file
named wintermp-guests.json writes 18-column needs rows, retiring column 12 to zero
and appending native PlayerTemp at column 17. Old 8–17-column profiles never
reinterpret their air sample, even if it looks plausible. Empty/invalid new
warmth stays unknown while valid unrelated needs and poses survive. Optional
dirtiness/BAC/warmth use independent cells, so missing dirtiness cannot drop a
known BAC or body value. Pose-only rows remain 8 columns; optional known floats
use invariant round-trip formatting, preserving zero and small values.

Validation passes **3,467 Net tests** (38 new), **18 launcher tests** and
**3,508 native checks** (13 new; all 3,495 v188 labels and multiplicities retained).
Core Debug/Release, both Net targets, the probe and launcher build with zero
warnings/errors. Native execution uses Debug in the disposable game and isolated
Wine prefix with all 54 existing flags; Release is build-only. Source, launcher
and disposable Core/Net/catalog/manifest hashes match. Logs, tested hashes and
the scoped source delta are in `build/body-warmth-smoke/result.json`.

The initial Net run identified three existing fixtures that needed the new known
warmth flag/trailing byte; all new tests already passed. Updating those fixtures
preserved their earlier field-order checks. Initial probe nullable warnings were
resolved before native execution. The first full native run passed. A second run
adds an invalid-BAC sequence assertion to the rejected-report check and also
passes; no additional error/exception log lines appear relative to v188. Complete
previous output directories were archived before each native run.

The native fixture imports the audited four Calculate actions into an inert FSM,
using temporary test globals and restoring the original global array afterward.
It exercises the actual needs reporter, reliable guest send, host report handler,
profile memory, reconnect-offer builder and UI needs mapping. Tests cover ambient
isolation, native progression, known zero/below-zero, absent/nonfinite sources,
one-time deferred restore, pending cancellation, first-zero/wrapped sequences,
forged/stale/duplicate/malformed rejection and legacy profiles. Guest-save
protection is held during the profile checks, so no profile files are written;
the pure codec's migration/serialization is exercised in Net tests across all
legacy widths and optional-field combinations, including a German locale.

This is not a full spawn picker, loading/respawn sequence, native save/reload,
physical passenger/driver interaction, rendered scene or two Steam clients.
Passenger heat delivery, full seat/door flows and live winter-survival convergence
remain open. In a live check, let a guest's warmth move well away from nearby air
temperature, leave/rejoin, choose the last position and verify warmth is restored
without an abrupt ambient change; repeat in cold/warm cabins and after driver
handoff. Legacy profiles should retain other needs while leaving unknown warmth
at the native initial value. No commit, push, release, installer, installed-game
deployment or personal-save use was performed.

### Passenger cabin heating (protocol 190, unreleased)

Local seated passengers now feed their car's current available cabin temperature
into both native BodyTemp::Calculations reader branches: Get temp (rain/roof
temperature) and Source (nearest heat-source temperature). A postfix runs after
the ordinary native GetFsmFloat.DoGetFsmFloat read and replaces only the local
Temperature result. Native Calculate, clothing, sweat and timing retain body
progression. No source objects, search radii, target operands, reader caches,
PlayerIn, driver ownership or global PlayerTemp are written by the hook.

Eligibility requires an active session, a living enabled local passenger with a
valid seat, a registered vehicle and the player physically parented under it.
Host passengers additionally require the host's accepted seat ledger. Guest
seating retains its existing optimistic behavior until explicit rejection or
ejection. The current climate producer reads its validated finite native
Data.InteriorTemp, quantized to the same -40..40 °C representation used on the
wire (maximum in-range error about .157 °C). Other passengers use a current-owner
accepted available sample while its existing three-second hold remains live.
Exit, invalid seat, death, disconnect, handoff, expiry and missing/nonfinite source
restore native lookup on the next body read; this does not rewrite the scratch
input midway through a native calculation.

VehicleClimate 61 assigns existing flag bit 3 (8) to cabin-temperature
availability without changing its 23-byte layout. Missing/nonfinite native data
clears the flag and captures neutral byte 128; available zero is distinct from
missing data. Receivers ignore any unavailable CabinTemp byte and skip cabin
presentation writes while continuing other climate fields. Existing source
authentication, owner checks, wrap-aware ordering, relay, snapshot and final-send
paths retain the flag. Protocol/manifest become 190; mod version stays 0.1.33,
no IDs are allocated and next free ID remains 201.

The optional catalog profile uses player-relative body/rain paths so passenger
parenting cannot invalidate absolute PLAYER paths. Both reader states, action
identity/type/enabled/one-shot signatures, source references, literal FSM/variable
names and local output must match. A failure detaches only passenger heating,
records a throttled diagnostic and permits a later binding retry. Session/scene
cleanup unregisters both native readers.

Validation passes **3,503 Net tests** (36 new), **18 launcher tests** and
**3,580 native checks** (72 new; all 3,508 v189 labels and multiplicities retained).
Core Debug/Release, both Net targets, native probe and launcher build with zero
warnings/errors. The first full native run passed with no additional
error/exception log lines relative to v189. Native execution uses Debug in the
disposable game and isolated Wine prefix with all 54 existing flags; Release is
build-only. The complete predecessor output was archived before execution.
Source, launcher and disposable Core/Net/catalog/manifest hashes match; source
delta, audited inputs and logs are recorded in
`build/passenger-heating-smoke/result.json`.

The read-only game audit extracted 21 FSMs, four array lists and 14 transforms
from build 23268598. It confirms native closest-source distance selection and
small cabin source radii; it does not prove which physical seat locations fall
inside them. The native fixture imports Get temp, Source and Calculate into an
inert body FSM with inert rain/nearby heat sources and a temporary restored
PlayerTemp global. Corris, Sorbet and taxi fixtures exercise real native readers,
body arithmetic, climate capture, authenticated packet routing/relay and accepted
snapshots. Cases cover cold/zero/absent/nonfinite temperatures, source-cache
changes, all three seat indices, rejected/invalid/exited seats, death, expiry,
handoff, changed actions/operands, fallback and teardown/rebinding. Seats and
physical parenting are arranged by the fixture; it does not drive the full seat
controller, nearby-source selection, clothing/sweat branches or continuous timer.

Live acceptance still needs two players entering/leaving cold and warm cars,
swapping drivers, reconnecting/respawning and opening doors while checking local
warmth progression. The full seat/door/load flow, rendering, prolonged winter
survival and two Steam clients are not covered by these headless checks. No
profile or personal save is used by the new fixture. No commit, push, release,
installer or installed-game deployment was performed.

### Passenger death and respawn (protocol 191, unreleased)

The passenger controller previously kept its seated flag and frame-by-frame pin
through death. Host death records also left the passenger seat ledger and observer
anchors occupied. A dead rider could remain tied to the car, block another player
from sitting, or have an old seat carried across respawn. The new lifecycle path
retires occupancy without resetting the connection's seat-message history.

Local PlayerDeathHook death/group-death and respawn notifications immediately
release the passenger controller. Ordinary exits, forced exits and matching host
corrections share that idempotent release. It captures the vehicle parent at entry
so it can detach even if the vehicle index disappears. It restores parenting and
upright rotation only while PLAYER remains attached to that captured parent;
if native respawn already moved the player, the new parent, pose and rotation stay
intact. A previously enabled CharacterController is restored if it still exists;
components destroyed by the game are never recreated. Dead players cannot enter,
and LateUpdate checks local death/session status before applying its seat pin.

The host retires a dead player's canonical occupancy before broadcasting the
existing PlayerDeathEvent. Observers retire that player's avatar anchor on the
event. Respawn retires any residual seat before applying the returned pose; entry
then requires a new living claim and the usual fresh proximity proof. A continuing
seat claim cannot bypass the dead-player check. Rejected dead claims still consume
their sequence; observer dead-player reports advance their dedup baseline without
creating anchors. Other living passengers keep their seats. Permadeath retires
all local/remote occupancy and marks connected remote records dead, even before
any local native group-death effect can be located or run.

PassengerSeatLedger.RetirePlayer/RetireAll clear occupancy only. The normal
ForgetPlayer/Clear paths still reset history for a disconnected player or new
session. Observer retirement likewise preserves sequence history; complete
session cleanup clears that history even when death has already emptied the
seat map. Existing authenticated death/respawn routing remains unchanged, so a
peer cannot choose another victim by forging PlayerId. The reliable death event
is the terminal seat notification; no new synthetic seat sequence is invented.
Protocol/manifest become 191 because these are message-semantic changes. All
layouts and IDs stay unchanged, mod stays 0.1.33, next free ID remains 201.

Validation passes **3,509 Net tests** (six new), **18 launcher tests** and
**3,597 native checks** (17 new; all 3,580 v190 labels/multiplicities retained).
Core Debug/Release, both Net targets, native probe and launcher build with zero
warnings/errors. The first full native run passed with no additional
error/exception log lines relative to v190. Native execution used Debug and all
54 existing flags in the disposable game and isolated Wine prefix; Release is
build-only. The full predecessor output directory was archived before launch,
and source/launcher/disposable production payloads match. Audit, scoped source
delta, hashes and logs are recorded in `build/passenger-lifecycle-smoke/result.json`.
The initial probe compilation identified two inaccessible test-only conversion
helpers; explicit Net values fixed them before native execution.

A fresh read-only build-23268598 audit confirms that Systems/Death::Activate Dead
Body destroys FPSInputController, CharacterMotor and CharacterController in
State 3; later Take photo is the existing mod death notification boundary. The
new probe uses only the audited FSM name, state names and variables, with every
vanilla action and transition removed. It installs the real PlayerDeathHook,
executes those hook entries, and drives the real PassengerController Enter,
LateUpdate, Exit and retirement methods against inert seat-anchor objects and a
real Unity CharacterController. A test explicitly destroys that component before
death notification. Actual authenticated packet handling, canonical ledger,
remote anchors, respawn pose handling and terminal broadcasts are exercised with
captured in-process transport. The group-death event test marks the local hook
already dead so no native death/permadeath effect is triggered.

These checks cover moving/tilted-car pinning and release, original disabled
controllers, host/guest death, destroyed controllers, death between updates,
respawn into a new parent, disappearing vehicle indices, session end, duplicate
retirement, dead keepalives, forged/unknown senders, observer cleanup, fresh
post-respawn entry and group retirement. They do not execute native death effects,
death camera behavior before Take photo, save deletion, a complete loading/respawn
sequence, player input/door interaction, physical collision or two live Steam
clients. Live acceptance still needs a passenger death and respawn during a cold
or warm drive, another player taking the vacated seat, and reentry after revival.
No profile/personal save, installed-game deployment, commit, push, release or
installer was used or created by this change.

### Passenger vehicle discovery recovery (still protocol 191, unreleased)

Passenger discovery previously cached each network vehicle ID once. A replacement
body with the same ID could remain invisible to the controller while seats and
remote avatar anchors stayed attached to the old body. A removed vehicle could
also retain its cached geometry. Discovery now compares cached bodies with the
current WorldSyncManager vehicle registry on each scan, removes obsolete entries,
and resolves the existing native anchors on replacements. Unchanged bodies retain
their seat bindings; bodies missing required anchors retry on subsequent scans.

Removing cached geometry releases a local rider through the existing forced-exit
path, preserving world position and restoring the original surviving controller
and parent. It sends the existing ordered SeatNone and requires deliberate entry
into the replacement. It does not teleport the rider into a new object. Accepted
remote seat membership and message history survive geometry replacement so the
same logical car can acquire new avatar anchors without an invented seat claim.
Missing geometry provides no remote anchor. Host occupancy for a missing car is
retired through the existing rejection of its next validated claim or keepalive.

Host validation forces this refresh before checking the current car's existence
and new-entry proximity, even when an older body remains alive or the periodic
scan is not yet due. An already accepted exact seat still bypasses fresh entry
proximity on keepalive, preserving the moving-car contract. Dead/disabled requests
remain rejected before this path. Message framing, layouts, semantics, IDs and
channel use are unchanged: protocol/manifest stay 191, mod 0.1.33, next free ID 201.

Validation passes **3,509 Net tests**, **18 launcher tests** and **3,613 native
checks** (16 new; all 3,597 preceding labels/multiplicities retained). Core
Debug/Release, both Net targets, native probe and launcher build with zero
warnings/errors. The full native run uses Debug with all 54 existing probe flags
in the disposable game and isolated Wine prefix. Release is build-only. Final
source/launcher/disposable payload hashes match; no additional error/exception
log lines appear relative to the preceding run. Evidence and scoped source delta
are in `build/passenger-seat-discovery-smoke/result.json`.

The first run failed one new proximity fixture. A diagnostic rerun proved that
Unity 5 had changed Rigidbody.position from 10000 to 10100 while Transform.position
remained at 10000 until the next physics tick. The synchronous fixture now moves
the Transform and asserts separation before sending a claim, separately checking
current-body identity and rejection. All 16 new checks then passed without any
further production change. Both failed runs and their fixture sources/output are
archived. Initial test-only object-comparison warnings were fixed before execution.

The new native probe installs inert Corris/Sorbet objects in the real world item
registry and runs the actual discovery, host packet validation, seat entry/exit,
normal Update and remote anchor paths with captured in-process transport. It
covers inactive anchors, live and destroyed body replacement, missing/null or
nonvehicle registration, unsupported replacements, late anchors, proximity versus
accepted keepalive, canonical rejection, host/guest local release, sequence
history and unaffected occupants in another car. The preceding lifecycle fixture
now registers its inert car too, so its validation uses the same world lookup.

A fresh read-only build-23268598 audit includes 169 FSMs and 646 transforms.
Corris/Sorbet retain their existing MassPassenger, DriveTrigger and bench/rear-seat
anchor lookup. Taxi's passenger anchor differs and its NPC/seat flow has not been
validated; PassengerController still supports Corris and Sorbet only. Earlier
passenger-heating fixture cases for taxi exercised manually arranged thermal
inputs, not actual taxi seat discovery or player entry. No catalog or installed
asset was changed. The new fixture uses structural anchor objects, not a full
reconstruction of the native vehicle hierarchy or physics.

Full scene reconstruction, collision/door/key input, loading/respawn, rendering,
continuous winter survival and two live Steam clients remain acceptance work.
Live testing needs both players to enter, ride and exit a supported car, observe
the same occupants, and repeat after scene reload/reconnection. No personal save,
profile, installed-game deployment, commit, push, release or installer is used by
this change.

### Native tyre pressure units (still protocol 191, unreleased)

The native Corris TirePressure.Data Pressure and PressureOptimal defaults are
190. Its TIRES transition enters WheelFriction, whose eight SetProperty actions
copy those values directly into all four Wheel.pressure/optimalPressure fields.
Core previously passed native Pressure to a bar encoder: 190 saturated to wire
255, and replay wrote 2.55 into the native variable. This was a units error at the
native boundary, despite the pure bar round-trip checks passing.

Condition capture now uses EncodeNativeTirePressure and replay uses
DecodeNativeTirePressure. The existing wire byte remains hundredths of a bar,
equivalent to one native kPa per byte. Default 190 becomes byte 190 and returns as
native 190; explicit bar helpers still turn 1.9 bar into that same byte. Native
fractional values round to nearest kPa, retaining the existing 0–255 wire range
and nonfinite mapping. The source value itself is not changed during capture.
Owner publication, host snapshots, parked checksums and remote apply use the
same corrected conversion. Protocol/manifest stay 191, mod 0.1.33, next free ID
201; framing, layout, authority and sequence contracts are unchanged.

Validation passes **3,527 Net tests** (18 new), **18 launcher tests** and
**3,626 native checks** (13 new; all 3,613 preceding labels/multiplicities
retained). Both Net targets, Core Debug/Release, probe and launcher build with
zero warnings/errors. Native execution uses Debug with all 54 existing flags in
the disposable game/isolated Wine prefix; Release is build-only. The first native
run passed with no additional error/exception lines relative to the predecessor.
The preceding complete output was archived before launch, and final production
source/launcher/disposable payload hashes match. Evidence is recorded in
`build/tire-pressure-smoke/result.json`. The initial Net run identified a test-only
packet header expectation (message IDs are two bytes); correcting the expected
16-byte packet and pressure offset 9 made all tests pass without a production
change. The earlier bar-based regression suite remains intact.

The native input is `build/tire-pressure-smoke/tire-pressure-probe.json`, copied
to the disposable game's root before running the existing vehicle-state flag.
It contains the single audited TirePressure.Data row and asset metadata, with
unrelated global definitions omitted. The probe builds typed variables/states
from that row and intentionally never loads its native actions.

A fresh read-only build-23268598 audit contains 33 wheel, tyre-pressure and
gearbox FSMs. The probe imports only TirePressure.Data's audited typed variables
and state graph, with all native actions omitted. It runs the real condition
probe, owner publication, authenticated observer packet routing, apply, snapshots
and repeated recapture in the disposable game with captured in-process transport.
It verifies native 0/1/127/190/230/254/255, fractional rounding, saturation, local
owner protection and snapshot/delta independence. PressureOptimal, Enabled and
the active FSM state remain unchanged. No native wheel component, physics action,
TIRES/ENABLEPRESSURE event, profile or personal save is used by this fixture.

This corrects the native scalar boundary; it does not complete tyre/drivetrain
condition sync. The audit proves Condition.Health is scratch read each frame from
ThisTire.Data.TireHealth. Flat friction writes that referenced TireHealth to zero;
RIM is local to Check rim, not a global transition. GearboxDamage.DamageType is
also scratch read from the fitted gearbox, and Reverse subtracts native Wear.
Those consumer/writer paths need authority and guest-original protection before
native replay can be treated as safe and complete. TIRES copies pressure into
wheel properties; ENABLEPRESSURE toggles Enabled. This fix calls neither, so it
does not claim to apply pressure to already-running wheel physics or enable the
native pressure simulation.

Stream gaps are also open: parked cars do not publish ordinary condition state,
unowned guest claims can pass the current host gate, and duplicate/handoff history
needs the established vehicle-stream policy. These findings are recorded in the
reopened roadmap 2.2. Follow-up must close those gaps and test tyre changes,
repairs, flat/rim transitions, driver handoff and join/reconnect using two live
players with different saves. No commit, push, release, installer or installed-game
deployment was performed.

### Condition stream ownership (protocol 192, unreleased)

VehicleCondition previously allowed a guest's report while the car was unowned,
relayed reports even when native apply rejected them as stale, treated sequence
zero as an unset baseline and forgot an owner's history whenever another sender
appeared. A stale report could overwrite pressure/health or repeat a native tyre
event. Host snapshots also sampled host scratch during guest ownership and
consumed the host's live condition counter.

The new condition stream policy requires the established owner after peer
identity validation. It rejects unowned/former-owner reports, locally owned or
already-parented pre-claim driver targets, invalid vehicle/sender identity,
contradictory puncture/rim flags, duplicate/stale/half-range sequences and guest
snapshot sentinels before native writes. Each vehicle keeps separate sender
histories through handoffs; zero is an ordinary first sequence and deduplicates
normally thereafter. Only an accepted apply is relayed. Guests continue accepting
world messages only from the selected host, then check current vehicle ownership.
Readmission clears the selected sender's history and cached report; session clear
resets all accepted state, outgoing counters and publication baselines.

Accepted reports are copied. While a guest owns a car, host join/resync snapshots
use only its accepted report and return no condition if that source is missing
or belongs to another owner. Parked snapshots retain native capture. The host
stamps snapshots with source 0 and sequence 65535 without consuming live counters
or delta baselines; guests accept those only on unowned/host-owned cars, without
advancing live dedup history or overriding an active guest driver. Regular live
sequences skip 65535. Native apply's corrected pressure units remain unchanged.

A departing owner sends final condition on reliable-ordered before its final
ItemTransform, bypassing unchanged-state and timer suppression. The normal item
release path performs this alongside its existing final ignition and climate
messages. It preserves current ownership until all final state has been sent.
Ordinary and final sends share the existing guarded probe/read path, so a failed
native lookup is contained before release. Ordinary publication still requires
local ownership; parked host publication is not enabled by this change.

These are message-semantic changes: protocol/manifest advance to 192. State 65
retains its existing fields and 14-byte payload (16-byte packet). Mod version
stays 0.1.33 and next free ID remains 201.

Validation passes **3,570 Net tests** (43 new), **18 launcher tests** and
**3,647 native checks** (21 new; all 3,626 preceding labels/multiplicities retained).
Both Net targets, Core Debug/Release, probe and launcher build with zero warnings
or errors. The first full native run passed with no additional error/exception
lines relative to the predecessor. Final review routed owner/final capture through
the existing guarded read path; the full native suite was rerun for that change. It used Debug and all 54 existing flags in the
disposable game and isolated Wine prefix; Release is build-only. The full preceding
output was archived before launch and final source/launcher/disposable payload
hashes match. Evidence and scoped source delta are in
`build/condition-stream-smoke/result.json`. Initial builds identified an
unqualified condition type in WorldSyncTypes and a test-only nonexistent session
state name; both were corrected before native execution.

The native probe reuses the preceding read-only build-23268598 audit and curates
six FSMs into `build/condition-stream-smoke/condition-stream-probe.json`: pressure
Data, gearbox Damage and the four wheel Condition graphs. It instantiates typed
variables and audited state transitions on inert objects, omitting every native
wear/physics action. Accepted PUNCTURE is checked only through the real local
State 1 → Check rim transition; the next native part-dependent branch is not run.
The real item registry, authenticated session dispatcher, host relay, scalar apply,
snapshot builder, teardown and ItemWorldSync release order are exercised using
captured in-process transport. The earlier 13 pressure checks now use guest
context when replaying host state, so they obey the stronger authority contract.

This does not complete native condition simulation: wheel Health and gearbox
DamageType remain scratch reads, flat/rim transitions need native consumer/writer
isolation and saved-original protection, and normal parked publication remains
open. The tested fixture cannot execute the native TireHealth/Wear mutations or
wheel physics. Full physical tyre changes/repairs, flat/rim behavior, driving,
join/reconnection and two live Steam clients still need acceptance. No profile,
personal save, installed-game deployment, commit, push, release or installer was
used or produced by this change.

### Guest tyre and gearbox write protection (protocol 193, unreleased)

The build-23268598 condition audit identified nine native mutations outside the
existing guest engine guard. Each Corris WHEELc_FL/FR/RL/RR Condition graph runs
State 1#11 SubtractFsmFloat(ThisTire::Data.TireHealth) every frame/per second and
Flat friction#0 SetFsmFloat(ThisTire::Data.TireHealth, 0) on entry. The latter can
run following a received PUNCTURE. GearboxDamage.Damage Reverse#6 subtracts
0.0525 from db_Gearbox::Data.Wear. These destinations are native mounted-part
scalars belonging to the guest's world; remote simulation must preserve them.

The catalog adds these five exact-path writer rules to guestEngineProtection:
16 writer graphs, 84 scalar actions, the existing distributor pose action and
23 paused graphs. No new suppression mechanism is introduced. The existing
admission scan and native FsmState.OnEnter guard validate state/index/type and
named destination, disable the selected actions, and retire already-active
writers. Other actions remain eligible. A replacement action with an unrecognized
destination pauses only the affected graph; restoring its audited signature
recovers without replaying its saved-part write. Solo/host preparation leaves
these actions enabled. Protection remains latched after guest disconnect under
the existing GuestSaveGuard policy.

Protocol/manifest advance to 193 because guest simulation behavior changes.
The VehicleCondition layout, authority, sequence/snapshot rules and all message
IDs remain unchanged from v192. Mod stays 0.1.33; next free message ID is 201.
No release, commit, push, installer or installed-game deployment was performed.

Validation passes **3,575 Net tests** (five new catalog cases), **18 launcher
tests**, and **3,675 native checks** (28 new; all 3,647 preceding labels and
multiplicities retained). Both Net targets, Core Debug/Release, probe and launcher
build with zero warnings/errors. All 54 existing native flags ran in the
disposable game and isolated Wine prefix. Release is build-only. Source, launcher
and tested disposable production payload hashes match. Evidence is recorded in
`build/tyre-write-smoke/result.json`.

The native fixture extends the preceding complete guest-engine-probe.json with
five rows from the read-only 33-FSM condition audit. Numeric exponents are
normalized for the legacy parser with parsed-JSON equivalence checked. The
importer executes the actual Assembly-CSharp SubtractFsmFloat/SetFsmFloat actions
and the wheel Health GetFsmFloat readers against inert, independent tyre targets.
It retains the audited wheel state transitions. Check rim's tyre-type selector,
Sound's audio activation, wheel-component properties and other unrelated native
actions are omitted; the test supplies SOUND after the real PUNCTURE edge and
uses the native FINISHED/FIXED edges. No wheel physics or real saved part runs.

Each wheel first demonstrates solo flat-entry zeroing and ongoing per-second
wear before guest admission. Guest checks then demonstrate retirement of that
already-running writer, continued native Health reads, puncture/repair reentry,
re-enabled-action containment, and changed-destination pause/recovery. The
gearbox proves the native 0.0525 baseline subtraction and the corresponding
guest entry/re-enable/signature protection. The existing complete engine,
condition-stream, shopping-bag and other suites remain included.

The first run passed all 28 new checks but failed the older host rename/delete
test because the preceding output directory retained its destination file.
The whole preceding directory was already archived; the failed run was also
archived, then its output directory was moved aside. Repeating all 54 flags
against a fresh disposable output directory passed without any source change.
The final native log has no added error/exception lines relative to the previous
successful baseline; the existing intentional Cooling Reset#10 negative-test
diagnostic remains.

This protects the nine audited writers; it does not establish shared native tyre
or gearbox condition. Health still reads the guest's native TireHealth, and
DamageType still reads native gearbox data. Their accepted host inputs need
projection without changing guest originals. RIM is local to Check rim, and Rim
friction has no FIXED transition. Physical flat/rim/replacement/repair behavior,
pressure application to Wheel components, authoritative wear under guest driving,
parked publication and two live players with different saves remain roadmap 2.2
work. Do not treat these fixture results as driving, save/reload or Steam-session
acceptance.


### Observer tyre health inputs (protocol 194, unreleased)

Writing Condition.Health once did not establish the native tyre input. Healthy
State 1#12 and Flat friction#3 read ThisTire::Data.TireHealth every frame, replacing
received health with the guest's saved mount value. The v193 write guard preserved
the original but did not change those reads.

Each wheel's existing guestEngineProtection writer rule now has a wheelHealth
index and two reader state/index bindings. The parser requires both healthy/flat
reader entries and both corresponding saved-tyre writer guards; reader/writer
slots cannot overlap. Native admission validates GetFsmFloat type, canonical
ThisTire reference, Data/TireHealth literals, canonical Health output and
EveryFrame alongside existing writer protection. Reader identity/enablement is
part of binding validity. A missing/replaced/disabled reader fails preparation;
changed operand fields fail projection. The affected graph pauses and can recover
after its audited signature is restored.

The existing GetFsmFloat hook supplies a byte-valued shared health only when the
guest is connected, the wheel belongs to the registered live vehicle body, the
accepted condition belongs to that vehicle and its current owner, and the local
player is neither the owner nor an already-parented pre-claim driver. Unowned
cars require accepted host-0 state. Known zero remains zero. This low-rate state
has no new TTL. Driver/host operation, missing data, ownership changes before a
new report, disconnected sessions, removed/replaced bodies and cleared streams
retain native lookup. A host snapshot can supply an unowned/host-owned observer
without consuming its live sequence counter.

Shared values are written only to the validated local Health scratch. Outputs
aliasing the referenced saved part or a global are rejected. The action source,
ThisTire reference and native cached FSM/object are unchanged, allowing native
lookup to resume immediately when observer eligibility ends. Existing guest
saved-part writers remain disabled. A scoped copied ApplyingVehicleCondition
supplies synchronous native reads during received PUNCTURE entry; it is restored
in finally, and the ordinary accepted copy is committed only after application.

Protocol/manifest are 194 for the native observer semantics. Message layouts,
IDs, v192 condition ownership/dedup/snapshot rules and mod version 0.1.33 remain
unchanged; next free message ID is 201. No commit, push, release, installer or
installed-game deployment was performed.

Validation passes **3,602 Net tests** (27 new), **18 launcher tests**, and
**3,701 native checks** (26 new; all 3,675 prior labels/multiplicities retained).
Both Net targets, Core Debug/Release, probe and launcher build without warnings
or errors. All 54 existing native flags ran against the disposable game and
isolated Wine prefix; Debug is native-tested and Release is build-only. Final
source/launcher/disposable production payload hashes match. Evidence is in
`build/tyre-health-read-smoke/result.json`.

The new wheel-health-probe.json has 13 rows from the unchanged read-only
build-23268598 audit: four Condition graphs, four wheel Data graphs, four mounted
wheel Data graphs and TirePressure.Data. Numeric exponents are normalized outside
strings for the legacy parser, with JSON equivalence checked. The fixture uses
real hierarchical paths and inert mounted-part targets. It executes the native
health readers, protected wear/flat-zero writers, healthy grip arithmetic,
Check rim GetFsmInt/IntCompare branch, and Sound activation against an inert
object. Native local transitions perform PUNCTURE → Check rim → Sound → Flat
friction during actual authenticated condition application. Wheel component
properties, physics, health-threshold/repair compares and all mounted Data actions
are omitted. No actual saved part or physical wheel is used.

Tests cover all four healthy and flat readers, repeated ticks and grip math,
known 0/255, the in-flight puncture value, forged/duplicate packets, owner change,
host snapshots, driver/pre-claim exclusion, missing/cleared data, host/disconnect,
body replacement/removal, native cache preservation and changed-reader recovery.
The first full run passed 3,699 checks. Final review added admission/identity
checks for missing/disabled reader slots and two regression cases; the full
second run passed 3,701. Entire output directories were archived and moved aside
before each launch. No added error/exception lines occur relative to the prior
successful baseline; the existing intentional Cooling Reset#10 diagnostic remains.

This is observer input reconciliation, not complete tyre simulation. Guest-driver
inputs still need shared bootstrapping; host-side tyre wear under guest driving,
durable gearbox inputs, wheel changes and repairs, correct RIM/flat restoration,
pressure application to Wheel components and parked publication remain open.
Protocol 195 below extends projection through an explicitly approved release.
Full driving, different personal saves, live Steam two-player and load/reconnect
acceptance remain roadmap 2.2 work.

### Parked observer tyre health (protocol 195, unreleased)

The accepted final condition arrived before the final pose, but clearing vehicle
ownership made the former driver's health ineligible for native observer reads.
The next tick restored the guest's own saved health. A successful final release
now captures an independent copy of that established owner's accepted condition,
bound to the same live Rigidbody. Final poses omit FlagVehicle; registered vehicle
identity and matching accepted condition establish the car.

Connected guest observers can read this retained health while the car is unowned.
The copy preserves its original sender and sequence; it grants no ownership,
admits no unowned guest reports and generates no condition packets. Rejected
poses, quiet-stream timeouts and unowned finals cannot approve a parked record.
Duplicate finals and unowned resync poses do not erase an existing approved one.
Host condition updates and accepted new claims retire it. Former-driver
departure/readmission preserves the approved world result, while session clear
removes it. Replaced/unregistered bodies and local/pre-claim drivers cannot read
the old record. Native saved tyre values and reader sources/caches remain intact.

A new local claim clears the previous incoming condition and resets the outgoing
delta gate, ensuring a fresh baseline even when its captured values are unchanged.
The sender's condition sequence counter and receiver histories remain intact.
Protocol and compatibility manifest advance to 195 for these simulation semantics;
message layouts/IDs, mod version 0.1.33 and next free ID 201 are unchanged.

Validation passes **3,630 Net tests** (28 new), **18 launcher tests**, and
**3,719 native checks** (18 new; all 3,701 previous labels/multiplicities retained).
Both Net targets, Core Debug/Release, probe and launcher build with no warnings or
errors. All 54 native flags ran in the disposable game and isolated Wine prefix.
Debug is native-tested; Release is build-only. Source, launcher and disposable
payload hashes match. Evidence: `build/parked-tyre-health-smoke/result.json`.

The unchanged 13-row native fixture exercises all four parked readers, known-zero
puncture, copied records, departure, host correction, same/other/local new claims,
fresh unchanged-value publication, forged reports, duplicate/unowned finals,
invalid poses, timeout, body replacement/removal and teardown. It also captures
actual claim/final packets from the local ownership path and replays them through
the selected-host dispatcher into an observer. This checks final-condition-before-
release ordering and the sender's actual flag combination. The first run exposed
an out-of-range test player; review also caught an incorrect FlagVehicle
requirement. Both were corrected before the complete second run. Each prior
output directory was archived and moved aside before launch. No additional
error/exception lines appear; the intentional Cooling Reset#10 diagnostic remains.

This preserves observer presentation through an approved release. It does not
implement ordinary parked host publication or durable host tyre wear. Guest
driver bootstrap, gearbox inputs, full flat/rim/repair and pressure physics remain
open. The inert fixture does not execute physical wheel components or mounted
Data writers. Actual driving, different saves, load/reconnect and two live Steam
players still need acceptance. No personal saves, installed-game deployment,
commit, push, release or installer were touched.

### Native wheel pressure application (protocol 196, unreleased)

Received VehicleCondition pressure previously changed TirePressure.Data.Pressure
without updating Wheel.pressure. The fresh audit also shows that vanilla TIRES
only enables the FL pressure/optimum pair; its other six property writes are
disabled. Simply sending that event cannot synchronize all four wheels.

The new `vehicleTirePressure` catalog profile records the exact source, event,
state, four wheel identities, eight action indices and native enabled flags.
Before dispatch, the mod validates the complete state: native SetProperty types,
one-shot scheduling, canonical source/target references, pressure/optimalPressure
members, finite positive optimum, event destination and absence of shadowing
local transitions. Missing/duplicate sources, changed properties, disabled graphs,
aliased operands or wrong/missing wheel components defer the complete update.
Every target is checked before any property write.

For an eligible accepted observer condition, the mod briefly enables all eight
audited one-shot writes and sends TIRES. A finally block restores their original
enabled flags and removes originally disabled actions from active scheduling.
It verifies the resulting pressure and optimum on all four components. Normal
native TIRES retains its FL-only behavior afterward. ENABLEPRESSURE, which flips
simulation enablement, and SETWHEELS, which invokes wheel setup, are never sent.
The native optimum remains local and unchanged; it is copied to each wheel just
as the audited actions specify.

Current-owner conditions and v195's approved parked record use the same admission
rules as shared health. Local/pre-claim drivers, disconnected sessions, unrelated
cars and stale ownership cannot apply pressure. Host observers can apply the
authenticated current guest driver's accepted pressure. Accepted updates apply
immediately; ordinary drift checks run at most once per second and unavailable
bindings retry after five seconds. A late target can therefore recover without
another condition packet. Session clear removes the binding and deferred state.

Protocol/manifest advance to 196 for these native simulation semantics. Message
layouts/IDs, mod 0.1.33 and next free message ID 201 remain unchanged. No personal
saves, installed-game deployment, commit, push, release or installer were touched.

Validation passes **3,667 Net tests** (37 new), **18 launcher tests**, and
**3,747 native checks** (28 new; all 3,719 previous labels/multiplicities retained).
Both Net targets, Core Debug/Release, probe and launcher build without warnings
or errors. All 54 native flags ran in the disposable game and isolated Wine
prefix. Debug is native-tested; Release is build-only. Source, launcher and
disposable payload hashes match. Evidence: `build/tyre-pressure-physics-smoke/result.json`.

Native checks cover exact pressure values on four real components, retained
simulation enablement/custom optimum, host receipt of guest-driver state,
duplicate/stale/forged rejection, driver/pre-claim exclusion, parked continuity,
bounded retry and immediate new updates, pressure/optimum drift, late source and
wheel readiness, invalid action/property/enablement/event signatures, aliases,
duplicate source, body replacement/unregistration and teardown. The first full
run and two focused diagnostic runs identified the initially missed disabled
action flags. After the scoped replay correction a complete run passed 3,745;
final cleanup and two additional binding checks passed 3,747. Entire output
directories were moved aside before each run. No new error/exception lines
remain relative to the previous baseline; the intentional Cooling Reset#10
diagnostic is unchanged.

One intermediate Net run had five inconsistent JSON parse failures across
unrelated catalog tests. The catalog source/output hashes matched afterward,
and two subsequent complete runs passed all 3,667 tests without further Net or
catalog changes. The intermediate log is preserved; its cause was not reproduced.

The one-row fixture preserves the audited pressure graph and enabled flags,
binding its eight real SetProperty actions to inactive real Wheel components
under the exact Corris hierarchy. Component Awake/Start/Update and wheel physics
do not run. Native type inspection confirms zero pressure has dedicated branches;
the fixture checks exact 0–255 pressure inputs, not their full driving behavior.
The native startup stiffness calculation is not invoked or changed by this work.
Driver-side bootstrap, authoritative tyre wear, complete flat/rim/repair behavior,
ordinary parked host publication, real driving, different saves and two live Steam
players remain roadmap 2.2 work.


### Condition availability and late discovery (protocol 197, unreleased)

Finding TirePressure previously latched condition discovery as complete even
when no wheels had loaded. Missing health then became a known zero on the wire,
and missing/replaced bindings never recovered. Condition capture now retries
hierarchy discovery every five seconds and tracks availability independently for
pressure, drivetrain damage and each of the four wheels. Body changes trigger
immediate discovery; cached source liveness, canonical variable identity, global
aliases, finite values and stable wheel state are checked between scans. Startup
and branching wheel states are unavailable. Duplicate sources remain unavailable
until ambiguity is removed. Zero from a valid ready source remains known zero.

VehicleCondition (65) appends an availability byte: pressure=1, drivetrain=2,
FL=4, FR=8, RL=16, RR=32. The high two bits are reserved and rejected on read,
write and admission. Each wheel bit covers health and both discrete flags. A zero
mask explicitly withdraws all fields. Owner publication detects mask-only
changes, including total binding loss; final release includes this withdrawal
before the final pose. Existing offsets, ownership rules and sender sequence
history stay intact. Copies, host snapshots and approved parked records retain
the mask. The packet grows from 16 to 17 bytes; protocol/manifest are 197, mod
version remains 0.1.33 and next free message ID remains 201.

Observers apply only the intersection of declared fields and ready native
bindings. Newly available bindings replay the eligible accepted or approved
parked result without another packet, sequence advancement or relay. Pending
reconciliation survives intervening capture/checksum reads. Temporary apply state
still supplies synchronous native health readers and is restored afterward.
Withdrawal retires shared reader input and pressure repair without writing zero
into unrelated native fields. Local/pre-claim drivers, changed ownership,
unapproved parked state and disconnected sessions remain excluded.

Vehicle checksums include availability and zero out undeclared values/flags.
Guest checksums intersect native readiness with the eligible authoritative mask,
so local data omitted by the host does not create permanent resync loops. They
still sample native values for declared fields and detect local drift or missing
targets. Host discovery is unaffected by a guest's partial mask. Capture and
checksums do not advance live publication counters or mutate accepted reports.

The native fixtures reuse the audited condition, wheel-health and TIRES graphs.
They preserve the real startup Wait in the new readiness check, exercise native
healthy/flat readers and pressure property actions, and replay actual packets
through the session handler. Full wheel driving/physics is not exercised. The
prior limits on native stiffness, driver bootstrap, durable host tyre/gearbox
wear, complete flat/rim/repair behavior, ordinary parked host publication,
different saves and two live Steam players remain open under roadmap 2.2.

Evidence is under `build/condition-readiness-smoke/`. The first full run found
two fixture assumptions (omitted native startup Wait and restart-on-enable) and
the previous release-order assertion's missing explicit withdrawal. After those
corrections and additional binding checks, all 3,775 native checks passed. Review
then found the partial-mask checksum convergence gap. Its first run exposed an
empty-condition path unnecessarily consulting session context and a checksum
fixture missing engine bindings; both were corrected. Interrupted staging and
intermediate output are preserved separately. Complete output directories must
be moved to a fresh archive path before every native run; copying them leaves a
rename destination that invalidates the save-file fixture.

Final validation passes **3,807 Net tests** (140 new), **18 launcher tests**, and
**3,777 native checks** (30 new; all 3,747 previous labels/multiplicities retained).
Both Net targets, Core Debug/Release, probe and launcher build with zero warnings
or errors. All 54 native flags ran against the verified final payload in the
disposable game and isolated Wine prefix. Debug is native-tested; Release is
build-only. There are no new error/exception lines against the v196 baseline;
the intentional Cooling Reset#10 diagnostic is unchanged. Source, launcher and
disposable payload hashes match. Final evidence and scoped source patch are in
`build/condition-readiness-smoke/result.json` and `change.patch`. No personal
saves, installed-game deployment, commit, push, release or installer were changed.


### Observer gearbox condition input (protocol 198, unreleased)

GearboxDamage::Damage previously reloaded its local saved DamageType each time
it entered `Damage type`, replacing the received VehicleCondition drivetrain
byte. Guest observers could therefore select a different native damage branch.
The new catalog `gearboxCondition` reader identifies `Damage type#0` GetFsmInt
on the exact Corris consumer and pairs it with the existing Reverse#6
SubtractFsmFloat saved-wear guard. This is the damage indicator, separate from
the transmission Type used by the starter interlock.

Guest protection validates the real private DoGetFsmInt boundary, the enabled
one-shot reader, literal Data/DamageType source, canonical db_Gearbox reference
and local DamageType output. The registered live consumer, current owner,
availability bit and approved parked record determine whether a shared value
can be read. Known zero is retained. Native source and warmed lookup cache are
untouched; the accepted result changes only the consumer's scratch integer.
Global/saved aliases, missing or disabled readers, changed signatures, moved
consumer identity and unprotected writer scheduling pause the consumer; restoring
the audited binding lets normal preparation recover it.

Host operation, local/pre-claim drivers, missing/withdrawn reports, changed
ownership, unapproved parked state, body replacement/unregistration and
disconnected sessions retain native reads. An approved final condition survives
parking and former-driver cleanup, and a new host correction supersedes it.
Reading never advances sequences, grants ownership or mutates accepted reports.
The existing reverse-gear wear write remains disabled on guests.

Protocol and launcher manifest advance to 198 for the changed native simulation
behavior. Message layouts and IDs, the 17-byte VehicleCondition packet, mod
0.1.33 and next free ID 201 are unchanged. Both peers must use protocol 198.

Validation passes **3,842 Net tests** (35 new), **18 launcher tests**, and
**3,806 native checks** (29 new; all 3,777 previous labels/multiplicities retained).
Both Net targets, Core Debug/Release, probe and launcher build without warnings
or errors. The initial complete native run passed. Final review tightened cached binding
validation so preparation cannot resume changed signatures or aliased outputs
until repaired; those cases now assert failed preparation while still broken.
The final full run uses all 54 flags in the disposable game and isolated Wine
prefix. There are no new native error/exception
lines against the preceding baseline; its intentional Cooling Reset#10 diagnostic
is unchanged. Debug is native-tested; Release is build-only. Source, launcher
and disposable payload hashes match. Results, hashes and scoped source patch are
under `build/gearbox-condition-smoke/`. No personal saves, installed-game
installation, commit, push, release or installer were changed.

The fixture imports the audited native damage GetFsmInt and IntCompare actions,
Delay Wait and protected Reverse subtraction, with inert saved Data and held
branch bodies. It checks native branch choices for 0, 1, 2, 3 and 255; it does
not run drivetrain physical failures, sound playback or gear-jump effects. Native
reader/cache isolation, packet admission, parked continuity, withdrawal,
pre-claim/ownership exclusion, changed signatures, removed/disabled readers,
aliases, teardown and recovery are covered. Fixture JSON numeric normalization
was corrected to preserve quoted strings and exact values before staging.

Driver input bootstrapping, durable host tyre/gearbox wear under guest driving,
complete flat/rim/repair and wheel physics, ordinary parked host publication,
different saves and two live Steam players remain roadmap 2.2 work.

### Tyre-reader safety (still protocol 198, unreleased)

Cached wheel-reader bindings previously admitted changed operands, and preparation
could resume readers with global/saved output aliases. Without shared state,
native fallback could write into a redirected saved output. Moved readers could
also keep projecting through their old bindings. All 23 initial regression cases
failed against the preceding production code in the disposable native runtime.

Both healthy and flat readers now revalidate their canonical source, output and
identity before recovery or native/shared reading. Missing outputs pause the
consumer before the native read can throw. Renamed/reparented consumers retain
their protection and recover only at the catalogued identity. Unrelated native
float readers retain their fast path. Capture and packet application also exclude
Health that aliases the referenced saved tyre, covering arrival before the next
protection scan. Healthy siblings continue; repaired bindings consume retained
accepted condition without requiring another packet.

This is an implementation fix to existing reader protection and availability:
protocol 198, message layouts, IDs and mod version 0.1.33 are unchanged. Evidence
and the scoped source patch are in `build/wheel-reader-safety-smoke/`.
The fixture uses the audited native healthy/flat GetFsmFloat actions and inert
saved tyre data. Driver bootstrapping, durable host wear, full flat/rim repairs,
running wheel physics and two-player testing remain unfinished under roadmap 2.2.

Final validation passes **3,842 Net tests**, **18 launcher tests**, and **3,833
native checks** (27 new; every one of the prior 3,806 retained). Both Net targets,
Core Debug/Release, the probe and launcher build with zero warnings/errors. The
final native run uses all 54 flags in the disposable game and isolated Wine
prefix; no new error/exception lines appear against the predecessor. Debug is
native-tested and Release is build-only. Source, staged and launcher payload
hashes match. No personal saves, installed-game files, release or installer were
changed; the work remains uncommitted and local.

### Guest condition claim inputs (protocol 199, unreleased)

Claiming a car previously cleared incoming condition, causing the guest's next
native healthy/flat tyre read or gearbox damage check to reload its own saved
value. The first outgoing condition could then report that value to the host.
The real physics-claim path now copies eligible accepted input before clearing
the previous owner, including same-body approved parked condition. The copy is
bound to the claimant, body and local lease. Protected native readers consume
its available tyre health and gearbox damage without rewriting saved parts or
their lookup caches. Capture uses the same input even before the first native
reader tick, intersecting retained availability with current native readiness.
Missing saved-part inputs remain unavailable in reports; pressure and discrete
wheel transitions still come from the native owner simulation.

Release sends the final condition before the final pose, then retires the input.
Accepted competing motion, body replacement and session reset also retire it.
Rejected reports cannot change the active copy; former-owner cleanup cannot erase
it. Sequence history remains intact and each new lease forces a first report.
Both peers require protocol 199; the existing 17-byte VehicleCondition packet,
message IDs, next free ID 201 and mod version 0.1.33 remain unchanged.

The native fixtures use actual interaction claims, seated takeovers of a remote
push owner, packet dispatch and settled releases. They execute the audited
healthy/flat GetFsmFloat reads and gearbox GetFsmInt/branch comparisons with inert
saved data. The earlier parked-claim regression now expects inherited health 30
instead of local saved health 90, while retaining its fresh-baseline/sequence
checks. Evidence and the scoped source patch are in `build/condition-claim-smoke/`.

This is the initial-input portion of driver handoff. Claims with no eligible
input and the pre-claim seated window retain native fallback; local release
retires the copy without treating an unacknowledged report as approved parked
state. New host-authoritative wear, post-release host confirmation, pressure
bootstrapping, full flat/rim/repair and running wheel physics remain unfinished.
Two-player acceptance is still required: join using different saved tyre/gearbox
condition, alternate driving after a host snapshot and approved remote release,
and compare the first reports and native damage behavior while preserving each
guest's saved parts.

Validation passes **3,875 Net tests** (33 new), **18 launcher tests** and **3,856
native checks** (23 new; all 3,833 previous labels/multiplicities retained). Both
Net targets, Core Debug/Release, the probe and launcher build without warnings
or errors. The native run uses all 54 flags in the disposable game and isolated
Wine prefix, with no new error/exception lines against the predecessor. Debug is
native-tested and Release is build-only. Final source, staged and launcher
payload hashes agree. Personal saves and installed-game files are unchanged;
there was no commit, push, release or installer build.

### Host-confirmed condition release (protocol 200, unreleased)

The former guest driver previously retired its shared input on release without
ever receiving the host's approval. Its native readers could then reload local
saved tyre health while the other guests retained the approved parked result.
The host now sends message 201 only to the authenticated releasing peer, after
accepting both its condition report and an established owner's final pose.
Confirmation contains the release sequence and accepted condition; it is 19
bytes including ID. Existing packets keep their layouts and the next free ID
is 202. Both peers require protocol 200; mod version remains 0.1.33.

The sender records its pending final report and pose before dispatch, then
requires matching player, vehicle, both sequences and body. Host approval may
arrive before the local release finishes; it waits until the car is unowned and
the local player is no longer driving. It then commits copied approved parked
condition, reconciles ready native consumers and retires the pending record.
Missing bindings recover without another packet. Confirmation neither replays
the final pose nor grants ownership. A new claim, accepted competing motion,
newer accepted condition, body replacement or teardown cancels old pending
approval. Duplicate, unsolicited, stale, wrong-channel and non-host messages
cannot revive it. An unacknowledged send never becomes approved by elapsed time.

Host snapshots and condition checksums now use same-body approved parked state
instead of native scratch that may already have reloaded another value. Guest
checksums still sample native readiness/values for declared fields, so physical
drift remains detectable. Unapproved parked cars continue native capture; later
claims/authority updates supersede the retained record. Ordinary parked native
repair/wear publication remains separate work and must explicitly supersede
approved state when implemented.

The fixture sends the actual guest claim, condition and final-pose packets
through host packet validation and returns the targeted encoded confirmation.
Separate host/guest item records share an inert native wheel fixture in one
process; this is not a two-process or live Steam test. It checks native reads,
late joins, condition checksum sources, missing bindings, early confirmation,
stale/forged messages and cleanup, with saved tyre values unchanged. New durable
wear, full flat/rim/repair physics and initial missing-input claims remain open.
Evidence and scoped patch are under `build/condition-release-smoke/`.

Final validation passes **3,901 Net tests** (26 new), **18 launcher tests** and
**3,877 native checks** (21 new; all 3,856 previous labels/multiplicities retained).
Both Net targets, Core Debug/Release, the probe and launcher build without
warnings/errors. The first native run found two existing checksum fixtures
accessing the session before checking for an approved parked record; that fast
path was corrected and the complete 54-flag run then passed. The initial default
message sweep was also updated to provide valid required confirmation identity.
There are no new error/exception lines against the preceding native baseline.
Debug is native-tested; Release is build-only. Final source, staged and launcher
payload hashes match. All native runs used the disposable game and isolated Wine
profile, with previous output moved to fresh archives. No personal saves,
installed-game files, commits, pushes, release or installer were changed.

### Guest periodic drivetrain write protection (protocol 201, unreleased)

The installed `CORRIS/Simulation/Systems/Drivetrain::Wear` FSM contains three
additional saved-mount writes: Wear #1 to db_Driveshaft.Data.Wear, #3 to
db_Gearbox.Data.Wear and #5 to db_RearAxle.Data.Wear. The existing catalog-driven
guest admission and native-entry guards now suppress all three. Rate calculations,
native transitions and the wait remain active. This uses the existing guard;
there are no new runtime hooks or catalog fields. The protection inventory is
17 writer FSMs / 87 scalar writes, one pose guard and 23 paused graphs.

Protocol/launcher compatibility advances to 201 because guests must agree on
saved-write protection. Message layouts, channels, IDs and mod version 0.1.33
are unchanged; next free message ID is 202. This is local and unreleased.

`build/drivetrain-wear-smoke/drivetrain-audit.json` records 12 FSMs extracted
read-only from installed level2 (build 23268598). The native loop reads signed
Drivetrain.differentialSpeed, applies FloatAbs and a >1 DRIVE threshold, divides
by RateDriveshaft/RateGearbox/RateRearAxle (42300/31500/22800), then subtracts
the resulting Rate from each mount. State 2 uses a two-second real-time Wait.
Engine RPM and road speed are not equivalent inputs. The prior 33-FSM condition
audit is retained alongside this audit.

`GuestEngineChecks.DrivetrainWear.cs` extends the existing disposable native
writer fixture. It supplies DiffSpeed directly, omitting the GetProperty action
and drivetrain component simulation; real native FloatAbs, FloatCompare,
FloatOperator, SubtractFsmFloat, transitions and Wait actions are imported.
Saved targets are inert fixture Data FSMs. Native solo baselines at 0, 1, 3150
and -3150 verify the exact threshold/division effects on all three targets and
the wait configuration. This does not measure the full physical drive cycle or
elapsed two-second cadence under gameplay load.

Guest checks exercise repeated positive/negative cycles, continuing native Rate
math, all three reenabled writes, nine changed destination signatures and repair,
disconnect, unrelated paths and a late canonical graph before scanning. They
prove saved scalar preservation in the fixture, not real fitted-part lifecycle,
save-file persistence or two-player Steam acceptance. Shared host drivetrain wear
during guest driving still needs validated differential-speed telemetry, mounted
target authority and publication. Gearbox failure wear, tyre wear, ordinary parked
repair publication and full flat/rim/repair physics remain separate open work.

The full 54-flag isolated Unity 5/Wine run passes **3,898 native checks**, adding
21 and retaining all 3,877 earlier labels and multiplicities. There are no new
native error/exception lines; the intentional prior cooling fault is unchanged.
All **3,904 Net tests** (three new catalog cases) and **18 launcher tests** pass.
Both Net targets, Core Debug/Release, probe and Launcher build cleanly; Debug is
native-tested, Release is build-only. The first Net run found one stale inventory
assertion expecting 84 writes; it was updated to 87 and the suite passed.

[Result and tested hashes](../build/drivetrain-wear-smoke/result.json) record the
source/staged/launcher payloads, audits and scoped patch. The previous native
output was moved to a fresh archive before launch. The game run used only the
disposable copy and isolated Wine profile; no personal saves or installed-game
files were changed, and no commit, push, release or installer was created.

### Native differential-speed telemetry (protocol 202, unreleased)

VehicleState 60 now appends a strict availability byte and signed finite
float32 differentialSpeed, after movementSpeedTenthsKmh. It is 28 payload bytes /
30 bytes including ID. Unknown must carry numeric zero; known zero is distinct.
Both peers and launcher compatibility use protocol 202. Mod version 0.1.33,
message IDs, channels and earlier field offsets are unchanged; next free ID 202.

`vehicleDifferentialSpeed` names the installed Wear.State 1 #0 GetProperty
source. The runtime requires a unique matching FSM and Drivetrain component on
the same registered body, current catalog identity, exact action/property binding,
canonical local variables and a public float field. The component and producer
must be active, enabled, and the producer started in a known native cycle state.
Capture reads the current Drivetrain.differentialSpeed field directly: it does
not replay GetProperty, change its cache, overwrite DiffSpeed, or touch saved
wear. The native loop samples DiffSpeed only every two seconds, so its scratch
value cannot supply fresh driver telemetry. RPM and the two existing speed
fields remain separate. Invalid, missing, disabled or changed sources report
unavailable, with throttled diagnostics and binding recovery.

Live and reliable final sends use this capture. Accepted state copies and fresh
guest-owned host snapshots retain the reported value; invalid/duplicate packets
cannot replace it and invalid input does not consume a sequence. Stale owner
state cannot provide a join snapshot. Source bindings retire on body replacement
or stream teardown. Receipt does not alter native drivetrain state.

The native fixture imports the real Wear graph and GetProperty action against
an actual Drivetrain component and inert saved Data targets. It warms the native
getter and proves capture reads a newly changed component field while leaving
the old scratch/cache intact. The drivetrain component remains disabled between
synchronous capture calls; its Start/physics loop is not executed. The sender
integration temporarily removes the separately tested RPM catalog profile so
the partial fixture can send through the normal live/final path. Its RPM profile
is restored afterward. This is one Unity process, not live Steam players.

The full 54-flag isolated Unity 5/Wine run passes **3,923 native checks**, with
25 new checks and all 3,898 earlier labels/multiplicities retained. New cases cover
signed values and known zero, stale scratch, nonfinite input, disabled/unknown
producer states, seven changed reader signatures, timed retry, ambiguity/global
alias, body replacement, actual sender packets, accepted copies/snapshots,
invalid/duplicate input, stale ownership, handoff and teardown. No new native
error/exception lines appear; the intentional prior cooling failure is unchanged.
All **3,947 Net tests** (43 new cases) and **18 launcher tests** pass. Both Net
targets, Core Debug/Release, probe and Launcher build cleanly. Debug is native
tested; Release is build-only. An initial probe compile found two missing RPM
arguments in test sample helpers; these were corrected before the native run.

This completes the driver telemetry prerequisite only. Feeding the value into
validated native host drivetrain wear, establishing mounted-target authority and
publishing resulting condition remain open. Protocol finiteness is not a future
wear-amount validation policy. Full fitted-part lifecycle, two-player acceptance,
tyre wear, parked repair publication and physical flat/rim repair are unchanged.

[Result and tested hashes](../build/drivetrain-input-smoke/result.json) preserve
the scoped patch, tested source/staged/launcher payloads, native comparison and
the earlier 12-FSM drivetrain /33-FSM condition audits. A fresh native Drivetrain
class inspection confirms the source field; no installed asset was edited.
Previous native output was moved to a fresh archive before launch. The game run
used the disposable copy and isolated Wine prefix; no personal saves, installed
game deployment, commit, push, release or installer was changed.

### Host periodic drivetrain wear (protocol 203, unreleased)

The host now uses fresh accepted driver differential speed in the native
CORRIS drivetrain Wear graph. Only the FloatCompare input and three division
inputs are temporarily substituted; the native GetProperty, FloatAbs, Rate
scratch, SubtractFsmFloat writes and two-second real-time Wait remain native.
The host's drivetrain component can remain disabled as an observer. Receipt
never sets differentialSpeed or DiffSpeed. Local ownership/seating, non-host
sessions and teardown use native local inputs.

`vehicleDrivetrainWear` catalogs the exact driveshaft, gearbox and rear-axle
saved mounts and native divisors 42300/31500/22800. Every native state entry
validates all three states/transitions, action signatures and flags, threshold,
wait, canonical local operands, literal Data.Wear destinations, ready unique
saved Data FSMs, and native writer caches. Scratch cannot alias saved or global
floats. A bad binding pauses only this Wear FSM before its actions run; throttled
retry restores a repaired graph and resumes a blocked entry. Body replacement
retires old hooks. Replacement canonical Wear storage is validated afresh;
native writers perform their own target lookup. Guest saved writes remain
protected by the v201 guard.

`VehicleDrivetrainWearPolicy` accepts only fresh, available, valid current-owner
live samples. It supplies absolute speed without changing the accepted packet.
Missing/stale/snapshot/wrong-owner input contributes zero, preserving the native
>1 threshold. All host Wear values and divisors must be finite. A mod validation
budget rejects the entire input if any target would lose more than one Wear
point in a cycle: with the audited divisors this rejects abs speed above 22800.
This is a host policy, not a native physics bound; rejected values are not
clamped and cannot partially wear earlier targets. Known zero remains valid.

Protocol 203 marks the new simulation semantics. VehicleState 60 remains 28
payload bytes /30 including ID, with the v202 availability/value offsets
unchanged. No IDs or channels change; next free ID remains 202 and mod version
remains 0.1.33. Both peers and launcher compatibility require protocol 203.

All **3,979 Net tests** (32 new) and **18 launcher tests** pass. The full 54-flag
isolated Unity 5/Wine run passes **3,959 native checks** (36 new), retaining all
3,923 previous labels and multiplicities with no new error/exception lines.
Both Net targets, Core Debug/Release, probe and Launcher build without warnings
or errors. The staged Debug build is native tested; Release is build-only.

The new fixture imports all native Wear actions, an actual Drivetrain component
and three inert Data.Wear mounts at their audited paths. It enters through the
native SwitchState boundary with normal subsequent transitions, and compares
local versus delegated cycles at 0, ±1, ±1.01, ±3150 and ±22800. Tests include
excessive input, stale/unavailable samples, driver handoff, local seating,
disconnection, nonfinite saved wear, changed division/write targets, stale native
writer caches, cadence changes, global aliases, missing/moved mounts, repaired
bindings, replaced saved scalar/body, helper-finalizer failure and teardown.
Native Wait is checked for no immediate repeat, without waiting two real seconds.
The drivetrain component's live Start/physics loop and full fitted-part Data
lifecycle are not executed. This is one Unity process, not a two-player drive.

Initial test runs exposed fixture problems: the shared remote-entry helper adds
a synthetic global transition, and local comparisons initially allowed a paused
baseline. The final fixture preserves the native graph and requires nonzero
native wear before comparing delegated wear. Diagnostic failures are archived
separately from the successful full regression.

Result publication to guests remains open, as do complete fitted-part lifecycle,
gearbox failure/reverse wear, tyre wear, ordinary parked repair publication,
physical flat/rim repair and live Steam acceptance. Roadmap 2.2 stays partial.
[Results and tested hashes](../build/host-drivetrain-wear-smoke/result.json)
preserve the scoped patch, source/staged/launcher payloads and native comparison.
The existing 12-FSM drivetrain and 33-FSM condition audits are retained; native
SubtractFsmFloat and FSM transition code were inspected from read-only game DLLs.
Every run used the disposable game copy and isolated Wine prefix after moving
the previous output into a fresh archive. Installed assets and personal saves
were not modified. No deployment, commit, push, release or installer was made.

### Host drivetrain wear publication (protocol 204, unreleased)

VehicleDrivetrainWearState (202) now delivers the host's exact saved driveshaft,
gearbox and rear-axle Wear values to every guest, including the current driver.
The reliable-ordered message has 21 payload bytes /23 including ID: vehicle ID,
uint32 host revision, one availability byte and three native float32 values.
All values must be finite; unavailable requires three zeros. Known zero and
negative wear remain distinct valid native results. Both peers and launcher
compatibility require protocol 204; mod version remains 0.1.33 and next free
message ID is 203. No existing wire layouts or channels change.

A host poll every 0.5 seconds reads the same audited graph and saved targets as
v203. It validates all state/action identities, canonical operands, target Data
readiness and native writer caches. Unlike simulation binding recovery, capture
never resumes a blocked entry or runs a saved write. A paused/disabled graph,
missing or changed body/target, invalid profile or nonfinite saved value withdraws
all three values. Ordinary parked native wear/repair changes use the same capture.
Host results retain exact floats; the v203 per-cycle input budget does not clamp
saved results. Reads do not depend on the host's observer Drivetrain being enabled.

Changed results broadcast on channel 0, with a five-second keepalive. Revision
history belongs to the host's vehicle, independently of driver identity, condition
reports or lease release. Removed vehicles withdraw their result; rediscovery and
repaired bindings retain revision history. Joins, vehicle resyncs and per-object
snapshot generation include current wear. Observing a snapshot can advance the
current revision but cannot mark its pending live broadcast sent.

Session admission rejects guest-originated wear results and accepts only the
selected host after handshake. Guest storage is bounded by the catalog vehicle
ID and can retain results before scene discovery. Exact duplicates are idempotent;
conflicting duplicates, older revisions and the uint32 half-range ambiguity are
rejected. Revision wrap is supported. Incoming and returned values are copied.
Parking, driver handoff and local driver ownership do not reset host history.
Withdrawal retires previously available values, and only a newer available result
restores them. Disconnection hides the result; session teardown clears both
replica and publication history. Receipt is passive and performs no saved writes
or native failure replay.

All **4,012 Net tests** (33 new) and **18 launcher tests** pass. Both Net targets,
Core Debug/Release, the probe and Launcher build with zero warnings/errors. The
full 54-flag isolated Unity 5/Wine run passes **3,976 native checks**, including
17 new cases and all 3,959 previous labels/multiplicities. There are no new native
error/exception lines; the prior intentional cooling failure remains unchanged.
Debug was native tested; Release was build-only. The launcher's four bundled
payload files match the source and staged Debug files byte-for-byte.

The native fixture first executes a real v203 delegated wear cycle against inert
Data mounts. It captures actual outgoing packets for every guest, then tests
receipt, exact values, keepalive, all three snapshot entry points, parked repair,
driver/observer/parked retention, forged/stale/conflicting input, pre-discovery
arrival, withdrawal/recovery, changed native data/caches, paused snapshot safety,
vehicle removal/body replacement and session reset. Capture never changes native
saved values, and passive guest receipt preserves different local saved wear.
An initial run exposed leaked fixture RPM bindings in six subsequent engine
checks; restoring the saved bindings fixes all six. That failing run is archived
separately from the final successful regression. The generic default-message test
also now supplies the required nonzero vehicle ID.

The 12-FSM native drivetrain audit identifies the next consumer work:
Transmission reads Wear in Driveshaft #2, Gearbox damage #0 and Rear axle #1;
automatic gearbox /3 speed reads it in Set stall speed #3. These paths also
include saved DamageType assignment (Gearbox damage #5), driveshaft BREAKOFF
(Shaft break #0), and saved OilLevel subtraction (automatic State 1/State 3 #3).
The native SetFsmInt implementation writes the referenced FsmInt directly,
without requiring its target FSM enabled, so pausing gearbox Data alone does
not protect that external assignment. These downstream consumers and failure
transitions remain unmodified pending protection and native integration tests.
This change completes delivery and passive retention, not physical failure
reconciliation or full fitted-part lifecycle. Tyre wear, ordinary parked wheel
repair, gearbox failure/reverse wear and live two-player acceptance remain open.

[Results and tested hashes](../build/drivetrain-publication-smoke/result.json)
preserve the scoped patch, native comparison, payload hashes and retained audits.
Every native run used the disposable game copy and isolated Wine prefix after
moving its previous output into a fresh archive. Installed game assets remain
unchanged, and personal saves were not used. No deployment, commit, push, release
or installer was made.

### Guarded guest drivetrain wear consumers (protocol 205, unreleased)

The four audited native GetFsmFloat actions now read the retained host result
from VehicleDrivetrainWearState (202). Transmission reads driveshaft Wear at
Driveshaft #2, gearbox Wear at Gearbox damage #0, and rear-axle Wear at Rear axle
#1. GearboxAutomatic /3 speed reads gearbox Wear at Set stall speed #3. Projection
changes only each canonical local output; it leaves the native target reference,
warm lookup cache and saved guest Data untouched. Exact zero, negative and
threshold-adjacent host values are retained, with no rounding or clamping.

Before any protected entry, the same binding disables Transmission's saved
DamageType assignment (Gearbox damage #5) and driveshaft BREAKOFF dispatch
(Shaft break #0), plus automatic gearbox OilLevel subtraction in State 1/State 3
#3. Selected actions are also removed from active lists. A SendEventByName prefix
blocks direct and reenabled dispatch, retaining old action identities after graph
replacement so a stale callback cannot escape. Destroyed owners retire those
identities. The native event signature requires its canonical GameObject target,
literal Data/BREAKOFF, literal false flags, zero delay, no pending delayed event
and once-only execution.

The paused gearbox Data now requires both external integer and float destination
guards. Native SetFsmInt does not require its destination FSM enabled, so pausing
Data alone is insufficient. The new prefix follows native goLastFrame/fsm cache
semantics and remembers the saved target through renaming and disconnect. It
blocks direct saved DamageType changes while unrelated integer destinations
still execute normally. The three existing external float guards also protect
the gearbox's OilLevel and Wear.

Reader validation requires exact action identity, canonical named references,
literal Data.Wear and a canonical local output without global or referenced-save
aliases. In-place changes are checked before cached recovery and before lookup.
Missing or withdrawn host data pauses both registered consumers; a newer valid
host result recovers after all saved actions are guarded. Missing wear metadata
also pauses supported registered consumers. Vehicle matching uses the same
stable vehicle ID as result receipt. Driver handoff, local driving ownership and
parking do not replace host wear. Host/solo/disconnected reads retain native
lookup, while saved guest write/event protection survives disconnect.

This changes simulation semantics, so both peers and the launcher compatibility
manifest require protocol 205. Message 202 remains 21 payload bytes /23 including
ID; no fields, message IDs or channels change. Mod version stays 0.1.33 and next
free message ID remains 203.

Validation passes **4,040 Net tests** (28 new), **18 launcher tests**, and
**4,003 native checks** (27 new). All 3,976 preceding native labels and their
multiplicities are retained. Both Net targets, Core Debug/Release, the probe and
Launcher build with zero warnings/errors. The complete isolated Unity 5/Wine
run uses all 54 existing probe flags and has no new native error/exception
lines; the preceding intentional cooling failure remains unchanged. Debug is
native tested; Release is build-only. The four launcher payload files match the
source and staged Debug payload byte-for-byte.

The new fixture imports audited native Transmission/3 speed readers, comparisons,
SetFsmInt, SendEventByName and oil writers against inert saved targets. A solo
positive control proves native integer/oil writes and BREAKOFF dispatch execute.
Guest checks cover all four exact reads around 1/7/15 thresholds, known zero and
negative wear, driver/parking retention, missing/withdrawn state, repair, changed
reader/event fields, saved/global aliases, direct and reenabled calls, warm cached
renamed targets, unrelated integer writes, stale callbacks after replacement,
missing catalog metadata and disconnect. Saved wear, damage, oil and detach
counts remain unchanged under guest protection.

An initial fixture load failed because the legacy catalog parser does not accept
scientific notation; the probe JSON now expands those numbers without changing
values. The next two runs exposed an interaction with the older starter fixture:
its automatic selector used placeholder wear actions and a synthetic vehicle ID.
The fixture now retains the native guarded signatures, and wear consumption uses
the same stable-ID eligibility as result receipt. Those failed runs are preserved
separately; the final full regression passes every previous starter check.

The fixture intentionally bounds transitions and physical actions: it tests
native reads/comparisons and guarded writes/events, not the complete transmission
physics loop. Full physical failure effects, fitted-part installation/removal
and reconciliation, gearbox failure/reverse wear authority, tyre wear and
ordinary parked wheel repair remain roadmap 2.2 work. Live two-player Steam
acceptance and save/reload behavior still need gameplay validation.

[Results and tested hashes](../build/drivetrain-consumer-smoke/result.json)
preserve the scoped patch, native comparison, payload hashes and retained native
12-FSM drivetrain/33-FSM condition audits. All native runs use the disposable
game copy and isolated Wine prefix after moving previous output to a fresh
archive. Installed game assets remain unchanged; personal saves were not used.
No deployment, commit, push, release or installer was made.

### Host gearbox oil input (protocol 206, unreleased)

Automatic gearbox /3 speed still read the guest's saved Data.OilLevel at Set
stall speed #0, even after v205 supplied host wear to #3. Native oil is clamped
to 0.02–6.3, used in 15120 /Oil for UpShiftRPM, then UpShiftRPM /800 for StallSpeed.
The state also calculates OilLeakRate from host wear and writes MaxStall/Stall
to the local Stallspeed FSM. Different saved oil could therefore produce different
shift and stall behavior despite identical received wear.

VehicleDrivetrainWearState (202) now appends gearboxOilAvailable:uint8 (strict
0/1) and gearboxOilLevel:float32 after rearAxleWear. All prior offsets remain
unchanged. Payload size is 26 bytes /28 including ID. Finite oil values, including
known zero and negative native results, survive exactly without quantization or
clamping. Unavailable oil requires zero; available oil requires available wear.
Both peers and launcher compatibility require protocol 206. Mod version remains
0.1.33 and next free ID stays 203; channels and other message layouts are unchanged.

Host capture reads the same canonical gearbox Data already validated for saved
wear. Missing or duplicate OilLevel, a non-variable/nonfinite value, or aliases
to a global, producer scratch or saved wear withdraw only the oil field. Invalid
wear bindings still withdraw the whole result. Capture never executes native
saved writes or recovers a paused wear entry. Oil-only changes, including parked
refills, advance the existing host revision, live poll, reliable keepalive, join
and targeted-resync paths. Snapshot observation cannot acknowledge a pending
broadcast. Replica copying and duplicate/conflict comparison include both new
fields, and the existing selected-host admission policy still applies.

The automatic writer profile now requires a `gearboxOil` reader at Set stall
speed #0, with canonical db_Gearbox, literal Data.OilLevel, canonical local Oil
output and once-only native GetFsmFloat. The existing global/saved alias checks,
cached revalidation and both automatic saved-oil guards apply. Projection changes
only the reader output; native source/caches, saved OilLevel and saved wear remain
intact. Missing or withdrawn oil pauses automatic until valid host input returns;
Transmission's wear consumers continue if wear is available. Driver handoff,
local driving ownership and parking do not replace host oil. Host/solo and
unregistered fixture lookups retain native behavior. Protected saved writes
remain blocked through disconnect.

All **4,072 Net tests** pass (32 added cases, including framing/truncation and
catalog changes), along with **18 launcher tests**. Both Net targets, Core
Debug/Release, the probe and Launcher build with zero warnings/errors. The full
54-flag isolated Unity 5/Wine run passes **4,038 native checks**: 35 new and all
4,003 preceding labels/multiplicities retained. There are no new native
error/exception lines. Debug is native tested; Release is build-only. Six staged
probe inputs retain their launch-time hashes; the four launcher payload files
match source and native-tested Debug payload byte-for-byte.

Native publication tests exercise exact oil below/at/above clamp limits, zero,
negative and large values; missing/duplicate/nonfinite/non-variable/aliased
sources; independent oil withdrawal and repair; all three snapshot entry points;
pending refill broadcasts; driver/parking continuity and copied guest storage.
The consumer fixture imports all ten native Set stall speed actions, including
FloatClamp/FloatOperator/FloatDivide and both local Stallspeed writes. It checks
exact host reads, native shift/stall/leak calculations, both blocked saved drains,
oil-only withdrawal, recovery, ownership changes, changed reader fields and
saved/global output aliases. These run against inert saved Data and local
Stallspeed targets, preserving original native source/cache identities.

The first full native run retained all previous checks but failed two reference
arithmetic comparisons near oil limits. The fixture's chained local arithmetic
could retain more precision than native intermediate FsmFloat storage on legacy
Mono. Storing each reference result as float32 makes the expected calculation
match native step boundaries; exact comparisons then pass without relaxing the
checks or changing mod/native arithmetic. That initial run and tested hashes are
archived separately from the final successful run. The audited Stallspeed row
was added to the disposable probe JSON with scientific notation expanded for the
legacy fixture parser; values were preserved.

This verifies host oil delivery and the complete native shift/stall calculation
state, not the automatic gearbox's full physics/gear-change loop. Host oil-loss
progression while a guest drives, complete physical failures, fitted-part
installation/removal/reconciliation, tyre wear and ordinary parked wheel repair
remain roadmap 2.2 work. Live two-player Steam and save/reload gameplay acceptance
are still unverified.

[Results and tested hashes](../build/gearbox-oil-smoke/result.json) preserve the
scoped source patch, comparison, payload hashes, retained 12-FSM drivetrain and
33-FSM condition audits, and inspected native FloatOperator. Both native runs
used the disposable game copy and isolated Wine prefix after moving prior output
to a fresh archive. Installed game assets are unchanged; personal saves were not
used. No deployment, commit, push, release or installer was made.


### Guest automatic gearbox oil use (protocol 207, unreleased)

A connected guest who owns and actually drives CORRIS reports each native
`GearboxAutomatic::3 speed` State 1/State 3 OilLevel callback. Message 203 has
vehicle ID, authenticated player ID, uint16 sequence and phase (1/3): 8 payload
bytes, 10 total, reliable channel 0. There is no guest-supplied amount, duration
or count. Message 202 remains 28 bytes; the manifest requires protocol 207,
mod version stays 0.1.33 and next free message ID is 204.

Both selected catalog actions require `gearboxOilUse: true`. Native action
identity, target, canonical OilLeakRate and once-per-entry cadence are checked;
only the actual guest driver retains enabled observation callbacks. Native saved
subtraction remains suppressed. Repeated callbacks in one state entry cannot
emit another request. Disconnect/non-driver ownership disables observation, and
changed native signatures pause/recover only the affected consumer.

The host verifies the authenticated current vehicle owner, rejects host-local
driving, and applies uint16 serial ordering (delta 1–32767 including wrap).
Each vehicle has an abuse budget of four initial events and four refilled per
second, shared through handoffs. This is a mod limit, not an audited maximum
native shift frequency. Rejected ordered events cannot replay after a repair or
budget refill. Player departure clears its ordering history while retaining the
vehicle budget; complete session teardown clears both.

The host captures validated current saved drivetrain wear/oil, resolves the
unique initialized automatic consumer and mounted Data, and requires Installed
true and Type 2. The three native calculation actions retain subtraction,
canonical local references, constants and once-only cadence. Temporary FsmFloat
operands feed current host wear through native FloatOperator, FloatDivide and
FloatClamp: (15 - Wear) / 5000, clamped to 0.0000001–1. The native saved
SubtractFsmFloat helper applies that result once. Original field references,
local scratch and active driving state are restored/preserved. Competing host
callbacks are suppressed during guest ownership; solo/host driving stays native.
The next drivetrain poll broadcasts saved oil through message 202. No deferred
request queue can drain oil after repair.

The isolated probe reuses the audited ten-action Set stall speed state and
real State 1/3 subtraction actions against inert saved Data. It covers reliable
request emission, repeated callbacks, non-driver/disconnect rejection, cadence
failure/repair, native host arithmetic at wear values 100/15/14/0/-6000, duplicate
rejection, manual/removed/missing/nonfinite targets, changed division and stale
foreign caches. It checks host observer suppression, unchanged scratch/action
references, ownership/sender validation, broadcasts, player-slot cleanup and
ordinary local native use.

The first native runs exposed an uninitialized test consumer: the fixture had
started action execution without completing Fsm.Init. Initializing it before
loading its selected native actions fixes the fixture; the host readiness guard
remains intact. Three failed diagnostic runs and the subsequent 4,058-check
successful run are archived before the final run, which adds reconnect coverage.

All **4,100 Net tests** pass (28 added), along with **18 launcher tests**.
Both Net targets, Core Debug/Release, the probe and Launcher Debug build with
zero warnings/errors. The final 54-flag isolated Unity 5/Wine run passes
**4,059 native checks**, adding 21 and retaining all 4,038 previous checks,
with no new native error signatures. Native execution uses Debug; Release Core
is build-only. The launcher payload matches the native-tested Debug inputs.

This validates the native oil-use callback/calculation path, not the full
physical automatic gearbox loop. Full driving/failure effects, fitted-part
lifecycle, tyre wear and ordinary parked wheel repair remain open. Live
two-player Steam and save/reload gameplay acceptance are still unverified.
[Results and tested hashes](../build/gearbox-oil-use-smoke/result.json) retain
the scoped patch, native comparison, source/payload hashes and native audits.
Every native run used the disposable game copy and isolated Wine prefix after
moving prior output to a fresh archive. Installed game assets are unchanged;
personal saves were not used. No deployment, commit, push, release or installer
was made.

### Guest gearbox failure wear (protocol 208, unreleased)

The installed GearboxDamage::Damage audit routes damage types 1/2/3 to the
native Reverse failure state. Reverse #6 subtracts literal 0.0525 from the
mounted gearbox's Data.Wear once per entry; everyFrame/perSecond are false.
The state also changes gear, plays a sound and waits 0.6 seconds. Only the
saved subtraction is delegated to the host in this change.

The actual guest driver owning CORRIS keeps this protected callback enabled as
an observer. Message 204 carries vehicle ID, player ID and uint16 sequence:
7 payload bytes, 9 total, reliable channel 0. No amount, duration, saved damage
or batch count comes from the guest. Entry epochs suppress duplicate direct
callbacks; missing ownership, non-driver status and disconnect cannot report.
Native signatures and the protected condition reader are revalidated; changed
amounts/cadence pause and recover that consumer without guest saved writes.

The host verifies authenticated current ownership and excludes host-local
driving. A shared callback policy provides per-sender serial ordering and a
vehicle-wide abuse budget, here two initial events and two per second. Driver
handoffs retain the budget/history. Player departure clears only that sender's
history, and session teardown clears both. Ordered rejected events cannot
replay after a repair or budget refill; requests have no delayed queue. Oil
use retains its independent four-event/four-per-second budget.

Read-only host drivetrain capture establishes current saved wear. The native
failure consumer must be initialized, reference that exact saved gearbox
object, and resolve its actual target/cache. Saved Installed must be true,
DamageType must be 1–3, and Wear must be finite. Duplicate saved fields,
global aliases, changed native amounts/cadence and foreign caches are rejected.
The native subtraction helper applies 0.0525 once without entering a host
failure state, changing host gear/scratch, playing sound or choosing random
clips. Host observer callbacks are suppressed during guest ownership; solo
and local host native writes remain active. The next message 202 publication
shares the resulting wear with all guests.

The exact-object requirement also covers oil-use callbacks. Both paths compare
the consumer's destination with the validated host drivetrain producer's
current db_Gearbox object. Identical scene paths and scalar values do not make
a duplicate gearbox authoritative. The native probe creates an inert duplicate
with matching path/values and verifies that both the original and duplicate
remain unchanged on rejection, then restores the target and accepts a fresh
callback without reviving the rejected sequence.

The callback mechanics were factored into GuestEngineProtection.GearboxCallbacks
and VehicleCallbackPolicy. Catalog `gearboxWear: true` is required only for the
existing Reverse #6 guard; counts stay 19 writers, 90 scalar actions, one named
event, one pose, 23 paused FSMs and five drivetrain float readers. Protocol and
manifest are 208, mod version remains 0.1.33, next free message ID is 205.
Existing message layouts are unchanged, including state 202 (28 bytes).

All **4,134 Net tests** pass (34 added), together with **18 launcher tests**.
Both Net targets, Core Debug/Release, GuestSaveProbe and Launcher Debug build
with zero warnings/errors. The final 54-flag Unity 5/Wine run passes **4,083
native checks**, adding 24 and retaining all 4,059 prior checks. There are no
new native error signatures. An earlier successful 4,081-check run is archived;
the final run adds exact-object rejection cases for failure wear and oil use.
Native execution uses Debug; Release Core is build-only. Seven staged native
inputs and the four relevant launcher payload files have matching hashes.

The fixture runs actual native failure callbacks and saved subtraction against
inert Data, along with current host result publication. It covers duplicate
callbacks, driver/disconnect gating, cadence/amount mutations, all three saved
damage types, healthy/uninstalled/unknown/duplicate/nonfinite targets, foreign
caches, ownership/authentication, reconnect cleanup, host double-charge guards
and local native use. This does not validate the full physical gear kick-out,
sound/vehicle-physics loop, fitted-part installation/removal/reconciliation,
tyre wear or parked wheel repair. Live two-player Steam and save/reload
acceptance remain open; roadmap 2.2 stays partial.

[Results and tested hashes](../build/gearbox-failure-wear-smoke/result.json)
retain the scoped source patch, native comparison, source/payload hashes and
installed native audits. Both runs used the disposable game copy and isolated
Wine prefix after moving prior output to fresh archives. Installed game assets
are unchanged; personal saves were not used. No deployment, commit, push,
release or installer was made.

### Wheel rim presentation (protocol 208, unreleased)

Healthy or punctured observers now apply an accepted rim flag by entering the
cataloged `Condition::Rim friction` state through the existing synthetic FSM
entry helper. The native state contains only two `SetProperty` actions: the
wheel's `radius <- RimRadius` once, and `rollingFrictionCoefficient <- 0.1`
every frame. The native healthy-state sound-off action stops a previous flat
sound. The replay does not enter `Check rim`, consult the observer's saved
`TireType`, or enter `Flat friction` and zero saved `TireHealth`.

The whole selected state, action types/cadence/inputs, local component identity,
scalar aliases, sound containment and synthetic event destination are checked
before physical application. Added actions, redirected or shadowed events,
foreign components, invalid values and disabled actions leave the accepted
condition pending with a five-second retry. Availability withdrawal cancels the
pending rim application. Ownership and body/session changes use the existing
condition gates. A fresh accepted keepalive repairs radius/friction drift even
when the FSM already reports the rim state.

Evidence: `build/wheel-rim-smoke/result.json`, `change.patch`, staged hashes,
full logs and the predecessor comparison. The local run passes **4,150 Net
checks** (16 added catalog cases), **18 launcher checks**, and **4,113 native
checks** (30 added; all 4,083 prior labels retained), with no new native error
signatures. Both Net targets, Core Debug/Release, the native probe and Launcher
Debug build cleanly. The first native run exposed four host-fixture cases using
the host peer as a guest sender; its complete output is preserved. The corrected
host cases use an authenticated guest and explicitly enable the native saved
writer with guest protection off: rim replay preserves saved health, while a
separate native puncture entry demonstrably writes zero.

The fixture uses the installed build 23268598 action definitions, real native
`Wheel` components with their autonomous solver disabled, and an inert sound
object driven by the real activation action. It verifies FSM/property callbacks,
not driving forces, terrain interaction or audible playback. Native execution
uses Debug; Release is build-only. All runs use disposable game/save targets;
installed asset hashes remain unchanged. Mod version stays 0.1.33, protocol
stays 208, next free message ID stays 205; no layout or message meaning changed.
Changes are local and unreleased.

Roadmap 2.2 remains partial: rim-to-healthy repair must restore the installed
tyre's physical settings; rim-to-flat transitions, fitted tyre type/grip
reconciliation, host-owned durable tyre wear,
ordinary parked repair/refill publication, full gearbox failures and fitted-part
lifecycle still need work. Live two-player driving and save/reload acceptance
are also pending.

### Host wheel-health inputs (protocol 209, unreleased)

`VehicleWheelHealthState` (205) carries the host's exact native FL/FR/RL/RR
health inputs, a four-bit availability mask and an independent uint32 revision.
The 25-byte payload is 27 bytes including its ID. The host polls every half
second and sends changed values or a five-second reliable keepalive to every
guest. Parked native health changes, join snapshots and targeted vehicle resync
all use the same publisher; snapshot reads cannot consume pending broadcasts.
A missing/invalid wheel withdraws independently, and removed/replaced cars
retain revision history instead of restarting at an old driver's sequence.

Catalog `vehicleWheelHealth` ties each wheel to its native `ThisTire` mount.
Capture validates the unique initialized consumer/mount, both native health
reader signatures and warmed caches, and the finite, non-aliased `TireHealth`
scalar. It reads the same mount value as vanilla without entering states,
preparing paused consumers, running wear or invoking fitted-part actions.
Disabled/unready consumers, missing metadata, changed references/caches,
duplicate scalars and nonfinite values withdraw the affected input.

Protected guest `Condition` healthy/flat readers use the exact host floats and
feed the existing native grip arithmetic. Driving ownership, approved former
driver conditions and quantized condition bytes cannot replace those inputs.
Missing or withdrawn host health pauses only the affected registered wheel;
metadata repair and rediscovery resume it. A once-registered wheel cannot fall
back to personal health during temporary unregistration or body replacement.
Known-vehicle packets received during a local metadata outage retain their
newer revision for recovery. Saved guest health, native source references and
reader caches stay intact. Host/solo readers remain native. The legacy
`VehicleCondition` layout is unchanged and still carries pressure, flat/rim
flags and gearbox damage category; complete physical failure authority is
separate work.

Validation: **4,212 Net tests** pass (62 added wire, revision, availability,
catalog and authentication cases), together with **18 launcher tests**. Both
Net targets, Core Debug/Release, the native probe and Launcher Debug build with
zero warnings/errors. The final 54-flag isolated Unity 5/Wine run passes
**4,143 native checks** (30 added; all 4,113 prior labels retained), with no new
native error signatures. The first green 4,142-check run is archived; the final
run adds and verifies temporary-unregistration/body-replacement protection.
Native execution uses Debug; Release is build-only. Eight staged native inputs
and the four relevant launcher payload files have matching hashes.

The fixture executes actual native health readers and grip arithmetic on inert
mount Data and exercises real session dispatch, publication, snapshot and
consumer recovery paths. Parked repair coverage changes the host mount's value;
it does not execute the Fleetari service workflow. Full wheel forces, terrain,
fitted tyre type/grip and physical rim-to-healthy/rim-to-flat repair are not
validated. Guest-driving durable tyre wear still requires validated driver slip
inputs and host wear application; parked pressure refills, complete gearbox
failures and fitted-part lifecycle also remain open. Live two-player Steam and
save/reload acceptance remain pending, so roadmap 2.2 stays partial.

[Results and tested hashes](../build/wheel-health-state-smoke/result.json)
record the scoped patch, source hashes, full logs and predecessor comparison.
All native runs use the disposable game copy and isolated Wine prefix, after
moving prior saved targets into fresh archives. Installed asset hashes remain
unchanged and personal saves are unused. Protocol is now 209, next free message
ID is 206, mod version remains 0.1.33. No deployment, release, installer,
commit or push was made.


## Valve-adjustment controls (2026-09-13, unreleased v215)

The eight cylinder-head `ValveAdjustment/Masked/BoltPM` Screw graphs previously
failed ordinary-bolt validation. Their native behavior is a float step of 0.05,
clamped to [2, 8], written to the head's `Valves` array, with visual rotation equal
to setting × 50. The dedicated catalog-backed adapter validates their native
turn, array, initialization, collider and visual actions before binding.

Host turns retain native actions and publish absolute message 206. Guests send
nearby, authenticated raw intents and update only their control display; their
saved head array is never used for prediction or overwritten by a host receipt.
Original guest states, scratch values and exact visual poses restore on cleanup.
Snapshots, object resync, checksums and delayed registration include the controls.
The protocol is **215**; the mod remains unreleased and version numbers are unchanged.

Validation:

- Net/Core/probe Release builds: zero warnings/errors; both Net targets built.
- **4,353 protocol tests** pass, including float framing, message admission,
  catalog containment, bounds and native array validation.
- **116 native checks** pass: 40 new control cases, 55 existing valve engine-input
  cases and 21 save-guard checks. Actual extracted game actions execute fractional
  turns and endpoint clamps; distant/stale/unknown/unavailable requests are refused;
  guest controls emit one rate-limited intent, do not predict into saved tuning,
  restore on disconnect and rebind without duplicate hooks.
- **22 local two-game checks** pass. All eight loose-head controls accept a host
  turn and a guest return turn. Initial state, unchanged guest saved arrays,
  idempotent resync and successful native host fitting are checked. Host updates
  continue reaching the guest after fitting.
- All **18 protected personal-save/installed-payload files** match their prior hashes.

**Open gameplay blocker:** physical cylinder-head reconstruction is not implemented.
After native host fitting, the guest's local head remains at its previous position;
a guest turn from that location failed the host distance gate in this run. Full
fitted-head guest interaction is **not passed**. Heads missing from a different
guest save, arbitrary replacement heads, physical screwdriver raycast/input and
Steam/two-PC play are also untested. This adapter does not complete engine assembly.

Evidence is in ignored `build/valve-turn-audit/`: `summary.md`, `result.json`,
`native-accepted/`, `live-controls/assertions.json`, `live-controls/limitations.json`,
protected-file verification, payload hashes and a scoped patch against the initial
working tree. Earlier failed fixture runs remain as diagnostic history. Native
fixtures import `native-valve-head.json` from installed assets; the audited fixture
copy omits unrelated globals/transforms and formats JSON numbers as decimals for
this legacy reader. Use `--wintermp-guest-save-probe --wintermp-valve-turn-probe
--wintermp-valve-adjustment-probe` only in a disposable game copy. The live driver
additionally requires the existing sandbox marker and `WINTERMP_LOCAL2P_VALVE_TEST=1`.
Temporary launch/check scripts are archived under the audit's `drivers/`; no probe
or game-root marker is left deployed.

### Cylinder-head placement (2026-09-13, unreleased v216)

The v215 valve test exposed a physical placement gap: fitting VIN1110 on the host
left the guest's head at its old loose position. Guests can now see the existing
head follow the host into VIN1010/VINP_Cylinderhead and return to loose item motion
after native host removal. The head keeps its child hierarchy; the local run
compared all 21 mount positions. Guest Data and saved fastening controls are
paused, while valve controls keep their separate protected display and host
intents. Guest native AssemblyID, condition and saved valve settings are not
rewritten. Cleanup restores the original hierarchy, pose, collision flags and
body properties, removing a temporary body if the guest originally loaded a
fitted head.

Protocol 216 adds host-only message 207, `CylinderHeadState`, on reliable-ordered
channel 0. The attachment is included in join/item resync and targeted replies.
Its revision orders fitting/removal separately from normal loose item movement;
old or contradictory states cannot undo a newer attachment. Native host fitting
must settle before publication. The mount identity and reset local pose come
from the installed-game definitions and strict `cylinderHead` catalog profile.

Validation:

- Net builds for both `net35` and `netstandard2.0`, and Core/probe builds against
  the installed Unity/PlayMaker assemblies: zero warnings/errors; deployment to
  the personal game disabled.
- **4,365 Net tests** pass, including packet shape, malformed physics/identity,
  revision wrap/staleness, host admission and catalog containment.
- **140 isolated native checks** pass: 24 head-presentation checks covering
  originally loose/fitted heads, child mounts, safe waiting, removal and exact
  restoration, plus the existing 40 valve-control, 55 valve-input and 21 save
  guard checks.
- **25 final local two-game assertions** pass: native host fit, matching head and
  21 child mount positions, no fitted pickup motion, all eight nearby guest
  valve turns and host results, unchanged saved guest tuning/AssemblyID, repeated
  attachment resync, host removal and subsequent loose-head valve interaction.
  The final Core/Net DLL hashes match the accepted live-run payloads.
- All 18 previously recorded personal save/installed payload files are unchanged.
  Owned test game processes stopped, probe DLLs/markers/fixture input were removed,
  and temporary drivers were archived under the ignored audit directory.

Evidence: `build/head-fitting-audit/summary.md`, `net-tests.log`, `build.log`,
`net-build.log`, `native-accepted/guest-save-probe/result.txt`,
`live-accepted/assertions.json`, role logs/command replies, payload/source hashes,
`protected-files.json`, `process-cleanup.json` and `scoped.patch`. The earlier
`live-attachment` exploration includes a probe-only null-parent formatting error;
`live-accepted` uses the corrected probe and is the acceptance run. These are
ignored local artifacts, not a public release.

Limits: this moves the matching persistent scene head; it does not create a
missing/different head or reconcile every unsupported child part. Guest head
installation/removal intents and fastening-bolt controls remain unfinished. The
native bolt geometry is retained but guest fastener inputs are paused to protect
the saved bolt array. The local driver invokes native fitting/tool/removal events
and moves test players; it is not physical mouse/spanner or Steam/two-PC
acceptance. Runtime reconstruction of an originally fitted guest head was covered
by the native fixture, not a separate full-game save. Rejoin while the host head
is already fitted, engine-block physical fitting, full assembly journeys and the
four-player soak remain open. The mod stays at 0.1.33 and is unreleased.

After this bounded placement fix, the next task rotates to shared firewood
completion/payment acceptance; the remaining assembly controls stay recorded in
the backlog.


## Firewood payment — 2026-09-13 (unreleased v217)

The original two-game run turned one prepared 500 mk firewood payment into
5,000 mk of both cash and net income: ten guest `FsmStateEnter` requests could
re-enter the native credit state while its hand animation was still running.
The fix tags only the firewood payment catalog rule with `hostPayment: firewood`.
It validates the installed native cash/income actions, reserves a live offer
before host entry, refuses repeat/inactive/invalid offers without queuing, gates
both native credit actions on guests, and removes payment action replay from
join snapshots. The host's existing WalletState sends the shared result. Credit
action flags are restored on cleanup. Protocol 217 records the semantic change;
there are no new fields or message IDs. The mod version remains 0.1.33, unreleased.

Validation against the installed Unity/PlayMaker DLLs:

- Core and developer probe Release builds pass without warnings/errors, with
  personal-game deployment disabled. Net builds for net35 and netstandard2.0.
- **4,381 Net tests** pass without warnings, including the payment eligibility
  rules and catalog selection/rejection of unsupported payment metadata.
- **61 isolated native checks** pass: 40 firewood checks across all four actual
  serialized customer payment graphs, plus 21 existing guest-save guards.
  The fixtures reproduce duplicate native additions, test reservation before
  execution, repeated entry, fresh offers, guest credit suppression, invalid or
  disabled offers, exact action-flag restoration and changed credit destinations.
- **9 final local two-game assertions** pass: both guards bind; ten simultaneous
  requests pay one 500 mk offer once; both balances converge; expired offers and
  stale guest collections cannot repay it; a guest native collection entry makes
  no optimistic cash/income change; fresh offers and host collection work; join
  snapshots contain no payment replay.

Evidence is in `build/firewood-job-audit/`: `summary.md`, `validation.json`,
`native-final/guest-save-probe/result.txt`, `live-accepted/assertions.json`, native
and role logs, command replies, payload/source hashes, protected-file comparison,
process/deployment cleanup records and `scoped.patch`. `live-baseline-offer`
contains the original tenfold payout reproduction. Earlier setup/precheck runs
are diagnostic only: the fixture needed a positive payment before activation,
and physical mouse-off immediately cancels a forced Wait button state. The final
driver enters State 1 after the mouse-selection boundary; it does not simulate
physical input. One initial native-fixture run also needed its empty wait-state
transitions removed; `native-final` is the final accepted run.

Limits: prepared-offer collection only. The driver temporarily enables the buyer,
seeds a 500 mk offer in disposable profiles, moves players near it and invokes
native payment entry. It does not complete a real wood delivery, prove job
acceptance/completion, synchronize the buyer's visibility or pending offer, or
validate Steam/two-PC play. JobSiteSync's nonnegative clamp also loses the native
negative Penalty value; signed job state needs separate correction. Those gaps
stay on the backlog. No personal game/profile was used for mutations.

Next bounded work rotates to shared milk condition/spoilage, as recorded in the
coverage checkpoint; do not silently count the full firewood work loop as passed.


## Milk condition — 2026-09-13 (unreleased v218)

A baseline two-game run found different condition values for milk NetId 2154130149
(host saved identity `milk1`). Entering the host's native spoilage state 100 times
reduced its condition without changing the guest's copy. The installed milk Use
FSM subtracts 0.006 per warm tick or 0.001 within a native fridge chill area, then
enters terminal Bad at condition <=1 and renames the carton `spoiled milk(itemx)`.
The consumed sentinel is 888 and is unrelated to freshness.

The milk-only catalog binding now keeps that host simulation and publishes
MilkConditionState (208): ID, revision, condition and spoiled flag. Guests suppress
the two decay branches, wait for initialization and a host seed before drinking,
and use the native Bad presentation. Fresh milk retains its native drink checks.
Snapshots and targeted resync include condition; five-second keepalives repair
unchanged drift. Saved spoiled milk maps to the fresh creation template followed
by its condition state. Cleanup restores native guest actions, original condition
and name while leaving the native save key alone; consumed bodies are not revived.
Protocol is 218; version 0.1.33 and shipped compatibility metadata are unchanged.

Validation:

- Core and developer probe compile against the installed game DLLs with zero
  warnings/errors and deployment to the personal game disabled. Net builds for
  both net35 and netstandard2.0. **4,393 Net tests pass**, covering packet shape,
  invalid conditions/sentinel/flags, host admission, ordering and catalog scope.
- **40 isolated native checks pass:** 19 milk checks plus 21 existing save guards.
  The fixture uses the installed serialized milk actions and actual PlayMaker
  execution. It verifies host warm/cold subtraction, revision stability, consumed
  sentinel exclusion, loading deferral, guest decay suppression, fresh/spoiled
  drink gating, drift/stale-state handling and reversible cleanup. Changed decay
  destinations are refused before replacing actions. The fixture excludes the
  absent scene's fridge lookup; the live test below exercises that lookup.
- **14 final local two-game assertions pass:** saved condition convergence;
  100 warm ticks subtract about 0.6 and cold ticks about 0.1; native fridge distance
  is below 0.45 m; guest ticks cannot decay independently; both rates converge;
  targeted requests and unchanged keepalives repair drift; both cartons enter
  native Bad with the spoiled name; local save identities stay distinct; spoiled
  guest drinking is blocked; disconnect restores the original fresh condition;
  reconnect restores the host's spoiled state.

Evidence: `build/food-condition-audit/summary.md`, `validation.json`,
`live-before/` (v217 reproduction), `live-final/assertions.json`,
`native-accepted/guest-save-probe/result.txt`, role logs and command replies,
protected-file comparisons, payload/source hashes, cleanup records and scoped
patch. `live-candidate/` and `native-final/` are earlier diagnostic/passing runs.
The accepted native run uses the final build; the live payload differs only by
subsequent indentation cleanup in WorldSyncManager.Snapshots.cs.

Limits: local UDP with two isolated game copies/profiles. The driver moves the
carton to a real chill area and enters native states; it does not simulate mouse
picking/drinking animation or change fridge electricity. Reconnect uses the same
running guest process, so a separate fresh-process join to an already spoiled
saved carton remains a follow-up acceptance case. Other foods, cooking, complete
fridge power behavior, Steam/two-PC and the full ordinary journey on one build
remain open. No personal game or profile was used for mutations.

The next bounded task rotates to guest cylinder-head fitting/removal, as recorded
in the coverage checkpoint. Do not mark all food or the mod complete from this test.


## Guest cylinder-head fitting — 2026-09-13 (unreleased v219)

Guests can now fit/remove the persistent VIN1110 head through the existing
part-operation request/receipt client (188/189). Message 207 supplies the observed
revision and resulting placement. The host requires the exact native head/block
mount, ready FSMs, a living guest's pose no older than two seconds within three
metres of both targets, and an idle operation. Fitting requires pickup ownership
(or the existing half-second release grace) and native assembly tolerance;
removal requires finite Tightness in [0,1). The native mount's Allow install? or
Allow removal? chain runs before same-frame confirmation. Failed previews are
cancelled immediately; committed native operations settle without replay.

Guest fit/remove prompts reuse the existing part UI and acknowledgments. Head
Data and fastening bolts remain suppressed; v216's reversible view places the
head. This does not add guest head fastening or missing-head reconstruction.
The starting native blocker reference is empty; the acceptance fixture binds a
real Data.Installed variable to exercise the native blocking branch explicitly.
No guessed substitute part is used for that prerequisite.

Validation:

- Core and probe build cleanly against the installed game DLLs with personal-game
  deployment disabled. Net builds cleanly for net35 and netstandard2.0.
- **4,408 Net tests pass**, including 15 new head interaction cases for identity,
  revision, ownership, distance, tightness, slot/operation scope and shared-ledger
  duplicate handling. The first full run measured 1,016 allocated bytes in the
  untouched EngineBlockTests.RepeatedReadOnlySnapshotsDoNotAllocate test. Its
  isolated rerun and the subsequent full suite pass without changing that code.
- **34 isolated native checks pass:** 13 head checks plus 21 save guards. Native
  serialized actions cover request binding, occupancy, blocking prerequisites,
  Far/Near, confirmation, removal and tightness boundaries. These fixtures stop
  before destructive lifecycle writes; the real host lifecycle runs below.
- **12 local two-game assertions pass:** native guest pickup/ownership and mount
  alignment; host fitting; guest placement with saved AssemblyID/Tightness still
  zero; bolted, blocked and distant removal rejection; successful shared removal;
  fitting without ownership rejection; blocked fitting with cleared preview;
  no delayed fitting after clearing the blocker; and a fresh owned retry that
  fits successfully after prior rejections. Host revisions progress 1→2→3→4.

Evidence: `build/guest-head-fit-audit/summary.md`, `validation.json`,
`native-final/guest-save-probe/result.txt`, `live-aligned/assertions.json`, native
and role logs/replies, native findings, payload/source hashes, protected-file and
cleanup records, `test-run-notes.json` and scoped patch. `live-candidate` and
`live-final` are setup runs: native pickup worked but the fixture's placement
fought the grabber physics. The final alignment temporarily holds the guest body
kinematic at the actual mount, uses native pickup and drops through the native
hand before sending the normal request. It does not relax production ownership,
proximity or tolerance checks. The initial native fixture also needed its empty
Mouse off state prevented from completing immediately.

All 18 protected personal files and 12 native guest .txt files remain unchanged.
No personal installation/profile was used for mutation, and no release was made.

Limits: local UDP, isolated copies/profiles and probe-driven requests. Physical
mouse selection/prompts, natural hand alignment, fresh-process save/reload,
Steam/two-PC and full car-building acceptance remain open. Guest head fastening,
missing/different heads and complete child-part parity remain separate work.
Existing Unity/audio/engine-readiness log errors and checksum recovery are not
claimed fixed by this task. The next bounded outcome rotates to firewood buyer
visibility and pending-offer amounts, needed to collect job payment naturally.


## Firewood buyer visibility and offers — 2026-09-13 (unreleased v220)

Guests now receive all four buyers' visibility, world pose and pending payment
amounts through FirewoodBuyerState (209). The native payment collider and
`TAKE MONEY … MK` label remain usable; the guest does not run buyer/job decisions
or optimistic cash/income additions. The existing v217 host reservation still
credits a collectable offer once. Optional buyer metadata failures retain that
payment safeguard.

Native house/buyer LOD and post-job departure distances include living guests
whose poses are no older than two seconds. This lets a guest visit and collect
while the host is elsewhere. The binding initializes the exact dormant graphs
without activating or starting them. Customer 1's native animation moves his NPC
root between CarPos and WoodPos; payment identity stays tied to the canonical
catalog entry, while live objects resolve from native job/buyer references and
the guest follows the host's pose. Pose, FSM flags and replaced native action
arrays are restored on cleanup.

Validation on the final matching Core/Net/probe/catalog payload:

- Core and probe Release builds against the installed game DLLs pass with zero
  warnings/errors, with personal deployment disabled. Net builds for net35 and
  netstandard2.0. **4,436 Net tests pass**, including 28 new buyer cases.
- **109 isolated native checks pass**: 48 buyer binding/presentation cases across
  the four extracted native graphs, 40 existing payment checks and 21 save guards.
  They cover distance binding/restoration, changed references, canonical identity
  after relocation, host pose, labels, pending/equal/stale revisions and cleanup.
- **28 local two-game assertions pass**. All four buyers and bills become visible
  with matching positions and amounts while the host remains away; prepared
  payments of 500/600/700/800 mk credit once and converge in cash and net income.
  Ten simultaneous requests against one offer pay once. Guest collection entries
  do not predict balances. Hidden/wrong local offers repair, and old states cannot
  resurrect collected offers. A new 321.5 mk offer for relocated customer 1 survives
  guest reconnect while the host stays nearby, arrives without paying itself and
  can be collected once. Both leaving hides the buyer through the native logic.
- All **18 protected personal files and 13 guest-profile text files** remain
  unchanged. Seven probe/marker/input cleanup checks pass; no owned game remains.

Evidence: `build/firewood-buyer-audit/summary.md`, `validation.json`,
`native-final/guest-save-probe/result.txt`, `live-final-verified/assertions.json`,
archived command replies and both role logs, `payload-comparison.json`, protected
file comparisons, cleanup records and the scoped source patch. Throwaway drivers
are archived in `drivers/`; copy them back into tools/ before reuse because they
derive the repository root from that location.

Earlier runs are diagnostic, not final acceptance. They exposed dormant discovery,
customer 1's different idle actions and relocation, and missing fixture animation
clips. The generic packet fixture needed a valid ID and normalized quaternion.
One reconnect setup let everyone leave, so native logic correctly expired the
offer; another collection ran before the guest's relocated pose settled and was
correctly rejected by proximity. The final driver waits for players and pose
settlement, and keeps the host nearby during reconnect. No production distance
or payment guard was relaxed. The full extracted input was reduced to the 20
relevant FSMs with decimal number notation for the existing small JSON reader.

Limits: the driver prepares a native order and sends the native UNLOADED signal
with a known amount; it does not cut/load/transport/unload actual firewood or
complete a real phone-order journey. Collection enters after native mouse
selection, so physical input is not proven. Signed penalties, car purchase from
customer 1, Steam/two-PC and four-player soak remain open. Existing unrelated
world/checksum/native-game diagnostics remain; this is not whole-mod stability
acceptance. Nothing was committed, released or installed into the personal game.

Next: run the ordinary local join/shop/drive/sleep/save-and-rejoin journey on one
identified build. This rotates from jobs to the cross-system acceptance gap;
Steam/two-PC acceptance stays separate.

## Combined local journey (2026-09-13, unreleased v220)

One unchanged 0.1.33/protocol-220 production payload on game build 23268598
completed the ordinary journey across two copied Linux/Proton game instances:
join, shop/unpack, drive together, sleep, native save/quit, process restart and
rejoin. `GuestSaveProbe.Plugin.Update` now ticks each explicitly enabled probe's
separate command channel, allowing these existing drivers to share one session.
No production behavior, catalog or protocol changed in this task.

Validation:

- **38 distinct passing local observations:** host checkout adds a three-product
  bag and charges both balances once; both holder roles reject a competing pickup;
  guest unpacking creates one item and leaves two; both roles drive the Sorbet with
  an accepted passenger, transfer the running engine, park and regain controls;
  declining sleep cancels, while consent completes and advances both clocks to
  14:00; native save/quit and restart restore cash, groceries, remaining bag,
  actual pre-save car position, time and guest needs/position. The resumed clock
  is 15:00. Reopening the saved bag creates only its two remaining purchases, and
  a repair snapshot preserves the unique inventory and closed spawn choice.
- **4,436 Net tests pass.** The Release Core/probe build against the actual game
  DLLs has zero warnings/errors, with `DeployToGame=false`. Core, Net, catalog,
  compatibility data, FastBoot and probe hashes match across the two journey
  stages. The full production source stayed unchanged from the task baseline.
- **18 protected personal files and 13 copied guest-profile text files remain
  unchanged**, including the guest's native save-and-quit. Only disposable host
  saves were written. Test games, probe DLL, three markers and command folders
  were removed from the copied installation after evidence capture.

Three initial driver assertions were corrected without changing the production
payload: local ownership uses a boolean rather than the local player's remote
owner ID; a newly spawned product gets a native saved-object ID on reload, which
must match between peers rather than the prior session's spawn ID; and persistence
must compare the car with the actual pre-save snapshot, not the earlier drive-end
snapshot. Raw failures and their dispositions are retained alongside 38 distinct
passing observations; duplicate observations are not counted twice.

The last correction exposed a separate unresolved observation: the Sorbet moved
**13.718 m between guest driver exit/sleep and saving**, with both games agreeing
at save time. Guest logs record proximity/pushing claims. A follow-up using the
later saved position and engine-off native seat entry/exit moved at most 2.5 mm
while the guest stayed in place, then 1.9 mm with both players away (nine samples
per phase, about 36 seconds each). That does not reproduce or explain the earlier
powered-drive/sleep transition. The first follow-up guest stalled during GAME
startup and produced no probe reply within 180 seconds; a single identical retry
succeeded. Preserve both runs; do not call all startup or parked-car behavior
accepted on this evidence.

Evidence: `build/journey-audit/summary.md`, `accepted-checks.json`,
`driver-assertion-corrections.json`, `first/` and `reload/` command replies and
both role logs, matching payload hashes, profile comparisons and cleanup records.
`parked-followup/` is the timed-out diagnostic; `parked-retry/` contains the bounded
parking comparison. One-off drivers are archived in `drivers/`; copy back to tools/
before reuse because they derive the repository root from that location.

Limits: test actions enter after physical mouse/key gestures, reposition players
between locations and supply bounded driving axes and sleep fatigue. This is not
physical-input, guest-checkout, long-drive, Steam/two-PC or four-player acceptance.
Existing native Unity diagnostics, missing Corris gearbox inputs and checksum
repairs remain in the logs. The combined sequence ran, but clean whole-mod
acceptance remains open. Nothing was committed, released or installed personally.

Next: reproduce the original guest-drive/exit/sleep parked movement from this
journey's initial disposable save, distinguish native/fixture behavior from a mod
fault, and fix only a demonstrated fault. This observed reliability concern takes
priority over unrelated new feature work at the next task boundary.

## Sorbet parking brake setting (2026-09-13, unreleased v221)

Tracing the original copied journey worlds found an actual brake-setting desync:
Sorbet's INCREASE/DECREASE states add to `KnobPos` per second and terminate through
local mouse picking. Replaying those states therefore produced different lever
values on the two processes. After guest parking, the host's native brake read
0.8207586 while the guest read 1.0.

The catalog now identifies the audited Sorbet control and its native clamp range.
`VehicleClimate` (61) appends strict availability and a finite normalized float,
raising its packet size from 23 to 28 bytes; protocol is 221. Existing ownership,
authentication, sequence rejection, reliable final-before-release and host snapshot
rules carry the exact value. A nearby user can claim an unowned vehicle for a lever
adjustment; another simulator's lease refuses it. Generic timed-state replay and
FSM snapshots/checksums exclude this control. Receivers write the scalar and stop
relative adjustment actions; host fallback retains the final value. Guest cleanup
restores its original local scalar. No new message ID or save-file format is added.

Validation on actual game build 23268598, mod 0.1.33, Linux/Proton:

- **4,451 Net tests pass**, including exact partial values, malformed fractions,
  availability/truncation, stale/wrong-owner packets, snapshot ownership, copies
  and invalid catalog isolation. Existing ice/heating packet-size fixtures and the
  generic message fixture were updated for the appended fields. Net Release builds
  for net35/netstandard2.0 and the real-game Core/probe Release build are clean.
- **30 distinct local gameplay assertions pass** on the fixed payload: the original
  shop, two driver/passenger roles and sleep sequence, both full native brakes after
  exit/sleep, an exact partial guest adjustment, reliable release, reconnect,
  returning host adjustment, and both native save-and-quit flows.
- **Three motion checks pass** in a final repeat from the original copied save with
  the guest moved clear of the cabin before sleep: both brakes stay at 1.0, matched
  car positions stay within 0.0292 m, and peak guest speed stays below 0.105 m/s.
  Both cars creep about 1.9 m during this interval under native physics, with the
  guest continuously holding simulation ownership and the host following.
- All **18 protected personal files and 13 copied guest-profile text files** remain
  unchanged. Test processes, probe DLL, three markers and command directories were
  cleaned up. No personal deployment, commit or release was made.

The original 13.718 m displacement was not reproduced exactly. The unfixed trace
reproduced a brake mismatch and roughly 3.3 m of post-sleep movement, including a
7.08 m/s guest impulse. With the scalar fix but the guest still left in the native
cabin exit position, brakes matched but movement/impulses remained (about 3.5 m,
21.54 m/s peak). Clearing the guest from the cabin removed the impulses in the last
repeat. This supports contact with the test's stationary player as a contributor;
it does not prove that the brake mismatch caused the entire original displacement.
Do not freeze native car physics or claim the original case wholly solved on this
evidence. Physical mouse/key exit, varied terrain, long parking and Steam/two-PC
acceptance remain open. The controlled trace repositions players and supplies
bounded driving axes/sleep fatigue; other vehicles' parking brakes are untouched.

Evidence: `build/parked-motion-audit/summary.md`, `net-tests.txt`, `net-build.txt`,
`first/` (unfixed reproduction), `candidate/` (fixed in-place comparison plus scalar,
reconnect and save checks), `clear-exit/` (cleared-cabin comparison), motion metrics,
exact command replies, both role logs, payload/source hashes, personal/guest file
comparisons and cleanup records. One-off drivers are archived under `drivers/`;
copy them to tools/ before reuse because their root calculation assumes that path.
A diagnostic bag snapshot attempted while the guest was driving failed because its
hand path was parented under the car; the requested native FSM description and the
motion trace still captured correctly. It was not a gameplay sync failure.

Next task: guest cylinder-head fastening, following the existing placement and
install/remove support. The observed brake mismatch is closed; remaining physical
exit/parking acceptance is kept explicit rather than expanding this test by inertia.

## Guest cylinder-head fastening (2026-09-13, unreleased v222)

Guests can tighten and loosen the fitted VIN1110 head's ten bolts using the native
Screw/tool interface. PartFitRequest/Receipt operations 6/7 now accept head slots
1–10, addressed by native array index plus one. The shared ledger, observed head
revision, settled native head/block ownership, living guest pose within three
metres, cooldown and native 0–8 bounds guard each host turn. Rejected requests do
not run later. Generic raw-event/BoltState registration is bypassed for these head
fasteners. Message 207 now includes availability, ten exact bolt values and the
independent native aggregate (61 packet bytes); both peers need protocol 222.

The guest's replacement tool graph changes only control scratch and visual offsets.
It does not write native Bolts, Data.Tightness, AssemblyID or parent BOLTING. Repair
mode and applied fitted state gate the pick colliders; removal disables them,
refitting/rejoining uses current host state, and cleanup restores original graphs,
scratch, visuals, group activity and collider states. The head removal prompt is
hidden while host tightness forbids removal. The binding reuses existing native
bolt signature validation and ten distinct array indices from catalogued geometry.

Validation on game build 23268598, mod 0.1.33, Linux/Proton:

- **4,463 Net tests pass**. Added cases cover all ten wire slots, exact aggregate,
  malformed values/availability/truncation, deep copies, conflicting or old
  revisions, tool request slot ranges, native bounds, proximity/readiness gates
  and altered/retried operations. Older packet-size and slot-range fixtures were
  updated. Core/probe Release and Net net35/netstandard2.0 Release builds have
  zero warnings/errors, with personal-game deployment disabled.
- **40 local two-game checks pass** on the final payload: 37 in `live-final/` and
  three after full process restart in `reload/`. The tests cover guest fitting,
  disabled repair mode, both turn directions on all ten slots, bolted removal
  rejection, stale/distant requests, both tightness bounds, host turns, visual
  offsets, removal/refit, reconnect, native save/quit to MainMenu, and loading and
  loosening the saved partially fastened head. Guest saved arrays, aggregate and
  assembly identity stay unchanged while their displayed host state changes.
- All **18 protected personal files and 13 copied guest text files** remain
  unchanged. Test games are closed; probe DLL, marker and command folder are
  removed from the copied game. No personal deployment, commit or release occurred.

Evidence: `build/head-fastening-audit/summary.md`, final/reload command replies and
checks, both role logs, tested payload hashes, source baseline/scoped patch,
file comparisons, build/test logs and cleanup records. One-off drivers are archived
in `drivers/` and assume the `tools/` location if reused. The first candidate caught
the need to handle runtime sibling suffixes in the bolt lookup. The next run passed
the gameplay checks but lacked the probe's save/reconnect opt-in. The final run
initially reached the fixture before its player was ready; choosing the native
returning-spawn offer and waiting for the player resolved that setup issue.
These setup failures are retained in the evidence and are not counted as passes.

Limits: separate copied worlds, local UDP with a probe-only stable guest identity,
probe-driven native tool events and aligned pickup. Physical mouse/tool selection,
originally fitted guest saves, missing/different heads, complete child-part parity
and Steam/two-PC acceptance remain open. Runtime logs still contain native Unity/
audio and existing readiness/teardown messages; this task does not claim clean
runtime logs or full engine-building acceptance.

Next task: verify fridge power and shared milk freshness when moving milk between
the room and fridge and changing power, fixing only demonstrated missing behavior.

## Electricity cutoff and fridge milk (2026-09-13, unreleased v223)

Native `ElectricityBills1/2::Data` enters `Cut off` and writes HouseElectricity
false every frame without changing MainSwitch. The v222 stream read MainSwitch,
leaving the guest powered. After an OPEN/CLOSE fridge cycle, the host's cooling
area stayed withdrawn while the guest's returned to the fridge. The before-run
captures this mismatch on game build 23268598.

Message 95 now shares actual supply separately from the main switch and bill
visibility, preserving native float debt. Four bounded pending slots retain state
through discovery/loading. The electricity binding validates the native running
and cutoff writers, then guests freeze the loaded meter in place using the existing
reversible suppressor. Host state drives supply/switch/debt/envelope while native
appliance controllers observe the supply. Disconnect restores the guest's original
values and execution. Phone timer behavior and the existing payment intent route
are unchanged. Both peers require protocol 223; no message ID was allocated.

Validation on build 23268598, mod 0.1.33, Linux/Proton:

- **4,476 Net tests pass**, including exact packet bytes, fractional debt, independent
  cutoff/switch flags, malformed meters/flags/debt/framing, retained copies and host
  admission. Core/probe Release and Net net35/netstandard2.0 Release builds pass with
  zero warnings/errors and personal-game deployment disabled.
- **46 local two-game checks pass** on the final payload. Both homes cover room
  placement, powered cooling, bill cutoff, guest OPEN/CLOSE, restoration, independent
  main-switch state and appliance activity. One hundred native milk ticks reduce
  condition by about 0.6 warm or 0.1 cooled, and guests converge. The native cooling
  area remains latched after cutoff until a door cycle; this vanilla behavior was
  preserved. Rejoins cover a guest originally powered while the host is cut off,
  and a guest's own native cutoff while the host is powered. Disconnect restores
  each original state; guest decay cannot compete and final Bad/name agree.
- All **18 protected personal files and 12 copied guest native text files** are
  unchanged. Both games exited successfully; the probe DLL, marker and command
  directory were removed from the copied game. No commit, release or personal
  deployment occurred. Existing Unity/audio/readiness errors remain in both runs;
  the final run adds no error signatures and no utility-binding failure was logged.

Evidence is under `build/fridge-power-audit/`: native-fridges.json, before-run
reproduction, final assertions/replies, role logs, payload/source hashes, build/test
logs, protected-file comparisons and the scoped patch. One-off drivers are archived
in `drivers/` and assume the `tools/` location if reused. The persistent probe's
milk opt-in is also required. Placement fixes a carton at the physical fridge
interior, independently of the movable native ChillArea; the test never writes a
cooling result. Cutoff/reset use native state entries, main-switch tests seed the
native variable, and returning guest choices use the existing prompt methods.

Limits: these are scripted local UDP tests with copied worlds and a stable probe
identity. Physical switch/door/payment input, natural bill accrual and payment,
native cutoff save/reload, phone cutoffs, individual fuses, other foods, and
Steam/two-PC acceptance remain open. The Chilling FSM itself is not streamed or
paused; arbitrary prior cooling-area states on join are not established by these
checks. Milk freshness always comes from the host regardless. This closes the
observed effective-supply mismatch, not all of household or M7 acceptance.

Next: verify head reconstruction when a joining guest's own save has no matching
cylinder head, a separate completeness issue for players with different saves.

## Missing cylinder-head save records (2026-09-13, protocol v223)

A guest need not have the host head's native save records. On build 23268598 the
scene already contains VIN1110. Its Data Init checks VIN1110AID; when absent, the
native BACK → Status → Idle → Stop path initializes that head with fresh defaults.
The existing attachment/fastener/valve adapters then reuse it. No replacement
factory, production code, protocol or catalog change was necessary in this task.

The isolated fixture removed exactly VIN1110AID/WEA/TGH/BLT/VLV/POS using native ES2
on a separate marked guest copy: tag enumeration changed from 386 to 380 with all
other tag names retained. A backup precedes deletion. The preparation API must run
after GAME loads because ES2's settings prefab is unavailable at plugin Awake.
The normal two-player run then uses the prepared profile with the fixture variable
unset; the host starts with its saved fitted head and partial fastening.

**24 local two-game checks pass:** one guest head throughout, all 21 child mounts
aligned, all eight valve displays receiving host settings, guest loosening,
removal/refitting/tightening and a valve adjustment, object resync, disconnect and
rejoin. The same native instance and its original guest Bolts/Valves arrays survive
the round trip. All six save records remain absent and all 12 prepared guest native
text files match their post-fixture hashes. All 18 protected personal files are
unchanged. Both games exited normally and the copied-game probe DLL, marker and
command folder were removed. Core/probe Release builds pass without warnings/errors.
The tested production DLL/catalog hashes match the prior v223 electricity build;
protocol tests were not rerun for this probe/documentation-only change.

Evidence: `build/missing-head-audit/summary.md`, `native-head.json` and matching
installed level hash, six-key fixture transcript, `live-accepted/checks.json`, exact
replies, both logs, payload hashes, protected-file comparisons and scoped patch.
One-off drivers are archived under `drivers/` and assume the tools/ location when
reused. `LiveBagProbe.HeadSave.cs` contains the opt-in marked-copy preparation and
head identity/save-record inspection. Probe startup exceptions now log explicitly.

The first two attempts exposed/diagnosed premature fixture initialization; they
are setup failures, not gameplay passes. Preparation then succeeded, but that run
produced no usable guest command reply within 180 seconds. Its cause remains
unresolved. The next run, using the prepared save and normal launch timing, passed.
Old logs copied into failed attempts are marked stale where identified. Existing
native Unity/audio/readiness warnings remain; no clean-log claim is made.

Limits: scripted local UDP, copied profiles and probe-driven tool/valve events,
aligned pickup and the existing returning-spawn methods. This verifies absent
native save records, not externally destroyed/corrupt scene objects, alternate
heads, a wholly empty guest world, full child-part parity, physical input or
Steam/two-PC. No commit, release or personal deployment occurred.

Next: verify shared moose-meat spawning from a host-chopped corpse, including
exactly-once visibility and late joining, before adding any missing implementation.

## Moose-meat output (2026-09-13, unreleased v224)

Previously, `CreateMooseMeat` output reached the general item scanner without a
creation descriptor for another game. The native factory now captures its exact
New object after creation/naming, waits for initialization, and uses the native
`moosemeat0` prefix/counter. Message 210 supplies identity, creation pose, revision,
condition and food variant. Existing item streams carry subsequent movement.
Saved native objects can be discovered by Use.ID; native save/reload acceptance is
still open. This deliberately does not extend ItemSpawn's trophy-only factory flag.

Replicas disable Use/Fire before cloning because native Use has no initial wait.
Only native grilled-food input/eating states are enabled on guests. Load/save,
independent spoilage and random cooking are excluded from that replica graph;
food names/materials come from validated native presentation states. Local guest
meat is hidden/paused and restored on disconnect. Eaten replicas leave native
body-less save tombstones; replacement and disconnect now remove those too.

**Validation:** 4,492 Net tests pass, including all-message round trips, host/channel
admission, malformed state rejection, revision order/wrap, identity/retirement and
catalog containment. Core/probe Release build against the real game DLLs passes
with zero warnings/errors and DeployToGame=false. Protocol and launcher metadata
are 224; mod version remains 0.1.33 and this work is unreleased.

**20 final local two-game checks pass** on build 23268598: four root corpse cuts,
its native four-piece limit, a spine cut while disconnected, five-piece late join,
an overlapping guest native ID kept separate, repeated snapshots without duplicates,
raw/grilled/rotten/charred/spoiled appearance/condition, guest eating with host removal,
retirement during resync/rejoin, original guest object/FSM restoration and no replica
save tombstones after disconnect. All 18 protected personal files and 12 copied guest
native text files retain their hashes. Both games quit normally; probe/marker/command
folder cleanup is complete. No commit, release or personal deployment occurred.

Evidence: `build/moose-meat-audit/summary.md`, native scene/prefab extracts with
source hashes, `live-accepted/checks.json`, command replies, both game logs,
payload hashes, file comparisons and `scoped.patch` against the turn baseline.
One-off drivers are archived under `drivers/` and assume their original tools/
location. The bounded developer probe is opt-in and requires marked copied profiles.

Earlier attempts are retained separately. The first attempt exposed the dormant
moose setup and clarified that GetName reads the prefab for the identity prefix.
Two eating checks did not converge; the first also skipped the returning-spawn
choice. The original host rejection reasons were not captured. The subsequent
19-check traced run and the final 20-check run passed. The final driver waits for
the spawn choice and an observed fresh nearby host-side guest pose before eating;
this is not a claim that intermittent interaction-readiness issues are resolved.
Existing Unity/audio/readiness warnings remain; no clean-log claim is made.

Limits: scripted local UDP and copied profiles. Chopping enters the native Pieces
state (including its output limit); food variants use native state entries and
eating uses the native Eat state. Physical axe/mouse/tool input, guest chop intents,
natural cooking/spoilage timing, native meat save/reload, four-player soak and
Steam/two-PC acceptance remain open.

Next: verify native utility-bill payment from debit through cleared debt and
restored shared power, including repeat requests and reconnect.

## Electricity bill payments (2026-09-13, unreleased v225)

The previous local v224 build rejected a guest electricity Pay request while the
host's sheet was inactive: `588EADF9 PAY` was logged as an unknown target/identity.
The generic purchase path also checks the off-screen sheet button's transform,
not the mailbox, and replays Date's native cash subtraction. The electricity Pay
buttons now leave generic buys. A dedicated adapter binds both inactive sheets,
validates native action arguments, displays the host invoice and waits for a
receipt. Host-local clicks use the same ledger as guests.

UtilityBillState (95) appends an invoice revision. New messages 211/212 carry a
quoted payment request and receipt. The host checks the current debt/envelope,
the authenticated player's fresh position within 6 m of the native Bill reference,
and sufficient finite cash. It settles once and runs native Pay bills, which
clears UnpaidBills/WaitCutoff, hides the envelope and restores supply. MainSwitch
remains independent. Exact retries return the prior receipt; competing/changed
quotes cannot spend again. State and wallet precede the receipt. Only the payer
plays the native receipt sound; no guest or observer replays cash subtraction.
Disconnect closes pending forms before restoring native actions and guest meter
values. Phone payment and phone charge calculation are unchanged.

**Validation:** 4,505 Net tests pass (13 new transaction/wire/catalog cases),
including duplicate/competing requests, changed quotes, sequence wrap/reuse,
exact-balance and insufficient/invalid cash, absent invoices, malformed packets,
authenticated message directions and catalog containment. Core and developer probe
Release builds against the real game libraries have zero warnings/errors, with
DeployToGame=false. Both Net targets compile through the Core build and test loop.
Protocol/compatibility metadata are 225; the mod remains 0.1.33, unreleased.

**20 final local two-game checks pass:** both bindings, debt/cutoff agreement,
exact guest quotes with the host's sheet closed, guest payment plus five duplicate
requests per meter, delayed receipts against a new invoice, host payment while the
guest has the old form open, insufficient funds, distant payment rejection,
MainSwitch-off settlement, conflicting guest data on reconnect, original guest
meter restoration on disconnect, second join, repeated world resync and a fresh
post-reconnect guest payment with restarted sequences. The final payload matches
the built Core/Net DLLs, catalog and compatibility metadata. All 18 protected
personal files and 12 copied guest native text files retain their hashes. Both
games quit normally; the probe, marker and command folder were removed. No commit,
release or personal deployment occurred.

**Acceptance limits:** installed build 23268598 has `m_Enabled = 0` on both
EnvelopeElectricityBill Use components. The first attempts stopped at those
disabled controls before reaching payment. The fixture explicitly enables each
copied envelope Use and sheet/button FSM, seeds debt/cash and enters native Cut off,
Open bill and Check money. Production does not enable those dormant controls.
This proves transaction behavior with prepared controls, not naturally generated
bill availability, physical mailbox/mouse input, phone payments, native cutoff or
payment save/reload, Steam/two-PC or a four-player soak. Existing unrelated Unity,
audio and readiness warnings remain; no clean-log claim is made.

Evidence is under `build/utility-payment-audit/`: hashed native action extraction
and `native-enabled.json`, previous-build `before-exposed/reproduction.json` and
logs, `live-final/checks.json`, replies, game logs and payload hashes, file hash
comparisons, `validation.json`, `source-changes.json`, `scoped.patch` and `summary.md`.
`live-first` records an earlier 18-check pass and its prior fixture stop separately.
The one-off drivers are archived in `drivers/`; they assume their original tools/
location. The opt-in probe requires the marked copied game and disposable profiles.

Next: validate passenger death/respawn with two running games, including releasing
the old seat, restoring movement and accepting a fresh seat entry. This addresses
a separate player-recovery gap instead of extending the electricity scope.


### Native passenger recovery (2026-09-13, unreleased v226)

Native two-game acceptance reproduced two v225 failures. Immediately after
passenger death, native State 3 had destroyed FPSInputController, CharacterMotor
and CharacterController while PLAYER remained parented to Sorbet and the other
peer still held the seat. Take photo released it several seconds later. At the
newspaper, after approximately 133 seconds, both peers marked the guest alive
while PLAYER was inactive and all three movement components were still absent.

The installed level2 extract (game build 23268598, SHA256
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`)
shows State 2 is the newspaper. Its continuation resets needs, broadcasts native
SAVEGAME and loads MainMenu. It never restores movement in the current scene.
The previous claim that State 2/timeout established respawn was incorrect.

PlayerDeathHook now initializes the inactive death graph's action bindings before
activation and observes State 3 before native destruction. Take photo remains an
idempotent fallback; registration also catches an already-running death graph.
DeathSyncManager retains pending death/report history through scene changes.
The 120-second threshold logs once and keeps waiting. A respawn requires an
active GAME player with enabled native movement and a finished/inactive death
graph. A guest must also finish spawn selection and relocation. Reports use the
live player pose; inactive cached death poses cannot announce life. Existing
reliable death/respawn message layouts and IDs remain unchanged. Protocol/launcher
manifest become 226 for the semantic change; next free ID is 213 and mod version
remains 0.1.33. No release or personal deployment was performed.

Validation and evidence are under `build/passenger-recovery-audit/`:

- `before-native/death-observations.json` preserves both reproduced failures.
  `live-before` stopped at the probe's permadeath guard before triggering death.
- `live-fixed/newspaper-*.json` verifies both peers remain dead through three
  45-second holds. Its Core/Net hashes match `live-final` exactly. The surviving
  host also occupied the dead guest's seat, and the guest completed native
  newspaper/MainMenu/load and a fresh accepted entry.
- `live-final/checks.json` contains **18 passing checks**: immediate seat release
  in both roles, the surviving guest taking the host's seat, native menu/load,
  guest spawn-selection gating, restored walking, fresh accepted claims and clean
  exits. `checks-first.json` retains the first walking failure: 212 input updates
  moved only 0.28 m from a spawn inside the car. Repeating in the existing shop
  fixture moved approximately 5.9 m in three seconds; the resumed checks use that
  clear area. Walking supplies a bounded direction to the native input/motor;
  it does not move the player transform during the measured walk.
- Core and the probe build with **zero warnings/errors** against actual game
  assemblies. **4,505 Net tests and 18 launcher tests pass**; the latter required
  restoring SDK targeting references before running. Both Net targets build as
  dependencies. Final payload hashes, protection hashes, scoped changes and
  cleanup status are recorded in `validation.json` and `scoped.patch`.

The test uses `build/local2p/game` and marked disposable Proton profiles. Recovery
commands require both the opt-in flag and an exact per-profile marker; the probe
refuses any death command when session or native permadeath is enabled. The live
session/native flag mismatch (`false` / `true`) required an explicit normal-death
fixture in each copied profile, including after loads. **This is a remaining
production binding bug**, and the next task must resolve it before permadeath or
group-wipe acceptance. No native action in the death/save/load graph was stripped.
The host's disposable world genuinely saves/reloads; the guest's 12 native text
save files and all 18 protected personal save/installed-mod files stay unchanged.
Probe binaries/markers/command folders are removed and both games exit normally.
One-off drivers are archived under this audit's `drivers/` folder.

This closes the bounded normal-death passenger recovery fixture, with limits:
Sorbet is stationary; actual physical keys, cameras/rendering, collision causes,
prolonged cold/warm driving, permadeath/group wipe and Steam/two-PC remain open.
Host death still leaves the live world for native MainMenu/load, so continuous
host-world operation is not implemented. The guest reload logged an unavailable
hockey ArrayList restore; full shared-world reload acceptance is not claimed.


### Native permadeath settings (2026-09-13, unreleased v227)

The v226 two-game reproduction showed both peers with session=false and native
PlayerPermaDeath=true, despite a saved PlayerPermaDeath=true. The old reader used
`UniqueTagPlayerPermaDeath` as a filename; it also attempted an invalid reflective
lookup of generic ES2.Save using a concrete bool parameter. It could report a
successful false read from a missing path. Its guest save-write path could neither
establish the native runtime global nor coexist with personal-save protection.

Installed level0 confirms native Continue (`Interface/Buttons/ButtonContinue`,
SetSize, Reset globals 2) runs LoadBool into global PlayerPermaDeath with
SavePlayerData=savefile.txt and tag PlayerPermaDeath. Character creation runs
SaveBool from that same global (`Licence/Buttons/ButtonBegin`, Generate ID).
The native action builds the address with `?tag=`. Game death reads this global
at both Permadeath 2 and Permadeath.

PermadeathSettings now reads the host's native address in menus and the loaded
host variable in GAME. Missing saves remain unavailable for the new-character
flow. A narrowly matched guest LoadBool uses the accepted host flag before Finish,
so native startup readers cannot observe the guest save's conflicting mode.
Handshake/runtime updates apply immediately; the connected guest also corrects
replacement globals without a one-shot latch. Guest save writes and synthetic
achievement events were removed. Native host LoadBool/SaveBool changes publish
SessionSettings 213, a reliable ordered one-byte flags message (three packet
bytes). Bit 0 is permadeath; other bits, guest senders, unselected hosts and
pre-handshake updates are rejected. Protocol/manifest are 227, next free ID 214,
and mod version remains 0.1.33. Native binding failure prevents guest admission.
Only scheduled normal-death recovery is preserved across scene changes; a
permadeath death with no respawn watch does not carry into a new scene as a
pending normal respawn.

Evidence under `build/permadeath-binding-audit/`:

- `live-before` captures v226 native saved/global=true but session/read=false,
  then host native SaveBool=false while guest native LoadBool still restores true.
- `live-fixed/checks.json` contains **21 passing checks**. Native host saves publish
  false/true/false, guest LoadBool immediately follows each host value, guest native
  SaveBool attempts preserve the original true personal tag, and reconnect repairs
  a deliberately conflicting guest global. Full MainMenu/native Continue loading
  on both host and guest is checked with both settings. No normal-death override
  fixture from v226 is used in this run.
- **4,515 Net tests** pass, including ten new setting framing, unknown-bit,
  sender/handshake and channel tests. **18 launcher tests** pass. Core/probe build
  against the actual installed game assemblies with zero warnings/errors; both
  Net targets build as dependencies. `validation.json` records hashes and results.
- Tests use the copied game and separate, explicitly marked disposable Proton
  profiles. Only the disposable host's setting is intentionally saved. All 12
  guest native text saves and all 18 protected personal save/installed-mod files
  remain byte-identical. Both games exit normally and probe binaries, markers and
  command folders are removed. One-off drivers are archived in `drivers/` (copy
  back to tools to run them); native extracts and the scoped source patch remain
  in this audit.

This closes the setting-binding task. Full character-creation UI, actual
permadeath group death/save deletion, interrupted group wipes, death during a
drive, physical input and Steam/two-PC remain untested here. The separate native
host death/world-reload limitation and hockey ArrayList restore warning remain
open. No commit, release, installer build or personal deployment was performed.
Reassessment selects guest moose chopping as the next bounded gameplay task.

## Guest moose chopping (2026-09-13, unreleased v228)

Native `CarHit/State 2` detaches the corpse, activates it and destroys the live
moose root. The old mover stream could lose that death entirely: the v227
reproduction in `build/guest-moose-chop-audit/live-before` showed a live host
corpse and an inactive guest corpse. The two Chop FSMs also advanced independent
four-piece counters while the guest meat factory was paused.

`ItemWorldSync.MooseChop*` now binds the dormant native graphs and preserves the
guest's original animal. The host publishes the surviving corpse's 11 body poses
and front/rear counts independently of the destroyed mover. Guest CarHit reports
before local destruction; the host checks a fresh nearby reporter and runs the
native death entry. Guest axe comparison and sound remain native; Pieces sends a
request containing the corpse identity, section and observed count. The host
requires that exact count, an idle section/factory and a fresh alive player within
4 m, then runs native Pieces with Spawnpoint temporarily set to the section.
Repeated requests cannot consume that piece again. Existing v224 meat state
publishes the exact native outputs. Protocol/compatibility is 228, messages are
214/215, next free ID is 216; the mod stays 0.1.33 and is not released.

The detached corpse also weighed enough to pass the vehicle mass fallback. The
first run exposed guest push ownership and cargo streaming for it. Vehicle
registration now excludes the catalog's native corpse name, so ragdoll ownership
stays with the host's corpse adapter.

**Final checks:** `build/guest-moose-chop-audit/live-accepted/checks.json` contains
24 passing local two-game checks on the final payload. These cover authenticated
guest native death entry, matching corpse sections, the wrong-object axe check,
distant/stale-corpse/spoofed-player requests, a guest chop at the corpse, paused
local guest factory, exact retries, competing requests, concurrent host/guest
chops, both native limits, eight distinct meat IDs, native guest restoration on
disconnect, host chopping while disconnected, snapshot instance stability and
reconnect without restoring consumed pieces. Final logs contain no corpse vehicle
registration, push ownership, cargo stream or chopping subsystem failure.

Core and the guarded probe build against the installed game DLLs with zero
warnings/errors and `DeployToGame=false`. All **4,541 Net tests and 18 launcher
tests pass**. This includes 26 new protocol/authority/count/catalog cases. The
final Core/Net/catalog/compatibility hashes match the copied test payload. All
18 protected personal files and all 12 copied guest native text files remain
unchanged. Both games quit normally; the copied-game test DLL, marker and command
folder are removed. No commit, release or personal deployment occurred.

Evidence is in `build/guest-moose-chop-audit/validation.json`, `summary.md`,
`source-changes.json`, `scoped.patch`, the installed-asset `native-moose.json`
extract, build/test logs and the exact live replies/logs/payload hashes. The
929-file starting tree is preserved by `baseline.json` and `before/`. Disposable
drivers are archived in `drivers/` and assume their original `tools/` location.

Earlier attempts remain separate: `live-first` established basic corpse/chop and
reconnect behavior; `live-final` completed 23 checks after a driver row-index fix,
but its logs exposed the vehicle classification conflict and it is superseded.
`live-guest-death` failed to establish controlled proximity while the live moose
ran away, then found CarHit already inactive; it is not an accepted guest-hit
check. The final run holds the test moose's Move FSM with a guarded test fixture,
positions a fresh guest near the host corpse location, enters native CarHit and
verifies the host's recorded guest death report before chopping.

These are local UDP fixtures using disposable profiles. Axe tests enter native
GameObjectCompare with the actual axe Pivot reference; they do not establish
physical mouse swings or vehicle collisions. Live-moose stream reacquisition,
physical corpse interactions, native meat save/reload, natural cooking/spoilage,
Steam/two-PC and four-player soak acceptance remain open. The bounded shared
chopping task is closed. Reassessment rotates to actual two-player permadeath
group-wipe consequences in disposable saves, since settings/recovery checks alone
do not verify host-world deletion and guest-save safety when everyone dies.

### Native permadeath group wipe (2026-09-13, unreleased v229)

The existing game rule is **one death ends everyone's permadeath run**. The prior
roadmap's first-death/last-survivor wording was incorrect; this checkpoint retains
the existing rule. It uses only `build/local2p/game` and separately marked,
disposable profiles under `build/permadeath-wipe-audit/profiles/`. Native death
really deletes the copied host world, so never run this probe against personal
profiles or remove its environment/marker guards.

A v228 guest-started reproduction found three concrete defects:

- Activating the native death object already entered Permadeath 2, Delete saves 2
  and State 3. The remote trigger then forcibly entered State 3 again, repeating
  destruction of movement components. Cause selection also occurred too late.
- A stale PlayerRespawn marked a permanently dead guest alive, and duplicate
  death reports published repeated wipe/chat events.
- MainMenu reset local death; the host then accepted a returning guest into the
  same transport session even though the world files had been deleted.

The trigger now validates the actual native start graph, sets its cause before
activation and lets both native delete stages run once. It does not enter State 3
or send a competing cause event. `DeathSessionPolicy` records the first wipe
before broadcast. Host and guest keep the terminal state through scene changes;
respawn application/relay/recovery and further admission are gated until the
transport session resets. Reconnect refusal tells the guest that the run ended.
A later settings change cannot revive it. Session reset clears local death/watch
state while preserving hooks already installed on a surviving graph.

The unchanged `GuestSaveGuard` suppresses native guest deletions of `savefile.txt`,
`carparts.txt`, `items2.txt`, `meshsave.txt`, `trophies.txt`, `hockeyleague.txt` and
`speedcam.txt`. The host follows native deletion of those seven files. Graveyard
recording follows the native host flow; it is not a world-deletion target.
Protocol/compatibility 229 is a semantic bump with no new layouts or IDs; the next
free message remains 216 and the mod version stays 0.1.33. Nothing is released.

**26 final local two-game checks pass on the same payload**, across
`live-final-host`, `live-final-guest` and `live-final-normal`:

- Either player's native death ends both permadeath players; both native startup
  and delete stages execute once. The host loses exactly the seven deletion
  targets while the guest retains them.
- Three duplicate guest reports plus a stale respawn leave death, stage counts
  and chat unchanged. Native Newspaper → Orbituary → MainMenu preserves the ended
  run, and reconnect is refused with a clear reason.
- With only the disposable host setting changed to false, guest native death
  takes State 2/Save all/MainMenu without killing the host or deleting world
  files. Native Continue and the completed returning-player choice restore life;
  the host receives the actual respawn and a later reconnect succeeds.
- All 12 copied guest native text saves remain byte-identical after each run.
  All 18 protected personal save/install files remain unchanged. Both games quit
  normally after every final run; test DLL, game marker and command folder are
  removed. No personal installation, commit or release was made.

The Release Core/Net/probe build against installed game DLLs has zero warnings or
errors with `DeployToGame=false`; **4,545 Net tests and 18 launcher tests pass**.
Four new policy tests cover normal recovery, one-time wipe, a later settings
change and explicit session reset. Final runtime/catalog/compatibility hashes
match all three accepted copied-game payloads.

Evidence: `build/permadeath-wipe-audit/validation.json`, `summary.md`,
`source-changes.json`, `scoped.patch`, `protected-{before,after}.json`,
`cleanup.json`, native death extraction, build/test logs and each run's exact
replies/logs/payload hashes. `baseline.json` and `before/` preserve the 934-file
starting tree. Temporary drivers are archived in `drivers/`; their relative paths
assume the original `tools/` location. `live-baseline` records the actual v228
regressions. `live-before` stopped at a probe reflection error before death;
`live-fixed-guest` predates the final local recovery gate. `live-normal-first`
missed the asynchronous spawn prompt and timed out while correctly dead; the
final driver waits for that prompt. These attempts are not final acceptance.

The fixture activates the actual native death graph with Fatigue selected and
advances its newspaper/obituary events; Continue uses native menu loading. This
establishes the loaded two-player UDP consequence, not every physical death cause
or menu input. Simultaneous natural deaths, interruption during deletion, Steam/
two-PC, four players, every remote cause screen and fresh-character hosting after
a wipe remain open. Existing startup/Unity/FSM warnings, including the guest
hockey reload warning, are not a clean-log pass. The bounded group-wipe task is
closed. Reassessment selects phone bill settlement next: two peers must pay the
same native invoice once, with matching cash and PhonePaid state after retries
and reconnect. Phone still uses the generic purchase path; electricity's receipt
checks do not establish phone charge calculation or settlement.

### Shared phone bill payments (2026-09-13, unreleased v230)

A native v229 reproduction showed different invoices: the copied host's 100 local
minutes, 20 long-distance minutes and 3+2 connections produced **140 mk**, while
the guest's own zero usage produced **128 mk**. Only UnpaidBills=500 and PhonePaid
were synchronized; the guest envelope was initially missing. After explicitly
exposing the disposable envelope, guest payment cleared only its local meter,
without settling the host's bill or shared wallet. The host's later payment alone
debited 140 mk. `live-before` records the missing envelope; `live-baseline` records
the distinct quotes and local-only guest settlement.

The dedicated `phonePayments` catalog replaces the generic PhoneBill purchase
rule. Host state now supplies four usage counters and four native sheet tariffs
in addition to debt, line status, envelope and invoice revision. The native sheet
price includes its base fee and differs from the meter's UnpaidBills accumulator.
Both detail columns and total use the host quote. The native calculation starts
with zero CostFinal so reopening does not compound previous charges. A changed
usage/rate invalidates the old quote even when the amount stays equal.

Phone meters use the existing receipt ledger: the host validates displayed
revision, envelope, nearby living payer, cash and native settlement bindings,
debits once and runs native Pay bills once. That hides the envelope, restores
PhonePaid and clears debt, cutoff timing and all four counters. Guest Date retains
feedback and local OldBill handling only. Guest timers pause after native load;
original meter/usage/envelope state restores on disconnect, and the next local
sheet opening recalculates. Host payments keep processing after the last guest
leaves. Packet 95 appends phone inputs for meters 2/3 (44 packet bytes); electricity
stays 12 bytes. Existing intent/result 211/212 now accept all four meters. Protocol
and compatibility become 230, next free ID stays 216; mod stays 0.1.33, unreleased.

**25 final local two-game checks pass** in `build/phone-payment-audit/live-final`:

- Both native phone forms bind, show matching charges with the host form closed,
  and retain the same total when reopened. Either role pays both bills once;
  five exact retries cannot add another debit. Native usage counters reset on
  both peers and the guest's old invoice closes after a host payment.
- Insufficient funds and a distant payer leave debt/cutoff/cash untouched.
  Competing host and guest requests cause one 140 mk debit.
- Reconnect and repeated resync retain the settled invoice. Disconnect restores
  deliberately conflicting original guest debt, line state and four usage
  counters. A new invoice accepts restarted guest sequences after rejoining.
- Both electricity meters retain their acknowledged single-payment behavior.
  A host alone after guest departure can pay; the returning guest receives that
  settlement without another debit.

Release Core/Net/probe build against installed game DLLs passed with zero warnings
or errors and `DeployToGame=false`. **4,557 Net and 18 launcher tests pass**,
including 12 new phone arithmetic, wire validation, authority/receipt, copy and
catalog cases. All 18 protected personal save/install files and all 12 copied
guest native text saves remain byte-identical. Both games quit normally; test DLL,
game marker and command folder are removed. No personal deployment, commit or
release occurred.

Evidence: `build/phone-payment-audit/validation.json`, `summary.md`,
`source-changes.json`, `scoped.patch`, build/test logs, protected file hashes,
`cleanup.json`, installed-asset `native-phone.json` and exact live replies/logs.
`baseline.json` and `before/` preserve the 936-file starting tree. Runtime/probe/
compatibility hashes match the final run; `catalog-format-only.json` verifies
identical parsed catalog data after restoring unrelated starting-file formatting.
The tested catalog bytes remain in `live-final/tested-catalog.json`. `live-first`
passes the earlier 23 checks but predates the host-alone/teardown cleanup. Drivers
are archived under `drivers/` and assume their original `tools/` location.

The fixture explicitly exposes/enables copied envelope/form controls, seeds usage
and enters native open/payment states. It does not establish natural invoice
availability, physical mouse/camera interaction, accrual from real phone calls,
native bill save/reload or Steam/two-PC/four-player behavior. Existing startup/FSM
warnings are not claimed clean. The bounded settlement task is closed. Reassessment
rotates next to the recurring guest hockey ArrayList errors during native reload:
reproduce the restored data mismatch and verify that resuming a shared world no
longer produces that error while preserving the guest's own saved data.

### Hockey scene cleanup (2026-09-13, unreleased v230)

The current build reproduced `Hockey state restore failed: Hockey live ArrayList
unavailable.` after the guest entered native MainMenu. Both remembered source FSMs
were already destroyed. The same payload correctly restored an ordinary in-game
disconnect, so the failure was a scene-lifetime mismatch, not a demonstrated
mismatch in saved hockey data. `Clear` now skips destroyed list/table proxies,
restores scalar variables only while their owning FSM survives, and refreshes
pages only while both sources survive. Live proxy validation still throws for
missing/read-only collections; its diagnostic has not been suppressed.

**13 final local two-game checks pass** in `build/hockey-reload-audit/live-final`:

- Initial native board matches and guest generators pause; a changed host odds
  value reaches the guest. Disconnect restores hashes covering all nine lists,
  six tables, LatestRound, GamesPlayed and KurPaWins, plus generator enable/restart
  flags. Rejoin receives the current host board.
- Two native menu/Continue cycles discard destroyed bindings without the warning,
  load new native collections and receive the shared board. Fresh host updates
  and full resync work after each cycle.
- A final disconnect restores the guest board captured from the new scene.

The Release Core/Net/probe build against installed game libraries passes with
zero warnings/errors and `DeployToGame=false`; all **4,557 Net tests pass**.
All 18 protected personal save/install files and all 12 native text saves in each
copied profile remain byte-identical. Both games quit normally; the test DLL,
marker and commands are removed. Runtime/probe/catalog/compatibility hashes match
the final run. No wire change, personal deployment, commit or release occurred:
protocol/compatibility remains 230, next free ID 216 and mod version 0.1.33.

Evidence: `build/hockey-reload-audit/validation.json`, `summary.md`, build/test logs,
`native-save-comparison.json`, protected file hashes, `cleanup.json`, exact command
replies and both game logs. `baseline.json` plus `before/` capture the inherited
939-file tree; `source-changes.json` and `scoped.patch` isolate this change.
Temporary drivers are archived under `drivers/` and assume their original `tools/`
location. The reusable probe is opt-in and requires both a copied-game marker and
an exact disposable-profile marker. The baseline's initial warning assertion ran
before log flushing; the warning was independently confirmed and recorded before
shutdown. Final warning checks wait for the writer and recheck complete logs.

Limits: local UDP, two copied profiles, seeded native host odds and native
menu/Continue state entry. No natural round generation, visible teletext pages,
Megaveto settlement, Steam/two-PC or four-player acceptance is claimed. Generic
native index messages and other Unity errors still occur on both roles; they are
counted separately in validation and are not attributed to this cleanup warning.
The bounded fix is closed. Next: the guest firewood delivery-to-payment flow with
the host away, because actual delivery remains unverified between the separately
checked buyer visibility and settlement steps.

### Shared firewood unloading (2026-09-13, unreleased v231)

The v230 baseline loaded the host trailer with 400 Logs / 300 Firewood while the
guest still saw zero. Preparing an equal guest load and starting native unloading
created only a local pile; the host kept its load and open order. A host-native
control produced the correct shared 700 mk offer, including the -100 early bonus,
but the old JobSite application clamped the guest's penalty to zero.

`FirewoodDeliverySync` now binds the catalogued flatbed load and ground-check FSMs
after native loading, shares exact load/mass/fill and a complete pile manifest,
and pauses guest consumption/pile generation. Guest hatch/tilt writes request a
host-native start/stop; the host validates authenticated identity, fresh living
proximity, load epoch and request sequence. Load additions/reset retire old
epochs, and readmission resets only that player's request latch. The native host
retains delivery, bonus/penalty and offer calculation. Resuming a partly unloaded
load reuses its existing native ground pile. Disconnect restores original guest
load, mass, pile visibility and FSM suppression state. JobSite firewood penalties
retain their sign; other site kinds keep nonnegative clamping.

**26 final local two-game checks pass** in `build/firewood-delivery-audit/live-final`:

- Host-supplied load and bed mass reach the guest. A distant unload request and
  replay from a finished load cannot consume stock or create another pile.
- Native deliveries produce matching active pile geometry, delivered Surplus,
  completed order and offers of 700, 1,560, 200 and 300 mk. Early bonus, positive
  penalty and zero adjustment are included. Ten repeat collection requests cannot
  add another payment; final shared cash increases by exactly 2,760 mk.
- Partial unloading pauses without continued loss, then finishes into the same
  pile. Disconnect restores the guest's original empty trailer and native FSM.
  Rejoin restores both piles and the unpaid offer; full resync adds no duplicates.
  The returning guest collects and can initiate a fresh unload with restarted
  request numbering.
- Native guest hatch entry with a prepared tilted bed starts shared unloading.
  Either role collects a guest delivery. Final disconnect removes all replicas
  and restores the original guest load.

Release Core/Net/probe builds against installed game libraries pass with zero
warnings/errors and `DeployToGame=false`. **4,582 Net and 18 launcher tests pass**;
the new Net cases cover wire validation/round trips, authority/channel admission,
wrapping revisions, copied manifests, request guards, signed adjustments and
missing catalog fields. All 18 personal save/install files and each disposable
profile's 12 native text saves are byte-identical. Both games quit normally and
the copied probe, marker and command folder are removed. Final Core, Net, probe,
catalog and compatibility hashes match current outputs. No personal deployment,
commit or release occurred; protocol/compatibility is 231, next message ID 218,
mod remains 0.1.33.

Evidence: `build/firewood-delivery-audit/summary.md`, `validation.json`,
`native-save-comparison.json`, protected hashes, `cleanup.json`, build/test logs,
native asset extracts and exact final replies/logs. `baseline.json` plus `before/`
preserve the inherited 940-file tree; `source-changes.json` and `scoped.patch`
isolate this task. Temporary drivers are archived in `drivers/` and assume their
original `tools/` location. `live-first` is intermediate evidence;
`live-before-spawn-readiness` contains a diagnostic reconnect collection attempted
before the asynchronous spawn selection completed. The host correctly refused
the distant payment. The final driver waits for an actual fresh host-side player
pose after resolving that prompt; the production proximity guard is unchanged.

Limits: load/order/adjustment fixtures, directly positioned frozen trailer,
prepared bed tilt and native state entry in two copied games over local UDP.
This validates unloading through collection from an existing host load. Guest
loose-log/firewood loading and cutting are not supplied by this subsystem;
tractor transport, physical control/camera input, native delivery save/reload,
Steam/two-PC and four-player acceptance remain open. The manifest supports up to
128 ground piles; invalid bindings/native state disable only delivery sync with
a diagnostic. Complete final logs contain zero delivery-disable/restore or buyer
disable warnings, but other Unity errors remain (including four host and three
guest native index exceptions). No clean-log claim is made. The bounded delivery
task is closed; next is native Corris puncture behavior through guest driving,
host handoff and parked rejoin.

### Guest Corris punctures (2026-09-13, unreleased v232)

The v231 native two-game reproduction reached guest driver ownership and rolling,
but its `PUNCTURE` event immediately returned the wheel to healthy. Protected
saved-health writes left the guest reading the host's unchanged 80 health; the
native flat-state reader took `FIXED` before polling could report damage. The
host remained healthy. This is captured in `live-matching-world` under
`build/puncture-drive-audit/`.

The guarded native guest flat-state entry now sends `WheelPunctureRequest` (218).
The host requires the authenticated living current driver, fresh motion, forward
sequence, available positive health and the exact wheel lifecycle epoch. It
validates and executes only the catalogued native zero-health write, then sends
message 205 immediately. Native Condition supplies flat physics on both roles.
Four appended epochs in 205 retire repaired/replaced/withdrawn tyre inputs
independently; ordinary wear and another wheel's puncture do not invalidate an
in-flight event. Guest native saved-health writes remain blocked.

**18 final local two-game checks pass** on one identified payload in
`build/puncture-drive-audit/live-verified`:

- All four actual wheel consumers use host health 80 while guest part Data stays
  65. A native guest puncture during controlled rolling changes host mount and
  part Data to zero; both peers agree on flat state, radius and friction fields.
- Guest exit, host takeover/rolling, parking, disconnect, rejoin and full resync
  retain the flat tyre. Guest part health remains 65.
- The returning guest's restarted request sequence works; three simultaneous
  other-wheel punctures all reach host part Data without retiring one another.
- Preparing a host health repair to 90 restores healthy state and matching wheel
  fields. Old epochs, spoofed player identity, parked-observer requests and a
  replayed sequence cannot damage the repaired tyre. A current driver request
  still succeeds after repair/handoff.

Release Net (both targets), Core and probe builds pass with zero warnings/errors
using installed game libraries and `DeployToGame=false`. **4,592 Net and 18
launcher tests pass.** Net coverage includes strict wire framing/validation,
independent epochs, repair/replacement/availability retirement, ordinary wear,
revision conflicts, copied state, authority/channel rules and callback ordering.
Protocol and compatibility are 232, next message ID 219, mod remains 0.1.33.
No personal deployment, commit or release occurred.

All 18 protected personal save/install files are unchanged. Each disposable
profile retains its 12 native text saves compared with its prepared baseline.
For this fixture only, six guest native files had deliberately been copied from
the disposable host: speedcam, savefile, carparts, items2, hockeyleague and meshsave.
Their original copies and both baselines are retained; the original guest world
is not claimed unchanged by preparation. No native save was requested; host
saved-part Data changes were verified in memory, not through disk save/reload.
Both games quit normally; the copied probe, marker and command bus are removed.
Final Core, Net, probe, catalog and compatibility hashes match current outputs.

Evidence: `summary.md`, `validation.json`, `native-save-comparison.json`, protected
hashes, `cleanup.json`, native asset extracts, final build/test logs and complete
role replies/logs in `build/puncture-drive-audit/`. `baseline.json` plus `before/`
preserve the inherited 946-file tree; `source-changes.json` and `scoped.patch`
isolate this task. Temporary drivers are archived in `drivers/` and assume their
original `tools/` location. Earlier runs preserve unassembled/prefab diagnostics,
the v231 puncture reproduction, a fixed puncture run whose direct-positioned
host entry trigger did not fire, and an intermediate 18-check pass before the
final initialization-guard review.

Limits: the disposable Corris was disassembled, with no loose tyres or fitted
seat. The probe instantiates real native tyre prefabs, supplies mount/seat
prerequisites, enters native mount setup, positions players nearby and supplies
native trigger/key events. A bounded velocity sets the car rolling under native
physics; this is not engine-powered travel, physical control/camera input, native
assembly, natural road-hazard puncture or tyre save/reload acceptance. Synthetic
fixture part identities produce warnings; other Unity errors remain (host/guest
index exceptions 4/2, error lines 18/20). Archived logs contain no wheel protection
or world-sync-disabled errors; this is not a clean-log claim.

Before wheel preparation, differing native host/guest worlds produced unstable
Corris motion. Matching disposable native files made the puncture test stable but
did not resolve that separate problem. Reassessment prioritizes reproducing it
without wheel fixtures and identifying conflicting body/part poses or physics.
Full assembly, durable ordinary tyre wear, rim/type replacement, long drives,
Steam/two-PC and four-player acceptance remain open; R2.2 remains partial.

### Corris join stability (2026-09-13, unreleased v232)

The unmodified `live-before` reproduction in `build/corris-join-audit/` uses the
host and guest's different disposable native saves with no tyre/seat fixture.
The guest starts near (1935.46, 4.45, -422.45); the host is parked near
(1940.42, 6.86, -386.12). Applying the host snapshot leaves the native root
world-connected FixedJoint anchored at its creation frame. The solver pulls the
car back, native LOD respawn intervenes below the world, and the driver-head body
runs away. The guest root eventually exceeds 11,000 m/s while the host stays
parked. Read-only traces begin before the probe's normal command-ready delay.

A diagnostic that removes only that root world joint prevents runaway, but lets
the unassembled car settle about 1.5 m from the host. The production fix instead
validates the catalogued native parking release and recreates that exact lock at
the accepted position/rotation. It preserves force/torque, anchors, axis, collision
and preprocessing settings, plus native FSM object-variable references. Connected
part joints are not replaced. Live kinematic streams do not rebuild joints each
frame; final pose, quiet-stream expiry, direct local claim and session release
rebase before restoring dynamic physics. Invalid or ambiguous native bindings
leave the old joint/pose intact and issue a rate-limited diagnostic.

**15 final local two-game assertions and 18 native checks pass** on one payload
in `live-final`:

- Original differing saved worlds join and remain parked together for at least
  thirty seconds, with repeated full resync and connected parts staying nearby.
- Changed/disabled native release actions and ambiguous root joints refuse the
  move. A translated/rotated parked pose preserves native settings, the joint
  variable and twelve connected part joints.
- Three streamed motion steps retain the same guest parking joint. Reliable final
  and timeout each replace it before physics resumes; both car poses stay aligned.
- Direct component claim, disconnect during a live stream and rejoin retain stable
  physics. The direct claim bypasses normal proximity/seat admission in the probe;
  it validates the release path, not native driver admission or powered driving.

The final early traces contain 1,800 host / 1,773 guest samples: maximum root
speeds are below 0.001 / 0.12 m/s respectively. `live-fixed` separately records a
stable minute before the final release-order review. The final driver initially
checked disconnect in the same frame as Shutdown, before WorldSyncManager's next
Update performed cleanup. The delayed reply confirms the expected replacement;
`acceptance-resume.log` finishes the remaining checks on the same running payload.
The archived corrected driver waits for the released state. No production change
was needed for that test timing correction.

Release Net (both targets), Core and probe build with zero warnings/errors against
the installed game libraries and `DeployToGame=false`. **4,600 Net and 18 launcher
tests pass.** Eight new Net cases cover parking catalog selection, missing fields
and invalid action indices. The first Net test process aborted after 1,853 passing
tests without a reported failing test; the complete retry after game shutdown
passed all 4,600. The crash cause is unestablished. Protocol/compatibility remain
232, next ID 219 and mod 0.1.33. No personal deployment, commit or release occurred.

All 18 protected personal save/install files and both disposable profiles' twelve
native text saves are unchanged. This task restored the original differing guest
files into its own copied profile before the baseline; it does not use the prior
puncture audit's matching-world workaround. No native save was requested. Both
final games quit normally with launcher status 0. No game processes remain; the
copied probe DLL, marker and command bus are removed. Final payload hashes match
the current Core, Net, probe, catalog and compatibility outputs.

Evidence in `build/corris-join-audit/`: `summary.md`, `validation.json`,
`native-save-comparison.json`, protected hashes, `cleanup.json`, native extracts,
build/test logs and complete role replies/logs. `baseline.json` plus `before/`
preserve the inherited 950-file tree; `source-changes.json` and `scoped.patch`
isolate this task. Temporary drivers are archived in `drivers/` and assume their
original `tools/` location. The diagnostic joint-removal patch is archived only
in `diagnostic-probe.cs`; it is absent from the final probe. The first diagnostic
startup stalled and was stopped by the runner's task-scoped termination; it is
not a normal-quit or gameplay pass. The retry and final runs quit normally.

Limits: copied games over local UDP; native unassembled cars and prepared small
host pose messages for release paths. Full assembly, engine-powered travel,
physical inputs, joint behavior after arbitrary game-build changes, native
save/reload, Steam/two-PC and four-player acceptance remain open. Final logs have
one intentional host parking-validation warning and none on the guest. Other
Unity errors remain (28 host / 44 guest error lines; 45 / 54 native index messages)
and are not attributed by this task. No clean-log claim is made. The bounded
joining instability is closed; next is guest cooking on the home stove with one
shared cooked result surviving guest rejoin.


## Guest home-stove cooking (2026-09-13, unreleased v233)

Both home stoves now accept authenticated nearby guest knob turns while the host
is elsewhere. The host runs native knob arithmetic and cooking simulation; guests
receive full-precision heat, knob rotations, cooking/burning triggers, indicator
light and smoke emission. The previous byte heat representation capped at 100,
below the native cooking threshold of 150. Bindings now wait for native FSM startup
before retaining their live variables. Native ignition remains enabled in YARD and
disabled in HOMENEW, matching the installed game's action settings.

Guest simulation pauses during the session. Disconnect restores original knob
actions, heat, fuse, triggers, light, smoke and fire-hazard values. Waiting, bound
and failed stove bindings reject legacy guest ignition reports. Message 219 carries
only plate/direction/sequence; the host validates identity, living/fresh nearby
position and ordering before applying a turn. State 99 carries absolute revisioned
results and supplies joining/resync. Existing meat state 210 carries the cooked
item identity and condition; no food factory was added here.

Protocol and compatibility are **233**, next message ID **220**, mod **0.1.33**,
unreleased. Both peers need the matching binaries and catalog. Release Core/Net35
and the guarded native probe build with **zero warnings/errors** and deployment
disabled. **4,625 Net tests and 18 launcher tests pass.** The SDK's missing .NET 8
reference packs were restored without retargeting projects; the earlier failed
test invocation is retained, not counted as a pass.

The native acceptance record is under `build/stove-cooking-audit/`. The first
rendered run (`live-acceptance`) passed 16 assertions, including both homes' eight
knobs, signed wraps, the cooking threshold and one shared grilled meat item after
its normal 243.86-second timer. Its later reconnect snapshot contains that same
cooked ID once, but the old probe threw while taking a snapshot during connecting;
that probe issue is fixed. The final payload adds smoke/hazard cleanup and the
legacy-report guard. Final-run evidence and limits are recorded in `summary.md`
and `validation.json` in the same audit directory. Ordinary rendered retries
stalled before Unity's first update; headless progressed differently but did not
complete gameplay. Wine virtual desktops reached both worlds. Fixed-duration
heating and smoke assumptions in the driver were corrected using the native
values; no gameplay implementation was changed to satisfy them.

The final virtual-desktop run passed **29 two-game assertions**, including **32
embedded native binding/refusal checks**, with the same identified payload across
driver continuations. Both games closed normally (launcher and driver exit0). Its meat reached native `Grilled` at 223.5548 seconds against a
223.5514-second timer, and both peers reported Type2 for the same item. Smoke
emission on/off reached the guest; disconnect restored native knob values and
resumed simulation; rejoin reconstructed that cooked identity once. Hazard/smoke
restoration is checked in source, with before/after native observations retained;
exact captured equality after several resumed frames is not claimed because
native actions immediately subtract/clamp hazards and clear smoke.

This is a controlled native two-game test, using separate disposable profiles with
different native saves. The probe enters real knob states, positions players,
invokes the native moose-meat factory, seeds plate heat at 149/200 and places/freezes
one claimed food body on the cooking trigger. It does not force the cooked state
or shorten the native food timer. Smoke uses the native emission action; the game
clears that emission during its normal cycle. These checks do not establish
physical mouse/hand placement, cold-stove warm-up duration, sausage-package
conversion, every food, complete house fires, native meat save/reload, Steam/two-PC
play or a long session. Those remain separate implementation or acceptance gaps.

Final hashes match the tested binaries/catalog. All **18 protected personal
files** and each disposable profile's **12 native text saves** are unchanged. The
test probe and sandbox marker were removed, the command bus/drivers archived, and
no test game processes remain. No personal deployment, commit or release occurred.

After this bounded task, the next implementation is guest ATF refill of the Corris
gearbox: cap operation, bottle depletion, destination oil/gauge and rejoin. ATF
already uses the supported ordinary-item factory; oil/coolant creation has separate
gaps. Then rotate to a guest earning loop using the full scope audit's work order.

## Guest Corris ATF refill (2026-09-13, unreleased v235)

Guests can open the installed Corris automatic gearbox's filler cap and pour from
an owned ATF bottle while the host is elsewhere. Native cap turns use the existing
33-unit step and 1–359 bounds. The host alone deducts finite source fluid and adds
the same amount to the mounted gearbox and its actual saved part. Each tick is
bounded by source, remaining capacity 6.3, native rate 0.1/second and capped elapsed
time. Guests supply intentions, never amounts. Fresh player/item poses, ownership,
mount identity, native tilt, exact capsule/sphere contact and a short pour lease
are rechecked continuously. Rejected requests consume their ordering position;
replays cannot become valid after approaching the car.

Dedicated ATF state preserves native `atfoil0` identities, bottle remainder and
empty presentation. A native empty bottle remains pickable with its small residual
quantity; it is not despawned or refilled. Both replica FSMs are disabled before
cloning, and the guest's own native bottles are hidden and restored. Native source
LOD simulation and competing filler writers are paused, allowing host-away use
without independent guest depletion. Host scalar updates reach the bottle's real
save field and mounted part immediately. Join, item/vehicle repair and targeted
snapshots reconstruct the same results. Local hand detection includes ATF's native
pickup joint, retaining ownership while a held bottle is still.

Two runtime findings required fixes. First, Corris's engine has its own hinged
body: matching car roots still left the filler about 0.099 m apart in the initial
controlled test. Message 221 now carries the entire transient cap subtree's pose
relative to Corris. Guests align its mesh, interaction collider and filler sphere
after car smoothing, without changing engine physics or saved assembly. Host
validation uses the bottle's latest accepted owner pose for tilt and geometry,
so visual smoothing cannot extend a withdrawn pour. Second, the native cap hides
the gauge recursively. Showing only its root left the bar invisible; the adapter
now shows the existing ATF subtree and restores every original visibility flag
on guest cleanup, together with the cap's original local pose/actions/scalars.

Protocol 234 introduced bottle state 220, filler state 221 and intent 222. Protocol 235
appends the cap pose to 221. Both peers require **235**; next message ID is **223**,
mod remains **0.1.33**, and this work is unreleased. Native metadata lives in
`catalog/sync-catalog.json::atfRefill`; source routing is in CODEMAP.md and exact
wire bounds/fields are in PROTOCOL.md.

Validation is retained locally under `build/atf-refill-audit/`:

- **4,755 Net tests pass**, including 130 focused ATF tests; **18 launcher tests
  pass**. Release Core/probe and both Net targets build with zero warnings/errors
  and deployment disabled. The reports and payload hashes identify the tested
  builds; this machine has the actual game libraries for Core compilation.
- **33 final two-game assertions pass** in `live-accept-v235`: differing native
  worlds, displaced host filler projection, distant-host guest cap/refill,
  conserved transfer, local gauge, source withdrawal/upright/cap closure,
  held ownership beyond the still timeout, native empty presentation and capacity
  stop. Host pouring, duplicate/foreign/distant replay refusal, exact gauge/cap
  cleanup, rejoin, fresh post-rejoin input, resync and native save/quit also pass.
  Embedded native guards check changed bindings and latest accepted source poses
  independently of misleading displayed bottle poses.
- **Six full-restart assertions pass** in `live-reload-v235` on the same payload.
  Native gearbox oil returns as 5.047114, partial `atfoil01` as 0.1318972 and empty
  `atfoil02` as 0.01893792. Host/guest identities, amounts and empty flags agree;
  the real `VIN1361DT2`, `atfoil01Fluid` and `atfoil02Fluid` ES2 tags agree with
  the results. Cap openness has no native save action: live rejoin restores it,
  while a full native load closes it as vanilla does.
- Both final stages close both games normally. All 18 protected personal files
  and the guest's 12 native text saves plus sandbox marker are unchanged. Only
  the disposable host's `savefile.txt`, `carparts.txt` and `items2.txt` change.
  No task game remains; the copied-install probe, command bus and test marker
  are removed. Six one-off drivers are archived under the ignored audit directory
  and removed from `tools/`. No commit, release or personal deployment occurred.

The test uses two native game processes through local transport and Wine virtual
desktops, a copied install and marked disposable profiles. The host already had
native automatic gearbox `VIN1361`; **no gearbox factory or assembly-prerequisite
bypass was used**. An earlier native shop/bag run bought two ATF bottles and chips,
then saved a partial bottle; final acceptance starts from those actual saved
bottles. Native hand pickup/cap states are entered by the probe. Needs are seeded,
both car roots are parked, and a held bottle follows the visible cap at a controlled
30-degree angle. Capacity/host-pour cases explicitly set destination oil first.

The inherited car is incomplete and inverted, with a 1 kg loose engine body. Initial
runs retained intermittent contact failures as that engine bounced against the
bottle. Final finite-transfer checks freeze and offset **only the disposable
host engine** by 0.12 m; the guest engine remains native, and its projected filler
must still match. This is an explicit fixture, not a production engine freeze or
proof of normal assembled-car handling. Earlier geometry/gauge failures and their
snapshots remain in the audit. Logs also retain recurring native startup, mesh-save,
audio and general checksum warnings; the acceptance result is not an error-free
runtime claim. The log review records the limitations of attribution.

Physical mouse/hand aiming, Steam/two-PC latency, moving or guest-driven Corris,
fully assembled engine behavior and other maintenance fluids remain untested or
incomplete. Cap projection can visibly offset against a guest's independently
simulated engine mesh. I12/V10 remain Partial: this closes the named ATF slice,
not all oil/coolant/brake-fluid/water/charcoal transfers or full vehicle servicing.

Reassessment selects **guest flea-market selling** next: native paid rental,
listing/pricing one supported shared loose item, unsold retrieval or host sale,
exact sold-item retirement, and once-only proceeds collection with rejoin/native
persistence. Existing rent/proceeds broadcasts leave listing inputs and identities
local. This closes a missing earning loop and rotates away from maintenance;
sewage and taxi have larger unresolved physical dependencies. Audit the actual
native listing and payment graphs before implementing that bounded journey.

## Flea rental and proceeds (2026-09-13, unreleased v236)

This is the financial dependency of the active J04 selling journey, on local mod
0.1.33 / protocol **236**, installed game build **23268598**. It does not establish
shared item placement, pricing, actual native item sales or exact sold-item
retirement. Those remain the next implementation step.

The previous guest rent hook forwarded BuyTableRent/Add to SaleTable/RENT before
checkout, granting unpaid days. MoneyFlea's generic replay could reuse a cached
cash amount. v236 uses revisioned native table quotes and one cached transaction
receipt per player. Rental selection stays local; rent-only checkout charges the
host and adds the paid weeks. Collection consumes the currently available native
expired-rental proceeds once. Native day 0 remains rented; -1 is inactive. Guest
Logic/Sell/DayChanger pause, native finances/cart/envelope restore on cleanup, and
opening hours govern transactions even when host-distance LOD hides the shop.

Guest checkout requires 1–52 rental weeks and an otherwise empty native Bought
array; a zero merchandise total alone is insufficient. Host mixed merchandise
keeps its native sequence. Both configured checkout/envelope boundaries and the
old canonical paths are excluded from generic buy/control registration. Shared
listing work must not restore those duplicate transaction routes.

Validation uses `build/local2p/game` and separate marked copies of the preceding
ATF disposable profiles under `build/flea-sale-audit/profiles`. The native probe
requires the existing sandbox markers plus `WINTERMP_LOCAL2P_FLEA_TEST=1` and the
persistence/bag opt-ins. It drives real rental Add/Remove, checkout and envelope
states. Fixtures teleport players, open shop presentation, set host cash and stage
native expired-rental proceeds. **No item was listed or sold to create those test
proceeds.** The native save button performs the save; full process restarts load it.

All **33 final two-game assertions pass** (9 reload/host-away checks plus 24
collected-reload/checkout/rejoin checks). Both games exited normally with launcher
status 0. All 18 protected personal files and the guest's twelve native save files
plus marker remain unchanged. The copied-game probe, command bus and test marker
were removed. Six one-off drivers are archived under `build/flea-sale-audit/drivers`
(outside the tracked source); the guarded native probe remains reusable.

Final production payloads, shared by the final restart/regression runs:

- Core SHA256 `0e782143973b4da26653023adebc760c30f6d5ca2ee9d4292808bc4ed4f36c6e`.
- Net SHA256 `19c4a9851b8ace81c392422ed7d65931e1251b3ba8a4c508a44961d23def53a2`.
- Catalog SHA256 `b7417f4dd57164ad5d76e433156c31efb131402576cc53ae29e0f046abcb1343`.

The targeted ledger/wire/catalog suite contains **37 checks**, and the complete
Net suite passes **4,792 tests**. All **18 launcher tests** pass. Core and probe
Release builds against the installed native libraries are clean; shared Net runs
as both net35 in the game and netstandard2.0 in the tests. These builds do not deploy
to the personal game installation.

Evidence is local/ignored under `build/flea-sale-audit/`:

- `native-flea.json`, `native-inputs.json` and compact `native-*.txt` preserve fresh
  native table, price UI, checkout, envelope, collection and save metadata.
- `live-reload-v236/acceptance.json` covers **nine** final-payload assertions:
  paid rental cold reload/rejoin; zero-price merchandise refusal; rental and
  collection with host LOD unloaded; repair snapshot; native envelope input and
  guest cleanup. The saved rental was 14 days with 700 MK. A subsequent two-week
  checkout left 28 days / 400 MK; collecting a staged 125 MK left 525 MK / zero
  proceeds, which was saved for the next restart.
- `live-final-v236/acceptance.json` covers **24** collected-state cold-reload and
  checkout/rejoin regression. The full-restart state is zero proceeds / 525 MK;
  a fresh collection request cannot recreate the payout. Further checks exercise
  native host/guest checkout, repeated/altered/stale requests, distance refusal,
  denied-request replay, insufficient funds, envelope collection and cleanup.
- Per-command JSON, native command-bus replies, role logs, payload hashes and
  before/after disposable saves accompany each run. `protection-check.json`
  compares 18 protected personal files and the guest's twelve native save files
  plus sandbox marker. `review.patch` is scoped against this pass's captured
  pre-existing dirty tree, not against HEAD.

The initial `live-checkout-v236` failure exposed the generic cash-register handler
intercepting the dedicated request. Removing that registration fixed it; the
superseded `live-checkout-fix-v236` contains 20 passing intermediate checks.
A later probe snapshot hit an InvalidCastException while enumerating Unity objects;
the rental selection itself completed. The read helper now skips non-FSM objects,
and the initial read failure remains archived. A rejoin fixture also attempted
checkout before the returning-player spawn choice arrived. The host rejected it;
the fixture now waits for and resolves that choice before moving the player and
retrying. Failed evidence remains separate from final passing assertions.

Native list-index, object-reference and unrelated general sync messages remain in
the logs. The checks establish these named financial outcomes, not an error-free
session, natural opening/expiry timing, physical mouse/key interaction, actual
merchandise buying, item sales, Steam/two-PC behavior or full-mod acceptance.

The active next step remains J04 shared listing identity/pricing, native sale or
unsold retrieval, exact object retirement and listing save/rejoin. The payment
bugs were a required dependency within that journey; do not extend this pass into
more financial polish. Reassess and rotate to ordinary supply creation after the
complete earning journey closes. No release or personal deployment was requested.


## Shared flea listings and sales (2026-09-13, unreleased v237)

The bounded J04 journey now supports **native chips packets**: a guest can price
an existing shared packet, the host lists that exact object, native sales remove
it once, and the guest collects the resulting proceeds. Rental expiry releases
unsold packets. This extends v236's paid rental/collection dependency; it does
not claim all flea-market item families or arbitrary item creation are finished.

The fresh native Chips prefab audit verifies `Use.ID`, `Consumed`, native
GARBAGE and save/delete behavior. The factory's `chipsN` ID maps to an eight-character
`OWNNNNNN` suffix on the native listing key. Native Sell still finds the proper
PriceGuide row, but the shared sale callback resolves the actual item by its
saved identity instead of FindChild on an ambiguous display name. Existing native
listing collections save both identity and price. Runtime network IDs may change
across restart; no sidecar or network-ID-based durable record is introduced.

Guest listing requests require a fresh nearby player, a paid active rental,
a supported released item resting within the real table trigger, and a current
listing revision. State snapshots pin the exact item and reconcile late creation;
ordinary transform/despawn requests cannot steal a listed object. Receipts reject
conflicting, altered and stale inputs. Supported native SELL/SELLRAND uses the
stored price, runs the real GARBAGE path and publishes retirement and proceeds.

Cold-load testing found two native details: item factories can finish loading
well after the world/finance bindings are ready; and RESET can restore proxy
snapshots containing old saved listings. Readiness checks wait for the native
factory, and expiry explicitly clears supported shared keys and releases the
native loading pin. Float normalization initially advanced unchanged listing
revisions; retaining the accepted pose fixes that. A concurrent player's price
sheet closes when another player lists its target.

Validation uses the same isolated game copy and fresh disposable profile copies
under `build/flea-listing-audit/profiles`; personal game files are not deployed.
The probe drives real price conversion/ENT, native SELL, expiry RESET, envelope
collection and host SAVEGAME. Placement is staged with body movement; later
checks use guest movement/replication into the native trigger. Players are
teleported, shop presentation is opened, and native sale/expiry events are driven
for determinism. The earlier 4 MK sale also occurred through the normal native
sale timer. This is not physical keyboard/mouse, Steam or two-PC acceptance.

Final checks: **4,814 Net tests** (22 new listing cases), **18 launcher tests**,
and a clean Core/native-probe Release build. **21 final native assertions** pass:
17 in `live-expiry-fix-v237/acceptance.json` and four in
`live-collected-v237/acceptance.json`, on the same final payload. The former covers
two saved same-name listings, stale motion/despawn/snapshots, exact stored-price
sale, repeated-sale refusal, sold/collected rejoin, expiry pickup and collection;
the latter verifies cold persisted 706 MK / zero proceeds, native deletion of
chips1/chips3, survival of chips2 and final native pricing cleanup. All five game
runs closed normally with launcher status0. Eighteen protected personal files and
the guest's twelve native save files plus marker/Steam metadata remain unchanged.
Runtime probe, command bus and sandbox marker were removed.

Final payload SHA256:

- Core: `8239f6195d79a25ff6cf08f0a7da84cf3638abb7d1bd8c6b4c58a0f1fbb6a558`
- Net: `cd4353b4907c2ba1919c848fd78f67cbc5b457a33f9cd33bf932d27bf7dbb6a9`
- Catalog: `e90c8c50b00ccec8b05809998c8c39fc85d286ff236f84198955656b3fde44a0`

Evidence is local/ignored under `build/flea-listing-audit/`. `native-items.json`
and `native-chips.json` supplement the previous fresh flea-table/pricing audit.
The native level2 hash remains
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`.
Per-command JSON, command-bus replies, native logs, payload hashes and disposable
saves accompany each run. `review.patch` compares this task against its captured
pre-existing dirty tree, not HEAD. One-off drivers are archived in `drivers`.

`live-initial-v237` and `live-reload-v237` preserve the first guest listing,
listed-stage rejoin, cold reload with a changed network ID, native timer sale,
two simultaneous same-name listings and duplicate/conflicting request checks.
Those use intermediate payloads. `live-final-v237` preserves the expiry bug and
the first ten passing sale assertions; it is superseded for final acceptance.
The final acceptance run also initially inspected cleanup in the same frame as
disconnect, and later expected a remote loose item to be non-kinematic while the
host was still streaming its physics. The corrected fixture waits for cleanup
and settled ownership; neither transient state is counted as a production failure.

Remaining limits: other native-accepted item families, adoption of legacy random
listing IDs, manual withdrawal while a rental is still active, broad ordinary
supply creation, physical input and Steam/two-PC testing. Native/general sync
index, reference, audio and other pre-existing log errors remain archived; this
is not an error-free-game claim. No commit, release or personal installation was
performed. Reassess toward one fuse-box purchase/opening journey next (I04/I05),
including the resulting shared fuses and remaining box contents.


### Shared fuse boxes and loose fuses (2026-09-14, unreleased v238)

This closes one ordinary supply journey, not the entire household electrical
system. Mod version remains 0.1.33; protocol and local compatibility metadata are
238. No personal deployment, commit, push or release was performed.

**Native evidence.** Fresh installed build23268598 extraction is in
`build/fuse-box-audit/native-fuse-scene.json` (75 FSMs),
`native-fuse-prefabs.json` (2 FSMs) and `native-spawner.json` (325 FSMs).
Scene level2 SHA256 is
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`;
sharedassets3.assets is
`4212b819e589e084c9fb0b26a6790e3760a27433d56c696e231aaf607b976b43`.
The latter hashes identify installed source assets, not runtime state.

- PERAPORTTI daily FusePackage costs 14.95 MK and uses the existing shop/bag path.
  Spawner/CreateItems::FusePackage has the same nine-action new / three-action
  saved package factory shape already supported for boxes. Its native ID prefix
  is fusepackage0, capacity is 5 and the item name is fuse package(Clone).
- Use/Create Fuse decrements Quantity once, sets Fuse/SpawnPoint to Owner, then
  sends SPAWNITEM. Its Load puts LoadTransform before IntClamp; the new profile
  retains that exact order rather than assuming the spark-plug order.
- Spawner/CreateItems::Fuse increments ObjectNumberInt, creates one Prefab at
  SpawnPoint, builds the persistent name and assigns it. Native saved Create
  recreates an existing identity without increasing the counter.
- fuse0::Use captures its persistent ID before renaming to fuse(Clone).
  GARBAGE sets Consumed/Destroy and removes the physical body while retaining
  the FSM that deletes the native transform key on SAVEGAME. Destroying the
  host object outright would bypass that native save cleanup.

**Implementation.** PackageState and the existing guest opening ledger now
support the catalog's ordinary supply output profile. A host opening is accepted
only after its exact next native output initializes with one decremented count.
SupplyItemState227 publishes persistent loose fuse identity and creation pose;
normal item ownership/transform streams carry movement. Guest factories are
suppressed, originals are hidden/restored, and replicas bypass guest native
load/save. Retirement blocks delayed materialization and replay. Join, soft repair
and object-state responses all include supplies. Generic saved-product manifests
exclude these dedicated outputs. New/live supplies publish once; immutable
identity messages are not rebuilt every frame after publication.

**Native two-player evidence.** Four disposable local UDP runs used the copied
`build/local2p/game` and `build/fuse-box-audit/profiles`, derived from the earlier
flea fixture. Each role has its own marked save directory. All four runs exited
with launcher status 0. The per-run acceptance files, command replies, native
logs, copied post-run saves and payload hashes are under:

- `build/fuse-box-audit/live-v238`: nine journey assertions. Guest purchase and
  bag unpacking made one five-fuse box; guest opening made fuse01 and count 4;
  host opening made fuse02 and count 3. Native disposal of fuse01 survived a
  delayed creation replay. Rejoining hid a separately created guest-local
  fuse01. A third opening plus duplicate and stale requests left only fuse02,
  fuse03 and count 2. Disconnect restored the guest's original fuse.
- `build/fuse-box-audit/cold-v238`: nine continuation assertions. Native restart
  retained box ID1865941722, fuse02 ID986654061 and fuse03 ID348957444, with
  count 2 and no retired fuse01. The guest picked up fuse02 through the native
  hand joint. Guest then host opening produced fuse04/fuse05 and count 0;
  attempted empty opens did not increment the factory past five. A native
  spark-plug box still opened to quantity 3 without disabling/stalling its
  existing part-output flow. Host saved from the native save button.
- `build/fuse-box-audit/empty-cold-v238`: four assertions. Empty box and fuse01
  remained deleted, all four surviving loose fuses appeared on both peers,
  wallet remained 691.05 MK and disconnect removed the guest replicas.
- `build/fuse-box-audit/final-cold-v238`: repeats those four assertions on the
  final compiled publication cleanup. Final Core/Net hashes match the build.

There are **22 distinct native assertions**, plus the final four-assertion repeat.
The first fixture attempt entered Buy/Add directly and bypassed the native guest
selection guard; it was corrected to Check inventory before the evidenced paid
purchase. During cold verification a result reader initially used the wrong
active-body column, so its read-only observation timed out; the corrected reader
produced the acceptance results above. Neither was a production fix or evidence
of a new player bug. Native Unity index/reference/audio warnings still occur in
these fixtures; this is not an error-free full-game claim.

The build of Core and GuestSaveProbe succeeds with zero warnings/errors against
the installed game. All **4,839 Net tests** pass (25 new fuse cases), as do all
**18 launcher tests**. Older package tests now distinguish 31 car-part boxes from
the added ordinary supply profile and allow distinct FSMs on one contents object.
`validation-summary.json` verifies all **18 protected personal files** and all
**14 guest native save/marker/cloud files** unchanged across the runs. Test probe,
command bus and game marker were removed. Reusable probe code remains developer
only; one-off drivers are archived under `build/fuse-box-audit/drivers`.

Final tested SHA256 values:

- Core: `61460c3dcc02fb215883cb78a5cc4af9b7e7731f6395eac18701ee3bbfe12267`
- Net: `fab91ebdb3490007ec72ee3ad09d2b7bf2d22b4f8da3f8c2946ecb04b63e4ab0`
- Catalog: `cf49d94c682b82ba4e424cd31db384c279c1cfd6d1d763019323981d70c60813`
- Probe: `701544d3bac86c963321cc79a3067ae23ad8e05fdfc7c7776249598ceaa24912`

**Limits and rotation.** This is controlled native FSM/body testing in two game
processes, not physical mouse/key or Steam/two-PC acceptance. Fuse holders,
installation/removal/tightening, blowout and electrical consequences remain H04.
Light-bulb and R20 battery contents remain open within I05, which moves from
Missing to Partial. I04's other separately sold supplies are not closed by this
bag-based fuse purchase. Next is a bounded Corris ignition-to-fuse-box wiring
installation/removal journey, with actual shared result and native persistence.

### Shared Corris ignition wire (2026-09-14, unreleased v239)

This closes source 5 WiringIgnitionFusebox, one of 35 native wiring connections.
Mod version remains 0.1.33; protocol/local compatibility is 239. No personal install,
commit, push or release was performed.

**Native evidence.** `build/corris-wiring-audit/native-wiring.json` contains 115 FSMs,
199 transforms and five ArrayLists from installed build 23268598 level2. The asset
SHA256 is `36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`.
`native-steering.json` adds six steering FSMs/49 transforms; the supplementary
`native-steering-part.json` contains two fire FSMs, not a saved loose column.
These extracts are asset definitions, not runtime/save values.

- Data loads/saves Installed under UniqueTag WiringIgnitionFusebox in carparts.txt.
  Basic state presents WireMesh and the inverted Installed trigger gate.
- Fusebox and Ignition Assemble endpoints use the native 0.1 m tool-distance check.
  Each selection enters Sound; the second sends CLOSELOOP to the first. Finish
  assembly sets Installed, activates the wire, broadcasts RESETWIRING and disables
  their parent. The mod guards the guest before those native effects.
- Status/Steering gates Ignition from VINP_SteeringColumn/Data Installed. The
  audited mount removal graph does not send this wire DESTROY. The identified
  destruction source is FireElectric/Init/Fire. The wire mesh has no collider or
  manual-removal FSM; no new removal control was introduced.
- Use registers WiringTool globally. Pickup reparents the same object; binding
  verifies its Save.UniqueTag instead of demanding its original EQUIPMENTS path.

**Implementation.** Connection descriptors validate native references/actions.
The host validates the request revision, available uninstalled wire, native column
prerequisite, recent live player within 3 m and tracked tool within 0.6 m, with no
conflicting holder. One immutable per-player operation receives Pending/terminal
receipts; duplicate success never executes again after later destruction. Host
Finish assembly performs the native write. WiringState193 retains its 9-byte
payload and adds source 5 Connectable flag 8; request 228/receipt 229 are channel 0.
The protocol spec documents payloads, ordering, authority and token/sequence rules.

Guests pause the saved wire Data and replace only the selected Status activation
input with a temporary host boolean. Their cable/endpoint objects follow host
state; their saved Installed value is untouched. Disconnect removes hooks, cancels
partial selections, restores original actions/visuals and resumes the original
Data. A failed validated binding pauses Data/endpoints and hides unrelated local
wire geometry, with recovery on a new successful join. Existing snapshot/keepalive
and engine-source plumbing carry the state without adding guest native save writes.

**Native two-player evidence.** Disposable UDP runs use `build/local2p/game` and
`build/corris-wiring-audit/profiles`, copied from the closed fuse fixture. All test
profiles carry the persistence marker; the probe and command bus do not ship.

- `verified-live-v239`: 10 assertions — conflicting guest original, published input,
  real native hand claim and both endpoint selections, one host install/shared
  cable, protected guest data, host destruction, accepted/stale replay rejection,
  disconnect restoration, and host native reconnection. The host completed native
  SAVEGAME/MainMenu; `native-save-complete.json` records that boundary.
- `cold-v239`: 4 assertions — native saved installation, new guest cable/input and
  native destruction convergence; a second native save persists destruction.
- `destroyed-cold-v239`: 4 assertions — destruction stays absent after restart,
  joining guest reconstruction, missing-column endpoint gate, native guest restore.
- `final-cold-v239`: 8 assertions — repeats those four on the final build, then
  deliberately invalidates the disposable guest tool tag to verify Data protection,
  endpoint/cable containment, original restoration and a successful repaired join.

That is **22 distinct native assertions**; the repeated four are not counted twice.
All four completed runs exited normally. Their command/reply evidence, assertions,
logs, native save copies and per-run hashes remain under the named run directories.
`validation-summary.json` consolidates results. All 18 protected personal files and
all 15 copied guest-profile files are byte-identical to their before values (twelve
native save/settings .txt files, Steam metadata, the marker and the mod sidecar).

**Fixture corrections and practical limits.** Earlier live-v239/journey-v239/
accepted-v239/final-live-v239 attempts remain archived. Startup could precede the
probe's cached Player reference; pickup invalidated an assumed fixed tool path;
teleport fall/hand motion moved the tool between checks; and moving an unclaimed
tool 2 km correctly failed host ownership admission. The final fixture acquires the
tool beside its original host position, waits for ownership, then pins the held
player/tool and suspends the native movement motor while selecting endpoints.
A disposable Installed boolean supplies the column prerequisite; it does not
claim physical steering-column construction. The incomplete car's Electrics graph
waits on other inputs, so assertions verify its published wiring source, not a
fresh full engine calculation/start. A post-save probe used a GAME-only command
after MainMenu; the corrected observer uses snapshot. A one-off cold driver had
an indentation error before assertions; it was corrected and the checks rerun.

**Build/test results.** Core/probe Release builds against actual Unity/PlayMaker
DLLs have zero warnings/errors. All 4,864 Net tests (25 new wiring cases) and 18
launcher tests pass. net35 and netstandard2.0 are built by those checks. An early
compile caught an overbroad ledger rename and a reference-only reflection helper;
full tests caught default WiringState encoding, which was corrected before native
acceptance. Final payload SHA256 values:

| Payload | SHA256 |
|---|---|
| Core Release | `ebec71a6e0c4cdcf7bc8e0ed779341c64a6f8929e6691a459a7499623e01a713` |
| Net Release | `dffc666396220d3991da6110151bbf645e1602cd5a8ba06adeb514abc0640747` |
| Catalog | `66586bcfb993034cf5e1c6693b78073e1c1117e16c467f5243df8fec9763e137` |
| Probe | `c283f8980b489d334bfd95bb484e92b86bd8cad97295b2df6bcdb8c3dc24300f` |

Manual aiming/carry feel, ordinary column fitting, every other wire, battery
terminal installation, shock/fire presentation and an operational engine remain
open, along with Steam/two-PC acceptance. One-off drivers were archived under
`build/corris-wiring-audit/drivers`; the game marker/probe/bus were removed and test
processes closed. The retained probe concern is opt-in and marker-gated.


## Guest taxi pickup prerequisite (2026-09-14, unreleased, still protocol239)

**Outcome:** the host's native customer can recognize a guest driving MACHTWAGEN,
walk to the car, board and add passenger mass while the host stays elsewhere.
This closes the first concrete dependency found while tracing the selected taxi
fare. It does **not** close J06 or establish a complete shared fare. The complete
native-flow findings and remaining dependencies are in
[SYNC-SCOPE-AUDIT.md](SYNC-SCOPE-AUDIT.md#taxi-native-lifecycle-audit-and-pickup-prerequisite-2026-09-14-still-v239).

`TaxiPickupBinding` locates the customer even while inactive or parented under the
car. It validates the native distance, look-at and vehicle-name bindings before
replacing four action inputs with private values. A hidden, unsaved context object
tracks only a connected, accepted taxi driver's seat. The existing driver-anchor
path requires an actual live driver stream for that car; the additional policy
rejects dead peers, missing/future/stale poses (over 2 seconds), non-finite values
and player positions more than 8 m from the seat. Walking beside or pushing the
car does not create driver authority. Without a qualifying guest, the private
inputs follow the original host camera/player/vehicle globals. Native thresholds,
walking, door animation, parenting, passenger mass and departure logic remain in
the game's FSM. No host player globals, saved values or wire fields are rewritten.
Driver changes are recorded as `taxi-pickup-driver` ring-buffer events.

A changed signature disables this adapter and retains native host behavior;
validation happens before input installation. Session cleanup restores original
input objects and removes the context object. The `taxiPickup` catalog block and
`TaxiPickupPolicy` supply configurable discovery and independently tested pose
bounds. Message103 retains its finalized customer Cost. The earlier live-meter
and immediate-wallet claims were documentation errors: native Tripmeter.Price
is the live meter, completed fares add to IncomeTotal, and later wages reach the
bank. Protocol239 / next free ID230 / mod0.1.33 remain unchanged and unreleased.

### Native evidence and boundaries

Fresh asset extraction from installed build23268598 (`level2` SHA256
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`)
is retained at `build/taxi-fare-audit/native-taxi.json`: 164 FSMs, 11 ArrayLists,
1 Hashtable and 752 transforms. `flow.txt` records decoded action flow.

Two runs use the copied `build/local2p/game` and copied, marked disposable
host/guest profiles. The fixture explicitly sets employment stage2 and executes
native Check stage **on both peers**, because activation is still unsynced.
It places the host's customer near the taxi and selects an existing distant native
destination. Guest and host enter through the native driver trigger events; the
customer then follows its native distance/vehicle/walk/boarding states without
forcing a boarding state. The host stays over 3 km away during guest pickup.
Needs are held low in these disposable profiles. This proves the controlled native
pickup dependency, not ordinary call generation, manually driven arrival or aiming.

`final-v239/checks.json` has **9 passing native assertions**:

| Check | Final reply |
|---|---|
| Guest native input references and no context object | guest-5 |
| Changed taxi signature contained, original distance inputs intact, no leaked context | host-4 |
| Repaired signature binds again | host-6 |
| Adapter disabled: accepted guest driver cannot advance host pickup | host-10 |
| Adapter enabled: guest drives; native host customer boards and adds mass | host-18 |
| Host camera and current-vehicle globals unchanged by guest pickup | host-18 |
| Driver exit withdraws pickup context; nearby unseated guest cannot board customer | host-20 |
| Host can take over and board customer natively | host-31 |
| Shutdown restores sampled distance/vehicle input references and removes context object | host-34 |

Replies are under `build/taxi-fare-audit/final-v239/live-bag/`. The earlier
`live-v239` run reproduces the same pickup/exit/handoff behavior and is not counted
again. Its initial guest snapshot attempted to print the camera before native
startup supplied it; the final probe handles that absence and both startup
snapshots pass. The deliberate signature failure is logged in the final run;
existing native startup/index warnings remain separate unresolved diagnostics.
Both game pairs exit with status0. This task does not exercise taxi earnings or
native save/reload; none of the production changes write taxi save values.

Core/probe Release builds against the installed game with **zero warnings/errors**.
**4,880 Net tests** (16 new pickup/catalog cases) and **18 launcher tests** pass.
All **18 protected personal files** and all **15 copied guest-profile files** are
byte-for-byte unchanged. `validation-summary.json`, `protected-before/after.json`,
`guest-before/after.json`, payload hashes and the task-only `review.patch` retain
the evidence. Final native payloads match current compiled outputs:

| Payload | SHA256 |
|---|---|
| Core Release | `781c4fbc28bbf39931ce5caa6f8d38a246eb53c971fcc6a7c9aae6035075001d` |
| Net Release | `acdbbe901e9250db78a31b25a6d23db9c8e5b40d4918f2faba5901c5b46b1a99` |
| Catalog | `42eca6a881796ade83b8916ee98f70253b98356a5812aaff47e0bd49ff7822b1` |
| Probe | `ce062e185d8ea977fa490136d2496d715225d1bcb9bd98dd382688d0a233d8e0` |

Test processes are closed; the copied-game probe, marker and command bus are
removed. One-off drivers are archived under `build/taxi-fare-audit/drivers`.
The retained probe commands require both `WINTERMP_LOCAL2P_TAXI_TEST=1` and the
existing marked persistence sandbox. No deployment, commit or release was made.

**Next required dependency:** host-owned taxi availability and call acceptance,
with shared pickup/destination/customer presentation. Meter controls, terminal
payment, luggage, receipt identity/handoff, payday authority, late joining,
completed earnings save/reload and Steam/two-PC play remain open. Taxi player
passenger seats are a separate open vehicle feature.

## Shared taxi calls and customer presentation (2026-09-14, unreleased v240)

**Bounded outcome:** host employment/availability now activates the guest taxi;
either player's incoming handset use can operate the host's native customer call.
Guests see the host's pickup/destination, customer visibility, walking pose,
boarding parent/passenger mass and native animations. Guest job/customer/ringing
choices pause instead of creating an independent fare. This does **not** close the
selected complete fare or J06.

`TaxiServiceBinding` uses the catalogued tripmeter Customer reference, so it finds
the same walker when inactive or already parented to the car. It validates native
job/phone links, Answer/Occupied outputs, Hangup/SUCCESS, audio and the three job
colliders. Dedicated messages 230/231 (protocol 240, next free 232) carry absolute
ordered presentation and authenticated current-call intents. A guest answer only
writes the host's native Ring.Answer/Occupied; caller duration and SUCCESS remain
native. It does not open the host keypad or move its hands/camera. Duplicate or
old answers cannot advance another fare; only the owner may hang up. Native host
handset input remains authoritative and clears the previous guest's call display.

The host samples at most 10 Hz with changes/2-second keepalive. The generic taxi NPC
mover is removed so pose cannot race a separate boarding/activation message. Guest
cleanup restores captured variables, actives/colliders, actions, parent/pose,
animations and FSM enabled/restart settings. A last-guest departure originally left
its handset occupied because the taxi updater skips network work with zero peers.
The new direct departure cleanup fixes that path; host driver context also returns
to its native inputs even with no peers remaining.

**Validation on native build 23268598:** Core/probe build succeeds with zero warnings
and errors; **4,899 Net tests** (19 new taxi service checks) and **18 launcher tests**
pass. Final isolated two-player artifacts are under
`build/taxi-service-audit/accepted-v240/`. Its `checks.json` records **18 distinct
controlled assertions**, all passing:

- Guest initially follows the inactive host job and pauses independent decisions;
  host employment then activates the guest taxi without a guest activation fixture.
- A distant guest cannot answer. A nearby guest native handset press starts the
  host caller while the host is elsewhere; a duplicate answer retains that call.
- Native caller completion creates matching visible customer, route and indicator;
  guest hangup does not advance that customer twice.
- An old call ID cannot answer a different call; early hangup cancels without
  accepting a fare.
- Host handset takeover clears the guest caller display; a guest cannot hang up a
  call now owned by the host.
- Disconnecting the last guest during a call releases the handset without accepting
  the fare. Rejoin restores the host job/route with guest decisions paused.
- A guest driver boards the shared customer while the host stays away; guest driver
  exit restores the native host pickup context.
- Guest teardown restores its original job stage, customer/phone decisions and GUI.
  Rejoining with the customer already boarded restores its seat, passenger mass,
  route and a native seated animation.
- Guest native save/exit reaches MainMenu, and all **15 copied guest-profile files**
  remain byte-identical. All **18 protected personal files** also remain unchanged.

The exact final Core/Net/catalog/probe hashes are in `accepted-v240/payload-hashes.json`;
`profile-integrity.json` holds both guest-file hash sets and personal comparison.
The earlier `native-v240` run exposed the last-guest departure bug; `final-v240`
passed 13 checks after that fix; `accepted-v240` repeats them and includes the final
host-takeover/cleanup/join-while-boarded checks. The failed initial `live-v240` run
was a disposable profile-copy setup error, not a gameplay result. Wine profiles
must be copied with symbolic links preserved (`shutil.copytree(..., symlinks=True)`
or equivalent); the failed copy was removed and recreated before testing.

Native source evidence remains `build/taxi-fare-audit/native-taxi.json` and its
`flow.txt`; this pass additionally extracted the native TaxiGUI SetText FSM into
`build/taxi-service-audit/native-taxi-gui.json`. Level2 SHA-256 remains
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`.

**Acceptance limits:** the probe uses marked disposable host/guest profiles and a
copied game. It enters the host's native Enable stuff and address-selection states
to skip recruitment and long call waits, teleports players near/far, and invokes
audited native handset/driver states. Boarding uses the existing prepared-customer
fixture near the taxi. The caller's native duration, completion, customer activation
and boarding transitions run normally. This is controlled state-driven evidence,
not physical mouse/keyboard or Steam/two-PC acceptance. Existing native warnings
(including disposed sound / GameObjectIsChildOf messages also present in the v239
run) remain in the logs; no taxi adapter, presentation or restore failures occurred
in the final acceptance run. This is not a clean full-game or performance playtest.

Guest duty/meter/terminal inputs, luggage identities/transport, receipt creation
and handoff, and completed wages/receipts/odometer save/reload remain open. Guest
luggage and fare-offer objects stay hidden until their shared paths exist. The
tutorial is host-operated; guest outbound keypad calls and human taxi passenger
seats are unsupported. Native customer save data does not include a mid-fare
passenger/route, so cold-load mid-fare recovery is not claimed.

**Reassessment/next:** this closes the selected availability/call/presentation
dependency, with cleanup and evidence recorded. Duty/meter controls and shared
arrival/terminal inputs are next because the fare still cannot be completed by a
guest. Then close luggage, receipt and completed earnings persistence; reassess
and rotate once those concrete fare requirements close. The broader 82-area audit
remains 22 Candidate / 51 Partial / 6 Missing / 3 Review. Mod 0.1.33 is unreleased; no
personal-game deployment, commit, push or release was made. Both test games are
closed and temporary probe/marker/command bus are removed; one-off drivers are
archived with the ignored evidence rather than left in tools.

## Shared taxi duty and meter (2026-09-14, unreleased v241)

**Bounded outcome:** guests can operate the normal duty/meter knob, start/pause a
fare with the roof-light button, and request the native long-press total reset.
The host owns the native fare calculation; both players share its values, LCD,
knob pose and lights. This closes the duty/meter dependency, **not J06 or the full
one-fare journey**. Mod version remains 0.1.33, unreleased; both peers need protocol
241 (messages 232/233, next free ID 234).

### Native findings retained for the remaining fare work

The meter is `JOBS/TAXIJOB/MACHTWAGEN/TaxiFunctions/Tripmeter::Function`.
Its child `KnobMode::Knob` uses RotationInt 0–210 in steps of 35. Native `Volume dec`
increases the mode, while `Volume inc` decreases it. Modes 0/35 are off/wait;
70/105 select tariffs 1/2 (native base costs 21/32); 140/175 display total distance
and income. Auxiliary mode 210 remains host-operated in this slice. Guests may
turn down from it, but cannot enter or operate its auxiliary button logic.

`ButtonMode::Use` flips TaxiLightOn and writes Meter.On. **On=true means the fare
is paused**, despite that variable's name. Base cost writes Price=BaseCost and
resets trip/interval counters. Native Time adds 2.483 after five seconds; Distance
adds 0.543 and 0.1 km after 100 metres. The native state switch uses speed thresholds
of 0.5 and 2 m/s. Both speed reads ordinarily come from the car odometer's Data.MpS,
which depends on the native Speedo gauge. The host can be far from a guest-driven
car, making its local gauge/odometer an unreliable input.

Only those two validated reads are adapted: accepted matching-owner VehicleState
SpeedTenthsKmh is converted to m/s while the taxi is delegated to a guest. The
existing two-second freshness rule applies; absent/stale telemetry contributes
zero distance, and fresh telemetry resumes input. Host-local driving retains the
native odometer read. The host still runs native Base cost, Time and Distance;
no guest fare estimate or custom fare calculator is introduced.

Native HARDRESET zeros OdoTrip, OdoTotal, IncomeTotal, IncomeReceipts and Price,
then sends RESET so the selected tariff can rebase the live fare. Native saved
TaxiIncome0/TaxiReceipts0/TaxiOdo0 totals remain host-owned. **The game does not save
mid-fare progress.** Terminal printing adds trip distance/receipt totals, customer
payment contributes completed income, and the later Payments FSM settles wages
into the bank; those complete-fare outputs are still unfinished.

### Authority, presentation and cleanup

Authenticated guest intents require an available taxi, idle controls, a fresh
living player within 3 m and the current control revision. Current-revision request
sequences are consumed even when rejected, preventing retries after walking into
range. Native host controls and accepted guest actions advance the control revision
independently of ordinary price updates. Admission advances it too: pre-rejoin
requests are rejected before sequence admission so they cannot mutate the meter
or poison the new guest sequence. Every processed request returns absolute state.

Guests retain the native input states but replace commit/output states with intents
and safe input return paths. They pause their meter, LCD reader and unfinished
payment terminal; otherwise the terminal could locally erase the shared fare.
Received state writes values and presentation directly without replaying controls.
Snapshots and a four-Hz ordered stream restore current state on rejoin. The native
knob's actual local quaternion is transmitted: the old SetRotation behavior can
produce a visible pose different from RotationInt, particularly after crossing
90 degrees. Reconstructing the guest knob from the scalar failed a native visual
check, so exact host pose is now authoritative.

Teardown restores original variables, actions/transitions, indicator actives,
knob pose, roof material, LCD text and FSM enabled/restart settings. Host teardown
restores the original odometer actions. Changed native bindings disable this slice
with a diagnostic; speed callback failures are contained instead of escaping into
the game. Other taxi and world systems retain their own boundaries.

### Validation and limits

On installed native build **23268598**, Core/probe builds pass with **zero warnings
and errors**; **4,926 Net tests** (27 new meter checks) and **18 launcher tests** pass.
`build/taxi-meter-audit/accepted-v241/checks.json` records **24 passing controlled
two-player assertions** on the final DLL/catalog/compat/probe payload:

- Host/guest binding, paused guest calculations, shared availability and guest duty
  selection while the host is elsewhere.
- Shared day tariff, native waiting charge and roof-light pause.
- Guest-owned native gauge input driving host distance charges, expired telemetry
  stopping new distance and fresh telemetry resuming it.
- Guest native reset rebasing the fare and clearing the shared trip.
- Distant requests rejected; rejected sequence still consumed after approach;
  alternate tariff accepted; duplicate request cannot advance the knob twice.
- Matching odometer/income LCDs and stable actual knob poses in the upper modes.
- Native host input defeating an old guest control revision.
- Guest disconnect restoring native actions/calculation/display/terminal; rejoin
  restoring fare and presentation; pre-rejoin traffic rejected without blocking
  the guest's fresh sequence.
- Host cleanup restoring native odometer reads and a clean subsequent binding.
- Native guest save returning to MainMenu with shared world writes guarded.

`profile-integrity.json` verifies **all 18 protected personal files** and **all 15
copied guest-profile files** are unchanged, with no additional guest save files.
The accepted payload hashes match the current build. Both games closed normally
(exit 0); the copied-game probe, marker and command bus were removed. Test profiles
were copied with symlinks preserved. No personal installation was deployed to and
no commit, push or release was made.

These checks use native state entry and disposable employment/player-position
fixtures. The distance case supplies a known guest native gauge value and then
withholds its normal vehicle stream; it does not hand-set host fare or distance.
This is **not** a human mouse/keyboard playtest, Steam/two-PC acceptance, a complete
road fare or completed-earnings save/reload. Earlier runs and their failures remain
separate in `native-v241`, `live-v241` and `visual-v241`; only `accepted-v241` is the
final payload acceptance. `validation-summary.json`, `review.patch`,
`changed-files.json` and archived drivers retain this checkpoint's evidence.

**Reassessment and next task:** shared arrival/terminal/payment is the next concrete
blocker in the already selected fare, followed by luggage identity/transport,
receipt handoff and saved earnings. The broad scope inventory remains 82 areas:
22 Candidate / 51 Partial / 6 Missing / 3 Review. Finish those required dependencies
and reassess across other missing ordinary gameplay loops; do not turn this into
optional meter polish or another unrelated profiling cycle.


## Shared taxi arrival, quote and cash (2026-09-14, unreleased v242)

This closes the bounded arrival/terminal/cash dependency of the selected taxi fare.
Guests can quote the host's paused meter at arrival, see the same cash offer and
collect it once. Native host save and cold reload retain collected taxi income on
both peers. **J06 and the complete fare remain open** for receipt printing/identity/
handoff, luggage and full payday. Mod version remains 0.1.33; protocol is **242**,
with ordered messages **234 TaxiFareState / 235 TaxiFareIntent** and next free 236.
No release, commit or push was made.

### Native behavior and authority

Ground truth is installed build **23268598**, retained in
`build/taxi-fare-audit/native-taxi.json` and `flow.txt`. Customer Out → State 9
signals arrival. With the meter paused, `PaymentTerminal/Payment::Use` Make payment
captures Tripmeter.Price into Cost, subtracts 4,500 from JOBS/TAXIJOB.Timer, displays
the rounded quote and sends CASHIER after two seconds. **Timer counts up as
non-work time**; this adjustment credits work time. It can become negative and
native SAVE clamps it to zero. It is not a countdown or a direct earnings amount.

Customer Pay reads that Cost and shows PayMoney; Paid → Add money credits the exact
float to Tripmeter.IncomeTotal. The tested quote was **28.449 MK**, displayed as
**28.4** / **TAKE MONEY 28.4 MK**, with exactly 28.449 credited and cold-loaded.
This is accumulated taxi income: native Payments settles wages into the bank later.
The guest collection applies only the audited Paid/hide outputs and lets the host
customer credit income. It deliberately avoids replaying the native hand's local
Steam achievement action on the host. Guest collection sound/achievement fidelity
is not included in this checkpoint.

`taxiFare` identifies native object references and states. Binding validates the
quote source, timer adjustment, delayed event, Paid output and IncomeTotal credit.
A fare ID advances on actual boarding. Quote/collection requires the current fare
and control revision, an authenticated fresh living player within 3 m, and the
correct native phase. Current-revision sequences are consumed even on rejection;
old fare/revision requests are rejected first. Admission advances the control
revision and resets that player's ordering, preventing old traffic from blocking
fresh input. Host controls advance the same revision. Requests return absolute
state on success and rejection.

Guest terminal/cash input states remain native; commit states send intents and
return safely to input. Other terminal outputs, receipt printing/reset and guest
income calculation remain suppressed. Service binds before fare to capture original
guest visibility, and fare restores before meter/service. The meter's existing
terminal guard grants input only to a validated fare adapter. Cash amounts, native
labels, LCD text and visibility are shared through join snapshots and a change-only
10 Hz stream with two-second keepalives. No new mid-fare persistence is introduced.

### Validation, isolation and limitations

Core and the probe build against installed game DLLs with **zero warnings/errors**.
**4,944 Net tests** (18 new fare cases) and **18 launcher tests** pass. Protocol
roundtrip, malformed data, action phases, authority, sequencing and catalog parsing
are covered. `git diff --check` passes.

The final identical DLL/catalog/compat/probe payload passed **17 controlled native
two-player assertions**, retained in `build/taxi-payment-audit/accepted-v242` (15)
and `cold-v242` (2):

- Both fare bindings initialize; independent guest payment remains suppressed.
- Guest pickup creates one shared fare; a pre-arrival charge is rejected.
- Accepted guest movement triggers native arrival with shared destination text.
- Guest quote captures host Price and adjusts Timer once; duplicate quote cannot
  apply the adjustment again. Both see the same delayed cash amount/label/LCD.
- Disconnect restores original terminal/cash actions. Rejoin restores unpaid cash
  without crediting income; old-connection and distant collection are rejected.
- Guest collection credits native IncomeTotal once, hides cash on both peers and
  rejects duplicate collection.
- Native guest Save returns to the menu with world writes guarded. Native host
  Save retains collected earnings; cold reload restores exactly that amount to
  both peers without a pending cash offer or second credit.

`profile-integrity.json` confirms **all 18 protected personal files** and **all 15
copied guest save files** are unchanged, with no extra guest save files. Both final
runs closed normally (exit 0); no games remain running, and the disposable-game
probe, sandbox marker and command bus were removed. Personal installation and saves
were never deployed to. Test profile copies preserve Wine symlinks.

These checks enter native input states through a gated developer probe. Disposable
fixtures activate employment, place a customer for boarding, park players at a
recorded safe position and move the host's destination marker nearby. An eight-metre
guest car move then crosses the native arrival threshold. They do not synthesize
host arrival, quote, Paid or income. The fare pause, quote and collection use guest
intents and native host outcomes. This is **not** a full road journey, human
mouse/keyboard input, role-reversed cash acceptance, Steam/two-PC test or complete
receipt/luggage/payday persistence. Mid-fare progress is not saved by vanilla.

Earlier fixture runs are retained separately and are not final acceptance. A
four-kilometre test teleport separated the native driver from the taxi, then caused
a guest Accident (cause 13) and the configured native group permadeath on the host.
Only the disposable host save was erased; it was restored from the pristine meter
profile. The initial diagnosis from an underwater position was drowning, corrected
by the death logs. Startup attempts without the erased save could not enter GAME.
The final fixture uses a safe anchor and a short move updating the native body
transform and driver position together. `fixture-notes.txt` records these failures.

`validation-summary.json`, `changed-files.json` and `review.patch` retain this
checkpoint relative to its fresh source baseline; one-off drivers are archived
under `build/taxi-payment-audit/drivers` instead of remaining in the source tree.

**Reassessment:** the receipt's printing, physical identity and customer handoff is
the next concrete blocker because a paid customer may still demand it before
leaving. Luggage and full payday/receipt/odometer persistence follow. J06 stays
Partial and the broad inventory stays 82 areas: 22 Candidate / 51 Partial /
6 Missing / 3 Review. Close those dependencies, then rotate toward another missing
ordinary gameplay loop; optional taxi polish and long profiling do not take priority.


## Shared taxi receipt (2026-09-14, unreleased v243)

The guest can print the host's fare receipt, take and carry the single native paper,
and hand it to a customer who requests it. Rejoin restores the loose paper without
reprinting or crediting anything again. This closes the bounded receipt dependency;
**J06 remains Partial** for luggage transport and complete payday. Mod stays 0.1.33;
protocol is **243**, extending messages 234/235 without allocating a new ID. Both
peers require this protocol. No release, commit or push was made.

### Native behavior and authority

Installed build **23268598** is the ground truth, retained in
`build/taxi-fare-audit/native-taxi.json` and `flow.txt`. The terminal's State 6 adds
Trip to Tripmeter.OdoTotal and Cost to IncomeReceipts, runs the native printer
animation and resets live Price. Drop ticket detaches the already-existing
`Printer/TicketPhysicalPivot/receipt(itemx)` Rigidbody. The printer pivot is normally
inactive: discovering active loose items alone cannot establish this identity.

The adapter registers that exact native paper under FNV-1a32("taxi:receipt")
(**1775766455**). It shares hidden/printing/ready/loose/customer/returned phases and
uses ordinary item motion only while loose. Current fare/revision, authenticated
fresh nearby actor, action phase and sequence are checked on the host. Give also
requires the actor's accepted receipt ownership and paper within 3 m of the hand.
The existing native pickup guard rejects a second holder; anchoring releases the
local native hand joint and blocks stale generic motion, cargo and guest retirement.
No receipt clone or custom receipt money award is created.

The native Receipt::Use trigger looks up the PART named `receipt(itemx)` under the
player's ItemPivot. Its State 1 reparents the paper to the customer fingers, sends
CASHIER and waits ten seconds before returning it to the hidden printer pivot.
Guest commit states send intents while native host actions own those results.
The receipt request is an optional native branch; printing itself also works after
the quote while cash remains outstanding.

A controlled run exposed a real co-op departure bug: native Start walking compared
only the host camera against 99 m, then hid Char while the guest was beside the
customer. That suspended Receipt::Use's return timer and left the paper in the
hidden fingers. The validated departure distance action now uses the closest host
camera or fresh living guest. Native walking, visibility threshold and paper return
remain in charge. Cleanup restores the original action, guest presentation and
pickup state; taxi cleanup precedes item teardown.

### Validation, isolation and limitations

Core and the developer probe build against the installed game DLLs with **zero
warnings/errors**. **4,951 Net tests** (seven new receipt cases) and **18 launcher
tests** pass. New coverage checks receipt action phases/authority, roundtrip,
contradictory flags and invalid poses. `git diff --check` passes.

The final identical DLL/catalog/compat/probe payload passed **28 controlled native
two-player assertions**: 25 in `accepted-v243/`, three in `cold-v243/`:

- Guest pickup, arrival and quote use native host outcomes; early, distant,
  duplicate and pre-rejoin payment requests are refused, and unpaid cash rejoins.
- Payment credits IncomeTotal once. Printing adds exactly **28.9919987 MK** to
  IncomeReceipts and **0.1 km** to OdoTotal, resets Price and refuses duplicate print.
- Taking creates one shared physical identity. Rejoin restores the loose paper
  without repeating payment or printing. Native guest pickup owns it; the host
  cannot also pick it up while the guest holds it.
- Native hand lookup finds the exact paper; handoff releases the guest joint and
  places it in both customer hands. The native ten-second return hides it at the
  printer while the host remains far away and the guest observes the departing
  customer. The host snapshot confirms Start walking with Distance 7.19 m.
- Duplicate handoff leaves totals unchanged. Native guest and host saves return to
  the menu; cold reload restores **28.9919987 MK** collected and receipted income
  plus **0.1 km** on both peers, without a pending cash offer or extra credit.

Both final runs closed normally (exit 0). `profile-integrity.json` confirms all
**18 protected personal files** and **15 copied guest save files** are unchanged,
with no extra guest files. No games remain running; the disposable-game probe,
sandbox marker and command bus were removed. Personal installation and saves were
never deployed to. Copied Wine profiles preserve their original symlinks.

No taxi adapter failure or departure-distance exception appeared in these runs.
The accepted logs retain the same ES2FilenameData.PathIsFolder and native
GameObjectIsChildOf exception signatures as the prior v242 accepted run (two of
each per peer); passing assertions do not claim generally error-free game logs.

The probe enters native input states; it does not exercise physical mouse/keyboard.
Fixtures activate employment, place the customer for boarding, select the native
receipt-request branch after actual payment, provide guest gauge speed for a
nonzero metered trip, move the destination nearby and move the guest car eight
metres across the arrival threshold. Players use recorded safe anchors and the
printer test stands beside the car. These are controlled native checks, not a full
road journey, human input, role-reversed receipt handoff or Steam/two-PC acceptance.
They do not establish luggage, full payday or integrated fare completion. Vanilla
mid-fare/physical-paper persistence has not been added.

Earlier runs are archived separately and excluded from final acceptance. Binding
corrections distinguish native plain bool fields from FsmBool, literal null parents
from IsNone and the mutable hand lookup variable from its current value. Probe
corrections handle the camera/hand reparenting during driving, mouse-off immediately
leaving Waitbutton, and the normal DROP_PART delay before HandEmpty becomes true.
The `departure-v243` run records the real host-distance bug described above. The
`dashboard-fixture-v243` run placed the guest inside the dashboard and flung the car
away, so the host correctly refused out-of-reach printing; its test position was
moved beside the car. There were no player deaths or save wipes in this checkpoint.
`native-notes.md` retains these distinctions.

`validation-summary.json`, `profile-integrity.json`, `changed-files.json` and
`review.patch` under `build/taxi-receipt-audit/` retain evidence relative to this
turn's fresh source baseline. One-off drivers are archived there under `drivers/`.

**Reassessment:** luggage identity and transport are the next concrete missing
outputs in the already-selected fare, followed by full payday and integrated fare
persistence. Existing grocery/stutter reports and the recent stove, ATF, flea chips,
fuses and ignition-wire evidence remain recorded; no new report displaces this
unfinished gameplay dependency. The broad inventory stays **82 areas: 22 Candidate /
51 Partial / 6 Missing / 3 Review**. Finish this fare, then rotate to another missing
ordinary gameplay loop; optional taxi polish and long profiling do not take priority.


## Shared taxi luggage (2026-09-14, unreleased v244)

The host's selected luggage now appears as the same carryable objects on both
players. Guests can pick it up, move it with the taxi and leave it for the host to
unload. Native reset recalls the objects, clears hands/cargo and assigns fresh
identities. Rejoin restores the existing selection without another random roll.
This closes the bounded luggage dependency; **J06 stays Partial** for full payday
and integrated fare acceptance. Mod remains 0.1.33, protocol **244**; no release,
commit or push was made.

### Native inventory and authority

Installed build **23268598**, retained in `build/taxi-fare-audit/native-taxi.json`
and `flow.txt`, has **five usable luggage rigidbodies**: three suitcases, a beer case
and a mattress. All are tagged PART. Earlier audits included the sixth Riflebag
reference, but its object has no Rigidbody and is absent from the native selection
pool. That is unfinished native content; this change does not invent a sixth item.

`TaxiWalker::Suitcases` RESET returns objects to inactive pivots, draws a count
from Amounts and detaches randomly selected entries from the Luggage pool. The
serialized count pool contains six even though the runtime choice pool contains
five. The host adapter bounds that count at the validated distinct choices before
the native zero check. A controlled native six-piece draw therefore terminates
with all five usable pieces. Selection still runs through the native random loop;
the actual choice list remains unchanged.

The `taxiService` descriptor identifies reset/release/pool and five object references.
Binding validates the native reset targets, pivots, pose flags, release and count
check. Each native reset advances an epoch before reusing the bodies. Stable item
IDs derive from epoch plus slot, so old movement/cargo packets refer to removed
identities and cannot move a later customer's set. Message 230 appends epoch,
five-bit selection mask and five world position/rotation pairs (145 bytes). No new
message IDs; the next free ID stays 236. Both peers require v244.

The item registry explicitly includes the normally inactive bodies. Only selected
pieces use ordinary item/cargo motion; the pickup guard rejects a second holder,
and guests cannot destroy reusable luggage. Reset releases native pickup and
restores collision/depenetration settings from cargo before removing old identities.
Guest random rolls and distance penalties stay paused. Guest activation places the
Transform before enabling physics, because inactive Rigidbody pose writes were
ignored by this Unity build. Rejoin applies the current host selection and pose.

Native departure sends DISTANCES. Each active piece outside 10 m adds **120 seconds
to MaxDelay**, affecting subsequent customer wait/call timing. It is not a direct
cash deduction. These checks now observe accepted shared item/cargo poses.

### Validation and limitations

Core and the gated developer probe build against the installed game DLLs with
**zero warnings/errors**. **4,965 Net tests** (14 new luggage cases) and **18 launcher
tests** pass. A focused rerun also checks the first invalid slot, mask bit and array
length. Tests cover fixed message layout, roundtrip, malformed poses, epoch/slot
identity and bounding an impossible native count. `git diff --check` passes.

The final matching payload passed **17 controlled native two-player assertions**
in `accepted-v244/`:

- Five hidden bodies start with distinct shared identities. A native six-piece
  draw terminates with the five valid choices visible on both peers.
- Guest suitcase pickup moves the host copy; the host cannot also grab it, and a
  guest retirement request cannot destroy it.
- Reset recalls held luggage, releases the guest hand and replaces identities;
  delayed previous-set movement cannot revive it.
- Disconnect restores native guest luggage decisions and removes shared registry
  entries. Rejoin restores the same selection/identities without a new roll.
- Native beer-case and mattress pickup also move their host copies.
- A loaded suitcase joins guest vehicle cargo and pins on the host. Both follow
  the controlled short vehicle relocation; the host can unload that same suitcase.
- Native nearby-luggage checks add no delay. One piece beyond the native threshold
  adds exactly 120 seconds to MaxDelay.
- Reset while cargo is active clears cargo memberships and restores the original
  collision-detection/depenetration settings on the reused body.

The games closed normally (exit 0). All **18 protected personal files** and **15
copied guest world files** remain unchanged, with no extra guest files. Final DLL,
catalog, compatibility and probe hashes match the accepted payload. No games remain
running, and the disposable-game probe, sandbox marker and command bus are removed.
No taxi-adapter failure or restoration error was logged in the accepted run.
Passing these assertions does not claim generally error-free vanilla game logs.

The local test temporarily pauses native customer walking, forces native Amounts
choices, and enters native pickup/drop/DISTANCES events. Players use safe recorded
anchors. Loading and a five-metre loaded-car relocation are controlled placements:
they check item/vehicle-cargo replication, not human trunk input or road physics.
This is not a full road fare, Steam/two-PC test or fresh integrated quote/receipt/
payday playtest. Passenger-held luggage while driving and long cargo trips remain
untested. Native mid-fare luggage persistence has not been added; no new save/cold
reload claim is made for these objects.

Exploratory runs are retained separately. `initial-v244` rejected the absent rifle
Rigidbody and corrected the six-piece inventory assumption. `five-piece-v244`
passed selection/count bounding but exposed guest activation at the wrong position;
it was stopped and the activation order was corrected. `activation-v244` passed
16 checks for carrying, reset, rejoin, transport, unload and native distance delay.
Final cleanup review then added restoration of cargo collision settings during
recall and an additional native reset-during-cargo check. Only the final accepted
payload is the completion evidence. No personal installation was deployed to;
these were separately marked disposable Wine profiles with symlinks preserved.
`native-notes.md` records the corrected facts and fixture boundaries.

Evidence is under `build/taxi-luggage-audit/`: final logs, native replies,
`validation-summary.json`, `profile-integrity.json`, `changed-files.json` and
`review.patch` relative to a fresh 975-file source baseline. One-off drivers are
archived under `drivers/` rather than left in the source tree.

**Reassessment:** payday through native bank settlement and saved results is the
next missing outcome in the selected taxi loop, followed by integrated fare
acceptance. Luggage is closed at the bounded local level; optional luggage polish
will not keep displacing other gameplay. Grocery/stutter reports and the recent
stove, ATF, flea chips, fuses and ignition-wire checkpoints retain their recorded
limits. J06 remains Partial and the inventory stays **82 areas: 22 Candidate /
51 Partial / 6 Missing / 3 Review**. Finish the fare, then rotate to another missing
ordinary gameplay loop.

## Shared taxi payday (2026-09-14, unreleased v245)

Bounded outcome: the host's native payday reaches the shared bank and net income,
clears settled taxi earnings, exposes the same salary report to the guest, and
survives host save/reload. **0.1.33 remains unreleased; protocol 245 is required on
both peers.** The bank credit already used WalletState. The missing implementation
was the guest salary letter/rundown and shared acknowledgment.

### Native evidence and implementation

Installed build **23268598**: retained Payments evidence in
`build/taxi-fare-audit/native-taxi.json` / `flow.txt`, plus a fresh extraction of
EnvelopeTaxiRundown, Sheets/TaxiRundown and Systems/BankAccount in
`build/taxi-payday-audit/native-payday.json` / `flow.txt` (12 FSMs, four ArrayLists).
The source level2 SHA256 is
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`.

Payments waits for its payment day and Tripmeter.Off, reads collected IncomeTotal,
applies the native 40% salary share, conditional 3.7 MK/km compensation and
1.83 MK/minute phone cost, then clamps the net wage. Positive pay adds to both
PlayerBankAccount and PlayerNetIncome and records a TaksiPalkka bank entry. State 5
updates the odometer baseline and clears CallMinutes, IncomeTotal, IncomeReceipts
and OdoTotal. Native ES2 saves the balance globals, rundown list, unread flag,
odometer baseline and call minutes. No second payout implementation was added.

`TaxiServiceBinding.Payday.cs` follows cataloged native references and validates
the eight-float rundown, net cell's index/reference/type/source, close flag output
and clamped Money source. Message 230 appends report ID, unread/envelope flags and
eight floats (37 bytes). Message 236 acknowledges a report ID; the transport actor,
current report, unread/visible flags and fresh player position within 4 m are checked.
Opening the native guest sheet captures the displayed report's ID; closing keeps
its native camera/menu flow and sends only that acknowledgment. The actual envelope
active flag is separate: vanilla does not hide the envelope immediately on close.
Guest Payments remains suppressed, and disconnect closes an owned sheet and restores
original guest rundown values, letter visibility, variables and native actions.

The exploratory run exposed a native zero-pay bug: Phone use skips Payment when
phone costs consume the wage, so rundown index 7 retained the previous payment.
At native State 5 the host adapter now writes that cell from the already-clamped
Money, including zero. It changes the report, not bank credit or wage calculation.

### Validation and limitations

Core and the gated developer probe build against installed game DLLs with **zero
warnings/errors**. **4,972 Net tests** (seven new payday cases) and **18 launcher
tests** pass. Wire checks cover the fixed rundown, malformed values/shape/flags,
authenticated acknowledgment, stale/distant/repeated reads and protocol roundtrips.
`git diff --check` passes.

**16 controlled native two-player assertions** in `final-v245/` passed:

- Native payday waits while the meter is on. Turning it off settles once.
- A 1,000 MK fare ledger, 900 receipted MK, 100 work km, 120 total km and ten
  phone minutes produce 400 MK salary + 444 MK fuel − 18.30 MK phone = **825.70 MK**.
  Both peers receive the same bank/net-income increase; cash stays unchanged.
- The host records `TaksiPalkka / 825.70+`. Both peers receive all eight report rows,
  and settled income, receipts, work distance and call minutes clear.
- The guest opens the native mailbox and salary sheet with those figures while
  the host's view stays closed. Disconnect while reading closes the guest menu
  and restores its original rundown; rejoin restores the host's report and balances.
- Distant acknowledgment is rejected; normal guest close shares the read flag.
  Repeated close and an empty-ledger payday do not pay again.
- A 10 MK ledger with 100 phone minutes produces **zero** bank credit and zero
  displayed net pay. An old report acknowledgment cannot mark that new report read.
- A later payday credits only its new **298.34 MK**. The guest's guarded save and
  the host's native save both return to the menu normally.

**Four cold-load assertions** in `cold-v245/` passed: native reopening restores the
same shared bank (**3,167.62 MK**), net income (**1,124.04 MK**) and cash (**691.05 MK**),
all eight rows of the latest report and its unread envelope; the paid earnings,
receipts and work-distance ledger stay zero; the guest can read the restored sheet
without another payment. The final cold payload also includes the additional native
net-cell signature guard added during review. The live 16-check run preceded only
that guard; wire and settlement behavior are identical. Payload hashes for each run
are retained rather than claiming both DLLs were identical.

Fixtures enter the native payday-day comparison with PaymentDay set to the current
day and seed completed-earnings/odometer/phone inputs. Native calculation, credit,
reset, sheet presentation and saves run normally. This is **not an integrated driven
fare**, a full week of clock simulation, physical mouse/keyboard acceptance, Steam,
two-PC testing or a long-road soak. Bank statement history on guests was not added.
No saved mid-fare customer, luggage or paper is claimed.

Earlier exploratory artifacts are retained: the original zero-net display failure;
a fixture that attempted to read a closed mailbox before its Use FSM was enabled;
a stopped acceptance attempt that mistook standby mode 35 for an active fare; and
a setup run stopped to correct its assumptions about native text formatting. The
final runner uses fare mode 70, opens the native mailbox, and checks its actual
numeric text. No taxi adapter/restore/hook errors occurred in the accepted live and
cold runs. Unrelated pre-existing Unity diagnostics remain in the raw logs.

### Isolation, cleanup and reassessment

Only `build/local2p/game` and copied marked profiles under
`build/taxi-payday-audit/profiles` were used. Both accepted runs closed normally.
All **18 protected personal files** and **15 copied guest world files** remain
unchanged, with no extra guest files. The disposable host save contains the payday
result. Test plugin, marker and command bus are removed; one-off drivers are archived
under the audit directory. Scoped baseline/review, checks, logs, payload hashes and
integrity results remain in the ignored audit directory. No commit, push or release.

This closes the bounded payday dependency. Reassessment keeps **J06 Partial** and
the wider inventory at **82 areas: 22 Candidate / 51 Partial / 6 Missing / 3 Review**.
The next required dependency is one integrated call → pickup/luggage → arrival →
cash/receipt → payday/save acceptance check. Then rotate to missing household fuse
installation (H04), which can use the already-shared loose fuses from I05. Existing
bag and stutter evidence remains recorded with its limits; no new report displaces
this final fare connection check. Optional taxi polish should not extend this topic.


## Connected taxi fare (2026-09-14, unreleased v245)

The selected call-to-payday journey now has one connected native local two-player
check. This closes the integration dependency left by the separate taxi checkpoints;
J06 remains Partial. Mod version stays **0.1.33 unreleased**, protocol **245**.
Native build **23268598** was used. Artifacts are in `build/taxi-journey-audit/`.

### Outcome and cleanup fix

The same host-created call produces the customer, selected suitcase and fare. The
guest answers, loads the suitcase, boards as driver, reaches the shortened native
destination, collects the quoted fare, prints and carries the requested receipt,
hands it to that customer, then unloads the same suitcase. Native receipt accounting
feeds native payday; no separate fare-income or receipt ledger is seeded. Both peers
receive the same bank credit and salary report. The guest reads it, the host saves,
and cold reload retains the credited balances and read report with no unpaid fare.

Log review of the first successful journey caught a menu-transition race: a late
meter packet reached its cached binding after Unity destroyed the native knob,
before world cleanup ran. All taxi message entry points now require the GAME scene
before touching native bindings. This is a local lifetime guard; no wire layout,
authority or in-world fare semantics changed. A gated developer check injects a
newer valid meter packet in the MainMenu load callback, while the binding still
exists and its knob is destroyed, and verifies that neither its remote state nor
failure flag changes. Normal world cleanup remains responsible for restoration.

### Validation and limits

Core and the gated developer probe build with **zero warnings/errors**. The protocol
suite passes **4,972 tests**. No launcher code changed; launcher tests were not rerun
in this checkpoint (the preceding payday checkpoint passed 18).

**17 live native checks** in `final-v245/` cover empty initial ledgers and shared
duty, the same accepted call/customer/fare, selected suitcase pickup/cargo/travel/
unloading, native arrival and one quote, collection, printing/carrying/handoff,
earned payday, guest reading, both native save exits and the deterministic late
meter packet after native destruction. The fare was **26.509 MK**, its native
receipt recorded **0.1 work km**, and payday credited **10.6036 MK**. Shared bank
ended at **2,054.18359 MK**, net income at **10.6036 MK**, with cash unchanged at
**691.05 MK**. Fare income, receipts and work-distance ledgers cleared.

**Four cold native checks** in `cold-final-v245/` confirm those exact balances
without another wage, all eight salary rows including the earned receipt/distance,
the saved read flag with no unread envelope, and no outstanding fare cash, receipt
or unpaid ledger. Accepted live and cold runs use **identical Core, Net, catalog,
compatibility and probe payloads**, matching the final build and disposable deploy.
`git diff --check` passes.

Fixture boundaries are explicit: host job availability is enabled through its
native state (hiring/tutorial is skipped); the next incoming call is triggered;
its pickup/drop-off transforms are shortened; native luggage choices select one
suitcase and the native receipt-request branch is made certain before the lifecycle
runs. The guest is moved near controls, native input events are driven by the probe,
the suitcase is placed in cargo, and short five-metre car/cargo movements stand in
for road travel. A 72 km/h telemetry fixture gives 0.1 km of metered work distance.
The payday comparison is advanced to the current weekday; the starting car-odometer
baseline is controlled. Needs are held stable on the disposable profiles.

Native customer creation, boarding, arrival, quote, collection, receipt processing,
departure, wage calculation, bank credit, ledger reset and saving execute along the
same connected lifecycle. This is **controlled local UDP evidence**, not physical
mouse/keyboard or road-driving acceptance, a full week of clock simulation, role
reversal, Steam/two-PC testing or saved mid-fare progress. Other luggage choices,
fares without receipts and alternate customer routes retain their earlier evidence
and limits. Guest bank statement history and player passenger seats remain open.

The earlier `live-v245/` attempt was stopped after overlapping controller commands
lost a startup reply; it reached no gameplay assertion. `connected-v245/` and
`cold-connected-v245/` passed their gameplay assertions but exposed the meter
menu-transition error in the live log. They are retained as pre-fix evidence.
Final payload hashes distinguish those runs from the corrected acceptance build.
No taxi adapter, restore or hook errors occurred in the accepted final live/cold
logs. Other diagnostics remain: native Unity errors, Corris guest-engine protection
messages, an unrelated missing chips template, and world/item checksum repair
requests already present in the previous payday run. This checkpoint does not
establish that the whole session log is clean or resolve those other systems.

### Isolation, cleanup and reassessment

Only the copied game at `build/local2p/game` and marked disposable profiles were
used. All **18 protected personal files** and **15 guest world files** remain
unchanged, with no extra guest files. Both accepted runs closed normally; no game
process remains. The developer plugin, sandbox marker and command bus were removed.
One-off drivers are archived under `build/taxi-journey-audit/drivers`; the reusable
gated journey/teardown probe remains in `tools/GuestSaveProbe`. Source baseline,
scoped review, per-check snapshots, expected fare/payday values, logs, payload hashes
and integrity results are retained in the ignored audit directory. No commit, push
or release was performed.

This closes the selected controlled one-fare journey. The scope inventory stays at
**82 areas: 22 Candidate / 51 Partial / 6 Missing / 3 Review**, with **J06 Partial**.
At this boundary, the recorded grocery/duplicate-bag and stutter reports have no new
reproduction that displaces missing gameplay. Existing car, supply and household
checkpoints retain their limits. Next is **H04 household fuse installation**: use an
already-shared I05 loose fuse in a native holder, share insertion/tightening and
circuit state, and verify removal plus host save/rejoin. Check the native holder and
power consumers first; the main switch and stove Fuse flags do not represent those
individual holders. Optional taxi polish, more synthetic fares and performance
work do not justify extending this category.

## Household fuse replacement (2026-09-14, unreleased v246)

The H04 implementation follows the native electricity databases: seven house
holders and four apartment holders. Guest actions insert one I05 shared loose
fuse, fit/remove a holder and turn its screw. The host validates ownership,
proximity, current holder revision and native readiness before running the native
state. Native Use controls condition, meshes, insertion availability and each
home's boolean circuit list. Only a good fully tightened fuse restores its circuit;
removing a blown fuse clears it, while removing a good fuse retains it in the holder.

Fixed holders do not participate in item motion. Loose holders have stable IDs
from their persistent catalog positions, exclusive pickup and ordinary item/cargo
motion. Fitting retires their current motion binding and releases the player's
hand. Fuse insertion consumes the actual shared supply through native GARBAGE,
including releasing a guest hand and native save deletion. Guest copies have their
Use/save simulation disabled; original guest holders and circuit lists are kept
for disconnect restoration. Guest turns run the shock check on the requesting
player, while the native host turn still runs its own check.

Protocol **246** adds messages **237–239** and updates compatibility metadata;
mod version remains **0.1.33 unreleased**. Cross-home fitting is rejected before
serializing an intent, because native holders retain their original electricity
database. A rejected cross-home attempt must leave ordinary controls usable.
Changed native signatures or malformed fuse catalog data disable the fuse adapter
without invalidating unrelated catalog sections. GAME-scene guards reject late
fuse messages before touching destroyed native objects.

### Native inventory and fixtures

Game build **23268598** was inspected. `level2` SHA-256 is
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`;
`sharedassets3.assets` is
`4212b819e589e084c9fb0b26a6790e3760a27433d56c696e231aaf607b976b43`.
The saved holder IDs are `fuseholder1x`–`fuseholder11x`. The live ArrayList
proxy exposes `arrayList` as a property. Slot IDs are local to each database;
network slots are house 0–6 and apartment 7–10. Native holder Assembly destroys
its Rigidbody; Removal recreates one. The adapter follows those transitions. Native cold loading can retain the old
socket parent even at zero tightness. Fitted classification therefore requires
positive native tightness; a parent pointer alone incorrectly immobilized a saved
loose holder on the guest. Wire validation rejects fitted zero-tightness entries.
Holder pickup uses the native PART branch (`Set pivot`), which has its own
exclusive-pickup gate in addition to the existing ITEM branch.

Artifacts are retained under `build/household-fuse-audit/`. The developer probe
requires `WINTERMP_LOCAL2P_HOUSEHOLD_FUSE_TEST=1` plus the existing bag, supply and
persistence gates and the exact disposable-profile marker. It drives native
Screw/Unscrew, Removal and Assembly input entries, sets the host's native BLOWFUSE
event, and creates replacement fuses through the native factory beside the tested
holder. It moves players near controls and aims the native hand joint and held
object together at the socket. It holds needs stable and suppresses the
random fatal shock branch while counting which player's native shock FSM executes.
The native holder, fuse-consumption, circuit and save outputs are not seeded.
These are local two-game UDP fixtures, not physical mouse/scroll-wheel acceptance,
actual random electrocution, automatic overload timing, Steam/two-PC play or a
full test of every powered appliance. The normal native circuit consumers remain
in place. Cross-home transfer remains unsupported; same-home slot interchange,
long cargo travel and every individual circuit still need physical acceptance.

### Accepted evidence and build boundaries

Core and the guarded probe build with **zero warnings/errors**; **4,988 Net tests**
(including 16 added fuse/catalog cases) and **18 launcher tests** pass.
`joint-v246/` passes **25 native workflow assertions**: both-home replacement,
one consumed shared fuse per insertion, restored native circuit flags, competing
pickup, good-fuse removal, duplicate/stale/distant/forged requests, original guest
holder restoration, rejoin, guest guarded save and native host save.

That save deliberately differs from the guest's personal world: network holders
0 and 7 are repaired and tight at 8; holder 1 is blown and tight at 8; holder 8
retains a good fuse but is loose at 0. House circuits are
`True,False,True,True,True,True,True`; apartment circuits are
`True,False,True,True`. Consumed native supplies `fuse06` and `fuse07` must not return.

`cold-fixed-v246/` passes **nine native reload/regression assertions on the final
build**. It reloads those exact states/circuit flags and confirms both consumed
fuses remain absent and guest originals remain pristine. Host native turns retain
the host's own shock checks. The test then follows actual native ITEM/PART
classification, refuses a guest pickup of a holder already held by the host,
refuses cross-home fitting by both roles, and successfully refits/tightens the
saved loose apartment holder afterward. The last sequence restores that apartment
circuit and releases the guest hand. This cold run quits without saving its extra
cross-home/ownership fixtures.

**The two accepted runs do not use identical binaries.** The workflow run preceded
the final native PART pickup entry guard and the cold-load classification fix.
The final cold run covers those changed paths, including a successful fit after
rejection, and matches the final Core, Net, catalog, compatibility and probe hashes.
Compatibility is unchanged between those runs; Core, Net, catalog and probe hashes
differ. `payload-verification.json` records this explicitly. These results must not
be described as 34 assertions on one unchanged binary, nor as Steam acceptance.
The final disposable deployment matches the final build; its test probe is removed.

Earlier attempts remain archived with their failures. `native-v246/` found the
array-list property binding mistake. `binding-v246/` found an ambiguous reflection
overload in the helper. Subsequent attempts corrected helper identity/revision/
hover assumptions, artificial long-distance pickup relocation and positioning only
the held object while the native hand joint pulled it back. They are not counted
as completed workflow acceptance. `cold-v246/` exposed the real saved-parent versus
loose-state bug described above; its unchanged saved input was rerun successfully
in `cold-fixed-v246/` after the production fix.

No fuse adapter failure, fuse restore or pickup-guard error occurred in the accepted
workflow/final cold logs. The whole log is **not** clean: native ES2 mesh-save,
PlayMaker and disposed-sound errors, Corris guest-engine protection messages, an
unrelated missing chips template, rejected distant/unauthorized movement and
world/item checksum repairs remain recorded. Similar native errors occur in the
preceding taxi checkpoint. This fuse task does not claim to resolve them.

### Isolation, cleanup and next work

All **18 protected personal files** and **15 guest world files** remain unchanged,
with no extra guest world files. Only the copied game and explicitly marked
profiles under `build/household-fuse-audit/profiles` were used. Both accepted runs
closed normally. No game process, developer test DLL, sandbox marker or command bus
remains. One-off drivers are archived in `build/household-fuse-audit/drivers`; only
the reusable gated probe source stays under `tools/GuestSaveProbe`. The 983-file
source baseline, scoped review, per-check snapshots, native inventory, logs,
payload comparisons and integrity report remain in that ignored audit directory.
No commit, push or release was performed. `git diff --check` passes.

H04 is now Candidate. The inventory is **82 areas: 23 Candidate / 51 Partial /
5 Missing / 3 Review**, with physical/platform acceptance still separate. Reassessment
selects **V13 tractor-trailer attachment and release** next: establish the native
connection and persistence behavior, then implement one shared coupling journey.
Towing ropes and other implements remain separate tasks. Existing tester reports
have no new reproduction that displaces this missing vehicle gameplay; optional
fuse polish or more synthetic cycles do not justify extending the household topic.


## Tractor-trailer coupling (protocol 247, unreleased)

2026-09-14, installed build **23268598**. This closes one bounded V13 journey:
automatic tractor attachment, host/guest release, physics delegation, reconnect
and native host save/reload. V13 as a whole is **Partial**, because towing ropes,
other implements and broader trailer use remain open. Mod version stays **0.1.33**;
nothing was committed, pushed or released.

### Native graph and implementation

The installed `level2` extraction identifies `KEKMET(350-400psi)/Trailer/Hook ::
Distance` and `Trailer/Remove :: Use`. The hook attaches below 0.3 m, writes
`TrailerAttached`, enables the release handle/rear hydraulics and saves
`TractorTrailerAttached`. After release it waits for more than 2 m of separation
before detecting another attachment. `FLATBED :: Detach` switches its CharacterJoint
between the tractor and its own detached support; a separate HingeJoint carries
the tipping bed and a FixedJoint links support to chassis. `FLATBED :: LOD` saves
`FlatbedTransform`. Native data and decoded action flows are retained in
`build/tractor-trailer-audit/native-trailer.json`, `native-hydraulics.json`,
`native-joints.json` and `flow.txt`. The native asset hash matches the preceding
build23268598 audits; this extraction describes asset defaults, not saved state.

`TractorTrailerSync.cs` and `.Bindings.cs` validate the native graph and use native
FSM transitions for connection/release. Guests pause automatic local coupling,
send revision/sequence-bound release requests and mirror the accepted connection.
Only the host validates actor, fresh proximity and current attachment, executes
release and owns the native save. Exactly three body poses/velocities and the
connection anchor are carried by `TractorTrailerState`/`TractorTrailerMotion`.
The accepted tractor simulator owns the attached trailer; detached trailers revert
to host simulation. Old revisions, duplicate sequences, forged actors, non-owner
motion and implausible separated hitch motion are refused before physics changes.
Guest originals, physics flags, native connection and bed joint settings restore
on disconnect. The adapter contains its own failures.

Two real integration conflicts were found and fixed:

- The generic vehicle registration **did already include FLATBED through its mass
  fallback**, despite the absence of its name from vehicle prefixes. That gave the
  chassis an independent lease while the new adapter controlled the bed/support.
  `VehicleRegistrationConfig` now excludes the cataloged trailer chassis, so the
  assembly has one owner. This corrects the initial inspection's narrower reading
  of name-based registration; the earlier missing connection-lifecycle finding
  remains valid.
- A coupled parked tractor could relinquish its generic lease when the host was
  far away. Its guest body would then simulate locally against a streamed trailer.
  `HostTrailerAttached` now keeps the ordinary host tractor stream alive outside
  proximity range. A real guest driver still takes over through the existing
  vehicle gate; release clears the extra hold. The trailer owner uses the same
  remote-stream freshness policy as the tractor.

Protocol **247**, IDs **240–242**, is documented in `protocol/PROTOCOL.md` and the
launcher compatibility manifest. Next free ID is **243**. Catalog `tractorTrailer`
contains native paths/state names and isolates malformed configuration to this
adapter. The probe requires both `WINTERMP_LOCAL2P_TRAILER_TEST=1` and the existing
marked disposable persistence environment; it is never distributed with the mod.

### Accepted evidence

`build/tractor-trailer-audit/journey-v247/checks.json` records **17 assertions**:
initial detached three-body presentation, native proximity attachment, distant and
forged release refusal, guest release, duplicate/stale refusal, the native two-metre
rearm, reattachment, guest physics handoff, moving joint/observer convergence,
disconnect returning physics to host, original guest restoration, rejoin,
host release/reattach, and guarded guest/native host saving. Per-command replies,
pre-save state, process logs and copied resulting save files are retained there.

`cold-v247/checks.json` records **7 further assertions** on a fresh game restart:
loaded attachment/joint, saved trailer location, full guest assembly, non-owner
motion rejection, host driving authority, joined/converged host motion and release
of the saved connection. The first cold script compared against a snapshot taken
while the final alignment was still settling, several seconds before saving. That
comparison failed. `checks-resumed.log` uses the archived **actual host save-command
reply** instead; no production code or saved input changed, and the remaining cold
checks continued in that same restarted session. The failed comparison is retained
in `checks.log`, not silently treated as a pass.

Warm and cold runs used **matching Core/Net DLLs, catalog, compatibility file and
probe**. The final Core/Net build hashes still match both accepted runs. One later
probe-only cleanup clones a state before forging a test packet so future tests do
not mutate their local cached snapshot; it compiled cleanly and does not change
production code or the acceptance payload. `payload-verification.json` records
these distinctions.

The final **4,998 Net tests** pass (including ten trailer protocol/catalog cases
and the all-message roundtrip sweeps), as do **18 launcher tests**. Core and the
opt-in probe compile against the installed game DLLs with **zero warnings/errors**,
using `DeployToGame=false`. Accepted native runs have no trailer adapter, restore,
handler or unexpected-channel error. Existing native ES2 mesh-save errors,
PlayMaker null references, disposed-sound errors and unrelated soft resyncs remain
in the logs; this is not a claim that the whole game/mod log is clean.

Earlier exploratory runs are retained: `initial-v247/` exposed a missing channel-1
registration and a bad alignment fixture; `coupling-v247/` attempted guest actions
before the returning-player spawn choice had completed; `authority-v247/` exposed
the competing generic chassis lease; `assembly-v247/` exposed the parked tractor
lease gap. None substitutes for the accepted final journey/cold evidence.

### Isolation, remaining work and rotation

The test used only `build/local2p/game` and marked copied profiles under
`build/tractor-trailer-audit/profiles`. **All 18 protected personal files and all
15 guest profile files remain byte-identical**, with no extra guest files.
Both accepted runs closed normally. The disposable probe DLL, sandbox marker and
command bus were removed. Five one-off drivers are archived under the audit's
`drivers/` directory; the gated reusable C# probe stays in `tools/GuestSaveProbe`.
The 1,042-file source baseline, scoped diff, logs, native asset evidence, payload
comparison and integrity report are retained in the ignored audit directory.

These are **controlled native fixtures**: alignment enters the native distance
check, release enters its normal native state, and driving tests parent the player
to the tractor and seed velocity. They exercise real joints/physics, networking and
save/load, but do not establish mouse input, engine-powered steering, long loaded
trips or Steam/two-PC play. Rear-hydraulic/hatch control sharing, cargo across the
extended bed, firewood delivery while travelling, towing ropes and other implements
remain separate gaps. The native saved root/attached flag is verified; persistence
of an arbitrarily raised bed was not added or claimed.

The project-wide reassessment selects **I09 sausage-package opening into four
shared loose sausages** next. It is a missing ordinary gameplay factory and builds
on the earlier stove work. The bounded coupling task is closed; optional trailer
polish and longer soak runs do not displace that missing supplies loop.


## Sausage package conversion (protocol 248, unreleased)

The I09 task adds a shared native package-to-four-items conversion. The game-build
23268598 audit found eight `SausageTrigger :: Logic` graphs: the cabin, cottage
fireplace/grill, portable grill, apartment stove, factory kitchen, and old-house
kitchen/fireplace. All eight bind in the native two-game run. Interactions were
exercised at the apartment stove; the inactive factory kitchen is a binding check.
The `sausage0` prefab has separate Use/Fire FSMs but no native ID or SAVEGAME
routine. Loose sausages remain session-lived, matching vanilla.

The host guards the actual package identity and captures four native CreateObject
outputs. Each gets a distinct shared ID and the package's host Condition. Native
SetFsmFloat writes mistakenly target the prefab; the capture instead initializes
each output, avoiding condition leaking between packages. The package's native
GARBAGE path preserves its consumed save tombstone. Source-only ActionGrill is
retired because it cannot identify the item being consumed.

A second-purchase test found that the generic scanner reused the display-name ID
of a consumed package and immediately retired its successor. Sausage packages now
use their native `sausagesN` IDs for both factory/bag outputs and saved-product
manifests. Guest-native packages are hidden and restored; network package/food
replicas are discarded on disconnect. Loose-food retirement hooks are removed
with their session bindings. Host-only cooking/spoilage supplies condition,
raw/grilled/charred/spoiled appearance and the separate native Grilled effect flag;
guests retain native eating for edible food. Rejoin and targeted/full snapshots
carry surviving food, and retirement defeats delayed food state.

Validation uses only `build/local2p/game` and disposable profiles under
`build/sausage-opening-audit/`. Production builds use `DeployToGame=false`.
The opt-in `tools/GuestSaveProbe/LiveBagProbe.Sausages.cs` requires both the copied
game marker, marked persistence profile and `WINTERMP_LOCAL2P_SAUSAGE_TEST=1`.
Drivers are archived in the audit's `drivers/` directory after use.

- Final `final-journey-v248`: 19 controlled assertions covering host native contact,
  four matching IDs/conditions, guest eating with cooking paused, spent-package
  rejection, cooked/burnt/spoiled state, retirement replay, successive packages,
  outside-trigger/distant/forged/duplicate requests, valid guest request and native
  guest opening hook, teardown, rejoin, repeated snapshot and native save.
- Final `final-cold-v248`: native cold lifetime, consumed-package absence,
  unopened native package identity, guest reconstruction, refusal of an old
  consumed-package request, replica cleanup and another join.
- `cold-v248` exposed a fixture detail: the final unopened package had been saved
  suspended above the stove. Native reload restored gravity and opened it. Its
  four output IDs match that saved package; none of the eleven old loose sausages
  returned. `native-reopen.json` records this. The same payload created/saved a
  floor fixture away from contact (`resaved-fixture.json`). `cold-floor-v248` then
  passed all seven checks. The final reviewed journey saves its unopened package
  on the floor directly; `final-cold-v248` loads that exact save.
- `initial-v248` used Rigidbody.position in a frozen placement fixture; Unity
  restored the old transform before contact. `placement-v248` exposed the actual
  reused package-ID bug. `identity-v248` passed the journey before the final
  explicit retirement-hook cleanup. `journey-v248` passed with that cleanup; the
  final review also bounds package condition, excludes unrelated item types from
  sausage identity inspection, and stops discovery after all eight sources bind.
  Earlier runs are retained as intermediate evidence.
- Net: **5,009 passed**, including message round trips, authority/geometry
  preconditions, source-specific IDs, retirement of successive packages, finite
  food/pose validation and malformed-catalog isolation. Launcher: **18 passed**.
  Net net35/netstandard2.0, Core and opt-in probe compile without warnings.

Artifacts include per-command replies, assertions, retained game logs, native FSM
extracts/flow, before/after save hashes, payload hashes and a scoped source review.
The final journey and cold run use identical production DLLs/catalog/compatibility.
Personal installed files and saves are outside the fixture. Integrity results are
recorded separately rather than inferred from the launcher configuration.

This is controlled native-state/physics testing, not physical mouse/hand gameplay
or Steam/two-PC acceptance. It does not establish natural grilling duration at all
locations, package/pizza refrigeration, NPC-provided food, full fire consequences
or general consumable authority. I09 is Candidate; I08 is Partial. The project-wide
reassessment selects **V04 human passengers in the taxi**, starting from its native
seat layout, because ordinary co-op travel still lacks that passenger registration.


## Taxi human passengers (protocol 249, unreleased)

On 2026-09-14, the installed game build 23268598 was extracted again before adding
taxi passenger seats. Native `Functions/MassPassenger` is the rear-right fare
seat. The new catalog selects the exact taxi body, driver mass, customer mass,
drive trigger and tutorial transforms. Front right mirrors the driver mass point;
rear left mirrors the customer point. Human wire slots are 0/2; slot 1 stays reserved
for the fare customer even while empty. Sorbet/Corris retain all three seats.

The host validates seat availability for new claims and continuing keepalives.
Distant, occupied, reserved or unavailable seats cannot become accepted occupancy.
Local unavailable seats release parenting and the CharacterController. A tutorial
that is active only on the host can take up to the existing eight-second keepalive
interval to reject a seated guest. No new message layout is needed; protocol 249
marks the changed accepted vehicle/seat semantics and next free ID stays 245.

**Validation:** 22 controlled two-game journey assertions and five cold-load
assertions pass on the exact same final Core/Net/catalog/compatibility/probe files.
The journey covers both native door prompts, both driver/passenger roles, short
translated/rotated car motion, keepalive retention, native customer boarding beside
a human, simultaneous use of the two human seats, conflict/reserved-seat rejection,
host-seat replay on rejoin, tutorial availability changes, fresh entry and cleanup.
The cold run reloads the host's native saved world, rediscovers the seats without
ghost occupants and repeats entry, reserved-seat correction and exit.

The Net suite passes **5,027 tests**, including 18 new catalog/seat-policy cases;
launcher tests pass 18. Net builds for net35/netstandard2.0; Core and the gated probe
build against the installed game with zero warnings/errors. No passenger subsystem
disables occur. The game still logs six unrelated native NullReferenceExceptions
per role in each run; this is not a globally clean-log claim.

Both runs use `build/local2p/game` and separate marked disposable Proton profiles.
All 18 protected personal save/installed-mod files and all 15 guest world/profile
files retain their hashes, with no extra guest files. The two games exit, and the
copied-game probe, marker and command bus are removed. Audit artifacts are under
`build/taxi-passenger-audit/`: `initial-v249` (22 checks), `cold-v249` (five),
`payload-verification.json`, `profile-integrity.json`, native extractions and source
review. Temporary drivers are archived there under `drivers/`; the source probe
remains gated by the taxi and taxi-passenger opt-ins and the persistence sandbox.

**Limits:** entry commands, positioning, short car displacement and tutorial
activation are controlled fixtures. The tutorial fixture pauses its native FSMs
while checking seat availability; it does not complete the driving tutorial.
Physical keyboard/mouse interaction, camera comfort, road-length powered drives,
Steam/two-PC, extra passenger vehicles and saved mid-fare progress remain open.
No public release, commit or installed-game deployment was made. The bounded taxi
seating task is closed; H10 coffee preparation/transfer/drinking is the next missing
ordinary gameplay implementation.


## Home coffee preparation and drinking (protocol 250, unreleased)

The bounded H10 slice is the household pot, reusable cup and finite ground-coffee
packets. Vendor/vending-machine cups remain missing; H10 stays Partial. This is
build 23268598 native-graph work with controlled local two-game tests, not mouse/
hand acceptance, a Steam/two-PC session or proof of all coffee variants.

The host keeps native pot Data/Fire/Empty, water and grounds simulation. Guests
suppress these writers. Cup filling replaces two independent native changes with
one conserved transfer: 0.16 units/second, 0.3 cup capacity and the native 0.1 pot
floor. Local filling renews its intent while held and aligned; the host validates
fresh player position, ownership, geometry and a short expiring lease. Cup drinking
waits for the host to empty the shared cup, then the matching actor runs the native
Play anim/DRINKCOFFEEHOME graph. Replay cannot grant another personal effect.

Native testing found and corrected three implementation issues:

- Ground-coffee IDs use `groundcoffee0` plus the positive counter (`groundcoffee01`,
  `groundcoffee02`), rather than `groundcoffee` plus a padded counter. Display names
  are unsuitable for identity.
- Returning immediately to the cup's idle input state could end filling at about
  0.128 units. A continuous input action now renews the lease until full, empty,
  dropped or separated; host and guest do not run the two native float writers.
- Picking up/dropping the cup changes its parent and scene path. Native
  `UniqueTagCoffee` values identify the same household cup/pot after reconnect,
  while their catalog paths retain stable network IDs. Vendor cups do not have
  these household save tags. Duplicate household save tags isolate this adapter.

Evidence is under `build/coffee-audit/`: native scene/prefab/player-effect/factory
extractions, source/personal-file baselines, diagnostic runs and accepted run
artifacts. `LiveBagProbe.Coffee.cs` is gated by the separate coffee opt-in plus
both disposable-game and disposable-save markers. It creates packets through the
native factory, positions/tilts bodies and enters native input states. Water tests
use the native lake-height condition; brewing uses native ONFIRE with a short
seeded grounds amount. This does not certify tap/stove placement or user input.
Lid/pour foley, varied recipes, vendor coffee, physical handling and long sessions
remain outside acceptance. Native boiling sound is part of the shared state.

Only `build/local2p/game` and copied profiles are used. The real installed plugin
and personal save files are fingerprinted before/after. Production changes are
unreleased 0.1.33; messages 245–247 and their semantics require protocol250.

Accepted final evidence: `final-v250/checks.json` (20 journey checks) and
`cold-v250/checks.json` (7 checks), on identical Core/Net/catalog/compat/probe
payloads. Cold reload retains pot Water/Ground/Coffee/derived Caffeine, a partially
filled cup and both native packet IDs/remaining grounds. Host drinking follows the
same acceptance path and does not activate the guest's effect. All 5,041 Net tests
and 18 launcher tests pass; Core/probe and both Net targets build with zero warnings
or errors. The last source-only cleanup aligns one foreach indentation; runtime
behavior is unchanged from the tested payload.

The save fixture initially expected the guest to remain in GAME. Vanilla returns
to MainMenu even though GuestSaveGuard prevents saving; corrected acceptance checks
all 15 guest profile files against their baseline. The failed fixture assertion
is retained beside the corrected checks. Earlier diagnostic runs are retained
separately and are not counted as successful acceptance runs.

Task closure: the bounded household coffee journey is implemented and checked.
H10 stays Partial for vendor coffee. Next is W02 train movement/collision/reset
synchronization, a different gameplay area still wholly missing from shared authority.

Final integrity: all 18 protected personal save/installed-plugin files and all 15
disposable guest profile files match their baselines. Both games are closed; the
probe DLL, sandbox marker and command bus have been removed from the copied game.
One-off drivers are archived under `build/coffee-audit/drivers/`; copy them back to
`tools/` before rerunning because their repository-root resolution is relative.

Each accepted role/run still logs six native NullReferenceExceptions and two
native ES2Auto mesh-save IndexOutOfRangeExceptions. No coffee subsystem disable
or coffee restoration warning occurs. This is scoped acceptance, not a globally
clean-log claim. `validation-summary.json`, `profile-integrity.json` and
`payload-verification.json` contain the exact checks and fingerprints.


## Shared train and collision lifecycle (protocol 251, unreleased)

W02 now has a host-authoritative adapter for the one native train. The host keeps
Move/Reset, tunnel volume, light switching and both whistle triggers. Guests pause
those six controllers and follow absolute host phase, pose, collider mask, light
flags and horn counter. Movement is sent at 20 Hz, with reliable lifecycle changes,
two-second recovery states and full/FSM-group/targeted snapshots. Guest prediction
stops at 0.2 seconds; collision shapes and audio disable after one second without
fresh state. Queued packets can extend the observed interval from the sender's
stop command, so the native stale check allows them to drain.

The guest body remains **dynamic**, preserves the native track constraints, and is
positioned in physics updates. Native player collision/death handling stays local;
ordinary rigidbody contacts must not be mistaken for the local player. Teardown
restores the original parent, Transform and Rigidbody pose, physics, active flags,
colliders, audio and controllers. Cold starts use the native initial leg: the
train has no native save fields; reconnect instead takes the current host leg.

Native audit of installed build 23268598 found one 100,000-mass Rigidbody, eleven
BoxColliders, four route phases, two horn sources, tunnel volume and night lights.
The moving body reparents between SpawnEast and SpawnWest. Delay/Delay2 hide Mesh
but leave the root Coll active. Read-only extraction and development evidence are
in `build/train-audit/`, including `native-scene.json`, `native-physics.json` and
`flow.txt`. No new train save format is introduced.

Tests caught and fixed three substantive errors: an ambiguous TRAIN name lookup,
restoring only Rigidbody pose while the Transform retained the other route, and a
kinematic guest body that could collide with test objects but could not produce the
native player collision event. The final body also retains native constraints to
prevent ground collisions lifting it off the host's track. Prior failed runs remain
in the audit directory; a missing probe player reference and short collision wait
were test-driver problems, also retained rather than represented as passing runs.

**Final evidence:** `verified/final-checks.json` records **20 controlled native
checks** on the final production payload, with hashes in `verified/payload-hashes.json`:

- Both native bindings, six paused guest controllers and dynamic collision bodies;
  continuous motion, both waits, both spawn parents and matching track height.
- Horn-counter presentation and replay rejection; stale hazard/audio withdrawal;
  targeted and full-world recovery; disconnect/restoration and rejoin while the
  train is under SpawnWest, without replaying earlier horn counters.
- Actual unrelated dynamic-body contacts on each role reach the native comparison
  without killing a player. Actual guest CharacterController contact produces
  native train death; the host remains alive and marks only the guest dead.
- Host native player-comparison entry also produces train death and reaches the
  guest. The host roof/drop placement fixture did **not** produce physical player
  contact; that negative result is retained in `verified/host-contact.json`.

Release builds of Core, the probe and both Net targets pass without warnings or
errors. **5,051 Net tests and 18 launcher tests pass.** All 18 protected personal
save/installed-mod files and all 15 guest-profile files (14 original files plus
the sandbox marker) remain unchanged. The disposable host's permadeath flag was
set false for native death checks. Both copied games closed normally; the probe,
command bus and sandbox marker were removed. Temporary runners are archived under
`build/train-audit/drivers/`.

These are controlled placement/state-entry tests over local UDP, not a physical
walking/driving session. Audible horn playback, real crossing approaches on both
roles, car/train crash consequences, complete death/respawn cycles, long travel and
Steam/two-PC acceptance remain open. The native logs still contain the pre-existing
six NullReferenceException and two ES2 IndexOutOfRangeException occurrences per
role, plus disposed-sound errors; there are no Train sync disabled errors in the
final run. This is not a globally clean-log claim.

W02 is Candidate. The scope inventory now has 25 Candidate, 54 Partial, zero Missing
and three Review rows; the many Partial rows still contain absent gameplay. Next
is I05 light-bulb-box opening and shared persistent bulbs, rotating from this
closed world-hazard slice to an ordinary supply gap. R20 battery boxes remain in
the same backlog; further train polish does not displace those missing actions.
Protocol/compatibility are 251, message 248 is TrainState, next ID 249. Mod remains
0.1.33 unreleased; nothing was committed, pushed or published.


### Light-bulb boxes and shared contents (protocol 252, unreleased)

2026-09-14, installed native build23268598, copied game only. The selected I05
bulb-box/loose-content slice now has **21 controlled native assertions**, retained
in `build/bulb-audit/acceptance.json`. Runs `opening-v252` and `cold-v252` use the
same Core/Net/catalog payload; payload hashes are retained with each run and
compared to the final build/catalog. `initial-v252` caught a probe reflection error
(reading a property as a field); the diagnostic was corrected before acceptance.
No production runtime change was needed after the first native binding check.

Verified outcomes:

- Host and guest native box opening produce one shared bulb and matching Empty
  boxes. The host records the exact native factory output and settled Wear.
- Host condition correction reaches the guest; an older cached state cannot undo it.
  The native guest hand can pick up the shared loose bulb.
- Guest shop selection/payment spends 27.95 MK once (691.05 → 663.10). Picking up
  and opening that shared bag yields one `lightbulbbox03`; host and guest use the
  same persistent package identity. A first unheld bag attempt required retry
  after the bag changed; it did not duplicate the contents.
- A fresh authenticated request opens the purchased box once. Duplicate and empty
  requests do not create another bulb. The earlier deliberately different request
  token was rejected as designed; rejoining reset the per-player opening ledger
  before the explicit packet replay fixture.
- Rejoin reconstructs the surviving bulb at Wear=41.25 and preserves consumed vs.
  unopened boxes. Host removal followed by cached-state replay cannot resurrect
  a retired bulb. The guest's separate native bulb remains hidden at its original
  Wear=95.65588; disconnect removes replicas and restores that original and its FSM.
- Host native save reaches MainMenu. A full cold reload retains only unopened
  `lightbulbbox04` with its same ID/Quantity=1 on both peers, keeps all consumed
  boxes gone, and retains the single purchase charge. Loose bulbs are absent:
  **vanilla has no loose-bulb save routine**. This is intentional native behavior,
  distinct from reconnecting to the still-running host.

These are opt-in native FSM/packet fixtures in two real game processes. They do
not establish normal mouse aiming, simultaneous physical pickup contention,
installed-bulb fitting/removal, vehicle lights, every mixed shopping bag, Steam/
two-PC use or long-session performance. Existing unrelated native warnings and
engine-protection logs remain in the retained logs; this is not a claim of an
error-free whole-game run. No bulb/package adapter was disabled in acceptance.

Validation: Core + probe build against the installed game references with zero
warnings/errors; Net builds for net35/netstandard2.0; **5,063 Net tests and 18
launcher tests pass**. Twelve added cases cover wire/truncation, invalid values,
revision/replay, native box identity/opening policy and malformed catalog profiles.
Package inventory expectations now count 33 profiles; replacement part families
remain unchanged. Protocol/version manifest is 252; mod stays 0.1.33 unreleased.

All 18 protected personal save/installed-mod files and all 15 guest-profile files
remain byte-identical. Both copied game processes closed. The copied-game probe,
sandbox marker and command bus were removed; temporary drivers are archived under
`build/bulb-audit/drivers/`. No commit, push or release was made.

I05 remains Partial for R20 battery boxes. The project reassessment selects **J09
advert delivery** next: verify current native reachability, then exact shared
mailbox completion, remaining sheets and native saved progress. This addresses a
missing job action after finishing the selected supply task.


### Advert delivery and native payday (protocol 253, unreleased)

2026-09-14, native build23268598, copied game and marked disposable profiles only.
The selected J09 delivery/payout slice now shares the finite pile, native sheet
outputs, exact mailbox results, day resets, pay and native persistence. The final
production payload is recorded in `build/advert-audit/available-v253`,
`available-cold-v253` and `available-paid-cold-v253`. Each run retains command
responses, logs, save copies, named acceptance assertions and payload hashes.
The three final runs pass **42 native assertions (40 distinct checks)** on matching
Core/Net/catalog/probe payloads. The combined list and final verification are in
`build/advert-audit/acceptance.json` and `validation.json`.

Native findings and implemented behavior:

- `JOBS/ADs::Data` owns 28 saved flags but only 27 scene mailbox indices exist;
  index22 has no target. Duplicate literal paths are disambiguated by BoxIndex.
  The adapter checks exact path/index/database bindings and action signatures.
- Both players can extract from the same 30-sheet pile. Host Use.Open decrements
  once and its exact CreateObject output becomes one shared `advert(Clone)`.
  That prefab has no FSM/save identity. Native hand ownership rejects a second
  holder, and native hand release clears a sheet before its destruction.
- The host reserves one owned nearby sheet for native mailbox Open/Close, which
  destroys it, increments Delivered and writes the exact saved flag. Guests run
  only the hatch presentation. Retired-sheet replay, stale revisions, reused
  sequences, spoofed actors, unowned sheets and absent mailbox22 cannot change
  the ledger. Successful snapshots never replay native pay or delivery accounting.
- Mailbox roots beneath house/store LOD temporarily move outside that local-camera
  controller, keeping their world poses. A guest-only visit to HouseShit3/box2
  exposed the old failure: the guest mailbox was active while the host's was not.
  The final build keeps all 27 targets available and accepts that delivery while
  the host is elsewhere. House/NPC LOD remains native; mailbox parents restore at
  teardown. Generic WaitAd/pile replay and older classified-job scalar application
  are superseded by the dedicated adapter.
- Rejoin retains the 27-sheet partial pile, completed indices2/24 (mask16777220)
  and exactly one unused loose sheet. Cached retired sheets remain gone. Guest
  teardown restores its own native job writer, original saved fields/list and
  clears a held replica. Cold reload retains the partial pile and completed flags;
  loose sheets disappear because vanilla does not save them.
- Friday's native Calc pay 2 credits two deliveries at 17 MK each, clears Delivered
  and writes one new `Jakopalkkio|34.00+` bank entry. Repeated native payday with no
  work does not add another entry. All 30 accepted extractions exhaust the pile;
  further attempts create nothing. Native reset replenishes it without extra pay.
  Host save reaches MainMenu, and the next cold load verifies the settled bank
  ledger, zero Delivered, reset mailbox list and full pile.

These are opt-in native state-entry, placement and packet fixtures over local UDP.
The native phone-number graph `08231206::Data` sends JOB on CALLED, establishing
the host enrolment route by code audit. The fixture enters New job directly and
advances GlobalDay; it does not exercise ordinary telephone input or wait through
the native 600-second enrolment delay. **Shared guest outbound enrolment is still
missing**: PhoneSync handles incoming calls, with no guest outbound request path.
J09 therefore stays Partial. Normal mouse aiming, full delivery routes, natural
calendar scheduling, hatch audio/visual quality and Steam/two-PC acceptance remain
open. This is not an ordinary full-game playtest or a claim that all jobs work.

Fixture corrections are retained rather than hidden: early binding passes exposed
asset templates and ScenePath sibling suffixes; a delivery pass exposed the native
hand remaining occupied after sheet destruction; the subsequent guest-only visit
exposed host-only house LOD. All production fixes preceded the final three runs.
A spoofed actor0 packet was rejected by serialization, so the host-authentication
fixture uses a different valid guest ID. Advancing the native calendar also pays
637.56 MK unemployment and 185.64 MK housing benefits and can charge 525 MK rent;
the final checks inspect native bank entries rather than attributing every balance
change to adverts. Four rapid extraction attempts were rejected while both nearby
peers claimed the pile: both still had 26 outputs plus four remaining. Moving the
guest away allowed the last four, preserving all 30 identities. No production
change was made to bypass ownership for that fixture.

Validation: Core and the opt-in probe build against the installed game DLLs with
zero warnings/errors; Net builds for net35/netstandard2.0; **5,077 Net tests and
18 launcher tests pass**. Fourteen new cases cover all three wire layouts and
truncations, finite values, native flags/stages, revision rules, sender/channel
authority and isolated catalog failure. Protocol and compatibility manifest are
253, IDs250–252 are used, next free ID253; mod remains 0.1.33 unreleased.

All 18 protected personal save/installed-mod files and all 15 guest native-profile
files remain byte-identical. Both copied games are closed; the probe DLL, command
bus and copied-game marker are removed. Temporary runners/check drivers are
archived under `build/advert-audit/drivers/`. Final logs contain no advert adapter
disable/restore errors. Unrelated native exceptions and disposed-sound errors
remain in the retained logs; this is not a globally clean-log claim.

The broader scope remains 25 Candidate / 54 Partial / 0 Missing / 3 Review. The
delivery/payout slice is closed; guest outbound enrolment remains a shared phone
dependency. Next is **I12 Corris engine-oil refill**, one bounded bottle-to-engine
transfer, rotating from jobs to missing ordinary maintenance. No commit, push or
release was requested or made.

### Motor-oil containers (protocol 254, unreleased)

Scope: the required container foundation for I12 engine-oil refilling. This
implements all three separately sold oil grades, host saved identities, material/
viscosity, remaining fluid and emptiness, exclusive carrying, guest purchase and
reconnect. **The engine-oil cap and conserved guest bottle-to-pan transfer are
still absent.** Guest replica pour colliders are disabled until that dependency is
implemented; native host source simulation is unchanged. I04/I12 remain Partial.

Native source: installed build23268598, level2 and sharedassets3 extracts under
`build/motoroil-audit/`. The factory prefix really is `motormoil1`; IDs append a
positive counter (`motormoil11`, etc.). The three shop MOil1/2/3 wrappers select
Type0/1/2. Native grade materials/viscosities were observed as motoroil1/.5,
motoroil2/.6, motoroil3/.7. Runtime reads native materials and host viscosity,
rather than treating these observed values as permanent constants. Four-litre
bottles save transform/Type/Fluid. Empty bottles retain their native save identity;
native garbage deletion removes it on saving.

Controlled native checks use two copies of the actual game process, the copied
`build/local2p/game` installation, marked disposable host/guest Wine profiles,
loopback transport and the opt-in `WINTERMP_LOCAL2P_MOTOROIL_TEST=1` probe. Final
production payload hashes, commands, replies, logs and saved outputs are in
`build/motoroil-audit/accepted-v254` and `accepted-cold-v254`. The persistent guest
fixture starts with no saved motor oil; a disconnected runtime-only factory
fixture creates a grade2 bottle with the same native ID as the host's grade0
partial bottle, specifically to exercise preservation and collision isolation.

Twenty-one final-build native checks cover the journey and its cold reload:

- Cold recovery of five prior saved bottles, with matching grade/material/viscosity,
  a 1.25-litre source, an empty bottle and a previously discarded ID remaining absent.
- A controlled host source-quantity change to .75 litres reaches the guest; replay
  of the older 1.25-litre descriptor cannot refill it. This fixture changes native
  source/root Fluid and uses native empty-state processing; it is **not a pour test**.
- Guest native hand pickup obtains ownership, a second host pickup is refused,
  and disconnect releases the held replica and removes shared copies.
- A guest's own grade2/four-litre bottle with the same native ID stays inactive
  during the session. Rejoin uses the host's grade0/.75-litre state. Disconnect
  restores the original ID, grade, quantity, native scripts and activity.
- Guest native shop selection and checkout create exactly three additional host
  bottles, one per grade, reconstructed once on both peers. Native factory/startup
  and the existing five-second item-discovery scan run before publication. An
  initial three-second test observation was too early; the retained
  `acceptance-before-discovery.json` records that timing failure. A later observation
  confirms all outputs without another checkout. This does not claim instant spawning.
- Host native GARBAGE retires a bottle; replay cannot revive it. Native host save
  completes to MainMenu. The final cold run checks all seven remaining identities,
  the .75-litre source, empty state, newly purchased grades and both discarded IDs.

The guest-original check caught and fixed a real restoration ordering error:
restoring RestartOnEnable while the object was inactive made Use restart when the
object became active, overwriting its ID. Originals now reactivate while native
FSMs remain suppressed, then resume with their previous flags. Generic saved-product
replay also excludes these dedicated bottle descriptors. The first exploratory
retirement observation hit a Unity destroyed-object reference in the developer
probe; the probe was fixed before final-build acceptance. No oil adapter-disable
or restoration failure is accepted in the final logs. Other existing native
startup/engine-protection diagnostics remain outside this bounded oil check.

Validation: protocol tests **5,091 passed**, launcher tests **18 passed**; both Net
frameworks and the game-dependent Core/probe compile with zero warnings/errors.
`acceptance.json`, `profile-integrity.json`, `payload-verification.json` and
`validation.json` retain final counts and hashes. Eighteen personal-save/installed
mod paths and all fifteen disposable guest baseline files remain unchanged. Test
processes, copied-game probe, bus and sandbox marker are removed afterward; drivers
are archived under `build/motoroil-audit/drivers/`. No release, deployment to the
installed game, commit or push was performed.

Next required dependency: the cylinder-head rocker-cover Screw cap and its
CapTrigger_MotorOil must resolve the actually attached oilpan. Native fill consumes
.1 litres/second and adds the same amount to pan Oil up to 3.7 litres, reduces
OilContamination by 3.2/second, and moves OilViscosity toward the bottle at .06/second.
The mounted Data proxy and persistent oilpan fields use different names: Oil /
OilContamination / OilViscosity versus OilLevel / OilDirt / OilViscosity. Implement
and verify their paired update and cap access before calling I12's refill journey
complete. Ordinary mouse use, tilted pouring, engine filling, Steam/two-PC and
other separately sold supplies remain unverified or unimplemented as stated.


## Corris engine-oil refill (protocol 255, unreleased)

The selected I12 journey adds shared cap access and host-conserved transfer from
a native motor-oil bottle into the **actual installed Corris head/rocker cover and
oilpan**. Guest intents carry the current target epoch and serial sequence; the
host requires an authenticated living nearby actor, approved bottle ownership,
fresh owner pose, open cap, native bottle tilt and overlapping pour colliders.
Removing/replacing the target invalidates outstanding requests. Both players
receive cap pose/rotation, oil quantity, contamination and viscosity; the pourer
gets the local native gauge/sound. Guest personal parts and saved oil stay protected.

The source is limited to four litres and the pan to 3.7. Transfer uses .1 L/s,
with .25 seconds maximum accounted per frame; the actual transferred amount
determines native cleaning (3.2/s) and viscosity movement (.06/s toward the bottle).
Each update writes both source/root Fluid and mounted/saved pan scalars before
empty processing. The pan's one-second native mirror is insufficient for immediate
saving. Repeated save/cold-load testing resurrected an empty bottle, then exposed
a nearby partial bottle changing from its loaded quantity to four litres. The host's
Save getter now retains root Fluid after Empty destroys Trigger; this protection
alone did not close the regression. Native save actions/tags remain intact.
The nearby-bottle startup race was then reproduced: child Data could copy
its default four litres and send GLOBALEVENT during the parent's one-second Load
wait, before Get data/Load2/State3. The early host hook pauses that child and gates
its event until the native root/source handoff finishes. It also restores correct
native grade/material/viscosity initialization. Source initialization happens before
the loaded values are handed across, and world-discovery reset cannot cancel the
in-flight protection. Native file reads remain native.

Native evidence is retained in `build/oil-refill-audit/`, with source baseline,
protocol/unit-test logs, native asset extracts, command/reply transcripts and
production payload hashes. Tests run two actual game processes from the copied
`build/local2p/game` under marked disposable Wine profiles and local UDP. They use
`WINTERMP_LOCAL2P_OIL_REFILL_TEST=1` alongside the existing motor-oil/persistence
probe flags. The driver fits the block/head/cover/pan through native mount states,
seeds mount tightness and test starting oil, then uses native hand pickup and cap
state entries. Bottle poses are pinned over the real trigger for controlled pours;
this is not an ordinary mouse/hand playtest. Final runs close the actual native
drain plug through its Tight? action; they do not disable production oil leakage.

The exploratory fixture had an open drain plug. Its Bolts/Oil/Calc subtracts
.2 L/s independently of refilling, so the pan initially lost oil during a valid
.1 L/s pour. An intermediate run isolated that drain; final acceptance uses the
natively tightened/saved plug. The small native pan-wear seep remains active.
A disconnected guest has no personal rocker cover: restoring its cap correctly
leaves the component enabled, rotation359 and its own inactive hierarchy. The
first fixture assertion incorrectly expected the cap to be active and started;
its retained failure and corrected native inspection are distinguished from a
production failure. The guest also may receive automatic spawn rather than a
returning-player prompt; drivers now handle either.

**45 controlled native checks pass** on one final production payload: 30 journey
checks, seven first-cold/resave checks and eight second-cold checks. Runs are
`guarded-journey-v255`, `guarded-resave-v255` and `guarded-final-cold-v255`; their
Core, Net, catalog, compatibility and probe hashes match the final files. The
second cold run retains the refilled pan's three saved values, native plug8,
closed cap359, the partial source, both old/new empty bottles and matching guest
reconstruction. Its first guest observation preceded bottle discovery; the retained
log records that harness failure, and later command replies complete the two
remaining assertions. Drivers now wait for both peers' bottle descriptors.

Validation record and final counts are in `validation.json` and the final native
`accepted-checks/checks.json`. All 18 protected installed-mod/personal-save paths
and 15 disposable guest baseline files match their original hashes. Native game
processes, copied-game probe, command bus and sandbox marker are removed; one-off
drivers are archived under `build/oil-refill-audit/drivers/`. No release, deployment
to the installed game, commit or push was performed. Core and the probe build
against installed build23268598. Both Net frameworks build
cleanly; **5,102 protocol tests and 18 launcher tests pass**. This is protocol255
on local mod0.1.33, not a packaged release.

Scope limits: grade2/.7 is the controlled pour source; all three container grades
have the earlier container tests, not three separate refill playtests. Detached
engines, complete physical assembly/bolt work, mouse positioning, moving/driving
refills, long latency/loss and Steam/two-PC remain unverified or outside this
Corris adapter. Other I12 coolant/brake-fluid/water/charcoal transfers remain open.
The selected saved refill journey is closed. The next bounded implementation
rotates to guest outgoing telephone enrolment for J09 advert delivery.


## Advert telephone enrolment (protocol 256, unreleased)

This closes the missing guest start action for the J09 advert route. The only
new supported number is 08231206. Native keypad number events and ringing lead
to an approval state, and one host reservation arbitrates the apartment, old
house and taxi phones. Host listing/job availability, current player position,
paid/plugged household service and line availability are required. Guest native
Call billing and Hangup2/CALLED writes are bypassed only for this number. The
caller plays the native 72-second speech/subtitle. The host adds one connection
and .2 native usage units per scaled second for household calls; carphone calls
have no native household charge. Early hangup or disconnect retains those charges
but does not enrol. The full host duration dispatches listing CALLED → job JOB;
existing host tags save number Stage 3 and job Stage 1. Existing AdvertJobState shares
the result. Vanilla retains its initial 600-second wait and subsequent schedule.

Implementation: `AdvertPhoneSync`, `AdvertPhoneBinding`, catalog `advertPhone`,
`AdvertCallLedger`, intent 256/result 257 (9-byte packets). Host replies are deferred
until PlayMaker has entered the approval state: immediate local replies previously
left the host stuck awaiting approval. The native old-house path contains two
UseHandle FSMs; the outgoing binding selects the one referencing the exact keypad.
A near-complete native Wait may arrive a fraction before the host's duration; the
last-second result waits for the host's complete 72 seconds, without shortening the
conversation. Earlier completion requests are rejected. Real-time leases, one
second heartbeats, serial Begin IDs, sender checks and native host job/listing
state prevent retries or competing callers from starting it twice.

Controlled test artifacts are in `build/advert-phone-audit/` (ignored). The
`integrated-v256` journey exercises actual native number events, accepted guest
calls, shared usage, duplicate Begin, unpaid/unplugged service, distance,
impersonation, premature completion, early hangup, service loss, host competition,
other phones and reconnect. Probe fixtures teleport players/activate phone roots,
prepare a waiting native advert job between independent phone cases and enter
native handle/call states; these are not manual keyboard/mouse acceptance tests.
The first complete guest call uses individual NUMBER events and native ringing;
subsequent focused cases enter the audited answering state to omit random rings.
Every successful enrolment still waits through the full speech duration.

Exploratory runs remain distinct: `discovery-v256` exposed the duplicate native
handle, `journey-v256` exposed the synchronous host-reply race, and later runs
isolated startup-choice timing and the completion-clock edge. `verified-v256`
then exposed the taxi presenter blocking the idle keypad; the integrated run
checks both outgoing use and incoming taxi answer/hangup after that correction. The harness now
waits for the returning guest's spawn choice and for its host-observed position
to settle before dialing. Early readiness/teleport assertions from those runs
are retained as failed exploratory evidence, not counted as accepted checks.
The final `validation.json` records accepted run/check totals, build/test results,
payload hashes, protected-save comparison and cleanup. The accepted runs have
**30 passing native assertions**: 24 in the journey and 6 in the separate cold
run. The cold run checks saved enrolment, used-number removal, shared
reconstruction, both bills, retry rejection and guest teardown. Core/probe build against native build 23268598 with no warnings/errors;
**5,113 protocol tests and 18 launcher tests pass**.

Limits: no manual audio listening, ordinary aiming/keyboard acceptance, full
27-mailbox route, ten-minute initial wait/whole scheduled week, injected latency
or Steam/two-PC test was performed for this task. Older v253 evidence covers
individual mailbox delivery, day reset/pay and saved progress. The local phone
listing/sign may still reflect the guest's own save; outgoing advert eligibility
uses the host. W04 incoming-call arbitration and other outgoing numbers remain
separate, and unfinished calls restart after reconnect. Personal saves and the
installed mod are outside the test profiles. This is local 0.1.33/protocol 256,
with no commit, push, installed deployment or release. All 18 protected installed/
personal paths and 15 disposable guest save files retained their baseline hashes.
Both accepted runs used the final five payloads. Test processes, copied-game
probe, marker and command bus are removed; one-off drivers are archived under
`build/advert-phone-audit/drivers/`.

J09 advances to Candidate under the broad experimental-alpha inventory. After
this bounded job task, the next selected implementation is I05 R20 battery boxes
and loose contents. Battery fitting/appliance operation (H12) remains a separate
native audit; the backlog still includes other missing ordinary gameplay.

The v256 carphone integration also restores idle local keypad/hand presentation
without guest writes to native taxi Answer/Occupied. Incoming taxi calls retain
the existing authenticated answer/hangup path and close the outgoing keypad;
loss of taxi-phone availability cancels outgoing use and releases local movement.
This resolves the earlier taxi presenter unconditionally hiding the keypad.

### R20 battery boxes and persistent cells (protocol 257, unreleased)

The I05 R20 contents slice now uses the existing package and ordinary-supply
adapters. Native build 23268598 has a four-cell `r20batterybox0` and persistent
`r20battery0N` outputs, both on `Spawner/CreateItems`. The loose cell's Use FSM
has no charge scalar. Its Consumed flag deletes its save tag, whereas fitted
fuses need their separate Destroy flag. `supplyContents.retirementVariable`
selects these two validated native layouts; no wire fields or message IDs change.
Protocol 257 prevents earlier peers joining without the new contents semantics.

Fresh installed-asset evidence is retained in `build/r20-audit/native-scene.json`
and `native-prefabs.json`, including source hashes. This describes asset defaults;
the separate native runs below test live and saved state. Package opening keeps
the existing host reservation, actor/range/ownership checks, exact output capture,
quantity revisions and replay receipts. Guest cells skip local load/save; guest
originals are hidden during co-op and restored on teardown.

**32 controlled native assertions pass**: 23 in `opening-v257` and 9 in
`cold-v257`, under `build/r20-audit/`. Both runs use the same final Core, Net,
catalog, compatibility file and probe; `payload-verification.json` compares their
hashes with the current outputs. The accepted checks and command/reply references
are retained in each run's `checks.json` and combined in `accepted-checks.json`.

- Host opening and three guest extractions yield the same four distinct native
  batteries and leave the shared box empty. Native guest hand pickup succeeds;
  a competing host pickup settles with only one holder.
- Guest purchase charges 34.50 MK once (663.10 → 628.60), creates a shared bag,
  and unpacking yields one four-cell box. That purchased box opens through the
  same authenticated host path. Repeated and stale opening requests do not
  produce additional cells.
- Host native garbage disposes of the exact cell. A cached state cannot restore
  it. Rejoin reconstructs live cells and empty/partial/unopened box quantities.
  Disconnect removes replicas and restores the guest's separate test cell.
- Host native SAVEGAME reaches MainMenu. Cold reload preserves three boxes
  (quantities 3, 3 and 4), five loose batteries with their exact previous IDs,
  and the single purchase charge. The consumed box and disposed cell stay gone.
  A subsequent guest opening advances the saved native counter, and the native
  hand can pick up a restored cell. Cold teardown does not invent local cells.

These are opt-in native state/packet fixtures in two copied game processes with
marked disposable profiles. They teleport players, invoke audited native states
and stabilize needs. They do not establish ordinary aiming/mouse controls,
physical garbage-bin contacts, guest disposal, battery fitting/appliance use,
mixed-product bag combinations, injected loss/latency or Steam/two-PC acceptance.
Appliance charge and installation remain H12 work. Existing unrelated native
fixture warnings remain in the logs; neither accepted run logged an R20 supply,
package-opening or FSM-hook failure.

Core/probe build against the installed game references succeeds with zero
warnings/errors; Net builds for net35/netstandard2.0. **5,117 Net tests and 18
launcher tests pass**. Four new test cases cover the four-cell identity/opening/
retirement path and invalid retirement profiles. Existing package inventory
expectations now count 34 profiles. The initial test run exposed stale counts
and a capacity expectation; those failures are retained in `net-tests.log`, with
the passing rerun in `net-tests-final.log`.

All 18 protected installed/personal files and all 15 disposable guest baseline
files retain their original hashes. Both game processes closed cleanly; the
copied-game probe, marker and command bus were removed. One-off drivers are
archived in `build/r20-audit/drivers/`; `validation.json` records the final checks,
payloads, integrity and cleanup. Mod stays 0.1.33 unreleased. No commit, push,
installed deployment or release was made.

I05 advances to Candidate for its three named contents families. After reviewing
missing gameplay, prior bag/stutter reports, reliability, usability and validation
gaps, the next selected task is V11 guest window scraping. Start with the native
tool/contact and frost-ownership audit, so either player can clear the same iced
vehicle. Other supplies, battery appliances and further soak tests remain queued.
