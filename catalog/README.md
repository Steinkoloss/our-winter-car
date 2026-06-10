# Sync catalog

Per-game-build data describing *what* gets synchronized and *how* (PLAN.md §4.2).

## Workflow

1. Run the game with the `WinterMP.Tools` plugin and press **F9** in the world.
   A raw dump lands in `<game>\WinterMP\dumps\catalog-<timestamp>.json`
   (every PlayMakerFSM with states/events/transitions/variables + all rigidbodies).
2. Copy it here as `dump-<gameBuildId>.json` (build id from the Steam manifest,
   shown in the launcher).
3. Curate sync descriptors from the dump (`descriptors-<gameBuildId>.json`,
   format below — tooling for this lands in M3).
4. On a game update: re-dump, diff against the previous dump, fix descriptors
   for whatever moved/renamed, re-test.

## Descriptor format (draft, finalized in M3)

```jsonc
{
  "gameBuild": "1234567",
  "fsms": [
    {
      "path": "MAP/Buildings/Home/Door",   // scene path; netId = fnv1a32(path + "::" + fsmName)
      "fsmName": "Use",
      "mode": "anyone-triggers",            // host-only | anyone-triggers | owner-only
      "syncedEvents": ["OPEN", "CLOSE"],
      "syncedVariables": [
        { "name": "DoorAngle", "type": "float", "rateHz": 5, "threshold": 0.5 }
      ]
    }
  ]
}
```

Raw dumps may be large; commit them — diffing across game builds is the whole
point (Early Access survival strategy, PLAN.md §6).
