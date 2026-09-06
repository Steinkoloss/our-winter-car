# Our Winter Car 0.1.33 — tester release

Prepared 6 September 2026 for My Winter Car **v.260516-01**, Steam build
**23268598**. Everyone in a session needs this same package (protocol **120**).
This is a test release: two-player gameplay and long sessions still need validation.

## Changes since 0.1.32

- Shopping bags now have one shared identity and inventory controlled by the host.
  Competing grabs cannot leave both players holding separate copies. Opening a bag
  requests one host action, and repeated requests cannot spill the contents twice.
- Bag contents are captured directly from the game's item factories. Items whose
  shop and display names differ now resolve correctly, and missing copies retry
  creation without waiting for another player to open a bag. Reconnects retain the
  shared bag and remaining items.
- Joining protects the guest's local world save from game save, delete and overwrite
  operations. Restart the game before hosting or saving a singleplayer world afterward.
- Guest replacement parts now keep the guest's original saved parts safely out of
  the shared world and restore them on exit, reducing duplicate parts and occupied
  mount conflicts.
- Guests can operate supported replacement-part bolts with the normal tool controls
  and adjust both alternators by hand. The host checks bolt state, limits and repeated
  requests before applying each change.

## Verification

Release checks passed: **1,067 protocol/policy tests**, **18 launcher tests** and
**11 catalog-tool tests**. Release builds compile against the installed game assemblies.
Both launchers include .NET 8.0.30 and the same verified mod payload; the dependency
audit reports no known vulnerabilities. Development testing also passed **66 isolated
native game checks** covering bag opening, item creation, pickup ownership, guest save
protection, part isolation and alternator actions.

These results do not establish that the packaged release has passed startup,
installation or two-player gameplay testing. Check the kit's `validation.json` for
the release-specific checks. The reported two-player shop sequence still needs to
be repeated with this version.

## Start here

Read **TESTING.md** before the first session. Use a disposable host save or a backed-up
copy. Close the game before installing. Upgrade every peer from 0.1.32 together;
protocol 120 is incompatible with earlier packages. Start with the host picking up
and opening a grocery bag, then reverse the roles, repeat purchases and reconnect.
This prerelease is separate from
stable updates, so the regular public updater may still offer an older stable version.

## Known limits

Opening bags containing native engine parts, including **fan belts and oil filters**,
remains guarded while their part adapters are unfinished. Begin with ordinary groceries.
Full guest engine behavior, other adjustments, non-box part creation and some
world/economy interactions remain incomplete. Supported bolt and alternator controls
still need real two-player acceptance testing.

Ventti's new implementation, including car/house wagers, still needs runtime validation.
See **TESTING.md** for focused checks and `docs/COVERAGE-ROADMAP.md` in the source for
remaining work. The host saves; guests do not save. A successful build or unit test is
not evidence of a completed two-player test.
