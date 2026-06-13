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
2. If setup could not find the game, open **Settings** and browse to your
   My Winter Car folder. The mod installs automatically once the game is detected.

If the game moved, update the folder path — install/repair runs automatically.

## Hosting a session

1. Open **Our Winter Car** (Start menu).
2. Click **HOST GAME**.
   - Your save is backed up automatically before launch.
   - The game starts through Steam with a friends-only lobby.
3. The game loads automatically (FastBoot). Friends can join via Steam **Join Game** while you play or from the lobby.

**Important:** Only the host should save the game. Guests must **never** save.

## Joining a friend

1. Install **Our Winter Car** the same way (run the setup exe).
2. In Steam, right-click your friend who is hosting → **Join Game**.

You can also click **JOIN GAME** in the launcher first (it opens the game without
hosting), then use Steam **Join Game** on your friend's profile.

**Our Winter Car is not for single player.** Launch **My Winter Car** from Steam
directly if you want to play alone.

## Updates

The launcher checks GitHub on startup, then every ~90 seconds, and shows a banner when a
newer release is available.

- **Update mod** — downloads the payload zip and installs into your game.
- **Update launcher** — downloads and runs the setup exe (launcher closes).

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
| Version mismatch in-game | **Update mod** in the launcher banner on both PCs |
| Guests must not save | Host saves only |
| Need help | Open a GitHub issue on the project repo |

In-game: press **TAB** for session status and version info.

## Removing the mod

Settings → **Remove mod from game**. BepInEx stays installed.
