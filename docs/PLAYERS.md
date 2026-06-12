# Our Winter Car — Player guide

**Our Winter Car** adds co-op multiplayer to **My Winter Car** — a nod to the
game's title. One player hosts with their savefile; friends join through Steam
**Join Game**. Everyone shares the same world, money, and progress while connected.

## Requirements

- **My Winter Car** on Steam (AppID 4164420)
- Windows 10/11
- **OurWinterCar-Setup.exe** from [GitHub Releases](https://github.com/Steinkoloss/our-winter-car/releases)
- All players must run the **same mod version**

## First-time setup

1. Run **OurWinterCar-Setup.exe** (installs the launcher and mod automatically).
2. If setup could not find the game, open **Our Winter Car** → **Settings** and
   browse to your My Winter Car folder, then click **Install / Repair**.

Re-run **Install / Repair** after an update or if the game moved.

## Hosting a session

1. Open **Our Winter Car** (Start menu).
2. Click **HOST GAME**.
   - Your save is backed up automatically before launch.
   - The game starts through Steam with a friends-only lobby.
3. Wait until you reach the main menu in-game. Friends can join once the lobby is up.

**Important:** Only the host should save the game. Guests must **never** save.

## Joining a friend

1. Install **Our Winter Car** the same way (run the setup exe).
2. In Steam, right-click your friend who is hosting → **Join Game**.

You do not need to click anything in the launcher to join. Steam overlay join
works from the main menu.

To play without hosting a session, use **LAUNCH GAME** in the launcher (or start
the game normally from Steam). Friends who are joining a host use Steam
**Join Game** — they do not need to host.

## Updates

The launcher checks GitHub on startup and shows a banner when a newer release
is available.

- **Update mod** — downloads the payload zip and installs into your game.
- **Update launcher** — downloads and runs the setup exe (launcher closes).
- **Check for updates** — manual check anytime.

## Save backups

- **Backup save** — manual backup anytime.
- **Restore save…** — pick a previous backup (current save is backed up first).
- **HOST GAME** — automatic backup before each host session.

Backups are stored in `%LOCALAPPDATA%\WinterMP\backups`.

## Troubleshooting

| Problem | What to try |
|--------|-------------|
| Game not found | Settings → browse to game folder |
| Friends cannot join | Same mod version; host used **HOST GAME** |
| Version mismatch in-game | **Install / Repair** on both PCs |
| Guests must not save | Host saves only |
| Need help | **Diagnostics zip** → GitHub issue |

In-game: press **TAB** for session status and version info.

## Removing the mod

Settings → **Remove mod from game**. BepInEx stays installed.
