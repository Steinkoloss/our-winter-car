# WinterMP wire protocol

Protocol version: **265** (`ProtocolInfo.Version` in `src/WinterMP.Net/Protocol.cs`).

### v265 — bounded electric sauna timer

270 SaunaTimerIntent, channel 0, authenticated guest -> host: SourceId:u32,
Epoch:u32, Actor:u8, Sequence:u32, ExpectedRevision:u32, Timer:f32,
Eye:vec3, Direction:vec3. Exact home sauna source hash; no owner lease. Host
checks fresh living actor, pose-bounded eye and first-hit knob contact within
the native one-metre ray, current revision and one +/-10 knob step clamped
1..120. Host-local scroll enters this same authority before native mutation.

271 SaunaTimerState, channel 0, selected host -> guests: SourceId:u32, Epoch:u32,
Revision:u32, Actor:u8, Sequence:u32, HighWater:u32, Status:u8 (0 observation,
1 accepted, 2 rejected), Timer:f32, Time:f32, KnobAngle:f32. Absolute native knob,
simulation timer and local mesh Y angle; not temperature/power/water/fuel state.
Timer observations allow vanilla cold zero; requests do not. Time is 0..720.
Epoch/revision are nonzero. Per-actor nonwrapping sequence high-water survives
rejoin and is re-advertised; revision changes on observed timer-state changes.
Reserve before all callbacks; stale, duplicate and reentrant requests never
execute again. Partial native failures fault the seam without retry or success.
Guests retain early state until binding, suppress timer writers and reconcile
absolutely, never replaying a delta. Native SaunaTimeKnob save/load stays intact;
no new save fields or Steam/native acceptance claim. Next free message ID: 272.
See `docs/H08-SAUNA-SEAM.md` for static native bindings and portable limits.

### v264 — bounded portable guest clothing persistence

No new message IDs (next free remains 270). All additions are append-only and
use reliable ordered channel 0. Existing owner-authoritative dressing reports
and host relay/join snapshots remain; there is no native clothing command.

* HandshakeResponse (2) appends `clothingAdmission:u64`. The host generates a
  nonzero token for each guest admission, including reconnects using the same
  player slot. Duplicate handshakes re-acknowledge the existing token.
* PlayerSpawn (20) appends the same owner's `clothingAdmission:u64`. The host
  owner uses token zero. Duplicate introductions do not reset clothing history.
* GuestSpawn (24) retains its 95-byte prefix including ID and appends
  `clothingPlayerId:u8, clothingAdmission:u64, hasSavedClothing:u8 (0/1),
  clothingStage:u8, clothingType:u8, winterGarment:u8`: **108 bytes including ID**.
  The triple must be zero if absent; a present winter garment is 0..2. The
  receiving GAME-scene guest must be connected and match both assigned player
  ID and current admission before accepting its one spawn offer. Other players'
  outfits and old-admission offers cannot restore local state.
* PlayerClothingState (32) appends `admission:u64, sequence:u32` after the existing
  four bytes: **18 bytes including ID**. Stage/type retain the existing byte
  domain 0..255; winter is 0 none, 1 jacket, 2 coverall. Sequences are positive,
  nonwrapping and strictly increasing per admitted owner. Invalid, duplicate,
  old-sequence, unknown-player and old-admission packets do not update accepted
  state, persist, or relay. The host verifies sender/player identity; guests
  accept only their selected admitted host. Snapshots carry the accepted owner's
  sequence/token, not a newly generated guest report. The host's own snapshot
  takes a new sequence from its local stream. Admission tokens are identity
  freshness markers, not secrets or a substitute for authenticated transport.

The historical CSV sidecar appends optional zero-based columns 18..20:
`clothingStage,clothingType,winterGarment`. Only a complete range-valid integer
triple asserts clothing (explicit 0,0,0 is known). Missing, partial, malformed,
nonfinite or out-of-range triples are ignored; earlier pose/needs data survive.
Legacy profiles remain readable. Clothing without needs pads columns 8..17 with
empty cells; the current parser does not treat that padding as zero needs.
The host saves only authenticated accepted guest reports under that guest's
SteamID, guarded by host role and the existing guest-save guard.

Restore updates a mod-owned local snapshot, NOT any native FsmInt, garment FSM,
ES2 tag or personal save. After local spawn readiness and both native clothing
bindings, the saved snapshot is reported through the normal owner path. The
loaded native tuple is only a baseline: unchanged personal-save clothing cannot
immediately erase the restored snapshot. A later owner-native tuple change
supersedes it. This portable bridge does NOT claim restored native insulation,
garment visuals, ordinary input, native persistence, live different-save join,
Steam/two-PC or soak acceptance. See `docs/CLOTHING-PERSISTENCE.md`.

### v263 — authenticated native yard-stain contributions (H15 WIP)

New ID 269 `PissAreaIntent`; next sequential unused ID 270. Reliable ordered (0)
only. An authenticated guest sends to the host; the host rejects state 109 from
guests. Guests accept state 109 only from their selected, handshaken host.

Payload order (little endian; excluding u16 ID):

* 269: u32 world epoch, u32 observed host revision, u32 request sequence,
  u64 admission token, u8 actor, u8 area (1..5), u8 native action
  (1 Full power, 2 pumping/State 4), f32 scale contribution. Packet: 29 bytes.
* 109 retains its original u16 sequence and five u8 scales, then appends
  u32 world epoch, u32 revision, u8 admission count (0..254), followed by
  count entries of u8 actor, u64 token, u32 accepted request high-water.
  A one-admission packet is 31 bytes. Scale quantization remains truncation
  of native localScale.x *20. The old u16 sequence is retained on the wire;
  nonwrapping u32 revision now determines absolute-state ordering.

Epochs are nonzero and change at world/session reset. Admissions name the exact
connected RemotePlayer instance; reconnecting a reused player slot gets a new
token, not its previous admission. Snapshots carry absolute presentation and
admissions, never a contribution to replay. Guest epoch changes require reset;
duplicate/old revisions cannot undo newer state. Sequence zero/wrap and
duplicate/out-of-order requests are rejected without consuming high-water.

The host verifies actor equality with transport identity, admission/epoch, a
host-issued revision no older than one second, fresh live actor pose (0..0.6 s),
no driving/passenger state, and native closest-area selection within 20 metres,
including the four NOPISS exclusion points, plus the native world-up 100m roof
ray on layer 27 at the guest's position (not the host's local RoofCheck flag).
The action kind comes from the
audited guest native callback; it is an input report, not host observation of a
remote FSM. Contributions must be finite in (0,0.25], separated by at least
0.1 seconds per actor. All five host scales must be finite/in-native-range and
the selected scale must increase; initialization and native SAVEGAME are not
interrupted. Reentry and native write failures do not consume accepted sequence.
A result-send failure keeps the committed mutation/sequence and relies on the
periodic absolute snapshot, never a second application of the intent.

Static native evidence corrects an older description: these are the five indoor
YARD room-stain meshes, not a player's personal Urine need or five snow colliders.
Vanilla selects the closest object after its indoor PISS entry and grows native
ChangeScale by Addition*deltaTime, where Addition=PissRate/3500; limits are
3/5/7/4/4. Guest SetScale is intercepted before mutation; the native producer and
personal need effects remain local. Host contributions add to the selected native
transform; a pending host-local ChangeScale is advanced too, preventing late native
SetScale from overwriting a guest result. Host-local native actions remain enabled.
Scale1..5 are only LoadFloat/SaveFloat caches. Snapshots therefore read/apply
transforms and leave the native load/save chain and HomePissStain1..5 tags alone.

Evidence is production-linked portable doubles plus static assets and net35 build,
not live native scheduling/contact, ordinary input, native save/reload, physical,
Steam/two-PC or four-player acceptance. Current package metadata is still v262
outside this implementation card's allowed paths: v263 is explicit WIP, not a
package ready for deployment. See docs/H15-YARD-STAINS.md and the worker receipt.

### v262 — finite gasoline-container to parked Sorbet tank transfer

IDs 267 `ContainerFuelIntent`, 268 `ContainerFuelResult`; ID 269 was subsequently allocated by v263.
Both use reliable ordered (0). Only authenticated peers may send intents to the
host; only the selected host after handshake may send results to a guest.
Payload order, little endian, excluding the u16 message ID:

* 267: u32 source container ID, u32 destination vehicle ID, u8 player,
  u32 sequence, f32 requested liters.
* 268: u32 source container ID, u32 destination vehicle ID, u8 player,
  u32 sequence, u32 host transfer revision, f32 accepted liters,
  f32 absolute source liters, f32 absolute destination liters.

The actor must match session transport authentication (host-local actor 0 uses the
same gate). Nonzero sequences strictly increase per actor for the session; zero,
duplicates, old sequences and wrap are refused. Rejections return no result and do
not consume a sequence. Authority history clears at session/scene teardown, not on
an ownership handover. Transfer revisions are host-global, nonzero, never wrap.
Amounts must be finite and positive, fit entirely in both native capacities and
be representable as an exactly conserved single-precision debit/credit; otherwise
neither level changes. No partial over-capacity fill or invented fuel is allowed.

Core binds only the tracked `EQUIPMENTS/gasoline(itemx)` source and
`SORBET(190-200psi)` tank. Current source ownership, fresh accepted guest item pose
and player pose (age 0..2 s), source/player <=3 m and tank/player/source <=8 m are
required. The target must have no local/remote driver or simulator lease, no active
ignition and speed <=0.5 m/s. Source uses the native saved `FluidTrigger::Data.Fluid`,
not cap-trigger `FuelLevel` scratch; destination uses `FuelTankSorbett::Data.FuelLevel`.
Local Alt+R while holding that tracked can requests 0.25 L into that specific tank.
This is explicit mod input, not native post-pour delta inference; input and native
binding remain NOT_TESTED outside portable doubles.

State 54 appends u32 `FuelRevision` after Capacity (packet length 22 bytes).
State 60 appends u32 `FuelRevision`, f32 `FuelLiters` after HandoffTemperature
(packet length 43 bytes). FuelLiters must be finite/nonnegative, zero if revision
is zero. Revision zero retains legacy normalized-byte fuel semantics. Revisioned
tanks apply exact liters, never the lossy gauge byte. Driver reports must match
the host tank revision; delayed lower-revision states cannot overwrite an accepted
transfer. Copies, live publications and snapshots carry the revision and exact
liters. Native future consumption by the current driver still uses that revision.
Known gasoline cans publish native host levels only; guest scalar reports cannot
mint fuel or undo the finite debit even after acknowledging its revision. Other
fluid-container identities retain their previous scalar path. Accepted results
apply absolute levels, including on the requesting guest, without replaying native
pouring. Missing bindings defer the result; periodic snapshots remain in use.

No new save keys, save simulation, mixtures, fire, detached engines, coolant or
brake-fluid behavior. Native discovery/input, Steam/two-PC, different saves, late
join/rejoin, native save/reload and four-player soak are NOT_TESTED. This is bounded
I11/V10 portable implementation evidence, not full vehicle-operation acceptance.

### v261 — one beer-case absolute-count transaction (portable; native binding unresolved)

IDs 265 `BeerCaseExtractIntent` and 266 `BeerCaseUpdate`; next unused ID 267.
Both use reliable ordered (0), including snapshots. Intents require an authenticated
peer at the host. Updates require the selected host and completed guest handshake.
The beercase::Use `Remove bottle` generic control is removed/quarantined; it must
never be used as a relative result or fallback for this transaction.

Payload order (little-endian, excluding the u16 message ID):

* 265: u32 case ID; string exact native ID; u32 epoch, connection token, sequence,
  expected revision; i32 expected remaining count; u8 player.
* 266: u32 case ID; string exact native ID; u32 epoch, revision; i32 capacity,
  absolute remaining count; u8 available (strict 0/1), player; u32 connection,
  sequence. Snapshot correlation is player=255, connection=sequence=0. An accepted
  extraction echoes the actor/connection/sequence; it is not a drink-effect grant.

The native ID is an opaque, nonempty 1..128 printable non-space ASCII string, not
a scene-path ordinal. Case ID is FNV-1a32(`beercase:` + native ID). Both must match
the host binding exactly (hash equality alone is insufficient). A collision must
fail discovery. Epoch/revision/connection/sequence are nonzero and never wrap;
actors are 1..254. Host admission supplies monotonically increasing connection
tokens per actor, retaining token high-water on disconnect. The request's token
never authenticates its own sender. Capacity must be supplied by audited native
data, in 1..65535 (a wire bound, NOT a claimed native pack size). Remaining is
0..capacity; extraction expects a positive count and exact current revision/count.

Both local-host and guest input enter the same serialized authority. Fresh living
host-observed actor/contact (ages 0..0.6 seconds, unobstructed distance 0..1.5 m),
exact case identity, availability, current count and connection are required;
rigidbody/cargo ownership is deliberately irrelevant. Authenticated attempts spend
their sequence before world checks, including busy/reentrant denials. Competing
requests with the same base revision/count cannot both consume. Invalid requests
produce no update and no canonical/native mutation. Host compare/extract must be
atomic; false means no native mutation. The authority observes exactly one removed
bottle before publishing the absolute result; uncertain/partial execution faults
the binding without retry or predicted success. Read-only capture observes vanilla
changes and clears action correlation for full/item-group/targeted recovery.

Replicas pin identity/epoch/capacity and reject stale/duplicate revisions. Applying
an absolute update never extracts, spawns, changes ownership or repeats drinking.
No beer inventory, save key, bottle item manifest or persistence sidecar is added.
Native installation, contact/input interception and view projection remain UNBOUND:
the graph dump lacks capacity, bottle-array/action fields and save-key semantics.
Portable source-linked ItemWorldSync tests are not native guest capability evidence.
See `docs/I10-BEER-CASE.md`; I10 remains partial, including wood-carrier contents.

### v260 — vendor coffee portable foundation; native adapter DISABLED

New registry IDs 262 `VendorCoffeeIntent`, 263 `VendorCoffeeState`, 264
`VendorCoffeeResult`. IDs 265–266 were subsequently allocated by v261. Household coffee 245–247 is unchanged.
All three use reliable ordered (0), including full/item-group/targeted snapshots;
bulk and unreliable channels are rejected. Intent: authenticated peer -> host.
State/result: selected host -> handshaken guest. This is protocol/policy evidence,
NOT a working native CoffeeAutomatic adapter or a native persistence claim.

Payload order (little-endian, excluding the u16 message ID):

* 262: u32 machine, cup, session epoch, serving generation, expected revision,
  connection token, sequence; u8 player, action.
* 263: u32 machine, cup, session epoch, serving generation, revision,
  holder connection; u8 holder, completed-action mask, lifecycle; f32 contents;
  Vector3 position (3 f32); Quaternion rotation (4 f32).
* 264: u32 machine, cup, session epoch, serving generation, accepted revision,
  connection token, sequence; u8 player, action; f32 consumed contents.

Actions: Acquire=1, Purchase=2, Fill=3, Drink=4. Lifecycle: Available=1, Active=2,
Retired=3 refers to a **network serving**, not destruction/persistence of the native
cup. Completed-action bits 0..3 record acceptance once per serving. Contents are
finite nonnegative native units, not household capacity/caffeine constants.
Result consumed is positive only for Drink; other receipts carry zero. No personal
effect event, factory name, wallet delta, native save key or guessed price is sent.

The future audited adapter must supply collision-checked machine/cup identities,
non-reused serving generations, a nonzero host-session epoch, and monotonically
increasing per-player connection tokens assigned on authenticated admission.
Zero identity/revision/sequence/token, actors outside 1..254, invalid enums/masks,
non-finite contents, invalid pose, and nonempty/nonzero-holder inactive cups are
refused. The host compares the request token to its own connection record, NOT to
the token claimed on the wire. Sequence and revision counters never wrap; exhausted
counters fail closed until a new authoritative session, not silent reuse.

Both actors enter `VendorCoffeeAuthority.TryAccept`. Read-only preparation plus
an atomic host commit is serialized for one machine/cup, including reentrant
callbacks. Authentication, fresh living pose, range, exact identity/generation,
expected revision, holder connection and completed-action mask are checked before
preparation. Invalid, duplicate, stale, conflicting and no-effect plans produce no
state/result or native commit. Valid envelopes spend their sequence even if denied
later. A successful commit advances absolute state once and then emits a correlated
receipt. Uncertain/throwing execution disables further acceptance; no retry fallback.

Replicas pin their host epoch and cup identity, retain revision/generation/action
high-water marks and retirement, and reject resurrected older servings. Snapshots
never replay acquire/pay/drink. State must arrive before its result on channel 0.
Only a matching live pending request in the drinker's connection can consume a
Drink receipt once; expiry, disconnect or fresh rejoin removes pending effects.
Rejoin reconstructs absolute state, not money operations or personal effects.

`vendorCoffee` catalog schema 1 permits exactly the discovered INSPECTION machine
and requires eight explicitly unresolved native fields. It has no enable switch:
non-null invented fields, missing keys or drift disable that section, not household
coffee. Core routes the dedicated messages and all snapshot seams through an inert
`VendorCoffeeRuntime`. The exact INSPECTION button is quarantined from generic Buy
registration even if its section is absent/malformed; other shop rules are untouched.
No native FSM input interception, cup binding/materialization, actor geometry,
connection bootstrap, player effect or save writer is installed. Vanilla local
actions are NOT claimed suppressed or multiplayer-safe. See
`docs/H10-VENDOR-COFFEE-FOUNDATION.md` for the missing-field and extraction contract.

### v259 — fixed cabin firewood authority (portable/build candidate)

New IDs 260 `WoodstoveFeedIntent` and 261 `WoodstoveFuelUpdate`, reliable ordered
(0). IDs 262–264 were subsequently allocated by v260. Only authenticated guest -> host intents and the
selected, handshaken host -> guest updates are admitted. Actor in an intent is
compared with the peer-authenticated player ID. Local host contact is queued and
uses the same `WoodstoveFuelAuthority.Decide` entry, not a native bypass.

Wire payload order (little-endian; bool is one byte):

* 260: u32 source, u32 epoch, u8 actor, u32 sequence, u32 resource.
* 261: u32 epoch, u8 actor (255 untargeted), u32 answered sequence (0 admission),
  u32 actor high-water, bool isDecision, u8 `WoodstoveFeedStatus`, bool hasSnapshot;
  if hasSnapshot: u32 source, u32 revision, i32 absolute fuel, f32 observed heat,
  bool native Hiillos/embers proxy, u16 retirement count, that many u32 retired IDs;
  then u32 resource (0 no descriptor), u8 shape (1 native log root half,
  2 native log child half), Vector3 position (3 f32), Quaternion rotation (4 f32).
  The retirement array is sorted, unique, nonzero and bounded to 256 entries.

Status values in order 0..14: Accepted, InvalidActor, StaleEpoch,
ReplayedSequence, WrongSource, InvalidResource, ResourceConsumed,
ActorUnavailable, OutOfRange, InvalidEquipment, InvalidContact,
SourceUnavailable, NativeFailure, Busy, Pending.

Only `CABIN/Cabin/woodstove/Fireplace` is bound. The host assigns nonzero random
epoch and piece IDs, collision-checked against the item table. A piece descriptor
is linked to an actual native firewood Collider by mesh/collider shape locally;
neither a clone path nor a Unity instance ID is an identity on the wire. Live
pieces use the existing authenticated ItemTransform route under this new ID.
Generic ItemDespawn and legacy cabin HeatSourceIntent.FeedWood cannot spend it.
Host scans split, unparented PART pieces within 20m of the stove, at most 256
identities in one world. Broader logging/creation interactions remain out of scope.

Host observations require alive/fresh actor pose (2s), distance <=3m, real native
trigger contact, matching available resource and exclusive motion/release access,
null native parent, exact PART/firewood(Clone), and WoodTrigger.Woods in 0..3.
There is no axe/host-camera/host-hand prerequisite for an authorized guest feed.
The adapter invokes the audited Destroy firewood state once with Collider assigned,
checks WoodTrigger.Woods increased by one (never SetFire's read cache), reserves the
piece, and waits for Unity destruction on a later frame. Pending/partial failure
carries no success snapshot. Timeout (2s) or inconsistent state faults the authority
until world reset; no possibly partial mutation is retried. Native depletion during
the wait is observed in the final absolute snapshot, not overwritten with old fuel.

Admission repeats the retained epoch, per-actor u32 attempt high-water and all
consumed-resource tombstones, plus live piece descriptors. Invalid authenticated
attempts retain sequence high-water; rejoin does not clear this ledger. Guests
allocate above admission, never predict fuel/destruction, and accept revisioned
absolute snapshots monotonically without losing matching acknowledgments when a
new snapshot overtakes a result. Tombstones cannot disappear or materialize again.
Implementation correction (round 000085, no wire/layout change): Busy attempts
also retain authenticated current-epoch high-water. A duplicate Busy/replay denial
does not cancel a guest attempt already confirmed Pending; the original final
Accepted/NativeFailure still resolves it. Core holds a reentrancy guard through
decision/admission publication so callbacks cannot feed another piece or interleave
a nested outcome; rejected callback attempts remain in the same sequence ledger.
See `docs/H07-FUEL-SEAM.md` for portable evidence and the still-open native gates.
Legacy cabin HeatSourceState and generic SetFire state relays are excluded; sauna,
other fireplaces and grills keep their previous paths. Only guest cabin feed and
fuel-depletion writers are suppressed, with loaded-log visuals applied absolutely.

There is no custom cold-save fuel/log persistence: native initialization and
NoRainCabin temperature save behavior are untouched. Runtime destruction timing,
two-peer replica materialization, ordinary input, native save/reload, Steam/two-PC
and soak remain NOT_TESTED under the inherited native-launch prohibition. Portable
adapter doubles and static signature checks are not native gameplay evidence.

### v258 — shared scraper lease and one parked Corris windshield

New IDs 258 `ScraperAction` and 259 `PaneScrapeUpdate`, reliable ordered (0).
At v258 the next unused ID was 260. Only authenticated guest -> host actions and selected,
handshaken host -> guest updates are admitted. Host local actions use the same
authority. No vehicle-ownership grant or VehicleClimate intent is involved.

Payload order (all integers little-endian, bool = one byte):

* 258: u32 epoch, u8 actor, u32 sequence, u32 vehicleId, u8 pane,
  u32 toolId, u8 operation, Vector3 eye, Vector3 unit direction (each 3 f32).
  Operation: 0 pickup, 1 equip, 2 off, 3 drop, 4 keepalive, 5 stroke.
  Pane 1 means only catalog CORRIS/BODY/Windshield/collider::Scrape.
  No guest cutoff/result or equipped-truth field exists.
* 259: u32 epoch, u32 vehicleId, u32 revision, f32 absolute cutoff,
  u32 toolId, u8 holder (255 none), bool equipped, u8 actor (255 untargeted),
  u32 answered sequence (0 snapshot), u32 actor high-water, bool isDecision,
  u8 status (PaneScrapeStatus: Accepted=0 through NativeFailure=10).
  Only an accepted isDecision stroke grants actor-local sound/body heat once.
  Equipment acknowledgments and periodic current-state snapshots grant no effects.

The host establishes an exclusive pickup lease using the discovered shared item
and native first-hit pickup distance 1m, then accepts equip/off/drop transitions
only for that holder. Keepalive cannot create or equip a lease. Host-observed
alive actor pose must be at most .6s old; lease expires at .6s and is revoked on
death/disconnect/drop. Off revokes equipped permission while retaining held pickup.
Input eye is bounded to feet (height .2..2.1m, horizontal .6m, yaw deviation <=45°),
and finite unit direction is raycast in host physics. Stroke requires first hit
on exactly the .8m pane, outside actor and linear/angular parked speed <=.1.
All authenticated valid-epoch attempts consume monotonically increasing u32
sequence, including denials; no wrap. Host high-water survives peer rejoin and
is sent in targeted snapshots to resynchronize restarted clients. A new world
or host session uses a new nonzero epoch; no saved cutoff replay is introduced.

Only the audited native glass FloatAdd/material actions execute on the host.
The guest native additive SendEventByName is replaced before mutation. Accepted
results carry the exact float, superseding climate byte writes for this pane;
other panes/interior frost remain on the existing climate route. Vanilla roof
startup, FREEZE and cold-load initialization remain native and are observed as
new absolute revisions, never restored from custom persistence.

Evidence limit: connected source/portable tests/net35 build are distinct from
rendered native equipment/contact acceptance. See docs/V11-PANE-AUDIT.md.

### v257 — R20 battery boxes and persistent loose cells

The existing PackageState, PackageOpenRequest/Receipt and SupplyItemState (227)
now also cover the four-cell R20 box. No message layouts or IDs change. Both
peers require v257 because the host-created contents and retirement semantics
are now shared. Next free message ID remains **258**; mod stays 0.1.33 unreleased.

Package identity uses factory `Spawner/CreateItems::R20BatteryBox` and native
`r20batterybox0N`; each output uses `Spawner/CreateItems::R20Battery` and
`r20battery0N`. The host observes one exact native output and one quantity
decrement for each accepted opening. Existing actor/range/ownership checks,
reservation, revisions and receipt replay apply. The fourth opening empties the
box. Existing item ownership/transform streams carry loose cells; host snapshots
and rejoin preserve their identities. Retired outputs cannot return from replay.

Loose R20 cells have no native charge scalar. They save their transform and
Consumed flag; native consumed disposal deletes the item's save tag. This differs
from fuses, whose separate Destroy flag deletes the save while fitted Consumed
fuses remain saved. Catalog `supplyContents.retirementVariable` selects the audited
Consumed or Destroy layout (default Destroy). Guest originals are hidden and
restored on teardown; replicas never write the guest's item save. Battery fitting
and appliance charge/use remain separate gameplay work.

### v256 — advert-job telephone enrolment

**256 AdvertPhoneIntent**, authenticated guest → host, channel 0: Call:u32,
PlayerId:u8, Phone:u8, Action:u8. **257 AdvertPhoneResult**, host → guests,
channel 0: Call:u32, PlayerId:u8, Phone:u8, Status:u8. Both packets are **9 bytes
including the message ID**. Call must be nonzero; phones 0/1/2 are the apartment,
old house and taxi carphone. Intent actors are 1–254 and must match the sending
peer; result actors may also be 0 for the host. Actions are Begin=0, KeepAlive=1,
Complete=2, Cancel=3; statuses are Accepted=0, Completed=1, Rejected=2. Unknown values,
truncation and trailing bytes are rejected. Neither message carries arbitrary
numbers, FSM events, fees, durations or job progress.

The only supported number is 08231206. Native digit entry and ringing precede
host approval. The host reserves one enrolment across all three phones, requires
the original available listing, job stage 0/Wait call, a nearby player (4m), an
idle host line and paid/plugged-in household service. The carphone has no native
household bill. Approved callers hear the native 72-second speech/subtitle;
household connections and elapsed scaled-time usage (.2 units/second) are charged
once on the host. Guest Call billing and CALLED side effects are bypassed for
this number. The guest taxi keypad remains local while the shared taxi service
is idle and unowned; an incoming taxi call or unavailable service closes it.
Incoming taxi answer/hangup retains its existing intent path. Other numbers use
their existing per-number handlers; no additional results for them are defined.

Begin IDs use per-actor serial ordering; denied IDs cannot be replayed when
conditions improve. Matching duplicate Begin packets are ignored. KeepAlive
does not start or revive a call. The lease expires after 8 seconds without a
heartbeat or after 80 total seconds. A completion within the final second may
wait for the host's full 72-second deadline; earlier completion, leaving range, service
loss, native host line activity, hangup and disconnect cancel enrolment. Charges
already incurred remain. Completion requires 72 host-measured real-time seconds
and a live lease, then dispatches the validated native listing CALLED → JOB once.
The native listing Stage 3/numberdisabled and job Stage 1 persist through existing
host SAVEGAME actions. Job progress uses existing AdvertJobState (250); vanilla's
initial 600-second wait and delivery schedule remain native. Calls are session-only
and are cancelled on departure/teardown; reconnect reads shared job progress and
must start any unfinished call again. Both peers require v256. Next free ID 258;
mod remains 0.1.33 unreleased.

### v255 — engine-oil cap and conserved refill

**254 MotorOilFillerState**, host → guests, channel0: Revision:u32, Epoch:u32,
HeadId:u32, PanId:u32, Rotation:f32, Oil:f32, Contamination:f32, Viscosity:f32,
CapPosition:vec3, CapRotation:quat. **62 bytes including ID**. Revision/Epoch
nonzero; both part IDs nonzero when available, both zero when unavailable.
HeadId/PanId hash `oil-head:`/`oil-pan:` plus the actual native saved part ID.
Cap rotation 1–359; Oil -1–3.7; Contamination -1–100; Viscosity 0–100, all finite.
Cap world pose uses the same finite position and quaternion validation as bottle
states. Equal revisions must agree on contents but may refresh a moving cap pose.
A change in attached head/pan or availability advances Epoch and clears pour leases.

**255 MotorOilRefillIntent**, authenticated guest → host, channel0: Epoch:u32,
BottleId:u32, Sequence:u16, PlayerId:u8, Action:u8. **14 bytes including ID**.
Epoch nonzero; actor1–254 must match sender. Action0 stops,1 pours,2 unscrews,3
screws. BottleId is nonzero for0/1 and zero for2/3. Per-actor serial sequences
consume rejected attempts too; duplicate, old and half-range sequences cannot
refresh a lease. Requests must match the current fitted-parts epoch. Pour leases
last at most .6 seconds and require current bottle ownership, accepted owner pose,
a living nearby actor, an open cap and overlapping native pour geometry.

The host alone updates the four-litre source and 3.7-litre pan. Transfer is capped
by source, free capacity, .1 litres/second and .25 seconds per frame. Dirt reduction
(3.2/second, minimum .01) and viscosity movement (.06/second towards the bottle)
use the time represented by the actual transferred amount. Mounted and saved pan
scalars and bottle root/source scalars are updated together before native empty
processing. Host bottle saving reads a live source or retains the root remainder
when native Empty destroyed the trigger; repeated cold saves must not recover the
prefab's four litres. Native startup also gates the child writer/event until the
loaded root quantity and grade have reached the source. Native save tags remain unchanged. No per-packet claimed
amount or duration is accepted. Native cap
rotation has no save tags; amounts/grades/viscosity follow native part/item saves.
The protocol also enables guest bottle pour geometry; v254 peers must not join.
Next free message ID at v255: **256**. Mod remains 0.1.33 unreleased.

### v254 — saved motor-oil containers

**253 MotorOilBottleState**, host → guests on reliable-ordered channel 0:
ItemId:u32, Revision:u32, NativeId:string, Fluid:f32, Viscosity:f32,
Grade:u8, Empty:bool, Position:vec3, Rotation:quat. **50 + UTF-8 NativeId
bytes including message ID**. Next free message ID at v254: **254**. Both peers need
v254; the mod remains 0.1.33 unreleased.

NativeId is `motormoil1` plus a canonical positive Int32 counter. ItemId
uses `FactoryItemIdentity` with factory hash of
`Spawner/CreateItemsSeparate/MotorOil::MotorOil` and must match NativeId.
Revision is nonzero; Fluid is finite 0–4 litres; Viscosity finite 0–10;
Grade is the native Type 0–2; Empty is strictly 0/1 and requires Fluid <0.1.
Position components are finite within ±100000, quaternion squared norm 0.9–1.1.
Serial ordering rejects old and half-range revisions; equal revisions may
refresh a creation pose but must agree on all bottle contents and identity.
Existing bodies use ordinary item motion and exclusive pickup ownership.
Terminal ItemDespawn prevents queued or replayed states resurrecting bottles.

Full, item-group and targeted snapshots supply the creation descriptor. Guest
native bottles/factory pause and restore on disconnect; replicas skip native
save/load and reconstruct grade materials from the game's native table.
The host's native quantities remain authoritative. Guest engine refill intents
are a pending dependency; replica pour colliders remain disabled until then.

### v253 — shared advert delivery and native mailbox accounting

All three messages use reliable-ordered channel 0. Both peers require v253;
mod remains 0.1.33 unreleased. Next free message ID at v253: **253**.

- **250 AdvertJobState**, host → guests: Revision:u32, Delivered:i32,
  Sheets:u8, Stage:u8, NextDay:u8, Flags:u8, CompletedMask:u32, Scale:f32,
  Salary:f32, Position:vec3, Rotation:quat. **54 bytes including ID**.
  Revision nonzero; Delivered 0–1000000; Sheets 0–30; native Stage 0/1/2/5;
  NextDay 0–7; Flags bits 0 advert-spawn active, 1 pile active in hierarchy,
  2 pile parented at its spawn; CompletedMask uses native list bits 0–27;
  Scale 0–1; Salary 0–100000000. Finite position components within ±100000,
  quaternion squared norm 0.9–1.1. The catalog maps 27 scene mailboxes to
  native BoxIndex; index 22 has no target in this build. Duplicate scene paths
  are intentional; mailbox index is the identity.
- **251 AdvertSheetState**, host → guests: ItemId:u32, Position:vec3,
  Rotation:quat. **34 bytes including ID**. Nonzero ItemId and the same pose
  validation. Session IDs use `FNV1a32("advert:sheet:" + decimal ordinal)`.
  Existing item ownership/motion handles carrying; ItemDespawn retirement
  prevents cached/queued sheet states from recreating a delivered sheet.
- **252 AdvertIntent**, authenticated guest → host: Sequence:u32,
  ExpectedRevision:u32, ItemId:u32, PlayerId:u8, Box:u8.
  **16 bytes including ID**. Nonzero sequence/revision/item, actor 1–254.
  Box=255 takes one sheet from the singleton pile, otherwise Box 0–27 names
  a delivery target. The sender must match PlayerId; stale/duplicate actor
  sequences are rejected. Requests require the current job revision, a fresh
  living nearby actor, an active native target and compatible item ownership.
  Delivery additionally requires the owned sheet within two metres of the
  target, an uncompleted mailbox and no reservation for that sheet elsewhere.

The pile identity is `FNV1a32("adverts:pile:" + catalog.root)`. Host-native Open
reduces Sheets once and its exact CreateObject output is captured. Delivery uses
native Open/Close; the host reserves the sheet until the native saved-list write
finishes. Guest presentation omits native destruction, Delivered increments and
list writes. The host retains job scheduling, list resets and bank settlement;
guests pause their independent Data/ResetBoxes writers. WorldProgressState's
Classifieds scalars are superseded while the advert profile is enabled.
Mailbox roots beneath the cataloged house/store LOD are temporarily parented
outside that local-camera controller on both peers. This keeps guest delivery
targets available while the host is elsewhere; unrelated houses/NPCs retain their
native LOD. Original mailbox parents/transforms restore on session teardown.

Job revisions advance on state changes (pose is the existing motion stream).
Older/half-range revisions are ignored; equal revisions require identical job
contents. Initial/change and two-second recovery states plus full/item snapshots
supply the ledger. Sheet creation/rejoin and five-second recovery states supply
loose outputs. Guest originals and controllers restore on teardown.
Native save preserves list flags, Delivered, Stage, pile Sheets and pile transform.
Loose advert sheets have no native save controller and are session-only.

### v252 — single-use light-bulb boxes and loose bulb condition

**249 BulbState**, host → guests, reliable-ordered channel 0:
ItemId:u32, Revision:u32, Wear:f32, Position:vec3, Rotation:quat.
**42 bytes including message ID**. Nonzero identity/revision, finite Wear 0–100,
finite position components within ±100000 and quaternion squared norm 0.9–1.1.
Guest-origin states are ignored. Serial uint revisions reject older/half-range
updates; equal revision accepts only the same Wear. Pose belongs to the existing
item ownership/transform stream once materialized. Changed condition and initial
creation publish immediately on the 0.25-second scan; five-second recovery,
join/full snapshots and item resync carry the same state. Retired identities
cannot be recreated by queued, duplicate or late states.

The catalog's `LightbulbBox` package has capacity one. Existing PackageState and
PackageOpenRequest/Receipt authority, ownership, proximity and replay rules apply.
Its native opening creates one bulb then enters Empty (which sets Quantity to
zero); unlike other boxes it has no separate decrement/check-quantity state.
The host captures that exact factory output and assigns
`FNV1a32("bulb:box:" + decimal package ItemId)`. Other host loose bulbs receive
session ordinals under `bulb:loose:`. Guests pause their own contents factory,
hide their original loose bulbs, and construct one replica per host identity,
starting after the native random-condition initialization. Teardown restores
guest originals and factory execution; only replicas are deleted.

Unopened boxes use native save/load. Empty boxes remain consumed after native
save/reload. **Loose bulbs have no native save routine**: identities/condition
survive reconnect to the same session, not a cold game reload. Installed bulbs,
fitting/removal intents and vehicle light operation are outside this slice.
Guests cannot use a raw ItemDespawn to delete a bulb on the host.

Both peers require v252; mod remains 0.1.33 unreleased. Next free message ID: **250**.

### v251 — host-authoritative train

**248 TrainState**, host → guests: Sequence:u32, Phase:u8, Flags:u8,
ColliderMask:u16, HornSequence:u32, Position:vec3, Rotation:quat, Velocity:vec3,
Volume:f32. **58 bytes including message ID**. Phase 0 westbound, 1 west wait,
2 eastbound, 3 east wait. Flag bits 0 train root active, 1 mesh active, 2 lights
active; no other bits. Collider bits 0–10 map to the eleven ordered catalog paths.
The invisible native wait still has its root collider: visibility does not imply
an empty collision mask. Nonzero Sequence, phase ≤3, flags ≤7, mask ≤2047;
finite position components within ±100000, speed ≤31 m/s, waiting velocity zero,
quaternion squared norm 0.9–1.1, volume 0–1. HornSequence may initially be zero.

20 Hz motion uses unreliable-sequenced channel 1; changed phase/flags/colliders/
horn and periodic two-second recovery states use reliable-ordered channel 0.
Join, full snapshot, FSM-group resync and the singleton object ID
`FNV1a32("train:" + catalog.root)` supply the same state on channel 0.
Guests accept only their selected host after handshake. Serial uint ordering
rejects duplicate, older and half-range sequences across both channels.

The guest pauses independent movement, track reset, horn, tunnel-volume and light
controllers. It reparents at route changes, moves the original collision body in
physics updates with native dynamic collision/track constraints, and extrapolates
at most 0.2 seconds. After one second without
fresh state, collisions and sound disable until fresh state arrives. The local
native player-death comparison remains active. New horn counters play once;
initial join adopts the counter without replaying earlier horns. Teardown restores
the original guest controllers, body settings, collision shapes, pose and audio.
The native train has no save tags: cold game reload starts its default route;
joining an existing session instead takes the host's current route and pose.

Both peers require v251; mod remains 0.1.33 unreleased. Next free message ID: **249**.

### v250 — home coffee preparation and drinking

All three messages use reliable-ordered channel 0. Both peers require v250;
mod version remains 0.1.33 unreleased. Vendor/vending-machine cups are excluded.

- **245 CoffeeIntent**, authenticated guest → host: ItemId:u32, Sequence:u32,
  PlayerId:u8, Action:u8 (1 open pot lid, 2 close lid, 3 fill household cup,
  4 drink). Nonzero identity/sequence, actor 1–254; sender must match actor.
  Host consumes fresh actor sequences, requires a fresh living player within
  3 m, and validates the cup holder or uncontested pot. Filling requires cup/pot
  targets within 0.1 m; its 0.8-second lease expires on disconnect, distance or
  ownership loss. Transfer is at most 0.16 units/second, conserves pot water plus
  cup contents, leaves the native 0.1 pot minimum and respects 0.3 cup capacity.
- **246 CoffeeState**, host → guests: ItemId:u32, Revision:u32, Kind:u8,
  Flags:u8, Water:f32, Ground:f32, Coffee:f32, Caffeine:f32, BoilVolume:f32,
  Position:vec3, Rotation:quat. Kind 0 pot, 1 household cup, 2 grounds packet.
  Flag bits 0 lid open, 1 boil sound; no other bits. Finite contents: water 0–2,
  grounds 0–26 (packet 0–100), coffee 0–2 (cup 0–0.3), caffeine 0–1.5,
  volume 0–0.6. Non-pot flags/water/volume, cup grounds, packet coffee/caffeine
  must be zero. Initial pose materializes packet replicas; item motion uses
  existing ownership messages. Pot/cup IDs hash their catalog item paths;
  native save tags resolve carried/reparented household roots on reconnect;
  packets hash `coffee-package:` plus native `groundcoffee0N`, never display names.
  Newer revisions apply on guests; full/item/targeted resync includes contents.
  Host retains native brewing, grounds/tap filling and save events; guest pot
  simulation and original packets are suppressed and restored on teardown.
- **247 CoffeeDrinkResult**, host → guests: ItemId:u32, Sequence:u32,
  PlayerId:u8, Amount:f32, Caffeine:f32. Actor 0–254, amount >0.01 and ≤0.3,
  caffeine 0–1.5, finite; identities/sequences nonzero. Host empties the shared
  cup once and enforces the native 5.52-second drink cooldown. Only the matching
  pending drinker runs native DRINKCOFFEEHOME animation/personal effects; stale
  or replayed receipts cannot consume twice. Unanswered requests leave the
  waiting state after five seconds without local effects.

Payload sizes including the two-byte message ID: 12, 60 and 19 bytes respectively.
Next free message ID: **248**.

### v249 — human taxi passengers

PassengerState (23) retains its existing layout and ordering. The taxi now accepts
human seat indices **0** (front right) and **2** (rear left). Index **1** remains
reserved for the native fare customer, including when the taxi is empty. Host
validation checks this on new claims and continuing keepalives. Inactive cars and
the active taxi tutorial refuse passenger seating; local riders release parenting
and control when their seat becomes unavailable. Claims still require a fresh
nearby living player; accepted moving-car keepalives retain their proximity exemption.
The existing host ledger, conflict decisions, exit corrections and join replay apply.
No new messages: the next free ID remains **245**. Catalog `taxiPassengers` selects
the audited native anchors; Sorbet/Corris retain their three passenger seats.

### v248 — shared sausage package conversion

**243 SausageOpenIntent**, channel 0, authenticated guest → host:
SourceId:u32 (catalog trigger path hash), PackageId:u32, Sequence:u32, PlayerId:u8;
15 bytes including ID. IDs and sequence are nonzero. The sender must match
PlayerId, be alive with a fresh position within 3 m of the trigger bounds, and
name an unconsumed sausage package within 0.25 m of those bounds. A package held
by the host or owned by another guest is refused. Duplicate/older sequences and
already opened packages cannot create more outputs. Native host trigger entries
use the same package identity/availability guard. Four captured native outputs
receive IDs `FNV1a32("sausage:" + decimalPackageId + ":" + index)` (index 0–3).
Package IDs are FNV1a32("sausage-package:" + nativeID), including bag outputs
and saved products; a later purchase cannot reuse a retired display-name ID.
The native package GARBAGE path retains its save tombstone, including guest
package eating. Guest-original packages are hidden/restored and network replicas
are discarded on disconnect. Source-only
HeatSourceIntent ActionGrill is retired; it cannot identify a consumed package.

**244 SausageState**, channel 0, host → guests: ItemId:u32, Revision:u32,
Condition:f32, Kind:u8 (0 fresh, 1 grilled, 2 charred, 3 spoiled), Grilled:bool,
Position:Vector3, Rotation:Quaternion; 44 bytes including ID. IDs/revision are
nonzero; finite Condition is 0–100, position bounded to ±100000, quaternion norm
squared 0.9–1.1. Fresh cannot have Grilled=true; grilled requires it. The separate
boolean preserves native food effects after burning/spoilage. Food revisions use
wrapping u32 order; equal revisions may refresh poses but cannot change food.
Existing ItemTransform/ItemDespawn carry motion/retirement. Join, targeted and
full resync include live sausage states; retirement always wins over deferred
creation. Guests clone the verified sausage prefab, retain native eating, and
pause native cooking/spoilage. Guest-local loose sausages are preserved while
connected and restored on disconnect. Host loose sausages present before hosting
receive session IDs; they have no native save routine and disappear on a cold
reload, as in vanilla. No sidecar persistence is added.

Next unused message ID: **245**. Mod version remains 0.1.33 (unreleased).

### v247 — tractor trailer connection and physics delegation

**240 TractorTrailerState**, channel 0, host → guests: Revision:u32 (nonzero),
Owner:u8 (0 host, 255 invalid), Attached:bool, ConnectedAnchor:Vector3, followed
by exactly three body records in chassis, tipping bed, detached support order.
Each record is Position:Vector3, Rotation:Quaternion, Velocity:Vector3,
AngularVelocity:Vector3. Total 176 bytes including ID. Detached trailers belong
to the host; attached trailers follow the tractor's accepted fresh physics owner.
The host increments Revision when attachment or ownership changes. Join snapshots
carry the complete assembly and native joint anchor. Guests pause their automatic
coupling FSM and reproduce the host's native connection graph.

**241 TractorTrailerIntent**, channel 0, guest → host: PlayerId:u8, Revision:u32,
Sequence:u32 (both nonzero); 11 bytes including ID. This requests release only.
The authenticated player must match PlayerId, have a fresh position within 3 m
of the release handle, and name the current attached revision with a newer
per-player sequence. The host runs native release; duplicate, stale, distant and
forged requests do not apply. Attachment remains native host proximity detection,
including vanilla's two-metre separation before rearming. Requests reset on
player admission; revision isolates subsequent connections.

**242 TractorTrailerMotion**, channel 1 only, simulator → host → observers:
Revision:u32, Sequence:u32, Owner:u8 and the same three body records; 167 bytes.
A guest's sender must be the current delegated trailer and tractor owner, with a
matching connection revision and a newer sequence. Its proposed hitch must stay
within 3 m of the host's tractor hitch. Observers accept motion only for the
current host-issued owner/revision. Revisions reset sequence baselines; streams
from an old attachment or driver cannot reclaim physics. Only the selected host
can send state to guests. The host resumes detached/stale-owner physics using the
last accepted body velocities. The generic chassis lease is excluded. While
coupled, an otherwise unowned parked tractor keeps a host transform lease even
outside proximity range; a real guest driver can still take over normally. Position, normalized quaternion, speed, angular
speed and bed/chassis separation bounds are checked before touching Unity.

Both peers require v247. Next free ID **243**; mod stays 0.1.33 unreleased.

### v246 — household fuse holders

Channel-0 host state **237 HouseholdFuseState** carries Revision:u32 (nonzero),
PowerMask:u16 (11 bits), then exactly 11 holder entries. Entries are in catalog
order: seven house holders followed by four apartment holders. Each entry is
ControlRevision:u32 (nonzero), Slot:u8 (0–10 or 255 loose), Fuse:u8 (0 empty,
1 good, 2 blown), Tightness:u8 (0–8), Flags:u8 (mesh 1, tip 2, insertion trigger 4),
world position:3*f32 and world rotation:4*f32. Loose holders have zero tightness,
fitted holders have tightness 1–8 (a loaded loose holder can retain its old scene parent);
occupied slots must be distinct and in the holder's home. Positions must be finite
within +/-100000 and quaternion squared length within [0.9,1.1].
Holder item IDs are FNV-1a32("household:fuseholder:" + invariant zero-based index).
Only loose holders accept the existing item/cargo motion authority. The host alone
runs native holder Use/save, fuse consumption, fitting/removal and power writes.

Guest intent **238 HouseholdFuseIntent** carries PlayerId:u8, Sequence:u32,
ControlRevision:u32, Holder:u8, Slot:u8, Action:u8, ItemId:u32. Actions are 0 insert
fuse, 1 fit holder, 2 remove holder, 3 tighten, 4 loosen. Sequences/control revisions
are nonzero. Slot is 255 except for fitting; ItemId is nonzero only for fuse
insertion. The authenticated sender must match PlayerId. Acceptance checks fresh
nearby position, current holder revision, native readiness, required item ownership,
empty insertion/installation destination and valid tightness. Repeated/stale intents
cannot repeat changes or consume another item. Cross-home holder fitting is rejected.

Host result **239 HouseholdFuseResult** carries PlayerId:u8, Sequence:u32, Holder:u8,
Accepted:bool and Shock:bool. Shock requires Accepted and only triggers the requesting
guest's native shock check for its outstanding accepted turn. Guest turns suppress
the host's native shock target, so they cannot electrocute the host. Results from
other peers and duplicate/completed sequences have no local effect. Native host
input retains its own shock behavior. Power outcomes and blown fuse presentation
come from host snapshots; original guest holders, power lists and native input are
restored on disconnect. Host native saves retain holder state and consumed fuses.
No new release version: 0.1.33 remains unreleased. Both peers require v246; next ID 240.

### v245 — native taxi payday and salary letter

Message 230 appends 37 bytes after luggage: paydayId:u32 (nonzero),
paydayFlags:u8 (bit 0 native Rundown/unread, bit 1 actual envelope active), then
exactly eight f32 native Rundown values in order: gross fares, salary share,
receipted fares, total kilometres, work kilometres, fuel allowance, phone cost,
net payment. Values must be finite; unknown flag bits or another array length
are invalid. Report identity changes at native settlement and host rebinding.
The service revision still rejects old/repeated absolute snapshots.

New channel-0 guest-to-host message **236 TaxiPaydayReadIntent** carries PlayerId:u8
and paydayId:u32. PlayerId must match the admitted transport peer. Only the current
unread visible report may be acknowledged, within 4 m of the mailbox letter using
a fresh live player position. Duplicate, stale, distant and forged acknowledgments
have no effect. Native guest sheet close sends the ID captured when opening that
sheet; the camera/menu remains local. Acceptance writes only Payments.Rundown=false,
matching the native close action. No payout can be requested with this message.

Payments remains suppressed on guests. The host's native weekday/meter-off checks,
fare share, fuel compensation, phone deduction, bank/net-income credit, statement
entry, ledger reset and ES2 save/load continue unchanged. At settlement, rundown
slot 7 is also set from clamped native Money on the zero-pay branch, correcting
vanilla displaying the previous payday's net amount. Shared balances use
WalletState; meter earnings use TaxiMeterState. The letter's active flag is distinct
from its unread flag because native close does not immediately hide the envelope.
Guests receive the retained rundown before opening their native sheet, and restore
their original list/letter/variables on disconnect. No mid-fare persistence added.
Both peers require v245; next free ID 237; mod stays 0.1.33 unreleased.

### v244 — host-selected taxi luggage

Message 230 TaxiServiceState appends luggageEpoch:u32, luggageMask:u8 and five
(position:3*f32, rotation:4*f32) pairs after skeletonTime (145 bytes). Slot order is
Suitcase1, Suitcase2, Suitcase3, Beercase1, Mattress1. Epoch must be nonzero,
mask is restricted to five bits, positions must be finite within +/-100000, and
quaternion squared length must be within [0.9, 1.1]. Arrays have exactly five entries.

The host alone rolls and resets native luggage. The requested count is capped at
the number of distinct validated entries in the native selection pool; the installed
build can roll six despite having only five usable choices. The unused rifle bag
reference has no Rigidbody and is not a luggage identity. Each native reset advances the
epoch before returning the reused bodies to their hidden pivots. Item identity is
FNV-1a32("taxi:luggage:" + invariant-decimal-epoch + ":" + zero-based-slot).
Old identities are removed; delayed item/cargo packets cannot move a later set.
Active pieces use existing item and vehicle-cargo ownership, including exclusive
native pickup. Hidden pieces reject generic movement and pickup; guests cannot
retire these reusable bodies. Native host luggage distance checks use the accepted
shared poses. Guest random selection and distance penalties remain suppressed.

An epoch or visibility change restores the matching native body and current world
pose. Rejoin receives the same five identities/selection without another random roll.
No saved mid-fare luggage state is added. No new message IDs; next free remains 236.
Both peers require v244; mod remains 0.1.33 unreleased.

### v243 — shared physical taxi receipt

Message 234 appends receiptStage:u8, receiptFlags:u8, receiptPosition:3*f32 and
receiptRotation:4*f32 after offerLabel (30 additional bytes). Stages are 0 hidden,
1 printing, 2 ready in printer, 3 loose/carryable, 4 in customer hand, 5 returned.
Flags are 1 can print, 2 can take, 4 can give, 8 printed strip visible, 16 customer
receipt trigger visible. Unknown stage/flag values and non-finite poses are rejected;
quaternion squared length must be between 0.5 and 1.5. Customer-stage pose is local
to the validated native finger transform; loose pose is world-space. Other stages
use the native hidden printer pivot. FareId zero permits no receipt stage/flags.
CanPrint requires an active charged fare and hidden stage, CanTake active/ready,
and CanGive active/paid/requested/loose. These flags are host availability, not
permission to bypass proximity, identity or current control revision.

Message 235 appends action values 2 print receipt, 3 release printed receipt,
4 give held receipt to customer; its byte layout is unchanged. The existing fare,
revision and sequence rules still apply. Printing enters the audited native state
which adds Trip to OdoTotal and Cost to IncomeReceipts once. Release detaches the
single native receipt. Give requires the actor's accepted receipt ownership, a
fresh nearby actor/receipt and the waiting paid customer; native CASHIER starts
customer departure and the timed paper return. No extra money is awarded.

The reusable paper uses stable item identity FNV-1a32("taxi:receipt") and existing
item transforms only while loose. It is explicitly registered while inactive;
printer/customer anchors suppress generic motion, cargo and guest retirement.
The native pickup guard rejects another holder and releases losing local pickup.
Receipt phase changes release the native hand joint before reparenting. Rejoin
restores the one paper, including a loose pose, without reprinting/recrediting.
Native host save owns receipt/income/odometer totals; no mid-fare persistence added.
The native 99-m departure visibility check uses the closest host camera or fresh
living guest, so a distant host cannot hide Char and suspend receipt cleanup while
a nearby guest is still watching. The threshold and native walking/return states
remain unchanged.

No new message IDs; next free remains **236**. Both peers require **v243**.
Mod remains 0.1.33 unreleased.

### v242 — native host taxi quote and cash collection

Channel-0 host-to-guest `TaxiFareState` (**234**): revision:u32,
controlRevision:u32, fareId:u32, flags:u8, quotedCost:f32, offeredCost:f32,
terminalDisplay:string, offerLabel:string. Flag bits are 1 active taxi, 2 arrived,
4 can charge, 8 cash visible, 16 can collect, 32 Paid, 64 receipt requested,
128 charged. Amounts are finite, nonnegative and at most 10,000,000. Strings allow
128 characters with no NUL. FareId zero permits only the active flag. CanCharge
requires active/arrived and not charged; CanCollect requires active/charged/visible
cash and not paid. Revision ordering is wrapping u32. The host samples at 10 Hz,
suppresses identical views, and supplies two-second keepalives and join snapshots.

Channel-0 guest-to-host `TaxiFareIntent` (**235**): playerId:u8, sequence:u32,
expectedControlRevision:u32, fareId:u32, action:u8 (0 charge at terminal,
1 collect customer's cash). Fare and sequence must be nonzero; actor must match
the authenticated player, with a fresh living position within 3 m of the target.
Fare IDs advance on native taxi boarding. Old fare/control revisions are rejected
before sequence admission; other current requests consume their sequence even on
rejection. Player admission resets that player's sequence and advances the control
revision, preventing delayed pre-rejoin requests from blocking new input.

The host requires its native arrived state, paused positive meter, ready terminal
and an uncharged fare before entering Make payment. That native state captures
Price, subtracts the job timer adjustment and sends delayed CASHIER. The host
allows collection only while that fare's cash is available and not already paid
or claimed. Remote collection applies the native Paid/hide outputs; the host's
Offer money -> Add money transition owns the IncomeTotal credit. There is no
immediate wallet/bank grant and no replay of the host's local achievement action.
Host native terminal/hand actions advance the same control revision. Requests
return absolute state on rejection as well as success.

Guest terminal/cash inputs operate only when offered by the host. Other guest
terminal decisions, printing, price reset and independent income changes remain
suppressed. The meter guard keeps terminal simulation paused if fare binding fails;
a validated fare adapter temporarily owns only safe input states. Guests mirror
the actual terminal text, offer label, amount and cash visibility. Receipt printing,
physical receipt handoff, luggage and complete payday remain separate dependencies.
No new persisted mid-fare state is introduced.

Next free message ID: **236**. Both peers require v242; mod remains 0.1.33 unreleased.

### v241 — shared taxi duty controls and native host meter

Channel-0 host-to-guest `TaxiMeterState` (**232**): revision:u32,
controlRevision:u32, flags:u16, mode:u8, nine float32 values, then two
length-prefixed UTF-8 strings (display and modeDisplay), followed by knobRotation:quat
(local rotation, near-unit length). Mode is the native knob
rotation divided by 35 (0–6). Values, in order: Price, BaseCost, OdoTrip,
OdoTotal, IncomeTotal, IncomeReceipts, Odo100meters, Interval, MpS. Values are
finite, nonnegative and at most 10,000,000; strings allow 128 characters with
no NUL. Flag bits are 1 available, 2 Meter.On, 4 Meter.Off, 8 roof light,
16 customer enabled, 32 driver break, 64 indicators active, 128 light indicator
active, 256 knob on. Other bits are invalid. Native Meter.On means fare paused.

The host sends an absolute view at most four times per second, plus responses
and join snapshots. Guests accept only newer wrapping-u32 state revisions.
They pause native meter/display/payment calculations and mirror host variables,
LCD text, the exact host knob pose and lights. The legacy native visible rotation
can differ from the scalar mode, so guests do not reconstruct that pose. Control
revisions advance for accepted requests
and native host changes, independently of time/distance display updates. Player
admission also advances this revision so delayed pre-rejoin requests stay stale.

Channel-0 guest-to-host `TaxiMeterIntent` (**233**): playerId:u8, sequence:u32,
expectedControlRevision:u32, action:u8 (0 increase mode, 1 decrease mode,
2 toggle roof light/start-pause fare, 3 native long-press total reset). The host
requires the authenticated actor, a nonzero newer sequence, matching control
revision, available taxi, idle controls and a fresh living player position
within 3 m of the meter. Requests for an old control revision are discarded before
sequence admission. For the current revision a sequence is consumed even when
rejected and is reset when that player rejoins. Duplicate/stale requests cannot
repeat a charge-affecting
toggle or total reset. Guests can select modes 0–5; native auxiliary mode 6
remains host-operated, with guest decrease permitted to leave it. The host fires
only validated native control states and returns an absolute view on rejection.

The host retains native tariff, waiting/distance math and saved total fields.
For a car delegated to a guest, the meter reads accepted, matching-owner vehicle
gauge speed from existing VehicleState telemetry; expired (two-second) or missing
samples contribute zero distance. Local-host odometer input remains native.
No client price/distance claim, new fare simulation or saved mid-fare state exists.
Payment, receipts, luggage and the completed earnings journey remain unfinished.

Next free message ID: **234**. Both peers require v241; mod remains 0.1.33 unreleased.

### v240 — shared taxi availability, incoming calls and customer presentation

Channel-0 host-to-guest `TaxiServiceState` (**230**) is one absolute ordered view:
revision:u32, callId:u32, flags:u32, callOwner:u8 (255 = none), colliderFlags:u8,
callPhase:u8 (0 silent, 1 ringing, 2 speaking, 3 finished), customerPosition:vec3,
customerRotation:quat, walkerPosition:vec3, walkerRotation:quat, then the seven
length-prefixed UTF-8 strings pickup, destination, indicatorText, subtitle, voice,
rootClip, skeletonClip, followed by rootTime:f32 and skeletonTime:f32.
Flags 1/2/4/8/16/32/64 are native activeSelf of car/customer root/phone/ignition/
character/indicator/passenger mass; bit128 selects the car boarding pivot as the
walker's parent, otherwise the original customer root. Customer pose is world
space; walker pose is relative to that explicitly selected parent. Collider bits
0–2 mirror the three native job-availability colliders. No other flag bits exist.
Positions and animation clocks must be finite and bounded; quaternions must be
near unit length. Pickup/destination strings allow 128 characters each,
indicator/subtitle 512 each, voice/clips 80 each, with no embedded NUL.

The host samples at most 10 Hz, suppresses identical presentations (animation clocks
alone do not send), and provides a 2-second keepalive plus join snapshots. Guests
accept only newer wrapping-u32 revisions from their admitted host. Taxi customer
pose is removed from the generic `NpcTransform` mover list: activation, parent
and pose travel together, avoiding a stale walking pose applied after boarding.
Guests pause independent job/customer/phone decisions and apply host presentation;
clips and audio resolve through validated native assets, not received FSM events.

Channel-0 guest-to-host `TaxiCallIntent` (**231**): playerId:u8, callId:u32,
action:u8 (0 answer, 1 hang up). Call IDs are nonzero and advance on each native
customer Call entry, including a timed retry. The authenticated transport player
must match playerId. Answer requires the current ringing call, an available taxi
and phone, no existing owner, and a fresh living player position within 4 m of the
phone. The host sets only native Ring.Answer/Occupied; the native caller's wait and
Hangup/SUCCESS still decide when the customer appears. Answer does not open the
host's keypad or move its hands/camera. Hangup requires the same call's owner and
may release the handset after moving away. It clears Answer/Occupied and stops
Ring; early cancellation leaves the native customer timeout/retry intact.

Duplicate answers cannot advance a held/finished call, and old call IDs cannot
answer a later call. Every processed request returns a fresh absolute state, even
when rejected; no currency transaction or independent optimistic fare is applied.
Host native handset input remains authoritative. A disconnected/dead guest owner
releases the handset. Call IDs/owners are session-local, not a new saved fare.
The native game does not persist mid-fare progress. Meter controls, fare terminal,
receipt/luggage identity and wage persistence remain separate unfinished work.

Next free message ID: **232**. Both peers require v240; mod remains 0.1.33 unreleased.

### v239 — shared ignition-wire installation and native destruction

Channel-0 guest-to-host `WiringInstallRequest` (**228**): playerId:u8, token:u64,
sequence:u32, sourceId:u32, expectedRevision:u32 (21 payload / 23 packet bytes).
Only source **5**, WiringIgnitionFusebox, is supported. The sender must be the
handshaken player named in the request. The host validates the exact native graph,
uninstalled state/revision, steering-column Installed prerequisite, recent live
player position within 3 m of an endpoint and the tracked wiring tool within
0.6 m of an endpoint, with no conflicting holder. This tool tolerance accommodates
network interpolation; native local selection still uses the game's 0.1 m check.

Channel-0 host-to-guest `WiringInstallReceipt` (**229**): playerId:u8, token:u64,
sequence:u32, sourceId:u32, status:u8 (18 payload / 20 packet bytes). Status:
0 Pending, 1 Accepted, 2 Busy, 3 Unavailable, 4 Stale, 5 Installed, 6 TooFar, 7 Failed.
The guest retries one immutable operation every 0.5 seconds until a matching
terminal receipt. The host retains each player's latest immutable operation;
repeats replay its result, changed payloads with the same sequence/token and old
sequences are rejected, and an unfinished operation cannot be replaced. Sequence
comparison handles UInt32 wrap. Busy is transient and revalidated. Disconnect
clears that player's operation token; native Installed is the persistence record.

Existing `WiringState` (**193**, unchanged 9-byte payload) adds flag **8 Connectable**
only for available, uninstalled source 5. It is incompatible with Installed/Bolted.
The flag reflects the host's validated connection and steering-column prerequisite;
a change increments the same source revision. All other source flags keep their
previous meanings. Ordered updates, keepalives and join/vehicle snapshots include it.
Next free message ID: **230**. Both peers require v239; mod remains 0.1.33 unreleased.

The guest follows both native endpoint selections, but the final local state is
intercepted before saved writes/reset broadcasts. One approved host native Finish
assembly performs the installation. Guests show the host cable and endpoint gate;
their native wire database is paused and the one Status activation input is
replaced with a temporary host-derived boolean. Original data, actions and visuals
are restored on disconnect. A native host DESTROY (electrical fire) publishes
uninstallation and reopens endpoints when prerequisites permit; replaying an older
accepted request cannot reconnect it. There is no invented manual wire-removal
intent. Other circuits, shocks/fire effects and the complete steering-column fitting
journey remain outside this connection's contract.

The current source5 implementation correction enters both audited native Sound /
CLOSELOOP endpoints on the host rather than jumping directly to Finish assembly.
Pending work is not a new wire revision: updates and join snapshots retain the
pre-request state until full native installation/presentation succeeds. Failed or
timed-out execution restores that checkpoint before its Failed receipt; tool
identity is checked again for each fresh intent. Payloads, status values, sequence
and retry rules above are unchanged. This candidate's native verification is
blocked at world loading; see `docs/V05-IGNITION-WIRE-TRANSACTION.md` for portable
evidence and the explicitly unverified native/persistence gates.

### v238 — fuse boxes and persistent loose supplies

New channel-0 host-to-guest `SupplyItemState` (**227**): factoryId:u32,
nativeId:string, position:vec3, rotation:quat. Payload is 34 + UTF-8 ID length
bytes; packet framing adds two bytes. Next free message ID: **228**. Mod remains
**0.1.33**, unreleased. Guest-originated supply state and other channels are rejected.

The catalog initially permits `Spawner/CreateItems::Fuse`, prefab prefix `fuse0`.
Native positive Int32 counters determine IDs; signs, leading zeroes, overflow,
unknown factories, non-finite positions and non-unit rotations are rejected.
`FactoryItemIdentity.ItemId(factoryId, nativeId)` is the shared item identity.
Repeated snapshots retain one body; retirement prevents deferred/replayed creation.
Snapshots supply a creation pose, while existing item ownership/transform streams
carry subsequent movement. Host snapshots include saved loose fuses after restart.

Existing PackageState (184), PackageOpenRequest (186) and PackageOpenReceipt (187)
now also support the catalog's five-fuse box. The guest sends the existing versioned
opening intent. The host validates ownership, proximity, revision, count and factory
readiness, then executes one native decrement/output. Acceptance requires the exact
next native ID and one initialized shared output. Duplicate requests retain their
original produced-item receipt. The guest cannot run its own contents factory.

Host native box quantity and fuse transform/consumed/deletion keys remain the save
source. Guest saved boxes/fuses are hidden during the session and restored on leave;
replicas bypass guest native load/save. Light-bulb and R20 battery boxes, fuse-holder
installation/removal and electrical effects are outside this bounded extension.

### v237 — shared flea listings and exact sale retirement

New channel-0 messages (next free ID **227**, mod **0.1.33**, unreleased):

| ID | Direction | Payload in order |
|---|---|---|
| 224 FleaListingState | host → guests | revision:u32, count:u8, entries[count] |
| 225 FleaListingIntent | guest → host | player:u8, sequence:u16, revision:u32, itemId:u32, price:u16 |
| 226 FleaListingResult | host → guests | player:u8, sequence:u16, itemId:u32, result:u8 |

Each entry is itemId:u32, nativeNumber:u32, price:u16, position:vec3,
rotation:quat (38 bytes). State payload is 5 + 38×count bytes; intent 13;
receipt 8. Packet framing adds two bytes. Capacity is 64; item IDs and native
numbers must be unique and nonzero, native numbers ≤999999, prices 0–999 MK,
positions finite and rotations near unit length. The initial supported family
is the catalog's native Chips factory product.

The native factory save ID `chipsN` maps to `potato chips(itemx)OWNNNNNN`.
The eight-character suffix preserves native Sell's object-name and price-guide
slicing; `OW` distinguishes it from vanilla's random numeric tags. The existing
native ItemIDs/Items save collections retain identity and price. Network IDs are
reconstructed from the live saved object after restart, never treated as durable
save IDs. Ambiguous or unsupported identities cannot be adopted or sold through
this shared path.

Guests request listing of an existing item, never its creation or sale. The host
requires an active rental, current listing revision, a fresh player pose within
4 m, an unheld item without a remote owner/cargo owner, and a resting position
inside the native table trigger. Accepted items stop participating in loose-item
motion/despawn claims. Guest materialization may lag the listing snapshot; pins
are retried, while existing item tombstones prohibit resurrection. Native host
SELL/SELLRAND selects the exact saved identity, credits its stored price once,
runs native GARBAGE and broadcasts existing ItemDespawn plus updated listings
and finance. Empty newer listings release pins; a removed listing by itself is
not a sale or authority to credit cash.

Receipt codes: 0 accepted, 1 changed revision, 2 unavailable, 3 distant/stale
player pose, 4 claimed/unavailable item ownership, 5 stale/altered request.
Each player's last receipt is immutable. Equal sequences replay only identical
requests; older or altered requests cannot mutate native state. Sequence and
state revision comparisons use half-range wrap ordering. Host authority and
handshake admission apply to all three messages. The v236 finance packets are
unchanged.

### v236 — paid flea-table rental and proceeds receipts

`FleaSaleState` (101, channel 0, host → admitted guests) retains
`sequence:u16, moneyTotal:f32, rentDays:u16, flags:u8` and appends
`revision:u32, weekPrice:f32`: **17 payload / 19 packet bytes**.
Flags: bit0 rented, bit1 collectable envelope. Native day zero is still rented;
unrented is represented by days=0/bit0 clear and restored as native -1.
Days are 0–1006; proceeds must be finite/nonnegative; the finite native week price
must be >0 and ≤1,000,000. Collection requires flags=2 and positive proceeds.
Equal revisions require identical scalar/flag values; newer revisions use the
forward wrapping uint half-range rule. Snapshot and live state use the same quote.

`FleaSaleIntent` (102, channel 0, authenticated guest → host) retains
`action:u8, playerId:u8, sequence:u16` and appends `revision:u32, weeks:u16`:
**10 payload / 12 packet bytes**. Actions 0 (old unpaid rent) and 1 (previously
reserved collection) are retired/reserved and rejected. Action 2 pays for 1–52
weeks at native checkout; action 3 collects the currently available proceeds and
requires weeks=0. A request references the displayed table revision and never
supplies a price, wallet balance or proceeds amount.

`FleaSaleResult` (223, channel 0, host → admitted guests):
`playerId:u8, action:u8, sequence:u16, result:u8`: **5 payload / 7 packet bytes**.
Result: 0 accepted, 1 changed quote, 2 unavailable, 3 distant, 4 funds/unsafe cash
precision, 5 stale or changed duplicate. The host remembers one immutable last
receipt per player, consumes denied sequences, and echoes exact retries without
repeating the transfer. Altered same-sequence requests and older/ambiguous sequence
values cannot mutate the table. Authenticated rejoin clears that player's receipt.

The host validates current availability, fresh player pose within 4m of the
specific checkout/envelope, current quote and exact finite cash transfer. Cash,
rental days and proceeds change together on the host; snapshots precede receipts.
Local host rent-only checkout uses the same transaction path. Host mixed
merchandise baskets retain native checkout; guest checkout currently requires an
otherwise empty basket. Selecting a rental week only changes the local cart.
MoneyFlea is excluded from generic FSM control replay. Guest table logic, sale RNG
and day timer pause, and the original native finance/cart/envelope state restores
on cleanup. Shared listing identity/pricing, exact sold-item retirement and listing
persistence remain the next implementation step; this revision does not claim them.

Next free message ID: **224**. Mod remains **0.1.33**, unreleased.

### v235 — shared ATF cap position on the Corris

`AtfFillerState` (221) appends `capLocalPosition:vec3, capLocalRotation:quat`.
Its complete layout is `vehicleId:u32, revision:u32, rotation:f32, oilLevel:f32,
flags:u8, capLocalPosition:vec3, capLocalRotation:quat` — **45 payload / 47 packet
bytes**. The pose describes the native `OpenCap` transform relative to the
tracked Corris root body, not the independently simulated engine body. Position
components must be finite and each within -5 through 5 metres inclusive;
quaternion components must be finite with squared norm 0.9–1.1. The existing
IDs, scalar bounds, availability bit and channel-0 host-only admission remain.

Cap movement participates in filler revisions. Poses are equivalent within
1 mm Euclidean displacement and 0.1 degree normalized rotation difference;
opposite quaternion signs represent the same orientation. Equal revisions
accept only equivalent poses and unchanged scalar fields. Newer revisions use
the existing forward wrapping rule. Publication compares against an anchored
pose so repeated sub-millimetre movement cannot hide accumulated displacement.
Join and repair snapshots carry the same pose and revision.

Guests project the complete cap subtree, including its visible cap, interaction
collider and native filler sphere, relative to their current Corris root. They
preserve and restore its original local pose on cleanup. The engine Rigidbody,
hinge, assembly data and delegated car ownership remain untouched. Host transfer
still requires current ownership, source freshness and actual native collider
overlap; the pose extension does not widen the accepted refill geometry.

Next free message ID remains **223**. Mod remains **0.1.33**, unreleased.

### v234 — guest ATF refill and shared bottle contents

Three new messages use reliable-ordered channel 0. States 220/221 are admitted
host → guests only; intent 222 is authenticated guest → host only. Other channels,
wrong-role senders, unauthenticated intents and state before admission are refused.

`AtfBottleState` (220): `itemId:u32, revision:u32, nativeId:string, fluid:f32,
empty:bool, position:vec3, rotation:quat` — **43 + N payload / 45 + N packet
bytes**, where N is the UTF-8 native-ID byte length and the string has the ordinary
u16 length prefix. Item ID and revision are nonzero. Native ID is at most 64
characters and exactly the native factory prefix `atfoil0` followed by an
unpadded positive Int32 counter (`atfoil01`, `atfoil02`, …). Fluid is finite
0–1. Empty is strictly byte 0/1; an empty bottle may retain native fluid ≤0.1,
so emptiness is independent of exact zero. Position is finite and quaternion
components are finite with squared norm 0.9–1.1. Creation pose permits a missing
guest bottle to be reconstructed; existing motion retains its item transform
stream. Equal revisions may refresh creation pose only when ID, native ID,
quantity and emptiness agree. Newer revisions use a forward uint delta less
than half range; stale, zero, half-range and equal-conflicting state is rejected.

`AtfFillerState` (221): `vehicleId:u32, revision:u32, rotation:f32, oilLevel:f32,
flags:u8` — **17 payload / 19 packet bytes**. Vehicle ID and revision are nonzero;
rotation is finite 1–359, and oil level is finite -1–6.3, allowing the native
slightly negative depletion result. Flags permits bit 0 (1), mounted automatic
gearbox available. The cap is open only at rotation 1. All state fields other
than revision must agree at equal revision; later revisions use the same wrapping
rule as bottle state. Both states participate in join, group and targeted
repair snapshots without consuming their ordinary publication baselines.

`AtfRefillIntent` (222): `vehicleId:u32, bottleId:u32, playerId:u8, sequence:u16,
action:u8` — **12 payload / 14 packet bytes**. Vehicle ID is nonzero and player
is 1–254. Action 0 stops pouring, 1 renews pouring, 2 unscrews the cap and 3
screws it. Pour/stop requests require a nonzero bottle ID; cap requests require
bottle ID zero. No guest supplies an amount, resulting level or elapsed time.
The host authenticates the player, then consumes fresh per-player ushort
ordering before checking geometry. Duplicates, older and half-range sequences
are refused; a rejected request cannot become valid by replaying it after moving
closer. Admission clears that player's ordering; session cleanup clears the ledger.

Host cap actions use native steps of 33 clamped to 1–359. Pouring has a host-clock
lease no longer than 0.6 seconds after the latest accepted keepalive; duplicates
cannot renew it. While that lease is alive, the host continuously validates
the current bottle owner, fresh living player/item pose, bottle tilt, native
filler overlap, open cap and available automatic gearbox. Loss of those
conditions stops transfer. Host transfer per tick is
`min(source, max(0, 6.3 - target), 0.1 * min(dt, 0.25))`; only finite valid
source/target and positive finite dt grant any transfer. That one amount is
deducted from the bottle and added to the gearbox, so neither retries nor a
stalled frame creates fluid or supplies catch-up credit. Guest state projection
must preserve its original native bottle/cap state for session cleanup and must
not replay destructive native empty-bottle actions on a borrowed local object.

Next free message ID: **223**. Mod remains **0.1.33**, unreleased.

### v233 — shared home stove controls and native cooking heat

`StoveKnobIntent` (219), authenticated guest → host on reliable-ordered channel 0:
`applianceId:u32, playerId:u8, plate:u8, direction:u8, sequence:u16` (9 payload /
11 packet bytes). Plate is 0–3, direction 0 decrease / 1 increase, player 1–254
and appliance ID nonzero. Only a fresh living guest within three metres of an
active native knob may turn it. Per-player wrapping sequences reject duplicates,
old and half-range inputs; a distant rejection consumes ordering. Readmission
forgets that player's sequence. The host uses its validated native step, wrap,
rotation and read actions; the guest cannot supply a setting or temperature.

`ApplianceState` (99) appends `stoveRevision:u32`, four `stoveHeat:f32`, four
`stoveRotation:f32`, `grillMask:u8`, `burnMask:u8`. Payload is 50 bytes (52 with ID).
Existing byte heats remain the legacy 0–100 representation. The appended heats
use native degrees (finite 0–750); rotations are native signed Rot values (finite
-360 through 330). Masks use bits 0–3 for the four plates and may not overlap.
Flags adds bit 2 (4) for the native stove indicator light and bit 3 (8) for native
smoke emission; these use the existing flags byte without changing frame length.
For a nonzero stove revision, appliance ID must be nonzero, kind 0, flags limited
to bits 0–3 and FirePlate 0–4. Revision zero means the native stove extension is unavailable.
Nonzero revisions advance on native control/heat/trigger/light/smoke/fuse/fire changes,
wrap past zero and reject stale, half-range or equal-conflicting receipts.
Snapshots do not consume the normal publication baseline.

Both catalogued home stoves use host-only simulation and publication. Guests
retain original controls, temperatures, fuse, fire-hazard counters and trigger/light/smoke settings, replace
native knob mutations with intents, and apply accepted absolute results without
entering the host's mouse-input states. Their native simulation pauses until
session cleanup restores the original values and actions. Legacy guest fire
reports are refused while these stoves are waiting, bound or failed; existing native ignition
actions retain each home's enabled/disabled settings. This version does not add
sausage-package conversion or a new food-state message; existing moose-meat
state 210 supplies that food's cooked result and rejoin recovery.

Next free message ID: **220**. Mod remains **0.1.33**, unreleased.

### v232 — host-confirmed native tyre punctures

VehicleWheelHealthState (205) appends four uint32 tyre lifecycle epochs in
FL/FR/RL/RR order after the existing four health floats. Its payload is now
41 bytes (43 including the ID). The host advances a wheel's nonzero epoch when
its mounted native part changes, availability changes, health increases (repair),
or positive health reaches zero. Ordinary decreasing wear retains the epoch;
other wheels' punctures do not retire a pending request for this wheel. Epoch
wrap skips zero. Epoch-only changes advance the existing publication revision,
are copied into snapshots and cannot consume the normal broadcast baseline.
Equal-revision conflicts include epoch differences. Zero epochs can represent
uninitialized input; they cannot authorize puncture requests.

WheelPunctureRequest (218), guest to host on reliable-ordered channel 0, is
`vehicleId:u32, playerId:u8, wheel:u8, sequence:u16, epoch:u32` (12 payload / 14
packet bytes). Vehicle ID and epoch must be nonzero, player is 1–254 and wheel
is 0–3. A validated native guest flat-state entry reports the event only for the
local driver with available positive host health. Guest saved-health writes stay
blocked; health projection may temporarily return the guest to healthy until the
host confirms damage.

The host requires the authenticated current remote driver, a living player,
fresh vehicle motion within two seconds, an available positive health result
and the matching wheel epoch. The existing wrapping callback ledger refuses
replays and limits each car to a burst of four events, replenished at one per
second. Ordering is consumed before native validation, so rejected events cannot
become delayed writes after repair; readmission forgets that player's sequence.
The host validates the native zero-health action and exact mount, executes only
that write, then publishes message 205 immediately. Native host/guest Condition
selects the resulting flat/rim behavior. Observer replay never reports damage.
No guest health amount, saved target or replacement identity is accepted.

Next free message ID: **219**. Mod remains **0.1.33**, unreleased.

### v231 — shared flatbed wood delivery

FirewoodLoadState (216), host to guests on reliable-ordered channel 0, carries
`revision:u32, epoch:u32, logs:f32, firewood:f32, mass:f32, bedScale:f32,
unloaded:f32, unloading:u8, pileCount:u8`, followed by each ground pile's
`position:vec3, rotation:quat, scale:f32`. The whole manifest replaces the previous
one, including an empty manifest. Payload is 30 + 32N bytes; packet is 32 + 32N.
Maximum 128 piles (4,128 packet bytes). Load/unloaded are in [0,1600], firewood in
[0,10000000] (native Firewood can exceed clamped Logs), mass in [1,100000], scales
in [0,1], unloading is 0/1, positions are finite within +/-1000000 and quaternion
squared norm is [.9,1.1]. All numeric inputs must be finite. Native host values and
existing native ground piles supply the state; no guest quantity is accepted.

Changed state broadcasts at most five times a second, with five-second keepalives
and full join/resync snapshots. Wrapping revisions reject older/equal states;
retained state repairs guest presentation without replaying payment or unloading.
Snapshots do not consume the ordinary broadcast baseline. Guests wait for their
native load to finish, preserve original trailer values/mass/pile presentation,
pause local consumption and ground-pile generation, and restore them on cleanup.
The native host alone changes delivered Surplus, calculates Money, sends UNLOADED
and completes the order. Existing guarded payment collection still credits once.

FirewoodUnloadIntent (217), guest to host on reliable-ordered channel 0, is
`playerId:u8, epoch:u32, sequence:u32, unload:u8` (10 payload / 12 packet bytes).
The authenticated sender must own playerId; 255 and nonboolean unload are invalid.
The host requires a live enabled trailer, a living guest pose no older than two
seconds within 12 m, the current load epoch, a fresh wrapping per-player request
sequence and an actual change in unloading state. Starting also requires at least
10 units. Native load additions and reset advance the epoch so a completed load
cannot be unloaded again by an old request. Readmission resets the player's
sequence latch. Rejected requests return current state without changing stock.
Guest native hatch/tilt writes request unloading; guests do not consume locally.
Pausing and resuming keeps the same host ground pile instead of creating another.

JobSiteState (56) layout is unchanged. Kind 2 (firewood) now preserves signed
Secondary/Penalty, including the native -100 early-delivery bonus; other kinds
retain nonnegative clamping. This changes semantics and requires v231.

Next free message ID: **218**. Mod version remains **0.1.33**, unreleased.

### v230 — authoritative phone invoices and payments

UtilityBillState (95) retains meter:u8, unpaidBills:f32, flags:u8, revision:u32.
For phone meters 2/3, append eight float32 values, in order: Minutes, MinutesLong,
Connects, ConnectsLong, base fee, connection rate, local minute rate, long-distance
minute rate. Phone state is 42 payload / 44 packet bytes; electricity meters 0/1
remain 10 payload / 12 packet bytes. Phone flags now allow bit 1 for envelope
visibility alongside bit 0 PhonePaid; bit 2 remains electricity-only. Phone state
requires finite nonnegative inputs and finite arithmetic intermediates. Electricity
state cannot carry phone fields. Copies include independent phone inputs.

The native sheet's total is min(999999, (Connects*connectionRate +
Minutes*minuteRate) + (ConnectsLong*connectionRate + MinutesLong*longMinuteRate)
+ baseFee), with float32 intermediate arithmetic in native action order. Tariffs
come from the validated host sheet variables, not the guest's save. UnpaidBills
remains the separate native meter accumulator and is not substituted for the
phone sheet price. Revisions advance for amount/visibility or any phone usage or
tariff change, including changed details that leave the total unchanged.

Existing UtilityPaymentIntent/Result (211/212) now accept meter 0–3 with unchanged
layouts and channels. Only an authenticated guest may send an intent; host input
uses the same ledger. The host requires the displayed revision, unpaid visible
envelope, fresh living player within 6 m, enough finite cash and the validated
native settlement graph. One accepted receipt debits the host sheet price and
runs native Pay bills once: PhonePaid=true, hidden envelope, zero debt, cutoff
timer and four usage counters. Repeated/competing requests cannot debit it again.
Guest Date retains local feedback/OldBill presentation only, without wallet or
meter mutation. Native phone calculation starts with zero CostFinal so reopening
does not accumulate old charges. Both bill detail columns and total come from
the retained host quote. Guests pause native phone timers after loading and
restore original meter/usage/envelope state on disconnect; closing the sheet
forces its next local opening to recalculate. Host payments continue with no
guests connected. Join/resync forces fresh state without replaying payment.
The generic PhoneBill purchase descriptor is removed. Next free ID remains 216.

### v229 — terminal permadeath runs

The existing rule is unchanged: the first player's death ends everyone's
permadeath run. On the first authenticated death report (29), the host records a
terminal wipe before publishing PlayerDeathEvent (30). Duplicate reports cannot
publish another wipe. Guests retain that terminal state when applying the host's
wipe flag. Scene changes, including the native newspaper/obituary/MainMenu path,
do not clear it. Only transport-session teardown resets it for a new session.

PlayerRespawn (31) is accepted and relayed only in a normal, non-wiped run.
An ended host rejects every subsequent handshake, including a returning peer,
with the explanation that the host must start a new session. Changing the native
permadeath setting cannot reopen an already-wiped run. Normal death/recovery
continues to use the v226 readiness requirements.

Remote group death sets the cause before activating the native death graph at
Permadeath 2; it never forcibly re-enters State 3. Both native deletion stages
execute once on the host. GuestSaveGuard suppresses the guest's native world-file
deletions and remains active through menu loading and disconnection. This is a
semantic version bump: packet layouts, IDs and channels are unchanged. Next free
message ID remains 216.

### v228 — host corpse and guest chopping

`MooseCorpseState` (214, ordered channel 0, admitted host → guests) carries
corpse:u32, revision:u32, dead:u8 (0/1 only), frontPieces:u8, rearPieces:u8.
Dead corpses append exactly 11 pairs of position:vec3 and rotation:quat, in the
catalog `mooseChop` body order (root, body1…body10). Living state has no poses
and zero pieces. Counts are 0–4, corpse identity is nonzero, positions are finite,
and rotations have squared norm 0.9–1.1. Payload is 11 bytes alive or 319 dead
(13/321 packet bytes). Newer uint serial revisions only; within one identity death
and consumed counts cannot go backwards. A newer corpse identity resets counts.

The native ragdoll survives after CarHit destroys the live mover; it now supplies
its own state every 250 ms while dead, every second otherwise, including late
joins/reconnects. Guests preserve their own animal for disconnect, hide the live
copy, freeze the 11 native corpse bodies and apply the host's poses/counters.
Guest CarHit intercepts before native destruction and retries the existing
NpcDeathReport (110) for up to five seconds. The host validates a fresh live reporter
within 150 m and runs the native death entry once; layouts for that report are
unchanged. Guest native state and action lists restore on disconnect.

`MooseChopIntent` (215, ordered channel 0, authenticated guest → host) carries
playerId:u8, corpse:u32, section:u8 (0 front/1 rear), expectedPieces:u8 (0–3):
7 payload / 9 packet bytes. Sender identity must match the player. The native axe
object comparison and sound remain; the guest's Pieces entry sends the request
and returns to native cooldown without spending a local piece. Pending requests
retry every 800 ms for up to three seconds, stopping when the authoritative count
changes. The host requires that corpse identity and count, an active corpse, an
idle enabled section/factory, and a fresh alive player within 4 m of that section.
It enters native Pieces with Spawnpoint temporarily set to the validated section,
so meat appears there instead of at the host's axe. Counts advance synchronously;
retries and concurrent requests for an already-consumed piece cannot spawn again.
Existing MooseMeatState supplies exact shared outputs. Next free message ID: 216.

### v227 — native permadeath settings

Handshake session flag bit 0 now comes from the native `savefile.txt` tag
`PlayerPermaDeath` (or the loaded host GAME variable). Guests apply that flag to
their running global variable and override the native LoadBool action before it
can advance startup readers. Guest personal saves are never rewritten to match.
Native host load/character-save changes publish `SessionSettings` (213), one flags
byte, reliable-ordered channel 0. Only bit 0 is defined; unknown bits are rejected.
Only the selected host may send this message after the guest handshake completes;
hosts reject all incoming settings. Each update replaces the current session
setting, including false; duplicate reliable updates are idempotent. This covers
guests already connected while the host completes native character creation/load.
Existing handshake and death-message layouts are unchanged. Next free ID is 214.

### v226 — native passenger death and recovery

Death report/event (29/30) now retires passenger occupancy at native State 3,
before movement components are destroyed, with Take photo retained as an
idempotent fallback. The inactive death graph is bound before activation.
PlayerRespawn (31) requires an active GAME player with enabled native movement
components and a finished death graph; guests also finish their spawn selection
and relocation. A newspaper timeout never establishes life or publishes a cached
death pose. Local death remains pending through the native MainMenu/load path.
Layouts, channels and message IDs are unchanged; next free ID remains 213.

### v225 — acknowledged electricity payments

`UtilityBillState` (95) appends invoice `revision:u32` after the existing flags:
10 payload / 12 packet bytes. Electricity revisions advance when native debt or
envelope visibility changes; power/switch changes alone do not change the quote.
Phone revisions remain zero and their existing payment behavior is unchanged.

`UtilityPaymentIntent` (211, ordered channel 0, guest → host) is playerId:u8,
meter:u8, sequence:u16, revision:u32 (8 payload / 10 packet bytes). Only electricity
meters 0/1 and player IDs 0..254 are valid. Host-local clicks use the same path.
The host authenticates the sender, checks a fresh live pose within 6 m of the
meter's current envelope reference, and validates the displayed invoice revision,
visible positive debt and sufficient finite shared cash. The host clears the
invoice once and runs its native Pay bills event; its sheet may stay inactive.

`UtilityPaymentResult` (212, ordered channel 0, host → guests) is playerId:u8,
meter:u8, sequence:u16, result:u8, paid:f32 (9 payload / 11 packet bytes). Results:
0 accepted, 1 changed quote, 2 unavailable, 3 distant/missing fresh pose,
4 insufficient or unrepresentable funds, 5 stale request. Accepted has positive
finite paid; all other results have zero paid. Only the requesting player's
matching pending sequence changes its local menu. State and shared wallet precede
the result. Guest or host presentation never replays native cash subtraction or
meter settlement.

Each electricity meter keeps one receipt per player. Exact retries return that
receipt without spending again; altered revisions at the same sequence and older
sequences are stale (forward ushort delta 1..32767, wrapping supported). A paid
invoice is unavailable to competing requests. Admission/departure clears that
player's receipts. One pending click retries every second until a receipt or
session teardown. Disconnect closes pending menus before restoring native actions
and the guest's original meter state. Next free message ID: 213.

### v223 — effective electricity and bill cutoff (historical layout)

`UtilityBillState` (95) retains its six-byte payload (eight with ID): meter:u8,
unpaidBills:f32, flags:u8. Electricity meters 0/1 now report the actual global
`HouseElectricity` / `HouseElectricity2` in bit 0, independently of the physical
`MainSwitch` in new bit 2. New bit 1 reports the native bill envelope's active
state. An unpaid-bill cutoff may therefore send flags 6: power off, bill visible,
switch on. Phone meters 2/3 keep bit 0 = `PhonePaid`; bits 1/2 are invalid for phones.
Unknown meters, unknown flag bits, non-finite or negative debt are rejected.
Debt keeps its native float precision.

The host publishes settled native meter state on change, keepalive and join.
Guests retain the latest ordered state in four bounded slots while discovery/save
loading completes. Electricity Data FSMs are reversibly paused after loading so
local bill/cutoff timers and native every-frame global writes cannot override the
host. Receivers apply supply, switch, debt and envelope visibility; native appliance
controllers observe the supply normally. Disconnect restores the guest's original
values, envelope and paused execution. Phone timer behavior is unchanged. Payments
still use the existing host-authoritative purchase path; this packet is not a
payment request. No new message ID is allocated; next free ID remains 210.

The native fridge Chilling graph remains unchanged, including its latched cooling
area after power loss until an OPEN/CLOSE cycle. Milk's existing authoritative
condition stream continues to follow the host's actual native cooling result.

### v222 — cylinder-head fastening

`CylinderHeadState` (207) appends `fastenersAvailable:u8` (strict 0/1), ten
`fasteners:u8` in native Bolts array order 0–9, then `tightness:f32`. Payload size
is 59 bytes (61 including the ID). Each bolt is 0–8; total tightness is finite in
[0,10000]. Unavailable requires zero for every appended value. Native aggregate
tightness is transmitted independently of the array; it is not recomputed as a
sum. Changes to availability, any bolt or the aggregate advance the same head
revision used by attachment. Equal revisions must agree on both attachment and
fasteners. Host keepalive, join and targeted resync include the complete state.

PartFitRequest/Receipt (188/189) retain their layout and operations. ToolTighten
(6) and ToolLoosen (7) now permit slot 1–10 for the catalogued persistent head;
the slot is the native array index plus one. Existing spark-plug operations still
require slot zero. Other families cannot use a head slot. The authenticated host
checks the current revision, fitted head/block mount, settled native fastener,
fresh living guest position within three metres, native bounds and cooldown.
The shared operation ledger rejects altered duplicates and prevents retrying a
turn from applying it twice. Absolute head state precedes the final receipt.

Head fasteners no longer register with generic raw-event/BoltState synchronization.
The guest's native tool still reads Screw.Boltsize and sends TIGHTEN/UNTIGHTEN,
but a reversible display graph sends the indexed request. Guest native Bolts,
Data.Tightness and assembly data remain untouched. Controls are available only
after fitted host state is applied and while native repair mode is active.
Removal disables them; refitting/rejoining requires the current head revision.
No message ID is added or reused; next free ID remains 210.

### v221 — exact Sorbet parking brake setting

`VehicleClimate` (61) appends `ParkingBrakeAvailable:u8` (strict 0/1) and
`ParkingBrake:f32` after `IceMask`. The payload is now 26 bytes (28 with packet
header). The fraction must be finite and within [0,1]; absent means exactly zero.
The existing authenticated vehicle simulator/sequence rules apply, including the
reliable final climate packet before motion ownership release and host-only
snapshot sentinel. Copies and resync preserve the setting. Unknown/unsupported
native brakes publish absent. The catalog currently enables Sorbet only.

The active simulator alone runs native timed lever actions. Those controls no
longer replay generic FsmStateEnter deltas or snapshot states on observers;
receivers apply the accepted scalar to the validated native clamp range. A nearby
user can claim an unowned vehicle for an adjustment; another player's current
vehicle lease blocks that adjustment. Host fallback keeps the final setting after
release, and periodic climate reports repair drift. Old peers are refused by the
version handshake. No message ID is added or reused.


v220 adds FirewoodBuyerState (209), host -> guests on reliable-ordered channel 0.
Wire order: `netId:u32, revision:u32, flags:u8, amount:f32, position:vec3, rotation:quat`
(41 payload / 43 packet bytes). The nonzero ID is the catalogued payment path's
`::Use` hash, retained through native reparenting. The job's Buyer/ThisMan and the
buyer's PayMoney references resolve the live objects, including customer 1's move
between CarPos and WoodPos. Bit 0 means
the buyer is present; bit 1 means a collectable offer. An offer requires both bits
and a positive finite amount. Without an offer the amount must be zero; other bits
are invalid. Only the selected, admitted host may publish this message.

Buyer/offer/amount or pose changes advance a wrapping revision. Position is the
NPC root's world position, finite within ±100000 on each axis; rotation has squared
quaternion length in [0.9, 1.1]. Guest root position/rotation follow the host without
running native job or car-purchase transitions. Older and contradictory
equal revisions are refused; identical equal revisions may repair guest LOD drift.
Unbound state is copied only for the four catalogued IDs. The host checks every
quarter second and sends changed state plus a three-second keepalive. Join
snapshots include all four buyers without consuming the normal broadcast baseline.

Guests pause native buyer/job decisions and apply pose, visibility, native offer-arm
presentation, payment collider and amount label. The native mouse collection state
still sends the existing v217 guarded intent; the update never enters a credit or
car-purchase state. A local collection has a one-second correction grace period.
Host buyer LOD and post-job departure checks include living guests with poses no
older than two seconds, using native distance thresholds. All edits are restored
on cleanup. Actual wood delivery and signed job penalties remain separate work.
Mod version stays 0.1.33, unreleased. Next free message ID: **210**.

v219 extends PartFitRequest/PartFitReceipt (188/189) to the catalogued persistent
cylinder head VIN1110; layouts and IDs are unchanged. Operations install/remove
(0/1), slot zero, refer to CylinderHeadState (207)'s observed revision. The shared
part-operation ledger authenticates player/token/sequence and replays receipts
without rerunning native actions. Other operations on this head are unavailable.

The host requires the exact head/block mount, ready native FSMs, a living guest's
pose no older than two seconds within three metres of both targets, and an idle
operation slot. Installation additionally requires loose state, native tolerance
(<0.1 m on the current build) and current guest pickup ownership or its existing
half-second release grace. Removal requires fitted state and finite tightness in
[0,1). The native ASSEMBLING/Remove paths run their actual mount prerequisite
checks. Same-frame guards recheck the candidate before confirmation; unconfirmed
previews are cancelled synchronously. Committed operations are observed for up to
three seconds without being replayed. Both part families share the busy gate.

The guest uses fit/remove prompts with the existing acknowledged request client;
its saved head Data and bolts remain suppressed. The host sends message 207 with
receipts and settled results; guest placement still uses v216's reversible view.
This does not add fastening bolt control, missing-head reconstruction or complete
child-part parity. Protocol 219 is unreleased, mod version stays 0.1.33 and next
free message ID stays 209.

v218 adds MilkConditionState (208), host -> guests on reliable-ordered channel 0.
Wire order is `netId:u32, revision:u32, condition:f32, spoiled:u8` (13 payload /
15 packet bytes). The nonzero ID addresses a live milk carton. Condition must be
finite and in [0, 100]; spoiled is 0 or 1, and 1 requires condition <= 1. The
native consumed sentinel 888 is excluded. Condition and native Bad phase changes
advance a wrapping revision; older or contradictory equal revisions are refused.
An identical equal revision may repair local drift.

The host keeps the native warm/fridge decay and publishes changed state at most
once per second, plus a five-second keepalive. Join snapshots, item-group resync
and targeted object replies include the state without consuming the ordinary
broadcast baseline. Guests replace the two native decay branches, await native
initialization and a host seed before drinking, and enter the native spoiled
presentation when instructed. Unknown-item state is copied into a bounded queue
(256 entries, 120-second expiry) until the item exists; retired IDs are ignored.
Saved spoiled milk uses the fresh creation template plus this state. Cleanup
restores original guest actions, condition and name unless the carton was consumed.
This covers loose milk only; other food, bag contents, fridge electricity changes
and cooking retain their existing handling. Next free message ID: **209**.
Mod version remains 0.1.33 (unreleased); the shipped compatibility manifest is
unchanged and must be updated by the release workflow when shipping is requested.

v217 changes catalogued firewood `PayMoney::Use / State 1` handling. A guest's
`FsmStateEnter` requests collection only while the host has an active, enabled,
positive finite payment in `Wait player` or `Wait button`. The host reserves it
before native entry, rejects concurrent/repeated/inactive requests without queuing,
and alone runs the cash and net-income additions. Guests ignore payout replays;
`WalletState` carries the shared balances. Payment controls are excluded from
`WorldDoorSnapshot` replay. There are no new fields or message IDs. This does not
add delivery, buyer visibility, or pending-offer synchronization.

v216 adds CylinderHeadState (207), a host-only reliable-ordered attachment for the
catalogued persistent scene head VIN1110. Payload: `netId:u32, revision:u32,
parentId:u32, worldPosition:vec3, worldRotation:quat, looseMass:f32` (44 payload /
46 packet bytes). Parent zero means loose; otherwise it identifies VIN1010, whose
catalogued VINP_Cylinderhead mount receives the head at zero local position,
identity local rotation and unit scale. The host publishes a fitted state only
when native AssemblyID, destroyed Rigidbody, mount occupancy and hierarchy agree.
Loose state requires the settled native Stop state. Positions/mass must be finite,
quaternions approximately unit length; loose mass is (0, 10000], fitted mass zero.
Unknown head/parent IDs are rejected by the catalog binding.

Attachment/mass or native body replacement advances the wrapping revision. Older
and contradictory equal-revision states are refused; loose motion itself does not
advance it. Guests apply recovery poses on attachment changes, then use ordinary
ItemTransform movement while loose. Host keepalives, join snapshots, item-group
resync and targeted head requests carry the attachment. Item checksums include
head identity and parent. Guest Data and saved fastener controls are paused; only
hierarchy, collision/pickup presentation and temporary loose physics change.
Saved fitting/condition/tuning variables remain untouched; cleanup restores the
original pose and physics. Valve intents still require the unchanged host 3 m
proximity check. This does not add guest head-install/remove intents, guest head
fastening-bolt controls, missing-head reconstruction or complete child-part parity.
Next free message ID: **208**; mod version remains 0.1.33 (unreleased).

v215 adds ValveAdjustmentState (206), an absolute `netId:u32, setting:f32`
host-to-guest message on reliable-ordered channel 0 (8 payload / 10 packet bytes).
The ID addresses a native cylinder-head valve Screw using the persistent part ID
and relative child path. Settings must be finite and in [2, 8], with a nonzero ID.
FsmRawEvent (41) now also accepts TIGHTEN/UNTIGHTEN on these separately validated
controls: an authenticated living guest must have a fresh pose within 3 m of a
ready loose or fitted host head. The host executes the native 0.05 float step and
clamp immediately, then broadcasts the settled value. Guest absolute settings
are rejected at message admission; raw turns are never relayed to guests.
Joining snapshots and targeted object replies include message 206. Pending guest
settings retain reliable receipt order and expire after 120 seconds. Guest controls
wait for host state, send intents without prediction, and display setting×50 degrees
without running the native array writer. Their saved head arrays remain unchanged;
native controls, scratch and visual pose restore on session cleanup. Full physical
cylinder-head reconstruction and mismatched/missing native heads remain outside
this adapter. EngineBlockState (195) retains the existing valve-engine-input layout. Next free
message ID: 207.

v214 keeps the wire layouts unchanged and fixes guest profile restoration.
Guests publish PlayerTransform (22) and PlayerNeedsReport (25) only in the GAME
scene after applying their initial GuestSpawn (24) offer and completing relocation.
Returning guests restore saved needs with either spawn choice. Later snapshot
offers cannot replace a pending choice or respawn a guest who is already playing.
Session reset and leaving GAME discard the previous offer and pending relocation.
This prevents loading/menu values from overwriting the host's saved guest profile.
The native save broadcast suspends profile publication before teardown begins.
ItemSpawn (52) replay also describes saved loose products after a host restart:
one entry uses the host's scanned item ID as container ID, epoch zero and
`saved-product` as its state label. Only verified shopping-product templates qualify;
live spill manifests retain their existing identities. Replays never consume a bag.

v213 adds engine handoff semantics to the existing ItemTransform/VehicleState
ownership streams and appends `handoffTemperatureAvailable` (u8 0/1) and
`handoffTemperature` (f32 Celsius) to VehicleState 60: 33 payload bytes, 35 including
the message ID. No new IDs. The temperature must be finite within [-100, 300],
and zero when unavailable; old/truncated packets and non-boolean availability
are rejected. This preserves subzero engine temperatures without gauge quantization.
For catalogued handoff vehicles
(currently Sorbet), a seated new simulator consumes only a fresh accepted engine
state matching the previous owner and vehicle. It restores native engine activation,
RPM/integrator, selected gear and the native transient coolant temperature once,
without cranking or charging starter costs again. Running handoff requires a
validated native temperature source and an available sample. Temperature capture
uses the existing non-host-authoritative catalog source; Corris keeps its independent
host coolant authority. Receiving a packet stores the sample and does not write a
coolant producer until this local simulator claim.
The former simulator stops its native engine when yielding ownership. Observers
use VehicleState presentation; raw ignition/starter FsmStateEnter replay is retired
for these vehicles. Other vehicles retain their previous behavior. Sorbet Gear now
reports its native drivetrain index (0 reverse, 1 neutral, 2 first) instead of the
fallback neutral value. RPM is read from the active native drivetrain; stale
FuelLine/gauge scratch cannot revive a stopped engine. Expired, unrelated or
absent state does not start an engine.

v212 corrects driver ownership in ItemTransform (42), with no layout or ID change.
Only actual local driver seating establishes FlagDriver; cabin-heating proximity
does not. Exiting beside a running car clears driver occupancy while its previous
simulator may retain a FlagVehicle stream. Other players can then enter the freed
driver seat under the existing host-validated ownership rules.

v211 tightens sleep consent (26–28) without changing layouts or IDs. The host
accepts only the first answer from each still-connected guest included in the
request. A decline ends the round immediately. Host withdrawal, timeout or the
departure of every requested guest cancels the round; remaining unanimous
approvals are rechecked when another guest leaves. Late joiners cannot answer a
request they were not sent. Cancelled rounds broadcast Accepted=false and retire
pending answers. Leaving the session dismisses its guest prompt.

v210 corrects radiator-fan identity: native engine load/cooling reads use the
newly catalogued VIN137 factory (`RadiatorFan137`), not VIN127
(`WaterpumpPulley127`). Both keep distinct deterministic factory/item IDs.
ReplacementPartState (185) retains its layout and per-family scalar order;
VIN137 carries Wear and Tightness. The version bump rejects peers that still
interpret a pulley attachment as an installed fan.

v209 adds `VehicleWheelHealthState` (205), host -> all guests on reliable-ordered
channel 0, including the driver. Payload: vehicleId:uint32, revision:uint32,
availability:uint8, healthFL/healthFR/healthRL/healthRR:float32. It is 25 payload
bytes, 27 including the message ID. Vehicle ID is nonzero. Availability bits
0/1/2/3 identify FL/FR/RL/RR; bits 4–7 are invalid. Every float is finite and an
unavailable wheel must encode zero. Available zero and finite negative native
health remain distinct from missing input. Revisions use uint32 serial ordering;
stale and half-range updates are rejected, equal revisions must match contents.

The host reads the same validated `ThisTire::Data.TireHealth` mounts as the
native wheel readers. Capture is read-only, individual bad sources withdraw
independently, and host publications/revisions survive driver changes and body
replacement. Live polling, parked host changes, joins and vehicle resync all
use this result; snapshot capture does not acknowledge the live broadcast.
Guests retain copied host results and project them into the guarded native
healthy/flat health readers. Missing/withdrawn inputs pause only affected
consumers; neither a former driver's report nor guest save data can substitute.
Once-registered consumers also wait through temporary body unregistration.

The existing `VehicleCondition` (65) layout is unchanged. Its quantized health
fields no longer supply registered CORRIS native health readers; pressure,
flat/rim flags and gearbox damage category still use the condition stream.
Host capture/native input consumption does not implement guest-driving tyre
wear, fitted tyre repair/type/grip reconciliation or complete failure effects.
Both peers and the compatibility manifest require protocol 209. Mod version
remains 0.1.33; next free ID is 206.
[Validation and limits](../docs/BUILDING.md#host-wheel-health-inputs-protocol-209-unreleased).

The preceding v208 wheel-rim replay correction used the existing `VehicleCondition` (65)
rim flags and ownership/availability rules without changing message layouts or meanings.
It kept protocol 208 and next free ID 205, correcting native state entry and
wheel physics application, with repair and durable tyre wear still unfinished.
[Validation and limits](../docs/BUILDING.md#wheel-rim-presentation-protocol-208-unreleased).

v208 adds GearboxWearRequest (204), guest -> host on reliable-ordered channel 0.
The payload is vehicleId:uint32, playerId:uint8, sequence:uint16: 7 payload bytes,
9 total. Vehicle ID is nonzero; player ID is 1–254. The guest supplies no wear
amount, time or saved damage value. Existing message layouts remain unchanged.
Both peers and the compatibility manifest require protocol 208; mod version
remains 0.1.33 and next free ID is 205.

An actual guest driver owning the registered CORRIS body reports the native
GearboxDamage::Damage Reverse #6 callback once per state entry. This state is
the damaged-gearbox kick-out path for damage types 1/2/3. Its SubtractFsmFloat
subtracts literal 0.0525 from db_Gearbox.Data.Wear once, not per second. Guest
observation preserves local failure actions while suppressing saved wear.
Repeated direct callbacks cannot duplicate an entry, and non-drivers or
disconnected sessions cannot send requests. Changed native signatures pause
and recover the affected consumer.

The host requires authenticated current ownership, no local host driver,
validated drivetrain wear, an initialized native failure consumer and an
installed saved gearbox with DamageType 1–3. It checks canonical operands,
unique saved fields, global aliases and the actual native target/cache before
running only the native subtraction helper. No host gear change, sound or
failure-state entry is replayed. Host observer callbacks cannot double-charge
that wear while a guest owns the vehicle. The next message 202 publication
carries the updated saved wear to all guests; its layout remains 28 bytes.

Failure callbacks use uint16 serial ordering (delta 1–32767 including wrap) and
a separate per-vehicle budget of two initial events and two per second. This
mod budget allows delivery jitter around the audited Reverse 0.6-second Wait;
it is not a substitute for full physical failure validation. Handoffs retain
the budget and sender history; departure clears only that sender's history.
Ordered rejections consume their sequence before budget/native validation,
so repairs cannot revive them. Requests are immediate with no replay queue.
Both saved-use paths now require the exact gearbox object referenced by the
validated host drivetrain producer; equal paths and values cannot substitute
a duplicate object.
Oil-use message 203 keeps its four-event/four-per-second budget and behavior;
both paths share the callback ordering implementation.
[Validation and limits](../docs/BUILDING.md#guest-gearbox-failure-wear-protocol-208-unreleased).

v207 adds GearboxOilUseRequest (203), guest -> host on reliable-ordered channel 0.
Its 8-byte payload is vehicleId:uint32, playerId:uint8, sequence:uint16,
phase:uint8 (1 or 3); total message length is 10 bytes. Vehicle ID must be nonzero
and player ID 1–254. No amount, duration or batch count comes from the guest.
All existing message layouts are unchanged, including message 202 (28 bytes).
Both peers and the compatibility manifest require protocol 207; mod version
remains 0.1.33 and the next free message ID is 204.

The actual guest driver, while owning the registered CORRIS body, reports the
native once-per-entry OilLevel subtraction in automatic `3 speed` State 1/3.
The callback remains enabled only for that driver; its saved write is suppressed.
Repeated direct callbacks in one entry, disconnected sessions and non-drivers
cannot emit additional requests. Changed native bindings pause that consumer.
The host authenticates player ID and current vehicle ownership, rejecting use
while the host owns/drives the car. Each sender has uint16 serial ordering
(delta 1–32767, including wrap); a per-vehicle budget allows four initial events
and refills four per second across driver handoffs. This is a mod abuse budget,
not a verified native shift-frequency limit. Ordered events consume their
sequence before budget/native validation, so rejection cannot replay after repair.
Player departure clears that sender's ordering history without refilling the
vehicle budget; a reused slot can start a new connection sequence.

The host validates the mounted automatic gearbox (Installed true, Type 2), saved
wear/oil and native action operands. It runs native FloatOperator, FloatDivide
and FloatClamp with temporary operands: (15 - current host wear) / 5000, clamped
to 0.0000001–1. The native SubtractFsmFloat helper applies that amount once to
host OilLevel; original operands, local scratch and active driving state remain
intact. Competing host oil callbacks are suppressed during guest ownership;
solo/local host use remains native. The next host poll publishes the new oil
through message 202. Requests are immediate, with no delayed replay queue.
Full automatic physics, physical failures, fitted-part lifecycle and live
Steam/save-reload acceptance remain open.
[Validation and limits](../docs/BUILDING.md#guest-automatic-gearbox-oil-use-protocol-207-unreleased).

v206 appends gearboxOilAvailable:uint8 (strict 0/1) and gearboxOilLevel:float32
to VehicleDrivetrainWearState (202), after rearAxleWear. The payload is now
26 bytes /28 including ID; existing offsets remain unchanged. Oil is finite,
unavailable requires numeric zero, and available oil requires the wear source
flag to be 1. Known zero and negative native oil remain valid, distinct from
unavailable. No wire quantization or clamping is applied. Both peers require
protocol 206; mod version stays 0.1.33 and next free message ID remains 203.

The host reads OilLevel from the same validated gearbox Data used for wear.
Missing, duplicate, nonfinite, non-variable or aliased oil withdraws only oil;
an invalid wear source withdraws the whole result. Oil-only changes, including
parked refills, advance the same host revision and use the existing reliable
poll, keepalive, join and targeted-resync paths. Copies and duplicate/conflict
checks include both new fields. Guest reports cannot author this state.

The guarded automatic gearbox Set stall speed #0 reader receives exact host oil.
Native clamp (0.02–6.3), shift RPM (15120 / clamped oil), stall ratio (shift RPM
/800) and local Stallspeed writes remain native. Missing/withdrawn oil pauses
only the automatic consumer until valid input returns; the three Transmission
wear readers continue with available host wear. Saved guest oil writes remain
blocked. Physical automatic operation, host oil-loss progression while guests
drive and full fitted-part lifecycle remain incomplete.
[Validation and limits](../docs/BUILDING.md#host-gearbox-oil-input-protocol-206-unreleased).

v205 feeds the retained host result into four guarded native drivetrain wear
readers on registered guest vehicles. Transmission consumes driveshaft, gearbox
and rear-axle wear; automatic gearbox /3 speed consumes gearbox wear. Only local
reader outputs change. Saved DamageType assignment, driveshaft BREAKOFF dispatch
and both automatic OilLevel subtractions remain blocked on protected guests.
External SetFsmInt and float destination guards also protect saved gearbox Data,
including native warm caches and remembered renamed targets.

Missing/withdrawn results or invalid consumer bindings pause the affected FSM;
a valid newer host result and repaired bindings allow recovery. Missing wear
catalog metadata also pauses registered consumers. Driver handoff, local driving
ownership and parking do not select different wear. Disconnected/host/solo
readers retain native lookup; protected guest saved-write guards survive disconnect.
Both peers require protocol 205 for these new simulation semantics. Message 202
stays 21 payload bytes /23 including ID; no IDs, fields or channels change.
Mod version remains 0.1.33 and next free ID remains 203. Full physical failure,
fitted-part lifecycle and live two-player acceptance are still open.
[Validation and limits](../docs/BUILDING.md#guarded-guest-drivetrain-wear-consumers-protocol-205-unreleased).

v204 adds VehicleDrivetrainWearState (202), a reliable host-only result for the
three saved drivetrain wear values. Both peers require protocol 204. Existing
message layouts and channels remain unchanged; mod version remains 0.1.33 and
the next free message ID is 203.

The 21-byte payload (23 bytes including ID) contains, in order:
vehicleId:uint32, revision:uint32, flags:uint8, driveshaftWear:float32,
gearboxWear:float32, rearAxleWear:float32. Vehicle ID must be nonzero. Flags is
exactly 0 (unavailable) or 1 (all three available). All floats must be finite;
unavailable requires all three numeric zeros. Available zero and negative wear
are valid native results. No quantization, threshold rounding or wear-amount
clamping is applied to host results.

Only the authenticated selected host can send this message to a connected guest;
hosts reject guest-originated results. It uses reliable-ordered channel 0.
The registered catalog vehicle ID bounds acceptance, including before native
scene discovery. Each vehicle has one uint32 host revision stream, independent
of driver identity, driving leases and VehicleCondition sequences. Exact duplicate
revisions are idempotent; conflicting duplicates, older revisions and the
half-range ambiguity are rejected. Serial comparison permits uint32 wrap.

The host validates the complete native Wear graph and canonical saved targets,
then reads current host values. This capture never recovers a paused wear entry
or executes a native write. Unready, paused, nonfinite or changed bindings
withdraw the whole result. A 0.5-second poll publishes changes and a five-second
keepalive on channel 0, including parked repairs. Removed vehicles withdraw
their prior result. Repaired/recreated sources retain the same host revision
history. Join, vehicle resync and per-object snapshots include current results;
capturing a snapshot does not acknowledge the pending live broadcast.

Guests retain copied host results through ownership changes, parking and late
discovery, without changing saved parts or replaying failure events. Withdrawal
supersedes old available state; only a newer available result restores it.
Disconnect hides the retained result and session teardown clears both replica
and publication history. Native Transmission/automatic-gearbox wear consumers,
failure transitions and complete fitted-part lifecycle remain unfinished.
[Validation and limits](../docs/BUILDING.md#host-drivetrain-wear-publication-protocol-204-unreleased).

v203 uses the fresh, accepted current driver's differential speed as input to
the host's native periodic drivetrain wear graph. This changes simulation
semantics and requires protocol 203 on both peers. VehicleState (60) retains its
v202 layout: 28 payload bytes / 30 including ID. No fields, IDs or channels
change; mod version remains 0.1.33 and next free message ID remains 202.

Only a hosting session with a remote driver delegates these reads. Each native
cycle validates the complete graph and all three host saved Data.Wear targets.
The comparison and divisions temporarily consume the accepted speed's absolute
value; the native >1 threshold, three subtract actions and two-second real-time
wait remain responsible for wear. Receipt never sets native differentialSpeed
or DiffSpeed. Missing, unavailable, stale, snapshot or wrong-owner samples
contribute zero. A host validation budget rejects the entire input if any target
would lose more than one Wear point per cycle (with native divisors, abs speed
over 22800). This budget is a mod policy, not an inferred native physics limit.
Nonfinite host wear or invalid rates also reject the whole input. Rejection is
not clamping and never substitutes host observer speed for unavailable input.

Changed graph or saved-target bindings pause only this wear FSM and retry.
Local driving and session teardown restore native operation. Guest saved-write
protection remains active. Result publication, complete fitted-part lifecycle,
tyre wear and gearbox failure wear remain separate unfinished work.
[Validation and limits](../docs/BUILDING.md#host-periodic-drivetrain-wear-protocol-203-unreleased).

v202 appends `differentialSpeedAvailable:uint8` (strictly 0 or 1) and
`differentialSpeed:float32` to VehicleState (60), after movementSpeedTenthsKmh.
The message is now 28 payload bytes / 30 bytes including ID. Existing fields
keep their offsets; the new availability byte is at offset 25 and the float at
26, counting the ID. The value is the signed native Drivetrain.differentialSpeed
field, with no RPM or km/h conversion. It must be finite and numerically zero
when unavailable. Available zero represents a stopped drivetrain. Old/truncated
payloads, nonboolean availability, NaN/infinity and hidden unavailable values are
rejected before stream acceptance. Both peers require protocol 202; mod version
remains 0.1.33 and the next free message ID remains 202 (no new ID allocated).

Native capture validates the catalog's exact Wear.State 1 GetProperty source,
component, field and vehicle body, then reads the current component field at
send time. The wear FSM's two-second-old DiffSpeed scratch is not a telemetry
source. Inactive/disabled components, a paused/unstarted producer, unknown native
states or invalid bindings report unavailable and can recover. Capture never
executes the native getter or writes native scratch, component fields or saved
mounts. Live and reliable final sends share this capture.

Current-owner admission, per-sender sequence history, copied accepted state and
host snapshot rules are unchanged. Guest-owned host snapshots copy the fresh
accepted input; stale owner state cannot become a snapshot. Receiving this field
does not set native differentialSpeed. v202 added no native host wear consumer;
v203 adds the consumer described above. Publishing resulting condition remains open.
[Validation and limits](../docs/BUILDING.md#native-differential-speed-telemetry-protocol-202-unreleased).

v201 protects the three native periodic drivetrain saved-mount Wear writes on
guests: `Drivetrain::Wear`, state `Wear`, actions 1/3/5 target the driveshaft,
gearbox and rear axle. Admission and native entry guards disable those writes;
native rate calculations and the two-second wait continue. Solo/host operation
keeps the native writes. This changes guest simulation compatibility, so both
peers require protocol 201. No message layout, ID, channel or condition field
changes; mod version remains 0.1.33 and next free message ID is 202.

This does not add differential-speed telemetry or authoritative drivetrain wear
while a guest drives. The audited native input is signed `differentialSpeed`,
followed by absolute value and a >1 threshold; engine RPM or road speed must not
silently stand in for that input.
[Validation and limits](../docs/BUILDING.md#guest-periodic-drivetrain-write-protection-protocol-201-unreleased).

v200 adds host-only VehicleConditionReleaseAck (201), reliable ordered, targeted
to the releasing guest. Its payload is `releaseSequence:uint16` followed by the
15-byte VehicleCondition payload (without an embedded message ID): 17 payload
bytes / 19 total. The enclosed vehicle ID is nonzero, owner is 1–254, condition
sequence is not 65535, availability uses only bits 0–5 and puncture/rim flags
cannot conflict. The release sequence may use the full uint16 range. Known zero
and explicit unavailable fields retain their ordinary condition meaning.

The host sends confirmation only after authenticating and accepting an
established guest owner's final pose with an accepted condition. The guest must
match its pending final condition sequence, release sequence, vehicle, player
and body; only its selected host after handshake may confirm. Early confirmation
waits for actual local release. Commit requires an unowned car with no local
driver, produces copied approved parked condition and consumes the pending
record. No transform, ownership or outgoing sequence is changed by confirmation.
New claims, accepted competing motion, newer accepted condition, body replacement
and teardown invalidate pending confirmation. Sending or timing out is never
approval; retransmitted/unsolicited/stale confirmations cannot resurrect state.

Host snapshots and condition checksums use the same-body approved parked record
until a new authority update or ownership change supersedes it, keeping late joins
consistent with the confirmed result. Unapproved parked cars still use native
capture; guest checksums still detect native drift in declared fields. This does
not implement ordinary parked native repair/wear publication. No existing layout
changes; VehicleCondition stays 17 bytes, mod 0.1.33 stays unchanged, both peers
require protocol 200, and the next free message ID is 202.
[Validation and limits](../docs/BUILDING.md#host-confirmed-condition-release-protocol-200-unreleased).

v199 retains copied, previously accepted tyre-health and gearbox-damage inputs
when a connected guest claims a vehicle's physics. Capture occurs before the old
owner and incoming state are cleared; a same-body approved parked result can seed
the claim. The copy is bound to the claiming player, body and local ownership,
and supplies the protected native readers without changing saved sources/caches.
Outgoing condition uses those saved-part inputs only where native readiness and
retained availability agree. Pressure and discrete wheel transitions remain
native delegated state. Claims, snapshots and readbacks do not reset sequences;
release, accepted competing motion, body replacement and teardown retire the
copy. Rejected packets cannot replace it. Hosts, pre-claim drivers, missing
inputs and disconnected sessions retain their existing native lookup behavior.
Both peers require protocol/manifest 199 for the simulation/reporting change;
message layouts and IDs, the 17-byte VehicleCondition packet, mod 0.1.33 and next
free ID 201 are unchanged. New authoritative wear, initial claims without shared
inputs, post-release host confirmation and full flat/rim repairs remain open.
[Validation and limits](../docs/BUILDING.md#guest-condition-claim-inputs-protocol-199-unreleased).

v198 makes the catalogued GearboxDamage `Damage type#0` native GetFsmInt read
consume accepted VehicleCondition drivetrain damage for registered guest
observers. The current owner, availability bit and approved parked-release rules
still apply; known zero remains valid. Source/cache and saved gearbox state stay
untouched. The reader is admitted only with the native Reverse saved-wear guard;
changed reader identity, signature, scheduling or aliased outputs pause its
consumer and recover after repair. Hosts, local/pre-claim drivers, missing or
withdrawn data, changed ownership and disconnected sessions retain native lookup.
This changes native simulation semantics, requiring protocol/manifest 198 on both
peers. Layouts/IDs, the 17-byte VehicleCondition packet, mod 0.1.33 and next free
ID 201 are unchanged. Authoritative wear and driver bootstrap remain unfinished.
[Validation and limits](../docs/BUILDING.md#observer-gearbox-condition-input-protocol-198-unreleased).

v197 appends `availability:uint8` to VehicleCondition (65): pressure=1,
drivetrain=2, FL=4, FR=8, RL=16, RR=32; all=63, bits 6–7 reserved and rejected.
A wheel bit covers its health and puncture/rim flags together. Zero means no
fields are available, and is a valid withdrawal; a declared field may have a
known value of zero. Undeclared payload bytes are ignored by application,
observer readers and vehicle checksums. The mask itself participates in the
checksum. A guest intersects native readiness with the eligible accepted/parked
mask, so withdrawn fields do not cause repeated resync requests; declared native
drift and missing local targets remain detectable. Payload is now **15 bytes / 17-byte packet**, with existing offsets
unchanged. Copies, snapshots and approved parked records preserve the mask;
owner authorization and sequence history remain unchanged across availability
changes. Both peers require protocol 197.
Native capture marks only live canonical inputs; wheel startup/branch states
remain unavailable. Discovery retries every five seconds, and binding/readiness
changes can replay an eligible accepted result without another packet or new
stream sequence. Missing targets never turn into known damage. Mod 0.1.33,
message IDs and next free ID 201 are unchanged.
[Validation and limits](../docs/BUILDING.md#condition-availability-and-late-discovery-protocol-197-unreleased).

v196 applies accepted observer tyre pressure through the catalogued native TIRES
state after validating all eight pressure/optimum property actions and four wheel
identities. The catalog records vanilla's enabled FL pair and disabled other six
writes; shared application temporarily enables the audited one-shot writes, then
restores native enablement and removes originally disabled actions from active
scheduling in finally. It never dispatches ENABLEPRESSURE or SETWHEELS. Eligible
accepted or approved parked state retries when native targets become ready and
repairs drift; local/pre-claim drivers remain excluded. Protocol/manifest are 196; layouts, IDs,
mod 0.1.33 and next free ID 201 remain unchanged. Driver bootstrap, tyre wear and
flat/rim/repair reconciliation remain unfinished.
[Validation and limits](../docs/BUILDING.md#native-wheel-pressure-application-protocol-196-unreleased).

v195 retains accepted tyre health for guest observers after the established
owner's reliable final vehicle pose releases the car. Final poses omit the live
FlagVehicle bit; the registered vehicle and matching accepted condition identify
the car. This is a copied local
presentation record, bound to the same live body; it grants no ownership and
never accepts reports from an unowned car. Host corrections and new claims
retire it; former-driver departure/readmission does not erase an already-approved
parked result. Session clear removes it. New local leases send a fresh condition
baseline without resetting sequence history. Layouts/IDs remain unchanged;
both peers require protocol 195. Parked host publication and durable host wear
remain unfinished.
[Validation and limits](../docs/BUILDING.md#parked-observer-tyre-health-protocol-195-unreleased).

v194 projects accepted VehicleCondition wheel health into the eight audited
native guest observer Health readers. The current registered vehicle and current
condition owner must match; local/pre-claim drivers, missing state, ownership
changes and disconnected sessions retain native lookup. PUNCTURE entry can read
the validated application before its accepted copy is committed. Shared health
never writes native saved TireHealth or the reader source/cache. No layout or ID
changes; both peers require protocol 194. Driver input bootstrapping, physical
flat/rim reconciliation, authoritative wear and parked publication remain open.
[Validation and limits](../docs/BUILDING.md#observer-tyre-health-inputs-protocol-194-unreleased).

v193 extends guest saved-part protection to the four native Corris wheel
Condition graphs and GearboxDamage.Damage. Guest admission retires ongoing
TireHealth wear, flat-entry TireHealth=0 and reverse-gear Wear subtraction;
subsequent state entries retain that protection. Solo/host native writers still
run. This changes guest simulation behavior, so both peers require protocol 193.
Message layouts, IDs and the v192 VehicleCondition stream rules are unchanged;
mod remains 0.1.33 and next free message ID remains 201. Native condition inputs,
flat/rim physics, parked publication and host wear under guest driving remain
unfinished. [Validation and limits](../docs/BUILDING.md#guest-tyre-and-gearbox-write-protection-protocol-193-unreleased).

v192 fixes VehicleCondition (65) ownership and ordering. Its fields and **16-byte
packet** (14-byte payload) stay unchanged; the existing sequence reserves 65535
for host join/resync snapshots. Live counters skip that sentinel. Both peers
require matching protocol versions; mod remains 0.1.33 and next free message ID remains 201.

The host accepts condition only from the authenticated current vehicle owner,
with a registered live body and no local driver. Unowned-car guest reports,
former owners, forged identities, guest snapshots and duplicate/stale sequences
are rejected before native writes or relay. Sequence zero is a valid first live
value, then deduplicated normally. Each vehicle retains separate sender histories
through ownership changes; readmission clears that sender across vehicles and
session teardown clears all. A wheel cannot carry both its puncture and rim bits.
Guests accept only the selected host's relay for the current owner, with local
and already-parented pre-claim drivers protected from remote writes.

Accepted reports are copied. While a guest owns a car, a host snapshot copies its
accepted report rather than sampling host scratch variables; missing or former-
owner reports cannot supply it. Snapshot source is host 0 and sequence 65535,
without consuming live publication counters or delta baselines. A guest accepts
that snapshot only while the car is unowned or host-owned; it cannot override an
active guest driver's stream or advance live dedup history. The current owner
sends final condition reliably before the final ItemTransform releases ownership,
even when its condition has not changed or its ordinary timer is not due.

Ordinary condition publication still requires local ownership. Parked host
publication and native durable wheel/gearbox inputs, safe flat/rim effects and
guest-original protection remain roadmap 2.2 work. The message's scalar conversion
and existing native apply path are unchanged by this ownership/order fix.
[Validation and limits](../docs/BUILDING.md#condition-stream-ownership-protocol-192-unreleased).

v191 makes death and respawn terminal passenger-seat transitions. Layouts and IDs
are unchanged: PassengerState (23), PlayerDeathReport (29), PlayerDeathEvent (30)
and PlayerRespawn (31) retain their existing reliable-ordered delivery. Both peers
require protocol 191; next free ID remains 201.

The existing authenticated death report causes the host to retire the player's
canonical seat before broadcasting PlayerDeathEvent. The event retires that
player's observer seat/anchor too; permadeath retires every seat and marks all
connected remote players dead. Respawn retires any residual occupancy before
applying the returned pose and requires a fresh seat claim. Retirement preserves
seat request history for the connection. Replayed old claims remain rejected;
new claims/keepalives from dead players are rejected and consume their sequence,
including previously accepted seats. Observers also consume but ignore newer
seat reports while that player is dead. A real disconnect/session clear resets
history as before; an empty seat map must not prevent clearing retained history.

Local native death/group-death and respawn notifications release passenger
parenting immediately. The controller restores only its own captured parent and
previous controller availability; it does not recreate components destroyed by
the native death flow or move a player that native respawn already reparented.
Dead players cannot enter, and LateUpdate checks death/session status before
pinning. The normal exit and matching host correction share the same idempotent
release. Full native death effects, save/loading, physical seat/door interactions
and two-player winter-survival acceptance remain open.
[Validation and limits](../docs/BUILDING.md#passenger-death-and-respawn-protocol-191-unreleased).

v190 assigns VehicleClimate (61) flag bit 3 (8) to **available cabin temperature**.
Its layout remains **23 bytes**. A finite, validated native Data.InteriorTemp source
sets the flag and uses the existing -40..40 °C quantization. Missing/nonfinite
sources clear it and emit neutral byte 128. Without the flag, receivers ignore
CabinTemp for both presentation and passenger heating; arbitrary unavailable bytes
remain valid on the wire. Other climate fields still apply. Availability survives
live/final sends, accepted caches, host relays and snapshots under existing owner,
authentication, sequence and hold rules. Both peers require protocol 190; no new
IDs are allocated and next free remains 201.

The optional catalog vehicleClimate.passengerHeating profile binds the local
BodyTemp::Calculations Get temp and Source native GetFsmFloat readers. After the
ordinary native read, an eligible local seated passenger uses that vehicle's
current shared cabin temperature as the local Temperature calculation input.
The host requires its accepted seat ledger; guests retain existing optimistic
seating until host rejection/ejection. The local player must be alive, enabled,
in an active session and physically parented under that registered vehicle.
Current producers use the validated native cabin sample in shared quantized units;
other passengers use a current-owner accepted available sample with an unexpired
three-second hold. Exit, death, disconnect, handoff, expiry or unavailable source
falls back to the normal native read on the next calculation.

Native source objects, search radii, target references, reader caches, PlayerIn,
ownership and global PlayerTemp remain untouched by this binding. Native body
arithmetic, clothing, sweat and timing remain in control. Incompatible actions
detach only passenger heating and emit throttled diagnostics; scene/session
cleanup removes both bindings. Full physical seat/door, loading/respawn and live
two-player winter-survival acceptance remain open.
[Validation and limits](../docs/BUILDING.md#passenger-cabin-heating-protocol-190-unreleased).

v189 corrects the persisted body warmth source. BodyTemp now means native global
`PlayerTemp`, which the local body calculation updates. The former source,
`PLAYER/BodyTemp::Calculations.Temperature`, is environmental air/heat-source
temperature and must never be restored as body warmth. PlayerTemp is the game's
warmth quantity, not a Celsius reading. Individual body simulation remains local.

PlayerNeedsReport (25) appends `hasBodyTemp:uint8` after HasAlco: **44 bytes
including ID**, retaining the previous 43-byte prefix. Availability must be 0 or
1; known warmth must be finite, including zero and negative values; unknown
warmth must be zero. Invalid extensions reject the report. GuestSpawn (24) stays
**95 bytes**, assigning flag bit 4 (16) to known saved body warmth in the existing
BodyTemp float. This flag requires saved-needs bit 1 (2); absent warmth must be
zero. Both peers require protocol 189; no new IDs, next free remains 201.

Host-authenticated, wrap-aware needs reports carry the new availability to the
profile and reconnect offer. Invalid/stale/forged reports do not advance needs
history. The existing last-position choice restores known body warmth, including
zero, to PlayerTemp only. A missing global defers restoration; reports retain the
pending host value until binding succeeds once. A newer valid unknown snapshot
or a session reset cancels old deferred warmth. Missing/nonfinite local sources
without a pending host value report unavailable zero without blocking other needs.

The historical CSV file named `wintermp-guests.json` now writes 18-column needs
rows. Column 12 (zero-based) is retired air temperature and writes zero. Column
17 is native PlayerTemp, or empty when unknown. Legacy 8–17-column rows never
convert the old air sample, even if it resembles valid warmth. Invalid new warmth
is treated as unknown; other valid needs and poses survive. Dirtiness and BAC
remain independent optional columns 15/16, using empty cells when absent, so a
late dirtiness binding cannot discard known BAC or warmth. Pose-only rows remain
8 columns. The pure profile codec is shared with CI tests; file I/O remains
host-only behind the existing guest-save guard.

This fixes warmth persistence and native source selection, not passenger heat
delivery or live thermal convergence. PlayerTransform and VehicleClimate layouts
remain as in v188/v184 respectively.
[Validation and limits](../docs/BUILDING.md#native-body-warmth-persistence-protocol-189-unreleased).

v188 appends `hasSweat:uint8` and `sweat:float32` after MoveState in PlayerTransform
(22): its original 34-byte prefix is unchanged and its total is now **39 bytes
including the message ID**. Both peers require protocol 188; no new IDs are
allocated and next free ID remains 201. Availability must be 0 or 1. Available
sweat must be finite and within 0–100, matching the audited native PlayerSweat
clamp; unavailable sweat must be zero. Invalid extensions reject the whole pose.

The normal player-pose stream supplies local PlayerSweat or explicit unavailable
zero. Host authentication, guest selected-host filtering, known-player lookup and
wrap-aware pose deduplication also gate sweat. Only an accepted known player's
pose refreshes sweat/time and may be relayed. Missing globals never retain an old
wetness value, and no sweat is persisted in a guest profile or written to another
player's global variables.

The current climate producer combines local entry/driver/passenger presence with
remote passengers seated in that same vehicle: the authoritative host ledger on
the host, or accepted remote seat state plus the connected roster on a guest.
Local guest seating retains the existing optimistic entry behavior. Remote
contributors must be alive and have an accepted pose received less than five
seconds ago; missing/future times, exited/rejected seats and departed players do
not contribute. An unavailable sweat value on a fresh seated player contributes
only the native dry minimum. Dead local players do not contribute either.

Each occupant contributes `clamp(sweat, 6, 30)` effective sweat units; the sum is
capped at 30. Scoped operands feed the native GlassFrosting Player in? BoolTest and
FloatOperator on the current producer only. Native division by 300, the .02–.1
clamp, rate copies and per-second accumulation remain in control: one dry occupant
gives .02, two dry occupants .04, and the cabin never exceeds .1. An empty cabin
uses native DefaultRate. This bounded multiplayer aggregation is mod policy;
vanilla supplies the individual rate limits, not a multiplayer formula.

The optional catalog profile and complete six-action signature must match before
binding; incompatible native actions disable only this input binding. Operand
references restore after nested/exceptional calls, and session/scene teardown
clears bindings even after an item leaves the current index. Native PlayerIn,
global PlayerSweat, driver ownership and saved values are never replaced.
Observers/delegated hosts keep native inputs; the established owner publishes the
result through unchanged 23-byte VehicleClimate (61), with v185 delivery and
v187 alpha-only presentation. Passenger body warmth and live two-player thermal,
seat/door and rendered acceptance remain open.
[Validation and limits](../docs/BUILDING.md#passenger-condensation-inputs-protocol-188-unreleased).

v187 corrects native condensation presentation without changing VehicleClimate
(61)'s 23-byte layout. Both peers require protocol 187; next free ID remains 201.
Frost is the accumulated GlassFrosting.Frost amount: receivers apply it to the
native float, Color.a and FrostGlass material _Color.a. RGB is tint, not fog;
interior material _Cutoff and all exterior pane cutoffs remain independent.

The old Fog byte is retired in place: local capture emits 0 and receivers ignore
its value. The codec/cache may retain arbitrary reserved bytes for framing/copy
compatibility, but none affect presentation or native calculations. There is no
separate native fog-opacity source beyond Frost. SweatRate is a growth-rate
scratch value; GlassFrosting.Temp is divided by 100 in Warm car and then used as
a per-second defrosting rate. Neither receives climate writes. CabinTemp samples
and applies only Data.InteriorTemp (degrees); missing source data uses the existing
neutral 0 °C encoding, never the already divided GlassFrosting.Temp.

Immediate and held presentation use the same alpha-only write and preserve tint,
shader cutoff and native scratch. Missing materials do not block the float/color,
cabin or available exterior panes. Ownership, stale rejection, snapshots and
hold expiry still follow v185; local entry remains isolated by v186. This does
not yet synchronize remote PlayerSweat in v187; v188 above adds condensation
inputs, while passenger warmth remains open.
[Validation and limits](../docs/BUILDING.md#native-condensation-presentation-protocol-187-unreleased).

v186 separates shared cabin occupancy from native local entry. VehicleClimate (61)
keeps its 23-byte v184 layout; both peers require protocol 186, with no new IDs and
next free ID 201. FlagPlayerIn (bit 2) reports occupancy for the source vehicle:
native local PlayerIn, the local passenger, host-accepted passenger ledger entries,
or (on a guest) accepted remote seats whose players remain in the connected roster.
Driver parenting remains a fallback. Passenger occupancy does not grant publication
or vehicle ownership; the v185 owner contract below still applies.

Receivers retain the bit in RemotePlayerIn and accepted snapshots, but neither
immediate application nor LateUpdate writes GlassFrosting.PlayerIn. That native
bool belongs to local PlayerTrigger entry/reset and is also used by the local
driver fallback. Received occupied/empty cabins therefore cannot create/clear a
local entry, and an expired hold cannot promote an observer. New host sampling
after release does not recycle the prior owner's occupancy bit. Guest seat
readmission clears the former seat/anchor and sequence; roster departure stops
contributing before periodic anchor cleanup. Host occupancy uses its authoritative
ledger rather than the guest presentation map.

This reports seats and isolates local entry; it does not add passenger sweat or
heat inputs to native condensation/temperature calculations. Those and complete
seat/door/live multiplayer acceptance remain open.
[Validation and limits](../docs/BUILDING.md#vehicle-climate-occupancy-isolation-protocol-186-unreleased).

v185 changes VehicleClimate (61) ownership and delivery semantics without changing
its 23-byte v184 layout. Both peers require protocol 185; no new IDs are allocated,
and next free remains 201. Climate now follows the host-established vehicle owner:
only the local owner publishes while delegated; the host publishes unowned parked
cars. Native ignition activity, passenger seating and proximity cannot grant a
guest climate authority. Existing authenticated item/vehicle ownership remains
the arbitration source, and existing control intents remain separate.

The host accepts live/final climate only from the authenticated current guest
owner, never a locally driven/owned car, guest snapshot sentinel or another nearby
player. Guests accept only the selected host transport and the current vehicle
source (host ID 0 when unowned). Each vehicle/sender retains its own live sequence
history across driver changes. First sequence zero is valid, duplicates/stale and
ambiguous half-range jumps are rejected, and unauthorized/invalid reports do not
advance history. Live counters skip 65535, reserved for host snapshots. Core also
rejects zero vehicle IDs, owner 255, unknown climate flags and invalid v184 ice.
Rejected reports neither refresh presentation nor relay to other guests.

Before releasing vehicle ownership, the sender sends final climate on reliable
channel 0 after final engine state and before the final item pose. This bypasses
the climate rate timer, uses the next ordinary sequence, and the host preserves
the incoming reliable channel when relaying. Periodic climate remains channel 1
at 2 Hz. The existing three-second hold stops immediately when its accepted source
no longer matches current ownership or the local player starts driving.

Host join/repair snapshots of guest-owned cars copy the latest accepted current
owner report, including all six windows and controls; they do not sample a divergent
host simulator or consume live sequence. Missing, expired or former-owner reports
produce no climate snapshot. Host snapshots retain owner ID 0/sentinel 65535;
guests accept them only for unowned/host-owned vehicles, without changing live
history. An already observed guest owner continues its live stream. Player
readmission clears that sender's histories and held report; session teardown
clears all climate histories, copies, hold timers and outgoing counters. Direct
disconnected delivery is rejected. [Validation and limits](../docs/BUILDING.md#vehicle-climate-ownership-protocol-185-unreleased).

v184 extends VehicleClimate (61) from 17 to 23 bytes including ID. The original
Ice byte remains the windshield cutoff. Append, in order: IceSideLeft:uint8,
IceSideRight:uint8, IceDoorLeft:uint8, IceDoorRight:uint8, IceRear:uint8,
IceMask:uint8. Mask bits 0–5 indicate available windshield, side-left, side-right,
door-left, door-right and rear readings respectively. Bits 6–7 are rejected;
unavailable panes must carry byte zero. Both serialization boundaries and direct
Core receipt reject invalid ice state before applying it or advancing dedup.
No new ID is allocated; next free remains 201. Both peers require protocol 184.

Each byte maps native Freezing.Cutoff* from 0 to 1: **0 is iced, 255 is clear**.
This corrects the old prose's reversed exterior interpretation; the original
windshield encoding itself is unchanged. Finite source values outside the visual
range clamp without rewriting the source. Missing or nonfinite sources clear
only their own availability bit. Available zero means iced, not unavailable.
Receivers copy each pane separately and pin only available panes during the
existing 3-second presentation hold. No pane borrows the windshield. Interior
frost/fog, cabin temperature, controls, stream authorization, delegated ownership,
snapshot sentinel and channel rules otherwise retain their existing behavior.
Live, join and targeted repair builders all capture the same six native values.
The shipped vehicleClimate catalog now includes the native taxi paths, matching
Corris and Sorbet. Full climate authority and live two-player acceptance remain
open; this change does not synchronize new scraping intents or suppress native
climate graphs. [Validation and limits](../docs/BUILDING.md#independent-window-ice-protocol-184-unreleased).

v183 appends RearWindowFlags:uint8 to host-only HeaterState (200), after Wear.
The message is now 12 bytes including ID: revision:uint32, flags:uint8,
wear:float32, rearWindowFlags:uint8. Rear flags independently accept 0 unavailable,
1 available without an element, or 3 available with an element. They do not depend
on blower installation, wear or the rear circuit. Main flags/wear validation stays
unchanged. Both peers require protocol 183; no new IDs are allocated and next free
ID remains 201.

The host reads CORRIS/BODY::Save.HeatingSprites after the native body reaches
State 1, State 4 or Save. This is the body's VIN-derived WindowHeater option;
the native M and '-' comparisons disable it and other values enable it. A source
must be active, uniquely named on its body, initialized and started. Loading,
transitional, missing, disabled or ambiguous sources publish rear flags 0 without
clearing blower state. Conversely a missing blower does not clear a ready body
option. Rear-only changes advance the shared revision, survive snapshot copies,
and use existing reliable keepalive, join/vehicle resync and teardown handling.

Required guestEngineInputs.heater.rearWindow metadata pins the native source,
variable and three accepted states. Connected protected guests' GetFsmBool reads
of HeatingSprites on that actual source consume the host rear flags into safe
consumer-local outputs. Unseeded/unavailable/absent host elements read false;
host/solo and unrelated reads remain native. Native source cache/fallback and
entry-only scheduling are preserved. Saved/global output aliases are rejected,
known identities survive movement/metadata loss until destruction, and neither
the body's Save FSM nor its option/visuals are overwritten or paused.

Rear defrosting #2 now uses that host option. The original circuit gate, element
gate, GlassDefrosting switch, consumption addition and CutoffRear heat addition
remain native. Full climate authority, physical glass-strip presentation and live
two-player acceptance remain separate work.

v182 supplies native heater inlet/outlet Installed reads from the host's existing
EngineBlockState (195) CoolantHoseFlags bits 2/3. Hose indices remain top=0,
bottom=1, inlet=2, outlet=3. No fields or message IDs change; both peers require
protocol 182, and next free ID remains 201. The host's settled hose capture,
independent revisions, reliable keepalive and join/vehicle resync are unchanged.

The two heater hose catalog records require projectNativeReads=true; radiator
hoses retain their existing indexed consumers. On connected protected guests,
GetFsmBool Installed reads of either actual heater hose Data source write only
safe consumer-local outputs. Absent/unseeded host hoses read false, independently
of radiator/block installation and clamp tightness. Host/solo and other sources
remain native; target caching, missing-name fallback and callback cadence stay
intact. Saved/global output aliases are rejected, known source identities survive
movement/metadata loss until destruction, and session reset clears host values.

HeaterUnit::Function Heater pipes? #2/#3 now use these hose inputs. Its original
#0 read of Cooling.WaterLevel, #1 comparison and #4 BoolAllTrue remain native.
Cooling already reads host radiator Coolant via the existing state-195 proxy,
clamps WaterLevel to 0–25, and sets it to zero without a radiator. The heater
continues only with WaterLevel >= 0.5 and both hoses installed. No packet directly
writes the guest's saved hose/radiator Data or the heater's calculation scratch.
Physical hose fitting, rear-window HeatingSprites, full climate and live two-player
acceptance remain separate work.

v181 extends host WiringState (193) with three independent Corris circuit sources:
9 WiringHeatercontrolFusebox, 10 WiringHeater, and 11 WiringFuseboxWindow, all under
CORRIS/Wiring/DatabaseWiring with Data FSMs. These unbolted circuits accept only
flags 0/1/3 and publish after native Basic state; missing, ambiguous, disabled or
loading sources remain unavailable. Existing framing and layouts are unchanged
(193 is 11 bytes including ID); both peers require protocol 181. No new message
IDs are allocated; next free ID remains 201.

Join/vehicle resync, change publication and five-second keepalive now include all
11 wiring sources. Each retains independent revisions, copy isolation and session
cleanup. The three new catalog entries require projectNativeReads=true; sources
1–8 keep their existing proxy bindings. Connected protected guests' native
GetFsmBool Installed helpers read the host circuit into a safe consumer-local
output; unavailable or unseeded circuits supply false. Native caching, fallback
and entry/update cadence are preserved. Saved source/global output aliases are
rejected; known source identities survive movement and metadata loss until
destruction. Host/solo and unrelated reads remain native.

HeaterUnit::Function Electrics? #2/#3 consume sources 9/10. Its native BoolAllTrue
still requires both circuits and host heater installation (state 200). Rear
defrosting #0 consumes source 11, leaving the native wire gate, heating-element
and control checks, and consumption arithmetic in place. This changes input
projection only: physical cables, hose/element inputs, climate authority and live
two-player acceptance remain separate work.

v180 adds host-only HeaterState (200), reliable-ordered channel 0: revision:uint32,
flags:uint8, wear:float32 (11 bytes including ID). Available=1, Installed=2; only
flags 0, 1 and 3 are valid. Wear must be finite and zero unless installed. Negative
installed wear is retained for the native broken-blower threshold. Both peers
require protocol 180; next free ID is 201. Existing message layouts are unchanged.

The host publishes the native Corris heater mount's Installed/Wear independently
of the physics owner. Capture requires the catalogued live started Data FSM, Idle
for settled absence or Update 2 for an installed part, with active unique Data on
ActivePart, direct parenting to the mount and AssemblyID=1. Transitional, disabled,
missing, ambiguous or invalid sources publish unavailable/zero. Installed wear
comes from the mount; its physical-part copy may lag one native update. Changed
state is polled at 0.2 seconds, sent reliably with a five-second keepalive, and
included in join/vehicle resync. Snapshot capture never consumes pending live
publication. Equal revisions must agree exactly; forward uint deltas 1–2^31−1
include wrap. Replicas copy values, reject guest-authored state and clear on reset.

Guest saved heater Data is paused with its active native work suppressed, and
external Set/Add/SubtractFsmFloat writes to that destination are blocked. The
catalog requires heater metadata, Installed/Wear read fields and complete paused
mount/external-write protection before engine-input admission. Native scalar-read
helpers project only connected protected guests' reads of the actual heater Data
source into consumer-local output variables. Unseeded, absent and unavailable host
state gives false/zero. Native same-object caching and missing-name fallback are
preserved; unrelated sources and host/solo reads stay native. Saved/global output
aliases are rejected. Known source identities remain protected through movement,
metadata loss and disconnect until destroyed. Existing heater control/climate
messages, native blower arithmetic and its Wear<3 broken decision are unchanged.
Heater wiring/hose prerequisites and full physical heater replication remain open.

v179 adds guest-to-host StarterWearRequest (199), reliable-ordered channel 0:
vehicleId:uint32, playerId:uint8, sequence:uint16, seconds:float32. It is 13 bytes
including ID. Vehicle must be nonzero, player 1–254, and duration finite in (0, 1].
Both peers require protocol 179; next free ID is 200. Existing message layouts,
including StarterDrawRequest (198), are unchanged.

The catalogued Fuel Mixture #11 SubtractFsmFloat remains enabled as a protected
guest observer. It skips saved wear writes and reports Time.deltaTime for each
native entry/update; its original everyFrame=true, perSecond=true cadence stays
intact. Only a connected current guest owner reports duration. Batches flush after
0.1 seconds, before exceeding one second, and before final vehicle state/ownership
release. Invalid or zero frame durations are ignored. Other crank states do not
report wear. No guest wear, durability, or rate is sent.

The host authenticates actor and current vehicle owner, rejects a local driver,
and admits forward ushort sequence deltas 1–32767 (including wrap) independently
per vehicle/player. A one-second burst budget replenishes by one second per host
unscaled second. Excess sequence numbers are consumed, never deferred. Native
validation also consumes admitted sequence numbers when it rejects work, so repair
cannot replay it. Checks require an installed settled battery, live native Starter,
installed starter/flywheel, starter/harness/ground wiring, the original local wear
operand and per-second cadence, and no simultaneous host crank. The native rate
must remain a finite nonnegative literal multiplied by local StarterDurability;
its reader must still source db_Starter::Data.Durability. Host current mounted
Durability and the host rate determine wear, rather than cached scratch values.
The native subtract helper applies the integrated amount once with scoped temporary
operands and restores its original fields. Native mounted Update 2 publishes Wear
to ActivePart.Data for normal ReplacementPartState (185) publication.

Requests are never relayed or snapshotted. Ownership loss/session clear drops
pending time; sequence history survives ownership changes until player readmission
or session reset. Batching can round floating point wear differently from individual
frames. Pauses, extreme frame intervals, sustained reports above the time budget,
physical handoff and live two-player starting require acceptance testing.

v178 adds guest-to-host StarterDrawRequest (198), reliable-ordered channel 0:
vehicleId:uint32, playerId:uint8, sequence:uint16, kind:uint8 (1 loaded, 2 unloaded),
count:uint16 (1–512). It is 12 bytes including ID. Vehicle must be nonzero and player
must be 1–254. Both peers require protocol 178; next free ID is 199. Existing state
layouts are unchanged, including BatteryState's 15 bytes and VehicleState's 25.

Five catalogued native Starter AddFsmFloat callbacks on a registered guest Corris
count cranking operations while their original saved-battery write stays blocked.
Only the current local owner in a connected protected guest reports work. Batches
flush after 0.1 seconds, on kind change/full count, and before final vehicle state
and ownership release. Other selected writes remain disabled. The native cadence
is OnEnter plus every-frame OnUpdate, perSecond=false; loaded/unloaded counts stay
separate. Guests do not send charge values or drain rates.

The host authenticates playerId and current vehicle ownership, rejects local-driver
conflicts, and deduplicates forward ushort sequences (delta 1–32767, wrap allowed)
per vehicle/player. A 512-count burst budget replenishes at 2,048 counts/second;
excess sequences are consumed without deferred work. Native validation requires a
settled installed host battery, the native starter and its wiring, matching host
flywheel installation, unchanged native action/operand/cadence, and no host crank
already active. Validation failures consume accepted sequence numbers, so a later
repair cannot replay rejected drain. The host invokes only the validated native
charge-add helper count times using its own loaded/unloaded rate. Host BatteryState
continues to publish results. Guest requests are never relayed or used in snapshots.
Session clear removes pending work; sequence history survives ownership changes
until player readmission/session reset. Other accessory loads and full live
electrical/physical handoff acceptance remain separate work.

v177 appends finite native `chargeMax:float32` after `charge` in host-only
BatteryState (194), now 15 bytes including its ID. Both values must be zero unless
Installed is set. The host captures the settled battery mount's maximum alongside
charge, never a stale part value. Maximum-only changes advance the same revision;
equal-revision conflicts are rejected and snapshots do not consume pending updates.
Both peers require protocol 177; channels, flags and next free ID 198 are unchanged.

The catalog requires `guestEngineInputs.battery.readVariables` to contain exactly
Installed, Charge and ChargeMax. Connected protected guests project those host
inputs into native GetFsmBool/GetFsmFloat consumer-local outputs when their resolved
source is the protected battery Data. This includes accessory, fan, wiring and
current/power calculations, without mutating the saved source or globals. Native
source caching and first-FSM fallback are preserved; other sources/scalars remain
native. Unseeded/unavailable/removed host batteries supply false/zero. Session clear
removes prior host inputs; host, solo and disconnected reads retain native behavior
while saved-battery write protection stays latched. This does not report guest
accessory loads or starter draw to the host; live electrical acceptance remains open.

v176 extends guest battery preservation to external native float writers. The
catalog's paused battery Data target requires `blockExternalFloatWrites: true`
before battery input projection or guest admission. Native SetFsmFloat,
AddFsmFloat and SubtractFsmFloat callbacks resolve their actual destination,
including native same-object FSM caching and first-FSM fallback. Writes to that
protected mount are skipped while consumer calculations continue. Remembered
target identities remain protected through movement, metadata loss and disconnect;
new targets are recognized at the write boundary before periodic discovery.
Unprotected sessions and other destinations retain native behavior.

No message layout, field, flag, channel or ID changes. BatteryState (194) remains
host-only; this does not report guest accessory loads or starter draw to the host.
Both peers require protocol 176; next free ID stays 198. Native validation covers
31 audited external battery writes, including 21 outside the earlier selected
writer list. Live electrical operation and two-player acceptance remain open.

v175 extends accepted guest-driver VehicleState (60) RPM to all three native
Corris Electrics readers: Engine running? #0 retains its strictly-greater-than-400
charging gate; Charge battery #1 retains RPM / AlternatorEfficiency; Run on battery
#0 retains RPM /60000 and the native 0.00001–1 discharge clamp. Message 60 stays
25 bytes including ID. No fields, flags, channels or IDs change; both peers require
protocol 175. Native host installation/wiring/damage checks, alternator condition,
engine temperature, voltage checks, battery charge/ChargeMax and alternator wear
writers remain authoritative.

The catalog's independent `vehicleElectrical` profile identifies the complete
three-reader group. Hosts bind on vehicle updates before the first driver report,
and on accepted state receipt. Scoped native reads use only the copied, valid,
current owner's fresh, non-snapshot sample; missing, stale, wrong-owner/vehicle,
snapshot or invalid input supplies zero RPM through native calculations. Optional
torque/movement absence does not invalidate RPM. This preserves the native minimum
battery drain at zero RPM. Local drivers, unowned cars, guests, protected saves and
inactive sessions retain original native inputs. Source operands restore after
nested reads or exceptions. No global RPM assignment or extra native tick is added.

Reader identity, unique FSMs/states, action arrays, global/local-shadow identity,
cadence, threshold/events/transitions and charge/drain arithmetic are validated.
A changed graph retires the group and retries independently of thermal/cooling/wear
bindings. Driver departure clears the accepted sample and cleanup removes readers.
The native Delay state applies the calculated rates on its normal cadence; it can
retain the preceding rate until the next native calculation. This change does not
synchronize every electrical load, starter draw, physical part failure or thermal
handoff. Full electrical operation and live two-player acceptance remain open.

v174 supplies host engine heat to three native Corris electrical inputs on
connected guests. State 197 retains its 19-byte layout, revisions, availability,
channels and IDs. Electrics/Battery #1 and
InteriorLight/Electrics/Consumption/Battery #1 use host EngineTemp as their
SetFloatValue source, preserving the native -20 to -0.1°C clamp and addition to
local Charge before the voltage-limit decision. Electrics/Charge battery #2
uses host EngineTemp before the native +51, /570, 0.0001–0.55 charging limit.
The guest's RPM and existing battery/alternator input authority remain unchanged.

The catalog requires a complete `electricalInputs` group. Its three readers share
scoped engine-temperature hooks but bind, retire, log and recover separately from
the six fuel/oil readers and cabin/heater group. Action/source identities, local
scratch variables, cold-penalty arithmetic, voltage events/transitions and charging
limit arithmetic are validated. Both SetFloatValue OnEnter and OnUpdate boundaries
restore original operands after nested dispatch or exceptions. Projection never
writes native EngineTemp or saved battery charge, and never adds a simulation tick.
Unseeded/unavailable host heat supplies zero degrees through native calculations;
driver ownership and telemetry expiry do not reset heat. Host/disconnected reads
stay native. Protocol 175 adds host charging/drain from guest-driver RPM. Remaining
thermal consumers, complete electrical operation and live two-player acceptance
remain open.

v173 extends the use of host state 197 to Corris cabin and heater inputs. The
19-byte layout, revisions, availability, channels and IDs are unchanged. On
connected guests, CarTempCorris/Data/Data #4
consumes host engine degrees before the native divide-by-five cabin temperature
limit. HeaterUnit/Function/Calc defrosting #0 substitutes host coolant degrees
at its GetFsmFloat result, before native blower/direction/temperature calculations
and subsequent coolant scaling. The two readers validate together against the
catalog's required `cabinInputs` paths, FSMs and states. They bind independently
of the six fuel/oil readers and dashboard presentation.

Unseeded/unavailable host state supplies zero degrees through native arithmetic.
Driving, passenger ownership and engine telemetry expiry do not replace the host
thermal source. Host/disconnected sessions keep native inputs. The cabin engine
operand restores after nested reads or exceptions; the heater writes only its
local scratch read result. Neither read writes EngineTemp, Cooling.CoolantTemp or
saved parts, advances a native tick, or bypasses the existing save guard. Existing
climate reports and control synchronization retain their behavior; this does not
claim complete cabin/frost authority or full thermal handoff.

v172 appends `engineCelsius:float32` to VehicleCoolantState (197), after coolant
Celsius. The first 15 bytes retain their layout; the complete message is now
**19 bytes including ID**. Both degrees must be finite, and unavailable flags=0
requires both to be zero. Availability is joint: host capture requires a ready
native cooling source, its local CoolantTemp and the catalogued global EngineTemp.
There must be no same-name local shadow in Cooling. Changes to either temperature
advance the shared revision; equal-revision engine conflicts are rejected. Message IDs, flags and channels are unchanged by the v172 layout extension.

The host captures native EngineTemp alongside coolant. It never accepts a guest
thermal result. On connected guests, six scoped reads consume accepted host engine
degrees: FuelLine/Carburator #3 and Priming #1 use it as the native FuelChamber
clamp minimum; Priming #0 retains its strictly-greater-than-3°C bypass; Mixture/
Calculate density #3 and Oil/Viscosity #0 retain their native +50 adjustments;
Pressure/Oil pressure #0 retains ModifierTemp minus engine temperature. Subsequent
native priming, density, friction and pressure calculations remain unchanged.
Guest RPM and part/fluid inputs keep their existing authority. Missing/unavailable
host state supplies zero degrees to these six reads. Local driving, driver handoff
and engine-stream expiry do not reset thermal state. Host and disconnected
sessions retain their original inputs.

The catalog's host temperature source declares `engineGlobal` and the complete
`engineInputs` fuel/mixture/oil/pressure group. Native action identity, arithmetic,
outputs, thresholds, priming transitions and global references are validated as
a group. A changed binding disables the group and retries; every scoped operand
restores on nested dispatch or exceptions. Neither packet receipt nor projection
writes EngineTemp, physical coolant or saved part values, and neither adds native
simulation ticks. Existing guest save/damage protection remains in effect.
Remaining EngineTemp consumers/writers, coolant heater/fan behavior, complete
physical thermal handoff and live two-player acceptance remain open.

v171 adds host-only `VehicleCoolantState` (197), independent of vehicle physics
ownership. On reliable-ordered channel 0, the payload is `vehicleId:uint32`,
`revision:uint32`, `flags:uint8`, `celsius:float32` (**15 bytes including ID**).
Vehicle ID must be nonzero. Flags are strictly 0 (unavailable) or 1 (available);
Celsius must be finite, and unavailable requires zero. Native negative and hot
values retain float precision instead of the older 0–120°C dashboard byte.
Revisions belong to the vehicle and session, not its driver. Equal identical
frames are idempotent; equal conflicting, older and half-range-ambiguous revisions
are rejected. Newer revisions use signed modular uint32 comparison. Receipt and
publication keep copies of caller-owned state.

The `vehicleTemperature` catalog declares `hostAuthoritative` on every source.
Only Corris currently enables it, with `readyState: "Coolant temp 2"`. The host
publishes its validated native Cooling.CoolantTemp after observing the first
thermal calculation state; stopped ready sources retain their finite heat.
Missing, invalid, replaced or unready sources publish unavailable. Changes are
sampled at 2 Hz, with a 5-second reliable keepalive. Join, vehicle resync and
targeted vehicle repair snapshots observe the same publication without consuming
its pending broadcast. Guests accept only the selected handshaken host and
catalogued stable vehicle IDs, retaining state for late scene discovery.

Corris ignores the driver-owned VehicleState (60) coolant byte for presentation.
The host dashboard reads its native simulation. Guest dashboards, including the
local driver, read state 197 at the native gauge GetFsmFloat boundary; native
scale/clamp/needle actions still run. Before an available host state, guests use
the native cold gauge stop. Driver changes and engine-stream expiry do not reset
coolant revisions. Disconnect/cleanup releases the display and clears session
state. This projection never writes physical CoolantTemp, global EngineTemp or
saved part values. Shared thermal consumers, physical thermal handoff and live
two-player acceptance remain unfinished. Other cars retain their existing driver
coolant-byte presentation. Both peers require protocol 171; state 60 remains
25 bytes and no existing field is removed.

v170 extends accepted VehicleState (60) RPM to all three native Corris Cooling
RPM reads: Water Pump 2 #2 (<100 RPM closes circulation), Fan #5
(RPM / CoolingFanModifier), and Motor on? #0 (RPM >=200 takes the housing-leak
check). The message remains **25 bytes including ID**, with no new fields,
flags, IDs or channels. Both peers require protocol 170 because older hosts use
their local RPM for these cooling decisions.

RPM and movement share one five-reader cooling binding and the established
host-only, unprotected, current-owner, sequence and freshness rules. Native pump
installation, belt state, pump wear/efficiency, fan installation/modifier and
leak branches remain in control. Equality at 100 RPM permits circulation;
200 RPM enters the running leak check. Missing/expired/wrong-owner/snapshot
samples provide zero RPM, preserving native stopped behavior. Valid RPM does
not require optional torque or movement availability. Local ownership/seating,
guest mode, disconnect and cleanup retain native operands. Packets never write
global RPM, cooling rates, circulation, temperatures or saved part values, and
never add a native calculation tick. Scoped inputs restore after exceptions and
nested state transitions. Any changed selected reader invalidates the entire
cooling group; repaired references can bind again.

The local catalog profile is now `vehicleCooling`, extending the former
`vehicleSpeed` source metadata with rpmGlobal/pumpState/fanState/leakState.
Full thermal replication and driver handoff remain separate work.

v169 appends `movementSpeedAvailable:uint8` (strict 0/1) and
`movementSpeedTenthsKmh:uint16` to VehicleState (60), after torque. The first
22 bytes including ID retain their order; the message is **25 bytes including
ID**. Movement speed is a nonnegative magnitude in 0.1 km/h, clamped to 65535;
unavailable requires zero. The existing `speedTenthsKmh` remains the wheel-speed
reading for dashboard presentation. Wheelspin must not become cooling airflow.
Both peers require protocol 169; IDs, flags, channels and sequencing are unchanged.

For Corris, movement capture validates the native Measurements producer's
GetSpeed reference to this car's Rigidbody and its following ×3.6 conversion to
global SpeedKMH. Its following differential-speed read must retain its separate
local output. The enabled, started producer must be in its measurement state;
missing, disabled, changed, negative or nonfinite output sends unavailable/zero.
Other vehicles currently send unavailable. Accepted, relayed and snapshot copies
preserve movement, wheel speed, RPM and torque from the same report.

Only the active, unprotected host of a guest-owned Corris substitutes movement
at Cooling/Air cooling's SpeedKMH multiplication and Check temp's >2 km/h
comparison. Both readers and the movement producer validate as one group.
Missing/unavailable, expired or wrong-owner samples use native stationary speed.
The native ambient adjustment, airflow absolute value/division/clamps, additional
fan/heater cooling and hot-stationary branch remain in control. No packet writes
global speed, temperature or a cooling rate, and no packet adds a cooling tick.
Local ownership/seating, guest mode, disconnect and cleanup retain native reads;
scoped operands restore on normal completion, exception and nested transitions.
Full thermal replication and handoff remain separate work.

v168 appends `torqueAvailable:uint8` (strict 0/1) and `engineTorque:float32`
to VehicleState (60), after gear. The preceding 17 bytes including ID retain
their order; the message is now **22 bytes including ID**. Torque must be finite
and zero when unavailable. Available zero and negative torque are valid native
drivetrain outputs. No new message ID or flag is allocated; both peers require
protocol 168. Channels and existing ownership/sequence gates are unchanged.

Corris captures torque from the validated, active native HeatGeneration
GetProperty torque producer's Power output. A stopped, missing, changed or
nonfinite source sends unavailable/zero. Accepted copies and relayed/snapshot
copies retain the RPM/torque pair from the same sample; the host never mixes
load from an earlier owner with new RPM.

Only the active, unprotected host of a guest-owned car substitutes these inputs
at native HeatGeneration arithmetic/threshold helpers. Both RPM operands in the
squared-RPM calculation, its torque operand, and both start/stop comparisons are
scoped together under complete-graph validation. The native host retains its
friction read, heat-rate arithmetic/clamps, per-second temperature writer and
threshold hysteresis. The audited build starts above 400 RPM and stops below
100 RPM; equality preserves the current state. Missing/unavailable, expired or
wrong-owner samples supply zero to the selected inputs, returning to the native
stopped state (including its ordinary final running tick). Local seating or
ownership, guest mode, disconnect and cleanup retain native reads. Scoped fields
restore after completion or exception, including nested state transitions.

Packets never write host EngineTemp or add heating ticks. The driver supplies
neither a heat rate nor a temperature. Cooling airflow/speed, thermal replication
to the guest driver and complete thermal handoff remain separate work.

v167 extends accepted VehicleState (60) RPM to the host's native
Oil/Oil contamination #2 FloatOperator (`RPM / 250000` into
`OilContaminationRate`). It joins the six v166 pressure/mechanical-wear readers
in one validated seven-reader group. The **17-byte layout including ID is
unchanged**; there are no added fields, flags or IDs. Both peers require protocol
167 because older hosts calculate oil contamination from their own local RPM.

The same host-only authority, copied telemetry, freshness and ownership rules
below apply. Native Oil filter branches select filtering from the host's filter
Dirt (`>100` disables filtering; equality still filters), and the native clamp
retains `OilFilteringRate` (currently 0.01) as its minimum contamination rate.
Zero RPM on timeout therefore preserves native stopped-engine filtering behavior,
including the minimum contamination increase with a clogged filter. Three native
oil/filter writes and the 1.1-second Oil wait remain unchanged; receiving packets
does not apply these writes or add ticks. Saved guest writers remain disabled.
This supplies no guest oil quantity, filter dirt, temperature or wear delta.

v166 introduced the gameplay meaning of accepted VehicleState (60) RPM: while a
guest owns the Corris simulation, the host uses that RPM in six native oil-pressure
and mechanical-wear arithmetic inputs. **The 17-byte message including ID is
unchanged**, with no added fields, flags or IDs. The current handshake requires
protocol 168, including the heating extension above.

Transport authentication and the existing ownership/sequence gates precede any
wear input. Only an active host outside the protected-guest-save latch can apply
it; a locally owned or locally seated car uses native inputs. A fresh accepted
sample must identify that same vehicle and current guest owner and cannot use
the snapshot sentinel. For a still-delegated car, missing, expired or mismatched
samples supply zero RPM to the selected calculations. Local takeover, no owner,
disconnect and stream cleanup return to native reads.

The selected inputs are Pressure/Oil pressure #1 and Wearing/State 1 #7,
Calculate rate #1, Calculate rate 2 #0, Oil level #2 and Pressure leak #14,
plus Oil/Oil contamination #2 from v167.
Native arithmetic, the 1.1-second wear loop and its fourteen host part writers
remain in charge. RPM is substituted in an action-local operand only during the
native arithmetic helper and restored on completion or exception. All seven
bindings are validated as a group before any substitution; changed bindings
remove the whole projection and retry on later telemetry.

The driver never supplies wear deltas or host part state. This does not write
host global RPM or EngineTemp, cooling/fuel state, or guest saved parts. Host
heat, oil quantity/quality and pressure modifiers remain native host inputs.
Full shared heat progression, thermal handoff and other wear/damage systems
remain separate work.

Local implementation correction (still v165): VehicleState 60's existing
coolantTemp byte remains degrees over 0–120 °C. Corris/Sorbet/Machtwagen capture
now reads their native cooling source before dashboard clamps. Observers use
catalog-selected native gauge division/clamp actions and scoped native-read
projection between packets, without writing the cooling source. Existing stream
ownership, sequencing and timeout still apply; no wire semantics or layout change.

v165 appends CoolingAmbientAvailable:uint8 (strict 0/1) and
CoolingAmbientTemperature:float32 to EngineBlockState (195), giving **209 payload
bytes**. The previous 204 bytes retain their order. Temperature must be finite,
and zero when unavailable; available zero and negative temperatures are valid.
Availability is independent of all engine, radiator, hose and body installation
flags. These fields use the same copied revisions, stale/conflict rejection,
pending live publication, snapshots and five-second keepalives. Both peers require
protocol 165; no new ID is used.

Host capture reads CORRIS/Functions/RoofCheck::Raycast.TempCar, the native
shelter-adjusted temperature. The unique active source must be enabled,
initialized/started and in one of its four audited states (Cast ray, Check roof,
Under roof, Under sky), with all those states present and a finite output.
Unavailable/malformed sources clear only this input. Native shelter raycasts and
temperature progression remain on their original producer.

Cooling/Reset #1 remains an entry-only GetFsmFloat from an inert action-local
Raycast proxy into TempArea, preserving the shared RoofCheck reference and native
scratch on packet arrival. Unlike installed-part inputs, missing ambient data
has no meaningful numeric fallback: only Cooling pauses until a connected host
provides an available temperature. Admission may proceed while that consumer
is verifiably paused: every binding and protected writer must still validate,
even when the pending source appears first. Runtime readiness stays false until
the input arrives, and the valid proxy is reused while waiting. Normal scoped
recovery resumes its blocked entry; it does not replay old exits or write the
guest's RoofCheck temperature.
Native air cooling still computes abs(speed * (TempArea - 30)) divided by the
body airflow modifier, clamps it to 0.03..2.1, and combines other cooling rates.
Physical shelter detection, global/dynamic engine temperatures, host wear under
guest driving and live two-player validation remain separate work.

v164 appends CoolingAirflowFlags:uint8 (Grille=1, GrilleBlockoff=2, Hood=4,
FiberglassHood=8; no other bits), GrilleAirflow, HoodAirflow and
FiberglassHoodAirflow (three float32 values in that order) to EngineBlockState
(195), giving **204 payload bytes**. The previous 191 bytes are unchanged.
Modifiers must be finite and zero when their corresponding installed bit is
absent. All four mounts are independent of block/head/radiator installation;
the cover and both bonnet flags may coexist. Native Cooling decides which
inputs contribute. No new message ID is used; both peers require protocol 164.
These fields share copied revisions, conflict/stale rejection, live publication,
join/vehicle resync and five-second keepalives with the existing engine inputs.

Host capture validates fixed Data mounts at Update 2, initialized and started,
enabled, Installed and containing a matching active attached AssemblyID=1 part.
The grille accepts VIN413/B/C/D originals and counters, stock bonnet VIN411,
and fiberglass bonnet HOODa0 counters. The scene cover's exact ID is BLOCKOFF0;
it has no scalar fields. The grille and bonnets supply live mounted
CoolingAirRateModifier, with each invalid/transitional source cleared separately.

Seven entry-only Cooling reads use inert action-local host Data. Shared db
references and scratch remain untouched until native execution. Native Reset
sets CoolingAirRateModifier to 2900; an installed grille adds its modifier and,
if the cover is installed, another 4000. An installed stock bonnet adds its
modifier; otherwise an installed fiberglass bonnet adds its modifier. The stock
bonnet takes priority when both bits are present. All four guest mount Data
graphs remain paused through disconnect. Physical body assembly, ambient/thermal
state and host wear during guest driving remain separate work.

v163 appends CoolantHoseFlags:uint8 (Top=1, Bottom=2, Inlet=4, Outlet=8;
no other bits), CoolantHoseTightness:4×float32 in that order, and
CarburettorTightness:float32 to EngineBlockState (195), giving **191 payload
bytes**. The previous 170 bytes retain their order and semantics. There is no
array-length prefix. Each hose value must be finite and zero when its installed
bit is absent. Hose installation is independent of radiator/block/head state.
CarburettorTightness must be finite and zero unless CarburettorInstalled is set.
Finite bolt totals are not clamped on the wire. Every value participates in
copied revision/conflict checks, pending live publication, join/vehicle resync
and five-second keepalives. Both peers require protocol 163; no new ID is used.

Host capture reads the four fixed CORRIS/Assemblies mounts for VIN202 top,
VIN203 bottom, VIN216 heater inlet and VIN217 heater outlet hoses. Unique Data
must be enabled, initialized/started, Installed and settled at Update 2 for
radiator hoses or Update for heater hoses. The active attached native
original/counter part needs unique Data, AssemblyID=1 and Tightness. Each mount
supplies its live Tightness; a missing, invalid or transitional hose clears only
its own bit/value. Carburettor capture adds mounted Tightness to its existing
atomic input group under the installed cylinder head, covering all three variants.

Cooling/Bottom hose #0 reads host bottom-hose Installed; Cooling/Hoses #0..#3
read the four host hose Tightness values and #4 reads CarburettorTightness. All
six reads are entry-only, using inert action-local Data while preserving shared
db references and scratch until native execution. The normal Reset clears
TightnessTotal and WaterLeakRate before Hoses adds the five clamps:
totals below 104 enter State 5, clamp to 1..104 and add 0.2 / total to the native
WaterLeakRate; totals >=104 bypass this addition. Missing bottom hose follows
Empty coolant while saved-radiator writes remain blocked. The four saved hose
Data graphs stay paused through disconnect, preventing removal's wear copy,
clamp reset and detachment. Physical hose reconstruction, clamp controls, leak
presentation and host wear while a guest drives remain separate work.

v162 appends RadiatorInstalled:uint8 (strictly 0 or 1), RadiatorWear:float32,
RadiatorCoolant:float32, RadiatorPressureCap:float32 and
RadiatorFlectEfficiency:float32 to EngineBlockState (195), giving **170 payload
bytes**. The existing 153 bytes retain their order and semantics. Radiator
installation is independent of block/head availability. All four values must be
finite, and all must be zero when absent; finite native values are not clamped
on the wire. Every field participates in copied revision/conflict checks and
pending live publication, joins, vehicle resync and five-second keepalives.
Both peers require protocol 162; no new message ID is allocated.

Host capture uses the fixed CORRIS/Assemblies/VINP_Radiator mount's unique Data,
enabled, initialized/started, Installed and settled at Update 2. ActivePart must
be active, attached to the mount, have unique Data, AssemblyID=1, Coolant and a
canonical VIN201 original/counter or RADIATORa0/RADIATORb0 counter identity.
All four values come from the live mount and are captured atomically; missing,
invalid or transitional sources clear the radiator group. Five entry-only reads
in CORRIS/Simulation/Systems/Cooling::Cooling use inert host Data: Installed
(Radiator installed? #0), Coolant/PressureCap/Wear (Radiator Data #0/#1/#3), and
FlectEfficiency (Flect #1). Arrival leaves native scratch, events and the shared
db_Radiator reference intact. Native coolant clamps, pressure decisions and fan
hysteresis remain local calculations. Saved radiator Data stays paused through
disconnect, including active wear/coolant copies and removal. Physical radiator
reconstruction and cap/filling, hose and airflow controls remain separate work.

v161 appends RockerCoverInstalled:uint8 (strictly 0 or 1) and
RockerCoverTightness:float32 to EngineBlockState (195), giving **153 payload
bytes**. Installation requires HeadInstalled. Tightness must be finite and must
be zero when absent; finite native values are not clamped. Existing v160 fields
retain their exact order and semantics. The cover participates in copied
revision/conflict checks, join/resync and pending live publication. Both peers
require protocol 161; no new ID is allocated.

Capture follows the same installed native VIN111 head used by the intake, to
its unique VINP_RockerCover Data. A started, initialized, enabled mount must be
Installed and settled at Update 2, with an active attached VIN118 original or
counter part, AssemblyID=1 and Tightness. Capture uses the mounted tightness.
Unavailable/malformed cover data clears this group independently of other inputs.
The guest's Oil/Valve Cover #0 GetFsmFloat reads an inert host proxy while shared
db_Rockercover1, native scratch and saved Data remain intact. The normal leak
calculation is (64 - Tightness) / 60000. Saved cover Data stays paused after head
movement and disconnect; native removal cannot copy wear or detach that part.
Physical cover reconstruction and oil-cap/filling controls remain separate work.

v160 appends OilpanInstalled:uint8 (strictly 0 or 1), OilpanWear:float32,
OilpanTightness:float32, Oil:float32, OilContamination:float32 and
OilViscosity:float32 to EngineBlockState (195), giving **148 payload bytes**.
Installation requires the installed block, independently of the cylinder head.
All five values must be finite; an absent pan requires five zeros. Finite values
retain the native range. The v159 fields keep their order and semantics. Every
new value participates in copied revision/conflict checks, join/resync, and
pending live publication. Both peers require protocol 160; no new ID is allocated.

Host capture follows VINP_Oilpan under the installed native VIN101 block. It
requires unique initialized/started Data, Installed=true, settled Update 2,
and an active attached VIN106 original/counter part with AssemblyID=1 and
OilLevel. The five inputs come atomically from the mounted Data, whose Oil and
OilContamination fields differ from the part's saved OilLevel and OilDirt.
Missing, invalid or transitional sources clear only this group. Oilpan Data is
paused on guests, including after block movement and disconnect. Seven native
GetFsmFloat owners across Oil, Wearing and Cylinders use inert host proxies;
packet arrival changes neither scratch nor events nor saved part data. Native
starvation comparisons, leak/wear calculations and contamination scaling remain
intact. Physical oilpan reconstruction, filling/draining controls and live
running-engine acceptance remain separate work.

v159 appends ValvesAvailable:uint8 (strictly 0 or 1) and eight unconditional
float32 ValveSettings to EngineBlockState (195), giving **127 payload bytes**.
The array has no length prefix. Order is cylinder 1 intake/exhaust, cylinder 2
intake/exhaust, cylinder 3 intake/exhaust, cylinder 4 intake/exhaust. Available
settings require HeadInstalled; all eight values must be finite. Unavailable
settings require eight zeros. Finite values retain the native range. The v158
fields keep their exact order and semantics. Revision/conflict checks, defensive
copies, join/resync and independent pending live publication include availability
and every array value. Both peers require protocol 159; no new ID is allocated.

The host reads the unique native Valves ArrayList from the same installed head
used for intake/header capture. It requires exactly eight boxed float values;
missing, duplicate, malformed, nonfinite or detached sources publish an
unavailable zero set atomically without discarding unrelated engine inputs.
Guests redirect eight native ArrayListGet action-local owners to an owned
in-memory array. Native slot order, Data scratch, arithmetic, tolerance decisions
and persistent-write protection remain intact. Packet arrival changes neither
native scratch nor events nor the saved head array. The existing Get cam profile
#8 head-reference reader is signature-checked and retains its native owner/output.
Repair/disconnect restores array reader owners before destroying the proxy.
Physical valve-adjustment controls and host wear under guest driving remain
separate work; this change supplies the native engine's input settings.

v158 appends ExhaustFlags:uint8 followed by twelve unconditional float32 values
to EngineBlockState (195). The payload is now **94 bytes**. ExhaustFlags uses
Headers=1, Front=2, Rear=4, Muffler=8; bits 4–7 are reserved. Values follow in
that group order, each as DataPower, DataTorque, DataPowerAdd. All twelve values
must be finite; an absent section requires its whole triplet to be zero.
Headers require HeadInstalled in the existing flags. The three car-mounted
sections remain independent of block/head availability and of one another.
There is no array-length field. All other fields retain their v157 order and
semantics. Defensive copies, revision ordering, duplicate conflict rejection,
join/vehicle resync and pending live publication cover the new flags and values.
Both peers require protocol 158; no message ID is allocated.

Host capture uses the four catalogued native performance mounts. The manifold
is a unique direct child of the same installed VIN111 head used for intake
capture; the front/rear/muffler use exact vehicle paths. Every mount requires
unique enabled, initialized, started Data in Update 2, Installed=true, and an
active directly attached ActivePart with unique Data, AssemblyID=1, a supported
native identity and DataPower. Stock and supported upgraded identities are
catalogued independently per mount. Aftermarket parts need not have Wear.
Invalid/transitional sources clear only their own triplet and flag. Missing
head/block clears headers while retaining valid car-mounted exhaust sections.

Valves Headers, Exhaust front, Exhaust rear and Muffler each redirect native
GetFsmFloat #0/#1/#2 for DataPower/DataTorque/DataPowerAdd into the original
PartPower/PartTorque/PartPowerAdd scratch. Native FloatAdd #3/#4/#5 calculates
MaxPower/MaxTorque/PowerAdd. Arrival never replays calculation states or writes
shared scratch. All four saved mount Data graphs are paused and remain protected
through disconnect; header protection follows the saved head's movement.
Physical exhaust reconstruction, sound and separate racing-front/sidepipe/tip
routing are outside these four native performance readers.

v157 appends six float32 fields to EngineBlockState (195), in this order:
CarburettorPower, CarburettorTorque, CarburettorPowerAdd, AirCleanerPower,
AirCleanerTorque and AirCleanerPowerAdd. They follow the v156 fields, giving
an unconditional 45-byte payload. AirCleanerInstalled=32 is added. Valid flags
are 0/1/3/7/11/15/27/31/43/47/59/63. Each intake part requires the installed
head/block, independently of the other intake part and of block damage.
Every float must be finite. Absent carburettors require zero for all six of
their fuel/tuning/performance fields; absent air cleaners require zero for their
three performance fields. Finite values, including negative adjustments, retain
the native range. Revisions, copy isolation, stale/conflict rejection and
snapshot/live publication cover all fields. Both peers require protocol 157;
no message ID is allocated.

Host capture reads DataPower, DataTorque and DataPowerAdd from the live
carburettor and air-cleaner mounts under the same installed head observation.
The direct VINP_AirCleaner mount requires unique enabled, initialized, started
Data in Update 2, Installed=true, an active attached ActivePart with unique
Data, AssemblyID=1, a DataPower field and VIN1350 or a positive VIN135 counter
identity. Existing stock/two-barrel/four-barrel carburettor identities remain
supported. Invalid or transitional intake state clears only its own fields;
invalid head/block state clears both intakes. All values in each group are
validated before publishing any of them.

FuelLine Airfilter #1 Installed→Installed1 uses the host's filter installation
for the native Starting versus Dirt accumulation decision. Valves Carburettor
and AirFilter #0/#1/#2 read DataPower/DataTorque/DataPowerAdd into the original
PartPower/PartTorque/PartPowerAdd scratch. The normal native additions determine
MaxPower, MaxTorque and PowerAdd. Packet arrival changes no calculation scratch
or events. Original saved mount references remain intact. Air-cleaner Data
protection covers all ten native states, follows the saved VIN111 head after
movement/renaming, and persists through disconnect. Its disabled continuous
wear action remains disabled; removal wear, performance clearing and detachment
are blocked. Physical intake reconstruction, exhaust performance inputs and
host fuel/wear during guest driving remain outside this change.

v156 appends FuelChamber, CarbReserve and SettingMixture (three float32 values,
in that order after Wear) to EngineBlockState (195), and adds
CarburettorInstalled=16. Its unconditional payload is now 21 bytes. Valid flags
are 0/1/3/7/11/15/27/31; carburettor installation requires an available installed
block and head. All three values must be finite and zero unless the carburettor
flag is set. Finite native values are preserved without pre-clamping. A change
to any scalar advances the shared revision; duplicate conflicts, stale records,
copy isolation and snapshot/live publication rules cover every field. Both
peers require protocol 156; no new message ID is allocated.

Host capture follows the same observed installed head into its single direct
VINP_Carburettor mount. Unique Data must be enabled, initialized and started in
Update 2 with Installed=true and an active attached part (unique Data,
AssemblyID=1, SettingMixture field). Supported part IDs are VIN1130, positive
VIN113 counters, and positive counters appended to CARB2BRLa0/CARB4BRLa0.
Invalid or transitional carburettors clear only their flag and three scalars;
head/block removal clears dependent carburettor state. FuelChamber, CarbReserve
and SettingMixture come from the live mount, not stale part defaults.

FuelLine Carburator reads Installed #0, FuelChamber #2 and CarbReserve #4;
Mixture Calculate density #0 reads SettingMixture into CarbSetting. Each uses
an inert input proxy with native cadence and calculation scratch. Packet
arrival sends no native events. Missing host input supplies false/zero. Saved
carburettor Data protection follows the native head by name/ID and remains
paused through disconnect. Paused graphs also drain previously active actions
so queued wear work cannot survive admission. Existing scalar-write guards
continue to protect fuel and tuning writes. This adds engine inputs, not
physical carburettor replication or host fuel/wear simulation while a guest
drives.

v155 extends existing `EngineBlockState` (195) with flag HeadInstalled=8.
The payload remains nine bytes (`Revision:uint32`, `Flags:uint8`, `Wear:float32`)
and no message ID is allocated. Valid flags are now 0/1/3/7/11/15: head installation
requires an available installed block. Block damage and head installation remain
independent. Existing finite-wear validation, host-only/channel-0 admission,
copied revision ordering, 0.2 s polling, 5 s keepalive and join/vehicle snapshots
remain unchanged. A head change advances the same block revision even when block
wear and other flags have not changed. Both peers require protocol 155.

The host reads the head only from its settled installed engine block's ActivePart.
That block must have native ID VIN1010 or a valid VIN101 counter identity. It must
contain exactly one direct VINP_Cylinderhead child with unique Data, enabled,
initialized and started in UPDATE, Installed=true, and an active ActivePart
parented to that mount. The head part requires unique Data, AssemblyID=1 and
native ID VIN1110 or a valid VIN111 counter identity. Missing/loading/foreign or
ambiguous head sources clear HeadInstalled while retaining the independently
observed block state. Removing the block clears its head flag as well.

Cylinders Powertrain #0 reads Installed→Installed1 from an inert input using
HeadInstalled. Its native once-per-entry GetFsmBool, the seven-part BoolAllTrue
and subsequent start/stop decisions remain unchanged. Missing host observation
means head absent; the guest's saved installation is never a fallback. Arrival
does not enter combustion states or overwrite calculation scratch. Named
original references remain intact and are restored on disconnect.

Guest head-mount Data is paused through disconnect. Protection recognizes both
the initial scene path and VINP_Cylinderhead under a native VIN101 block, using
its scene name before load or its saved ID after movement/renaming. Input
validation additionally requires a unique parent Data with a valid block ID.
This prevents native removal from copying wear or detaching the saved head.
The catalog requires all ten head-mount states and the exact moving-parent rule.
The profile totals 78 sources/132 reads across nine consumers, 75 scalar guards
across 11 graphs and five paused graphs; factories remain 38 replacement/31
package. Physical block/head reconstruction, valve arrays, head-dependent thermal
and oil behavior, other engine inputs and full two-player starting remain open.

v154 adds host-only `GearboxState` (196) on reliable-ordered channel 0. Its
9-byte payload is `Revision:uint32`, `Flags:uint8`, `Type:int32`, in that order.
Available=1; only flags 0/1 are valid, with Type=0 required when unavailable.
Available types preserve the native integer without clamping or inventing an
enum. In particular, unavailable zero is **not** an available manual gearbox.
Hosts reject peer records; guests accept only the selected host after handshake.
Copied revisions reject stale/conflicting duplicates, accept identical retries
and compare unsigned wrap using a strictly positive signed forward delta.

The host samples the unique CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox::Data
every 0.2 s. It must be enabled, initialized and started, with Installed, Type and
ActivePart fields. A fitted source requires Update 2, an active part parented to
the mount, unique part Data, AssemblyID=1 and matching native part Type. A settled
absent source requires Idle and preserves the mount's retained Type: the vanilla
starter reads that field even after removal. Missing/transitioning/invalid sources
publish unavailable and recover on polling. Changes advance the revision; a 5 s
keepalive retries. Join and vehicle-group snapshots do not consume pending live
updates. Session teardown clears both accepted and publication state.

Starter Check automatic #0 redirects only GetFsmInt(Data.Type→Automatic) to an
inert host input. Its native once-per-entry cadence, IntCompare threshold and
subsequent SimAutomatic/3 speed.GearLetter read and P/N comparisons remain intact.
The current driver retains the local selector. Arrival does not enter a state,
overwrite calculation scratch or move that selector. Missing/unavailable host
input pauses the protected Starter graph until a valid observation can rebind.
The protection guard may then resume an already-blocked native entry through
the unchanged interlock; packet receipt alone does not execute it. It never falls back to the guest's saved type or a manual default.

Guest gearbox mount Data stays paused through disconnect, protecting its active
Update 2 wear/oil/damage writes and removal detachment. The profile now has
77 sources/131 reads across nine consumers, 75 scalar guards across 11 graphs,
four paused graphs and unchanged 38 replacement/31 package factories. This is a
starter-interlock input, not physical transmission reconstruction or complete
gearbox/driver simulation. Transmission ratios, oil/damage inputs elsewhere,
head and other engine dependencies, guest-driving host wear and live two-player
acceptance remain open. Matching protocol 154 peers are required.

v153 adds host-only `EngineBlockState` (195), reliable-ordered channel 0. Its
9-byte payload is `Revision:uint32`, `Flags:uint8`, `Wear:float32`. Available=1,
Installed=2 and Damaged=4; only flags 0/1/3/7 are accepted. Unavailable/removed
blocks carry wear zero; installed wear must be finite and is not clamped.
Damaged is the host mount's native flag, never inferred from condition. Hosts
reject guest records; guests accept only the selected host after handshake.
Copied revisions reject stale or conflicting duplicates, allow identical retries
and compare unsigned wrap using a strictly positive signed forward delta.

Host capture samples CORRIS/MotorPivot/MassCenter/Block/VINP_Block::Data at 0.2 s.
It must be enabled, initialized and started. Installed=false is settled only in
Idle; fitted inputs require Update, active ActivePart parented to the mount,
unique part Data, AssemblyID=1 and a Wear field. Condition comes from mount Wear.
Missing/intermediate/invalid sources publish unavailable and recover on polling.
Changes advance the revision, with a 5 s keepalive. Join/vehicle-group snapshots
include the record without consuming a pending live update. Session end clears
accepted and publication state. No physical block replica or fitting intent is
introduced.

Four native reads use separate inert host inputs: Starter Motor installed #0
(Installed→MotorInstalled, once) and Running #8 (same field, every frame), Oil
Major damage? #0 (Wear→Wear), and Cooling Block damage #0 (Damaged→Damaged).
Starter uses literal object targets; their original wrappers stay intact and
must resolve to the same exact native block mount. Oil/Cooling retain db_Block
references. Read cadence and native comparisons are unchanged; packet arrival
does not enter states, stall the engine or overwrite calculation scratch.
Absent/unavailable proxies expose Installed=false, Wear=0 and Damaged=true.

Guest block mount Data is paused until restart, including after disconnect,
because native removal copies wear back to the saved part, sends uninstall
notifications and detaches the assembly. The installed build's continuous wear
copy in Update #1 is disabled and remains disabled. Existing 75 scalar guards
across 11 engine graphs remain unchanged; paused graphs increase to three.
The input profile has 76 sources/130 reads across nine consumers, with the same
38 replacement/31 package factories. Matching protocol 153 peers are required.
Physical block reconstruction, other engine inputs (including gearbox/head),
guest-driving host wear and full two-player engine acceptance remain open.

v152 adds host-only `BatteryState` (194) on reliable-ordered channel 0. Its original
9-byte payload (extended with ChargeMax in v177) is `Revision:uint32`, `Flags:uint8`, `Charge:float32`, in that
order. Available=1 and Installed=2; only flags 0, 1 and 3 are valid. Unavailable
or removed sources carry charge zero. Installed charge must be finite; native
transient negative values are preserved without invented clamps or thresholds.
Hosts reject this message even from authenticated guests. Guests accept it only
from the selected host after handshake, cache copies, reject stale/conflicting
revisions and accept identical retries. Revision comparison uses unsigned wrap
with a strictly positive signed forward delta; session end clears the cache.

The host samples the unique CORRIS/Assemblies/VINP_Battery::Data every 0.2 s.
It must be enabled, initialized and started. A settled absent battery requires
Installed=false in Idle. Installed inputs require Check joint, Delay 2, Calc or
Died battery, an active ActivePart parented to that mount, unique part Data,
AssemblyID=1 and the native Charge/ChargeMax/DischargeRate fields. Loading,
intermediate attachment, missing bindings and invalid values publish unavailable
zero inputs and recover on subsequent polls. Charge comes from the mount, where
native engine draw and charging accumulate, rather than the part's stale copy.
Changed flags/charge advance the revision; unchanged state has a 5 s keepalive.
Join and vehicle-group resync snapshots include the record without consuming a
pending live broadcast. No battery intent, factory or physical replica is added.

Three native Electrics reads consume an inert host-input proxy: Wiring #0 reads
Installed into Battery, Battery #0 reads Charge into Charge, and Engine running?
#1 reads Charge into Volts. Each remains a one-shot read on native state entry.
Native cold adjustment, voltage comparison and division by ten remain native;
receiving a packet does not replay calculations or alter shared db_Battery/Data
references. Guest VINP_Battery::Data is paused through the persistent save latch,
and ten Starter/Electrics Charge/ChargeMax writes are guarded. This brings engine
protection to 75 scalar writes across 11 consumers and two paused graphs. The
input profile has 73 sources/126 reads across nine consumers; factory counts
remain 38 replacement/31 package. Matching protocol 152 peers are required.

Physical battery installation/removal, guest-driver draw reaching the host,
other electrical writers, cable visuals/fire, remaining engine dependencies and
full two-player engine acceptance remain open. This record supplies engine
inputs and save protection; it is not a completed battery simulation.

v151 makes Starter Check Flywheel #0 read Installed from the same unique
accepted/applied host flywheel or flexplate used by Cylinders. VIN120,
FLYWHEELa0, FLYWHEELb0 and VIN138 share the native block-relative mount
CrankshaftParent/VINP_CrankPulley/VINP_FlywheelFlexplate. All four factory
references and the consumer must agree on the live mount. Missing, pending,
removed, conflicting or mismatched replicas supply false. The native BoolTest
chooses Prepare starting or No Flywheel; this gate introduces no wear, tightness
or inertia threshold. Arriving state updates an inert bool proxy; the next native
read controls scratch and decisions, without changing saved guest Data or the
shared db_Flywheel reference.

Existing message layouts, IDs, framing and channels are unchanged. State 185
retains Wear/Tightness/InertiaFactor for all four families. Matching protocol 151
peers are required for this changed input behavior. The profile has 72 sources
and 123 reads across nine consumers, with 38 replacement and 31 package factories.
Battery, gearbox/block inputs, full native starting and two-player acceptance
remain separate work.

v150 adds host-only `WiringState` (193) on reliable-ordered channel 0. Its payload
is `SourceId:uint32`, `Revision:uint32`, `Flags:uint8`, in that order (9 bytes).
Flags are Available=1, Installed=2, Bolted=4; unavailable is exactly zero. Unknown
bits and unavailable values are rejected at serialization and decoding. Known
source IDs and bolt capability are checked before accepting or publishing state:

| ID | Native source under CORRIS/Wiring/DatabaseWiring | Bolted supported |
|---|---|---|
| 1 | WiringCoilHarness | no |
| 2 | WiringBatteryStarter | yes |
| 3 | WiringBatteryHarness | yes |
| 4 | WiringBatteryGround | yes |
| 5 | WiringIgnitionFusebox | no |
| 6 | WiringAlternatorRegulator | no |
| 7 | WiringRegulatorHarness | no |
| 8 | WiringFueltank | no |
| 9 (v181) | WiringHeatercontrolFusebox | no |
| 10 (v181) | WiringHeater | no |
| 11 (v181) | WiringFuseboxWindow | no |

These IDs are wiring-source IDs, separate from item IDs. Installed and Bolted
remain independent native values: Starter reads the ground's Installed flag,
while Electrics reads its Bolted flag. Standard sources publish after Data
reaches Basic state; battery sources wait for Set bolt after Tightness? finishes.
Missing, inactive, disabled, loading or invalid sources publish unavailable.

Only the selected host after handshake may deliver state; hosts reject incoming
193 even from authenticated guests. Each source advances its revision when flags
change. Guests use signed uint differences for wraparound ordering; identical
revision/flags are duplicates, identical revision/different flags are rejected.
Accepted and returned values are copied, and session teardown clears the cache.
Host polling is every 0.2 seconds, with changed records and a 5-second keepalive
on channel 0. Join snapshots and vehicle soft-resync include all eleven records
(the original eight plus the three heater circuits added in v181).
Snapshot capture does not consume pending live broadcasts. Wiring is not added
to the vehicle checksum; ordered keepalive and vehicle resync restore its state.

Eleven wiring bindings supply thirteen native bool reads in Cylinders, Starter
and Electrics through inert read-only proxies. Unavailable/unreceived state
supplies false. Arrivals update proxy values; normal native reads own calculation
scratch and decision timing. Saved guest wiring Data and wire visuals are not
mutated. There are now 71 sources/122 reads across nine consumers (28 sources in
Cylinders), with 38 replacement and 31 package factories unchanged. Guest wiring
tools, cable/bolt visuals, battery state, wiring fire and complete engine operation
remain separate work. Matching protocol 150 peers are required.

v149 adds the native VIN212 ignition-coil factory at
CARPARTS/PARTSYSTEM/SPAWNERS_VIN/IgnitionCoil212::Spawn. Its replacement state
185 scalars are Wear, Tightness, in that order. It uses the standard native
factory output path and has no CreatePartsPackages binding. Native fresh
initialization chooses host wear; guest replicas retain the published value.
Existing family layouts, framing, channels and message IDs remain unchanged.
Matching protocol 149 peers are required for the new factory/input semantics.

Cylinders Ignition #0 Installed now reads from the unique accepted/applied host
coil at the vehicle-relative Assemblies/VINP_IgnitionCoil mount. The consumer
and factory must agree on that live mount and its car body must be registered.
Pending, missing, conflicting, removed or mismatched copies supply false.
The native three-way coil/distributor/wiring gate decides whether to proceed
to Plug data or stop the starter. Only the coil reader's owner is replaced;
the separate wiring input, native scratch timing and saved guest mount Data
remain intact. This coil installation gate does not impose a wear or bolt
threshold. There are 60 sources/109 reads across nine consumers (27 in Cylinders),
38 replacement factories and 31 package factories. Wiring authority, complete
engine operation and live two-player acceptance remain open.

v148 makes native Cylinders Limiter #0 Installed and #3 SettingRPM use the
unique accepted/applied REVLIMITER0 attachment at the vehicle-relative
AssembliesTuning/VINP_Revlimiter mount. State 185 retains its existing
Tightness/SettingRPM scalar order; no fields, framing, channels or IDs change.
Matching protocol 148 peers are required for the changed engine-input semantics.

Both the native consumer and factory must agree on the live mount, whose car
body must be registered. Missing, pending, conflicting, removed or mismatched
copies supply Installed=false and SettingRPM=0. The native installation gate
disables Drivetrain.revLimiter and exits before assigning maxRPM; an absent
limiter does not replace the existing maximum RPM. An applied part supplies the
host's actual setting without rounding or substituting a prefab default. Updates
do not replay the Limiter state: native GetFsm reads drive the scratch values
and drivetrain writes. Saved guest mount Data and ActivePart remain untouched.
There are 59 input sources and 108 reads across nine consumers (26 in Cylinders).
Guest knob requests/presentation, complete engine operation and live two-player
acceptance remain open. No new message ID is allocated.

v147 adds stock VIN120, lightweight FLYWHEELa0/FLYWHEELb0 and automatic
VIN138 flexplate factory profiles.
Their state 185 scalars are Wear, Tightness, InertiaFactor, in that order.
No existing family layout, framing, channel or message ID changes. Matching
protocol 147 peers are required for these new factory and engine-input semantics.

The native Cylinders Flywheel state reads Installed at #0 and InertiaFactor at #2
from the unique accepted/applied host attachment at
VIN1010/CrankshaftParent/VINP_CrankPulley/VINP_FlywheelFlexplate. All four
factories must agree on the live mount. Missing, pending, removed, conflicting
or mismatched replicas supply false installation and zero inertia; the native
installation gate exits before performing inertia calculations. Host capture and
replica admission reject nonpositive/nonfinite inertia and values that overflow
the native .04/InertiaFactor calculation. Native reads alone update EngineInertia
scratch, Drivetrain.engineInertia and MotorShake.Flywheel. Saved mount Data and
ActivePart remain unchanged. The profile has 58 sources and 106 reads across
nine consumers, including 25 sources in Cylinders. Complete engine operation,
cylinder-head assembly synchronization and live two-player acceptance remain open.

v146 makes the four native cylinder spark-plug inputs follow accepted/applied
SPRKPLUG0 replacement state. Sparkplugs array slots 1–4 map to cylinders 4–1.
Each slot supplies Installed, Wear, Tightness and Durability through an independent
read-only proxy; saved guest mount wear writers remain protected. State 185
retains Wear/Tightness/Durability scalar order, layout and channel. There are no
new message IDs. The changed engine-input semantics require matching peers.

The v146 profile has 57 sources and 104 reads across nine consumers (24 sources
in Cylinders). For cylinder N, the native reads are Reset #(8+N) Wear,
CylinderN #3 Installed, Add to power N #2 Tightness, and Plug data #(6+N)
Durability. The native array, consumer mount, accepted assembly index and applied
replica identity/parent must agree. Missing, pending, conflicting or removed
plugs supply false installation and zero condition until resolved. Native
firing/efficiency, tightness/wear misfire eligibility and durability arithmetic
read the host values without overwriting shared calculation scratch or saved
parts. Random misfire outcomes are still local native calculations; this change
does not establish deterministic full-engine simulation or host wear while a
guest drives.

v145 adds ToolTighten=6/ToolLoosen=7 to replacement requests/receipts 188–189
for catalogued SPRKPLUG0 wrench turns. Wire lengths, slot placement and message
IDs are unchanged; older peers are refused. The host validates the observed
revision, fitted socket identity, fresh proximity, integer 0–8 tightness and
native readiness/cooldown, then executes one native turn and publishes state 185.
Guest native wrench/ratchet events produce intents, with local scalar writers
replaced. Identity-based recognition retains the native wrench-size gate.

v144 validates replacement removal against each factory's native pick layer.
SPRKPLUG0 uses layer 12 after its native Screw pose update; other factories
retain layer 19. State 185 can now publish RemovalAllowed for a loose fitted
spark plug when the host's collider, mount and attachment checks pass. Requests
188 still traverse the native removal prerequisite. Fields and IDs are unchanged;
matching peers are required for the changed removal semantics. Guest screw-tool
input, complete fitting/removal lifecycle acceptance and engine inputs remain open.

v143 gives sparkplugbox0 boxes host-owned quantity/opening through 184/186/187
and individual SPRKPLUG0 outputs replacement state 185 with Wear/Tightness/Durability.
No fields, framing or message IDs change; the new factory semantics require
matching peers. Spark-plug fitting, screw-tool controls and engine inputs remain
separate work.

v142 introduced fan inputs; v210 corrects their source to accepted, applied
VIN137 radiator fan state (185) for native engine-load and cooling effects.
Existing Wear/Tightness order, framing and message IDs are
unchanged; older peers are refused because engine-input semantics changed.

v141 gives all four fitted VIN103 piston slots native cylinder-firing, efficiency
and mixture smoke-check effects from replacement state (185). Wear/Tightness
order, framing and message IDs are unchanged; older peers are refused because
engine-input semantics changed.

v140 gives all five fitted VIN104 main-bearing slots native oil-pressure condition
effects from replacement state (185). Wear/Tightness order, framing and message
IDs are unchanged; older peers are refused because engine-input semantics changed.

v139 gives accepted fitted head gasket, thermostat, thermostat housing and oil
filter state (185) native combustion, cooling, leak and contamination input effects.
Existing scalar order, framing and message IDs are unchanged; older peers are
refused because engine-input semantics changed.

v138 gives accepted fitted crankshaft, crank pulley and auxiliary shaft/sprocket
state (185) native engine-input effects. Existing Wear/Tightness order, framing
and message IDs are unchanged; older peers are refused because semantics changed.

v137 gives accepted fitted VIN107 state (185) native timing-belt installation
and wear effects in Cylinders. Existing scalar order, framing and message IDs
are unchanged; older peers are refused because engine-input semantics changed.

v136 gives accepted fitted FANBELT0 state (185) native installation effects in
Oil, Valves, Cooling and Electrics. No fields or message IDs change; older peers
are refused at handshake because engine-input semantics changed.

v135 appends Durability/Efficiency to both alternator scalar arrays and appends an
optional fitted alternator damage byte after CamProfile in replacement state
(185). Seven electrical reads use the actual host part/mount condition, including
repeated Installed/Damaged checkpoints. Older peers are refused at handshake.

v134 appends Friction after Wear/Tightness/SettingRotation for VIN133 and
ALTERNATOR0 replacement state (185). Three Oil reads consume the accepted applied
stock or upgraded alternator. Older peers are refused at handshake.

v133 gives accepted fitted VIN117 state (185) and latest bolt receipts native
Bolted effects in eight distinct rocker slots. No fields or message IDs change;
older peers are refused at handshake because engine-input semantics changed.

v132 appends CamProfile to replacement state (185), and appends Durability and
ValveTolerance after Wear/Tightness for all five camshaft families. Cylinders,
Wearing and Valves consume their current applied host camshaft. Older peers are
refused at handshake.

v131 appends Durability after Wear/Tightness for VIN132 replacement state (185).
Oil and Wearing now require multiple independent part sources in each native FSM;
older peers are refused at handshake.

v130 appends Durability and OutputRate after Wear/Tightness for both VIN125 and
FUELPUMP0 replacement state (185). Two fuel-pump consumer entries accept either
factory at their shared mount; older peers are refused at handshake.

v129 appends Durability and Efficiency after Wear/Tightness in VIN126 water-pump
replacement state (185). The required guest input profile adds all seven native
water-pump reads across Oil and Cooling. Older peers are refused at handshake.

v128 appends Durability after Wear/Tightness in VIN130 replacement state (185)
and gives that accepted host starter three native guest inputs: Installed, Wear
and Durability. The distributor projection remains in place. No new message IDs
or outer framing change; the starter scalar count and guest semantics require
the version bump.

v127 gives accepted and applied VIN131 replacement state (185) engine-input effects
on guests: combustion reads Installed, Wear, Tightness and SparkAngle from an owned
runtime proxy for the host's fitted distributor. Guest saved mount data stays
unchanged. This is a semantic version bump with no new fields, layouts or message
IDs; older peers must not connect with different distributor engine behavior.

v126 ties VehicleState (60) to established vehicle simulation ownership, including
parked ignition, and retains separate sequence history for each vehicle/sender.
Observers cannot republish received ignition as their own state. The last simulator
sends a reliable OFF before releasing its parked vehicle; an incoming nearby driver
can take an empty seat from a remote non-driver. Host snapshots do not override a
live guest simulator, and targeted vehicle poses use WorldItemSnapshot (122) without
changing ownership. Message layouts and IDs are unchanged.

v125 extends part rotation operations 2/3 to the catalogued VIN131 distributor
timing control. The host selects the native adjustment profile from the part's
catalog binding: distributor SparkAngle changes by 0.2 within 0–20, while
alternators retain their 0.5 steps within 0–7. Requests contain no profile or
absolute angle; layouts and message IDs are unchanged. Saved timing and its
guest pivot presentation use ReplacementPartState (185).

v124 makes VehicleDamage (64) host-only, including while a guest drives. Guests
retain copied host condition for checksums and presentation; they neither publish
damage nor write native mount Wear or replay failure events. The native guest
PartBreakages graph remains paused while its world is protected. VehicleDamage's
81-byte layout is unchanged; its retained OwnerPlayerId field must be zero. Other
vehicle streams retain their existing ownership rules.

v123 appends a presentation revision and optional fitted fan-belt presentation record
to ReplacementPartState (185): visibility, running, deformation scale, audio
pitch/volume and texture scroll speed. The host observes its native belt presentation;
guests apply isolated cosmetics without running native wear or breakage actions.
Cosmetic changes advance only PresentationRevision, preserving the gameplay Revision
used by fitting/removal requests. Absent records clear earlier visual observations.
No message IDs change.

v122 adds HandTighten=4 and HandLoosen=5 to PartFitRequest/PartFitReceipt (188–189),
with slot zero, for the catalogued oil-filter Screw control. The host executes one
validated native turn of integer Tightness within 0–8. Saved Data.Tightness continues
through ReplacementPartState (185); no child BoltState (44/123) stream is added.
Direction and observed revision remain immutable across retries. Message layouts
and IDs are unchanged; alternator rotation operations 2/3 retain their v119 meaning.

v121 extends ReplacementPartState (185) to the direct shopping-bag fan-belt and
oil-filter factories. These outputs retain native Data/ID part identities and use
the existing replacement-state publication, join and resync paths. They are excluded
from generic ItemSpawn manifests. Message layouts and IDs are unchanged; the version
bump prevents older guests from accepting a bag opening without these part adapters.

v120 adds BagState (190), BagOpenRequest (191) and BagOpenReceipt (192).
Shopping bags use the host's persistent factory/native identity. Guests request
one/all openings against a revision; only the host consumes inventory and creates
outputs. SpawnIntent (53) is retired and ignored; its ID is never reused. ItemSpawn
(52) keeps its layout, with host ownership and offerSequence=0 for bag spills.

v119 adds RotateIncrease=2 and RotateDecrease=3 to the existing part-operation
request/receipt (188–189), with slot zero. The two catalogued alternators use
native half-degree hand rotation within 0–7 degrees. Identity/revision, settled
mount ownership, fresh guest proximity, the loosened adjusting bolt and native
readiness gate each request; retries only recover its receipt. Absolute scalar
and pivot updates continue through ReplacementPartState (185). Layouts are unchanged.

v118 appends SlotIndex to PartFitRequest/PartFitReceipt (188–189) and adds guest
fitting for the piston, main-bearing and rocker arrays. The requested native slot
is immutable; the host rejects a changed nearest slot before entering installation.
Zero retains fixed-mount fitting and removal; nonzero slots are catalog-bound.
No new IDs; ReplacementPartState (185) retains its v117 layout.

v117 appends an install/remove operation to PartFitRequest/PartFitReceipt (188–189)
and a strict RemovalAllowed flag to ReplacementPartState (185). Guests can request
native removal of fitted replacement copies using the same immutable request ledger.
The host validates the current attachment, tightness and native interaction readiness
before removal; old clicks cannot remove a subsequently refitted part. No new IDs.

v116 adds PartFitRequest/PartFitReceipt (188–189). Guests request fitting of an
observed replacement part; the host selects its fixed native mount and traverses
the native occupancy, prerequisite and distance checks. Retries recover the same
outcome. Generic guest state-40 replay for catalogued replacement parts is refused.

v115 appends stable parent identity, relative path and local pose/scale to
ReplacementPartState (185). Missing fitted replacement copies can attach to a ready,
unoccupied guest mount and follow its hierarchy; removal restores loose-item motion.
Their native installation, bolt and save actions remain disabled. No new message IDs.

v114 appends the native parent-part tightness total to BoltState (44) and
WorldBoltSnapshot (123). Raw bolt turns (41) are guest intents executed only by the
host; guest scalar reports cannot change host values. Host results restore the bolt
save-array entry, native position scale and absolute parent total, including zeros.
Derived part states (40: Bolted/Unbolted/Stop) are no longer relayed. Native part
identity also covers parts without the optional Installed scratch bool. No new IDs.

v113 preserves native part lifetime when fitting removes its Rigidbody. Message 185
keeps its layout, but Installed now means AssemblyId > 0; contradictory pairs are
rejected. Data.Installed is native occupancy-query scratch state. Fitted parts remain
available in replacement snapshots/targeted replies without a loose item body;
physics-component removal must not produce ItemDespawn. No new message IDs.

v112 adds PackageOpenRequest/PackageOpenReceipt (186–187). Guests request an
opening; the host validates and executes one native operation, confirms the exact
part output and acknowledges it. Retries cannot consume another quantity or mint
another part. Complete fitted-part reconstruction remains deferred.

v111 adds ReplacementPartState (185) for native boxed-content factory outputs and
isolated loose-part copies. Accepted ItemDespawn requests are echoed to their sender;
new replacement copies wait for that acceptance before removal. Installed-part graph
reconstruction remains deferred; v112 adds guest opening intents.

v110 routes native part bodies and their registered FSMs by persistent save identity.
Part/bolt/control IDs remain stable when parts are renamed, installed or scanned in
different orders. Existing message layouts and message IDs are unchanged.

v109 adds PackageState (184) for host-created standard boxes, their quantities and
join/resync materialization. Guests create isolated replicas, preserving their own
saved boxes. Guest opening is implemented by 186–187 in v112; replacement-part assembly support remains incomplete.

v108 introduced stable package identities and native host disposal/save cleanup.

v107 appends full native tightness/wear floats to PartState (45) and WorldPartSnapshot
(124). Legacy byte fields remain on the wire but are not applied. Guest part reports
request a deferred host observation instead of overwriting host scalars. Part resync
includes zero-valued records, retries late bindings and hashes native condition.

v106 adds catalog-backed trophy factories to ItemSpawn (52), using flag bit 1 and
persistent native item IDs. Existing framing/layout and all message IDs are unchanged.
Connected guests and late joiners create the host's awards from the exact native
prefab; guest save objects are preserved separately.

v105 preserves message layouts and strengthens shared-item recovery: item resync
includes session removals and refreshed spawn manifests, removals remain terminal
until session teardown, and replay manifests repair missing live replicas.

v104 appends the complete hockey betting collections to HockeyBettingState (160):
six matchups, all 18 odds, previous matchups/results/odds/scores, games played and
standings text. Guests preserve and pause their native season/odds generators;
complete host boards survive delayed scene binding. Megaveto ticket transactions
remain separate unfinished work.

v103 adds host-issued Lotto tickets, selected rows and acknowledged cash/bank
claims (181–183). Generic Lotto Pay replay is removed; host ticket IDs persist
through the native ticket save lifecycle. Megaveto transactions remain separate.

v102 replaces the incorrect Lotto string state (96, retired) with complete native
draw lists, prize tiers, pots, rounds and results visibility (180). Guest native
draw generation pauses and late-bound/joining guests retain the latest host draw.

v101 appends connection-scoped report identity to RallyIntent (87) and exact
acknowledgments plus per-player revisions to RallyState (86). Native crossing
edges retry until acknowledged; duplicates cannot restart the host's race clock.

v100 adds host-observed Ventti NPC/furniture poses (178) and live sound cues (179).
Guests apply poses and the host's chosen native sound variation without entering
native outcome FSMs. Sound cues are never included in join or resync snapshots.

v99 adds host-owned Ventti hands (175), authenticated commands (176), and cached
receipts (177). Legacy control replay (94) is retired. The host alone debits the
shared wallet, draws its private deck, and settles native property effects.

v98 added `VenttiTableState` (174) for full-width native observations before the
host adopts the table. It stops once 175 is available; guests ignore 174 after
receiving 175. Legacy `GamblingState` (93) remains retired.

v97 adds `VenttiPropertyState` (173) for host-owned key flags and cabin access.

v96 preserves all message layouts. `WorldStateChecksum.vehicleCrc` now includes
parked-car damage and tire/drivetrain condition (id 47 below). `VehicleCondition`
tire pressure (id 65) rounds to the nearest hundredth of a bar before clamping to
0–255, and decodes as `byte / 100f`; encoding that decoded float returns the same
byte, including across ownership handoffs. Non-finite local pressure maps to 0
for NaN/negative infinity and 255 for positive infinity.
Local v191 corrects the Core conversion boundary for native TirePressure.Data:
its Pressure variable uses kPa (default 190), so capture rounds that native value
to the existing byte and native replay writes the byte as kPa. The bar helpers
remain bar×100 and byte/100; one wire unit is unchanged at 0.01 bar = 1 kPa.
No message framing, layout, sequence or authority semantics change in this fix.
As of v197, Core capture marks non-finite native pressure unavailable instead of
publishing a known saturated value; the standalone quantization helpers retain
their earlier behavior.
Any breaking change to framing, message layout or semantics bumps the version;
hosts refuse mismatched clients during handshake.

## Transport & framing

Datagram transports: classic Steam P2P via `SteamNetworking.SendP2PPacket`,
the UDP local-test transport, and loopback for dev. `PacketCodec` receives this
frame after the transport has supplied its channel metadata:

```
[2 bytes messageId]  -- little-endian ushort
[payload]            -- message-specific, see below
```

The channel is not part of the `PacketCodec` frame. Steam carries it in native
`nChannel`; the UDP test transport carries it in its outer control envelope; and
loopback carries it alongside the queued payload.

All integers little-endian. Strings are UTF-8 with ushort byte-length prefix
(empty == null). Blobs are int32 length + raw bytes. Floats are raw IEEE 754.

## Channels

| Channel | Guarantees | Used for |
|---|---|---|
| 0 ReliableOrdered | reliable, ordered | events, FSM transitions, economy, chat, handshake |
| 1 UnreliableSequenced | best-effort; *receiver* drops stale packets via per-stream sequence numbers | transforms |
| 2 ReliableBulk | reliable; large, chunked | reserved for a future bulk transfer message |

Session admission is host-authoritative too: before a peer completes its
handshake, a host accepts only that peer's reliable-ordered `HandshakeRequest`.
While connecting, a guest accepts only its selected host's reliable-ordered
`HandshakeResponse`; it cannot apply host world state until that response is
accepted. Afterwards the host accepts only registered peers, while a guest
accepts packets only from its selected host. Every current non-transform message
uses channel 0; `PlayerTransform` uses channel 1, and item/vehicle/NPC transform
messages, including `VenttiSceneState`, may use channel 0 for a final or snapshot state. Channel 2 is currently
reserved. Packets that break those rules are dropped.

## Session flow

```
client                          host
  |------ HandshakeRequest ------>|   protocol/mod/game version + catalog hash
  |<----- HandshakeResponse ------|   accept (playerId) or refuse (reason)
  |<----- PlayerSpawn * n --------|   existing players
  |<----- PlayerSpawn (self) -----|   broadcast to all incl. newcomer (clients filter own id)
  |                               |
  |   (guest loads the GAME scene and finishes its first world scan)
  |------ WorldSnapshotRequest -->|   guest's id hash (diagnostic)
  |<----- WorldDoorSnapshot * n --|   doors the host has seen change
  |<----- WorldItemSnapshot * n --|   current pose of every item/vehicle
  |<----- WorldBoltSnapshot * n --|   every ready fitted Screw FSM, including zero tightness
  |<----- WorldPartSnapshot * n --|   native installed/tightness/wear for every readable car part
  |<----- world-state msgs * n ---|   current state of every host-authoritative subsystem
  |                               |   (vehicles/climate 60-61, radio 66, police 82, stereo 84,
  |                               |   races 86-92, economy 93-99, world 101-109, crime 140-143,
  |                               |   race results 150-151 — see the message table)
  |<----- TimeSync ---------------|   clock + weather + calendar (also re-broadcast every 30 s)
  |<----- WalletState ------------|   shared wallet (also re-broadcast every ~2 s while balance changes)
  |<----- PassengerState * n -----|   current vehicle seat occupancy (also in join snapshot)
  |<----- GuestSpawn -------------|   host pose + optional last saved pose + needs; guest picks locally
  |<----- PlayerClothingState * n-|   current outfit of every other player (change-only otherwise)
  |<-----> PlayerNeedsReport -----|   guest -> host every ~12 s (needs sidecar)
  |<-----> SleepConsent * --------|   host sleep attempt -> guest accept/decline
  |<-----> PlayerDeath * ---------|   death report -> host event; permadeath wipes all clients
  |<-----> Chat / PlayerTransform / ItemTransform / FsmStateEnter ...
```

**Mid-session rejoin (PLAN.md §4.5):** when a guest disconnects, the host keeps their
`playerId` slot and writes pose (and needs, when reported) into `wintermp-guests.json`.
The same SteamID reconnecting gets the same `playerId`, a fresh world snapshot, and
`GuestSpawn` with last saved pose/needs — chat shows `reconnected` instead of `joined`.

Topology is a star: guests only talk to the host; the host relays chat and
transforms to other guests and is authoritative for all world state.

## Message ids

| Id | Message | Channel | Notes |
|---|---|---|---|
| 1 | HandshakeRequest | 0 | versions, catalog hash, player name |
| 2 | HandshakeResponse | 0 | accepted + playerId + hostPlayerName + sessionFlags (bit 0 = host permadeath enabled), or refusal reason; v264 appends clothingAdmission:u64 |
| 3 | Ping | 0 | nonce + sender time |
| 4 | Pong | 0 | echoes nonce |
| 5 | Disconnect | 0 | human-readable reason |
| 10 | Chat | 0 | senderPlayerId + text |
| 20 | PlayerSpawn | 0 | playerId, steamId, name; v264 appends clothingAdmission:u64 |
| 21 | PlayerDespawn | 0 | playerId, reason |
| 22 | PlayerTransform | 1 | playerId:uint8, seq:uint16, pos:3×float32, rot:4×float32, moveState:uint8; **v188 appends** hasSweat:uint8 (0/1), sweat:float32 (finite 0–100 when available, zero otherwise). **39 bytes including ID.** Since v53, relays reject non-finite/out-of-map positions, non-unit rotations, and unknown move-state bits before storing, relaying, persisting, or using the pose as proximity proof. Sweat shares authenticated, known-player, wrap-aware pose acceptance; fresh seated reports supply the current climate producer as described above. |
| 23 | PassengerState | 0 | playerId, vehicleId, seatIndex (0 front passenger, 1 rear right, 2 rear left, 255 none; taxi accepts only 0/2 in v249), seq (**appended v52**); re-broadcast every ~8 s while seated. The host accepts only an authenticated player's next sequence: an exit must carry vehicle id 0, while a new or changed seat claim must name an available discovered passenger anchor and be within 2 m of it from a fresh player pose. **v95:** keepalives for an already accepted exact seat verify the vehicle/seat still exists without rechecking world-space entry proximity (moving cars and delayed poses must not eject occupants). Duplicate/older requests are silently ignored; new rejected requests consume their sequence and clear canonical occupancy with a `SeatNone` broadcast to every peer, including the claimant, using the request sequence. A claimant applies a self-addressed correction only if it matches its latest request. Same-seat races still resolve by lowest player id; the winner's broadcast evicts any conflicting local/remote occupant, and the losing guest also receives a `SeatNone` with its last accepted sequence. Join snapshots contain only current accepted occupants. **v191:** death/respawn retire occupancy while retaining sequence history; dead players cannot claim or keep a seat, and observers ignore seat updates for dead players until respawn. |
| 24 | GuestSpawn | 0 | host -> joining guest after snapshot: host pos/rot, last saved pos/rot, flags:uint8 (bit 0 last position, 1 saved needs, 2 dirtiness, 3 BAC, v189 bit 4 body warmth), hunger/fatigue/thirst/urine/bodyTemp/stress/drunk/dirtiness/PlayerAlco:float32. The original 95-byte prefix is unchanged; v264 appends clothingPlayerId:u8, clothingAdmission:u64, hasSavedClothing:u8, clothingStage:u8, clothingType:u8, winterGarment:u8: **108 bytes including ID**. See v264 above for strict availability and local recipient admission. BodyTemp is native PlayerTemp; bit 4 requires bit 1, known values are finite including zero, unknown warmth is zero. Bit 0 is set only for returning guests known before connection. The last-position choice restores saved needs; absent globals defer known warmth until binding. New guests snap to the host as before. Clothing restores mod-owned state only, independently of position choice. |
| 25 | PlayerNeedsReport | 0 | guest -> host every ~12 s: playerId:uint8, hunger/fatigue/thirst/urine/bodyTemp/stress/drunk:float32, seq:uint16, dirtiness:float32, HasDirtiness:uint8, PlayerAlco:float32, HasAlco:uint8, **v189 HasBodyTemp:uint8**. **44 bytes including ID.** BodyTemp now reads native PlayerTemp; its new availability is 0/1, known warmth is finite including zero, and unknown warmth is zero. Reports retain authentication, finite-value checks and wrap-aware ordering. Missing optional globals do not block other needs. The host persists 18-column CSV needs rows with independent optional dirtiness/BAC/warmth; legacy air-temperature samples are discarded as described above. |
| 26 | SleepConsentRequest | 0 | host -> all guests when host enters a sleep/time-skip FSM state: requestId, initiatorPlayerId |
| 27 | SleepConsentResponse | 0 | guest -> host: requestId, playerId, accepted (byte 0/1) — first answer from each requested, connected guest; any decline cancels immediately; 90 s timeout, host withdrawal or no remaining requested guests also cancels |
| 28 | SleepConsentResult | 0 | host -> all guests: requestId, accepted (byte 0/1) — dismisses guest prompt; on accept guests reset fatigue locally; host sends ACTIVATE to proceed and pushes TimeSync when the sleep FSM reaches Calc rates |
| 29 | PlayerDeathReport | 0 | any -> host: playerId, cause, seq — host rebroadcasts PlayerDeathEvent. Cause bytes are append-only (`DeathCause`): 0 unknown, 1-13 fatigue/hunger/thirst/urine/stress/run-over/drown/fire/electrocute/hypothermia/murder/train/accident, **14-21 appended v32**: sewage/carbon-monoxide/PTO/cutter-blade/jail/piss-TV/burn/smoking (the death FSM's remaining cause bools) |
| 30 | PlayerDeathEvent | 0 | host -> all: playerId, cause, flags (bit 0 = permadeath group wipe) — hides avatars; wipe triggers local Systems/Death on every client (accident maps to the RUNOVER screen — State 3 has no crash transition; burn → FIRE; smoking → FATIGUE) **v191:** terminal passenger-seat retirement; permadeath clears all seats and marks all connected remote players dead. Seat claim history is preserved. |
| 31 | PlayerRespawn | 0 | respawning player -> all: playerId, pos, rot, seq — non-permadeath only; avatar visible again **v191:** retires residual passenger occupancy before applying the respawn pose; seating requires a fresh claim. |
| 32 | PlayerClothingState | 0 | any -> host -> other guests: playerId:u8, clothingStage:u8, clothingType:u8, v79 winterGarment:u8 (0 none/1 jacket/2 coverall); v264 appends admission:u64, sequence:u32. **18 bytes including ID**. Owner-authoritative reports are authenticated, range-checked, strictly sequenced and persisted by the host. Never written onto the owning player's native FSM. The host also sends each other connected player's accepted clothing to a joiner after GuestSpawn, preserving that owner's token/sequence. Mod-owned resume data is not native warmth or garment rendering acceptance. |
| 40 | FsmStateEnter | 0 | netId + state name; receiver replays via injected MP_* global transition (doors, ignitions, vehicle controls, car parts Install/Remove, shop Buy/CashRegister Purchase/Cashier/Add, Peräpörtti restaurant Cashier/State 1, inspection Pay, post-package Close box/Remove order, post-office Spawn, phone-order Spawn package, Fleetari Pending cost/State 3, service brochure Fleetari 2, engine run/stall on SORBET/CORRIS Starter FSMs, and **v51** home/yard/apartment shower tap plus valve ON/OFF controls). **v114:** part Bolted/Unbolted/Stop are derived from authoritative tightness, excluded from state replay/snapshots. |
| 41 | FsmRawEvent | 0 | guest -> host: netId + whitelisted TIGHTEN/UNTIGHTEN intent on a ready fitted bolt; authenticated, fresh pose within 3 m; executed immediately on host, never queued or relayed to guests. Unsupported at-limit timing adjustments are rejected. Host bolt results use 44; v215 also accepts validated cylinder-head valve turns (loose or fitted), whose float results use 206. |
| 42 | ItemTransform | 1 (vehicle/final: 0) | itemId, ownerPlayerId, seq, flags, pos, rot [, velocity when flags bit 3] — items *and* vehicles |
| 43 | TimeSync | 0 | host -> guests: hour (1-24), minutes, forecast temps, snowing, forecast index, daysPassed, dayOfWeek (0=Mon..6=Sun, 255 unknown) |
| 44 | BoltState | 0 | netId:u32, boltTightness:u16 (0–8), screwInt:u16 (reserved zero), then v114 partTightness:f32; host absolute result restores native save array/pose/parent total. Guest report requests host correction only. |
| 45 | PartState | 0 | netId, flags (bit 0 installed), legacy tightness:u8, legacy wear:u8; v107 appends tightnessValue:f32, wearValue:f32. Host observations overwrite guest Data scalars without unit clamping. Guest reports request a fresh host observation after native interactions settle; their values are never applied on the host. |
| 46 | ItemDespawn | 0 | itemId — pickable eaten/destroyed (Destroy, Check drink → State 2/5/6); receivers remove their local rigidbody. Since v105, host-authorized removal is retained for the session even before the body exists, preventing deferred creation or replay from reviving it. Standard packages run native GARBAGE on the host; v109 guests remove their isolated replica while preserving their local saved boxes. |
| 47 | WorldStateChecksum | 0 | host -> guests every ~20 s: walletCrc, worldCrc (FSM + part + bolt vars), itemCrc (resting pickables), vehicleCrc, seq — guests compare and may request soft resync. **v96 vehicle fold:** vehicles sorted by id, excluding local/remote ownership and unavailable vehicle systems. Starting at `StableHash.OffsetBasis`, combine each id, engine/accessory/blinker/hazard flags, fuel byte, concrete damage mask, **v197 condition availability byte**, tire-pressure byte, drivetrain-damage byte, wheel health FL/FR/RL/RR bytes, then wheel puncture/rim flags, using `StableHash.Combine` for each value. Unavailable condition fields and their wheel flags contribute zero; the mask distinguishes unknown from known zero. Guests intersect native readiness with an eligible accepted or approved parked mask, preserving native drift checks only for declared fields. **v124 damage:** the host reconciles its live fitted-part wear; guests use their latest accepted host condition without reading native saved mounts. Missing damage/condition systems contribute zero. RPM, climate, continuous part wear, binding bookkeeping, ownership ids and stream sequences are excluded. Reads do not advance send sequences or change baselines. A vehicle mismatch requests the existing complete vehicle resync (state, climate, damage and condition). |
| 48 | WorldResyncRequest | 0 | guest -> host: flags (bit 0 wallet, bit 1 FSM states, bit 2 parts, bit 3 bolts, bit 4 items, bit 5 vehicles), checksumSequence — flags must name at least one listed group; host replies with targeted snapshot chunks and accepts at most one request per guest every 15 s |
| 49 | WorldObjectStateRequest | 0 | guest -> host: netId — host replies with the object state it has (final ItemTransform, VehicleState/Climate, FsmStateEnter, PartState, or BoltState); v107 part replies include both the known FSM state and native scalar state; guests auto-request after a pending FSM event expires (5 s cooldown per id), while the host admits at most four requests per second per guest |
| 50 | HeatSourceState | 0 | host -> all (v28): sourceId (scene-path hash of the source container), flags (bit 0 = lit/embers), fuel (0-255 firewood), heatOutput (0-255), saunaTemp (sauna heat ×100, ushort; 0 for non-sauna). Host-authoritative shared state for the cabin woodstove (`CABIN/Cabin/woodstove/Fireplace`), sauna kiuas (`COTTAGE/Stuff/Sauna/Stove` — `SaunaHeat`/`StoveHeat`), and cottage/living-room fireplaces. Broadcast on change + ~20 s keepalive from whoever hosts; guests write the values back onto their local FSMs so each client's own position-derived body-temp calc warms consistently |
| 51 | HeatSourceIntent | 0 | guest → host: sourceId, action (0 light, 1 feed wood, 3 steam), authenticated player and sequence; fresh nearby per-source intent. **Action 2/grill is retired in v248**; package conversion requires SausageOpenIntent 243. |
| 52 | ItemSpawn | 0 | host → guests: containerNetId (uint32), epoch (uint16), ownerPlayerId (byte), stateName (string), count (byte, ≤32), entries (netId uint32, templateName string, position vec3, rotation quat), flags (byte: bit0 replay, bit1 catalog trophy factory), offerSequence (uint16, zero for bags in v120). Exact native host outputs; duplicate receipts cannot create another item. Missing templates retry, and join/resync replays refresh live entries. |
| 53 | SpawnIntent — retired v120 | — | Former guest spill offers. Ignored by the session dispatcher; ID remains reserved. Guests use BagOpenRequest (191). |
| 54 | FluidContainerState | 0 | owner -> host -> others: tracked fuel-container item id, owner player id, sequence, flags (bit 0 pouring), fuel level, capacity. The host accepts and relays a guest update only while its item-transform ownership still names that guest, its owner-local sequence advances, and flags/level/capacity are finite and in range; rejected packets never reach other peers. |
| 55 | WorldProgressState | 0 | host -> guests: compact scalar mirror of a host-owned M8 system. `kind` 1 = classifieds (`phase` job stage; primary delivered; secondary sheets; tertiary day; value salary); 2 = factory (employment stage; empty/total packages; paychecks; worked minutes); 3 = Marketti magazine (layout type; day/index/issue). |
| 56 | JobSiteState | 0 | host -> guests: stable FSM-path id, kind, flags, sequence, primary and secondary values. Kind 1 sewage site: bit 0 `Called`, `ShitLevel`, `BasePrice`; kind 2 firewood site: bit 0 `Order`, `Surplus`, `Penalty`; **kind 3 (v35) GIFU sewage truck**: bit 0 `PumpRunning`, bit 1 `HoseAttached`, bit 2 `HoseInShit`, bit 3 `Sucking`, `ShitLevel`, `PumpEfficiency`. Every registered site and the truck pump are included in join snapshots. | **v67: kind 4 = farm (Primary=int JobStage, Active=Done).**
| 57 | MailOrderState | 0 | host -> guests (v36): persisted mail-order record: kind (1 AMIS, 2 Yellow Pages, 3 hidden Yellow Pages), flags (bit 0 active, bit 1 consumed, bit 2 saved position), seq, price, wait time, price integer, and the game's three opaque saved package strings. It is sent on change and in join snapshots so a pending host order retains its delivery descriptor on every peer. |
| 58 | MailOrderIntent | 0 | guest -> host (v37): player id, exact `OrderAMIS`/`OrderYP` data-FSM id, kind, flags, seq, price/wait/price-int, and the three selected-listing package strings. The host authenticates player id, accepts only that player’s next monotonic order sequence, holds the record for 5 seconds, and applies it only when the same player’s immediately following `PurchaseIntent(PAYMENT)` targets that exact order FSM; otherwise the payment is rejected. This makes the host spawn the guest’s actual generated phone selection rather than its own divergent local listing. |
| 59 | InspectionState | 0 | host -> guests (v38; appended v40): flags (host inspection record: inspected/stamp/museum registration/pass/issued stamp/museum order), seq, next inspection day, renewal intervals, two bitmasks with 38 result-sheet check slots (36 bind on the current game build — `ShockRL`/`ShockRR` have no Results-FSM bool there and their bits stay 0; the slots are kept so a future build adding rear-shock checks binds without a bit reshuffle), then plate availability flags and the host-generated standard/museum plate strings. Peers write those exact generator values and activate the matching physical plate pairs; no client reruns the random plate FSM. Sent on change and in join snapshots; inspection order/payment already use the host purchase path, so the host car’s evaluated result is authoritative. |
| 60 | VehicleState | 1 (final OFF/repair/reconciliation: 0) | vehicleId, ownerPlayerId, seq, flags (bit 0 engine on, bit 1 ACC/electrics on, bit 2 blinker left, bit 3 blinker right, bit 4 hazard), rpm, speedTenthsKmh, fuelLevel (0-255), coolantTemp (0-255 → 0-120 °C), **gear** (v63: gear+1, 0=reverse/1=neutral), **torqueAvailable, engineTorque** (v168: native finite torque, zero when unavailable), **movementSpeedAvailable, movementSpeedTenthsKmh** (v169: actual movement magnitude, zero when unavailable), **differentialSpeedAvailable, differentialSpeed** (v202: finite signed native differential speed, zero when unavailable), **handoffTemperatureAvailable, handoffTemperature** (v213: exact signed Celsius in [-100,300], zero when unavailable) — 35 bytes including message ID. **v126:** ~4 Hz from the established simulator, or host for an unowned vehicle; guest reports require current ownership. Per-vehicle/per-sender sequences reject stale reports before application and relay. Receivers replay Electricity ON/OFF FSM, push rpm/speed/fuel/coolant into gauge variables (CORRIS angle gauges included), apply blinker/hazard stalk/events + hazard button replay, and synthesize engine audio (pitch from RPM), stopping after 2 s without packets. **v167:** the host supplies accepted guest RPM to seven native Pressure/Wearing/Oil inputs; native filtering, contamination, host heat, cadence and fourteen part-wear writers remain authoritative (see above). **v168:** native host HeatGeneration also uses the accepted RPM/torque pair; host friction, heat-rate clamps and temperature progression remain authoritative. **v169:** native host Cooling uses movement speed in its airflow and stationary-temperature checks; wheel speed remains presentation data. **v170:** the same cooling group supplies RPM to pump circulation, mechanical fan and running leak checks. **v175:** native host Electrics also uses accepted RPM for its running/charging gate, charge rate and battery drain, retaining native host part, temperature and charge writers. **v171:** Corris ignores this message’s coolant byte for presentation and uses host state 197; other cars retain the coolant byte. |
| 61 | VehicleClimate | 1 (snapshot/repair/final: 0) | vehicleId:uint32, ownerPlayerId:uint8, seq:uint16, frost:uint8 (interior), flags:uint8 (window heater=1, glass defrosting=2, player in cabin=4, **v190 cabin temperature available=8**), heaterTemp:uint8, heaterBlower:uint8, heaterDirection:uint8, fog:uint8 (**retired v187**, capture 0, ignored on apply), cabinTemp:uint8 (-40 to +40 °C), ice:uint8 (windshield; v28), then **v184** iceSideLeft:uint8, iceSideRight:uint8, iceDoorLeft:uint8, iceDoorRight:uint8, iceRear:uint8, iceMask:uint8, then **v221** parkingBrakeAvailable:uint8 (0/1), parkingBrake:float32 (finite normalized 0–1; zero when unavailable). **28 bytes including ID.** Exterior cutoffs map 0 iced to 255 clear, independently for each available pane (mask bits 0–5 in that order); unknown bits or nonzero unavailable values are invalid. ~2 Hz; join/repair use the snapshot sentinel; LateUpdate pins available panes during the existing hold. Interior frost and exterior cutoffs remain independent. **v185:** established vehicle owner only; parked host fallback, per-sender live dedup, reliable final before release, accepted-state snapshots and no stale relay. Control intents remain separate. **v186:** cabin occupancy includes accepted seats but never writes native local PlayerIn; seat presence cannot grant driving/stream ownership. **v187:** Frost drives native/material alpha only; tint, interior shader cutoff, SweatRate and divided Temp remain native. **v190:** bit 3 marks finite native cabin temperature; missing sources clear it and capture byte 128. Unavailable bytes are ignored by presentation and passenger body input. Current seated passengers use available current-owner cabin input, with native fallback after expiry/exit. |
| 62 | VehicleCargo | 1 (empty set: 0) | vehicleId, ownerPlayerId, seq, count (≤24), entries (itemId, localPos, localRot in vehicle-root space) — sent by the vehicle's transform owner at the vehicle send rate while it moves; the owner's physics simulates the cargo and streams its live vehicle-local poses. Each packet is the COMPLETE cargo set: receivers pin listed items kinematically (composed against their own smoothed vehicle pose, colliders untouched) and release tracked items that are no longer listed, seeding them with the pin's observed world motion (a rider inherits ~the car's velocity, an item merely shielded at its resting pose stays at rest; the car's velocity is the fallback when no fresh sample exists). The non-empty → empty transition is sent reliably; per-vehicle wrap-aware seq dedup per owner |
| 63 | VehicleFuelIntent | 0 | guest -> host (v39): vehicle id, Peräpörtti nozzle FSM id, authenticated player id, sequence, requested tank level (0-255 of that tank's capacity). A guest emits it only while its local nozzle is actively dispensing into a nearby non-owned vehicle. The host accepts only monotonically increasing, rate-limited fuel while the player's fresh pose, the static nozzle, and the stationary vehicle are co-located; it writes the real host tank and returns a reliable `VehicleState` reconciliation. |
| 64 | VehicleDamage | 0 | **v124 host -> guests only**, retaining the v91 layout: vehicleId (uint, nonzero), ownerPlayerId (byte, always 0), damageMask (uint), seq (ushort), knownPartsMask (uint), wear (16 floats, fixed length). Slots 0–4 bearing1–5, 5 crankshaft, 6 headgasket, 7–10 piston1–4, 11 oilpan, 12 timingbelt, 14 block. Slots 13 (SEIZE) and 15 (CAMFAIL) remain retired random selectors; both masks reject unknown bits. Known wear must be finite and its damage bit must match wear ≤ 0. The guest retains the complete host observation without native writes or event replay. Per-vehicle host sequence only; ownership changes cannot reset it. Change broadcasts plus 15 s keepalives, joins and targeted vehicle resyncs include healthy parts too. Authenticated guest reports are rejected before dispatch. See engine damage v124 below. |
| 65 | VehicleCondition | 0 | Current owner -> host -> peers (**v197**): vehicleId, ownerPlayerId, seq, tirePressure (byte, bar×100 / native kPa), drivetrainDamage (byte), health FL/FR/RL/RR (bytes), flags (bits 0–3 puncture, 4–7 rim), availability (byte: pressure=1, drivetrain=2, FL=4, FR=8, RL=16, RR=32; reserved bits rejected). 15-byte payload / 17-byte packet. Zero mask withdraws all fields; absent fields and their wheel flags are ignored, known zero remains applicable. Authenticated current owner only; unowned claims and stale/duplicate reports never relay. Histories are per vehicle/sender through handoffs. Seq 65535 is host-only snapshot, excluded from live counters/history; accepted only on unowned/host-owned cars. Current guest-owned snapshots copy accepted reports; parked snapshots read native state without advancing live baselines. Reliable final condition precedes final item release. Ordinary owner sends are change-based with 20 s keepalive. The v198 guest observer gearbox damage reader consumes the available accepted/approved parked drivetrain byte without changing saved data. In v199, a guest physics claim retains available accepted tyre/gearbox inputs for native reads and outgoing capture, bound to claimant/body/local ownership until release or replacement; pressure and discrete flags remain native. v209 supersedes the registered CORRIS native tyre-health input with host message 205; these byte fields retain their layout. Delegated tyre wear, full physical repair/failure effects and parked pressure publication remain incomplete (roadmap 2.2). |
| 66 | CarRadioState | 0 | host -> guests (**v81**): seq, radioId (0 corris, 1 sorbet), tune (float), volume (byte, knob ×100). Host owns the in-car radio tuning + volume; guests apply. `tune` replaced a byte `channel` in v81 — no radio Knob FSM has a float named Channel (the only Channel is a *string* on the CD player), so the old field bound null and the station never synced. Bound to `StockRadio0/ButtonsRadio/Volume :: Knob`, which carries both `Tune` and `Volume`. Unquantized: the per-station windows are not knowable from the catalog dump. In the join snapshot. |
| 80 | WalletState | 0 | money (float mk), seq (ushort), bankBalance (float), netIncome (float), flags (byte; bit0 bank available, bit1 income available). The final three fields were appended in **v90**. Host -> guests on change, every ~2 s, on join and wallet resync. Binds the verified globals `PlayerMoney`, `PlayerBankAccount`, `PlayerNetIncome`. Missing optional bindings never block cash or overwrite a valid remote balance with zero. Guests retain the latest accepted state for delayed bindings and local drift correction; wrapping sequences reject stale state. Wallet CRC now includes cash, bank and income rounded to whole mk. Guest bank interest/ledger FSM is suppressed for the session and restored on exit. |
| 81 | PurchaseIntent | 0 | guest -> host only: playerId, netId (Buy/CashRegister/Use/Data/Button buy FSM), eventName (USE/PURCHASE/DEPURCHASE/PAY/PAYMENT/BUY/CLICK/**ACTIVATE**), seq — guest aborts local buy guard and restores wallet; host fires the event and broadcasts resulting FsmStateEnter + WalletState. **Since v48**, the host accepts only the authenticated player’s next monotonic sequence while that player has a fresh pose within the buy target’s interaction radius, and only for a catalogued entry-guard event; unknown, distant, non-entry, duplicate, and stale packets do not reach the FSM. `ACTIVATE` is used by the host-validated Fines record (v41) to enter its normal money check without trying to replay a remote player's police collision. **v50** adds the exact `PriceMoneyRace` and `PriceMoneyRally` `USE` paths, so race prize collection executes only on the host and converges through the shared wallet. |
| 82 | PoliceState | 0 | host -> guests (v41): target player id, offence flags, active flag, sequence, stable checkpoint id, and host-validated fine. The host writes the fine to its own Fines record and peers mirror that durable record; payment still goes through the normal guarded `PurchaseIntent` path, so the shared wallet remains host-owned. When the host's Fines price clears (fine paid/reset), it broadcasts the record once more with the active flag cleared and drops it from join snapshots, so late joiners never inherit a phantom fine. A guest advances its report-dedup baseline only on the host's accepting echo, re-sending each interval until then. Offence bits: alcohol, fuel, helmet, inspection, radar, registration plates, seatbelts, speeding (low to high bit). |
| 83 | PoliceIntent | 0 | guest -> host (v41): player id, locally observed checkpoint offence bits, sequence, stable checkpoint id, and the locally generated fine. This is a report rather than authority: the host accepts only a fresh authenticated player transform close to that exact known checkpoint and a nearby vehicle currently delegated to that player; stale/replayed sequences, invalid flags, and non-finite/out-of-range fines are discarded. |
| 84 | HomeStereoState | 0 | host -> guests (v42): fixed home stereo power/channel flags, sequence, volume and bass. These are the durable audio/electricity inputs, rather than fragile presentation FSM states; sent on host change and in join snapshots. |
| 85 | HomeStereoIntent | 0 | guest -> host (v42): authenticated player id, requested power/channel flags, sequence, volume and bass. The host accepts only finite normalised values while the sender's fresh pose is within 6 m of the exact home-stereo switch, then writes and broadcasts the authoritative scalar state. |
| 86 | RallyState | 0 | host -> guests (v43, extended **v101**). Wire order: playerId/stage/phase/checkpoint (bytes), sequence (ushort), elapsedCentiseconds (uint); appended revision (uint), reportToken (ulong), reportSequence (ushort), flags (byte: bit0 HasReport). Stage is 1–3, checkpoint 0–6, phase 1 racing or 2 finished; phase 0 remains reserved/decodable but is not published or applied. Finished requires checkpoint >0. HasReport requires a nonzero token; otherwise token and reportSequence must be zero. Other flags and player id 255 are invalid. Revision is per player, with forward uint delta 1..2³¹−1, including wrap; the old sequence remains on the wire but no longer gates application. HasReport echoes the most recently accepted guest command for that record; snapshots and racing keepalives can acknowledge it too. An exact duplicate command returns current progress with the same acknowledgment, without replaying the crossing. Elapsed time uses the host clock and freezes at finish, including a finish at time zero. Sent on acceptance/duplicate acknowledgment, every second while racing, once for a host-local finish, and in join/resync snapshots. Snapshots do not consume an unpublished finish. Packet length 27 bytes including id. |
| 87 | RallyIntent | 0 | guest -> host (v43, extended **v101**). Wire order: playerId/stage/checkpoint (bytes), sequence (ushort); appended reportToken (ulong, nonzero, freshly generated per guest connection). Session authenticates playerId. Stage 1–3, start 0 or checkpoint 1–6 within the completely bound native stage. Guest retains up to 21 crossing edges in checkpoint order, sends only the head, and retries the exact command every 0.25 s until 86 echoes player/stage/checkpoint/token/sequence. A fresh native start abandons old pending edges; preexisting flags at initial bind/reconnect are only a baseline. Ten seconds without acknowledgment clears pending reports and asks the player to restart the stage. Host checks marker proximity from its own recent driver evidence (below), then strict next-checkpoint progression and a forward ushort delta 1..32767. Only accepted reports advance the sequence/token baseline. Same sequence with different stage/checkpoint, or another token before readmission, is rejected. Admission drops cached report identity and proximity evidence while retaining the race and elapsed clock. Packet length 15 bytes including id. |
| 88 | IceRaceState | 0 | host -> guests (v44): player id, inferred time-trial/lap-race start mode, accepted checkpoint phase, completed laps, sequence and host-clocked elapsed centiseconds. Sent on accepted markers, every second while active, and in join snapshots. |
| 89 | IceRaceIntent | 0 | guest -> host (v44): authenticated player id, marker (start, checkpoint 1, checkpoint 2, finish) and sequence. The host infers the start mode only from the driver’s proximity to one of the two fixed start/finish markers, then requires checkpoint 1 → checkpoint 2 → that mode’s finish marker for each lap with fresh pose and nearby delegated vehicle validation. |
| 90 | IceRaceEventState | 0 | host -> guests (v47): grid-ready/on-track/player-registered flags, sequence, car-limit/current-car/on-track counts, heat stage, lane, final race distance then qualifying race distance (wire order), starter count, event time, selected car id/reference, then race stage. This mirrors `RACES/ICERACE/TrackFunctions :: Data` plus `TrackFunctions/LINEUPS :: Logic` on change and in join snapshots, so registration/grid/heat presentation uses the host event configuration. |
| 91 | IceRaceResultsState | 0 | host -> guests (v46): sequence plus up to six ordered result rows (driver name, number, model, UA). The rows come from the host-generated `Stats/ResultsRace/Data/{0..5}` records and are mirrored on change and in join snapshots so every result board shows the same ranking rather than each client rerunning leaderboard generation. |
| 92 | RadiatorThermostatState | 0 | host -> guests (v54): thermostat Knob FSM `netId` + its game-owned `Rotation` float after the host applies a turn. Guests emit only the +/- knob `FsmStateEnter` intent; the host applies it and broadcasts this absolute settled value so receivers and joiners *set* the rotation rather than re-applying a relative increase/decrease. Sent on change and in join snapshots; a state whose target Knob FSM has not registered yet is held as a pending apply until it does. |
| 93 | GamblingState (retired) | 0 | Retired in **v98**; no current sender or game-state handler. Old layout stays decodable for diagnostics: machineId (uint), kind/flags (bytes), credit (float), bet/V1/V2/V3 (bytes), payout (int). Slots moved to 164–166 in v92; Ventti table observations now use 174, property keys/access use 173. Never reuse id 93 or its retired fields. |
| 94 | GamblingIntent (retired) | 0 | Retired in **v99**, with no sender or game-state handler. Diagnostic layout remains machineId (uint), action (byte), playerId (byte), sequence (ushort). Slot actions 0–6 and Ventti actions 7–11 remain reserved forever. Replaced by 165 for slots and 176 for Ventti. |
| 95 | UtilityBillState | 0 | Host → guests (**v230**): meter:u8, unpaidBills:f32, flags:u8, revision:u32; phone meters append eight float32 usage/tariff inputs (see v230). Electricity remains 12 packet bytes; phone is 44. Bit 0 = effective supply/PhonePaid; bit 1 = envelope visible; electricity-only bit 2 = MainSwitch. Host supplies complete invoices; guest timers pause after load and restore on disconnect. Revisioned payment/receipt 211/212 support all four meters. |
| 99 | ApplianceState | 0 | host -> guests (**v75**, grown **v85/v233**): applianceId:u32, kind:u8, flags:u8 (fire 1, fuse 2, light 4, smoke 8), legacy heat1–4:u8, fireCount:u8, firePlate:u8, stoveRevision:u32, stoveHeat1–4:f32, stoveRotation1–4:f32, grillMask:u8, burnMask:u8. **50 payload / 52 packet bytes.** Full native heat, controls and triggers use the revisioned v233 extension; see its bounds/reconciliation contract above. Ignition remains edge-carried; the first count seeds the baseline without replaying an old fire. In the join snapshot. |
| 96 | LotteryDrawState (retired) | 0 | Retired in **v102**; diagnostic decoding only, no live sender/game-state handler. Legacy layout remains round (int), nationalPot (int), winningNumbers (string), flags (byte). The old string binding `UTNational7` is the save key `Lotto7`, not winning numbers. Never reuse id 96. Replaced by 180. |
| 97 | FleetariOrderState | 0 | host -> guests (**v62**): flags (bit0 order active), seq, jobTotalCost (float), carPaintColor + rimPaintColor (packed RGBA), jobs/orderCode/paintCode/axleCode/tireCode (strings). The shared repair-shop order record; broadcast on change + join so observers/joiners agree. The host's Work FSMs apply it to the host-owned shop car. |
| 98 | FleetariOrderIntent | 0 | guest -> host (**v62**): the exact `OrderFleetari` record the guest configured (same fields + playerId), captured when it confirms so the host pairs it to that guest's next `PurchaseIntent(PAYMENT/PAY)` and applies the actual jobs. Mirrors `MailOrderIntent` capture-and-pair; payment rides the existing catalogued OrderFleetari buy. |
| 100 | NpcTransform | 1 (final: 0) | netId, seq, flags, pos, rot — host-only stream for TRAFFIC/, NPC_CARS/, HUMANS/, and ice-race opponent rigidbodies; guests pin kinematic and ease toward pose (also freezing race-driving FSMs); distance tiers ~8 Hz (≤80 m), ~3 Hz (≤200 m), off beyond; moving bodies still stream until a reliable **final** at-rest packet. Also carries the transform-driven *scripted movers* (no rigidbody; guest AI FSM frozen while the stream is live): the moose, the hitchhiker, Reijo the janitor, and the farm-job farmer's Walker (the last so a guest's payday press passes the host's PayMoney proximity gate). Flags: bit0 final, bit1 dead (moose corpse, see id 110). |
| 101 | FleaSaleState | 0 | host → guests: sequence, proceeds, rental days/flags, table revision, native week price. See v236. |
| 102 | FleaSaleIntent | 0 | guest → host: action, player, sequence, revision, weeks. Paid checkout (2) or proceeds collection (3); 0/1 rejected. See v236. |
| 103 | TaxiJobState | 0 | host -> guests (**v65**, grown **v87**): seq, jobStage (int), money (float — employment payday calculation, `TaxiFunctions :: Payments`), kmsDriven (float), flags (bit0 employed), fareCost (float — finalized `TaxiWalker :: Logic` Cost, populated at payment). In the join snapshot. Native audit 2026-09-14 corrects earlier documentation: the live meter is `Tripmeter.Price`; accepting the hand offer sets Paid and credits `Tripmeter.IncomeTotal`, with wages later reaching the bank. This message does not include the live meter, activation, boarding/receipt presentation or income totals. |
| 104 | WorldScalarsState | 0 | host -> guests (**v86**): seq, scrapPriceMKkg (float), scrapChange (float), primeInterest (float), uncleStage (byte), conlineNumber (int), flags (bit0 GIFU key). Misc host-owned world scalars that each re-roll or progress per-client: the daily scrap-metal price, the bank prime interest rate (the *rate* half of the bank gap — the balance itself is a PlayMaker global, gated on a fresh dump), and `Database/Keys :: PlayerKeys` progression. Guests write the values back; their own daily re-rolls get stomped on the next tick. On change + 30 s keepalive + join snapshot. |
| 105 | BrewState | 0 | owner -> host -> peers (**v66**): itemId (tracked bucket), ownerPlayerId, seq, flags (bit0 finished, bit1 lid on), alcohol (float), brewTime (float). Kilju fermentation carried like `FluidContainerState` — whoever holds the bucket streams it, others apply; the stream also carries ingredient-add effects. Since v82+ a guest lid-flip on a resting bucket claims the bucket so the interaction streams (see KiljuSync). In the join snapshot. |
| 107 | WelfareState | 0 | host -> guests (**v68**, grown **v84**): seq, unemployDays (int), paidAmount (float), weekly (float), flags (bit0 claiming, bit1 evicted), rentDebt (float), rentPerWeek (float), asumistukiPerWeek (float — the last three appended v84). Host owns the whole `Systems/Expenses` record (Kela claim + weekly rent debit + housing benefit); guests apply the scalars, suppress their own Rent/Livingsupport FSMs for the session (their local weekly ticks are throwaway divergence — a guest plays in the host's world), and replay the terminal `Kick out` eviction state once when bit1 appears (furniture destruction + relocation happen everywhere). In the join snapshot. |
| 106 | HitchhikerState | 0 | host -> guests (**v69**): seq, drunkStage (int), movingStage (int), money (int), flags (bit0 paid, bit1 KiljuMurderer, bit2 suicide, bit3 active). Host owns the hiker variant + stage; guests apply. Body pose streams over NpcTransform (ScriptedMover). In the join snapshot. |
| 108 | PhoneCallEvent | 0 | host -> guests (**v76**): callId (ushort, monotonic), topic (string). Host decides an incoming call and broadcasts it; guests set the phone Topic + fire the matching ring event. Discrete one-shot (not in snapshot). |
| 109 | PissAreaState | 0 | host -> guests (**v263**): original seq + five *20 bytes; appended epoch, revision and per-connected-player admissions/high-water. Absolute native stain transforms, including join snapshots. |
| 269 | PissAreaIntent | 0 | authenticated guest -> host (**v263**): epoch, observed revision, sequence, admission, actor, area, native action kind and bounded contribution. |
| 110 | NpcDeathReport | 0 | Guest → host: mover netId:u32, playerId:u8, sequence:u16. Authenticated fresh live reporter within 150 m. Since v228 the moose CarHit entry sends this before guest native destruction, retries for five seconds, and the host runs native death once. Independent MooseCorpseState (214) carries the surviving ragdoll; see v228. |
| 120 | WorldSnapshotRequest | 0 | guest -> host once its first world scan completes; carries the guest's id hash (diagnostic only). Host accepts at most one request per guest every 10 s. |
| 121 | WorldDoorSnapshot | 0 | host -> guest: (netId, stateName) pairs for doors/ignitions/controls/starters the host saw change; chunked (≤60/message) |
| 122 | WorldItemSnapshot | 0 | host -> guest: (itemId, pos, rot) for every item/vehicle; chunked (≤40/message); unknown ids are parked until scanned. Since v126 targeted vehicle resync also uses this pose-only message, preserving current live ownership instead of sending a host final ItemTransform. |
| 123 | WorldBoltSnapshot | 0 | host -> guest: count:u16, complete (netId:u32, boltTightness:u16, screwInt:u16) entry block, then v114 partTightness:f32 for each entry in order. Every ready fitted bolt, including zero; ≤80/message; unready IDs wait for binding. |
| 124 | WorldPartSnapshot | 0 | host -> guest: count:u16, legacy (netId:u32, flags:u8, tightness:u8, wear:u8) entries; v107 appends one (tightnessValue:f32, wearValue:f32) pair per entry, in the same order, after the complete legacy block. All readable car parts, including zero values; ≤80/message; delayed bindings retry. |
| 125 | WorldItemDespawnSnapshot | 0 | host -> guest: itemIds consumed/destroyed during this session; guests retain terminal removals and delete existing or subsequently scanned/materialized copies; chunked (≤80/message). Included in join and, since v105, item-group resync before replay manifests. |
| 140 | WantedState | 0 | host -> guests (**v70**): seq, manslaughter/attemptedManslaughter/policeEvasion/trafficFatality/daysFines/sentence/daysInJail (int), flags (bit0 cousin). Host owns the shared group wanted level; guests apply. In the join snapshot. |
| 141 | CrimeReport | 0 | guest -> host (**v70**): playerId, seq, crimeType (0 manslaughter..4 daysFines), delta (int, ≤32). A guest whose local `PlayerWanted` counter rose reports the delta; host validates identity + monotonic seq and adds it to its authoritative counter. |
| 142 | JailState | 0 | two-way (**v71**, reshaped **v83**): seq, daysLeft (int), sentence (int), flags (bit0 jailed), jailedPlayerId (byte, 255 = nobody; appended v83). The arrest→jail flow is offender-local, so the day-countdown runs ONLY on the jailed client — that client owns the record: while its local DaysLeft is positive it sends this guest → host (change + 5 s keepalive + one 0-report on release); the host validates the authenticated sender IS the claimed player, adopts the record (30 s TTL against a vanished reporter) and relays it host → guests with its own sequence. With nobody or the host jailed, the host broadcasts its own FSM. Non-jailed clients write DaysLeft for presentation; the jailed client ignores broadcasts about itself. Confinement position rides the player transform stream. In the join snapshot. |
| 143 | PursuitState | 0 | host -> guests (**v72**): seq, flags (bit0/1 cop car 1/2 chasing, bit2/3 cop car 1/2 siren). Host owns the pursuit; guests apply the sirens (lights) **only** — the chase bits are observability, deliberately not mirrored onto the guest's CopPassenger FSM (that would make each guest raise its own duplicate fine). Cop-car pose streams over NpcTransform. In the join snapshot. |
| 160 | HockeyBettingState | 0 | host -> guests (**v88**, appended **v104**): legacy scalar prefix followed by gamesPlayed, upcoming/previous pairings, six 1/X/2 odds tables, six result symbols and odds, scores and standings text. Whole completed boards broadcast on change + 30 s keepalive + join. See Hockey v104 below; this does not authorize Megaveto ticket payments. |
| 161 | ApplianceFireReport | 0 | guest -> host (**v89**): applianceId, plate (1-4), playerId, seq. The sender's own oven sim rolled an ignition — the FireHazard RNG runs per-client even over synced heats, so without this a guest's house fire stayed invisible to everyone else. Host validates the authenticated sender + monotonic per-player seq (reset on rejoin), then replays the plate's ignition-commit state on its authoritative oven; the shared fire streams back via id 99's fireCount. Guest-side replays of the host's own ignition are suppressed from re-reporting (echo guard), and reports pace at one per 30 s per oven. |
| 162 | BankTransferIntent | 0 | guest -> host (**v90**): playerId (byte), sequence (ushort), amount (signed int16). +100 deposits one note; -100/-200/-300/-500/-800/-1000 withdraw. Host requires the authenticated sender, a fresh live-player pose within 6 m of the ATM, an allowed denomination, sufficient funds, finite balances and sub-cent conservation. Guests settle deposits per inserted note and withdrawals on cash collection; only the corresponding vanilla money actions are gated, preserving local ATM controls. One outstanding request per guest, retried each second with the same sequence. Missing bindings/pose are transient; insufficient funds and distant requests are terminal rejections. |
| 163 | BankTransferResult | 0 | host -> guests (**v90**): playerId (byte), sequence (ushort), accepted (bool byte). Only the named requester consumes it. Host broadcasts a fresh WalletState before this acknowledgment. Accepted and rejected receipts are cached per player: duplicate requests re-acknowledge without moving money again; older sequences are dropped. Admission resets that player's receipt. Guest dequeues only on a matching result; disconnect clears pending work. Transfers do not affect taxable income. Bank statement history remains host-local; the shared numeric balances are authoritative. |
| 164 | SlotMachineState | 0 | host -> guests (**v92**), fields in wire order: machineId, revision, round (uint each); playerId (byte, 255 = no lease); spinning (bool byte); bet (byte 1–5); holdMask (byte, bits0–2, at most two); canHold (bool byte); credit, winnings, lastWin (int each); reel1–3 (byte each, raw stops 1–9; 0 only before a first result). Credit and accumulated winnings are separate, bounded 0–1,000,000 mk. LastWin is the current round's predetermined payout while spinning, otherwise the last completed payout, bounded 0–5000. Spinning/canHold require three nonzero stops; held reels require canHold. A host ledger draws from catalogued weighted reels without activating host UI. Local native animations use those exact stops and skip their RNG actions. Revision is compared modulo uint (forward delta ≤2³¹−1); change + 5 s keepalive + join force-broadcast. State is retained for inactive/late-bound machines. |
| 165 | SlotMachineResult | 0 | host -> guests (**v92**): machineId (uint), playerId (byte), sequence (ushort), result (byte: 0 accepted, 1 busy, 2 funds/capacity, 3 invalid, 4 distant), cashDelta (signed int). Host sends machine state and WalletState first. Only the matching outstanding requester dequeues; cached receipts retain both result and cashDelta. Delta is negative for inserted money, positive for cash-out, zero otherwise; it is informational and never applied a second time to the wallet. An accepted cash-out of at least the catalogued threshold triggers the requester's native slot achievement. |
| 166 | SlotMachineIntent | 0 | guest -> host (**v92**): machineId (uint), playerId (byte), sequence (ushort), action (byte: 0 insert current bet, 1 cycle bet 1–5, 2 spin, 3–5 toggle hold, 6 cash-out, 7 animation finished), round (uint; zero except action 7). Host's own controls use the same ledger. Authenticated guests require a fresh live pose within 6 m. Per-machine/player receipts deduplicate exact requests, reject changed payloads at the same sequence, and compare ushort sequence modulo 65536 (forward delta ≤32767). One outstanding request per machine retries every second; missing bindings/pose and an early finish are transient, all reported rejections terminal. A 15 s idle lease excludes other players; disconnect releases it. Spin debits credit if sufficient, otherwise winnings if sufficient, never combines insufficient sources. Cash-out pays accumulated winnings only. Completion credits the predetermined win once; minimum finish delay is 0.5 s per unheld reel, with a 10 s host timeout and settlement on disconnect/session end. Rejoin clears that player's receipts. Holds follow native eligibility and permit at most two; bet wrap 5→1 preserves holds as the game does. Cash mutations require finite, sub-cent-conserving floats. |
| 167 | PokerState | 0 | host -> guests (**v93**), wire order: machineId, revision, round (uint each); playerId (byte, 255 = unleased), phase (byte: 0 ready, 1 hold/redraw, 2 win offer, 3 high/low guess), bet (byte 1–5), holdMask (byte bits0–4, nonzero only in phase 1), hand (byte 0–9); credit, winnings, pendingWin (int each); five card bytes; doubleCard (byte). Cards encode suit×13+rank: suits spades/clubs/hearts/diamonds = 0/1/2/3, ace = 1 through king = 13. Zero is unset/covered. Main cards must be five distinct 1–52 values except an all-zero ready state. The private doubling card is always zero in phase 3, revealed only after a guess. Credit/winnings are separately bounded 0–9999; pendingWin is 0–998 and positive only in phases 2/3; winnings + pendingWin ≤9999. uint revision comparison accepts forward delta ≤2³¹−1. Change, 5 s keepalive and join broadcast; guests retain valid snapshots before the town/UI binds. |
| 168 | PokerResult | 0 | host -> guests (**v93**): machineId (uint), playerId (byte), sequence (ushort), result (byte: 0 accepted, 1 busy, 2 funds/capacity, 3 invalid, 4 distant), cashDelta (signed int), achievements (byte flags: bit0 royal flush, bit1 cash-out threshold). State and WalletState precede the receipt. Only a matching outstanding request dequeues and awards the native achievement; retries retain the original receipt including amount and flags. CashDelta is informational and is never applied to the wallet by the recipient. |
| 169 | PokerIntent | 0 | guest -> host (**v93**): machineId (uint), playerId (byte), sequence (ushort), action (byte: 0 insert current bet, 1 cycle bet, 2 deal/redraw/collect, 3 double, 4 low, 5 high, 6–10 hold cards 1–5, 11 collect/cash-out), round (uint matching the currently displayed round for every action). Authenticated fresh live pose within 6 m; host-local input follows the same ledger. Exact receipt deduplication and ushort sequence comparison (forward delta ≤32767); changed payload at the same sequence and older sequences are silently discarded. One outstanding request retries each second; unavailable bindings/pose are transient, reported rejections terminal. Controls are leased for 30 s while ready, 300 s during a hand; expiry/disconnect releases controls but preserves the exact hand, used cards and any committed secret double. Rejoining clears that player's receipts. |
| 170 | DebtLetterState | 0 | host -> guests (**v94**), wire order: revision (uint), debt (float principal), total (float payable), available (bool byte). Amounts must be finite/nonnegative; debt and total are either both zero or both positive, and available requires positive debt. Revision advances on any principal, rate, fee or envelope-availability change, even when a different fee composition produces the same total. uint forward delta ≤2³¹−1; change + 5 s keepalive + join snapshot. Guests retain valid state before the letter binds. |
| 171 | DebtPaymentIntent | 0 | guest -> host (**v94**): playerId (byte), sequence (ushort), revision (uint of the displayed quote). Host-local input uses the same ledger. Authenticated guests require a fresh live pose within 6 m of the envelope referenced by Rent/Letter, including after eviction relocates the mailbox. One outstanding payment retries every second. Missing bindings/pose are transient; reported rejections are terminal. Exact receipts are cached per player; changed revision at the same sequence and older sequences are silently discarded (ushort forward delta ≤32767). Admission/departure clears that player's receipts. |
| 172 | DebtPaymentResult | 0 | host -> guests (**v94**): playerId (byte), sequence (ushort), result (byte: 0 accepted, 1 quote changed, 2 unavailable, 3 distant, 4 funds/precision), paid (float: exact debit on success, zero on rejection). State, WalletState and WelfareState precede the receipt. Only the named player's matching outstanding request dequeues; exact retries return the original result and paid amount without charging again. Paid is informational and is never applied by the recipient. Internal stale code 255 is discarded, never sent. |
| 173 | VenttiPropertyState | 0 | host -> guests (**v97**), wire order: sequence (uint), keys (byte: bit0 Ruscko, bit1 Satsuma, bit2 Home), knownAccess (byte), access (byte). Access bits: 0 cabin Sleep activeSelf, 1 woodstove hatch Handle activeSelf, 2 Logwall Use enabled. All other bits are invalid; access must be a subset of knownAccess. All three key globals must bind and contain 0/1 before host publication. Missing access bindings are unknown, never a revocation. Guest requires a forward uint sequence delta in 1..2³¹−1 (including zero after wrap), retains the latest keys and merges known access bits for deferred application. Change + 20 s keepalive + join snapshot; joins do not advance the periodic change baseline. Guest applies only with its native wager resolver suppressed, retries deferred bindings, and captures/restores original local keys/access on teardown. No wager, money, stress, speech or save-point actions are replayed. Save-point activation remains host-local under the host-only save rule. Bindings live in catalog `venttiProperty`; the native Ruscko/Satsuma names are preserved. |
| 174 | VenttiTableState | 0 | host -> guests (**v98**), wire order: tableId (uint scene-path hash), sequence (uint), stake (float), playerTotal (int), houseTotal (int), outcome (byte: 0 none/reset, 1 cash win, 2 cash loss, 3 car win, 4 car loss, 5 cabin win, 6 home loss). Stake must be finite and nonnegative; totals must be nonnegative; unknown outcome codes and wrong table ids are rejected before sequencing. No stake or total is clamped to a byte. Outcome observes the native `LoseText.Status`, which can announce a result before settlement; it is never a payment instruction. Nonempty unmapped native status prevents publication and logs a diagnostic. All table variables must bind before publication/application. Change + 20 s keepalive + actual join snapshot; snapshots do not advance the live change baseline. Guest retains the latest complete observation across late bindings and accepts only forward uint deltas in 1..2³¹−1 (including zero after wrap), so stale snapshots cannot roll back live state. Original guest variables are restored at teardown. The native wager resolver must remain disabled before applying stake/totals/status; no result FSM, money, property, save or global interaction UI action is replayed. Bindings and the six native result strings live in `venttiTable`. In v99 this is a fallback observation only until the host ledger is adopted. Publication stops after adoption, and a guest that has accepted 175 ignores 174 for the rest of the session. |
| 175 | VenttiLedgerState | 0 | host -> guests (**v99**). Wire order: tableId (uint), revision (uint), round (uint), playerId (byte, 255 unleased), outcome (byte, same six result slots as 174), phase (byte: 0 betting, 1 playing, 2 resolved, 3 closed), wager (byte: 0 cash, 1 car, 2 house), stake/betMaximum/opponentLoss/pendingCash (four floats), propertyStage/playerTotal/houseTotal (three ints), playerCardCount (byte) + playerCardIds (bytes), houseCardCount (byte) + houseCardIds (bytes). Only revealed cards are sent; the remaining shuffled deck never leaves the host. Card ids 1–52 map through catalog rules, with no repeats across hands, correct totals and legal stop conditions. Decoder bounds each array to 52; state validation bounds each hand to 21 cards and totals to 33. Money fields must be finite; stake/pendingCash nonnegative, betMaximum at least one increment. Property stage is 0–2; wager/phase/outcome/card consistency is required. Stake is already paid escrow while betting/playing and historical after resolution. pendingCash is an owed return, never a new guest credit. Guest accepts only forward uint revision deltas 1..2³¹−1, including wrap, and retains a copied state before scene binding. Changes, 5 s keepalive, and join/resync snapshot; snapshot enumeration does not consume a pending live broadcast. |
| 176 | VenttiRequest | 0 | guest -> host (**v99**). Wire order: tableId/revision (uints), playerId (byte), sequence (ushort), action (byte: 0 increase, 1 decrease, 2 hit/deal, 3 stand, 4 next hand). Session authenticates playerId. Host checks table/action, fresh nearby live-player pose (≤6 m, remote pose ≤2 s), finite wallet/host time, revision and lease. Increase/decrease implement native stepped bets and property conversion; hit deals two player cards and one house card on the first press. One local command remains pending until its matching receipt; retry every second uses the exact same command. The host rate-limits processing per player to 0.1 s and caches the last exact command/receipt. A repeated sequence with different action/revision is stale. Sequence acceptance uses forward ushort deltas 1..32767. Lease expires after 15 s betting/resolved or 300 s playing; timeout/disconnect releases ownership without refunding or redealing the committed hand. |
| 177 | VenttiReceipt | 0 | host -> guests (**v99**). Wire order: tableId/revision (uints), playerId (byte), sequence (ushort), status (byte: 0 accepted, 1 busy, 2 funds, 3 invalid, 4 distant, 5 refresh, 6 stale, 7 finished), cashDelta (float), round (uint), outcome (byte). CashDelta is an audit value only: the shared WalletState is authoritative, and neither a receipt nor its retry moves money again. Rejections have zero cashDelta and outcome. An accepted command that resolves a hand carries its round/outcome; the acting peer applies its own native stress adjustment once when removing the matching pending command. State and wallet precede receipts on channel 0. A refresh may reissue the same action with the latest received revision and a new sequence, at most twice. Old/malformed/unmatched receipts do not settle a pending command. |
| 178 | VenttiSceneState | 1/0 | host -> guests (**v100**). Wire order: tableId/layoutId/sequence (uints), poseCount (byte, 3–56), then each pose: position (three floats), rotation XYZW (four signed int16 components), activeSelf (byte, 0/1). First three poses are world-space NPC/table/chair roots; remaining poses are parent-first local NPC descendants in catalog order. Quaternion components encode clamp(value, −1, 1) × 32767 rounded to nearest, ties to even; decode divides by 32767, with −32768 reserved/invalid. Quaternion squared length must be in [0.999, 1.001]; all components must be finite. Absolute position components are bounded to 100000 for world roots and 1000 for locals. Table/layout/count and pose validity are checked before accepting a forward uint sequence delta 1..2³¹−1, including wrap. Changed poses use channel 1, stationary keepalive every 2 s and join/resync snapshots use channel 0. Snapshot reads do not consume the live change baseline. See reaction integration below. |
| 179 | VenttiSoundCue | 0 | host -> guests (**v100**, live only). Wire order: tableId/layoutId/sequence (uints), sound (byte catalog variation index), world position (three floats), delay (float seconds). Exact selected host variation, never a request to choose a random clip or enter a native FSM. Validate table/layout, index within catalog, finite position components within ±100000 and finite delay 0–5 before accepting a forward uint sequence delta 1..2³¹−1. Guest queue holds at most 32 copied cues, dropping oldest on overflow; each becomes due at local receive time + delay and expires 2 s later. A delayed cue does not block subsequent immediate cues. Removed once for playback; duplicate sequences cannot replay. No historical audio in snapshots; reconnect clears the queue and sequence baseline. |
| 180 | LottoDrawState | 0 | host -> guests (**v102**). Fixed 77 bytes including id: sequence (uint32), round/ticketRound/nationalPot/nationalPotMin/nationalPotFull (five int32), numbers (7 bytes), bonus (3 bytes), prizes (5 int32), winners (5 int32), flags (byte: bit0 native DrawDone, bit1 teletext results visible). Tier order: 7, 6+bonus, 6, 5, 4. Whole completed native draws only; join and FSM-group resync included. No purchase, ticket claim or payout event. See Lotto v102 below. |
| 181 | LottoTicketRequest | 0 | guest -> host. playerId (byte), token (uint64), sequence (uint32), operation (byte: 0 buy, 1 claim), round (int32), lineCount (byte), ticketId (string), numbers (21 bytes, three seven-slot rows). 44 bytes plus UTF-8 ticketId including message id. Buy requires an empty id, 1–3 complete paid rows, zero unused rows and the host's current sales round. Claim requires a known host ticket id and zero round/lineCount/numbers. |
| 182 | LottoTicketReceipt | 0 | host -> peers. playerId (byte), token (uint64), sequence (uint32), result (byte: 0 accepted, 1 invalid, 2 round changed, 3 distant, 4 funds/precision, 5 unavailable, 6 already redeemed, 7 stale), ticketId (string), amount (float32), destination (byte: 0 cash, 1 bank). 23 bytes plus id text including message id. Stale receipts are not transmitted. Only the matching pending local operation consumes a receipt. |
| 183 | LottoTicketState | 0 | host -> guests. sequence (uint32), ticketId (string), round (int32), numbers (21 bytes), winnings (float32), retired (bool), position (vec3), rotation (quat). 66 bytes plus id text including message id. Complete ticket identity/content and initial pose; ongoing motion uses ItemTransform with FNV1a32("lotto:" + ticketId). Includes join and item-group resync; retirement is terminal. |
| 184 | PackageState | 0 | host -> guests (v109): revision (uint32), factoryId (uint32), nativeId (string), quantity (uint16), position (vec3), rotation (quat). Creates/repairs the exact standard box and mirrors remaining quantity; join/item resync and targeted replies included. |
| 185 | ReplacementPartState | 0 | host -> guests: revision:u32, factoryId:u32, nativeId:string, assemblyId:i32, installed:byte 0/1, scalarCount:byte (≤8), native f32 scalars, world position:vec3/rotation:quat; v115 appends parentKind:byte, parentId:u32, parentPath:string, local position:vec3/rotation:quat/scale:vec3; v117 appends removalAllowed:byte 0/1; v123 appends presentationRevision:u32, beltVisualFlags:byte and, when present, scale/pitch/volume/scrollSpeed:f32 each; v132 appends CamProfile:string; v135 appends AlternatorDamaged:byte 0/1/2. Creates loose copies or fitted presentation at ready unoccupied mounts; join/item resync and targeted replies included. |
| 186 | PackageOpenRequest | 0 | guest -> host (v112): playerId (byte), token (uint64), sequence (uint32), itemId (uint32), expectedRevision (uint32). One requested native box opening. |
| 187 | PackageOpenReceipt | 0 | host -> guests (v112): playerId (byte), token (uint64), sequence (uint32), itemId (uint32), status (byte), producedItemId (uint32). Exact request outcome; only the matching guest acts on it. |
| 188 | PartFitRequest | 0 | guest -> host (v116; extended v117–v119/v122/v125/v145): playerId (byte), token (uint64), sequence (uint32), itemId (uint32), expectedRevision (uint32), operation (byte: 0 install, 1 remove, 2 rotate increase, 3 rotate decrease, 4 hand tighten, 5 hand loosen, 6 tool tighten, 7 tool loosen), slotIndex (byte). One request to fit, remove or adjust a catalogued replacement; the host selects the adjustment profile from its catalog binding. |
| 189 | PartFitReceipt | 0 | host -> guests (v116; extended v117–v119/v122/v125/v145): playerId (byte), token (uint64), sequence (uint32), itemId (uint32), status (byte), operation (byte: 0 install, 1 remove, 2 rotate increase, 3 rotate decrease, 4 hand tighten, 5 hand loosen, 6 tool tighten, 7 tool loosen), slotIndex (byte). Acknowledged outcome; only the matching guest acts on it. |
| 190 | BagState | 0 | host → guests: itemId (uint32), factoryId (uint32), nativeId (string), revision (uint32), remaining (uint16), condition (float32), position (vec3), rotation (quat). |
| 191 | BagOpenRequest | 0 | guest → host: playerId (byte), sequence (uint32), itemId (uint32), expectedRevision (uint32), openAll (bool). |
| 192 | BagOpenReceipt | 0 | host → guests: same fields/order as191, followed by status (byte: 0 Pending, 1 Applied, 2 Stale, 3 Unavailable, 4 Busy, 5 OutOfReach, 6 NotOwner, 7 Failed). |
| 193 | WiringState | 0 | host → guests: sourceId:uint32, revision:uint32, flags:uint8 (Available=1, Installed=2, Bolted=4); eleven fixed wiring sources (1–8 engine, 9–11 heater/defroster added v181), unavailable flags=0; 11 bytes including ID.  Source5 additionally carries Connectable flag8 in v239; see the current installation contract. |
| 194 | BatteryState | 0 | host → guests: revision:uint32, flags:uint8 (Available=1, Installed=2; valid 0/1/3), charge:float32, chargeMax:float32 (appended v177; both finite and zero unless installed). 15 bytes including ID. Native Corris engine/accessory battery inputs; join/vehicle resync and 5 s keepalive. |
| 195 | EngineBlockState | 0 | host → guests: revision:uint32, flags:uint8 (Available=1, Installed=2, Damaged=4, HeadInstalled=8, CarburettorInstalled=16, AirCleanerInstalled=32; valid 0/1/3/7/11/15/27/31/43/47/59/63), wear:float32 (finite; zero unless block installed), fuelChamber:float32, carbReserve:float32, settingMixture:float32 (all finite, zero unless carburettor installed). carburettorPower:float32, carburettorTorque:float32, carburettorPowerAdd:float32 (finite, zero unless carburettor installed), airCleanerPower:float32, airCleanerTorque:float32, airCleanerPowerAdd:float32 (finite, zero unless air cleaner installed). exhaustFlags:uint8 (Headers=1, Front=2, Rear=4, Muffler=8; no other bits), then twelve float32 values in headers/front/rear/muffler order, each DataPower/DataTorque/DataPowerAdd (finite; zero triplet unless its section is installed; headers require HeadInstalled, fixed sections are independent). No array-length field. valvesAvailable:uint8 (0/1; requires HeadInstalled), valveSettings:8×float32 in cylinder 1–4 intake/exhaust order (finite; all zero unless available). No array length prefix. oilpanInstalled:uint8 (0/1; requires Installed), oilpanWear:float32, oilpanTightness:float32, oil:float32, oilContamination:float32, oilViscosity:float32 (all finite; five zeros unless oilpan installed). rockerCoverInstalled:uint8 (0/1; requires HeadInstalled), rockerCoverTightness:float32 (finite; zero unless cover installed). radiatorInstalled:uint8 (0/1; independent of block/head), radiatorWear:float32, radiatorCoolant:float32, radiatorPressureCap:float32, radiatorFlectEfficiency:float32 (all finite; four zeros unless radiator installed). coolantHoseFlags:uint8 (Top=1, Bottom=2, Inlet=4, Outlet=8; no other bits; independent of radiator/block/head), coolantHoseTightness:4×float32 (finite; each zero unless its bit is installed; no length prefix), carburettorTightness:float32 (finite; zero unless CarburettorInstalled). coolingAirflowFlags:uint8 (Grille=1, GrilleBlockoff=2, Hood=4, FiberglassHood=8; no other bits), grilleAirflow:float32, hoodAirflow:float32, fiberglassHoodAirflow:float32 (finite; each zero unless its corresponding bit is installed; independent of engine/radiator flags). coolingAmbientAvailable:uint8 (0/1), coolingAmbientTemperature:float32 (finite; zero unless available; independent of installation flags). 209-byte Corris engine input payload, join/vehicle resync and 5 s keepalive. |
| 196 | GearboxState | 0 | host → guests: revision:uint32, flags:uint8 (Available=1; valid 0/1), type:int32 (zero when unavailable). Corris starter interlock type, join/vehicle resync and 5 s keepalive. |
| 197 | VehicleCoolantState | 0 | host → guests: vehicleId:uint32 (nonzero), revision:uint32, flags:uint8 (Available=1; valid 0/1), celsius:float32, engineCelsius:float32 (v172). Both finite; both zero when unavailable. Corris dashboard, six native fuel/oil inputs (v172), two native cabin/heater inputs (v173), and three native electrical inputs (v174), independent of driver ownership; 19 bytes including ID. Join/vehicle/targeted resync, 2 Hz change sampling and 5 s keepalive. |
| 198 | StarterDrawRequest | 0 | guest → host: vehicleId:uint32, playerId:uint8 (1–254), sequence:uint16, kind:uint8 (1 loaded, 2 unloaded), count:uint16 (1–512). 12 bytes including ID. Authenticated current-owner native cranking callbacks; host validates native battery/wiring/flywheel and applies its own rate once per accepted count. Never relayed or snapshotted. |
| 199 | StarterWearRequest | 0 | guest → host: vehicleId:uint32, playerId:uint8 (1–254), sequence:uint16, seconds:float32 (finite, >0 and ≤1). 13 bytes including ID. Current-owner native Fuel Mixture cranking duration; host deduplicates/bounds time and applies its own starter wear rate/durability. Never relayed or snapshotted. |
| 200 | HeaterState | 0 | host → guests: revision:uint32, flags:uint8 (Available=1, Installed=2; valid 0/1/3), wear:float32 (finite, zero unless installed), rearWindowFlags:uint8 (appended v183; independent valid 0/1/3). 12 bytes including ID. Settled Corris heater condition and native body rear-window element option, join/vehicle resync and 5 s keepalive; independent of driver ownership. |
| 201 | VehicleConditionReleaseAck | 0 | Host -> releasing guest: releaseSequence:uint16 plus the 15-byte VehicleCondition payload; 19 bytes including ID. Exact pending vehicle/player/report/release and body match, committed only after local release. Approved parked state supplies host snapshots/checksums; no pose, ownership or save writes. |
| 202 | VehicleDrivetrainWearState | 0 | Host -> all guests, including driver: vehicleId:uint32, revision:uint32, flags:uint8 (0 unavailable/1 wear available), driveshaftWear:float32, gearboxWear:float32, rearAxleWear:float32, gearboxOilAvailable:uint8 (0/1), gearboxOilLevel:float32. All floats finite; unavailable fields zero; available oil requires available wear. 26 payload/28 total bytes. Host revisions include oil-only changes and survive driver changes; snapshots do not acknowledge live publication. Guarded guest reader inputs; no saved writes or failure replay. |
| 203 | GearboxOilUseRequest | 0 | Guest -> host: vehicleId:uint32, playerId:uint8, sequence:uint16, phase:uint8 (1/3). 8 payload/10 total bytes. Authenticated current owner intent; host calculates and applies native saved oil use. No guest-supplied amount. |
| 204 | GearboxWearRequest | 0 | Guest -> host: vehicleId:uint32, playerId:uint8, sequence:uint16. 7 payload/9 total bytes. One native damaged-gearbox kick-out callback; authenticated current owner, host-native 0.0525 saved wear subtraction after target validation. |
| 205 | VehicleWheelHealthState | 0 | Host -> all guests: vehicleId:uint32, revision:uint32, availability:uint8 (FL/FR/RL/RR bits 0–3), healthFL/healthFR/healthRL/healthRR:float32, then (v232) epochFL/epochFR/epochRL/epochRR:uint32. All floats finite; unavailable wheels zero. 41 payload/43 total bytes. Exact native tyre-health inputs with host revisions independent of driving ownership; parked changes, joins/resync and keepalive. Guest readers wait on missing inputs and preserve saved data. |
| 206 | ValveAdjustmentState | 0 | Host -> guests: netId:uint32, setting:float32. Nonzero ID and finite setting in [2, 8]. Absolute native valve setting for live changes, join and object resync; guest display only, with saved arrays untouched. |
| 207 | CylinderHeadState | 0 | Host -> guests: netId:uint32, revision:uint32, parentId:uint32, worldPosition:vec3, worldRotation:quat, looseMass:float32, **v222** fastenersAvailable:uint8 (0/1), fasteners:10×uint8 (0–8), tightness:float32 (59 payload / 61 packet bytes). Catalogued VIN1110 head: parent 0 is loose; VIN1010 means fitted at the validated cylinder-head mount. Attachment and fastener changes advance revision; unavailable fasteners/total are zero. Guest saved fitting/tuning arrays stay untouched; join/item/object resync and keepalive include the complete state. |
| 208 | MilkConditionState | 0 | Host -> guests: netId:uint32, revision:uint32, condition:float32, spoiled:byte (13 payload / 15 packet bytes). Live milk condition and native spoiled phase; finite [0,100], spoiled 0/1, spoiled requires condition <=1. Revisioned absolute state with join/item/object resync and five-second keepalive; guest native decay is paused. |
| 209 | FirewoodBuyerState | 0 | host -> guests: buyer netId:uint32, revision:uint32, flags:u8, amount:f32, position:vec3, rotation:quat; 41 payload / 43 packet bytes. Host publishes only selected catalogued buyers, only when buyer offer is collectable, and keeps buyer state fresh to keep offer pose and amount synchronized on guests and snapshots. |
| 210 | MooseMeatState | 0 | Host → guests: factoryId:uint32, nativeId:string, revision:uint32, position:vec3, rotation:quat, condition:float32, kind:byte. Native meat creation and host food presentation; see v224 contract below. |
| 211 | UtilityPaymentIntent | 0 | Guest → host: playerId:u8, meter:u8, sequence:u16, revision:u32. Electricity or phone invoice payment (meters 0–3); see v230 contract. |
| 212 | UtilityPaymentResult | 0 | Host → guests: playerId:u8, meter:u8, sequence:u16, result:u8, paid:f32. Electricity or phone receipt; see v230 contract. |
| 213 | SessionSettings | 0 | Host → accepted guests: flags:u8, bit 0 = permadeath, all other bits rejected. Native host load/character-save updates replace the current runtime setting; guest personal saves remain untouched. 1 payload / 3 packet bytes; see v227 contract. |
| 214 | MooseCorpseState | 0 | Host → accepted guests: corpse identity, revision, death, two consumed-piece counts, and 11 dead ragdoll poses; see v228. |
| 215 | MooseChopIntent | 0 | Guest → host: playerId:u8, corpse:u32, section:u8, expectedPieces:u8; see v228. |
| 216 | FirewoodLoadState | 0 | host -> guests (**v231**): absolute trailer load, mass, fill and complete ground-pile manifest; no payment replay. |
| 217 | FirewoodUnloadIntent | 0 | guest -> host (**v231**): authenticated, nearby start/stop bound to the current load epoch and player sequence. |
| 218 | WheelPunctureRequest | 0 | guest -> host (**v232**): native driver puncture bound to one tyre lifecycle epoch; host alone records damage. |
| 219 | StoveKnobIntent | 0 | guest -> host (**v233**): authenticated player, appliance, plate, direction and sequence; native bounded home-stove control. |
| 220 | AtfBottleState | 0 | host -> guests (**v234**): item/native identity, nonzero revision, finite quantity, independent empty flag and creation pose; 43 + N payload bytes. |
| 221 | AtfFillerState | 0 | host -> guests (**v234**, extended **v235**): vehicle/revision, cap rotation, gearbox oil level, automatic-gearbox availability and root-relative cap pose; 45 payload bytes. |
| 222 | AtfRefillIntent | 0 | guest -> host (**v234**): vehicle/bottle, authenticated player, sequence and stop/pour/unscrew/screw action; 12 payload bytes. |
| 223 | FleaSaleResult | 0 | host → guests: player, action, sequence and transaction result; v236. |
| 224 | FleaListingState | 0 | host → guests: revision, count and exact item/native-number/price/pose entries; v237. |
| 225 | FleaListingIntent | 0 | guest → host: player, sequence, revision, item ID and price; v237. |
| 226 | FleaListingResult | 0 | host → guests: player, sequence, item ID and listing result; v237. |
| 227 | SupplyItemState | 0 | host → guests: factory ID, native ID and creation pose; durable loose fuse/R20 identity, v238/v257. |
| 228 | WiringInstallRequest | 0 | guest → host: immutable ignition-wire installation intent; see v239. |
| 229 | WiringInstallReceipt | 0 | host → guest: matching operation status; see v239. |
| 230 | TaxiServiceState | 0 | host → guests: taxi service, customer, luggage and payday; see v240/v244/v245. |
| 231 | TaxiCallIntent | 0 | guest → host: authenticated answer/hangup for one current incoming call; see v240. |
| 232 | TaxiMeterState | 0 | host → guests: absolute native fare, controls and display; see v241. |
| 233 | TaxiMeterIntent | 0 | guest → host: authenticated revision-bound mode, light or total reset; see v241. |
| 234 | TaxiFareState | 0 | host → guests: native arrival, quote and cash availability; see v242. |
| 235 | TaxiFareIntent | 0 | guest → host: current-fare terminal charge or cash collection; see v242. |
| 236 | TaxiPaydayReadIntent | 0 | guest → host: acknowledge the current native salary report; see v245. |
| 237 | HouseholdFuseState | 0 | host → guests: eleven persistent holders, poses and both circuit tables; see v246. |
| 238 | HouseholdFuseIntent | 0 | guest → host: revision-bound fuse insertion, holder fitting/removal or turn; see v246. |
| 239 | HouseholdFuseResult | 0 | host → guests: acceptance and the requesting player's native shock check; see v246. |
| 240 | TractorTrailerState | 0 | host → guests: connection revision, physics owner, joint anchor and three body poses/momenta; see v247. |
| 241 | TractorTrailerIntent | 0 | guest → host: current-connection release request with actor and sequence; see v247. |
| 242 | TractorTrailerMotion | 1 | accepted simulator → host → observers: three body poses/momenta in the current connection revision; see v247. |
| 243 | SausageOpenIntent | 0 | guest → host: exact native cooking source/package and actor sequence; see v248. |
| 244 | SausageState | 0 | host → guests: session food identity, revision, condition, appearance/effects and initial pose; see v248. |
| 245 | CoffeeIntent | 0 | guest → host: home lid/fill/drink request, authenticated actor and sequence; see v250. |
| 246 | CoffeeState | 0 | host → guests: pot/cup/grounds packet contents, appearance and initial pose; see v250. |
| 247 | CoffeeDrinkResult | 0 | host → guests: accepted drink amount and strength, matched to pending actor request; see v250. |
| 248 | TrainState | 0/1 | host → guests: train route, pose, collision, light and horn state; see v251. |
| 249 | BulbState | 0 | host → guests: transient loose bulb identity, condition revision and initial pose; see v252. |
| 250 | AdvertJobState | 0 | host → guests: native job, pile and exact mailbox ledger; see v253. |
| 251 | AdvertSheetState | 0 | host → guests: session-only sheet identity and initial pose; see v253. |
| 252 | AdvertIntent | 0 | authenticated guest → host: revision-bound sheet extraction or mailbox delivery; see v253. |
| 253 | MotorOilBottleState | 0 | host → guests, reliable-ordered: ItemId:u32, Revision:u32, NativeId:string, Fluid:f32, Viscosity:f32, Grade:u8, Empty:bool, Position:vec3, Rotation:quat. |
| 254 | MotorOilFillerState | 0 | host → guests, reliable-ordered: revision:u32, epoch:u32, headId:u32, panId:u32, rotation:f32, oil:f32, contamination:f32, viscosity:f32, capPosition:vec3, capRotation:quat. |
| 255 | MotorOilRefillIntent | 0 | guest → host, reliable-ordered: epoch:u32, BottleId:u32, Sequence:u16, PlayerId:u8, Action:u8. |
| 256 | AdvertPhoneIntent | 0 | authenticated guest → host: advert call begin/heartbeat/completion/cancel; see v256. |
| 257 | AdvertPhoneResult | 0 | host → caller: advert call approved/completed/rejected; see v256. |
| 150 | RallyResultsState | 0 | host -> guests (**v73**): seq, timeSS1/2/3 (int stage times), playerTimeTotal (float), playerClassLevel (int), timePenalty (float), flags (bit0 raceOver, bit1 winner, bit2 registered, bit3 secondDay). Host owns the rally results ledger + enroll + parc-fermé penalty; guests apply. In the join snapshot. Reward rides the existing host-gated race price triggers. |
| 151 | JokkisRaceState | 0 | host -> guests (**v74**): seq, laps (int), timeCentiseconds (int), flags (bit0/1 checkpoint 1/2). Host owns the JOKKIS banger race lap/time/checkpoint; guests apply. In the join snapshot. |

VideoPoker uses a host-owned 52-card deck without replacement, including discarded
cards on the second draw. It uses a separate 52-card deck for doubling, also without
replacement within a hand. Clients never send cards or payout amounts. Native resolver
FSMs are paused on both peers; their catalogued textures, materials, card objects,
texts, sounds and physical buttons present the shared state independently of the
host's town LOD. A running native host hand finishes before takeover.

VideoPoker insertion floors `PlayerMoney`, requires strictly more than the current
bet, and checks credit <500 before adding the bet (499+5 is allowed). The bet cycles
1–5, wrapping to 1 when the next bet is unaffordable. A first deal increments the
round and spends credit first, combining winnings for any remainder. Bets are blocked
above 9001 winnings to reserve room for the maximum possible doubled payout. Any
subset of cards may be held, including all five; the second deal resolves the hand.
Categories 0–9 are loss, jacks-or-better pair (including aces), two pairs, three of a
kind, straight, flush, full house, four of a kind, straight flush and royal flush;
installed-build multipliers are 0/1/2/3/5/7/10/15/30/50 times the bet. Both A2345 and
10JQKA are straights. Payouts and achievement thresholds are catalogued.

During a win offer, deal or take-win only transfers the pending win to winnings.
A subsequent take-win while ready pays **both** credit and winnings to shared cash.
Double commits a secret card before the guess: low accepts ranks 1–6, high accepts
8–13, and seven loses either guess. A correct guess doubles the offer; reaching
500 or more automatically collects it. No accepted action can silently clamp away
money, and wallet transfers require finite, sub-cent-conserving float arithmetic.
Session teardown stands on an unsubmitted five-card hand, forfeits a committed double
without a guess, collects any remaining offer and returns both banks once. If cash
cannot represent the refund, the settled banks are restored after the native menu's
reset, including when the town next activates. Native FSMs, input actions and visuals
are restored on teardown. Two-player gameplay verification remains required.

Debt-letter quotes use the host's `Rent/Debt` and the inactive sheet's own `Interest`,
`Cost1` and `Cost2` variables, read through the catalog's `debtLetter` bindings.
The calculation preserves the native single-precision action order: multiply the
principal by Interest, add Cost1, then add Cost2. Build 23268598 defaults are 1.29,
59 and 864; the bank prime rate is unrelated. Display formatting (principal `0`,
total `0.0`) does not round the actual charge. A payment must match the current
quote, find an available envelope and preserve cash to within half a cent. Accepted
payments subtract shared `PlayerMoney`, clear the host's rent debt and hide the
envelope once. Concurrent payments cannot clear or debit the same debt twice.
Bank balance, taxable income and an already completed eviction are unaffected.

Both peers replace the native calculation/request/debit actions while retaining
the local camera, hover and close flow. A confirmed receipt plays the native buy
sound and closes the letter; changed quotes require another press after review.
The envelope follows host availability, independently of the host's open sheet.
Payment binding failures disable the payment button without disabling welfare sync.
Teardown closes an outstanding payment screen before restoring native actions.
Two-player checks must cover concurrent payment, quote changes, relocation,
reconnects and a subsequent singleplayer payment (COVERAGE-ROADMAP R1.6).


`NpcTransform.flags`: bit 0 = **final**, bit 1 = **dead** (**v77**: moose collision→corpse; guests activate their own ragdoll).
Originally bit 0 = **final** (at-rest pose, sent reliable; receiver
restores original kinematic state and sleeps the body). Only the host sends;
guests never relay.

`ItemTransform.flags`: bit 0 = **final** (at-rest pose, sent reliable; receiver
restores physics and sleeps the body), bit 1 = **driver** (sender's player sits
in this vehicle; driver claims beat proximity claims, ties broken by lowest
player id), bit 2 = **vehicle** (stream is a registered vehicle root; sent
reliable), bit 3 = **hasVelocity** (payload appends the sender's rigidbody
velocity as a Vector3; set on moving-vehicle packets — receivers dead-reckon
toward pose + velocity·min(age, 0.3 s) instead of trailing the last pose, and
seed released bodies with it). A seated driver never releases on stillness — it
keeps the vehicle with ~2.5 Hz keepalives until the player leaves the seat, and
receivers block the vehicle's drive trigger while a remote driver holds it.

`VehicleCargo` interplay: while an item is pinned by a live cargo stream, a
world-space `ItemTransform` for it is accepted only from the *same* owner (the
authority handing it off: flung out, grabbed, or settled at rest) — third-party
streams wait until the pin goes stale (1 s without cargo packets). Items held
by the local player are never pinned, and a machine never applies cargo packets
for a vehicle it streams itself.

`TimeSync` semantics: guests jump their sun/cloud hour FSMs only when total
drift exceeds 10 game-minutes; forecast and `DaysPassed` variables are
overwritten on every message (host wins). When `dayOfWeek` changes, guests
broadcast the matching global weekday event (MONDAY…SUNDAY) so TV/HUD/job
schedulers stay aligned.

`ItemSpawn` net IDs are minted by the host as `hash("spawn:" + containerNetId +
":" + epoch + ":" + ordinal)`. For bags, the container ID is the shared persistent
bag item ID; outputs are observed directly at native factory completion before
ordinary scanning. Large spills use multiple ≤32-entry manifests with separate
epochs. Retries retain an epoch; already-bound bodies never receive a second ID.
Native package outputs keep their PackageState identity and are excluded from
these generic manifests. Spawned pickables use ordinary transform/cargo/despawn.

`VehicleClimate` retains its v26 snapshot sentinel; the v185 ownership contract above supersedes earlier observer authority.
`Sequence == 65535` (`SnapshotSequence`) denotes a join/resync observation applied
without advancing the live stream's dedup baseline.

Per-player dedup latches on the host are dropped when a player (re)handshakes into a
slot (v82+): the remote's counters restart with its session, and a surviving latch
would reject everything the returning player sends as "stale" until it out-counted
its previous life.

### Vehicle engine stream ownership v126

VehicleState rejects vehicle ID zero, owner 255 and flag bits outside 0–4 before
application or sequence changes. Guests may report only for a vehicle whose current
remote owner is that authenticated guest, while the host does not own it locally.
Nearby observation alone grants no engine/fuel reporting authority. Hosts apply and
relay only accepted fresh reports, preserving reliable channel 0 for final OFF.
Guests accept live state from the established remote owner, or from host zero when
the vehicle is unowned. A locally owned simulator rejects incoming vehicle state.

Each receiver tracks live sequences independently per vehicle and sender. The first
normal sequence, including zero or a high initial value, is accepted. Later values
advance only for unsigned uint16 differences in 1–32767. Ownership changes retain
all sender histories, so a returning owner cannot replay its old packets. Re-admission
forgets only that player's histories; session teardown clears all histories. Live
senders skip 65535 when wrapping. A host-zero snapshot sentinel is accepted only by
a guest with no local owner and no active guest remote owner; it never creates or
advances any sender baseline. Guests cannot submit the sentinel to the host.

A local simulator keeps its established vehicle ownership while its own engine or
ACC remains active after leaving the seat; the driver flag clears immediately. This
continuation does not begin merely because remote gauge/electrics presentation is
active. After ignition is off and the vehicle settles, a normal-sequence OFF state
travels on reliable ordered channel 0 immediately before the final ItemTransform.
An authenticated nearby guest's fresh driver claim can replace a current remote
non-driver simulator, but cannot take a live driver's vehicle, use a final packet as
a new claim or take authority from outside the proximity gate.

Host join/resync state for a remotely owned vehicle copies its fresh accepted owner
observation, including gear, instead of reading a competing native host simulation.
Host-native snapshots include gear as well. Targeted vehicle poses use the existing
pose-only WorldItemSnapshot path and do not clear a simulator's ownership. These
changes establish stream ordering and ownership; they do not make native engine
RPM, load or wear simulation operational on the host during guest driving.

### Engine damage v124

VehicleDamage retains its fixed 81-byte layout, including the message ID. Only the
selected host may send it after the guest handshake, and OwnerPlayerId must be zero
regardless of which player currently drives or holds the vehicle. Hosts reject this
message from guests even after authentication. VehicleState and VehicleCondition
keep their existing direction and ownership rules. VehicleClimate follows the
v185 contract above.

The host alone observes its native fitted-part Wear, reconciles concrete breakage
and publishes repairs. Unknown native slots retain whatever prior damage the host
includes in its complete DamageMask. Guests do not merge that mask with local native
state or older observations: every accepted packet replaces the stored mask, known
mask and wear array. Known wear must be finite and agree with the corresponding
damage bit; unknown wear values are not interpreted. Snapshots and reads own deep
copies so deferred consumers cannot mutate the retained condition.

Each vehicle has one host sequence baseline. The first valid packet may use any
ushort sequence, including zero. Forward modular differences 1–32767 are accepted;
duplicates, older packets and the ambiguous half-range difference are rejected.
Changing vehicle ownership cannot rebase the host sequence. Session cleanup clears
the replica so a later host session may start its sequence again.

Guest checksums use that accepted host condition, without reading or writing saved
mount Data. The guest's native PartBreakages graph remains suppressed even while the
guest drives or its protected world outlives the network connection. No received
failure event is replayed: native piston failures can randomly trigger oilpan/block
damage, and some failures follow ActivePart to mutate persistent saved part data.
The host keeps its native simulation and its normal singleplayer/hosting authority.

This boundary prevents damage packets and failure replay from corrupting isolated
guest parts or overwriting the host with guest-save wear. It does not reconstruct
guest operational engine references or guarantee guest-driving wear progression:
the current remote engine stream updates gauges/electrics, without reproducing all
host native engine RPM, throttle, load and oil-pressure inputs. That simulation
integration remains separate work.

### Reserved ranges

Still-unassigned ids inside each themed range (everything else in 1–159 is
assigned in the table above):

- 67–79 vehicles
- 111–119 NPCs/jobs
- 126–139 snapshot/bulk transfer control (save data)
- 144–149 crime
- 152–159 racing
- 160–183 economy overflow is fully allocated; 184 is standard package state (see message table)
- 184–197 package/part/bag state and intents, engine wiring, battery, block, gearbox and host coolant states are allocated
- 202+ unallocated

Rules: never reuse a retired id; new fields are appended only together with a
protocol version bump (no silent format drift).

### Rally v101 crossing reliability

The guest's report token is a correlation value, not authentication; session peer
identity still supplies authorization. Host observes registered guests' received
poses no older than 2 s, within 28 m of the requested static marker, with a vehicle
delegated to that driver within 14 m. It remembers each matching player/stage/marker
for at most 3 s from the original pose timestamp. Re-reading the same old pose
cannot extend that window. This permits a brief report/pose ordering delay without
accepting guest-supplied positions or clocks. An already accepted exact retry needs
no fresh proximity, so a missing acknowledgment can recover after the driver leaves.
New reports still need valid evidence; an extended outage cannot establish an
unobserved crossing. The clock begins when the host accepts the start, not from a
guest timestamp.

Catalog `rallyProgress` lists the three stage timing/start paths, ordered checkpoint
names, and native marker commit/completed states. Core validates the corresponding
Timing variables and waits for every marker before observation. The installed build
has 4/6/4 checkpoints for SS1/SS2/SS3. A marker's local `Checkpoint` bool is unused:
its `Set bool` state writes the Timing FSM's named flag and ends in terminal `Idle`.
Core observes that durable terminal state, because Timing clears its flags during
finish. Crossings are processed in number order; an incomplete scan cannot finish a
stage early. A scene rebind within the same connection preserves the report token
and sequence counter. Initial save flags/states do not reconstruct a race that
predates sync. A retained host record can
continue after readmission when native local progress agrees; reconstruction of
native local rally progress from a host record remains outside this adapter.

These records serve rally progress observers. They do not replay vanilla timing,
write prize money/results, or control opponent cars. Stage-aware opponent fleet
synchronization remains R2.10; two-player runtime verification is still pending.

### Ventti v99 host integration

Core adopts only a loaded, idle native manager with zero card stages, an empty
used deck and all 52 undealt native cards. A partly played native hand is never
reconstructed from totals. Both peers cut the four native betting/dealing FSMs'
actions before enabling mod input. The host leaves GameManager load/save handling
active; catalog-checked outcome states retain native keys, doors, cabin access,
dialogue and animations while their wallet/progression/stress actions are cut.
Each resolved round enters its outcome once. A cash return that cannot be added
precisely remains owed; the native terminal animation waits for that credit.
After the native resolver returns idle and a three-second result window expires,
the host resets a paid resolved hand without requiring a second player command.

Guests render the same native card textures (material slot 1, `_MainTex`), retain
state across late binding, and send intents from the native pick targets. Extra
plain meshes extend hands beyond the game's nine visual slots without copying
FSMs or deck objects. Property replication remains 173; guests never enter a
native outcome or activate a save point. Teardown refunds an undealt stake or
stands on the already committed deck, then restores native controls. An unusually
large wallet that cannot represent an exact final return leaves that amount as
already paid native stake, rather than discarding it. Guest variables, materials,
visibility and action flags are restored. The v100 reaction adapter below mirrors
host NPC effects. The two-player save/reconnect/LOD matrix still needs runtime
validation.

### Ventti v100 reaction integration

`venttiTable.reactions` defines the root path, ordered pose paths and ordered
native AudioSource paths. `layoutId` uses `StableHash.Fnv1a32` (UTF-16LE code-unit
bytes) over `"VenttiReaction/v1\n" + rootPath + "\n" + join(poses, "\n") + "\0" +
join(sounds, "\n")`. The current catalog has 46 poses and 21 sound variations.
A pose packet is 15 + 21 × count bytes including message id: 981 bytes currently,
1191 at the 56-pose cap, below the [classic Steam unreliable packet limit](https://partner.steamgames.com/doc/api/ISteamNetworking#EP2PSend)
of 1200 bytes. Sound packets are 31 bytes.

Host samples at 10 Hz when a guest with a fresh pose is within 80 m of a world
root, otherwise 1 Hz. Guest buffers state before binding, pauses native NPC FSM
actions and animations, makes replicated rigidbodies kinematic, and applies poses
in LateUpdate. It interpolates for 100 ms, snapping world moves of at least 25 m
or visibility changes. First received pose applies immediately, including after
late join. Current pose, rather than native outcome replay, also covers a thrown
table/chair and the NPC's subsequent crawl/walk. Original guest transforms,
visibility, physics flags/velocities, animation and action flags return on teardown.

Host sound hooks run immediately before catalog-validated MasterAudio actions,
after their native ArrayList variation choice. Guest binds the corresponding
AudioSource by full native path, including inactive sources, and plays its clip
from a temporary source at the received origin. Binding retries retain only
unexpired cues; playback skips elapsed audio and distant/missing listeners.
The native delay is relative to receipt, with no cross-peer clock correction.
Missing or changed audio bindings do not disable poses or betting. Native group
mixing and audiovisual timing still require in-game comparison. No reaction
packet writes money, keys, save points, stress, progression or native FSM events.

### Lotto v102 complete native draws

`LottoDrawState` is one global host-owned state for `Systems/Lottery::Numbers`.
The catalog fixes the scalar/list slot meanings and maps their native names.
Main and bonus numbers are each strictly ascending in 1..39, with no duplicates
across either list. All rounds, pots, prizes and winner counts must be nonnegative
int32; no float/ushort narrowing is permitted. Unknown flag bits and malformed
array shapes are rejected. Zero winner counts do not imply zero prizes: the
native calculation remains the authority. DrawDone is sampled data, **not** a
completion flag (vanilla sets it before drawing).

The host samples only after the native `Reset points`, `Day`, `Time` or save
terminal `State 35` is reached, with all four live ArrayLists at their full sizes
and the five winner variables matching the winner-count list. Intermediate RNG
and prize calculations are never published. Broadcasts compare all fields/list
slots every two seconds, with a thirty-second keepalive. Join/resync snapshots
use fresh sequence numbers without consuming an existing peer's change edge;
if the host is still calculating, the next completed broadcast supplies the draw.

Guests accept only the selected authenticated host. A forward uint sequence
delta in 1..2³¹−1 replaces a copied pending draw; invalid, duplicate, stale and
half-range updates do not consume that baseline. Pending state survives missing
scene bindings. Once native save loading/calculation has reached a stable state,
the guest preserves its local arrays/scalars/display visibility and pauses the
Numbers FSM with restart-on-enable disabled, even while waiting for host data.
Applying a snapshot updates the live ArrayList instances (numbers remain boxed
int32), scalar variables and DrawDone without sending `CHECKLOTTERY`, `RESULTS`
or `LOTTODRAW`. Teletext reads once on enable, so changed data refreshes its
Texts subtree after the whole draw is written; visibility follows the host,
including when the paused guest cannot exit `Reset points` to reveal it.
Teardown restores local data before resuming the native FSM. A subsystem error
retains suppression until teardown instead of running a partially applied draw.

This message does **not** establish shared ticket identity, purchases, claim
validation or exactly-once payouts. Megaveto uses hockey results and is separate.
Two-player native draw/display/save-restoration checks remain required.


### Standard package creation, quantities and disposal v109

PackageState (184), host -> guests on reliable ordered channel 0:

| Field | Wire type | Meaning |
|---|---|---|
| Revision | uint32 | Per-box content revision, compared with unsigned wraparound |
| FactoryId | uint32 | FNV1a32(factoryPath + "::" + factoryFsm) |
| NativeId | string | Exact native Use.ID, including its positive counter |
| Quantity | uint16 | Remaining parts, 0 through the cataloged capacity |
| Position | vec3 | Current host pose for creating a missing box |
| Rotation | quat | Finite approximately unit rotation |

The encoded message is 42 bytes plus UTF-8 NativeId bytes, including the message ID.
All 30 standard boxes under `Spawner/CreatePartsPackages` are cataloged. Capacities
are 4 for pistons, 5 for main bearings, 8 for rockers, and 1 for the other 27 types.
The stable item ID is `FNV1a32("factory:" + factoryIdDecimal + ":" + nativeId)`;
ItemTransform (42), item snapshots (122), cargo and removals use this same ID.
ItemSpawn's factory flag remains limited to trophies.

The counter suffix is a canonical positive Int32. `boxalternator01` is prefix
`boxalternator0` plus counter 1; timing belts use prefix `boxtimingbelt`. Live native
binding requires both Use.ID and the exact CreateItemsDB contents-factory path.
Unknown kinds, invalid counters, conflicting contents references, invalid quantities,
non-finite poses and identity collisions are rejected. Generic spill capture,
adoption and template cloning exclude structurally recognized boxes, whose common
`package(Clone)` / `empty(itemx)` names cannot identify their contents.

The host captures each factory's exact New output after native name assignment,
waits for Use initialization/load, and also discovers saved boxes. New boxes and
changed quantities are broadcast; joining, item resync and targeted object replies
include PackageState with a fresh creation pose. Snapshot observation never advances
the broadcast baseline owed to existing peers. Guests retain copied validated states
until the catalog factory is ready. Older revisions and equal revisions with changed
quantities are rejected; equal-revision replays may refresh the pose for repairing a
missing replica. Quantity updates do not teleport an existing body. Item snapshots
and transform streams continue to move existing boxes.

Guests pause each native box factory at idle after its saved-object load. Local
saved/generated boxes are hidden and preserved separately, including those sharing
the host's ID; a snapshot never overwrites their transforms. Replicas use the exact
cataloged prefab with its Use disabled before cloning, skip native load, and initialize
owner/ID/capacity/quantity before starting normal interaction. Their save/delete
callbacks cannot write guest saves. Since v112, attempted opening queues a host
request without decrementing quantity or invoking a guest native part spawner. Disconnect destroys replicas and restores original boxes
and factory FSMs without re-running their native load.

Accepted ItemDespawn/removal snapshots remain terminal for the session, including
before creation. Host disposal runs native GARBAGE, clears Quantity and removes
physical components while retaining Use for SAVEGAME -> Quantity <= 0 -> Delete(ID).
Guest disposal removes only its replica. Empty boxes remain physical until disposal.
Existing ownership/proximity gates and optimistic guest removal behavior remain.

Complete installation/bolt/save replication and native two-player/save verification
remain unfinished. v110 supplies part identities, v111 supplies loose boxed-content
creation, and v112 supplies guest opening intents. BrakeBiasRegulator is a direct part and Plugwires has null factory
references in this build; neither is one of these 30 boxes.

### Acknowledged guest box opening v112

PackageOpenRequest (186), 23 bytes including the message ID:

| Field | Wire type | Meaning |
|---|---|---|
| PlayerId | byte | Must match the authenticated guest, excluding 0 and 255 |
| Token | uint64 | Nonzero guest-generated token, renewed when its session state resets |
| Sequence | uint32 | Monotonic request sequence within that token |
| ItemId | uint32 | Stable standard-package body ID |
| ExpectedRevision | uint32 | Last host PackageState revision observed by the requester |

PackageOpenReceipt (187), 24 bytes including the message ID: PlayerId (byte), Token
(uint64), Sequence (uint32), ItemId (uint32), Status (byte), ProducedItemId (uint32).
Status is strictly 0 Pending, 1 Accepted, 2 Busy, 3 Unavailable, 4 Stale, 5 Empty,
6 TooFar, or 7 Failed. ProducedItemId names the confirmed native output on acceptance
and is zero otherwise. Receipt identity is compared by player/token/sequence/box ID.

The guest permits one outstanding request and retries its **unchanged** fields every
0.5 seconds until a matching terminal receipt arrives. Pending and Busy are
nonterminal. Quantity and native counters are never changed optimistically. The
host remembers one operation/outcome per guest. Duplicate exact requests return its
current outcome; same-sequence changed payloads, foreign tokens and stale sequences
cannot execute. A new sequence cannot replace an in-flight operation. Unsigned
sequence differences above Int32.MaxValue are stale; wraparound is supported.
Busy before reservation does not permanently consume the request sequence. Admission
forgets the old player's ledger; old receipts cannot clear a new client's token.

Before reserving an opening the host requires: a known live, nonretired box, matching
revision, positive quantity, a living authenticated guest with a pose no older than
two seconds within three meters, no different guest owning the item stream, and
ready native box/contents FSMs. The exact contents factory must be registered and
produce one part: the piston/bearing/rocker NumberOfProducts variables currently equal
**1** (their serialized action operands contain stale default 4 values and must not
be used as the live count). Other factories return directly to Idle. Counter overflow
and unsupported references fail closed. Guests also wait for their contents adapter.

One opening is reserved at a time. A head guard also controls native host clicks,
redirecting blocked interactions before any native actions run. On shipped PlayMaker
1.7.7.6, a self-event queues a state change and ActivateActions stops immediately.
The accepting operation uses the original Create Plug actions, including the Quantity
-1, PartSpawnPoint assignment, MinimumWear write and SPAWNITEM. The factory tail
captures the exact next prefix/counter identity; the box tail records its resulting
quantity. Acceptance waits for one decrement and one matching, initialized native
part. Partial/unconfirmed failures are terminal for that request and disable that
box's opening adapter; they are never automatically retried as another creation.

Capturing the quantity at the tail permits later legitimate disposal of the box.
If the created part was already consumed, confirmation publishes its retirement
instead of creating it again. Replies send fresh box and accepted output state (or
retirement) before the receipt. Retried accepted requests therefore repair a missing
copy while respecting terminal removals. Snapshot reads preserve delta publication
baselines owed to other guests. Only the requester plays native opening audio, once
on an accepted receipt; nonterminal retries do not play it. Host native audio remains.

Disconnect clears the pending client and ledger and removes only owned box hooks.
No opening is replayed as part of cleanup. This enables unpacking and loose-item
carrying; it does not enable the still-deferred full fitted-part/bolt graph. Native
two-player, save/reload and simultaneous host/guest interaction tests are pending.

### Guest replacement fitting/removal/adjustment v116–v119/v122/v125/v145

PartFitRequest (188) is 25 bytes including its uint16 message ID; PartFitReceipt
(189) is 22 bytes. Field order is exactly the table above. Both use reliable ordered
channel 0. Requests contain no arbitrary mount path, pose, assembly state or condition.
SlotIndex is appended after Operation: zero for fixed-mount installation and all
removal/adjustment requests, 1–32 for catalog array installation. Framing rejects larger values
and any nonzero removal/adjustment slot. Application requires the family's actual slot count
and the host's nearest slot to match. Current arrays have 4, 5 and 8 slots.
Receipt statuses are Pending=0, Accepted=1, Busy=2, Unavailable=3, Stale=4,
NotLoose=5, TooFar=6, Blocked=7, Failed=8, NotFitted=9 and Bolted=10. Status bytes
above 10 and operation bytes other than Install=0/Remove=1/RotateIncrease=2/RotateDecrease=3/
HandTighten=4/HandLoosen=5/ToolTighten=6/ToolLoosen=7 are invalid framing.

Rotation operations are admitted only for a catalogued `handRotation` or
`distributorTiming` binding. The host selects the profile from the known part;
guests cannot request another profile or scalar. For an alternator `handRotation`,
the requested revision must still describe the same fitted part; its Data,
InstallPoint, actual parent and mount Part/Mpoint/Installed references must agree.
The host requires a fresh living guest within 3 m, an enabled native HandRotate
and pick collider, and adjusting-bolt tightness 0–7. Native Wait/turn states and
the 0.1-second cooldown are busy. An accepted request enters one native Clockwise
or Counterwise state; both part and mount SettingRotation and the pivot pose must
settle at the expected half-degree step (clamped to 0–7). No guest supplies an
absolute angle or changes mount references. A turn at a limit is Blocked; a
tightened adjusting bolt is Bolted. State 185 precedes the final receipt. The
guest's original HandRotate stays disabled, and its pose uses absolute host state.

For VIN131 `distributorTiming`, native SparkAngle is a finite value within 0–20.
RotateIncrease/RotateDecrease changes it by +0.2/-0.2, clamped at the endpoints;
its native randomized fractional starting angle is preserved without rounding to
a grid. A turn at a limit is Blocked. The current observed revision and fitted
attachment must agree with the saved Data and native mount references. Native
Data.Tightness must be finite and in 0–8 exclusive of 8; otherwise the adjustment
is Bolted. Fresh living-player proximity, native pick/input readiness and cooldown
also gate the host operation. The guest's empty-hand pick is within 1 m.
The host enters one native Clockwise/Counterwise turn and confirms both part and
mount SparkAngle and the resulting pivot pose before acceptance. State 185 precedes
the final receipt. The original guest timing graph stays disabled; its visible
pivot follows accepted absolute SparkAngle. No BoltState (44/123) stream is added
for this angle, and an earlier accepted turn cannot be repeated by a lost receipt.

HandTighten/HandLoosen are admitted only for the catalogued oil-filter hand-screw
binding. Data.Tightness must be a finite integer in 0–8; a request changes it by
exactly +1/-1. Invalid or fractional values are Unavailable, and a turn beyond a
limit is Blocked. The observed revision, fitted attachment, fresh living-player
proximity, matching native mount and native readiness/cooldown gate the turn.
Native host execution must settle at the expected saved value before acceptance.
The guest supplies a direction, never an absolute tightness or another target.

State 185 publishes the saved part Tightness before the receipt. Guest child
presentation reads the resolved Data.Tightness after the shared part-receipt order
has been applied, so a deferred older part/bolt observation cannot undo a newer
hand turn. The original guest Screw graph remains disabled. This control does not
use BoltState (44) or WorldBoltSnapshot (123), whose native integer-array bolt
contract remains unchanged. Join and object resync use the same state 185 scalar.

ToolTighten/ToolLoosen use the catalogued SPRKPLUG0 `toolScrew` profile only.
They require slotIndex zero, finite integer Tightness in 0–8, an unchanged
observed revision and the same fitted native attachment. The host checks the
part's saved AssemblyID against the native Sparkplugs array; a mount's AssemblyID
is installer scratch and is not authoritative after loading. Mount occupant,
parent, Installed, Tightness and Bolted must agree. Fresh living-player proximity
is within 3 m and at most 2 seconds old. The fastest native ratchet cadence is
0.08 seconds. Host collider visibility is independent of the guest's tool mode.
The host refreshes Screw's scratch from saved Data before entering one native
Screw/Unscrew state, then verifies saved tightness, mount state and local depth.
State 185 carries the absolute scalar and native randomized pose before the
receipt; duplicate requests cannot add another turn.

On guests, a fitted and fully applied replica exposes its native layer-12 trigger
only in RepairMode, while the factory is suppressed and no turn is pending.
The safe Screw graph has no scalar or BOLTING writes. Native wrench and ratchet
sends require the same selected object and the native .55 ± .02 tool-size gate.
The name-recognition hook accepts registered plug identities without renaming
them or bypassing that size check. Pending state, missing parents, failed controls
and loose presentation disable the tool interaction. Loose presentation restores
the original layer and collider behavior. Session cleanup removes the recognition
hook. No bolt-array message or engine input is added for these controls.

The claimed player must match the authenticated sender and token must be nonzero.
Each admission retains one immutable request/outcome per player. A uint32 sequence
advances only when its unsigned difference is in 1..Int32.MaxValue. Equal sequence
retries must match token, part, observed revision, operation and slot. They return the cached pending
or final result without entering native fitting/removal/adjustment again. A later request cannot replace
an unfinished operation. Terminal denials, including Busy, require a new click;
they are not automatically retried as future operations. The guest retries its
exact pending request every 0.5 seconds until a matching terminal receipt. Admission
and session cleanup reset the ledger; old tokens/receipts cannot complete new work.
Receipt matching includes the operation and slot. Installation and removal share one host
in-flight operation, and one client pending request.

Installation admission requires a tracked, active native replacement with the exact observed
revision, AssemblyID=0, and ready native Data/Stop and mount/Idle states. The requesting
living player's latest pose must be no older than 2 seconds and within 3 m of both
part and mount. Item authority must belong to that guest, or have been released by
that same guest within 0.5 seconds with no subsequent owner. A host-owned or other
guest-owned part is unavailable to the operation. Part-to-mount distance must be
strictly below the live native PartsAssemblyTolerance (finite, positive, at most 1 m).

For the 27 fixed-mount replacement families, the host verifies the factory-supplied
InstallPoint. It sends the native ASSEMBLING event, which selects
ActivePart and traverses Allow install?/Far/Near; only an unchanged, nearby, still
authorized candidate at Near receives PROCEED from its entry guard, in the same
frame as the prerequisite checks. Delayed Near entry cancels the preview instead
of confirming old prerequisite results. Native installation owns assembly
IDs, prerequisite effects, mass, Rigidbody removal and reparenting. One remote
fitting may be in progress at a time. The host waits up to 3 seconds for a fitted
state whose native mount and actual parent agree. It publishes state 185 before the
receipt. Failure after dispatch never automatically repeats installation, and an
unsettled commit disables fitting for that part for the session. Cancellation only
backs out this candidate's Far/Near preview, never another part or an installed state.

v118 adds the remaining three families using their native AssemblyDatabase arrays:
VIN103/Pistons (4), VIN104/MainBearings (5) and VIN117/Rockers (8). Each catalog rule
binds ArrayReference and the exact slot count. The adapter resolves the live native
array with a null index-zero sentinel and unique GameObject references. It preserves
holes and includes occupied/inactive slots in distance selection, matching the
native closest-object action; equal distances choose the later index. It never
skips an occupied nearest slot to choose a farther free one. Invalid geometry or
tolerance, missing/inactive selected mounts and unavailable arrays defer/reject.
The guest's normal held-part fitting click includes this selected slot. If the
host selects another slot, the request receives Stale and requires a new click.
These three native Data templates lack Installed; creation and initialization no
longer require that fixed-part scratch flag. AssemblyID remains the wire authority.

Slot admission also requires an idle shared Installer with cleared ActivePart,
AssemblyPoint and Index, plus a ready idle mount and loose Data/Stop. The native
part's ASSEMBLING → Far entry sets Installer.ActivePart and Reference and sends
CHECK. Before any Installer/Near action, a temporary guard rechecks the requested
slot, array reference, native selected index/point, current part revision, authority,
fresh player proximity and free mount. Native Near assigns the mount's AssemblyID
and AllowInstall and sends its CHECK. The mount's native prerequisites must reach
Near in that same frame; the existing fitting guard confirms only the same part,
slot and AllowInstall result. Native Install 1 reads the selected ActivePart,
sets its InstallPoint and AssemblyID, stops the Installer and sends INSTALL.
Native engine/mass/body/reparenting actions remain authoritative.

An unconfirmed slot selection cancels before the initiating call returns, clearing
only its preview's AllowInstall and backing out that mount before stopping the
still-owned Installer. It cannot wait for a later host mouse click. Once committed,
the operation is observed for up to 3 seconds; acceptance requires the requested
AssemblyID and actual InstallPoint, plus the existing settled attachment proof.
Failed committed operations are never reexecuted. Cleanup removes the selection
and mount guards. All 17 array mounts were inspected in build 23268598; runtime
two-player, slot movement, competing input and save/reload checks remain pending.

Held items keep their transform ownership alive even when stationary. Guest-created
loose copies display a left-click prompt while held at a free known
mount. Their installation, bolt and save actions remain disabled; the host's state
185 updates their presentation. The normal pickup click may release the body first.
Occupied guest-save mounts still defer presentation. Original guest-save isolation
and operational bolt/engine graphs for
these copies remain unfinished. Existing native guest parts retain their legacy
adapter, but their generic replacement-part install/remove states cannot bypass
the new host gate. These paths require native two-player/save verification.

For removal, the host resolves the part's current Data.InstallPoint, including
dynamic piston/bearing/rocker mounts; it does not use the factory's installation
default. Admission requires the exact observed revision, positive AssemblyID,
a validated attachment, finite native Tightness in [0, 1), no remaining Rigidbody,
and active, started part/mount FSMs. ActivePart, Installed and AssemblyPoint must
agree with the part and its actual parent. The native root BoxCollider must be
enabled and a trigger, with the part on layer 19 and untagged, and Data must be in
Mouse off/Mouse over. These checks preserve native blocker/collider gating.
The living guest's pose must be fresh within 2 seconds and within 3 m of both
part and mount. Fitted parts have no loose-item ownership requirement.

The published RemovalAllowed flag reflects this native readiness. The host checks
it again, together with revision and proximity, at a temporary guard before any
native Data/Remove action. The validated entry writes this part as ActivePart and
sends REMOVE to that mount's native Allow removal? flow. Native actions own mass,
dependent removals, UNINSTALL, assembly identity, body creation and detachment.
The host observes for at most 3 seconds and accepts only a loose zero-tightness
state with a new Rigidbody, Data/Stop, and no continued occupancy of the old mount
by this part. State 185 precedes the receipt. An unsettled commit disables further
removal for that part for the session without repeating the native operation.
Cancellation before commit derives fitted interaction through BOLTING only while
the part still owns that mount; otherwise it stops this Data entry without writing
the mount. Cleanup removes the temporary guard.

Guest-created fitted copies offer a right-click removal prompt only with the native
hand in PickUp/Look for object. Selection analytically intersects their existing,
disabled native root BoxCollider with the camera ray, using the native 1 m range
and layer-19 physics occlusion. A nearer fitted copy also occludes a farther copy.
Colliders remain disabled and no guest assembly or save action runs. Host loose
state restores normal item tracking and carrying. Native bindings were checked
for all 30 replacement families; gameplay and save verification remain pending.

### Replacement-part creation v111/v121 and fitted presentation v115/v123

`replacementParts` maps 32 factories: the 30 standard box-content factories and,
since v121, the direct shopping-bag fan-belt and oil-filter factories. Each rule
selects its exact native prefab/save prefix, ordered Data save-float variables and
factory-to-Data object references (InstallPoint and optional PartBlocking). Factory
FSM names are rule-specific, with the existing box-factory default retained.
Factory ID is the usual
`FNV1a32(path + "::" + fsm)`; body ID remains v110's `FNV1a32("part:" + nativeId)`.
The native counter is canonical Int32 decimal, including zero for original parts.
A packet cannot choose an arbitrary prefab, state name or variable name.

The v121 direct factories share path `Spawner/CreateItems` and retain distinct FSM
names in their factory hashes. Both bind Data.InstallPoint from factory VINP.

| Factory FSM | Native prefix | Scalar order | First purchased ID |
|---|---|---|---|
| Fanbelt | FANBELT0 | Wear, Tightness | FANBELT01 |
| Oilfilter | OILFILTR0 | Dirt, Tightness | OILFILTR01 |

ReplacementPartState (185) fields, in order:

| Field | Encoding |
|---|---|
| Revision, FactoryId | uint32 each |
| NativeId | uint16 UTF-8 byte length + bytes |
| AssemblyId | nonnegative int32 |
| Installed | byte, strictly 0 or 1; since v113 must equal AssemblyId > 0 |
| ScalarCount | byte, 0–8 on framing; exact catalog count required before application |
| Scalars | ScalarCount float32 values, in factory catalog order |
| Position, Rotation | vec3 + quaternion |
| ParentKind | v115 byte: 0 none/unresolved, 1 native part, 2 vehicle |
| ParentId | v115 uint32: persistent native part body ID or vehicle ID |
| ParentPath | v115 uint16 UTF-8 byte length + relative transform path, ≤512 bytes |
| LocalPosition, LocalRotation, LocalScale | v115 vec3 + quaternion + vec3 relative to that parent transform |
| RemovalAllowed | v117 byte, strictly 0 or 1; host native removal readiness |
| PresentationRevision | v123 uint32, ordering the complete cosmetic observation within a gameplay Revision |
| BeltVisualFlags | v123 byte: 0 means absent; otherwise bit 0 present, bit 1 visible, bit 2 running; all other bits must be zero |
| BeltVisual.Scale, Pitch, Volume, ScrollSpeed | v123 four float32 values in that order, present only when BeltVisualFlags bit 0 is set |
| CamProfile | v132 uint16 UTF-8 byte length + bytes; empty for other families, exactly eight ASCII digits for supported camshafts |
| AlternatorDamaged | v135 byte: 0 unavailable/not applicable, 1 false, 2 true; required nonzero for attached supported alternators |

The entire v123 prefix is preserved. Including the uint16 message ID, v135 size is
`102 + UTF8(NativeId).Length + UTF8(ParentPath).Length + 4*ScalarCount + CamProfile.Length`
bytes without a belt visual record, plus 16 bytes when present. PresentationRevision,
the flags byte, CamProfile length and AlternatorDamaged byte are always required. Framing permits only an
empty profile or eight ASCII digits; the replica additionally enforces the family's
required/unsupported profile. This
message uses reliable ordered channel 0 for live publication, join and object resync.
Scalars/positions must be finite and quaternion squared norms between 0.9 and
1.1. Retained states own both counters, arrays and optional visual records.

Revision orders gameplay: native identity, assembly, scalars, CamProfile, AlternatorDamaged, attachment and
RemovalAllowed. A true RemovalAllowed requires a fitted part, valid attachment and
catalog Tightness in [0, 1); contradictory states are rejected. Gameplay changes and
body replacement advance Revision, including a fit/remove cycle between polls whose
final saved values match. This does not reset native identity. PresentationRevision
starts at 1 and advances on every gameplay or cosmetic change. Cosmetic-only changes
leave Revision unchanged, so animation, sound and scrolling cannot invalidate an
otherwise current PartFitRequest.ExpectedRevision. Motion through world space and
input counter values do not themselves advance either counter.

Both counters use unsigned modular ordering: a forward difference from 1 through
Int32.MaxValue is newer. An older gameplay Revision is rejected regardless of the
presentation counter. A newer gameplay Revision accepts the complete validated
snapshot and establishes its PresentationRevision baseline, including a zero value.
At equal gameplay Revision, all gameplay values must match, and an older
PresentationRevision is rejected. A newer PresentationRevision may change only the
cosmetic record. Equal values of both counters require identical gameplay and
cosmetics, but may refresh the world creation pose. Snapshot observation never
acknowledges a broadcast owed to other guests; publication tracks both sent counters.
Session retirement rejects pending materialization and all later replays.

Only the catalogued FANBELT0 rule supports BeltVisual. A present record requires
Installed=true and a valid attachment address; it cannot accompany a loose
part or an unresolved ParentKind=None. Absence remains valid when the native visual
binding is unavailable. Running requires Visible. Scale must be finite and within
[0.0999, 1.1001], allowing native float rounding around 0.1–1.1; Pitch is finite in
[0, 3], Volume finite in [0, 1] and ScrollSpeed finite in [-10000, 10000]. Flags 2,
4, 5 and 6 are invalid, as are unknown bits. The four floats are required even when
a present visual is hidden or stopped.
Framing validates these values and attachment presence; the replica also checks
the factory's visual profile. A newer absent record clears an earlier visual, and
delayed or conflicting equal-counter records cannot restart it. Join, deferred
materialization and object resync retain the same complete observation.

This record carries presentation only. Native saved Wear remains in Scalars; only
host native actions can wear, break or detach the belt. ScrollSpeed is the host's
native texture movement rate; the guest integrates its own texture phase. Texture
offset and pulse phase are not transmitted and cannot churn the presentation revision
each frame. Guest pulse timing is cosmetic
and must not use native wear/breakage FSMs or mutate preserved guest engine references.
The host's observed audio values also carry the result of its native seized-component
sound behavior without exposing that simulation to the guest.

Every validated native creation tail captures its own New/ID; it does not wait
for factory Idle, which would lose earlier products in a multi-output loop. Binding
waits for the part's completed native save identity. Direct bag factories use their
validated creation profile rather than the boxed-content action sequence. Guest
factories pause at Idle after saved loading, and pending local outputs settle before
materialization. Supported saved guest originals are retained inactive and release
their identities for isolated host copies; disconnect restores the originals.
Discovery and factory-output tracking retain the persistent Data FSM, including
saved fitted parts whose Rigidbody has already been destroyed by their mount.

Native lifetime uses Data existence, Consumed, AssemblyID and presence of the current
Rigidbody. Positive AssemblyID is fitted with or without a body; AssemblyID=0 with no
body is a transition, not disposal. Only destruction of Data or Consumed retires the
part. When removal supplies a new Rigidbody, loose-item tracking binds that body and
forgets the previous physics ownership/cargo state. Fitted/transitioning parts reject
item transforms, cargo pins and loose-item pose snapshots; fitted replacement state
continues publishing from Data's transform. The host's observed assembly phase,
rather than the native Data.Installed scratch variable, supplies the wire flag.

A missing loose part requires AssemblyId=0, Installed=false and native Tightness=0.
A missing fitted part requires a validated attachment and a ready parent. The exact
prefab's FSMs are disabled before Instantiate. The guest then
runs native identity/presentation initialization with host saved floats, replacing
the save-existence test and suppressing load/save/delete and assembly side effects.
Child assembly/bolt FSMs remain disabled and generic FSM registration excludes the
replica. Normal loose-item ownership, transforms and carrying use its stable body ID.
No change is made to the guest's saved same-ID object or factory counter.

The host resolves Data.InstallPoint to its native mount Data, requiring ActivePart
to reference the same part, Installed=true and AssemblyPoint to match the actual
parent transform. This avoids capturing the intermediate fitting frame before
native reparenting. The nearest enclosing native part or tracked vehicle supplies
ParentId; ParentPath uses the same indexed sibling segments as native FSM IDs.
The guest resolves within that root only: no absolute paths, traversal, backslashes,
colons or control characters. Ambiguous sibling paths do not bind. Unknown parent
kinds, self-parenting and known attachment cycles are rejected before replacing
accepted state. Local scale must be finite and strictly positive. None/unresolved
requires zero ParentId, empty path, zero local position, identity local rotation and
unit scale; a loose part cannot carry an attachment.

Ready fitted copies are parented at the host's local pose/scale, become untagged,
kinematic and non-colliding, and leave item/cargo authority. Only contained native
SetRotation presentation actions are replayed for saved alternator adjustments;
the catalogued distributor timing pivot also follows accepted SparkAngle.
No mount Installed/ActivePart/physics/engine values
are assigned and no native INSTALL is executed on the guest. This is fitted
presentation, not an operational reconstructed engine or guest installation.

**Spark-plug boxes and individual outputs (v143):** the package profile now
contains 31 families and the replacement profile contains 33. The new box uses
Spawner/CreateItems::Sparkplugs, prefix sparkplugbox0 and fixed capacity 4.
PackageState (184) identity uses that exact factory path/FSM, independently of
Spawner/CreateItems::Sparkplug, which produces SPRKPLUG0 replacement parts (185).
The part scalar order is Wear, Tightness, Durability; the four-slot family is
Sparkplugs. It has AssemblyID and no native Installed bool. These identities
remain distinct from generic bag-spawn identities even after native display
renaming to spark plug box(Clone) or spark plug(VINXX).

Both local host openings and guest PackageOpenRequest (186) enter the validated
native Create Plug state. It decrements Quantity once, assigns the box Owner to
Sparkplug.SpawnPoint with SetFsmGameObject, then sends SPAWNITEM to that exact FSM.
It does not write the standard PartSpawnPoint global or MinimumWear. The host
captures the next native counter/ID and accepts only one initialized output and
one decrement. Replays return the original receipt (187) and output state, and
an empty box cannot create another plug. Existing proximity, ownership, revision,
serialization of concurrent operations and retirement rules still apply.

Guests materialize a distinct box with host quantity, suppress the local factory
and replace creation/save/deletion actions with request or safe-return hooks.
Fixed-capacity boxes do not acquire a fabricated QuantityMax variable; the native
Load clamp must match capacity 4. The guarded initial state runs before a replica
is returned, preventing a delayed Empty startup from clearing a newer quantity
receipt. Late joins and duplicate part states reuse stable box/plug identities.
Each plug keeps its host Wear/Tightness/Durability through safe native identity
initialization. Native Screw controls, complete slotted fitting/removal acceptance
and the cylinder firing/misfire inputs are not established by this change.

**Guest radiator-fan power and cooling inputs (v142; corrected v210):** two
required VIN137 sources read Installed through db_RadiatorFan::Data. Valves/Radiator fan action
0 and Cooling/Fan action 1 both output Installed1 once on state entry. Their
`RadiatorFan137` factory VINP must resolve to the exact live nested mount under
VIN1010/WaterpumpParent/VINP_RadiatorFan. VIN127's factory mount is the separate
VINP_WaterpumpPulley; its receipt cannot supply this input. The original v142
profile contained fifty-three sources and eighty-eight reads across nine
consumers. Wear/Tightness arrays remain unchanged; the fan inputs need only
validated installation.

Each consumer has an independent inert bool proxy, supplied only by a unique,
accepted and applied host attachment. Pending, conflicting, stale, mismatched,
unfitted or disconnected parts expose false. The guest's saved mount, ActivePart,
scalars and native writer targets remain unchanged. Native scratch can be reused
by the fan-belt reader without coupling the two source proxies.

Native Valves skips fan load when the belt is absent (+0.13 PowerAdd); a fitted
belt with no fan adds +0.09; both fitted add zero. Cooling/Fan first multiplies
its existing CoolingFanRate by WaterLevel. Only when both fan and belt are fitted
does the native action replace that value with RPM/CoolingFanModifier. These
operations and downstream state boundaries remain native. Full cooling and
overheating, fan visuals, remaining engine inputs and two-player operation still
require separate acceptance.

**Guest piston combustion and smoke inputs (v141):** eight required VIN103
sources cover all four Pistons slots in Cylinders and Mixture. Cylinders/Reset
reads Wear at zero-based indices 5–8 into Piston1Wear–Piston4Wear. Cylinder1–4
each read Installed at index 2 into the shared Installed1 scratch. Mixture/Pistons
reads Wear at indices 0/2/4/6 into Math1. Fifty-one sources now contain eighty-six
reads across nine consumers.

AssemblyDatabase's Pistons array must contain the exact four live mounts under
VIN1010/PistonPivots (1-4 for slots 1/4, 2-3 for slots 2/3). Each source checks
the native template family, current slot table, accepted and applied assembly
indices, replica identity/revision and actual fitted parent. Native slotted Data
contains AssemblyID rather than Installed; Installed is supplied only after
those attachment checks pass. No native INSTALL event or saved-mount Installed
write occurs. Wear-only Mixture sources retain the same internal attachment gate.
Missing, pending, conflicting or disconnected slots expose false installation
and zero wear, independently in both consumers.

Only the native reads update scratch. Cylinders skips a piston with Wear <10;
at exactly 10 it still proceeds if the other cylinder prerequisites pass. Native
efficiency arithmetic still includes piston wear alongside spark-plug condition
and BaseEfficiency. Mixture takes Oil smoke when any piston Wear <10, preserving
the same boundary. Its saved oil/contamination writers remain blocked on guests.
Smoke rendering, downstream mixture/engine operation, host wear during guest
driving and live two-player acceptance are separate work.

**Guest main-bearing oil-pressure inputs (v140):** five required VIN104 sources
read each bearing's Wear in Wearing's Pressure leak state. Slots 1–5 map to
zero-based action indices 3/5/7/9/11, references db_MainBearing1–5, and the shared
native Condition output. The catalog retains the five MainBearings array entries
at CARPARTS/StartParts/VIN1010/VINP_Mainbearing1–5 and uses AssemblyDatabase as the
mount source. Wear/Tightness arrays remain unchanged. Forty-three sources contain
seventy-four native reads across eight consumers.

Slot shape/order, unique live mounts, template ArrayReference/scalar availability,
accepted assembly index, applied Data assembly index, identity, current revision
and actual fitted parent must agree. Missing, pending, conflicting, mismatched
or disconnected parts expose zero wear. Each slot has an independent inert Data
proxy; normal GetFsmFloat actions alone update shared Condition scratch. No Bolted
value is derived for bearing wear. Rocker inputs retain their existing native
tightness-to-Bolted validation and behavior.

Wearing still adds the crankshaft and five bearing conditions, divides by 100,
clamps using RPM/8000 and the native maximum of 1, then writes PressureLeak to
the separate Pressure FSM. Saved mount Wear/ActivePart and native wear writers
remain protected. RedLining tightness/failure behavior, downstream pressure/flow
simulation and host wear progression while guests drive are separate work.

**Guest head gasket, thermostat and oil-filter inputs (v139):** five required
sources add eight one-shot native Data reads. VIN134/VIN129/VIN128 keep their
Wear/Tightness arrays; OILFILTR0 keeps Dirt/Tightness, with no invented Wear field.

| Family | Consumer | Native reads (zero-based action index) |
|---|---|---|
| VIN134 head gasket | Cylinders | Head gasket #0 Installed → Installed1; Gasket damage #0 Wear → Wear |
| VIN134 head gasket | Oil | Headgasket #0 Installed → Installed1 |
| VIN129 thermostat | Cooling | Thermostat #0 Installed → Installed1; Thermostat #5 Wear → Wear |
| VIN128 thermostat housing | Cooling | Housing tightness #0 Tightness → Tightness1 |
| OILFILTR0 oil filter | Oil | Oilfilter leak #0 Tightness → Tightness; Oil filter #1 Dirt → Dirt |

The profile has thirty-eight sources and sixty-nine reads across eight consumers.
Factory VINP and consumer references must resolve the same live mount. Each
source requires a unique accepted host attachment, matching applied revision,
native identity and fitted parent. Missing, pending, conflicting or disconnected
sources expose Installed=false and zero scalars; float-only readers retain the
internal installation gate. Latest accepted tightening receipts can supersede
the snapshot's Tightness. Source projection leaves native scratch values alone
until the corresponding GetFsmBool/GetFsmFloat runs. Saved Data, ActivePart,
consumer source references and protected native writers retain their original targets.

Native comparisons remain unchanged: gasket Wear <=1 takes the failure branch;
thermostat Wear <7 closes it, 7 <= Wear <15 opens it, and Wear >=15 uses the
temperature/opening comparison (equal or colder closes). An absent thermostat
takes the open branch. Housing Tightness <16 takes the leak branch; oil-filter
leak rate is (8 - Tightness) /800, and Dirt >100 takes the contamination branch.
These are bounded input integrations, not completed guest engine operation or
physical failure presentation.

**Guest crankshaft and auxiliary-drive inputs (v138):** seven required sources
add eight native reads from existing replacement state 185. All four families keep
their Wear/Tightness scalar arrays. Factory VINP and consumer references must agree
on the live mount, including nested CrankshaftParent/VINP_CrankPulley addresses.

| Family | Consumer | Native reads (zero-based action index) |
|---|---|---|
| VIN102 crankshaft | Cylinders | Powertrain #2 Installed → Installed3; Crank #0 Wear → Wear |
| VIN102 crankshaft | Oil | Crank wear #0 Wear → Wear |
| VIN102 crankshaft | Wearing | Pressure leak #1 Wear → Condition |
| VIN105 crank pulley | Cylinders | Powertrain #6 Installed → Installed7 |
| VIN109 auxiliary sprocket | Cylinders | Powertrain #5 Installed → Installed6 |
| VIN110 auxiliary shaft | Cylinders | Powertrain #4 Installed → Installed5 |
| VIN110 auxiliary shaft | FuelLine | Fuel Usage #9 Wear → AuxShaftWear |

Every entry uses one-shot GetFsmBool/GetFsmFloat actions on Data. The profile now
has thirty-three sources across eight consumers, including fifteen in Cylinders.
Independent proxies require a unique accepted/applied host attachment and retain
normal scratch ownership; missing, pending, conflicting, mismatched or disconnected
parts expose Installed=false and zero wear. Saved mounts, references and native
wear writers remain protected. Wear-only sources retain the internal installation
gate without inventing additional native bool readers.

Native Powertrain decisions remain intact. Crank fails at Wear <1; Oil's crank
wear branch shakes at Wear <=5; FuelLine takes its low-fuel branch when auxiliary
shaft Wear <2. Wearing combines crank condition with five native bearing reads
before its original pressure division/clamp. This does not implement main-bearing
inputs, the remaining cylinder-head dependency, engine rotation, physical failure
presentation or full guest-driving wear/operation.

**Guest timing-belt combustion inputs (v137):** VIN107 retains Scalars in the
order Wear, Tightness. A required guestEngineInputs entry reads Installed at
Cylinders Powertrain #1 into Installed2, and Wear at Timing belt #0 into Wear.
Both are native one-shot reads of Data through db_TimingBelt, matched against the
factory's VINP reference to the live VINP_TimingBelt mount. The profile now has
twenty-six sources across eight consumers, including eleven sources in Cylinders.

A unique accepted, applied host attachment supplies the owned bool/float proxy.
Missing, pending, conflicting, hidden, identity-mismatched or disconnected belts
supply Installed=false and Wear=0. Native scratch updates only at normal reads.
The native Powertrain gate, strict Wear <1 broken-belt comparison and subsequent
cam ValveTolerance <=1 branch remain unchanged. Timing belt wear still performs
its native RPM arithmetic, with guest writes to saved belt/distributor Data
suppressed. State 185 continues to carry actual host wear without new fields.

This covers combustion inputs. TimingData/RotateEngine, physical failure
presentation, remaining engine inputs and actual host wear under guest driving
still need separate implementation or acceptance.

**Guest fan-belt inputs (v136):** FANBELT0 keeps its existing Wear/Tightness
scalar array and v123 presentation record. Four required input entries project
Installed into six native GetFsmBool actions at the live VINP_FanBelt mount:
Oil Fan belt #0; Valves Fan belt #0; Cooling Water Pump 2 #0 and Fan #3;
Electrics Check alternator #2 and State 2 #1. The source names retain the game's
exact casing. The profile now has twenty-five sources across eight consumers.

Each consumer owns an independent inert bool proxy. Missing, pending, conflicting,
unapplied or disconnected host belts stay absent. Actual native installation
branches retain the game's load, power, circulation, fan cooling and charging
calculations. Native scratch changes only on its normal reads; saved mount data,
ActivePart references and wear writers stay protected.

Accepted presentation-only receipts and duplicates may refresh an already applied
belt display without queuing gameplay reapplication. This requires the matching
applied revision, active replica identity/body, fitted presentation and resolved
actual parent, with no pending gameplay/repair work. Display failure remains
independent of engine installation. Missing visual sources retry separately;
changed gameplay, identity, attachment or pending repair still requires normal
application. The ledger continues to reject same-revision gameplay conflicts.

TimingData/RotateEngine belt readers, the separate timing belt, radiator-fan
installation inputs, battery/wiring, complete engine operation and guest-driving
host wear still need separate work and two-player acceptance.

**Guest alternator electrical inputs (v135):** VIN133 and ALTERNATOR0 now publish
Scalars in the order Wear, Tightness, SettingRotation, Friction, Durability,
Efficiency. Every state 185 appends one byte after CamProfile: 0 means no fitted
alternator damage state, 1 healthy (false), 2 damaged (true). Other values are
invalid. Unsupported families and loose/unresolved alternators require 0; accepted
attached alternators require 1 or 2. Missing trailing bytes are rejected.
AlternatorDamaged is gameplay state: changes advance Revision, same-revision
conflicts are rejected, and state copies retain it. No message ID is added.

Host capture reads actual part Durability/Efficiency and the mount's Damaged bool.
Damage is captured only after the attachment resolves and native InstallPoint,
factory reference, actual ActivePart, Installed, AssemblyPoint and hierarchy agree.
Missing or ambiguous damage bindings disable that replacement factory rather than
publishing an invented healthy flag. Incomplete fitting carries no attachment or
damage flag. Guests never apply this flag to saved mount Data or invent it on the
loose replica; only the owned electrical input proxy uses it.

The Electrics entry requires both alternator factories and all seven native reads:
Check alternator #1 Efficiency and #3 Installed; Alternator damage #0 Damaged;
State 1 #1 Durability; Alternator eff #0 Wear; Run on alternator #2 Damaged; State 2
#0 Installed. Repeated fields retain distinct action bindings and may share their
native output only for the same field. The proxy has one typed value per field.
The profile now has twenty-one sources across eight consumers. Missing, pending,
conflicting, unapplied or disconnected sources expose Installed=false, Damaged=true
and zero floats, including at the second running damage check. Native writes stay
protected, native scratch changes only when read, and signature failures pause
only the affected consumer.

Native electrical damage/repair branches, durability multiplication, voltage and
charging arithmetic remain active. In the audited build Efficiency is read but
the later calculation uses constant 350; the mod preserves this behavior. Battery,
belts, wiring, complete electrical integration and guest-driving host wear still
need separate work and two-player acceptance.

**Guest alternator mechanical inputs (v134):** VIN133 and ALTERNATOR0 publish
Scalars in the order Wear, Tightness, SettingRotation, Friction. Existing scalar
indices, framing, the CamProfile suffix and message IDs are unchanged. Friction
comes from actual host part Data, advances the gameplay revision when changed,
and is applied only to the owned replica.

One required guestEngineInputs entry accepts both factories at the same live
VINP_Alternator mount. Oil now has three distinct part sources; the complete
profile has twenty sources across seven consumers. Starting engine #3 reads
Friction into AlternatorFrictionRate; Alternator #1/#3 read Installed/Wear into
Installed1/Wear. These are native one-shot GetFsmBool/GetFsmFloat actions.
Identity, unique accepted attachment and applied-revision gates match earlier
input sources. Pending, loose, removed, conflicting or unavailable parts expose
neutral inputs. Native scratch, saved Data/ActivePart and writer targets remain
unchanged; invalid signatures pause only the affected consumer.

Native Oil selects Alternator seize at Wear <=5, Wearing 3 above 5 and Fan belt
when absent. The audited build reads AlternatorFrictionRate but does not use it
in another Oil action: normal load still uses native Amperes/2 clamped to [0,.2],
and seizure sets .2. This projection preserves those calculations. Electrical
Efficiency/Durability and mount-owned Damaged reads, belt/wiring dependencies,
full engine operation and host wear while guests drive remain separate work.

**Guest rocker inputs (v133):** VIN117 retains Scalars in the order Wear, Tightness.
Eight required source entries extend guestEngineInputs to nineteen sources across
seven native consumers. Each entry has slotIndex 1–8 and uses the existing
replacement family's Rockers array with eight slots. The global AssemblyDatabase
must resolve to CORRIS/AssembyDatabase. Its unique native PlayMakerArrayListProxy
must have nine entries: null at zero and eight distinct live mount objects. The
entry's selected array mount must match its native Cylinders source and asset leaf.
The factory's VINP is null for this family and is not a slot selector.

At CORRIS/Simulation/Engine/Combustion::Cylinders, Cylinder1 through Cylinder4
read Rocker1/Rocker2, Rocker3/Rocker4, Rocker5/Rocker6 and Rocker7/Rocker8 respectively.
Each state's action #4 reads exhaust Bolted into Installed3; #5 reads intake
Bolted into Installed4. All are one-shot native GetFsmBool reads. Their mount paths
are under the VIN1110 head's ValvesExhaust/CylNExh or ValvesIntake/CylNIn branches;
relative attachment identities survive head movement into the car.

Every slot has its own inert Installed/Bolted proxy. Only one accepted, fully
applied and correctly owned fitted replica at that exact attachment can supply it.
Host AssemblyId, applied replica AssemblyID and slotIndex must agree, and the
replica's ArrayReference must remain Rockers. Bolted is true only when latest
receipt-ordered Tightness is finite and at least 1. Unavailable, removed, pending,
misassigned or conflicting parts supply false. Missing or malformed native bindings
pause Cylinders until repaired; unrelated native consumers keep running.

The actual VIN117 template's Tightness? comparison, transitions and Bolted/Unbolted
writers are validated against this derivation. The native cylinder gate still
checks both rocker inputs alongside its piston/spark-plug dependencies. Projection
changes only reader-local targets and owned proxies, never saved mount Bolted,
Installed, ActivePart, slot arrays or native scratch outside normal reads. The
mounts' asset AssemblyID defaults are not used as slot identities. No native
installation or bolt replay runs on guest saved parts. Other engine inputs,
host wear progression while guests drive and two-player acceptance remain open.

**Guest camshaft inputs (v132):** VIN115 and CAMTUNEa0/b0/c0/d0 publish Scalars in
the order Wear, Tightness, Durability, ValveTolerance, plus actual part Data.CamProfile.
The profile is gameplay state, copied through publication, retained state, join/resync
and owned replica application. It cannot change at an equal gameplay revision.
Factory defaults are evidence, not a substitute for the host's current values.

At v132, eleven required source entries spanned seven native consumers. All five cam factories
must agree with db_Camshaft on the live VINP reference. Its asset address is
CARPARTS/StartParts/VIN1110/CamParent/VINP_CamshaftSprocket/VINP_CamShaft; the native
cylinder head supplies the relative attachment root even after moving into the car.
At CORRIS/Simulation/Engine/Combustion, Cylinders reads Installed at Powertrain#3,
ValveTolerance at Break 2#1 and Wear at Cam wear#0, sharing its graph with the
distributor. Wearing at CORRIS/Simulation/Engine/Oil reads Durability at State 4#0,
alongside both pump sources. Valves at CORRIS/Simulation/Engine/Valves reads
ValveTolerance and CamProfile at Get cam profile#0/#1. The latter is the native
GetFsmString; the other fields retain their exact bool/float signatures and all
six reads are one-shot.

Typed action-local proxies preserve saved mounts, ActivePart and wear writers.
Each source requires exactly one accepted, fully applied, fitted, correctly owned
host replica. Pending/conflicting variants and unavailable identities close input.
Absent proxies use Installed=false, zero floats and CamProfile="00000000", allowing
native substring/int/float conversion without borrowing saved cam data. Normal
native entries alone update calculation scratch; changes do not synthesize replay.
Native cam wear <= 7 selects damage, broken-belt ValveTolerance <= 1 proceeds,
and valve comparisons use the host tolerance. These projections do not complete
other engine inputs or host wear progression while a guest drives.

**Guest oil-pump inputs and shared consumers (v131):** VIN132 publishes Scalars
in the order Wear, Tightness, Durability. The host reads actual part Data, matching
the native mount copy from ActivePart. Existing count/finite validation applies.

At v131 the required guestEngineInputs profile had eight source entries in six native
consumers. Oil at CORRIS/Simulation/Engine/Oil combines its existing water-pump
source with VIN132 Installed at Oil pump?#0 and Wear at #2. Wearing at that same
path combines the fuel-pump source with VIN132 Durability at State 4#2. These three
one-shot readers use db_Oilpump and the live factory VINP reference; the asset mount
is CARPARTS/StartParts/VIN1010/VINP_Oilpump.

A native FSM may contain several input sources. Each source keeps its own proxy,
factory/identity/attachment validation and original reader wrappers. Source target
variables and action slots must be unique within the consumer; different states
may use the same native output locals. Oil intentionally shares Installed1/Wear
between its oil-pump and water-pump reads. Preparation does not write those locals.
All source bindings must validate before a blocked native graph can resume. A
broken source pauses its whole native consumer; other consumers continue receiving
host values. Recovery rebuilds only the affected source proxy, and teardown keeps
the consumer protected until every retained target is restored. Missing/pending/
removed parts supply neutral input without deleting the other source's values.

Native Oil selects No circulation when the pump is absent and Starving below Wear
13; equality follows the normal Friction branch. Wearing multiplies Wear2 by host
Durability while its saved pump-wear write stays disabled. Publication, live block
movement and saved originals follow the existing ownership gates. Other engine
inputs and host wear while a guest drives remain unfinished.

**Guest stock/racing fuel-pump inputs (v130):** VIN125 and FUELPUMP0 publish Scalars
in the order Wear, Tightness, Durability, OutputRate. The host reads actual part
Data, matching native mount Install 2 copying both values from ActivePart. Existing
scalar count and finite-value validation apply to both families.

The guestEngineInputs profile now requires six consumers. Two new entries use
primary familyPrefix VIN125 with alternateFamilies [FUELPUMP0]. FuelLine reads
Installed at Fuel Pump#0, Wear at Fuel Usage#8 and OutputRate at State 2#1. Wearing
reads only Durability at State 4#1. Each resolves the same live VINP_Fuelpump through
both native factories and db_Fuelpump, and all four reads are one-shot. Missing
variants, missing published fields, invalid mount references or duplicate consumers
reject the dependent profile. Unrelated families cannot declare alternatives.

Each consumer retains a stable independent proxy across stock/racing replacement.
Only one accepted, current applied replica at the matching attachment may supply
its values. A second occupant, including a pending different variant, closes the
input; removing the old part cannot expose an unapplied new part. Selected factory
identity must match the accepted state. Both factories must be valid and agree on
the live mount. Wearing's scalar-only reader still uses the internal Installed
gate to neutralize Durability when no eligible host pump exists; it adds no native
Installed read. Saved part/mount Data, shared references and wear-write targets
remain untouched.

Native FuelLine keeps its missing-pump shutdown, Wear < 5 starvation branch,
Wear/8000 efficiency calculation clamped to 0.006..1, and Power > PumpRate capacity
comparison. Wearing multiplies Wear2 by host pump Durability while saved wear
writes remain suppressed. New inputs wait for normal reads without altering native
scratch or forcing ignition/state replay. Other fuel/engine dependencies and host
wear progression while guests drive remain unfinished.

**Guest water-pump engine inputs (v129):** VIN126 publishes Scalars in the order
Wear, Tightness, Durability, Efficiency. The host reads the actual part Data;
native mount Install 2#2/#3 copies Durability/Efficiency from ActivePart. Incomplete
scalar arrays are rejected by the existing family validation.

The required guestEngineInputs profile now contains four unique consumers:
distributor Cylinders, Starter, water-pump Oil, and water-pump Cooling. A family
may supply both audited consumers; each has an independent inert proxy and reader
validation. Oil reads Installed at Water Pump#1, Wear at Water Pump#3, and Durability
at Starting engine#4. Cooling reads Installed at Water Pump 2#3, Wear at #6,
Efficiency at #5, and Tightness at Pump tightness#0. All seven reads are one-shot;
shared output variables are left to native action execution. Only these owner
wrappers move to proxy Data; shared db_Waterpump and protected writes retain their
original targets. Current applied revision, owned identity, unique attachment,
removal/pending and disconnect gates apply to both consumers.

Native Oil decides seizure at Wear <= 5 and calculates wear from water pressure
and host durability while its saved-wear writer stays disabled. Cooling closes
circulation below Wear 7 and otherwise uses host Efficiency, subject to its native
belt/RPM gates. Pump tightness uses the latest accepted bolt receipt; the native
comparison against 32 and loose-pump leak arithmetic remain intact. These values
follow the audited game logic, including its threshold differing from the mount's
TightnessMax 24. New host state never synthesizes native entry or rewrites scratch.
Other cooling/engine dependencies, host wear while a guest drives and full engine
operation remain unfinished.

**Guest starter engine inputs (v128):** VIN130 now publishes Scalars in the order
Wear, Tightness, Durability. Durability is read from the actual host part Data;
the native mount copies that field from its installed ActivePart in Install 2.
It is not taken from a guest prefab or an assumed mount default. Existing scalar
count and finite-value validation reject incomplete/invalid starter payloads.

The required guestEngineInputs profile has distinct distributor and starter
consumer entries. Starter::Wiring#3 reads Installed into Installed5;
Starter damage#0 reads Wear; Starter damage#1 reads Durability into
StarterDurability. Those three read actions get independent target wrappers to
one inert starter Data proxy containing Installed/Wear/Durability. The shared
db_Starter reference, saved mount/part Data and native wear-write target stay
unchanged. The same fully applied revision, unique live attachment, identity,
pending/removal/retirement and disconnect gates used by the distributor apply.

Starter inputs refresh at the next native read entry; existing starting attempts
retain their calculation scratch. Native wiring and wear checks still decide
whether to proceed, including the strict Wear > 25 healthy branch. Late refitting
does not force a start. Native durability arithmetic runs while its saved-wear
writer remains suppressed. Wiring, battery, block, flywheel, gearbox and other
engine inputs still require separate integration. This is not a complete starting
or engine simulation reconstruction.

**Guest distributor engine inputs (v127):** the `guestEngineInputs`
catalog is required for guest admission, resolves VIN131 to its existing factory identity and selects exactly four
native `Cylinders` read actions: Installed (Ignition#1), SparkAngle (Spark angle?#1),
Tightness (Distributor tight?#0), and Wear (Damage?#0). The reader graph must also
belong to `guestEngineProtection.writers`; selected readers cannot overlap its
suppressed scalar/pose actions. Each read gets its own owned target wrapper to a
separate, disabled Data FSM containing only those four values. Shared
`db_Distributor`, native saved Data/ActivePart, installation fields, physics,
saved scalars and native write-action targets remain unchanged.

The proxy represents a distributor only after the latest accepted state 185 has
been fully applied to a single owned fitted replica at the matching host attachment
address. Its parent must be the live mount shared by the factory's VINP reference
and the reader's native db_Distributor; an absolute StartParts path is insufficient
because fitting the block moves that hierarchy. Stale applied revisions,
ambiguous occupancy, incomplete materialization and failed bindings cannot expose
the guest's saved distributor as a fallback. Initial absence, loose/pending state
or retirement instead supplies Installed=false and zero scalar inputs. Tightness
uses the latest accepted bolt receipt when it supersedes the state 185 scalar,
matching the existing replacement bolt-resolution path.

Projection changes only the values read by normal native actions. It does not
overwrite shared calculation scratch variables or force a combustion cycle.
A running Cylinders graph observes changed input on its normal approximately
one-second cycle; after native Not ok, the normal ignition restart is still
required. Signature failures defer protected engine entry and preserve successful
write guards. Cleanup restores the action target wrappers before destroying owned
proxies. This bounded distributor input path does not establish complete guest
engine reconstruction or host wear progression while a guest drives.

An unresolved, inactive, missing or occupied mount stays pending; an existing copy
is detached, hidden and removed from item authority. A different fitted guest-save
part under the same parent is preserved rather than overlaid. Parent availability
is rechecked, including after local hierarchy loss. A later loose result restores
the copy's original scale/tag/collider settings, loose body tracking and current host
world pose without an old ownership/cargo lease. Retiring a parent first detaches
owned child copies; retiring a copy remains terminal. Disconnect detaches owned
copies before deleting them, leaving native parent objects intact.

Host garbage keeps Data alive and sets Consumed for native SAVEGAME deletion. Guest
replica garbage sends an ordinary ItemDespawn request, retaining its body until the
host accepts. The host checks existing ownership/fresh proximity plus a current loose
replacement state, applies native GARBAGE, and echoes accepted removal to **all** peers,
including the requester. Existing optimistic item adapters tolerate that idempotent
echo. Rejected requests leave the replacement intact. Disconnect removes owned hooks,
destroys temporary copies and restores factory FSMs without reloading native saves.

Operational guest engine references, other non-box part creation and native
two-player/save tests remain open. Oilfilter Screw input/presentation uses v122
operations 4/5; fitted fan-belt cosmetics use the v123 record above. Guest original
isolation and ordinary replacement bolt controls are implemented after 0.1.32. Guest opening is implemented in v112; v114
restores individual bolt arrays for native parts already present on both peers.

### Native bolt authority and reconciliation v114

Post-0.1.32 implementation note (still protocol 118): owned replacement copies use
these same 41/44/123 messages and stable part/child IDs for spanner/ratchet input.
They do not predict native turns or run parent/engine actions. Their presentation
arrays and poses change only from host absolute replies. Tool picks wait for a fresh
host observation after an attachment change; older queued observations are discarded
and the existing targeted object-state request obtains a current bolt reply. Receipt
ordering also includes accepted replacement-part revisions (185), preventing a later
application of an older replacement scalar from rolling back a bolt's parent total.
No fields, message IDs or wire authority semantics change in this implementation.

BoltState (44) preserves its eight-byte legacy payload and appends the parent
Data.Tightness float; including the message ID it is 14 bytes. WorldBoltSnapshot
(123) preserves its count and complete legacy entry block, then appends one float
per entry in entry order: `4 + 12 * count` bytes including the message ID, maximum
964 bytes for 80 entries. Both use reliable ordered channel 0. ScrewInt is reserved
zero; it is never applied as a relative direction. Tightness must be 0–8 and the
parent total finite. Duplicate IDs or any invalid entry reject an entire chunk
before writes. No retired message IDs are reused.

Authenticated guest turns (41) require a living player's pose at most two seconds
old, within three metres of a ready fitted bolt. The host executes the native
TIGHTEN/UNTIGHTEN action chain immediately and coalesces settled observations for
broadcast to all guests. An unready intent is discarded, not saved for a later
installation. Guest BoltState messages request the current host result and cannot
assign tightness, parent totals or turn direction. Host raw events are not sent to
guests, so a predicted turn is never incremented a second time by an echo.

On receipt, the adapter writes the native `Bolts[Index]` integer array and
BoltTightness, restores TightnessF using the divisor read from Calc pos (normally
−400), and assigns the absolute parent total. It enters Set pos and, for ordinary
bolts, invokes the native Data.BOLTING check to update mount tightness, Bolted and
collider effects. This never replays Screw or adds to the aggregate. Fully loose
zeros are real live/snapshot/targeted records. Applying an identical result does
not replay effects. Alternator adjustment bolts have no parent increment; the
clutch-plate variant has no divide/position action, so its divisor is one.

Unready entries retain complete values and retry; a newer successful result clears
an older pending result for that bolt. A session-local receipt order shared with
PartState/WorldPartSnapshot prevents an older delayed sibling bolt or part scalar
from rolling back a newer parent total. Receipt order is not an added wire field.
Pending values and receipt history clear at teardown. The world checksum includes
bolt ID, tightness and parent total rounded to 0.001; last turn direction is excluded.

Bindings validate the native action chain and live array reference before enabling
each bolt adapter. Build 23268598 has two at-limit ADJUST routes (crank pulley and
camshaft sprocket): normal turns remain supported, but extra guest tightening at
eight is stopped before the mount timing action, with the host also rejecting it.
Engine timing replication remains separate unfinished work. The MUDFLAPa0 prefab
has an unresolved ThisPart reference; its adapter fails closed if that reference is
still absent at runtime. Continuous VIN106 drain and VIN209 alignment controls do
not match this integer-step adapter and remain separate work. This does not create missing fitted guest graphs or isolate
pre-existing guest saves. Native two-player, LOD and save verification remain open.

### Persistent native part identities v110

A native assembly part is identified by its cataloged Data/ID and matching save
keys, not its display name or current position. The `partIdentity` bindings require
AssemblyID and Consumed variables, UTAssemblyID = ID + AID and UTPos = ID + POS.
Since v114 the optional Installed scratch bool is not an identity requirement;
this covers all 192 native Data prefabs, including 36 previously excluded parts.
Registration defers until native initialization/load has finished. IDs are opaque
ASCII alphanumeric save identifiers, starting with a letter and ending in a digit,
limited to 128 characters. Counter zero is valid for original parts. The whole
`ALTERNATOR01` is preserved; its factory prefix contains a zero. Missing proof,
changed IDs and duplicate live owners cannot fall back to position ordinals.

- Part body ID: `FNV1a32("part:" + nativeId)`.
- Part FSM ID: `FNV1a32("part-fsm:" + itemIdDecimal + ":" + relativePath + "::" + fsmName)`.
- `relativePath` excludes the native part root and all its ancestors. For the root
  Data FSM it is empty. For a child screw it may be `Bolts/BoltPM[1]`; normal sibling
  suffixes distinguish identical bolt names within one prefab. Nested part Data
  roots establish their own identity before looking at an enclosing assembly.

PartState (45), WorldPartSnapshot (124), generic FSM state/raw events (40/41),
BoltState (44), bolt snapshots (123), and registered child controls use these FSM
IDs. ItemTransform (42), item snapshots (122), cargo and removal use the body ID.
Non-part objects retain existing identity rules. Grocery spill capture, adoption,
stale-clone reuse and template cloning exclude native part graphs, including
uninitialized prefabs. Their native save/installation graphs need a dedicated
contents adapter and cannot be recreated by common display name.

The generic FSM registry now removes its own callbacks and registration marks on
session teardown. A failed partial registration is cleaned before retry. Native and
other-subsystem actions remain in place; reconnect can register parts, bolts and
other generic FSMs again without abandoned callbacks sending duplicate events.

This corrects routing for parts present on both peers. It does not materialize
missing fitted parts, synchronize complete installation references, or isolate
the guest's entire native part save graph. v111 adds loose creation, v112 guest box
opening, and v114 individual bolt arrays on existing native parts. Complete guest
assembly and native disposal/reconnect tests remain pending.

### Native car-part scalars v107

Part `Data.Wear` uses native condition values, often about 0–100: the installed
VIN133 alternator prefab initializes it with RandomFloat(90, 99). Treating that
value as 0–1 encoded every healthy part as 255 and then wrote **1** on the receiver.
The same saturation hid real wear differences from the world checksum.

PartState (45) retains `netId:u32, flags:u8, tightness:u8, wear:u8`, then appends
`tightnessValue:f32, wearValue:f32`. The legacy hints preserve their old encoding
but have no authority. WorldPartSnapshot (124) retains its count and complete
seven-byte legacy entry block, then appends two floats per entry in entry order.
Including the two-byte message ID, a PartState is 17 bytes and a full 80-entry
snapshot is 1,204 bytes. Both use reliable channel 0. No new IDs are allocated.

Native floats are applied directly, with no unit or percentage conversion. Only
finite values and known flags (bit 0 installed) are admitted; finite native values
slightly past thresholds are preserved. Snapshot IDs must be unique, and an invalid
entry rejects the whole snapshot before any scalar writes or pending replacement.
Zero wear/tightness and uninstalled parts are included, so resync can clear stale
nonzero guest state. Pending values retain full floats and retry after a registered
FSM becomes available. A newer successful application clears older pending data.
An object-state request for a part returns the known FSM state followed by scalars;
FSM state alone cannot restore wear.

A guest PartState is now an observation request for a nearby cataloged part with a
fresh player pose, not authority to assign Installed/Tightness/Wear. Existing native
install/bolt events execute on the host. Reports coalesce by part ID, and the host
samples after processing pending interaction events, then sends its current state
to **all** guests, including the sender. Local host settle hooks use the same deferred
sampling. Neither a guest's random initial wear nor its reported wear/tightness can
rewrite the host. Fitted-engine wear continues through the separately validated
vehicle damage authority path. Pending reports clear on session teardown.

The world checksum hashes native part flags, tightness and wear rounded to 0.001
for comparison only; wire/apply remains full precision. Signed zero is canonical.
This detects 95 versus 99 wear without treating tiny float noise as a desync.
Native two-player bolt/install/load/repair/save verification remains required.
This correction is a prerequisite for package contents; it does not add package
creation, opening or persistent car-part identity replication.

### Native trophy factory manifests v106

`ItemSpawn` (52, reliable ordered channel 0) gains `FlagFactory = 2`. No fields
are added. When set, `containerNetId` is FNV-1a32 of `factoryPath + "::" + fsmName`
from the matching catalog `trophyFactories` entry. Each entry's `templateName`
contains its **native persistent ID**, not a display name or fuzzy template hint.
`netId` is FNV-1a32 of `"factory:" + invariantDecimal(containerNetId) + ":" + nativeId`.
The native ID must be the catalog prefix followed by a canonical positive Int32
counter (no sign, padding, whitespace or non-ASCII digits). Gold, silver and bronze
awards from different race classes remain distinct despite shared visible names.
`stateName` is the catalog creation state; `offerSeq` must be zero and the manifest
must contain 1–32 entries. Guests reject unknown factories, incorrect prefixes or
hashes, and the ordinary malformed pose/flag/count checks before accepting a receipt.
Factory manifests are host output only; `SpawnIntent` cannot request a native award.

The host observes the factory's direct `New` output after its five creation/name
actions finish, waits for native initialization, and binds its persistent identity
to ordinary item movement. Existing saved trophies are discovered by native `Use.ID`,
so snapshots include awards loaded before this session. Live manifests use a minted
factory epoch; join/resync chunks set both factory and replay flags and epoch zero.
Multiple replay chunks can share that key. Factory receipts deduplicate by live
item identity, not epoch, so a wrapping epoch cannot drop a new native item;
missing live replicas can recover, and session removals always take precedence.

Guests wait for their local factory to finish loading and pause it at idle. They
retain descriptors until the matching factory binds, instantiate its exact prefab
with the persistence-only `Use` FSM disabled before cloning, and set display name,
scale, pose and normal item authority explicitly. This also covers the ice-race/rally
prefabs whose native initialization has no initial wait. The prefab component's
original enabled value is restored immediately after the synchronous copy. Guests
hide and pause their own saved trophies without rebinding them to host IDs; teardown
destroys replicas, restores those saved objects/FSMs and removes factory hooks.

This covers the 15 trophy factories under Amateur, Junior, Icerace, RallyAMA and
RallyJR. It does not add missing race outcome authority or support moose meat,
parts packages or spray cans. Those need separate contents/condition/save adapters.
Native two-player creation, save isolation, recovery and reconnect tests remain open.

### Shared-item recovery v105

No fields or IDs are added. On item-group resync, the host sends item poses,
session removal chunks (125), then refreshed `ItemSpawn` replays (52, flag bit 0).
It excludes retired IDs and destroyed bodies from poses/manifests. Both ordinary
ItemDespawn and removal snapshots establish terminal IDs on guests even if the
body has not been created. Deferred creation checks retirement at execution time;
old poses/replays cannot revive an ID. Lifecycle state clears on disconnect or
scene teardown; rejected guest removal requests do not establish host retirement.

Original bag-spill manifests remain deduplicated by container/epoch. Replays can repeat that
key: existing live bodies remain untouched, missing bodies may be adopted/created,
and destroyed registrations are discarded before recreation. Repeated pending
replays coalesce without extending the materialization deadline. A failed template
lookup can retry on the next item resync. This reuses the existing template lookup;
it does not add new game-event spawner coverage.

Before consuming a manifest key, guests validate flags, the existing 32-entry cap,
unique entry IDs, nonempty template names up to 128 characters without controls,
state name up to 128 characters, finite positions/quaternions and quaternion squared
norm within [0.9,1.1]. Materialization normalizes accepted quaternions. Empty
acknowledgments for unsuccessful guest offers remain valid.

### Hockey v104 complete betting board

Message 160 retains the v88 prefix in order: `sequence:u16`, `latestRound:i32`,
`gameIndex:i32`, `team1Id:i32`, `team2Id:i32`, `team1Odds:f32`, `team2Odds:f32`,
`tieOdds:f32`, `result:string`, `flags:u8` (only bit 0, native KurPaWins).
The team IDs, scalar odds, result string and cursor describe temporary native FSM
variables, not six games. Guests do not overwrite that paused calculation cursor.
`LatestRound` can be zero after loading; it is not a completion signal.

v104 appends, in this order, with no array counts:

| Field | Wire shape | Native source / order |
|---|---|---|
| gamesPlayed | i32 | Runkosarja GamesPlayed |
| pairs | 12 u8 | Runkosarja PairsNew, adjacent home/away IDs |
| previousPairs | 12 u8 | Runkosarja Pairs |
| odds | 18 f32 | Betting Hashtables 0–5, each in key order `1`, `X`, `2` |
| results | 6 u8 | Betting ResultsGame, ASCII `1`, `X`, or `2` |
| resultOdds | 6 f32 | Betting ResultsOdds for the previous results, independent of new odds |
| scores | 6 strings | Runkosarja ResultsPair, at most 16 characters each |
| standings | 48 strings | Four blocks of 12: Order, GamesString, GoalsString, PointsString; at most 80 characters each |

Total frame size is 273 bytes plus the UTF-8 bytes of the prefix result, scores
and standings. LatestRound/GamesPlayed are nonnegative. Upcoming pairs must be a
permutation of IDs 0–11; previous IDs must be in range but may repeat in vanilla's
initial history. Collection odds are finite and within [1,100]; scratch odds may
also be zero. Prefix gameIndex is 0–6 and team IDs 0–11. All text excludes control
characters and surrogate code units; the prefix result is at most 256 characters.
Malformed boards do not consume sequence numbers. Sequence uses unsigned 16-bit
forward half-range ordering, with an explicit unset state so zero after wrap is
neither an initial-state sentinel nor permission to accept stale traffic.

The host only samples when both native load/calculation pipelines are in their
catalogued stable states. A join during calculation can use the last complete
board, without consuming the next ordinary broadcast. Guests buffer an owned
copy, wait for their local pipelines to finish, preserve their collections and
pause both generators. They update the existing live collection instances and
refresh visible teletext pages 240/241/302 without replaying ROUND, ODDS,
CHECKMEGAVETO or SAVEGAME. Disconnect restores guest data before resuming native
FSMs. The individual-player scoring table remains outside this snapshot, as do
Megaveto ticket identity, selections, purchases and claims (R2.21).

### Lotto v103 tickets and collection

`lottoTickets` catalog bindings name the native Pay button, factory, prefab fields,
three ArrayLists and collection-box actions. Host and guests hold Pay/Check money
until a receipt, capturing completed rows before any native debit. Each paid row
contains seven distinct integers in 1..39. Unpaid rows are zeroed, including a
partially edited next row. The host derives the cost from the catalog (native
3 mk per row) and verifies the request's round against Numbers/CurrentRound and
a fresh player pose within 6 m of the native spawn point.

The host creates the factory's actual prefab with those captured rows, its own
next persistent ID and its own spawn pose. It preserves the host's open form.
Factory save loading must have reached Idle with SaveID initialized to the
prefab name before issuing anything. A missing/unready factory is retryable and
consumes neither money nor a receipt. Native ID counter and ticket Use save
handling remain host-owned; no guest-provided ID can mint a ticket. Existing
saved host tickets are discovered after their native loading completes.

Requests carry a nonzero connection-lifetime token and wrapping uint sequence.
The latest receipt per authenticated player stores a copy of the complete request.
An exact retry returns that receipt without spawning or paying again; changed
payloads at the same sequence, old sequences and an unadmitted token change are
stale. Admission clears command deduplication but **never** lifetime ticket
retirement. A new press after a decline uses a new sequence. An unready host
keeps the original request pending instead of guessing an outcome.

Collection intercepts only Lotto objects in the native TrashTrigger. The host
validates the player's fresh pose within 6 m of the box and its own ticket body
within 3 m, and waits for Data/State 16 before reading native Winnings. Guest
requests carry no prize amount. Values below zero yield no payment; prizes below
1,000 mk credit shared cash and prizes at or above 1,000 mk credit shared bank.
Unrepresentable float money changes are declined without consuming the ticket.
An accepted claim first records terminal retirement and sets the native deletion
sentinel (TicketRound=8888), then commits the balance and runs the native garbage
transition. This protects duplicate/concurrent claims and guest reconnects.
Ordinary ItemDespawn for a ticket also runs its native retirement on the host
instead of destroying the save-handling FSM; discarding pays nothing.
Native save/load supplies outstanding tickets across host restarts; save testing
must verify deletion of claimed ticket keys. Bank statement text and achievement
presentation are not yet replayed by this handler.

Guests retain copied per-ticket states until bindings exist. Sequences are checked
per ticket; round/rows cannot change for an existing ID and a retirement can never
be undone by a later live packet. Invalid IDs, numeric fields, array sizes or poses
do not consume the receive baseline. Pending and repeated states do not teleport
an existing body: the regular item ownership/transform/snapshot path moves it.
The latest host state recreates a missing guest body. Ticket keepalives run every
15 seconds; changes are checked twice a second, and snapshots never consume a
connected guest's change baseline.

Guest replicas use the actual native prefab, with local prize calculation and
all ticket load/save/delete actions removed or paused. The guest's own saved
tickets are hidden while replicas exist and restored on teardown; replicas are
destroyed. Main form and claim hooks are restored, including a pending request's
held state. Binding failures contain the fault to Lotto ticket interactions.
This is implemented but requires the two-player/save/LOD matrix in BUILDING.md;
Megaveto purchase selections and claims are not covered by messages 181–183.

### Host-authoritative shopping bags v120

Bag item IDs use `FactoryItemIdentity.ItemId(factoryId, nativeId)`, where factoryId
hashes the catalog factory path and FSM name. The bag's native `Use.ID` survives
pickup/reparenting and display-name changes. A BagState requires a matching derived
item ID, valid catalog prefix, finite pose and condition 0–100. Identity fields are
immutable; stale revisions and contradictory equal-revision inventory are ignored.
Join, item resync and targeted replies include bag states and terminal removals.
Guest save bags are preserved inactive; isolated replicas skip native save/loading
and inventory mutation while retaining native short/long use-button gestures.

The host authenticates the requester, validates the observed revision, remaining
inventory, a living guest pose ≤2 seconds old within 3 m, and current bag ownership.
Host and guest openings share one reservation. Native contents factories use shared
scratch globals, so spills are serialized until their native work and exact output
capture finish. The host sets `CurrentBag` explicitly and enters the validated
one/all spill state; the player-input `Confirm` state is never replayed remotely.
A duplicate request recovers its immutable receipt; busy/rejected sequences cannot
become successful later. Changed revisions cannot spend the same inventory twice.
The capture survives timeouts while native work remains active.

The scanner never names bag outputs by hierarchy or position ordinal. Native product
names are discovered from the factory prefab's display-name actions, including
`chips` → `potato chips(itemx)`. Remote materialization retries unresolved entries
at 0.5-second intervals, drops retired/live entries, and reports failures at most
once every 30 seconds. Pickup guards prevent a remotely held bag from attaching to
the local hand; accepted remote ownership/removal releases that exact held bag.

Unsupported native part products fail opening preflight before inventory mutation.
In v121, fan belts and oil filters use their validated replacement-part factories.
The bag capture counts each native output but does not reserve a transient item ID.
Completion requires the recorded factory and initialized output Data/ID to produce
a replacement state. Fitting/removal may replace its Rigidbody; a currently loose
part must match the body's current native Data binding. Host retirement also
completes that output without resurrection. A permanently failed factory leaves its
native output on the host, fails the opening and releases the global opening lock
after publishing valid outputs. The host publishes each valid state on channel 0 before completing
the capture; retries retain the same part identity. These parts never enter the
generic ItemSpawn manifest and recover through replacement join/resync state 185.
This adapter does not yet establish complete condition/state replication for every
possible product in a mixed shopping bag; see the building guide's acceptance matrix.

### Native moose-meat output v224

`MooseMeatState` (210, reliable ordered channel 0, admitted host → guests) carries:
`factoryId uint32`, `nativeId string`, `revision uint32`, `position vec3`,
`rotation quat`, `condition float32`, `kind byte`. Kind is 0 raw, 1 rotten raw,
2 grilled/edible, 3 charred, 4 spoiled grilled. Condition is finite in 0–100;
poses must be finite with quaternion squared norm in 0.9–1.1. Only kinds 0–4,
nonzero factory IDs and nonempty native IDs up to 128 characters without whitespace
or control characters are accepted. Runtime additionally requires the catalog
`mooseMeat` factory hash and its prefix plus a canonical positive Int32 counter.
Factory/item hashes use `FactoryItemIdentity`, as with trophies. Message 52's
factory flag remains trophy-only; meat state supplies its own creation descriptor.

The host captures the direct New output after native creation/naming and waits
for native initialization. Saved meat is recovered by Use.ID. The same state
supplies live output, join, item-group resync and individual object resync.
Revisions advance only when food presentation/condition changes; equal revisions
may refresh creation poses but must agree on food. Serial arithmetic rejects old
and half-range revisions. Existing bodies never snap to these creation poses;
ordinary item transforms handle movement. Missing bodies can be recreated;
retired IDs cannot be revived by late or repeated states.

Guest native save items remain hidden and paused during the session, then restore
on disconnect. Replicas bypass native load/save and independent cooking/spoilage.
The host supplies display names/material variants and condition; grilled replicas
retain native eating input/effects and existing ItemDespawn authority validation.
Guest factory execution is paused after its own saved items load. Guest corpse
chopping intents are not added by this message.
