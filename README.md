# WinterMP — Multiplayer for My Winter Car

Full co-op conversion of [My Winter Car](https://store.steampowered.com/app/4164420/My_Winter_Car/):
one player hosts with their savefile, friends join through the **Steam friends
list** — shared money, shared world, everything synced.

> **Status: early scaffold (pre-M1).** Protocol, session layer, tooling and
> launcher skeletons are in place; Steam transport is experimental and the
> world-sync subsystems are not implemented yet. See [PLAN.md](PLAN.md) for the
> full design and roadmap.

## Layout

| Path | What it is |
|---|---|
| `PLAN.md` | Architecture, sync model, milestones, risks — read this first |
| `src/WinterMP.Net` | Engine-independent protocol library (netstandard2.0, unit-tested) |
| `src/WinterMP.Net.Tests` | xUnit tests for the protocol layer |
| `src/WinterMP.Core` | The mod: BepInEx 5 plugin (session, Steam lobby/transport, overlay) |
| `src/WinterMP.Tools` | Dev plugin: F9 dumps the FSM/object catalog for sync curation |
| `src/WinterMP.Launcher` | Windows launcher: install/repair, save backups, host/join UX |
| `catalog/` | Generated per-game-build sync catalogs (FSM descriptors) |
| `protocol/` | Wire protocol specification |
| `docs/BUILDING.md` | Build, deploy and dev-loop instructions |
| `libs/` | Drop-in for `Steamworks.NET.dll` (enables the Steam transport) |

## Quick start (developers)

```powershell
dotnet build          # builds everything; Steam transport only with libs/Steamworks.NET.dll
dotnet test           # protocol unit tests
```

See [docs/BUILDING.md](docs/BUILDING.md) for game deployment and the in-game
dev loop (F8 loopback session, F9 catalog dump).

## How it will work for players

- **Host:** open the launcher → *HOST GAME*. Save is backed up automatically,
  a friends-only Steam lobby opens.
- **Friends:** click **Join Game** on the host in the Steam friends list.
  That's the whole flow — no IPs, no ports, no lobby codes.

## License

TBD before first public release (decision tracked in PLAN.md §6 — MSCMP is
GPLv3 and must not be copied from while this is undecided).
