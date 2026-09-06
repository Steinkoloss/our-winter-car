# COVERAGE-ROADMAP.md — closing every unsynced vanilla system

This is a **work queue for an AI agent**. It decomposes every shared-state gap found in
the 2026-07-21 full-coverage audit (all 8,615 game FSMs vs. the mod's sync surface) into
ordered, self-contained tasks. Work it **top to bottom**; each task is sized to one
branch / PR / session.

`PLAN.md` §4–5 remains the architecture + milestone source of truth. This doc is the
*detailed decomposition of its "gameplay long tail + M11 polish + parity backlog"* — the
concrete, per-system tasks PLAN doesn't spell out. When a task lands, tick it in the
**Progress** list and, if it changes a PLAN §4.4 status, update PLAN in the same PR.

---

## 0. How to use this (read once)

**Loop:** pick the first unchecked task → read its spec → implement following the named
existing pattern → build + test → tick it → PR. Do **not** batch unrelated tasks.

**Every task's Definition of Done (DoD):**
1. Follows the **host-authoritative** rule (guests send intents; host validates + broadcasts). Host always wins.
2. Wrapped for **crash containment** (`Util/SafeApp.cs`); a fault disables *that* subsystem, never the game.
3. If it touches the wire: **bump `ProtocolInfo.Version`** (`src/WinterMP.Net/Protocol.cs`), append the new id in the reserved range, and **update `protocol/PROTOCOL.md` in the same change** (see §5 discipline in `AGENTS.md`). Never reuse a retired id.
4. Protocol logic lands with a **round-trip test** in `WinterMP.Net.Tests` (the CI loop). `AllMessagesRoundTripTests` auto-covers any new `IMessage` — keep it green.
5. `dotnet build -c Release` of `WinterMP.Core` is clean (0 err) and `dotnet test src/WinterMP.Net.Tests` is green.
6. Add a `catalog/` rule instead of hardcoding when the interaction is a plain button/purchase.
7. Note the **2-player check** the task needs (most of this is not unit-verifiable — it feeds the playtest list).

**How-to references (don't re-derive):** adding a message / catalog rule / FSM hook →
`docs/AGENT-RECIPES.md`. Where a subsystem's code lives → `docs/CODEMAP.md`. Query the
game dump → `python3 tools/extract_fsm_details.py catalog/dump-23268598.json show:<path>`.

**Reuse these existing patterns — do not invent new ones:**

| Pattern | Use it for | Reference implementation |
|---|---|---|
| **Host scalar-state + guest intent** (State broadcast on change/join; Intent validated by fresh-pose proximity + monotonic per-player sequence) | gambling, bills, sauna, appliances | `HeatSourceSync` (`HeatSourceState`/`HeatSourceIntent`) |
| **Capture-and-pair before payment** (host captures the guest's exact generated record, pairs it to that player's next `PurchaseIntent(PAYMENT)`) | repair-shop order, any "configure then pay" flow | `MailOrderSync` (`OrderAMIS`/`OrderYP`) |
| **Host-validated record from guest observation** (guest reports a local outcome; host re-derives/validates, owns the durable record, broadcasts + retires it) | wanted level, race results, fines | `PoliceSync` (fines), `RallySync` (stage) |
| **Host-authoritative transform streaming** (host sims, streams pose over `NpcTransform`; guest freezes local AI while a stream is live) | moose corpse, crime NPCs, opponent cars | `NpcTrafficSync` (`ScriptedMover` + rigidbody paths) |
| **Anyone-triggers control / buy** (catalog rule replays a button/purchase reliably on the host) | simple switches, one-shot buys | `catalog/sync-catalog.json` `controls[]` / `buys[]` |
| **Runtime item spawn manifest** (host mints ids; peers materialize) | moose-meat, any `Spawner/*` output | `ItemWorldSync.Spawn` (`ItemSpawn`/`SpawnIntent`) |

**Recurring bug classes to check in ANY new sync subsystem** (each has burned us before):
- **A** — optimistic send-time dedup latch with no ack → lost on a transient host reject. Advance the baseline only on the host's accepting echo.
- **B** — a join-snapshot path mutating the periodic delta baseline → strands connected guests. Snapshot must pass `advanceBaseline:false`.
- **C** — a host-owned/observed record with no terminal-state broadcast → phantom on late-join, or never clears. Broadcast the retire/finish edge.
- **D** — `FindFsmFloat` vs `FindFsmInt` type mismatch → silent null bind kills the whole subsystem. Verify var *type* against the dump, not just the name.
- **E** — remote-apply write onto a locally-owned object (ownership contamination). Guard every game-state write with `!LocallyOwned` / owner check.
- **F** — sequence reset on a reused/path-derived id. Handle re-baseline (see `NpcTrafficSync` stale-run detection).

**Reserved protocol id ranges** (current version **v120**; allocate the next free id in-range):
`63–79` vehicles · `93–99` economy/appliances · `101–119` NPCs/jobs · `126–139` snapshot/bulk.
Ranges are tight — if a range fills, extend it in `IMessage.cs` and document it.
The economy overflow currently uses 160–183; 164–166 are the v92 slot ledger messages,
167–169 are v93 VideoPoker, 170–172 are v94 debt-letter quotes/payments, 173 is v97
Ventti property state, and 174 is v98 Ventti table observations (retiring 93).
175–177 are the v99 Ventti ledger/commands/receipts (retiring 94), and 178–179
are v100 Ventti NPC/furniture poses and live sound cues.
v101 appends rally report tokens/acknowledgments and per-player revisions to 86/87;
it allocates no new ids. v102 adds complete Lotto draw state 180, retiring incorrect 96.
v103 adds Lotto ticket requests/receipts/state 181–183.
v104 appends complete hockey betting collections to 160 without allocating a new id.
v105 strengthens item despawn/replay/resync semantics without adding fields or IDs.
v106 adds native trophy factory semantics to ItemSpawn flag bit 1 without new fields or IDs.
v107 appends full native part condition floats to 45/124 and changes part-report/CRC semantics.
v108 changes standard-package item identity/disposal semantics without adding fields or IDs.
v109 adds standard box identity/quantity/creation state 184.
v110 changes native part body/FSM identity semantics; no new fields or message IDs.
v111 adds loose replacement-part creation state 185 and echoes accepted removals to requesters.
v112 adds acknowledged box opening requests/receipts 186–187.
v113 corrects native part lifetime and the Installed semantics in 185; no new IDs.
v114 appends native parent tightness to 44/123 and makes bolt intents/results host-authoritative; no new IDs.
v115 appends parent identity/path and local pose/scale to replacement state 185; no new IDs.
v116 adds replacement fitting requests/receipts 188–189; next free ID is 190.
v117 appends install/remove operation to 188–189 and removal readiness to 185;
next free ID remains 190.
v118 appends SlotIndex to 188–189 for native piston/main-bearing/rocker installation;
next free ID remains 190.
v119 adds alternator adjustment operations to 188–189.
v120 adds shared bags 190–192 and retires guest SpawnIntent 53; next free ID is 193.

**Do NOT work on (verified out-of-scope — see §Appendix):** the in-game computer + its
fishing minigame, host migration, water wells/taps (per-player thirst), map clock/weather
(already synced), jukebox/CD-track/cloud-sprite (cosmetic), food spoilage (no decay FSM exists).

---

## 1. Progress

Tick when merged. Ordered by priority (shared-state corruption first).

> **The first pass ticked these 35 tasks, but later audits reopened incomplete work.**
> These tasks were the *2026-07-21 audit's* decomposition and remain largely
> **unplaytested**. A later re-audit found a whole unsynced background-economy
> cluster plus several subsystems that bind the wrong FSM variable and silently no-op.
> **Read §1b before concluding anything here is finished.**

**Phase 0 — Housekeeping (do first; cheap, unblocks others)**
- [x] 0.1 Correct the 4 `PLAN.md` coverage overstatements
- [x] 0.2 Fix the vehicle-registration `RequireRoot` gate (unblocks 2.4, 3.2) — structural `Simulation/Engine` check; adds only the taxi

**Phase 1 — Economy integrity** *(these corrupt the shared wallet / car ownership right now)*
- [x] 1.1 Gambling: pub & station slot machines — `GamblingSync` (164–166, v92), host ledger and seeded local reels; two-player verification pending
- [x] 1.2 Gambling: Ventti blackjack (incl. car wager) — `VenttiSync` reuses 93/94 (Kind=Ventti), host deals
- [x] 1.3 Electricity + phone bills → power cutoff / blackout — `UtilityBillState` (95, v58) + catalogued Pay buttons
- [ ] 1.4 Lotto draw (180, v102) and Lotto ticket purchases/claims (181–183, v103) implemented; two-player/save validation and Megaveto transactions remain incomplete (R2.21/R2.26).

**Phase 2 — Shared-car integrity** *(the project car is THE shared object)*
- [x] 2.1 Engine part-breakage RNG — `VehicleDamage` (64, v60) owner-authoritative mask + non-owner RNG suppression
- [x] 2.2 Drivetrain wear + tire pressure/puncture — `VehicleCondition` (65, v61) owner-authoritative
- [x] 2.3 Repair-shop (Fleetari) service results + order record — `RepairShopSync` (97/98, v62) capture-and-pair + record broadcast
- [x] 2.4 Register the taxi (MACHTWAGEN) as a vehicle — done by 0.2's structural `Simulation/Engine` check (no taxi-specific code)
- [x] 2.5 Gearbox / clutch state *(lower)* — `Gear` byte appended to `VehicleState` (id 60, v63)

**Phase 3 — Income & jobs** *(each currently credits only the local wallet)*
- [x] 3.1 Flea-market selling (rent table / price / timed sales) — `FleaSaleSync` (101/102, v64) host-owned table + Sell-RNG suppression
- [x] 3.2 Taxi job lifecycle *(dep 2.4)* — `TaxiJobState` (103, v65) host-owned lifecycle; taxi vehicle streams via 2.4
- [x] 3.3 Kilju: fermentation + selling to KiljuBuyer — `BrewState` (105, v66) tracked-item stream (mirrors FluidContainerState)
- [x] 3.4 Hitchhiker rides (+ dangerous variants) — `HitchhikerState` (106, v69) + ScriptedMover body streaming
- [x] 3.5 Farm job (hay / combine) — `JobSiteState` kind 4 (v67), extended JobSiteSync (int JobStage)
- [x] 3.6 Kela welfare / unemployment paperwork income — `WelfareState` (107, v68) host-owned claim/benefit

**Phase 4 — Crime & consequence**
- [x] 4.1 PlayerWanted crime counters + wanted level — `WantedState`/`CrimeReport` (140/141, v70) host-owned group wanted level
- [x] 4.2 Arrest (cop dispatch) + jail sentence *(dep 4.1)* — `JailState` (142, v71) host-owned countdown
- [x] 4.3 Vehicle impound / tow *(dep 4.2)* — no new protocol: host-driven relocation rides the existing ItemTransform/VehicleState reconciliation (no distinct impound FSM exists)
- [x] 4.4 Police chase/pursuit trigger + DUI arrest path *(dep 4.1)* — `PursuitState` (143, v72) chase/siren flags; cop cars NPC-streamed

**Phase 5 — Racing completeness**
- [x] 5.1 Rally ResultsWeekend scoring/reward + RegisterRally enroll + ParcFerme — `RallyResultsState` (150, v73)
- [x] 5.2 Rally PartsSalesman vendor — catalog `buys[]` entry (Purchase/Money flow); spawned parts via 7.2
- [x] 5.3 JOKKIS banger-race lifecycle — `JokkisRaceState` (151, v74) host-authoritative (avoids risky IceRaceSync refactor)

**Phase 6 — Home & survival depth**
- [x] 6.1 Oven/stove cooking + fire hazard — `ApplianceState` (99, v75) host-owned hotplate heats + fire + fuse
- [x] 6.2 Electric home sauna — added to `HeatSourceSync` registration (no protocol; reuses HeatSourceState)
- [x] 6.3 Incoming phone-call events — `PhoneCallEvent` (108, v76) host-decided ring topic
- [x] 6.4 Fridge chilling · per-appliance consumption · individual fuses *(dep 1.3)* — folded into 6.1 (ApplianceState fuse flag) + 1.3 (UtilityBillState consumption/blackout); fridge chilling has no time-based decay FSM (see Appendix), so nothing to sync there

**Phase 7 — World & wildlife**
- [x] 7.1 Moose death → corpse → meat spawn chain — `NpcTransform` FlagDead (v77) death edge; meat spawn = 7.2 SPAWNITEM residual
- [x] 7.2 Spawner manifest completeness audit — 14 subroots audited in ItemWorldSync.Spawn; bag ones covered, SPAWNITEM ones excluded-with-reason (late-join covered; real-time hook deferred)
- [x] 7.3 Crime/reaction NPCs (Reijo janitor, pub fighter) — Reijo registered as ScriptedMover (pose+AI freeze, no protocol); fighter already HUMANS/-streamed

**Phase 8 — Player polish**
- [x] 8.1 PlayerAlco (BAC) reconnect persistence — extended PlayerNeedsReport+GuestSpawn (v78) mirroring the Dirtiness add across 5 files
- [x] 8.2 Winter jacket / coverall worn state — extended PlayerClothingState (v79) with WinterGarment byte
- [x] 8.3 Yard piss-stains (persistent world marks) — `PissAreaState` (109, v80) host-owned 5 stain scales
- [x] 8.4 In-car radio / CD power+channel *(cosmetic-adjacent; lowest)* — `CarRadioState` (66, v80) host-owned channel/volume; the station half was a silent no-op until **v81** (see §1b R2.5)

---

## 1b. Round 2 — what the v80 "complete" claim missed

Every box in §1 is ticked, but a 2026-07-22 re-audit (and a 2026-07-24 dump-verification
pass) showed §1 is **not** the same thing as "every shared-state gap is closed". The tasks
above decomposed *interactive* systems; they missed the passive daily-tick `Systems/*`
economy, and several shipped subsystems bind the wrong FSM variable and silently no-op.

Work this list the same way as §1. **Priority order — the top group corrupts shared state.**

**Local verification, 2026-09-05 (0.1.31 tester package, v94):** 335 protocol/catalog tests,
18 launcher tests and 5 evidence-tool tests pass. Net (both targets), Core, Tools,
FastBoot and the launcher build against the installed game DLLs. Static extraction
of build 23268598 verifies the configured economy globals, all five ATM mutations
and 14 concrete damage mappings. Both slot machines' seven control bindings, typed
variables, weighted reels and all 729 payout combinations match installed action data.
VideoPoker’s 44 native visual assets, 12 input hooks and required scene objects also
match the installed game; tests cover all 2,598,960 five-card hands, and an independent
execution of the extracted classifier states agrees on 4,800 ordered hands. The debt
letter's 15 native calculation/request/payment actions, typed variables and scene/text
targets match the installed game; payment tests cover concurrency, retries, changing
quotes and fractional charges. No two-player gameplay test has run for this pass.
An isolated Wine startup check loads Core 0.1.31 in the current Unity/Mono runtime,
reaches MainMenu and starts a local UDP host; native Steam-dependent menu actions
report missing-Steam errors in that offline profile. It does not exercise the shared world.
The slot half of R2.12, VideoPoker (R1.3) and debt payments (R1.6) are implemented;
Ventti settlement remains open (R2.12); property/table replication (R2.11/13) and
vehicle CRC coverage (R2.4b) are implemented with two-player checks still pending.

**R1 — Never synced at all** (verified: zero references anywhere in `src/WinterMP.Core`)
- [x] R1.1 `Systems/BankAccount` — implemented 2026-09-05 (**v90**), runtime validation
      pending. Static extraction of installed build 23268598 establishes the globals
      `PlayerMoney`, `PlayerBankAccount`, `PlayerNetIncome`. This also exposed and fixed
      WalletSync's incorrect `Money` binding (there is no such global). WalletState now
      carries all three balances with optional-field flags, retained state for late binds,
      sequence ordering, and expanded wallet CRC/resync. Guests suppress the bank's own
      interest/ledger FSM, restored on disconnect/scene change/destruction. ATM transfers
      use host-validated requests + exact-once acknowledgments (162/163); guest deposits
      settle per inserted note and withdrawals on cash collection, preserving local ATM UI.
      Globals and ATM mutation bindings live in the catalog's `banking` section; hooks
      verify each action's target balance before installing.
      Bank statement history remains host-local. **Two-player check:** start from different
      cash/bank balances, deposit/withdraw concurrently, retry a request, reconnect, cross an
      interest day, and verify a later singleplayer session still runs the bank/ATM.
- [x] R1.2 `Systems/Expenses::Rent` + `::Livingsupport` — fixed 2026-07-27 (**v84**).
      `WelfareSync` now owns the whole Expenses object: `WelfareState` grew rentDebt /
      rentPerWeek / asumistukiPerWeek + an Evicted flag. Guests suppress their own
      Rent/Livingsupport FSMs for the session (`FsmSuppressor`; a guest plays in the host's
      world — its local weekly ticks were throwaway divergence) and replay the terminal
      `Kick out` state once when the host's eviction flag appears, so the furniture
      destruction/relocation happens everywhere. Eviction detection polls
      `ActiveStateName == "Kick out"` — safe because that state is *terminal* (unlike the
      R2.3 one-frame `Fire` transient). Money movement itself still lands in the bank
      balance → converges only once R1.1 ships.
- [x] R1.3 VideoPoker / Rami-Pokeri — implemented 2026-09-05 (**v93**, 167–169),
      two-player verification pending. `PokerSync` pauses native resolution on both peers;
      `PokerLedger` owns betting, two-draw hands, holds, the separate high/low deck,
      win collection and shared-wallet cash-out. The native physical controls, screen,
      card art, balances and sounds present authoritative state even with the host outside
      the town LOD. All bindings/assets and payout multipliers live in `videoPoker`.
      Receipts prevent repeat debits/payouts; control leases prevent concurrent hands.
      Abandoning/rejoining keeps the existing hand and secret double. Teardown settles
      once and refunds machine balances, with native-bank handoff if cash is imprecise.
      **Two-player check:** join mid-hand with the host outside town; hold/redraw together;
      collect a win, then cash out; double low/high including seven and the ≥500 automatic
      collection; retry a payment; reconnect during a hand/double; end the session and play
      the native machine again. Verify money conservation and native achievements.
- [ ] R1.4 `Systems/HockeyGames` — **v104 complete board implemented 2026-09-06;
      runtime verification pending.** The v88 scalar broadcaster did not synchronize
      the actual betting data: GameIndex/team IDs/odds/Result are loop scratch.
      Installed build 23268598 proves six live Hashtables (0–5, keys 1/X/2),
      ResultsGame/ResultsOdds lists, upcoming/previous pairings, scores and standings
      display lists. They are now appended to `HockeyBettingState` 160 as one complete
      snapshot. Host capture waits for both season and odds generation to finish;
      join snapshots do not consume ordinary broadcasts. Guests retain pending state,
      pause both local generators after loading, update live collections and refresh
      teletext 240/241/302. Disconnect restores their original collections and values
      before resuming native FSMs. No ROUND/ODDS/CHECKMEGAVETO or save event is replayed.
      The old claim that standings require ES2-level sync was incorrect: their display
      lists are live proxies too. Individual-player scoring (Pisteporssi) is still
      outside this snapshot. Use the v104 BUILDING.md checklist for loading, live
      rounds, visible pages, guest restoration and reconnect. Megaveto tickets still
      need their own host transaction ledger (R2.21); board sync alone does not settle
      purchases or claims.
- [x] R1.5 `Systems/ScrapMetalPrice` — the *price* half fixed 2026-07-27 (**v86**,
      `WorldScalarsState` 104 / `WorldScalarsSync`): the host's daily `ScrapPriceMKkg` +
      `Change` are broadcast and guests' own re-rolls get stomped. Still open: the
      `GarbageTrigger` selling *payout* is a money-gain button — same class as the lottery
      win (R2.21), needs a catalogued rule from the fresh dump (R3.1b).
- [x] R1.6 `Sheets/DebtLetter` — implemented 2026-09-05 (**v94**, 170–172),
      two-player verification pending. `WelfareSync.DebtLetter` supplies host quotes
      and acknowledged shared-cash payments through `DebtPaymentLedger`. Installed
      build 23268598 establishes the exact calculation: `Rent/Debt` × the sheet's
      `Interest` (default 1.29), then `Cost1` (59) and `Cost2` (864), with native float
      precision. The bank prime rate is unrelated. Catalogued actions/variables are
      validated before replacing the calculation and payment actions on both peers.
      Payments compare the displayed quote revision, validate proximity to `Rent/Letter`,
      debit shared cash, clear rent debt and hide the envelope once, even with the
      host's sheet inactive. Revision changes and cached receipts prevent stale or
      concurrent duplicate debits. The Letter reference survives eviction's mailbox
      relocation; payment does not undo eviction. Disconnect closes pending UI and
      restores native actions; binding failure disables payment while welfare continues.
      **Two-player check:** open the bill with the host away; pay concurrently; test
      insufficient funds then another press after adding cash; change rent debt while
      the bill is open and confirm the stale quote is declined; retry/reconnect; pay
      after the mailbox moves on eviction; Escape while payment is pending; disconnect
      and pay a later letter in singleplayer. Check cash, debt and envelope convergence.
- [x] R1.7 `Database/Keys::PlayerKeys` — fixed 2026-07-27 (**v86**, rides
      `WorldScalarsState`): `UncleStage`, the GIFU key bool and `ConlineNMBRint` (the
      randomly-generated phone number — now identical on every client) are host-broadcast;
      guests write them back. Physical key items ride the normal item sync.
- [x] R1.8 Non-project vehicle cabin climate — fixed 2026-07-27. Dump-verified the premise
      first: only THREE vehicles carry a `CarTemp*` cabin sim (SORBET, CORRIS, and the taxi
      `JOBS/TAXIJOB/MACHTWAGEN/Simulation/CarTempTaxi`); GIFU/KEKMET/BACHGLOTZ/JONNEZ have no
      CarTemp/HeaterUnit subtree, so they never diverged — nothing to sync. Added the taxi to
      `VehicleClimateConfig` (path prefix + CarTemp marker; all bound var names verified
      identical on the taxi FSMs). Taxi heater buttons have no `Set`/`Set angle` commit state —
      `FireKnobCommitState` degrades to writing Setting/Angle vars only (knob visual may not
      animate remotely; heat output itself is host-streamed via `HeaterUnit`).

**R2 — Synced but wrong** (shipped code that silently does nothing, or the wrong thing)
- [x] R2.1 **`VenttiSync` guest hooks are observers, not suppressors** — fixed 2026-07-24.
      `FsmHook.OnStateEnter` *prepends* a callback but lets the state's own actions run, so a
      guest's Bet/Hit/Stand resolved on local RNG; money reconverged via `WalletState` but the
      SATSUMA wager diverged permanently. Fix: new `FsmSuppressor` primitive in `FsmHook.cs`
      (disable the component, pinning `Fsm.RestartOnEnable = false` so it freezes and resumes
      in place), applied to `Table/GameManager :: Use` on guests — the sole owner of
      `Win car`/`Lose car`/`Win house`/`Lose house`. Verified against the decompiled
      `PlayMaker.dll`: `Fsm.Active` gates on `owner.enabled` and `Fsm.ProcessEvent`
      early-returns when inactive, so a disabled FSM cannot be driven by Update, a targeted
      `SendEvent`, a global transition, or a broadcast. Slots now gate every local money
      control before native mutation and use seeded animation only (v92, R2.12 below).
      Also fixed alongside: the duplicate-hook latch in both classes (`HooksInstalled` was
      all-or-nothing, so one un-Awake FSM made the 5 s probe re-prepend hooks to already-hooked
      states → N duplicate intents per press), and `FleaSaleSync`'s suppression leak (it
      disabled `Sell` and never restored, so a player who guested once could not sell at the
      flea market again *even in singleplayer* until they restarted the game).
- [x] R2.1b Slot keepalives rewriting reels mid-spin — fixed 2026-09-05 using the
      installed asset's action data. Guest retains only the newest state until **both**
      machines are idle; the verified idle states are `Wait player` / `Wait button`.
      `Reset game` is still a mutating state and is treated as busy. Pending state survives
      until a later idle tick, even without another packet. **Two-player check:** spin on
      a guest during host keepalives and confirm symbols/locks settle without mid-spin jumps.
- [x] R2.2 Moose killed by a *guest* never dies for anyone else — fixed 2026-07-27
      (protocol **v82**, `NpcDeathReport` 110). Guest polls its mover corpses (1 s) and
      reports; host validates fresh reporter pose ≤60 m of its own copy, then replays the
      vanilla `Mesh/Collider :: CarHit` death entry (`State 2`) so the game's own death
      actions run host-side and `FlagDead` streams to everyone. Host is idempotent and the
      guest re-sends every 3 s until FlagDead echoes — no send latch (class-A safe). Also
      fixed alongside: a mover that dies while its stream is idle (at rest) now emits a
      one-shot death announce, so at-rest kills reach guests too.
- [x] R2.3 Appliance house-fire never propagates — fixed 2026-07-27 (**v85**). `FlagFire`
      sampled `ActiveStateName == "Fire"`, a one-frame transient, so it was almost never
      true. The dump shows the actual ignition commits are the per-plate `Start fire{,2,3,4}`
      states (`Fire N --FIRE--> Start fire N`); the host now edge-hooks all four via
      `FsmHook.OnStateEnter`, bumps a wrapping `FireCount` (+ `FirePlate`) on
      `ApplianceState`, and guests replay exactly that plate's commit state once per bump —
      role checked at fire time so a guest's replayed entry can't echo. First received count
      only seeds the guest baseline (no re-igniting fires that predate the join). Residuals:
      extinguishing/burn-out still runs per-client, and a fire actively burning at join time
      is not re-ignited for the joiner — both documented, both smaller wrongs than the gap.
- [x] R2.4a Vehicle late-join was climate-only — fixed 2026-07-24. The join snapshot called
      `BuildJoinClimateSnapshots()`, so a late joiner started with whatever engine/fuel/gear/
      damage/tire state its *own* save held. It now sends `BuildJoinVehicleSnapshots()`, which
      reuses the existing full `BuildVehicleResyncMessages()` set (VehicleState + climate).
- [ ] R2.4b Parked-vehicle CRC — implemented 2026-09-05 (**v96**, unreleased),
      **two-player verification remains required before shipping**. `VehicleChecksum` now
      folds concrete damage, tire pressure, drivetrain damage, each wheel's health and
      puncture/rim flags alongside id/flags/fuel. The v91 live fitted-part reader reconciles
      `LiveDamageMask | AppliedDamageMask` against current wear: a bare union would retain
      stale failures after repair. Both roles use those same reads, with no send-sequence or
      change-baseline mutation. Tire pressure now rounds to hundredths; truncating a decoded
      `n / 100f` could lose a unit on re-encoding and cause permanent mismatches. Tests cover
      all 256 pressure values across repeated handoffs, role bookkeeping, repairs, every
      concrete damage bit and every condition field through the wire/snapshot path. Dynamic
      wear/climate/RPM and actively owned cars remain excluded. **Two-player check:** let a
      damaged/flat parked car settle, repair it, hand ownership between peers, reconnect,
      then idle for several checksum periods. Confirm a deliberately divergent condition
      requests a vehicle resync, heals and stops requesting; driving/cooling/frost changes
      must not introduce recurring vehicle mismatch logs. See `docs/BUILDING.md`.
- [x] R2.5 `CarRadioSync` station never synced — fixed 2026-07-24 (protocol **v81**). No radio
      Knob FSM has a *float* named `Channel` (the only `Channel` in the subtree is a string on
      the CD player), so `FindFsmFloat("Channel")` bound null and only `Volume` worked — bug
      class **D**. The station value is `Tune`. Also fixed a second fault: `Locate()` took the
      *first* FSM named "Knob" in the subtree, but each radio has three (tuner, radio volume,
      CD volume), so hierarchy order decided which was bound; it now targets
      `StockRadio0/ButtonsRadio/Volume :: Knob` by path, the one FSM carrying both `Tune` and
      `Volume`. Wire field `byte Channel` → `float Tune`, unquantized (the per-station windows
      are not knowable from a dump without action data).
- [x] R2.6 Guest-initiated sleep is ungated — fixed 2026-07-27. `PlayerSleepHook` now probes
      on both roles and decides at fire time (the hook outlives a session, so role is resolved
      live via `SessionManager.Instance`). Host keeps the consent flow; a guest entering
      `Confirm` is bounced straight back to `State 3` (Confirm's own "didn't confirm" route) with
      a rate-limited chat hint to ask the host. Vanilla sleep still works when disconnected
      (`PlayerCount == 0` guard).
- [x] R2.7 Ice race broadcasts no standings for host-driven races — fixed 2026-07-28 (no
      wire change; the state messages existed, only host records were never produced).
      `IceRaceSync.ObserveHost` mirrors `RallySync.ObserveHostStage`: the same FSM edges the
      guest path reports feed `TryAdvance` directly, with the host's own pose plumbed via
      the new `ItemWorldSync.TryGetLocalPlayerPosition` (the missing piece the old
      known-limitation comment named). Mode detection uses the exact local pose — stricter
      input than the shipped guest path's network pose; still on the playtest list like all
      of §1b.
- [x] R2.8 Taxi fare is lost for guests — fixed 2026-07-28 (**v87**), all three parts:
      (1) `Customer1/TaxiWalker` is now a host-authoritative `ScriptedMoverDef` (Logic
      frozen on guests, pose streamed), so the customer's enter/exit/fare logic is
      single-sourced on the host — its Cost accrues off the guest's *vehicle-synced* taxi —
      and the PayMoney press passes the host's proximity gate; (2) the hand PayMoney button
      is a catalogued control like the other round-2 job payouts; (3) `TaxiJobState` gained
      `FareCost` so the guest's frozen meter displays the fare the host will charge. The
      `Payments`-is-the-payday-FSM docstring corrected in the same change.
- [x] R2.9 `JOBS/Farm/Farmer` proximity gate — fixed 2026-07-28. The farmer's Walker
      (which carries the Logic walking AI and the hand-bone PayMoney button — same
      static-root/moving-child shape as the hitchhiker) is now a `ScriptedMoverDef`, so
      the guest's farmer stands where the host's does and the payday press passes the
      host's proximity gate. Self-limiting if the transform choice were wrong (a still
      mover sends one final and the guest AI is restored). The hitchhiker half needed no
      change: its pose was already streamed, and the host overwriting `Timer.Money` is
      the *correct* host-authoritative flow — a guest's replayed payday press pays the
      host's amount.
- [ ] R2.10 Rally opponent cars run per-client — **do not "fix" this by streaming them.**
      Unlike ICERACE (16 opponents partitioned into four disjoint per-venue sets), RALLY has
      exactly **one** fleet, `RACES/RALLY/RallyCars/RALLYCAR{1,2,3}`, multiplexed across all
      three special stages by `RallyCars::AIdrivers` (int `Stage`, events SS1/SS2/SS3), which
      nothing syncs — while `RallySync` deliberately tracks stage progress *per player*. So
      "host on SS1, guest on SS3" is a supported state, and host-streaming those three bodies
      would freeze the guest's AI and teleport the guest's opponents onto the host's stage,
      hijacking the guest's own rally. Attempted and reverted 2026-07-24. Any real fix must
      sync `AIdrivers` `Stage` first and gate the stream on stage agreement.
      **Installed-action evidence (2026-09-05):** `AIdrivers.Stage` is a sampled
      copy of `RACES/RALLY :: Reset/CurrentStage`, not a durable fleet assignment.
      Each car's Navigation starts also reparent the car, select stage-specific
      waypoint ranges, toggle physics/drive FSMs and wait on local start timers.
      A future stream must carry stage context atomically with poses, preserve ids
      across reparenting, and restore those local lifecycle states on disagreement
      or timeout. Merely adding these bodies to `NpcTrafficSync` remains unsafe.

- [ ] R2.11 Host Ventti property transfers — implemented 2026-09-05 (**v97**, 173),
      **two-player verification pending**. Installed actions set the global ints
      `PlayerKeyRuscko`, `PlayerKeySatsuma`, `PlayerKeyHome` and toggle cabin sleep,
      woodstove hatch and logging access. These native legacy names are not aliases for
      the Corris/Sorbet. `VenttiSync.Properties` broadcasts that state on change/keepalive
      and join, independently of the table observations and with inactive-object lookup.
      The native home-door close edges already use catalogued door sync; subsequent
      opening checks `PlayerKeyHome`. Guests retain packets across late bindings, keep
      their wager resolver disabled, and restore original local keys/access at teardown.
      Missing access bindings remain unknown; explicit false states revoke old access.
      No money, stress, speech or wager actions are replayed; save-point activation stays
      host-local (only the host saves). Catalog bindings and packet/replica tests pass.
      **Two-player check:** host wins/loses each stake with the guest away from the table;
      compare keys, home door access, cabin sleep/stove/logging; join after a transfer,
      reload inactive cabin LOD, then disconnect and verify the guest's original local
      access returns. Inactive-host Ventti play remains R2.12; full-width table state
      is implemented in R2.13 below.
- [ ] R2.12 **Ventti guest actions while the host is away** — host-ledger integration
      implemented 2026-09-05 (**v99**, 175–177); **two-player verification pending**.
      Both peers cut native bet/deal actions and use acknowledged, authenticated
      commands with one-player leases. Host draws a private 52-card deck, escrows
      bets, resolves cash/property hands and publishes concrete card ids. Both peers
      use native textures and pick targets; extra plain meshes support hands beyond
      nine cards. The host's loaded manager keeps native save handling and runs each
      catalog-checked property/NPC outcome once with duplicate accounting actions
      removed. Actor stress is applied once from the matching receipt. Teardown
      refunds an undealt stake or stands on the committed deck; an unrepresentable
      final wallet return is retained as paid native stake. Guest variables, action
      flags, materials and visibility are restored. Legacy control replay (94) is
      retired. Net tests include 500 conservation hands plus 900 wire/replica hands.
      **Still verify in game:** simultaneous bettors, host away with table controls
      inactive, LOD changes, native hand already in progress, all property outcomes,
      actor stress, save/load, late join, repeated reconnect and teardown during a
      committed hand. Guest NPC/furniture pose and sound mirroring is implemented
      in **v100** (178–179): host-observed bones, visibility and thrown furniture;
      exact host-selected sound variations, with bounded/expiring live cues and no
      historical speech in snapshots. Guest physics/AI/animations stay paused and
      originals return on teardown. Verify throws, crawl/walk, audio, late joins
      and missing assets on two peers. Do not mark R2.12 done until the runtime
      matrix passes.
- [ ] R2.13 **Ventti's wire fields are lossy** — observation fix implemented
      2026-09-05 (**v98**, 174), **two-player verification pending**. `VenttiTableState`
      replaces retired 93 with the native float stake, int hand totals, a sequence and
      six explicit native result codes. `LoseText.Status` supplies the result; an empty
      status clears it, and an unknown nonempty status is diagnosed, not guessed.
      Native status can announce a win/loss before settlement: this is an observation,
      never a payout or permission to replay car/house transfers. Catalog `venttiTable`
      owns paths, typed variable names and result strings. Guests retain complete state
      across late bindings, reject stale/invalid packets and restore their original
      variables on teardown. Inactive discovery does not activate host gameplay.
      Join snapshots leave the connected-guest broadcast baseline untouched. In v99,
      observation 174 yields to full ledger state 175 once the table is adopted.
      **Still verify:** >255 mk and fractional stakes, all six result codes and reset,
      late table binding, join after a result, inactive LOD and repeated reconnect.
      Leased controls, host settlement and native card/result presentation now have
      a v99 implementation in R2.12; runtime verification remains required.

**Round 3 (2026-07-27) — full class-A/B/C/D/E sweep over the v57–v81 subsystems** (they
were written *after* the 2026-07-20 sweeps, so those lessons had never been checked here;
class B and D came back clean everywhere, the rest did not):

- [x] R2.14 **Vehicle damage/condition: cross-owner sequence blackout, join gap, wheel
      drift** — fixed 2026-07-27 (**v82**). (a) Receivers deduped `VehicleDamage`/
      `VehicleCondition` sequences per *vehicle* while every client counts per *sender*, so
      each ownership handoff had every receiver rejecting the new owner's low counter for
      minutes (`LastDamageSequenceOwner`/`LastConditionSequenceOwner` rebase, mirroring
      `ItemWorldSync`'s `LastRemoteSequenceOwner`). (b) Neither message was in the join
      snapshot despite the R2.4a comment claiming so — a parked seized car stayed healthy for
      late joiners forever (no owner to keepalive it); the host now snapshots its best-known
      mask (`Live ∪ Applied`) + live condition FSM reads for every vehicle. (c)
      `ApplyWheelDiscrete` diffed against apply *bookkeeping* instead of the wheel FSM's
      actual state, so a locally-drifted (or save-borne) flat could never be healed by
      keepalives — now diffs `ReadWheelState`. Also: a fresh claimer now seeds
      `LiveDamageMask` from `AppliedDamageMask` (no more mask-0 "un-breaking" + re-claim
      re-fire storm).
- [x] R2.15 **WantedSync guest crimes were erased** — fixed 2026-07-27. Reports latched
      `_reportedUpTo` at *send* with no re-send; a transiently rejected report (host FSM not
      bound yet) was then made unrecoverable by `Apply` overwriting the local counter *and*
      re-baselining. Evidence now lives in a pending-delta accumulator the broadcast cannot
      stomp; the host queues reports that arrive before its FSM binds (reliable channel +
      queue = exact-once without an ack protocol); first observation seeds the baseline so a
      save's pre-existing counters are not replayed as fresh crimes.
- [x] R2.16 **JailSync guest-offender inversion** — fixed 2026-07-27 (**v83**,
      `JailState` + `JailedPlayerId`). The countdown runs only on the jailed client (the
      file's own doc said so), yet the host keepalived its idle `DaysLeft` = 0 over the
      jailed guest every ≤20 s. Now the jailed client owns the record and reports it; the
      host adopts (30 s TTL) + relays; the jailed client ignores broadcasts about itself.
- [x] R2.17 **Gambling/HeatSource intent latches keyed per player only** — fixed
      2026-07-27. Guests count intents per *machine/source*, so after using machine A,
      machine B's fresh low counter read as stale and the second slot machine (or a second
      stove) went permanently dead for that guest. Latches now key (player, machine).
- [x] R2.18 **RepairShopSync shared one guest-intent latch + one pending-order slot across
      all guests** — fixed 2026-07-27; per-player dicts mirroring `MailOrderSync` (guest B's
      first Fleetari order was silently rejected while guest A had ever ordered, and
      concurrent confirms evicted each other).
- [x] R2.19 **Per-player host latches survived reconnects** (systemic, 14 subsystems) —
      fixed 2026-07-27. A rejoining guest keeps its PlayerId but restarts every counter, so
      each subsystem's stale latch rejected everything it sent as "stale" until it
      out-counted its previous life. Every handshake admission now calls
      `WorldSyncManager.OnPlayerAdmitted` → `ForgetPlayer` on all per-player dedup state
      (wanted, jail, gambling, ventti, fleaSale, repairShop, mailOrders, heat, police,
      rally, iceRace, homeStereo, purchases, item spawns, passenger seats).
- [x] R2.20 **Kilju: a guest could not brew** — fixed 2026-07-27. Guest brew edits streamed
      only for *held* buckets; a bucket resting on the floor is never motion-claimed, so the
      host's 3 s keepalive reverted every lid flip. A local lid flip (the one purely
      player-driven flag — the floats advance by per-client simulation and must NOT trigger
      claims) on an unowned resting bucket now claims it via
      `ItemWorldSync.TryClaimForInteraction`; the at-rest release hands it back. Receivers
      that own a bucket now also ignore remote `BrewState` for it.
- [ ] R2.21 **Lottery ticket purchases and claims lack proven host authority** — reopened
      2026-09-06 after installed action inspection. **Form isolation fixed 2026-09-06,
      runtime verification pending:** removed the three erroneous `buys[]` rules for
      Lotto docked-ticket inspection, BuyLotto and BuyMegaveto. Their guard states
      Wait button/Wait button 2 are entered on hover and still wait for
      GetMouseButtonDown/GetButtonDown to emit USE. The old entry hooks sent repeated
      requests before a press, aborted the guest's wait, and broadcast native form
      opening as a shared purchase result. These three Use FSMs now remain local;
      actual Sheets/LottoTicket/Pay and Sheets/MegavetoTicket/Pay bindings remain.
      No wire layout/meaning changed; the existing catalog hash rejects mixed catalogs.
      `check_fsm_bindings.py --purchase-catalog` now detects this input-wait guard
      mistake from action evidence; incomplete dumps/templates remain unverified.
      **Lotto transaction implementation 2026-09-06 (v103, 181–183), runtime
      verification pending:** dedicated `LottoTicketSync` replaces generic Lotto Pay
      replay. It captures completed paid rows, validates current round and fresh
      player proximity, and instantiates the host factory's actual prefab with a
      host-issued persistent ID. Partial unpaid rows are cleared; an open host form
      is preserved. Host native ticket calculation/save handling stays authoritative.
      The actual claim path is VoittousArea/TrashTrigger: under 1,000 mk goes to
      shared cash, at/above 1,000 mk to shared bank. Claims identify a host ticket,
      require the host body near the box, wait for complete native calculation and
      retire the ticket before crediting money. Copied request receipts and lifetime
      retirement prevent repeated/concurrent claims and guest reconnect duplicates.
      Host ticket metadata joins/resyncs with stable item IDs; movement uses normal
      item authority. Guests hide their local saved tickets and create native replicas
      with load/save/delete/calculation disabled, restoring local tickets on teardown.
      The offline extractor can now select `--asset sharedassets3.assets`, so the
      spawned prefab was inspected alongside the scene instance and claim actions.
      **Still open:** two-player form/purchase/claim timing, host-away behavior,
      saved ticket reload/deletion, late binding and repeated reconnect (BUILDING.md).
      An uninitialized host factory waits without charging; first-visit/loading
      behavior needs a runtime check. Bank statement/achievement presentation is
      still local-only/unimplemented for the new claim path. Megaveto selections,
      ticket identity and claims still need their own host ledger; its generic
      Pay rule and native collection path remain, so R2.21 and 1.4 stay unchecked.
      **Megaveto prerequisite (v104):** complete host odds/results/pairings and
      standings are now synchronized (R1.4), replacing the insufficient one-match
      scratch values. Guest season calculation is paused; a future ticket ledger must
      use host ticket winnings rather than reintroduce guest CHECKMEGAVETO replay.
- [x] R2.22 **A repeat phone call with the same Topic may never broadcast** — fixed
      2026-07-28. The ringing object deactivates once a call ends (`Disable phone`
      terminals under `FunctionsDisable`), so the host now re-arms `_lastTopic` whenever
      the phone object is inactive — safe regardless of whether vanilla clears `Topic`.
      Known residual (pre-existing, unchanged): a guest whose own phone object is idle when
      the event arrives fires `SendEvent` at an inactive FSM (inert, R2.12 class); making
      that ring for real needs the activation flow from the fresh dump.
- [x] R2.23 **Engine repairs never cleared damage; SEIZE/CAMFAIL rerolled it on peers** —
      implemented 2026-09-05 (**v91**), in-game verification pending. Installed action
      data maps all 14 concrete failures to the currently fitted `db_*` parts' `Data/Wear`.
      These bindings live in the catalog's `vehicleDamage` section, with fixed wire slots.
      The wire now carries current wear plus a known-parts mask, so replacements clear
      failure bits and delayed bindings retry. SEIZE/CAMFAIL are random selectors, now
      retired as durable bits; non-owner damage states are gated before actions run.
      Parked cars broadcast from the host. Joins and targeted vehicle resyncs carry healthy
      parts and tire condition too. Hooks are removed on disconnect/scene teardown, and
      exceptions disable only that vehicle's damage sync. **Still verify with two players:**
      identical concrete outcomes from SEIZE/CAMFAIL; break/replace/rejoin with host and
      guest drivers; late part binding; visual breakoff after a snapshot; no hook buildup
      after reconnect. The separate vehicle CRC gap (R2.4b) remains open.

**Round 3 self-review (2026-07-28)** — an adversarial agent pass over everything round 3
itself wrote (v82–v88); 13 findings, 10 fixed the same day:

- [x] Kilju lid-flip was deterministically lost over Steam: the claim rides the unreliable
      channel, `BrewState` the reliable one, and the receive pump drains reliable FIRST —
      the host rejected the one-shot state before the claim registered. `TryAcceptGuestState`
      now accepts `RemoteOwner == NoOwner` (the vehicle damage/condition precedent).
- [x] JailSync could adopt a jailed guest's record but never RELAY it: `JAIL/Functions` is
      inactive until an arrest, `GameObject.Find` can't see inactive objects, and the
      broadcast was gated on Ready. Locate now scans `FindObjectsOfTypeAll`, and a live
      guest record broadcasts/snapshots without needing the host's own FSM. Two
      concurrently-jailed players also no longer stomp each other (a serving client ignores
      foreign jail broadcasts).
- [x] PhoneSync could never make a guest's idle phone ring (same inactive-object Find gap +
      SendEvent at a deactivated FSM is a silent no-op): Locate scans all FSMs, Apply
      activates the ringing object before writing Topic. Whether the conversation routes
      fully vanilla needs R3.1b action data — but ringing at all is strictly better than
      never.
- [x] Per-ITEM same-sender sequence latches survived a rejoin (the OnPlayerAdmitted reset
      only covered per-player dicts): a crashed-and-rejoined guest's fresh counters were
      stale-dropped per item on EVERY client. `ItemWorldSync.ForgetPlayerItemSequences`
      downgrades all six latch families to the owner-change sentinel, and OnPlayerAdmitted
      now also runs on guests when the relayed `PlayerSpawn` arrives.
- [x] Scripted movers lacked the sequence-reset escape rigidbody NPCs have (a late joiner
      meeting a >32767-packet moose stream dropped it, corpse announcements included) —
      `StaleStreak` fallback added; dead movers are also now in the join snapshot (the
      one-shot death announce predates a late joiner).
- [x] Moose death-report radius 60→150 m (validation runs against the reporter's CURRENT
      pose; by the 3 s retry a highway-speed car had left the radius → permanent rejection).
- [x] WorldScalars Ready is all-of-three (a partial bind broadcast UncleStage=0 over real
      progression; 0 is a valid value so no field guard can help) + PrimeInterest/Conline
      joined the change predicate.
- [x] Kilju/FluidContainer pending-state now clears on level change (path-derived ids
      repeat after a reload).
- [x] R2.24 **A guest's own oven fire was invisible to the host** — fixed 2026-07-28
      (**v89**, `ApplianceFireReport` 161). The guest's `Simulation::Data` rolls its own
      `FireHazard` RNG even over synced heats; a guest ignition is now reported (paced 30 s
      per oven, per-player seq with rejoin reset) and the host replays the plate's commit
      state, single-sourcing the house fire — the moose-report pattern. The guest's replay
      of a HOST ignition is suppressed from re-reporting (1 s echo window around
      `ReplayIgnition`).
- [x] R2.25 **Rally crossing reports were lost after transient rejection** — implemented
      2026-09-06 (**v101**, extended 86/87), **two-player verification pending**.
      Guest crossing edges remain queued in checkpoint order until an exact host
      acknowledgment; retries reuse their connection token and sequence. Host rejects
      malformed/out-of-order reports without advancing dedup state, and a duplicate
      start cannot reset elapsed time. A short history of host-observed marker/driver
      proximity handles report/pose ordering delays without trusting guest clocks or
      coordinates. Evidence expires from the original pose timestamp. Complete native
      stage binding prevents partial scans from finishing a race early. Installed
      actions also exposed an unused `Checkpoint` bool in every marker: completion
      now observes the catalogued terminal `Idle` state, which survives Timing's
      flag reset at finish. Bindings and the 4/6/4 marker layout are catalog data.
      Same-connection scene rebinds preserve report identity/counters. Initial save
      flags are baselines, so reconnect does not invent a new start; admission preserves
      the host race while clearing old report identity. Per-player uint revisions
      prevent one driver's stream from aging another's state out; final times freeze
      and join snapshots preserve pending host finish broadcasts. The Net suite covers
      300 seeded races with rejection/ack-loss, plus malformed data, wrap, reconnect,
      evidence expiry and queue timeout/recovery. No opponent-car synchronization or
      native result/prize mutation is added; R2.10 remains open. Verify native trigger
      timing, delayed poses, reconnect with matching/mismatching native progress and
      SS1/SS2/SS3 under two-player gameplay (BUILDING.md).
- [x] R2.26 **Lotto sent a save key instead of winning numbers** — implemented
      2026-09-06 (**v102**, `LottoDrawState` 180; 96 retired), **two-player verification
      pending**. `UTNational7` contains `Lotto7`, the save tag for NationalLine7.
      Actual results are four live integer ArrayLists: Results (7), ResultsBonus (3),
      ResultsLinesWinnings (5), ResultsLinesWon (5). Complete stable-state snapshots
      now carry them with round/ticket round, all three pots, DrawDone and teletext
      visibility. Guest Numbers FSM pauses after native loading; pending snapshots
      survive late binding, and teardown restores the local draw before resuming.
      Both join and FSM-group resync include the draw; same-round list/prize changes
      publish too. Display refresh avoids freezing the native Reset points hide.
      Net tests cover wire sizes, partial/type-invalid native lists, invalid numbers,
      full integer values, sequence wrap/order, copied pending state and change
      detection. Native ticket events are not replayed; R2.21 remains open.
- [ ] R2.27 **Shared-item removal and replay recovery** — implemented 2026-09-06
      (**v105**, runtime verification pending). Live removal previously disappeared
      if the guest had not materialized the body yet; deferred spawning could then
      resurrect it. Removal now establishes a terminal session ID, checked by
      scanning, offered-clone binding, deferred creation and replay. Item-group
      resync now includes removal chunks and fresh live spawn manifests after poses.
      Replays repair missing bodies even when the spill was previously received;
      existing bodies are not duplicated or teleported. Repeated pending replays
      coalesce, invalid manifests cannot consume dedup keys, and destroyed host
      bodies/retired IDs are excluded from replays. Session teardown clears lifecycle
      state. `ItemSpawnLifecycleTests` includes 200 seeded loss/removal/replay runs.
      Use BUILDING.md for two-player delayed spawn, consumed item, missing template,
      repeated resync and reconnect verification. The excluded SPAWNITEM factories
      in 7.2 still need separate real-time materialization hooks.
- [ ] R2.28 **Part-settle sync turned healthy parts into almost worn-out parts** —
      implemented 2026-09-06 (**v107**, native checks pending). Actual VIN133 Data
      initializes Wear with RandomFloat(90,99); ReadPartVars clamped every value above
      1 to byte 255, and ApplyPartState decoded that as 1. PartState and part snapshots
      now append and apply full native floats; legacy bytes remain wire hints only.
      World CRCs distinguish native wear values instead of saturating them. All
      readable parts, including zero values, are snapshotted; deferred values retry
      when registered FSMs become available, and newer applies clear older pending
      data. Object-state replies include native scalars alongside FSM state. Guest
      reports request a deferred host reading and cannot alter its wear, installed
      flag or tightness; accepted install/bolt events and vehicle damage retain their
      existing authority paths. Protocol tests cover native values, legacy framing,
      chunk pairing, malformed data, host authority and checksum convergence.
      **Still verify:** guest/host bolt turns, late joins, delayed FSM activation,
      repair and replacement, guest driving wear, corrupted-replica resync, and saved
      condition after reconnect. Package spawning/opening is still unfinished.
- Accepted residuals (documented, not bugs to fix): a crime incremented in the same frame a
      host broadcast applies can be missed (sub-frame window); a SleepTrigger variant
      without "State 3" would hint-but-not-block a guest (all dump variants have it);
      `PaidAmount`/`Weekly`/`KMsDriven` ride keepalives only (delayed, never lost).

**R3 — Tooling debt blocking the above**
- [x] R3.1a Dumper emits `globalTransitions` (2026-07-24, tools 0.2.0). Per-state
      `actionTypes` was already emitted — it just post-dates the dump on disk.
- [ ] R3.1b **Take a fresh F9 dump.** `catalog/dump-23268598.json` is from 2026-06-13 at
      `toolsVersion 0.1.0`, so it has *no* action data and *no* global transitions — 0 of its
      8,615 records carry `actionTypes`. That is why so much of §R2 reads "unverifiable":
      you cannot tell from the repo whether a state charges money, whether
      `SendEvent("NORMAL")` is accepted from a resting state, or where a global-only event
      like `SaleTable::Logic`'s `RENT` lands. Needs the game running, so it is a **human
      step**: launch with WinterMP.Tools deployed, press F9, commit the new dump.
      **Do this before R2.1/R2.3** — both hinge on questions only a richer dump can answer.
      2026-07-27, tools **0.3.0**: the dump now also carries a top-level `globalVariables`
      section (PlayMakerGlobals names + scalar values) — the fresh dump will additionally
      answer *which global is the bank balance* (R1.1) and make global bindings checkable
      by `tools/check_fsm_bindings.py`.
      **2026-09-05:** `tools/extract_fsm_assets.py` can now decode the installed Unity 5
      assets offline, including typed globals, global transitions, action field names,
      scalar arguments and referenced scene objects. This removes the *static-evidence*
      blocker for banking, debt-letter calculation, gambling and damage mapping. It is
      not a runtime dump: initialization, save-loaded values and multiplayer effects still
      require the game. `check_fsm_bindings.py --globals <extract.json>` now checks literal
      global bindings separately from local variables (a local `Money` can no longer hide
      a missing global `Money`). See `docs/BUILDING.md` for reproduction.

---

## 2. Task specs

Template — **Impact · Game truth · Model · Protocol · Touch · Done when · Watch · Deps.**

### Phase 0 — Housekeeping

#### 0.1 · Correct PLAN.md coverage overstatements  `TODO`
- **Impact** Docs claim coverage that doesn't exist → future agents skip real gaps.
- **Do** Edit `PLAN.md` §4.4: (a) Death row — remove "moose-hit death already in DeathSync" (0 refs in `DeathSyncManager`); point moose death at task 7.1. (b) Car-assembly row — "wear/tuning as synced FSM vars ✅" → only bolt-settle wear is synced; driving wear/breakage → tasks 2.1–2.2. (c) Jobs/flea row — "flea market" is buy-side only; selling → 3.1. (d) Racing row — "enroll" is ice-race only; rally enroll → 5.1.
- **Protocol** none. **Touch** `PLAN.md`. **Done when** the four claims match reality. **Watch** — **Deps** none.

#### 0.2 · Fix the vehicle-registration RequireRoot gate  `TODO`
- **Impact** Any drivable whose rigidbody has a parent is silently dropped — the taxi today, any future nested drivable tomorrow.
- **Game truth** `JOBS/TAXIJOB/MACHTWAGEN` is a full drivable sim car nested at depth ≥2.
- **Model** `VehicleCatalogConfig.IsVehicleRoot` rejects `transform.parent != null` *before* the mass≥150 / name-prefix test. Relax so a body that carries the vehicle-simulation subtree (Engine/Electricity/Steering FSMs) registers regardless of nesting — OR add an explicit allow-list entry for the taxi and gate on "has drivable subtree." Prefer a structural check (has `Simulation/Engine`) over a name list so it generalizes.
- **Protocol** none (registration only). **Touch** `src/WinterMP.Core/Catalog/VehicleCatalogConfig.cs`, `SyncCatalog.cs`.
- **Done when** MACHTWAGEN registers as `IsVehicle` and streams via the existing `VehicleWorldSync` path; the 7 existing families still register unchanged. **Watch** E (don't let a parented job-vehicle's stream contaminate an owned car — the existing owner guards apply). **Deps** none. Unblocks 2.4, 3.2.

### Phase 1 — Economy integrity

#### 1.1 · Slot machines  `TODO`
- **Impact** HIGH. Stake, win RNG, and cashout all mutate the single shared `Money` global with no intent → a guest's win/loss flip-flops when the host's ~2 s `WalletState` overwrites it; reels resolve on per-peer RNG so even the displayed outcome differs.
- **Game truth** `STORE_AREA/Stuff/LOD/GFX_Pub/SlotMachinePub/Buttons/{PayMoney,Start,LockRoll1-3}::Use`; `PERAPORTTI/…/SlotMachine/Buttons/PayMoney::Use`. Reels `Roll/Roll1-3`.
- **Model** Host-authoritative machine: the **host** runs the spin RNG and resolves the payout; the guest sends a *play intent* (stake + which machine, proximity-validated) and applies the host's result. Follow `HeatSourceSync` intent shape; route the wallet delta through the existing host wallet (never a direct guest `Money` write). Reuse `PurchaseIntent`/guarded buy path for the stake if it maps cleanly; otherwise a small `SlotMachineIntent`/`SlotMachineResult` pair.
- **Protocol** new pair in `93–99`; +version; PROTOCOL.md rows. **Touch** new `Sync/SlotMachineSync.cs`; wire in `WorldSyncManager` + `SessionManager.Messages`.
- **Done when** two players at one machine see identical reels + one authoritative balance; a guest win survives the next `WalletState`. **Watch** A (result latch on host ack), C (no phantom pending spin). **Deps** none.

#### 1.2 · Ventti blackjack (incl. car wager)  `TODO`
- **Impact** HIGH. Money betting **and** wagering the Satsuma car — resolves on per-client RNG; car stake can desync ownership.
- **Game truth** `PERAJARVI/Kunnalliskoti/Functions/RoomVenttiPig/Ventti/Table/GAME/Gamestuff/{Bet,HitPlayer,HitHouse,Stand}::Use`, `Table/GameManager::Use`; `Bet` exposes `Bet/BetMax/BigBetLimit`, a `Cars` state + `SATSUMA` event.
- **Model** Host owns the deal: host shuffles/draws and decides win/lose; guest sends bet/hit/stand intents; host applies wallet delta and, if a car is staked, drives the ownership transfer through the existing vehicle-ownership model (host reassigns `RemoteOwner`/registration — never a local guest write). Same State+Intent shape as 1.1.
- **Protocol** reuse the 1.1 pair if generalizable to "table game," else a `CardGame*` pair in `93–99`; +version; PROTOCOL.md. **Touch** `SlotMachineSync` (rename to `GamblingSync`) or a sibling `Sync/VenttiSync.cs`.
- **Done when** identical hands both sides; wallet + (if wagered) car ownership converge on the host result. **Watch** A, C, E (car-ownership write is host-only). **Deps** none (do after 1.1 to share the model).

#### 1.3 · Electricity + phone bills → cutoff / blackout  `TODO`
- **Impact** HIGH. Bills accrue on the (synced) host clock; unpaid → `CUTOFF` flips `MainSwitch` off, killing home lights/heating/appliances (the winter-survival loop). The whole meter/bill FSM runs per-client; bill payment isn't wallet-routed.
- **Game truth** `Systems/ElectricityBills1·2::Data` (`NextBill/NextCutoff/UnpaidBills/Price/MainSwitch`; events `BILL/CUTOFF/ELEC_CUTOFF`) consumed by ~16 appliance FSMs (`HouseElectricity::Status` Blackout/Lamp, radiators, fridge, oven, TVs, sauna). `Systems/PhoneBills1·2::Data` (`PhonePaid`). Pay buttons `Sheets/{ElectricityBill,PhoneBill}*/Pay` (`Check money → BUY → Date`).
- **Model** Host owns the bill ledger: broadcast `UnpaidBills` / cutoff state on change + join; suppress the guest's local bill FSM from acting (drive it from the host stream). Route bill payment through the **existing host purchase path** — add the Pay buttons to `buys[]` in `catalog/sync-catalog.json` so paying debits the shared wallet. Broadcast the `MainSwitch`/blackout edge so home power agrees on all peers.
- **Protocol** a small `UtilityBillState` (host→guests) in `93–99`; payment via existing `PurchaseIntent`; +version; PROTOCOL.md. **Touch** new `Sync/UtilityBillSync.cs`; catalog buys.
- **Done when** bills, payment, and blackout are identical on both machines; unpaid electricity cuts *both* homes' power. **Watch** B (join snapshot must not advance the delta baseline), C (retire a paid bill), D (verify `MainSwitch` is a Bool). **Deps** none. Pairs with 6.4.

#### 1.4 · Lottery + gambling tickets  `PARTIAL`
- **Implemented** v102 `LottoDrawState` (180): complete host Lotto arrays, rounds, pots, winner counts/prizes, guest draw suppression, late binding, join/resync and teardown restoration (R2.26). Native two-player validation is still pending.
- **Game truth** `Systems/Lottery::Numbers` stores Results/ResultsBonus/ResultsLinesWinnings/ResultsLinesWon as live integer ArrayLists. `UTNational7` is a save tag. Megaveto uses hockey data rather than these Lotto lists.
- **Implemented** v103 Lotto selected rows, native host-issued persistent tickets, acknowledged purchases and one-time cash/bank claims (`LottoTicketSync`, 181–183). Guest replicas use the native prefab and regular item transforms; native host save handling persists outstanding tickets.
- **Implemented** v104 complete hockey betting collections, pairings/results and standings (160), with guest generator suppression and local restoration. This supplies Megaveto's shared betting data; it does not implement its tickets.
- **Remaining** R2.21: runtime/save verification of Lotto and the separate Megaveto purchase/claim ledger. Bank statement/achievement presentation for Lotto claims is not implemented.
- **Done when** both peers see the same draw, purchased tickets belong to the correct player/round, duplicate claims never credit twice, and reconnect preserves outstanding tickets. **Watch** A, C. **Touch** `LotterySync.cs`, ticket FSM/catalog bindings, wallet/host ledger; bump protocol again for ticket semantics.

### Phase 2 — Shared-car integrity

#### 2.1 · Engine part-breakage RNG  `TODO`
- **Impact** HIGH. Each client rolls its own engine damage → the shared car's engine seizes for one player, runs fine for the other.
- **Game truth** `CORRIS/Simulation/Systems/PartBreakages::Damages` (`Chance` float; events `BEARING1-5/CRANKSHAFT/HEADGASKET/PISTON1-4/OILPAN/TIMINGBELT/SEIZE`). Present on each drivable's Simulation subtree.
- **Model** The vehicle *owner* (driver) is authoritative for its own car's breakage: suppress the non-owner's local `PartBreakages` roll and replay the owner's breakage *event* as a reliable FSM event over the existing FSM-event path, keyed to the vehicle id. Do NOT let each peer roll independently. Consider carrying a "damaged parts" bitset in the join snapshot so late joiners agree.
- **Protocol** prefer the existing `FsmRawEvent`/catalog control replay for the breakage event (no new id) + a snapshot field; if a compact per-vehicle damage mask is needed, add to `63–79`. +version if wire changes; PROTOCOL.md. **Touch** `VehicleWorldSync.Engine.cs`, `FsmWorldSync`, snapshot builder.
- **Done when** a breakage fires once, authoritatively, and both peers + late joiners see the same broken parts. **Watch** E (owner-gated), C (snapshot terminal state), D. **Deps** none.

#### 2.2 · Drivetrain wear + tire pressure/puncture  `TODO`
- **Impact** HIGH. Continuous wear + tire flats diverge per client.
- **Game truth** `…/Systems/Drivetrain::Wear` (`RateGearbox/RateDriveshaft/RateRearAxle`; `Transmission Damage`/`WORN`); `…/TirePressure::Data` (`Pressure`); per-wheel `WHEELc_XX::Condition` (`PUNCTURE/RIM`, `Health/FinalWear/Friction/GripReduction`, `TireType`).
- **Model** Owner-authoritative: stream the owner's wear/tire condition as low-rate scalar state per vehicle (extend `VehicleClimate`-style periodic send or a new `VehicleCondition`), applied only by non-owners (`!LocallyOwned`). Puncture is a discrete owner event.
- **Protocol** `VehicleCondition` (owner→host→peers) in `63–79`; +version; PROTOCOL.md. **Touch** `VehicleWorldSync.*`. **Done when** wear + a flat tire agree across peers + join. **Watch** E, B, F (per-vehicle wrap-aware seq). **Deps** none.

#### 2.3 · Repair-shop (Fleetari) service results + order record  `TODO`
- **Impact** HIGH. Engine tune, gear ratios, paint color, bodywork, tire/spring work — the outputs you pay for — never propagate.
- **Game truth** `REPAIRSHOP/Jobs/*::Work` FSMs (unhooked; scanner only handles Use/Screw/Buy/Data/Button). Outputs: `SettingAFR/SparkAngle/Valve*/OilViscosity`, `FinalGear`, spring stiffness, tire condition, bodywork, `PagePaintCar` color. `OrderFleetari` record (`Order`, `UTJobs`, `PaintCode/TireCode/AxleCode`, `JobTotalCost`).
- **Model** **Capture-and-pair** (mirror `MailOrderSync`): the guest configuring the brochure has the host capture the exact `OrderFleetari` record and pair it to that guest's next `PurchaseIntent(PAYMENT)`; the host applies the service authoritatively and broadcasts the resulting car settings (tune/gears/paint) as vehicle state so peers see the painted/tuned car.
- **Protocol** `FleetariOrderState`/`Intent` in `93–99` (like `MailOrderState`/`Intent`); +version; PROTOCOL.md. **Touch** new `Sync/RepairShopSync.cs`; catalog the `REPAIRSHOP/Jobs/*::Work` hooks; extend vehicle snapshot with paint/tune. **Done when** a paint/tune bought by one player shows on the shared car for all. **Watch** A/C (order retire), E. **Deps** 2.1/2.2 help (shared condition surface).

#### 2.4 · Register the taxi (MACHTWAGEN)  `TODO`
- **Impact** MEDIUM. The taxi is a full drivable sim car that never syncs — peers see it frozen; collisions invisible.
- **Model** After 0.2 relaxes `RequireRoot`, confirm MACHTWAGEN registers and streams through the generic `VehicleWorldSync` discovery (engine/electricity/fuel/lights/climate by child-FSM name). No taxi-specific code should be needed beyond registration.
- **Protocol** none. **Touch** verify only; add a catalog/name entry if 0.2 used an allow-list. **Done when** driving the taxi streams pose/engine/lights to peers. **Watch** E, F. **Deps** 0.2.

#### 2.5 · Gearbox / clutch state  `TODO`  *(lower priority)*
- **Impact** LOW. Selected gear/clutch is driver-local; observers see wrong gear indicator, minor.
- **Game truth** `Drivetrain::Gears` (`Gear` int, `IsManual/InstalledAuto`), `Transmission AutoClutch`, `GearboxManual/Clutch`.
- **Model** Add a gear field to `VehicleState` (owner→peers). **Protocol** extend `VehicleState` (id 60) — +version; PROTOCOL.md. **Touch** `VehicleWorldSync.Engine.cs`, `WorldMessages.cs`. **Done when** gear indicator agrees. **Watch** E. **Deps** none.

### Phase 3 — Income & jobs

> Shared pattern for 3.x: the payout must reach the **host wallet** (never a local `Money` write), and any host-owned job/record state broadcasts + retires. Route cash-in through the existing guarded purchase/wallet path or a host-validated payout record (mirror `PoliceSync`'s validate-and-own). Most also need the paying/earning player's **fresh-pose proximity** to the payer NPC/table.

#### 3.1 · Flea-market selling  `TODO`
- **Impact** HIGH. Rent a table, price items, host-timed random sales → wallet. Entirely local today (only buying is synced).
- **Game truth** `FleaMarket/SaleTable::Logic` (`MoneyTotal/Price/RentDays/ItemSold/MoneyEnvelope`; `SELL/SELLRAND/MONEY/PRICESET/RENT`), `SaleTable::Sell`, `SaleTable::DayChanger`, `BuyTableRent::Buy`.
- **Model** Host owns the sale table: table rental via host purchase; item placement/pricing as validated intents (dynamic item refs → carry item id like fuel/cargo intents do); the host runs the day-timed sale RNG and pays `MoneyEnvelope` into the shared wallet. Broadcast table contents/state on change + join.
- **Protocol** `FleaSaleState`/`FleaSaleIntent` in `101–119`; +version; PROTOCOL.md. **Touch** new `Sync/FleaSaleSync.cs`. **Done when** both see the same table + a host-driven sale credits the shared wallet once. **Watch** A/B/C, E. **Deps** none.

#### 3.2 · Taxi job lifecycle  `TODO`
- **Impact** HIGH (job) — depends on 2.4 for the vehicle.
- **Game truth** `JOBS/TAXIJOB` (`Logic` Activate/Disable-car), `TaxiFunctions::Payments` (`Money/Employed/KMsDriven/Phonebill`→Bank), `PaymentTerminal/Payment::Use`, `Customer1/TaxiWalker PayMoney/Receipt`, `Taximeter/Tripmeter`.
- **Model** Host owns the job: employment/fare/payment state broadcast; fare payout → shared wallet via host validation; passenger NPC (the fare) streams like an NPC. Job enable/disable of the taxi vehicle is host-owned world state.
- **Protocol** `TaxiJobState`/`Intent` in `101–119`; +version; PROTOCOL.md. **Touch** new `Sync/TaxiJobSync.cs`; reuse `JobSiteSync` shape. **Done when** a completed fare pays the shared wallet and both see taxi enabled/positioned. **Watch** A/B/C, E. **Deps** 2.4.

#### 3.3 · Kilju: fermentation + selling  `TODO`
- **Impact** MEDIUM. Bucket ferments over days (host time); selling to the buyer NPC pays into the shared wallet on un-synced quality.
- **Game truth** `EQUIPMENTS/bucket(itemx)::Use` (`Alcohol/Sweetness/Vinegar/BrewTime/Yeast/Sugar/Water`, `KiljuFinished/LidOn`), `TriggerIngredients` (`SUGAR/YEAST`), `TriggerFill`; sell at `JOBS/JOKKE/HouseDrunkNew/KiljuBuyer/CanTrigger::Logic` (`KiljuValue/Price`) + `PayMoney::Use`.
- **Model** The bucket's brew state is host-owned world state (it advances on the host clock): stream it as tracked-item scalar state (like `FluidContainerState`) so both see the same brew; ingredient adds are anyone-triggers/ownership intents; selling routes payout through the host wallet and derives price from the *agreed* brew state.
- **Protocol** extend `FluidContainerState` or add `BrewState` in `101–119`; +version; PROTOCOL.md. **Touch** new `Sync/KiljuSync.cs`; catalog ingredient triggers. **Done when** brew quality + sale payout agree. **Watch** B/C, D, E. **Deps** none.

#### 3.4 · Hitchhiker rides  `TODO`
- **Impact** MEDIUM. Ride-for-money payout is local; the passenger NPC riding one player's car is invisible to others; dangerous variants (KiljuMurderer) branch per-client.
- **Game truth** `JOBS/KILJUGUY/HikerPivot/Hitchhiker::Logic/Timer` (`Money/Paid`), `suitcase(itemx)/Money::Use`, `KiljuMurderer` spawn, `Hitchhiker::Save` (persistent stage: DrunkStage/Story).
- **Model** Host owns the hitchhiker's state + spawn (which variant, story stage) and streams it; boarding a car is a passenger-NPC event (reuse `PassengerController`/NPC streaming); payout → host wallet.
- **Protocol** reuse `NpcTransform` for the body + a small `HitchhikerState` in `101–119` for stage/payout; +version; PROTOCOL.md. **Touch** `NpcTrafficSync` (register the hiker) + new state. **Done when** the same hiker/variant rides + pays on both. **Watch** C, E, F. **Deps** none.

#### 3.5 · Farm job (hay / combine)  `TODO`
- **Impact** MEDIUM. Job stage + payout local.
- **Game truth** `JOBS/Farm/Farmer/Walker::Speak` (`JobStage`, `HAYBALE/COMBINE/PAYMENT`), `Farmer/…/PayMoney::Use` (`Money/RandomCash`).
- **Model** Mirror `JobSiteSync` (host-owned job stage + payout to wallet). **Protocol** extend `JobSiteState` (id 56) with a farm kind, or a new job state in `101–119`; +version; PROTOCOL.md. **Touch** `JobSiteSync`. **Done when** stage + payout agree. **Watch** A/B/C. **Deps** none.

#### 3.6 · Kela welfare / unemployment income  `TODO`
- **Impact** MEDIUM. Recurring benefit income keyed to per-client claim status.
- **Game truth** `Systems/Expenses::Kela`; `Sheets/{UnemployPaper,UnemployApplication,JobContract,UnemployCancelPaper}` (BUY-guarded repayment).
- **Model** Host owns claim status + benefit calc; benefit payment/repayment route through the shared wallet; broadcast claim state. **Protocol** `WelfareState` in `101–119` + catalog the pay buttons; +version; PROTOCOL.md. **Touch** new `Sync/WelfareSync.cs`. **Done when** benefit income agrees. **Watch** A/C. **Deps** none.

### Phase 4 — Crime & consequence

#### 4.1 · PlayerWanted crime counters + wanted level  `TODO`
- **Impact** MEDIUM–HIGH. `ManSlaughter/AttemptedManslaughter/PoliceEvasion/TrafficFatality/DaysFines` accrue per-client; only the fine byte reaches the host.
- **Game truth** `Systems/PlayerWanted::Activate` (int counters, `Sentence/DaysInJail`), crime entry via `HumanTriggerCop/HumanTriggerCrime::CarHit:Crime`.
- **Model** Host-validated record (mirror `PoliceSync`): the guest reports a locally-observed crime (validated by fresh pose + the exact known crime trigger); the host owns the wanted-level counters and broadcasts them. This is the foundation for 4.2–4.4.
- **Protocol** `WantedState`/`CrimeReport` in `93–99`; +version; PROTOCOL.md. **Touch** extend `PoliceSync` or new `Sync/WantedSync.cs`. **Done when** wanted level converges. **Watch** A (advance on host ack), C (retire on served), E. **Deps** none.

#### 4.2 · Arrest + jail sentence  `TODO`
- **Impact** HIGH. Whole arrest→jail flow is offender-local: one player jailed while the other roams free; the JAIL interior (day-countdown, confinement, cousin/food/sleep) runs only on the jailed client.
- **Game truth** `Systems/PlayerWanted` `Check wanted→Activate cops` spawns `COPS/CopHome*`/`PoliceCarPSK`; `JAIL/Functions::Time` (`DaysLeft/Sentence`), `MAP/JailSafeTrigger`, `GUI/HUD/Jailtime`.
- **Model** Host owns the sentence: when the host's wanted logic decides an arrest, broadcast a jail record (who, sentence, release day); the jailed player's confinement is host-driven; peers see them removed from the world for the duration (like a scoped despawn) and returned on release. Arrest is a host-owned flow (PLAN already names arrest/impound as needed).
- **Protocol** `JailState` in `93–99`; +version; PROTOCOL.md. **Touch** `Sync/WantedSync.cs`. **Done when** a jailed player is jailed on all machines and released together. **Watch** C (release edge), E. **Deps** 4.1.

#### 4.3 · Vehicle impound / tow  `TODO`
- **Impact** MEDIUM. Car relocated during arrest has no host-owned flow; the car is a shared object.
- **Model** Host performs the impound (moves/locks the vehicle) and broadcasts the relocation via the existing vehicle ownership/transform path. **Protocol** likely none (reuse vehicle reconciliation) or a flag; PROTOCOL.md if wire changes. **Touch** `VehicleWorldSync`, `WantedSync`. **Done when** an impounded car is relocated for all. **Watch** E. **Deps** 4.2.

#### 4.4 · Police chase + DUI arrest path  `TODO`
- **Impact** MEDIUM. Chase trigger/target/siren decided per-client; DUI escalation-to-arrest unsynced (only the fine byte).
- **Game truth** `TargetDispatch1/2::Check` (`Chase`), `POLICECAR*/Navigation::Chase`, `Sirens`, `CheckPoint::Traps`; `CopAlc1/2` (Alcometer→Fine→ArrestWarrant).
- **Model** Host decides + owns the pursuit (whom it targets, siren/trap state) and streams the cop car (already NPC-streamed) plus the chase-active flag; DUI stop escalates through the 4.1/4.2 wanted/arrest record, not just the fine bit.
- **Protocol** small `PursuitState` in `93–99` or fold into `WantedState`; +version; PROTOCOL.md. **Touch** `WantedSync`, `NpcTrafficSync`. **Done when** a chase triggers on all peers against the same target. **Watch** C, E. **Deps** 4.1.

### Phase 5 — Racing completeness

#### 5.1 · Rally results/reward + enroll + parc-fermé  `TODO`
- **Impact** HIGH. `RallySync` syncs only stage timing; the scoring/reward ledger, enroll, and penalties are unsynced.
- **Game truth** `RALLY/ResultsWeekend::Data` (registration, class, `TimeSS1-3`, placement, `GOLD/SILVER/BRONZE→Money`, `PriceMoney`, `Winner/RaceOver`, ~30 `UniqueTag*`); `RALLY/Scenery/RegisterRally::Activate` (`Registered`); `RALLY/ParcFerme::Logic` (day-gate + `TimePenalty`).
- **Model** Host owns the results ledger + enroll + penalties (mirror `IceRaceResultsSync`/`RallySync`): the host computes placement/reward from the agreed stage times it already owns and broadcasts the result board + reward (reward via wallet). Enroll is a host-owned record.
- **Protocol** `RallyResultsState` + enroll in `93–99` (near ice-race results 91); +version; PROTOCOL.md. **Touch** `RallySync`, new results sync. **Done when** both see the same standings + payout, and enroll agrees. **Watch** B/C. **Deps** none.

#### 5.2 · Rally PartsSalesman vendor  `TODO`
- **Impact** MEDIUM. Sells 12 rally parts for cash + SPAWNITEMs them; its `Buy` FSMs use non-standard state names (`Wait button 2`/`Wait`/`Purchase`/`Money`, no `Check money`) so the generic `shopBuy` catalog rule doesn't match (exact-match `FsmHook.FindState`).
- **Model** Add explicit catalog `buys[]` entries for each `Buy*` FSM with its actual state/entry-guard names; the spawned parts go through the item-spawn manifest (see 7.2). **Protocol** none (catalog + existing purchase/spawn). **Touch** `catalog/sync-catalog.json`. **Done when** buying a rally part debits the shared wallet and spawns for all. **Watch** spawn manifest (7.2). **Deps** 7.2 helps.

#### 5.3 · JOKKIS banger-race lifecycle  `TODO`
- **Impact** MEDIUM. The JOKKIS car is vehicle-synced, but its race lifecycle isn't — lap/time/result run per-client.
- **Game truth** `JOKKIS/DB/RaceTrigger::Data` (identical LAP/TIME/checkpoint/ENDRACE structure to CORRIS, which `IceRaceSync` hooks).
- **Model** Reuse `IceRaceSync`/`RallySync` directly — register the JOKKIS race markers and drive host-validated progress + result. **Protocol** reuse ice-race/rally messages if shape matches, else extend; +version if wire changes; PROTOCOL.md. **Touch** `IceRaceSync` (add JOKKIS markers) or a thin sibling. **Done when** both see the same JOKKIS standings + payout. **Watch** the rally/ice-race rejected-checkpoint stall (known limitation — edge-triggered crossings). **Deps** none.

### Phase 6 — Home & survival depth

#### 6.1 · Oven/stove cooking + fire hazard  `TODO`
- **Impact** MEDIUM. An unattended stove is a house-fire hazard; cooking state + burner knobs are local.
- **Game truth** `…/OvenStove/Simulation::Data` (Grill/Fire/Smoke), `KnobPower1-4/KnobTempOven/KnobModeOven::Screw`, `SausageTrigger`.
- **Model** Host-owned appliance state (knob settings + cooking/fire sim broadcast; knobs as anyone-triggers controls). Cooked-food item via spawn manifest.
- **Protocol** `ApplianceState` in `93–99` (generalize for oven/fridge) + catalog knobs; +version; PROTOCOL.md. **Touch** new `Sync/ApplianceSync.cs`. **Done when** stove state + a house fire agree. **Watch** B/C, D. **Deps** 1.3 (shares electricity/blackout).

#### 6.2 · Electric home sauna  `TODO`
- **Impact** MEDIUM. Only the cottage wood sauna is registered in `HeatSourceSync`; the home electric sauna isn't.
- **Game truth** `YARD/Building/SAUNA/Sauna/Simulation::Time` (`SaunaHeat/StoveHeat/Power/Fuse/ELEC_CUTOFF`), `Kiuas/ButtonTime::Screw`, `Kiuas/StoveTrigger::Steam`.
- **Model** Add it to `HeatSourceSync`'s registration list (its 5-entry hardcoded set) — same State/Intent path as the other heat sources; it also consumes `ELEC_CUTOFF` (ties to 1.3).
- **Protocol** none (reuses `HeatSourceState/Intent`). **Touch** `HeatSourceSync.cs` registration. **Done when** the electric sauna heats identically on peers. **Watch** D (verify var types), E. **Deps** 1.3 for cutoff.

#### 6.3 · Incoming phone-call events  `TODO`
- **Impact** LOW–MEDIUM. Which topic rings + when is per-client RNG/time.
- **Game truth** `HOMENEW/…/Telephone/Logic/PhoneLogicNEW::Ring`, `RingingNEW::Ring` (per-client `JokeEvent`, `RingTimes`).
- **Model** Host decides the call (topic + time) and broadcasts a ring event; guests replay it (audio/subtitle local). **Protocol** small `PhoneCallEvent` in `101–119`; +version; PROTOCOL.md. **Touch** new sync or fold into an events sync. **Done when** both phones ring for the same call. **Watch** C. **Deps** none.

#### 6.4 · Fridge chilling · appliance consumption · fuses  `TODO`
- **Impact** LOW–MEDIUM. Fridge chilling gates food freshness (elec-aware); per-appliance consumption feeds the bill; individual fuses (per-room) — only the aggregate MainSwitch is cataloged (PLAN defers individual fuses).
- **Model** Fold into the `ApplianceState`/`UtilityBillSync` from 6.1/1.3: host-owned consumption + fridge chilling; per-room fuse bools as anyone-triggers if pursued (else keep deferred). **Protocol** extend `ApplianceState`; +version if wire changes; PROTOCOL.md. **Touch** `ApplianceSync`, `UtilityBillSync`, catalog fuses. **Done when** fridge/consumption/fuses agree. **Watch** B/C. **Deps** 1.3, 6.1.

### Phase 7 — World & wildlife

#### 7.1 · Moose death → corpse → meat chain  `TODO`
- **Impact** HIGH. Moose position streams, but collision→dead transition, corpse pose, and moose-meat spawn fire only on the hitting client. (Also closes the 0.1 doc bug.)
- **Game truth** `Collider::CarHit` (TRIGGER ENTER, activates `dead moose(xxxxx)` ragdoll locally); `dead moose::Chop` → `Spawner/CreateMooseMeat::MooseMeat` (SPAWNITEM `moosemeat0`).
- **Model** Host owns the moose life→death transition and the corpse: broadcast the death edge + corpse pose (reuse the moose's existing `ScriptedMover`/`NpcTransform` streaming, extended with a dead flag); the meat spawn goes through the `ItemSpawn` manifest (register `CreateMooseMeat` as a spawn container — see 7.2).
- **Protocol** extend `NpcTransform` with a dead flag or a small `AnimalDeath` event in `101–119`; meat via `ItemSpawn`; +version; PROTOCOL.md. **Touch** `NpcTrafficSync`, `ItemWorldSync.Spawn`. **Done when** the moose dies + yields meat identically for both. **Watch** C, E, F. **Deps** 7.2.

#### 7.2 · Spawner manifest completeness  `TODO`

- **v119 guest alternator adjustment implemented; multiplayer checks pending:**
  both VIN133 and ALTERNATOR0 expose scroll adjustment on owned guest copies with
  a freshly seeded, loosened adjusting bolt. Requests share the part-operation
  ledger, so retries and stale competing clicks cannot apply another turn. The
  host validates native mount/part ownership, fresh nearby player, bolt/pick/hand
  readiness and 0.1-second cooldown, then uses native 0.5-degree turns within 0–7.
  Acceptance verifies the saved part, engine mount and pivot agree; state 185
  updates guest presentation. Native guest HandRotate remains disabled. 1,036
  protocol/catalog/policy tests, 18 launcher tests and 46 isolated Unity/Wine checks
  pass; Core and Net builds are clean. **Next:**
  two-player adjustment/bolt/fit/reconnect checks, other continuous adjustments,
  operational guest engine references and non-box families; see BUILDING.md.

- **Guest part/mount isolation implemented after 0.1.32; multiplayer checks pending:**
  the 30 catalogued boxed-part families retain saved originals in inactive storage
  and release their IDs for host copies. Existing same-ID originals no longer
  silently bypass host state. Settled native mounts pause with their original
  references intact; fitting previews use host occupancy, including pending copies.
  Saved FSMs/item motion are excluded while loading/isolation is pending. Passive
  PartState views keep host checksum data without native action replay. Cleanup
  restores original parent/pose/physics/FSM state after owned copies are removed;
  unexpected nested/unsupported occupants remain preserved. The 30 prefab leaf
  structures are verified; 1,014 automated tests and 28 isolated Unity/Wine checks
  (7 restoration, 21 save-library) pass. **Next:** full two-player
  mount/identity/checksum/reconnect checks, engine references/continuous adjustments,
  and non-box families. Save protection still requires restart before personal play.

- **Guest save protection implemented after 0.1.32; native gameplay checks pending:**
  Core guards native ES2 persistence before admitting guests, covering normal saves,
  LowMemory temporary files, whole-file/tag/folder deletion, renames and direct
  stream storage. Protection lasts until restart, including shutdown and reconnect;
  protected processes cannot host. Host/solo persistence and ordinary PlayerPrefs
  settings remain available. All 21 isolated Unity/Wine save-library checks and
  1,011 protocol/catalog/policy tests pass; Core and Net builds are clean. No wire
  change (protocol 118). Part/mount isolation is now implemented above; continuous
  adjustments/engine behavior and non-box creation remain open. Actual native save/quit,
  permadeath and two-player checks remain pending; see BUILDING.md.

- **Guest replacement bolt controls implemented after 0.1.32; gameplay checks pending:**
  55 integer-step controls across 24 replacement families reuse the native spanner/
  ratchet interface and the existing protocol-118 bolt contract. Owned input/pose
  graphs send host intents without guest native deltas or engine/timing actions.
  Fitted trigger picks require fresh host bolt state after each attachment; stale
  parked observations cannot enable a refitted part. Absolute replies update arrays,
  poses and ordered parent totals; teardown removes owned registries and triggers.
  1,008 protocol/catalog tests pass and Core/Net builds are clean. **Next:** guest
  mount isolation, continuous adjustments/engine behavior and non-box creation.
  Native two-player/LOD/save validation remains pending; see BUILDING.md.

- **v118 guest array-slot fitting implemented, gameplay checks pending:**
  pistons, main bearings and rockers now use the native shared Installer and their
  catalog-bound arrays. The click's slot index is immutable and must match host
  nearest-slot selection; equal distances choose the later index and occupied
  nearest slots are rejected rather than skipped. Native selection and mount
  prerequisite guards confirm only the same candidate/slot in the same frame.
  Unconfirmed previews cancel immediately; committed installation remains native
  and must settle at that slot before acceptance. Asset evidence validates all 17
  mounts, completing fitting adapters for the 30 boxed replacement families.
  Their replacement creation no longer requires the absent Installed scratch flag;
  positive native AssemblyID remains authoritative. 983 protocol/catalog and 18
  launcher tests pass; Core and both Net targets build cleanly. **Next:** guest mount/save
  isolation and operational replica bolt/engine graphs. Native two-player/save
  checks and non-box part creation remain pending; see the v118 checklist in BUILDING.md.

- **v117 guest replacement removal implemented, gameplay checks pending:**
  empty-handed guests can right-click a fitted replica to request native removal.
  The host checks revision, fresh living-player proximity, tightness below one,
  enabled native removal collider and actual mount ownership, then guards the
  native Remove entry and waits for the settled loose body. Installation/removal
  share one immutable receipt ledger; duplicates cannot reexecute and stale clicks
  cannot remove a refitted part. State 185 includes revisioned RemovalAllowed;
  guest ray selection reads disabled BoxColliders without enabling guest physics.
  Native bindings validate all 30 replacement families, including dynamic fitted
  piston/bearing/rocker mounts. 960 protocol/catalog and 18 launcher tests pass;
  Core and both Net targets build cleanly. v118 adds multi-slot
  installation selection. **Next:** guest mount/save isolation and operational replica
  bolt/engine graphs. Native two-player/save checks remain pending; see the v117
  checklist in BUILDING.md.

- **v116 fixed-mount guest replacement fitting implemented, gameplay checks pending:**
  guest-created loose copies offer the normal left click when held at a known free
  mount. The host validates observed revision, fresh living-player proximity, item
  authority and native distance, then executes ASSEMBLING and the native mount's
  prerequisite checks. Requests receive immutable receipts; retries cannot repeat
  fitting, and Busy requires another click. Native installation and settled state
  185 own the result. Generic guest replacement-part state replay is refused.
  Extracted bindings validate 27 fixed-mount families; 930 protocol/catalog and 18
  launcher tests pass. v117 adds guest removal. **Next:**
  guest mount/save isolation and
  operational replica bolt/engine graphs. Two-player/save checks remain pending;
  see the v116 checklist in BUILDING.md. v118 adds piston/main-bearing/rocker selection.

- **v115 fitted replacement presentation implemented, gameplay checks pending:**
  missing boxed-content copies can attach to a ready, unoccupied mount on a known
  native part or vehicle and follow its movement. Host publication waits for the
  mount reference and actual parent to agree. Relative pose/scale and parent identity
  participate in revision validation; stale replays cannot reattach a removed part.
  Fitted copies are non-colliding/kinematic and excluded from item/cargo authority;
  removal restores loose tracking and the current host pose. Unready/occupied mounts
  defer, preserving existing guest-save occupants. Owned child copies detach before
  parent retirement or reconnect cleanup. 896 protocol/catalog and 18 launcher tests
  pass; Core and both Net targets build cleanly. Native action/reference evidence
  covers the 30 replacement families, including the dynamic piston/bearing/rocker
  mounts. v116 adds fixed-mount installation requests; v117 adds removal. **Still open:** guest mount
  isolation, followed by operational engine/bolt graphs for these copies. They remain
  presentation-only while fitted. Non-box parts and native two-player/save checks
  are also unfinished; see the v115 checklist in BUILDING.md.

- **v114 existing native bolt reconciliation implemented, gameplay checks pending:**
  guests send nearby turns to the host; their scalar reports cannot overwrite host
  state. Absolute results restore Bolts[Index], scaled TightnessF and parent total,
  including zero. Join/targeted resync and delayed binding use the same application;
  local receipt ordering stops an older deferred sibling or part report from rolling
  back the latest total. Derived Bolted/Unbolted/Stop replay is removed. Persistent
  identities now include 36 parts without Installed, covering 192 native Data prefabs.
  The asset audit validates 489 ordinary bolt bindings; MUDFLAPa0's ThisPart is
  unresolved, and three drain/alignment controls need separate adapters. Crank-pulley
  and camshaft ordinary turns work through this adapter; guest extra-turn timing
  adjustment is guarded pending separate replication. 872 protocol/catalog and 18
  launcher tests pass; Core builds cleanly. **Next:** construct missing fitted guest
  graphs and mount references; then complete separate adjustments and original
  guest-save isolation, and run the v114 two-player/save checklist. This does not
  enable bolt interaction on the isolated loose replacement copies from v111.

- **v113 native fitting lifetime fixed, gameplay checks pending:** native fitting
  removes Rigidbody while preserving Data/save identity. This now removes loose-item
  authority without broadcasting permanent disposal. Saved fitted parts are discovered
  through Data; removal rebinds their new body. Fitted state keeps publishing and
  answering targeted queries without an item-table entry. Item transforms, cargo and
  loose pose snapshots cannot move a fitted/transitioning part. Message 185 Installed
  means positive AssemblyID; the native bool is occupancy-query scratch data. Body
  replacement also marks publication dirty. 838 protocol/catalog and 18 launcher
  tests pass; installed action evidence verifies the alternator body lifecycle and
  all 30 replacement assembly-ID transitions. **Still open:** complete guest fitting,
  mount/bolt reconstruction, existing guest-save isolation and two-player verification.

- **v112 guest box opening implemented, native checks pending:** guests request
  openings using a session token, request sequence, stable box ID and observed
  revision. The host validates fresh proximity/ownership, current quantity and a
  ready one-output factory. A guard serializes native host/guest box openings before
  quantity or PartSpawnPoint changes. Retries cannot rerun a reserved operation;
  acceptance verifies the exact next native part ID and one quantity decrement.
  Box/part state (or retirement) is replayed with receipts. Guest interaction never
  calls its own contents factory; audio plays once on acceptance. An already-disposed
  box or output does not cause a completed creation to repeat. 822 protocol/catalog
  tests and 18 launcher tests pass, including contention on the last part, lost
  receipts, stale requests, sequence wrap and reconnect tokens. All 30 opening
  action/variable/target bindings match installed build 23268598. **Still open:**
  two-player opening/disposal/save tests, complete fitted-part/bolt reconstruction
  and the other spawner types below.

- **v111 loose replacement implementation, native checks pending:** all 30 box-content
  factories capture every native output, including saved loads and multi-product
  loops. ReplacementPartState (185) publishes fresh identity/pose, assembly status
  and native saved floats (wear, tightness and type-specific adjustments). Missing
  loose parts are created on guests with isolated persistence, normal item carrying
  and no native assembly authority. Existing saved parts are not replaced. A host
  installation hides/unregisters the loose copy; removal exposes it at the fresh
  host pose. Installed missing parts remain pending. Disposal preserves host save
  cleanup and waits for an accepting host echo on guests. Join/resync reads do not
  consume delta baselines. Static extraction verifies all 30 factory/ref/initialization
  chains; 797 protocol/catalog and 18 launcher tests pass. **Still open:** guest box
  opening, full installed-part/mount/bolt reconstruction and native two-player checks.

- **v110 native part identity implementation, native checks pending:** root bodies
  use native Data/ID with matching assembly/position save keys. Part, bolt and child
  control FSMs use that part identity plus a relative child path, independent of
  root rename, reparenting and peer scan order. Initialization waits for the native
  save identity; invalid/duplicate owners are rejected. Grocery spill cloning cannot
  capture or clone a native part graph. Generic FSM teardown now removes owned
  hooks and registration marks, allowing reconnect and retry without duplicate
  callbacks. Static evidence confirms the save-key pattern across 156 part prefabs;
  tests cover 4,500 body/Data/bolt IDs, native zero-counter IDs, shuffled packet
  routing and root rename/reparent. **Still open:** materializing missing part
  contents, guest opening, complete install/bolt/save replication and native checks.

- **v109 package creation/quantity implementation, native checks pending:** all 30
  native factories capture the exact New box and register it after initialization.
  Saved and newly created boxes share stable factory/Use.ID identities, quantities
  and exact cataloged prefabs. PackageState (184) supports late join, item resync,
  targeted replies and missing-replica repair; equal-revision refresh cannot change
  contents or resurrect retired boxes. Snapshot reads cannot swallow broadcasts to
  existing peers. Guests preserve local boxes separately and pause native factories;
  replica startup skips load and disables persistence. Host disposal retains native
  save cleanup; guest disposal removes the replica. Guest opening shows a local
  request to ask the host instead of spawning unshared parts. Automated tests cover
  identities, capacities, wire framing, stale states, replay, retirement and session
  reset. **Still open:** guest opening intents, exactly-once contents creation,
  persistent replacement-part identity/installation/bolts, and two-player/save tests.

- **v106 implementation, runtime verification pending:** the 15 trophy factories
  under Amateur/Junior/Icerace/RallyAMA/RallyJR now capture the exact native `New`
  output after creation/name assignment, then wait for item initialization. Stable
  factory + native item IDs distinguish awards with identical visible names. Native
  saved awards are also discovered and included in join/resync manifests. Guests
  create the exact factory prefab with persistence disabled, pause their factory
  after load, and hide/preserve/restore local saved trophies across disconnect.
  Per-factory binding failures are contained and logged. The installed factory and
  prefab action layouts were verified; protocol/catalog identity and replay tests
  pass. Follow `BUILDING.md`'s v106 checklist for the outstanding native checks.
  **Still open:** moose meat (death/cooking/spoilage state), parts packages (contents
  and opening), spray cans (paint/remaining contents), missing race outcome authority,
  and two-player/save-reload validation. The old claim that item-position snapshots
  alone created these missing items for late joiners was incorrect and is removed.

- **Package recon (v107):** 30 standard package factories reference a boxed prefab
  and a `CARPARTS/PARTSYSTEM/SPAWNERS_*` contents factory. `BrakeBiasRegulator` is a
  direct part, and `Plugwires` has null prefab/spawner references in this build.
  Standard `Use/Create Plug` decrements Quantity, sets global PartSpawnPoint and
  sends SPAWNITEM to its referenced part factory; Empty retains a native save-delete
  lifecycle. Part factories mint native IDs, assign installation references and
  initialize wear. These cannot safely use a display-name-only box clone. This trace
  exposed the now-fixed scalar corruption in R2.28; contents replication
  remains the next dependency, with actual prefab evidence in BUILDING.md.
- **Impact** MEDIUM (correctness of item spawning at large). The `Spawner` root (101 FSMs) is the item factory; `CreateMooseMeat` is proven unregistered, so the manifest isn't exhaustive.
- **Model** Systematically diff every `Spawner/*` SPAWNITEM child against `ItemWorldSync.Spawn`'s registered-container list; register the missing ones (parts packages, bag contents, trophies, moose meat) so any host-minted spawn materializes on peers. Log any deliberately-excluded spawner.
- **Protocol** `ItemSpawn` factory flag in v106; `PackageState` 184 in v109; `SpawnIntent` remains for bags. **Touch** `ItemWorldSync.Spawn`, `.Factories`, `.PackageFactories`, `.PackageReplicas`, catalog `trophyFactories` / `partsPackages`. **Done when** every gameplay spawner is registered or explicitly excluded with a reason. **Watch** the spawn-sync design notes ([[spawn-sync-gap]] equivalent). **Deps** none. Unblocks 5.2, 7.1.

#### 7.3 · Crime / reaction NPCs  `TODO`
- **Impact** LOW. `SOCCER/Janitor "Reijo"` (anger accumulator, `CRIME/PLAYER/SHOOT`, can shoot) and `HUMANS/FighterPub/Fighter2` react to player/car crime; only pose (if streamed) syncs.
- **Model** Register these NPCs in `NpcTrafficSync` (pose) and drive their reaction state (anger/fight/shoot) host-authoritatively; crime they register feeds 4.1. **Protocol** reuse `NpcTransform` + a small reaction flag; +version if wire changes; PROTOCOL.md. **Touch** `NpcTrafficSync`. **Done when** Reijo/fighter react identically. **Watch** C, F. **Deps** 4.1 helps.

### Phase 8 — Player polish

#### 8.1 · PlayerAlco (BAC) reconnect persistence  `TODO`
- **Impact** LOW. The mod restores `DrunkCurrent` (visual) on rejoin but not the underlying persistent `PlayerAlco` BAC that drives DUI checkpoints + sobering.
- **Model** Append `PlayerAlco` to the needs report + guest profile (mirror the Dirtiness v55 add: wire field + `HasX` gate + sidecar column). **Protocol** extend `PlayerNeedsReport` (id 25) — +version; PROTOCOL.md + round-trip test. **Touch** `PlayerNeedsSync.cs`, `PlayerProfileMessages.cs`, `GuestProfileStore.cs`. **Done when** BAC survives rejoin. **Watch** the needs-report coupling class (don't block other needs on a late bind). **Deps** none.

#### 8.2 · Winter jacket / coverall worn state  `TODO`
- **Impact** LOW. `EQUIPMENTS/winter jacket(itemx)`/`winter coverall(itemx)` use a separate `ClothType` int from `ClothingSync`'s `ClothingStage/ClothingType`; the worn garment shows only on the wearer.
- **Model** Extend `PlayerClothingState` to carry the worn winter-garment (owner→host→peers) and render it on remote avatars. **Protocol** extend id 32; +version; PROTOCOL.md. **Touch** `ClothingSync.cs`, avatar. **Done when** a worn jacket shows on peers + affects the warmth tier. **Watch** E (per-player guard). **Deps** none.

#### 8.3 · Yard piss-stains  `TODO`
- **Impact** LOW. `YARD/PissAreas::Logic` scales persistent world-visible snow stains (`UTPissArea1-5`) on the PISS action; host-saved, shared-visible, unsynced.
- **Model** Host-owned world state: broadcast the stain level on change + join (small scalar state or an anyone-triggers event). **Protocol** small `PissAreaState` in `101–119` or a catalog event; +version if wire changes; PROTOCOL.md. **Touch** small sync or catalog. **Done when** stains agree. **Watch** B/C. **Deps** none.

#### 8.4 · In-car radio / CD  `TODO`  *(lowest; cosmetic-adjacent)*
- **Impact** LOW. In-car radio/CD power + channel/track selection unsynced (home stereo IS synced). Track *content* is cosmetic; power/channel is minor shared state.
- **Model** Mirror `HomeStereoSync` scalar-state model for the in-car radio power/channel (skip the track audio — cosmetic). **Protocol** reuse the home-stereo shape or a small `CarRadioState` in `63–79`; +version if wire changes; PROTOCOL.md. **Touch** `VehicleWorldSync` or a sibling. **Done when** radio power/channel agree. **Watch** A/E. **Deps** none.

---

## Appendix — verified out of scope (do not build)

These were audited and are correctly *not* gaps — don't spend tasks on them:

- **In-game computer** + its fishing minigame (`COMPUTER/*`, `Kaappis-Fishgame`) — MSC-save-import toy, explicit post-1.0 parity backlog.
- **Water wells / kitchen taps** (`WaterWell`, `KitchenWaterTap`, jail `wcbowl`) — infinite fixtures feeding the per-player **Thirst** need; divergence is tolerable by the needs model.
- **Map clock + weather forecast** — already host-synced (snow/temp/hour via `TimeWeatherSync`); only the cosmetic cloud-sprite selection is re-homed by hour.
- **Jukebox track / CD track / weather cloud sprite** — cosmetic-only presentation; no gameplay desync.
- **Food spoilage** — no time-based decay FSM exists in this build (fridge *chilling* is 6.4, not decay).
- **Host migration** — out of scope by design (host quit ends the session).

When the game updates (Early Access), re-dump the catalog (F9) and re-run the coverage
audit against the new build before trusting this list — MWC patches break FSM names/paths.

#### Shopping bag duplication regression (v120, shipped in 0.1.33)

Player report on v0.1.32: host and guest could hold separate bags; the host's spill
was invisible until the guest opened theirs, then both sets appeared. Fixed the
peer-local bag identities/independent inventories, scanner capture race, missing
native-name template lookup and discarded failed materialization. New bag states
and atomic opening requests use 190–192; old guest SpawnIntent 53 is retired. Exact
factory outputs are reserved before scanning and split into 32-item packets.
Native pickup guards release losing/removed copies. Two-player acceptance still
required; mixed bags containing unsupported native parts remain guarded.
