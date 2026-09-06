# Tester guide — Our Winter Car 0.1.32

Target: My Winter Car **v.260516-01**, Steam build **23268598**, on Windows x64
or Linux x64 through Steam Proton. Use Steam's public branch and the same mod package
on every machine. This build uses protocol **118** and rejects incompatible peers.

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
   **mywintercar.exe** in Settings. Info should show **0.1.32**, protocol **118** and
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
| Installation | Fresh install, upgrade from 0.1.31, install again | 0.1.32 on both peers; no missing-file errors |
| Connection | Steam invite, late join, guest disconnect/rejoin | Matching world and player list; no duplicate player |
| Replacement boxes | Host and guest open newly purchased boxes; repeat the click and reconnect | One part appears per box; no duplicate item or payment |
| Fixed replacement parts | Guest picks up, carries, releases and fits a supported replacement part | Host accepts the correct mount; both peers see the fitted part |
| Engine slots | Fit pistons, main bearings and rockers to different eligible slots | The selected native slot is used; occupied or blocked slots refuse the fit |
| Removal | Host loosens native bolts, then guest removes the part; also try while tightened | Allowed removal creates one loose part; tightened or blocked parts stay attached |
| Fitted parts and rejoin | Move the car, reconnect the guest, remove/refit a replacement part | The part follows its mount and keeps the same identity without duplicates |
| Shared money | ATM deposit/withdraw on both peers, insufficient funds | Cash and bank agree; each transfer charges once |
| Ventti | On a disposable save, bet/hit/stand/collect while host is away, then reconnect | One shared hand and settlement; table, reactions and balances agree |
| Vehicles | Drive, swap driver/passenger, wear/break/repair, late join | Position, seats and condition converge; repaired parts stay repaired |
| Winter/world | Sleep/time, weather, heating, shared purchases/jobs | Host and guest see consistent progression |
| Teardown | Leave and relaunch singleplayer from Steam | Native controls work again |
| Recovery | Restore a backup with the game closed, then host again | Restored world loads; previous save has its own backup |

Guest replacement copies do not yet have operational native bolt or engine behavior.
Use the host for tightening/loosening during these fitting/removal checks. Guest save
isolation is incomplete: mounts already occupied by the guest's locally loaded parts
may defer the host's fitted copy. Record that case separately from an empty-mount fit.
Non-box part creation and full engine behavior remain unfinished.

Ventti settlement and property/table synchronization now have implementations, but
runtime validation is still pending. Keep car/house wagers confined to disposable saves.
Broader residuals are listed in the source's `docs/COVERAGE-ROADMAP.md`. Run the
first-session checks before a longer survival session. None of these cases is marked
passed merely because the release's automated tests pass.

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
