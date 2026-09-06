# Our Winter Car 0.1.32 — tester release

Prepared 6 September 2026 for My Winter Car **v.260516-01**, Steam build
**23268598**. Everyone in a session needs this same package (protocol **118**).
This is a test release: two-player gameplay and long sessions still need validation.

## Changes since 0.1.31

- Replacement boxes now use acknowledged opening requests and native part factories,
  with stable part identities across fitting, removal and body replacement.
- Guest fitting adapters cover all 30 boxed replacement-part families, including the
  17 piston, main-bearing and rocker slots. The host validates the selected mount,
  distance, ownership and prerequisites before committing a fit.
- Guest removal checks native bolts and blockers. Repeated or changed requests cannot
  silently perform a second operation. Fitted copies follow their host mount and
  reconcile after late join, reconnect and targeted resynchronization.
- Native bolt snapshots reconcile individual turns with their parent tightness totals.
- Vehicle wear/checksum, passenger ownership and session-reset handling have been
  corrected, with acknowledged rally progress and better item retirement/recovery.
- Ventti now has host-controlled hands and settlement, property/table synchronization,
  and shared reactions and sounds. Lottery-ticket and hockey-betting synchronization
  also received updates; these systems still need multiplayer acceptance testing.
- Release validation uses version-specific test reports, records the actual tool-test
  count and no longer carries startup-test results over from an older build.

## Verification

All **1,012 automated tests** pass: 983 protocol/catalog tests, 18 launcher tests and
11 catalog-tool tests. Release builds compile against the installed game's own assemblies.
Both launchers bundle .NET 8.0.30; the dependency audit reports no known vulnerabilities.
The kit verifies that both platforms contain the same mod payload and catalog, checks
archive integrity and includes SHA-256 checksums and matching source.

Game startup, native Windows installation and two-player gameplay have **not** been
rerun for this version. Earlier startup results for 0.1.31 do not validate this build.
The kit's `validation.json` records this release's automated checks and their limits.

## Start here

Read **TESTING.md** before the first session. Use a disposable host save or a backed-up
copy. Close the game before installing. Upgrade every peer from 0.1.31 together;
protocol 118 is incompatible with earlier packages. This prerelease is separate from
stable updates, so the regular public updater may still offer an older stable version.

## Known limits

Guest replacement copies do not yet have operational native bolt or engine behavior;
the host must operate the native bolts during fitting/removal tests. Guest save isolation
and full mounting integration remain unfinished, and a mount already occupied by the
guest's locally loaded part can defer a host replacement copy. Non-box part creation,
full engine behavior and some world/economy interactions remain incomplete.

Ventti's new implementation, including car/house wagers, still needs runtime validation.
See **TESTING.md** for focused checks and `docs/COVERAGE-ROADMAP.md` in the source for
remaining work. The host saves; guests do not save. A successful build or unit test is
not evidence of a completed two-player test.
