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

### Versioned tester kit

The current test release is 0.1.33, protocol 120, targeting game build 23268598.
On Linux, build and verify a separate kit without deploying into the game or publishing:

```bash
python3 tools/build-test-release.py --appimage \
  --inno-prefix /path/to/dedicated-inno-wine-prefix \
  --cosmocc /path/to/cosmocc/bin/cosmocc
```

The prefix must already contain `drive_c/inno/ISCC.exe` (see the Wine setup below).
The default run rebuilds the plugins, runs both test suites and the evidence-tool tests,
then publishes both standalone launchers. `--skip-build --skip-tests` reuses the versioned
publish folders and requires passing TRX reports from that version’s `build/test-release/v0.1.33/test-results/` folder. It still verifies payload bytes,
versions, archives and packaged documentation. Only reuse after confirming source/build
consistency. Output is `dist/test-v0.1.33/`, separate from stable release artifacts.

The kit includes source, release notes, a tester checklist, validation results and SHA-256
checksums. Each payload manifest contains hashes for the mod files; the launcher verifies
them and reads the protocol constant directly from the networking DLL without loading it.
`targetGameBuildIds` records the build used for compilation/binding checks;
`testedGameBuildIds` stays empty until multiplayer testing is complete.
The package builder records the actual number of evidence-tool tests and does not reuse
runtime smoke results from earlier releases. Runtime and multiplayer checks must be
reported separately for each new package.
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

## Verification history

Per-feature verification journals (what each local two-game run covered and
what stayed open) were removed from this file on 2026-09-22. Read them at the
[snapshot tag](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md) or with
`git show snapshot/hermes-2026-09-14:docs/BUILDING.md`. Other docs that say
"see BUILDING.md's vNNN checklist" mean that snapshot. Record new test results in
the PR/commit message, not here.
