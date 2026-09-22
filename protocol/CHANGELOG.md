# WinterMP protocol history

Per-version wire changes, newest first. Many message layouts from v121 onward are
specified only here; `PROTOCOL.md`'s message table points at the version entry.

## v257 — R20 battery boxes and persistent loose cells

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

## v256 — advert-job telephone enrolment

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

## v255 — engine-oil cap and conserved refill

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

## v254 — saved motor-oil containers

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

## v253 — shared advert delivery and native mailbox accounting

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

## v252 — single-use light-bulb boxes and loose bulb condition

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

## v251 — host-authoritative train

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

## v250 — home coffee preparation and drinking

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

## v249 — human taxi passengers

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

## v248 — shared sausage package conversion

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

## v247 — tractor trailer connection and physics delegation

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

## v246 — household fuse holders

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

## v245 — native taxi payday and salary letter

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

## v244 — host-selected taxi luggage

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

## v243 — shared physical taxi receipt

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

## v242 — native host taxi quote and cash collection

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

## v241 — shared taxi duty controls and native host meter

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

## v240 — shared taxi availability, incoming calls and customer presentation

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

## v239 — shared ignition-wire installation and native destruction

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

## v238 — fuse boxes and persistent loose supplies

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

## v237 — shared flea listings and exact sale retirement

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

## v236 — paid flea-table rental and proceeds receipts

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

## v235 — shared ATF cap position on the Corris

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

## v234 — guest ATF refill and shared bottle contents

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

## v233 — shared home stove controls and native cooking heat

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

## v232 — host-confirmed native tyre punctures

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

## v231 — shared flatbed wood delivery

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

## v230 — authoritative phone invoices and payments

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

## v229 — terminal permadeath runs

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

## v228 — host corpse and guest chopping

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

## v227 — native permadeath settings

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

## v226 — native passenger death and recovery

Death report/event (29/30) now retires passenger occupancy at native State 3,
before movement components are destroyed, with Take photo retained as an
idempotent fallback. The inactive death graph is bound before activation.
PlayerRespawn (31) requires an active GAME player with enabled native movement
components and a finished death graph; guests also finish their spawn selection
and relocation. A newspaper timeout never establishes life or publishes a cached
death pose. Local death remains pending through the native MainMenu/load path.
Layouts, channels and message IDs are unchanged; next free ID remains 213.

## v225 — acknowledged electricity payments

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

## v223 — effective electricity and bill cutoff (historical layout)

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

## v222 — cylinder-head fastening

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

## v221 — exact Sorbet parking brake setting

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-wheel-health-inputs-protocol-209-unreleased).

The preceding v208 wheel-rim replay correction used the existing `VehicleCondition` (65)
rim flags and ownership/availability rules without changing message layouts or meanings.
It kept protocol 208 and next free ID 205, correcting native state entry and
wheel physics application, with repair and durable tyre wear still unfinished.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#wheel-rim-presentation-protocol-208-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-gearbox-failure-wear-protocol-208-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-automatic-gearbox-oil-use-protocol-207-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-gearbox-oil-input-protocol-206-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guarded-guest-drivetrain-wear-consumers-protocol-205-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-drivetrain-wear-publication-protocol-204-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-periodic-drivetrain-wear-protocol-203-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-differential-speed-telemetry-protocol-202-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-periodic-drivetrain-write-protection-protocol-201-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-confirmed-condition-release-protocol-200-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-condition-claim-inputs-protocol-199-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#observer-gearbox-condition-input-protocol-198-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#condition-availability-and-late-discovery-protocol-197-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-wheel-pressure-application-protocol-196-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#parked-observer-tyre-health-protocol-195-unreleased).

v194 projects accepted VehicleCondition wheel health into the eight audited
native guest observer Health readers. The current registered vehicle and current
condition owner must match; local/pre-claim drivers, missing state, ownership
changes and disconnected sessions retain native lookup. PUNCTURE entry can read
the validated application before its accepted copy is committed. Shared health
never writes native saved TireHealth or the reader source/cache. No layout or ID
changes; both peers require protocol 194. Driver input bootstrapping, physical
flat/rim reconciliation, authoritative wear and parked publication remain open.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#observer-tyre-health-inputs-protocol-194-unreleased).

v193 extends guest saved-part protection to the four native Corris wheel
Condition graphs and GearboxDamage.Damage. Guest admission retires ongoing
TireHealth wear, flat-entry TireHealth=0 and reverse-gear Wear subtraction;
subsequent state entries retain that protection. Solo/host native writers still
run. This changes guest simulation behavior, so both peers require protocol 193.
Message layouts, IDs and the v192 VehicleCondition stream rules are unchanged;
mod remains 0.1.33 and next free message ID remains 201. Native condition inputs,
flat/rim physics, parked publication and host wear under guest driving remain
unfinished. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-tyre-and-gearbox-write-protection-protocol-193-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#condition-stream-ownership-protocol-192-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#passenger-death-and-respawn-protocol-191-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#passenger-cabin-heating-protocol-190-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-body-warmth-persistence-protocol-189-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#passenger-condensation-inputs-protocol-188-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-condensation-presentation-protocol-187-unreleased).

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
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#vehicle-climate-occupancy-isolation-protocol-186-unreleased).

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
disconnected delivery is rejected. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#vehicle-climate-ownership-protocol-185-unreleased).

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
climate graphs. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#independent-window-ice-protocol-184-unreleased).

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
