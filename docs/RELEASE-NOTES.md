# Our Winter Car 0.1.31 — tester release

Prepared 5 September 2026 for My Winter Car **v.260516-01**, Steam build
**23268598**. Everyone in a session needs this same package (protocol **94**).
This is a test release: two-player gameplay and long sessions still need validation.

## Changes since 0.1.30

- Correct shared cash, bank and income balances; acknowledged ATM deposits and withdrawals.
- Host-controlled slot machines and VideoPoker, including holds, payouts and reconnects.
- Shared debt-letter quotes and payments, protected against stale quotes and duplicate charges.
- Updated vehicle wear and concrete damage mappings for the current game.
- Additional rent, welfare, hockey, world-state and session fixes detailed in the coverage roadmap.
- Launcher installs its newer bundled mod, waits for a completed save backup before hosting,
  and stages installs and restores before replacing existing files.
- Mod updates work from a writable cache with the Linux AppImage. Diagnostics include
  installed compatibility information, FastBoot settings and installation/update logs.
- Patched the Linux desktop integration dependency for [GHSA-xrw6-gwf8-vvr9](https://github.com/tmds/Tmds.DBus/security/advisories/GHSA-xrw6-gwf8-vvr9).
- Added visible Install / Repair, tester guide and diagnostic export controls; removing
  the mod now pauses automatic reinstallation.

## Verification

All **358 automated tests** pass: 335 protocol/catalog tests, 18 launcher tests and
5 catalog-tool tests. Release builds compile against the installed game's own assemblies.
Both launchers bundle .NET 8.0.30; the dependency audit reports no known vulnerabilities.
The Linux launcher starts successfully. The Windows launcher and complete installer
successfully install into an isolated game copy under Wine; native Windows remains a
tester check.

The package targets the latest announced game update, [v.260516-01](https://steamcommunity.com/app/4164420/announcements/).
A separate game copy loaded Core 0.1.31 and FastBoot in Unity 5.0/legacy Mono, reached
the main menu and started a local UDP host on protocol 94. No player saves were used.
That offline profile has no Steam client, so native Steam-dependent menu actions report
initialization errors; friends/P2P connections and in-world gameplay remain for testers.
The kit's `validation.json` records the automated tests and the limits of this startup check.

## Start here

Read **TESTING.md** before the first session. Use a disposable host save or a backed-up
copy. Close the game before installing. Everyone must install the same tester package;
the regular public updater may still show 0.1.30 as the latest stable release.

## Known limits

Ventti settlement and car/house wagers are unfinished: leave that table out of shared
progression tests. Some world interactions and vehicle resync coverage remain incomplete;
see `docs/COVERAGE-ROADMAP.md` in the source. The host saves; guests do not save.
A successful build or unit test is not evidence of a completed two-player test.
