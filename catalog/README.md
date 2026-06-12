# Sync catalog

Per-game-build data describing *what* gets synchronized (PLAN.md §4.2).

## Two files, two jobs

| File | Purpose |
|------|---------|
| `sync-catalog.json` | **Shipped with the mod.** Curated rules the runtime loads. |
| `dump-<buildId>.json` | **Dev reference only.** Full F9 dump (8k+ FSMs). Commit per game build; diff across patches. |

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

## Still heuristic in code

Vehicle gauge/system FSM binding (speedo, fuel, blinkers, revs) remains in
`WorldSyncManager.EnsureVehicleSystemsProbe` until moved into declarative bindings.
