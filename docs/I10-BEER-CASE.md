# I10 one beer-case count transaction — protocol261 portable slice

Status: PARTIAL. Protected-input provenance remains BLOCKED. The count authority,
codec, replica and production ItemWorldSync message/input/snapshot seams execute
in portable tests. There is NO installed native binding, native input suppression,
contact collector, beer-case view projection or connection bootstrap for this slice.
Do not call a denied/unbound native request a guest-capability pass. Wood-carrier
contents, drinking effects and a generic beer inventory are outside this slice.

## Static audit and native boundaries

The source catalog dump `catalog/dump-23268598.json:8903-9149` records one inactive
root `beercase::Use` (historical FSM netId 1319252914). Its transitions include:

- Wait player / Wait button -> Check drink -> Remove bottle -> Play anim ->
  Check bottles, with STOP branches to State 4.
- Load -> Remove bottles -> Loop / State 3 -> Bottles; Save -> State 6.
- NPC Drink -> Check bottles / State 4; Is garbage also exists.
- Integers DestroyProgress and DestroyedBottles, bool Consumed, strings ID and
  UniqueTagBottles, object references Bottle, Hand and Owner.

That graph has no native action fields, array contents, values, global transitions
or ES2 save-key arguments. Neither native capacity nor the relationship between
DestroyedBottles and the visible bottles is established. In particular, this is
NOT evidence of a Remaining variable, a 24-pack, a saved native ID format, bottle
spawning or unopened/empty save retention. The FSM hash is not the runtime item ID.

Previously `sync-catalog.json.controls` replayed Remove bottle as a generic state.
This is removed, and the same exact object-name/FSM guard in RegisterControl
prevents an older catalog restoring that unsafe route. No local vanilla state is
patched or replayed: native guest use remains unverified and may still act locally.
`ItemWorldSync.Scan.cs`'s same-save path/position ordinal fallback is not used as
proof of a stable saved case identity. Existing generic item motion is unchanged.

## Executed production logic

`BeerCaseAuthority` owns one exact native identity plus host epoch. Full native ID
and a deterministic namespaced case hash are checked together. Native capacity is
an explicit observation; 65535 is only a packet bound. Both host-local and guest
requests enter `TryAccept`, which verifies an authenticated, admitted actor/token,
monotonic sequence, expected revision/count, live availability/count, and fresh
host-only actor/contact observations. No motion owner, held-item, cargo or other
world state is consulted or modified. One compare/extract operation runs on the
host boundary. Its observed remaining count must be exactly one less before an
absolute result is published. Failure is not converted into a relative replay.

Denied authenticated attempts are spent, including busy/reentrant attempts. Native
reads and commits are serialized; a concurrent request with the same base cannot
consume another bottle. Exceptions and incorrect post-counts fault the authority
rather than emitting predicted success. The adapter's false return is an atomic
no-mutation contract; arbitrary external side effects cannot be rolled back by a
portable ledger. A native adapter must establish that contract from actual actions.

`ItemWorldSync.BeerCase.cs` sends guest intents without optimistic native/count
mutation, accepts host-local inputs via the same authority, sends absolute updates,
and invokes only an absolute view callback on the guest. SessionManager maps the
actual peer to actor; SessionMessagePolicy limits direction/authentication/channel.
WorldSyncManager routes full, item-group and targeted snapshots. Read-only capture
observes native changes and strips action correlation; replicas never extract or
grant drinking effects. Clear/release revokes the authority and view. Admission
revokes old connection state; a future binding must issue a new host token, not
reuse a token supplied by the client.

No caller currently invokes the native bind seams in the game. The portable
Core-source-linked suite invokes them with explicit synthetic actor/count/contact
and session doubles. This is a functional production count model, NOT a working
native Remove bottle adapter or evidence that native guests can extract today.

## Verification and retained evidence

Actual assigned RUN: `autonomous/rounds/000043-work` (task t_16dcfb48).
`handoff.md` and `verification.json` there enumerate final commands, exit codes,
source/output hashes, raw logs, assertion counts and cleanup. Receipts are new
exclusive leaves; earlier red/green logs are not overwritten.

Before implementation the valid non-owner extraction packet regression failed
with `ProtocolException: Unknown message id 265` (a behavioral codec failure,
not a compilation failure or simulated native result). The authority and paired
Core bridge tests then exercise the accepted path, exact once-only host decrement,
stable identity and matching guest absolute count. The initial bridge also exposed
snapshot action-correlation leakage; its failing assertion is retained separately.

All fixture identities/counts are synthetic. Native discovery/action, injected-state
fixture, ordinary input, different saves, fresh-player native late join, native
save/reload, Steam/two-PC and four-player soak are NOT_TESTED. Native unopened,
partially used and empty case save semantics remain UNKNOWN, not assumed transient
or persistent. There is no save writer, new field, inventory or spawned-bottle ID.

## Exact remaining native work (after independent gate clearance only)

1. Establish current live case identity/collisions and authoritative capacity;
   extract action fields, bottle-array membership and exact save/load/reset keys
   for unopened, partially used, empty and consumed cases, including NPC Drink.
2. Implement an audited `IBeerCaseHost` and guest absolute view projection. Prove
   an atomic one-bottle count/visual mutation with no host drinking/player effect
   for a guest, rather than calling the full relative Remove bottle state.
3. Replace/intercept input before native mutation for either actor, restore only
   owned hooks on teardown, bootstrap epoch/admission tokens and fresh unobstructed
   contact from the host's actual actor pose. Verify identity across discovery,
   rejoin/different saves and native initialization without inventing save records.
4. Exercise the genuine host/guest paths in the authorized disposable rig after
   the protected-input gate clears, then separately ordinary input, save/reload,
   fresh-player join, real Steam/two-PC and soak. Until then retain I10 Partial.
