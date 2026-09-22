# Sync catalog

Per-game-build data describing *what* gets synchronized (PLAN.md §4.2).

## Two files, two jobs

| File | Purpose |
|------|---------|
| `sync-catalog.json` | **Shipped with the mod.** Curated rules the runtime loads. |
| `dump-23268598.json` | **Dev reference** for Steam build 23268598 (GAME scene, post-sleep F9 dump 2026-06-13). Full F9 dump (~8600 FSMs). ⚠ `toolsVersion 0.1.0` — **no `actionTypes`, no `globalTransitions`**; re-dump with tools ≥ 0.2.0 before relying on it for sync work. Diff across patches with `tools/catalog_diff.py`. |

## Rule sections in `sync-catalog.json`

| Section | Registers as | Example |
|---------|----------------|---------|
| `doors` | Door handles (house, car, sauna) | Open door / Close door; garage Open/Close |
| `controls` | Buttons, knobs, interactables | SORBET hazard, beercase Remove bottle, Fleetari brochure Set job / On / Switch |
| `switchRules` | ON/OFF style toggles | Lights, radiators, fireplaces, TV |
| `ignitions` | Key/ACC FSMs | `IGNITION` objects with ACC on / Motor OFF |
| `starters` | Engine run/stall FSMs | SORBET/CORRIS Starter, CORRIS Pushstart |
| `buys` | Host-authoritative purchases / payments | Inspection, shops (`template: shopBuy`), Fleetari, cash register |
| `parts` | Car-part assembly Data FSMs | `(VINXX)` bolt on/off, install, remove |
| `bolts` | Wrench Screw FSMs | Tight? / Loose? / Set pos |
| `vehicles` | Rigidbody roots treated as vehicles | `minMass`, `namePrefixes` |
| `pickables` | Synced item rigidbodies | `(itemx)` suffixes, optional Use FSM probe |
| `consumables` | Food/drink despawn hooks | Destroy + drink empty states |
| `vehicleClimate` | Frost/heater FSM path filters | SORBET / CORRIS car-temp roots |
| `stoves` | Host-owned home-stove controls, heat and cooking presentation | Both OvenStove roots; validated native knob, simulation and smoke actions |
| `atfRefill` | Host-owned ATF source/destination transfer and filler controls | Native ATF bottle, installed Corris automatic gearbox, cap and gauge bindings |

Shared rule fields:

- `pathPrefix` — scene path must start with this (optional)
- `pathContains` — substring match (optional)
- `objectName` — exact GameObject name (optional)
- `objectNameContains` — substring in GameObject name (optional)
- `fsmName` — required PlayMaker template name
- `states` — synced states (all must exist on the FSM)
- `requireStates` — extra states that must exist but are not synced (switch shape checks)
- `excludePathPrefixes` — skip paths starting with these prefixes

`buys` entries use `entryGuards` (`state` + `event`, optional `optional: true`) and
`resultStates`, or `template: shopBuy` to infer guards from a generic `Buy` FSM.
`parts` add `optionalStates` when present on the FSM.

## Adding a rule

1. F9-dump the GAME scene (or use an existing dump under `<game>/WinterMP/dumps/`).
   Schema v2 dumps also include each state’s PlayMaker action type names, which are
   essential when a transition shape alone cannot prove whether it charges money,
   rolls RNG, spawns an object, or only updates presentation.
   **Check `meta.toolsVersion` before trusting a dump.** `0.1.0` has neither
   `actionTypes` nor `globalTransitions`; `0.2.0` has both. Without
   `globalTransitions` an event can appear in `events[]` with no state pointing at
   it and still be a live entry point — absence reads as "unknown", not "none".
   `dump-23268598.json` is `0.1.0`, so it cannot answer either question.
2. Find the FSM: `python tools/extract_fsm_details.py dump.json show:ButtonFoo`
3. Add an entry to the right section in `sync-catalog.json`.
4. Rebuild — `sync-catalog.json` deploys next to `WinterMP.Core.dll`.
5. Both players must show the same **cat** hash on the debug overlay (TAB).

Handshake refuses a catalog mismatch (same as protocol/mod/game version).

## Raw dump workflow

1. Run the game with `WinterMP.Tools` — auto-dump ~20s after level load, or **F9** manual.
2. Copy `WinterMP/dumps/catalog-<timestamp>.json` here as `dump-<gameBuildId>.json`.
3. After a game update: `python tools/catalog_diff.py dump-old.json dump-new.json`
4. Fix `sync-catalog.json` if paths or state names moved.

## Sleep / time skip (build 23268598)

From `dump-23268598.json` (post-sleep F9). Host sleep uses `SleepTrigger :: Activate`:

| Path | States (consent hooks) | Events |
|------|------------------------|--------|
| `HOMENEW/Functions/FunctionsDisable/Sleep/SleepTrigger` | `Confirm`, `Get positions` → `AnimateSleep` → `Sleep` → `Sleep time` | `ACTIVATE`, `STOP`, `ABORT`, `DAY` |
| `GIFU(...)/LOD/Sleep/SleepTrigger` | same template (in-vehicle sleep) | same |
| `CABIN/LOD/Sleep/SleepTrigger`, `COTTAGE/LOD/...` | same template | same |

Runtime: `PlayerSleepHook` hooks `Confirm` / `Get positions` on any `*/Sleep/SleepTrigger :: Activate`;
`Get positions` rolls back to `Confirm` until all guests accept. Post-sleep TimeSync fires on `Calc rates`.
Cancellation uses Confirm's `State 3` release destination and retires the request
before another answer can resume it. Static action extraction on 2026-09-12 confirms
`STOP` is local to `Conditions?`; global `ABORT` enters `Calc rates` and applies
wake-up needs, so neither is a valid pre-sleep cancellation route. Proceed after
consent: `ACTIVATE`. Guests get `SleepConsentResult` + fatigue reset on acceptance.

## Death / respawn (build 23268598)

| Path | Role |
|------|------|
| `Systems/Death :: Activate Dead Body` | Master death/orbituary FSM — v226 binds while inactive; `State 3` retires seats before controller destruction, `Take photo` is an idempotent fallback. `State 2` is the newspaper, followed by save and MainMenu, not a respawn. |
| `Interface/Buttons/ButtonContinue :: SetSize` | `Reset globals 2` loads global `PlayerPermaDeath` from `SavePlayerData` (`savefile.txt`) tag `PlayerPermaDeath`; v227 overrides this native LoadBool for connected guests before Finish. |
| `Licence/Buttons/ButtonBegin :: SetSize` | `Generate ID` saves global `PlayerPermaDeath` using `UniqueTagPermaDeath`; v227 observes the native host SaveBool and publishes the resulting session setting. |
| `Systems/Steam :: Achi` | Achievement bookkeeping — `_DEATHON` / `_DEATHOFF` when mirroring host permadeath on guests |

Runtime: v227 corrects the native permadeath binding. The handshake reads the
host's saved tag or loaded GAME variable; native host LoadBool/SaveBool changes
publish SessionSettings 213 to connected guests. Guest globals follow the host
through native loading and reconnects; no guest setting is persisted. Both modes
pass 21 local two-game setting checks. Full group-wipe acceptance remains open.
Normal death hides the avatar and runs the native newspaper/save/MainMenu flow.
v226 sends `PlayerRespawn` only after a loaded GAME player has enabled movement;
guests also finish spawn selection and relocation. See the
[live recovery audit](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-passenger-recovery-2026-09-13-unreleased-v226).

## Guest rejoin (build 23268598)

Host keeps stable `playerId` slots per SteamID for the session. On disconnect the host writes pose
to `wintermp-guests.json` (needs when `PlayerNeedsReport` has arrived). Reconnecting guests get
the same id, snapshot + `GuestSpawn` with last pose, and chat `* name reconnected`.

## Still heuristic in code

Vehicle gauge/system FSM binding (speedo, fuel, blinkers, revs) remains in
`WorldSyncManager.EnsureVehicleSystemsProbe` until moved into declarative bindings.


Per-feature binding notes (FSM quirks, native IDs, why a binding is shaped the way
it is) live in [BINDINGS.md](BINDINGS.md). Read the section for the system you touch.
