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
handoffs. The Core checksum reads the same live damage/condition values as snapshots
without advancing send sequences or change baselines. This does not verify native FSM
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
   save/reload disposable saves. Confirm the existing vehicle damage authority path
   still carries driving wear and the host keeps the correct final condition.

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

Mixed bags containing native part products without a replica adapter (including
fanbelts and oil filters) are rejected before inventory mutation. Catalogued Fleetari
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
