# WinterMP — Player guide

WinterMP adds co-op multiplayer to **My Winter Car**. One player hosts with their
savefile; friends join through Steam **Join Game**. Everyone shares the same world,
money, and progress while connected.

## Requirements

- **My Winter Car** on Steam (AppID 4164420)
- Windows 10/11
- **WinterMP-Setup.exe** (recommended) or the portable zip from [GitHub Releases](https://github.com/Steinkoloss/our-winter-car/releases)
- All players must run the **same WinterMP mod version**

## First-time setup

1. Run **WinterMP-Setup.exe** (installs the launcher and mod into your game automatically).
2. If setup could not find the game, open **WinterMP Launcher** → **Settings** and
   browse to your My Winter Car folder, then click **Install / Repair**.

Re-run **Install / Repair** after a WinterMP update or if the game moved.

## Hosting a session

1. Open the launcher.
2. Click **HOST GAME**.
   - Your save is backed up automatically before launch.
   - The game starts through Steam with a friends-only lobby.
3. Wait until you reach the main menu in-game. Friends can join once the lobby is up.

**Important:** Only the host should save the game. Guests must **never** save —
the host owns the savefile.

## Joining a friend

1. Install WinterMP the same way (launcher → Install / Repair).
2. In Steam, right-click your friend who is hosting → **Join Game**.

You do not need to click anything in the launcher to join. Steam overlay join
works from the main menu.

To play solo without multiplayer, use **PLAY SOLO** in the launcher (or start
the game normally from Steam without WinterMP launch args).

## Updates

The launcher checks GitHub on startup and shows a yellow banner when a newer
release is available.

- **Update mod** — downloads `WinterMP-payload.zip` and installs into your game.
- **Update launcher** — downloads and runs `WinterMP-Setup.exe` (launcher closes).
- **Check for updates** — manual check anytime.

Use **Dismiss** to hide the banner for the current release.

## Save backups

- **Backup save** — manual backup anytime.
- **Restore save…** — pick a previous backup (current save is backed up first).
- **HOST GAME** — automatic backup before each host session.

Backups are stored in `%LOCALAPPDATA%\WinterMP\backups`.

## Troubleshooting

| Problem | What to try |
|--------|-------------|
| Game not found | Settings → browse to game folder |
| Friends cannot join | Both players same mod version; host used **HOST GAME**; check Steam friends |
| Version mismatch in-game | Install / Repair on both PCs |
| Shop or sync issues | Host saves only; F10 retry if session shows Failed (in-game) |
| Need help | **Diagnostics zip** → attach to a GitHub issue |

In-game: press **TAB** for a debug panel (session state, protocol version).

## Removing WinterMP

Settings → **Remove WinterMP mod from game**. This removes only the mod folder;
BepInEx stays installed. Delete the launcher separately if you no longer need it.
