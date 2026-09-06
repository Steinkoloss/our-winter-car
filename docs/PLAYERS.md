# Our Winter Car — Player guide

**Our Winter Car** adds co-op multiplayer to **My Winter Car** — a nod to the
game's title. One player hosts with their savefile; friends join through Steam
**Join Game**. Everyone shares the same world, money, and progress while connected.

For **0.1.33 tester packages**, start with [TESTING.md](TESTING.md). This package
targets My Winter Car **v.260516-01**, Steam build **23268598** (protocol **120**).
Use the downloads supplied by your test organizer. The GitHub links below lead to
public releases, which may still list an older version.

## Requirements

- **My Winter Car** on Steam
- Windows 10/11 **or** Linux (with Steam + Proton)
- All players must run the **same mod version**

## First-time setup — one file (Windows **or** Linux)

**OurWinterCar-Installer.com** is a single download that installs the mod on either
OS. Get it from [GitHub Releases](https://github.com/Steinkoloss/our-winter-car/releases).

- **Windows:** double-click it. (If SmartScreen blocks the `.com`, rename it to
  `.exe` and run, or use **OurWinterCar-Setup.exe** below.)
- **Linux:** `sh ./OurWinterCar-Installer.com` (running it directly can be grabbed
  by Wine on some distros; the `sh` form always works — or use the AppImage below).

It finds your Steam/Proton game, installs the mod, and opens the launcher for
host/join + save-backups. The per-OS installers below do exactly the same thing.

## First-time setup — Windows

1. Download **OurWinterCar-Setup.exe** from [GitHub Releases](https://github.com/Steinkoloss/our-winter-car/releases) and run it.
2. If setup could not find the game, open **Settings** and browse to your
   My Winter Car folder. The mod installs automatically once the game is detected.

If the game moved, update the folder path — install/repair runs automatically.

## First-time setup — Linux

My Winter Car runs through Steam Proton on Linux.

1. Download **OurWinterCar-Launcher-linux-x64.AppImage** from [GitHub Releases](https://github.com/Steinkoloss/our-winter-car/releases).
2. Make it executable and launch it:
   ```
   chmod +x OurWinterCar-Launcher-linux-x64.AppImage
   ./OurWinterCar-Launcher-linux-x64.AppImage
   ```
   The launcher detects your Steam/Proton game folder and installs the mod automatically.
   If your system lacks FUSE, run it with `--appimage-extract-and-run`, or use the Linux launcher zip.
3. In Steam → My Winter Car → Properties → Launch Options, use
   `WINEDLLOVERRIDES="winhttp=n,b" %command%` so Proton loads BepInEx.
4. If the game is not found, open **Settings** and browse to the game folder inside your
   Steam library (e.g. `~/.steam/steam/steamapps/common/My Winter Car`).

## Hosting a session

1. Open **Our Winter Car** (Start menu).
2. Click **HOST GAME**.
   - Your save is backed up automatically before launch.
   - The game starts through Steam with a friends-only lobby.
3. The game loads automatically (FastBoot). Friends can join via Steam **Join Game** while you play or from the lobby.

**Important:** Only the host should save the game. Guests must **never** save.

## Joining a friend

1. Install **Our Winter Car** using the instructions for your operating system above.
2. In Steam, right-click your friend who is hosting → **Join Game**.

You can also click **JOIN GAME** in the launcher first (it opens the game without
hosting), then use Steam **Join Game** on your friend's profile.

**Our Winter Car is not for single player.** Launch **My Winter Car** from Steam
directly if you want to play alone.

## Updates

The launcher checks GitHub on startup, then every ~90 seconds, and shows a banner when a
newer release is available.

- **Update mod** — downloads the payload zip and installs into your game.
- **Update launcher** on Windows — downloads and runs the setup exe (launcher closes).
- **Linux launcher** — close the launcher and replace its AppImage or extracted launcher
  folder with the newer download. Mod updates are available inside the launcher.

## Save backups

- **Backup save** — manual backup with the game closed.
- **Restore save…** — pick a previous backup (current save is backed up first).
- **HOST GAME** — automatic backup before each host session.

Backups are stored in `%LOCALAPPDATA%\WinterMP\backups` on Windows, or
`~/.local/share/WinterMP/backups` on Linux (under `$XDG_DATA_HOME` if customized).

## Troubleshooting

| Problem | What to try |
|--------|-------------|
| Game not found | Settings → browse to game folder |
| Friends cannot join | Same mod version; host used **HOST GAME** |
| Version mismatch in-game | Install the same tester package on both PCs; for public releases, use **Update mod** |
| Guests must not save | Host saves only |
| Need help | Open a GitHub issue on the project repo |

In-game: press **TAB** for session status and version info.

## Removing the mod

Settings → **Remove mod from game**. BepInEx stays installed; automatic mod installation
is paused until you choose **Install / Repair**.

For a bug report, use **Info → Export diagnostics** and send the zip with reproduction
steps. **Info → Tester guide** opens the testing checklist.
