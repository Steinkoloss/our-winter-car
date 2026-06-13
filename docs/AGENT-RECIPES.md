# Agent recipes — common tasks

Copy these checklists when implementing features. Read `AGENTS.md` for constraints.
File locations: `docs/CODEMAP.md`.

---

## Add a protocol message

1. Bump `ProtocolInfo.Version` in `src/WinterMP.Net/Protocol.cs`.
2. Add enum value in `Messages/IMessage.cs` (**never reuse retired ids**).
3. Implement `IMessage` in `Messages/` (append-only fields).
4. Register in `Messages/MessageRegistry.cs`.
5. Update `protocol/PROTOCOL.md` (id, channel, layout, semantics).
6. Add round-trip test in `WinterMP.Net.Tests/` (follow existing `WorldMessagesTests` patterns).
7. Handle in `SessionManager.cs` `OnPacket` switch (or appropriate subsystem).
8. Bump `wintermp-compat.json` `protocolVersion` when shipping.

Run: `dotnet test src/WinterMP.Net.Tests`

---

## Add a catalog sync rule (door, shop, bolt, …)

1. Find the FSM in the dump — **do not read the whole JSON**:
   ```powershell
   python tools/extract_fsm_details.py catalog/dump-23268598.json "find:DoorName"
   python tools/extract_fsm_details.py catalog/dump-23268598.json "show:path/fragment"
   ```
2. Add a rule to the right section in `catalog/sync-catalog.json` (`doors`, `buys`, `bolts`, …).
   See field docs in `catalog/README.md`.
3. Rebuild Core — `sync-catalog.json` deploys beside `WinterMP.Core.dll`.
4. Both peers must show the same **cat** hash (hold TAB in-game).

Rules are data; runtime registration is in `FsmWorldSync.Registry.cs` + `SyncCatalog.cs`.

---

## Hook a PlayMaker FSM state (host-only or consent gating)

1. Locate FSM path/name with `extract_fsm_details.py` (see `catalog/README.md` sleep table as example).
2. Use `Sync/FsmHook.cs`:
   - `FsmHook.HasState(fsm, stateName)`
   - `FsmHook.OnStateEnter(fsm, stateName, callback)`
3. Resolve stable paths with `ScenePath.Of(transform)` when matching by path fragment.
4. Probe with `Resources.FindObjectsOfTypeAll<PlayMakerFSM>()` on a timer — objects may load late.
5. Wrap probe/update in try/catch; **never let one subsystem kill the whole plugin** (see `PlayerSyncManager`).

Examples: `PlayerSleepHook.cs`, `FsmWorldSync.Registry.cs`.

For **abort/proceed**, inspect the FSM's `events` list in the dump (`STOP`, `ACTIVATE`, …).

---

## Debug “players can't see each other”

1. Confirm **same mod + protocol version** on both machines (TAB overlay).
2. Check `<game>\BepInEx\LogOutput.log` for:
   - `PlayerSync: tracking local player`
   - `PlayerSync: avatar created for …`
   - `PlayerSync disabled after unhandled error` (fatal — paste full line)
3. Host only sends transforms when `PlayerCount > 0`; guests always send when connected.
4. Remote bodies: `RemoteAvatar`, `NpcCharacterFactory` — capsule fallback if walker missing.
5. Swimming move-state must not false-positive on dry land (`PlayerMoveStateReader`).

Dev without Steam: **F8** loopback or `-wintermp hostlocal` / `tools/Local2PTest.bat`.

---

## Game patch / new Steam build

1. F9 dump in GAME scene (after loading save) → `WinterMP/dumps/catalog-*.json`.
2. Copy to repo as `catalog/dump-<buildId>.json`; remove old dump file.
3. `python tools/catalog_diff.py catalog/dump-old.json catalog/dump-new.json`
4. `python tools/analyze_catalog.py catalog/dump-new.json --out catalog/analysis-GAME.md`
5. Fix renamed paths/states in `catalog/sync-catalog.json`.
6. Re-test handshake + one feature per touched catalog section.

---

## Build without guessing

| Goal | Command |
|------|---------|
| Protocol + launcher (no game) | `dotnet build` + `dotnet test` |
| Full plugin (needs game path) | Set `Directory.Build.props.user`, then `dotnet build` |
| Deploy to game | Auto on Core build when `MwcGamePath` is set |

If Core cannot build on the agent machine, **say so** — implement in `WinterMP.Net` with tests instead.
