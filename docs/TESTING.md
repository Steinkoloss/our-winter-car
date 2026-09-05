# Tester guide — Our Winter Car 0.1.31

Target: My Winter Car **v.260516-01**, Steam build **23268598**, on Windows x64
or Linux x64 through Steam Proton. Use Steam's public branch and the same mod package
on every machine. This build uses protocol **94** and rejects incompatible peers.

## Install and protect your save

1. Close My Winter Car. Keep a copy of your normal save and use a disposable host save.
2. Windows: run **OurWinterCar-Setup.exe**, or extract the Windows launcher zip to a
   writable folder and run **WinterMPLauncher.exe**. Keep its payload and vendor folders.
3. Linux: run the AppImage (`--appimage-extract-and-run` if FUSE is unavailable),
   or extract the Linux launcher archive, make
   **WinterMPLauncher** executable if needed, and run it. Steam and Proton must already
   launch the unmodified game. Set Steam's launch options to
   `WINEDLLOVERRIDES="winhttp=n,b" %command%` so Proton loads BepInEx.
4. The launcher installs the bundled mod. If detection fails, select the folder containing
   **mywintercar.exe** in Settings. Info should show **0.1.31**, protocol **94** and
   game build **23268598**. An older public release in the update status is normal.
5. Use **Backup save** once and confirm the backup exists before your first test.
   The launcher also completes a backup before starting each hosted session.

## First session — stop and report if this fails

1. Host selects **HOST GAME**. Guest uses Steam friends → **Join Game**, or opens the
   game with the launcher's **JOIN GAME** first. Use separate Steam accounts.
2. Confirm both players load into the world. Hold **TAB** for player/session status;
   send a message with **T**. Check that each player sees the other move.
3. Open a door, move/drop an item and compare cash. Drive together, exit, then reconnect
   the guest. Confirm the door, item, car and cash still agree.
4. Host saves and ends the session. Guests leave without saving. Reload the host save
   and check it. Keep the backup until the entire test pass is accepted.

## Focused checks

Record **PASS / FAIL / NOT TESTED**, host/guest platform and steps for each case.

| Area | What to try | Expected result |
|---|---|---|
| Installation | Fresh install, upgrade from 0.1.30, install again | 0.1.31 on both peers; no missing-file errors |
| Connection | Steam invite, late join, guest disconnect/rejoin | Matching world and player list; no duplicate player |
| Shared money | ATM deposit/withdraw on both peers, insufficient funds | Cash and bank agree; each transfer charges once |
| Slots | Guest plays while host is away; hold/spin/cash out, reconnect | Reels and balances agree; no repeated payout |
| VideoPoker | Deal, hold/redraw, collect, high/low double, rejoin mid-hand | Shared hand and banks; no free redraw or duplicate money |
| Debt letter | Both pay together; debt changes while open; Escape during payment | At most one payment; changed quote requires another press |
| Eviction | Open/pay a debt letter after the mailbox relocates | Shared debt/envelope state; payment does not reverse eviction |
| Vehicles | Drive, swap driver, wear/break/repair, late join | Position and condition converge; repaired parts stay repaired |
| Winter/world | Sleep/time, weather, heating, shared purchases/jobs | Host and guest see consistent progression |
| Teardown | Leave and relaunch singleplayer from Steam | Native ATM, debt letter and gambling controls work again |
| Recovery | Restore a backup with the game closed, then host again | Restored world loads; previous save has its own backup |

Ventti's table settlement and car/house wagers remain unfinished. Do not use them for
shared progression in this test release. Broader residuals are listed in the source's
`docs/COVERAGE-ROADMAP.md`. Run the first-session checks before a longer survival session.

## Report a problem

Press **F7** in-game to flush diagnostic information if possible, then use the launcher's
**Info → Export diagnostics** action. Send the resulting zip from **both host and guest**, plus:

- What you did, what you expected and what each player actually saw.
- The approximate time, host/guest names, Windows or Linux/Proton, and other installed mods.
- Whether it repeats after reconnecting or restoring the pre-test backup.
- A screenshot or short clip if the problem is visual.

The zip contains logs, mod configuration and local paths; send it to your test organizer.
It does not include game saves. Keep a failing save separately if the organizer asks for it.

## Roll back

Close the game. Use **Restore save…** in the launcher to choose the pre-test backup;
the current save is backed up first. Settings → **Remove mod from game** removes
Our Winter Car and restores its resolution-dialog change when the game data still matches.
BepInEx remains installed. To return to an older mod, run that release's installer after
removing this one; do not mix old and new DLLs. Linux launcher backups and diagnostics live
under `~/.local/share/WinterMP`; Windows uses `%LOCALAPPDATA%\WinterMP`.
