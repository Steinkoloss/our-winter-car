# Our Winter Car — Multiplayer for My Winter Car

**[Download the latest release](https://github.com/Steinkoloss/our-winter-car/releases/latest)** —
`OurWinterCar-Installer.com` is one file that installs on **both Windows and Linux**
(Windows: double-click; Linux: `sh ./OurWinterCar-Installer.com`). Windows-only
`OurWinterCar-Setup.exe` and the Linux AppImage are also available.

This thing is completely AI slopped with Fable 5 & Composer.
Probably horrible!


Full co-op conversion of [My Winter Car](https://store.steampowered.com/app/4164420/My_Winter_Car/):
one player hosts with their savefile, friends join through the **Steam friends
list** — shared money, shared world, everything synced.

> **Status: alpha.** Steam lobby host/join works; world sync covers doors, bolts,
> parts, shop, items, vehicles, climate, wallet, and time. See [PLAN.md](PLAN.md)
> for the roadmap. Players: [docs/PLAYERS.md](docs/PLAYERS.md).

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
| `docs/PLAYERS.md` | Install, host, join, backups — player guide |
| `libs/` | Drop-in for `Steamworks.NET.dll` (enables the Steam transport) |

## Quick start (developers)

```powershell
dotnet build          # builds everything; Steam transport only with libs/Steamworks.NET.dll
dotnet test           # protocol unit tests
```

See [docs/BUILDING.md](docs/BUILDING.md) for game deployment and the in-game
dev loop (F8 loopback session, F9 catalog dump).

## Players

**Our Winter Car** is the mod name players see in-game and in the launcher.
(Code and folders still use `WinterMP` internally.)

Build with `.\tools\build-launcher.ps1` or grab a release from GitHub.
Full instructions: [docs/PLAYERS.md](docs/PLAYERS.md).

- **Host:** **Our Winter Car** launcher → **HOST GAME** (save backed up, Steam lobby).
- **Friends:** install the same build, then Steam **Join Game** on the host.

## License

[GNU General Public License v3.0 or later](LICENSE) (GPL-3.0-or-later).

Copyleft: you may use, modify, and redistribute this project; derivative
works must stay under the same license and include corresponding source when
you convey binaries. See [LICENSE](LICENSE) for the full terms.
