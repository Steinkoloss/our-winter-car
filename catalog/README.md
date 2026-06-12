# Sync catalog

Per-game-build data describing *what* gets synchronized (PLAN.md §4.2).

## Two files, two jobs

| File | Purpose |
|------|---------|
| `sync-catalog.json` | **Shipped with the mod.** Curated rules the runtime loads. Edit this to add vehicle dashboard controls. |
| `dump-<buildId>.json` | **Dev reference only.** Full F9 dump (8k+ FSMs). Commit per game build; diff across patches. |

## Adding a vehicle control

1. F9-dump the GAME scene (or use an existing dump under `<game>/WinterMP/dumps/`).
2. Find the FSM: `python tools/extract_fsm_details.py dump.json show:ButtonFoo`
3. Add a rule to `sync-catalog.json`:

```json
{
  "pathPrefix": "SORBET(190-200psi)/",
  "objectName": "ButtonFoo",
  "fsmName": "Use",
  "states": ["On", "Off"]
}
```

Optional `pathContains` narrows matches (handbrake lever, etc.). `objectName` can be omitted when `fsmName` is unique under the prefix (e.g. CORRIS `LightModes`).

4. Rebuild — `sync-catalog.json` deploys next to `WinterMP.Core.dll`.
5. Both players must show the same **cat** hash on the debug overlay (TAB).

Handshake refuses a catalog mismatch (same as protocol/mod/game version).

## Raw dump workflow

1. Run the game with `WinterMP.Tools` — auto-dump ~20s after level load, or **F9** manual.
2. Copy `WinterMP/dumps/catalog-<timestamp>.json` here as `dump-<gameBuildId>.json`.
3. After a game update: `python tools/catalog_diff.py dump-old.json dump-new.json`
4. Fix `sync-catalog.json` if paths or state names moved.

## Not in sync-catalog yet

Doors, bolts, ignitions, starters, and vehicle climate/gauge probes still use runtime heuristics in `WorldSyncManager`. Those can move into descriptors incrementally when the format grows (`syncedVariables`, climate probes, etc.).
