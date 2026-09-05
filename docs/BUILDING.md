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

The current test release is 0.1.31, protocol 94, targeting game build 23268598.
On Linux, build and verify a separate kit without deploying into the game or publishing:

```bash
python3 tools/build-test-release.py --appimage \
  --inno-prefix /path/to/dedicated-inno-wine-prefix \
  --cosmocc /path/to/cosmocc/bin/cosmocc
```

The prefix must already contain `drive_c/inno/ISCC.exe` (see the Wine setup below).
The default run rebuilds the plugins, runs both test suites and the evidence-tool tests,
then publishes both standalone launchers. `--skip-build --skip-tests` reuses the versioned
publish folders and requires passing saved TRX reports. It still verifies payload bytes,
versions, archives and packaged documentation. Only reuse after confirming source/build
consistency. Output is `dist/test-v0.1.31/`, separate from stable release artifacts.

The kit includes source, release notes, a tester checklist, validation results and SHA-256
checksums. Each payload manifest contains hashes for the mod files; the launcher verifies
them and reads the protocol constant directly from the networking DLL without loading it.
`targetGameBuildIds` records the build used for compilation/binding checks;
`testedGameBuildIds` stays empty until multiplayer testing is complete.
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

### VideoPoker verification (v93)

`PokerTests` exercises all 2,598,960 five-card combinations, held-card replacement,
private doubling, payment receipts, disconnects and teardown. `PokerSync` binds
native visual assets from the catalog's `videoPoker.assets` action references; the
asset extractor now resolves PlayMaker's shared material/texture object table.
Installed build 23268598 provides 44 visual assets and 12 physical input bindings.
Static evidence does not verify screen rendering or input timing in a running game.
Use the R1.3 two-player checklist in `COVERAGE-ROADMAP.md`, including an inactive host
LOD, leaving/rejoining mid-hand, and returning to singleplayer after ending a session.

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
