# Our Winter Car — UNRELEASED LOCAL TEST PACKAGE

Local package: mod **0.1.33**, protocol **265**, channel **test**.

Target: My Winter Car **v.260516-01**, Steam build **23268598**. Everyone in a
session needs the same package hashes. Core, Launcher and installer retain source
version 0.1.33; this does not make the package identical to the older public kit.
Nothing has been published, installed or launched by the package builder.

## Existing implementation context (not fresh package gameplay evidence)

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

Read this attempt's `validation.json`, raw command receipts and `SHA256SUMS.txt`.
They record actual build/test counts, payload hashes and optional artifact blockers;
no historical test totals, runtime versions or vulnerability results are reused.
Core/Net/FastBoot and the catalog are compared byte-for-byte with current Release
outputs/source. The compatibility manifest hashes the four content files; its own
hash is recorded externally to avoid a circular self-hash.

A payload-only kit is not a complete launcher/installer. The full kit requires
`vendor/BepInEx_win_x64_5.4.23.5.zip`; optional Setup/AppImage/universal artifacts
require their respective compilers. Missing dependencies stay BLOCKED/NOT_TESTED.
Native discovery, ordinary input, different saves, fresh-player late join, native
save/reload, Steam/two-PC and four-player soak remain **NOT_TESTED** for this package.
Portable tests and hash verification do not establish installation or gameplay.

## Start here

Read **TESTING.md** before the first session. Use a disposable host save or a backed-up
copy. Install only a complete matching launcher kit, with the game closed.
Protocol265 is incompatible with earlier protocols. Start with the host picking up
and opening a grocery bag, then reverse the roles, repeat purchases and reconnect.
Do not use the public updater to obtain this local build; there is no test-channel
selector. Keep all peers on the exact same local payload.

## Known limits

Unsupported native part products remain guarded. Supported fan-belt/oil-filter
adapters do not establish all product coverage. Protocol261 beer-case extraction is
native-UNBOUND portable authority/replication code, not native bottle extraction.
Vendor coffee remains unbound. See **TESTING.md**, `docs/SYNC-SCOPE-AUDIT.md` and
`docs/COVERAGE-ROADMAP.md` for specific implemented slices and remaining work.
The host saves; guests do not save. No mechanic is promoted by a successful package.
