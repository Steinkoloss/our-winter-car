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

**Reserved protocol id ranges** (current version **v80**; allocate the next free id in-range):
`63–79` vehicles · `93–99` economy/appliances · `101–119` NPCs/jobs · `126–139` snapshot/bulk.
Ranges are tight — if a range fills, extend it in `IMessage.cs` and document it.

**Do NOT work on (verified out-of-scope — see §Appendix):** the in-game computer + its
fishing minigame, host migration, water wells/taps (per-player thirst), map clock/weather
(already synced), jukebox/CD-track/cloud-sprite (cosmetic), food spoilage (no decay FSM exists).

---

## 1. Progress

Tick when merged. Ordered by priority (shared-state corruption first).

> **Every box below is ticked — that does not mean the game is fully synced.** These 35
> tasks were the *2026-07-21 audit's* decomposition, and they are implemented but still
> largely **unplaytested**. A later re-audit found a whole unsynced background-economy
> cluster plus several subsystems that bind the wrong FSM variable and silently no-op.
> **Read §1b before concluding anything here is finished.**

**Phase 0 — Housekeeping (do first; cheap, unblocks others)**
- [x] 0.1 Correct the 4 `PLAN.md` coverage overstatements
- [x] 0.2 Fix the vehicle-registration `RequireRoot` gate (unblocks 2.4, 3.2) — structural `Simulation/Engine` check; adds only the taxi

**Phase 1 — Economy integrity** *(these corrupt the shared wallet / car ownership right now)*
- [x] 1.1 Gambling: pub & station slot machines — `GamblingSync` (93/94, v57), host-authoritative spin/payout
- [x] 1.2 Gambling: Ventti blackjack (incl. car wager) — `VenttiSync` reuses 93/94 (Kind=Ventti), host deals
- [x] 1.3 Electricity + phone bills → power cutoff / blackout — `UtilityBillState` (95, v58) + catalogued Pay buttons
- [x] 1.4 Lottery national draw + Lotto/Megaveto tickets — `LotteryDrawState` (96, v59) + catalogued ticket Pay buttons

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
- [x] 8.4 In-car radio / CD power+channel *(cosmetic-adjacent; lowest)* — `CarRadioState` (66, v80) host-owned channel/volume

---

## 1b. Round 2 — what the v80 "complete" claim missed

Every box in §1 is ticked, but a 2026-07-22 re-audit (and a 2026-07-24 dump-verification
pass) showed §1 is **not** the same thing as "every shared-state gap is closed". The tasks
above decomposed *interactive* systems; they missed the passive daily-tick `Systems/*`
economy, and several shipped subsystems bind the wrong FSM variable and silently no-op.

Work this list the same way as §1. **Priority order — the top group corrupts shared state.**

**R1 — Never synced at all** (verified: zero references anywhere in `src/WinterMP.Core`)
- [ ] R1.1 `Systems/BankAccount` (+ `Data/InterestRate`) — the bank balance is a *separate*
      variable from the cash `Money` global that `WalletSync` owns, and daily interest
      accrues per-client. Balances diverge permanently.
- [ ] R1.2 `Systems/Expenses::Rent` + `::Livingsupport` — `WelfareSync` binds only Kela.
      Weekly rent debit, the KICKOUT eviction (destroys furniture, relocates the player) and
      housing benefit are all unsynced.
- [ ] R1.3 VideoPoker / Rami-Pokeri (`PERAPORTTI/.../VideoPoker`) — a *third* money machine
      covered by neither `GamblingSync` (slots) nor `VenttiSync`. Per-client card RNG on the
      shared wallet.
- [ ] R1.4 `Systems/HockeyGames` incl. `Betting::Logic` + `Runkosarja` season sim — betting
      real money on standings each client simulates independently (`GoalsSimulated` RNG).
- [ ] R1.5 `Systems/ScrapMetalPrice` + `REPAIRSHOP/Scrapmetal/GarbageTrigger` — daily RNG
      scrap price and the scrap-selling payout.
- [ ] R1.6 `Sheets/DebtLetter` — an interest-bearing loan accruing per-client; payment is
      not wallet-routed.
- [ ] R1.7 `Database/Keys::PlayerKeys` (UncleStage / property keys) — progression + ownership.
- [ ] R1.8 Non-project vehicle cabin climate — `VehicleClimateConfig` hardcodes SORBET and
      CORRIS only, so taxi/GIFU/KEKMET/BACHGLOTZ cabins diverge. Violates the
      host-authoritative-climate rule.

**R2 — Synced but wrong** (shipped code that silently does nothing, or the wrong thing)
- [ ] R2.1 **`VenttiSync` guest hooks are observers, not suppressors.** `HookOnce` uses
      `FsmHook.OnStateEnter`, which *prepends* a callback but lets the state's own actions
      run — so a guest's Bet/Hit/Stand still resolve on local RNG. Money reconverges via
      `WalletState`, but the **SATSUMA car wager does not**: ownership diverges permanently.
      The class docstring's "any car transfer runs through the host's own GameManager" is
      false today. `GamblingSync` (slots) has the identical pattern — it only self-heals
      because slots move money and nothing else. Needs a real suppression primitive in
      `FsmHook` (`FleaSaleSync.SuppressLocalSaleRng`'s `fsm.enabled = false` is the closest
      existing precedent). **Fix both together.**
- [ ] R2.2 Moose killed by a *guest* never dies for anyone else — `NpcTransform` `FlagDead`
      is only ever set in the host's `UpdateHostMovers`; there is no guest→host death report.
      The guest sees a corpse while the host keeps streaming a live pose.
- [ ] R2.3 Appliance house-fire never propagates — `FlagFire` samples
      `Simulation::Data.ActiveStateName == "Fire"`, a one-frame transient in a continuous
      polling loop that rests elsewhere, so it is almost never true. Ignition stays per-client.
- [ ] R2.4 Vehicle late-join is climate-only — the join snapshot carries no engine/fuel/gear/
      damage/tire state, *and* the vehicle CRC folds only flags+fuel, so a parked damaged
      unowned car checksums identical on both peers and never resyncs.
- [ ] R2.5 `CarRadioSync` station never syncs — `Channel` is bound with `FindFsmFloat` but
      the radio's `Channel` is a Bool (the SORBET tuner float is named `Tune`), so the bind
      is null and only `Volume` works. Bug class **D**.
- [ ] R2.6 Guest-initiated sleep is ungated — `PlayerSleepHook.Probe` bails on `!IsHost`, so
      only the host hooks `SleepTrigger`. A sleeping guest skips its own clock (then gets
      snapped back by `TimeSync`) and fires time-gated FSMs locally.
- [ ] R2.7 Ice race broadcasts no standings (in-code known limitation; the guest→host path
      exists but there is no `ObserveHost` counterpart as rally has).
- [ ] R2.8 Taxi fare is lost for guests — but **not** for the reason it looks like. Adding a
      `JOBS/TAXIJOB/Customer1/TaxiWalker/.../PayMoney` controls rule *alone is a no-op*:
      (a) the host rejects the intent because `IsGuestNear` measures against the host's copy
      of the customer, whose pose is unsynced (the customer is not in `NpcTrafficSync`
      `MoverDefs`), and (b) the replayed button pays the *host's* `TaxiWalker::Logic` `Cost`,
      which is also unsynced. `TaxiFunctions::Payments` (what `TaxiJobSync` streams today) is
      the employment payday FSM, not the per-ride fare — fix that docstring too. Needs all
      three parts: catalog rule + customer `ScriptedMoverDef` + `Cost`/`Paid` on the wire.
- [ ] R2.9 `JOBS/Farm/Farmer` is a walking, position-unsynced NPC, so the farm `PayMoney`
      rule added in round 2 may also be dropped by the same proximity gate as R2.8. Same
      question for the hitchhiker rule, whose `Timer.Money` the host overwrites every 20 s.
- [ ] R2.10 Rally opponent cars run per-client — **do not "fix" this by streaming them.**
      Unlike ICERACE (16 opponents partitioned into four disjoint per-venue sets), RALLY has
      exactly **one** fleet, `RACES/RALLY/RallyCars/RALLYCAR{1,2,3}`, multiplexed across all
      three special stages by `RallyCars::AIdrivers` (int `Stage`, events SS1/SS2/SS3), which
      nothing syncs — while `RallySync` deliberately tracks stage progress *per player*. So
      "host on SS1, guest on SS3" is a supported state, and host-streaming those three bodies
      would freeze the guest's AI and teleport the guest's opponents onto the host's stage,
      hijacking the guest's own rally. Attempted and reverted 2026-07-24. Any real fix must
      sync `AIdrivers` `Stage` first and gate the stream on stage agreement.

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

#### 1.4 · Lottery + gambling tickets  `TODO`
- **Impact** MEDIUM. National draw + Lotto/Megaveto buy-in/payout are per-client → winners diverge, wallet flip-flops.
- **Game truth** `Systems/Lottery::Numbers` (`NationalPot/Winning`, `LOTTODRAW/WIN/RESULTS`); `Sheets/{LottoTicket,MegavetoTicket}/Pay` (BUY-guarded).
- **Model** Host draws the winning numbers and broadcasts them (host scalar-state); ticket buy-in + payout route through the host purchase/wallet path (catalog the Pay buttons). Guest ticket selection is local until the host draw resolves it.
- **Protocol** `LotteryDrawState` (host→guests) in `93–99`; buys via catalog; +version; PROTOCOL.md. **Touch** new `Sync/LotterySync.cs`; catalog. **Done when** both see the same draw + payout. **Watch** A, C. **Deps** none.

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
- **Impact** MEDIUM (correctness of item spawning at large). The `Spawner` root (101 FSMs) is the item factory; `CreateMooseMeat` is proven unregistered, so the manifest isn't exhaustive.
- **Model** Systematically diff every `Spawner/*` SPAWNITEM child against `ItemWorldSync.Spawn`'s registered-container list; register the missing ones (parts packages, bag contents, trophies, moose meat) so any host-minted spawn materializes on peers. Log any deliberately-excluded spawner.
- **Protocol** none (uses `ItemSpawn`/`SpawnIntent`). **Touch** `ItemWorldSync.Spawn`, spawn registration. **Done when** every gameplay spawner is registered or explicitly excluded with a reason. **Watch** the spawn-sync design notes ([[spawn-sync-gap]] equivalent). **Deps** none. Unblocks 5.2, 7.1.

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
