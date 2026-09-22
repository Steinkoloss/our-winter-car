# Catalog binding notes

Per-system notes for `sync-catalog.json` bindings. Generic rules: [README.md](README.md).

## Lottery forms stay local

The Lotto and Megaveto form-opening `Use` FSMs and Lotto ticket inspection are
intentionally absent from `buys[]`. Their Wait button/Wait button 2 states run
while hovering, before GetMouseButtonDown/GetButtonDown emits USE. Registering
those states as purchase guards makes hovering send repeated intents; registering
their form-open result also changes other players' camera/menu state. Keep the
form and selected numbers local until a dedicated purchase request supplies the
complete selection to the host. The separate Sheets/Pay bindings still exist;
they do not yet prove complete ticket-purchase or claim authority (R2.21).

Audit guards after changing catalog buys:

```bash
python3 tools/check_fsm_bindings.py /path/to/action-evidence.json --purchase-catalog catalog/sync-catalog.json
```

This mode flags an entry guard still waiting for its own input event. A valid
purchase guard belongs after input; a pure form opener belongs outside buys.
Use a fresh F9 dump with action parameters or an `extract_fsm_assets.py` result.
Unmatched rules, inferred templates and incomplete evidence are reported as
unverified. A zero exit status only means no confirmed premature-input guard;
it does not validate amounts, spawn data, result replay or payout accounting.

## Hockey betting bindings (v104)

`hockeyBetting` binds Betting::Logic and Runkosarja::Data. Nine list slots are
PairsNew, Pairs, ResultsGame, ResultsOdds, ResultsPair, Order, GamesString,
GoalsString and PointsString. Slots 2/3 belong to Betting; the rest to Runkosarja.
Six Hashtable proxies on Betting (references 0–5) supply odds in key order 1/X/2.
Read their live `arrayList` / `hashTable` properties each time; save loading can
replace a collection. Serialized preFill values and UT save tags are not live state.

Build 23268598 samples are complete only in Betting State 1/State 6 together with
Runkosarja Day/Time/Reset points. LatestRound changes during individual matches;
CHECKMEGAVETO occurs before the new odds are generated. Neither indicates an
atomic completed board. SAVEGAME terminals are not sampled as completion states.
Teletext 240/241/302 Texts descendants only format/read data and cycle display
subpages; refreshing an active Texts root updates their one-shot readers.

After a game patch, extract `--match HockeyGames --include-array-lists
--include-hash-tables` and the three `PAGES/240`, `PAGES/241`, `PAGES/302` subtrees.
Verify proxy types, table keys, typed variables, completed states and all display
actions before changing bindings. The extractor preserves Hashtable key/type/
default evidence alongside ArrayLists; neither replaces a running-game check.
Megaveto's six selections, persistent ticket factory and native collection box are
separate from this board snapshot (R2.21).

## Lotto draw bindings (v102)

`lottoDraw` maps the one Numbers FSM and its teletext Texts subtree. Scalar slots
are CurrentRound, TicketRound, NationalPot, NationalPotMin, NationalPotFull;
winner-variable and prize/list tier order is 7, 6+bonus, 6, 5, 4. Four distinct
ArrayList reference names map Results, ResultsBonus, ResultsLinesWinnings and
ResultsLinesWon (lengths 7/3/5/5). Bind the live `arrayList` property, never a
preFill list. `UTNational7` is the save tag `Lotto7`, not draw data.

Installed build 23268598 action evidence (level2 SHA256
`36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`) shows unique
RandomInt draws in 1..39, both lists sorted before tier calculations, and complete
states Reset points/Day/Time/State 35. DrawDone becomes true at draw start.
Reset points temporarily hides `Systems/TV/Teletext/VKTekstiTV/PAGES/301/Texts`;
its six descendant FSMs only read/format display data when enabled. The adapter
mirrors host activeSelf and refreshes that subtree after writing complete data,
so pausing the guest Numbers FSM cannot freeze its reset-on-exit hide forever.

After a game patch, extract `--match Lottery --match Lotto --match Megaveto
--include-array-lists` and `--match PAGES/301` with `extract_fsm_assets.py`.
Recheck scalar types, array references/sizes, draw bounds, sorting, prize tier
order, completed states and display-only actions together. Do not add draw/
calculation states to stableStates or turn ticket-accounting events into display
refreshes. The catalog hash gates mismatched bindings. Ticket purchase and claim
authority remains separate work in roadmap R2.21.

## Rally progress bindings (v101)

`rallyProgress.stages` lists SS1/SS2/SS3 in fixed wire order, their Timing FSM paths,
start-line paths and ordered checkpoint names (4/6/4 in build 23268598). Each name
is both a direct marker child and a bool variable in its parent Timing FSM. The
adapter waits for the complete configured stage before observing it.

The marker's own `Checkpoint` bool is unused. Its `Set bool` state obtains the
marker name, writes that named bool on Timing and ends in terminal `Idle`. Timing
clears its bools in `Finish`; observing the terminal marker state preserves the last
crossing. `commitState` and `completedState` define that flow; runtime binding checks
that the commit leads to the terminal state. SS2 checkpoint 4 also activates markers
5/6 on entering `Idle`; those native actions remain intact. No FSM event is replayed.

After a game patch, extract `--match RACES/RALLY` with `extract_fsm_assets.py` and
check the marker actions, transition targets, Timing variables and complete stage
layouts together. The handshake catalog hash prevents different bindings from
connecting. See BUILDING.md for the required two-player validation.

## Ventti table bindings (v99/v100)

`venttiTable` identifies the table root, relative button/manager paths, float stake,
int hand totals and `LoseText.Status` string. The six result bindings have fixed
wire meanings: cash win/loss, car win/loss, cabin win, home loss. Empty status is
none/reset; an unknown nonempty status is logged and not published. Result strings
and FSM paths must be distinct, and the resolver must match `venttiProperty`.
The installed build 23268598 action evidence supplies these bindings.

`venttiTable.rules` records the engine inputs separately from the observation
bindings: native card values (53 slots, unused zero then card ids 1–52), 50 mk
increments, the strict >4000 mk property threshold, 50 mk cash-win cap growth,
7000 mk opponent-loss cutoff, and 6000/0 loss resets for property wins/losses.
The Values proxy contains four cards of each value 1–13: aces are 1, face cards
are 11–13. These are not standard blackjack or VideoPoker card ids. The Net
ledger and Core adapter consume these rules. `idleStates`, deck paths and card
stages gate adoption until the host has loaded its progression and is between
hands. `outcomes` lists all six native state names, complete action-type layouts
and accounting/stress/speech action indices to suppress. Validate the whole
layout before removing any action; leave native property and NPC effects intact.
The manager's SAVEGAME path remains active on the host. Guest managers stay cut.

Presentation bindings specify both card roots, the Textures ArrayList proxy,
nine existing slots, material index 1 and `_MainTex`. Card ids index Textures and
Values identically. Mouse pick distance and the local interaction/stress globals
are catalog data. A new layout or missing asset must disable/wait for the Ventti
adapter without permitting native guest wallet or random draws.

`venttiTable.reactions` adds v100 pose and sound bindings. The first three pose
paths are disjoint world-space NPC/table/chair roots; remaining paths enumerate
all NPC descendants in parent-first order, including inactive bones, gaze targets,
smoking objects and hand colliders. The current layout has 46 poses; the wire cap
is 56 to fit an unreliable Steam packet. Root/ordered pose/ordered sound paths
form a layout hash, so a changed layout cannot silently remap received slots.

The 21 sound paths identify native AudioSources under `MasterAudio`, not clip
names: variation `Pig/pig1` owns clip `pig01`. Six `sources` describe checked
MasterAudioPlaySound actions, their origin, group, fixed variation or local string
variable, and delay. Hooks observe the host's already selected variation. Guest
receives live-only cues, never native FSM events. Invalid audio bindings isolate
audio failure; the pose stream and gameplay remain active. All pose targets must
bind before guest physics/animation writers are paused. Teardown restores them.

Refresh the FSM, ArrayList and transform evidence together after a game patch:

```bash
python tools/extract_fsm_assets.py /path/to/game --match RoomVenttiPig \
  --include-array-lists --include-transforms --out /tmp/ventti.json
```

The optional `arrayLists` output preserves `preFillType`, count and all pre-fill
lists rather than inferring the active list from stale editor defaults. It remains
asset evidence, not runtime/save data. Check the native Values and Textures arrays
and card object ids together before changing the stable catalog card slots.
The optional `transforms` output includes full paths, local poses, activeSelf and
component types. Check the complete NPC descendant set and the source actions
against this evidence before changing the reaction layout.

The legacy table observation is observational: the game can announce a result before committing the
payout. Replication must never enter a native win/loss state or enable global UI.
The guest resolver stays disabled; original table variables return at teardown.
The v99 host ledger owns leased controls and settlement; v100 mirrors host NPC
reactions. R2.12 still requires the two-player runtime matrix in BUILDING.md.

## Ventti property bindings (v97)

`venttiProperty` identifies the native resolver and three global integer keys in
fixed wire order: Ruscko, Satsuma, Home. Those are the game's actual legacy variable
names. Access slots are cabin Sleep activeSelf, woodstove hatch Handle activeSelf,
and Logwall Use enabled. Sleep is a parent without its own FSM; the binder finds it
through SleepTrigger/Activate, including while the parent is inactive. Missing
access bindings stay unknown until they load. Key and access bindings must be
nonempty and distinct; do not reorder the wire slots.

Refresh these paths and globals against installed action evidence after a patch.
The Win/Lose car/house states also alter stress, dialogue and save points; property
replication applies only the keys and the three access slots, with the guest
resolver suppressed. Save-point activation stays on the host, which owns saving.
Guest original values are restored on teardown. See COVERAGE-ROADMAP R2.11 and
BUILDING.md for the required two-player verification.

## Lotto tickets (`lottoTickets`, v103)

This section replaces the generic `LottoTicket/Pay` buy rule. It supplies exact
form, factory and collection-box paths/FSM names; typed variable bindings;
three fixed seven-slot ArrayList names; native lifecycle states; line price
(3 mk) and bank-payment boundary (1,000 mk). Wire slot order is line1, line2,
line3. Runtime checks verify the Pay and claim action shapes before replacing
the accounting actions. Inputs and pending receipts stay local to the actor.

The factory's `Prefab` points into `sharedassets3.assets`, distinct from the
scene's docked ticket. Use `extract_fsm_assets.py --asset sharedassets3.assets
--match LotteryTicket --include-array-lists` to inspect its actual persistence
and initialization actions. Host IDs concatenate native SaveID and the next
ObjectNumberInt; guest replicas never load or save those IDs. Native Winnings
is sampled after Data/State 16, and native TicketRound=8888 retires a claimed
ticket for saving. Per-ticket metadata uses 183; ordinary ItemTransform handles
carrying and dropped poses under FNV1a32("lotto:" + persistent ID).

Keep this section separate from `lottoDraw`: draw generation and ticket
transactions have independent binding/containment lifetimes. Megaveto does not
use this ledger. Runtime/save verification remains in BUILDING.md/R2.21.

## Trophy factory identities (v106)

`trophyFactories` binds 15 exact path/FSM pairs to their native ID prefixes, prefab
names and display names. Common fields identify the direct `New` output, creation
completion, factory idle state and native item initialization/persistence states.
Do not replace these with display-name matching: five different award classes use
only three display names. The adapter checks action layouts before binding and
keeps guest replicas separate from guest save objects. After game updates, extract
both `Spawner` from level2 and trophy prefabs from sharedassets3.assets; the v106
checklist in `docs/BUILDING.md` records the verified hashes and native checks.

## Parts-package factories (v109)

`partsPackages` maps 30 factory FSMs under `Spawner/CreatePartsPackages` to the
actual boxed-prefab name (the native save-ID prefix) and referenced contents
factory path. Prefixes retain their trailing zero where present; `boxtimingbelt`
has no trailing zero. The parser rejects duplicate factories and overlapping
counter prefixes. Live matching requires both Use.ID and CreateItemsDB; display
names cannot identify packages. Common bindings name factory output/creation, item
initialization, readiness, garbage and save/delete states. Capacities are required: pistons 4, main bearings 5, rockers
8 and all other boxes 1. Runtime validation checks the exact factory action sequence,
prefab capacity and contents reference before enabling that factory adapter.
`SyncCatalogJson.Factories.cs` reads this section and the trophy section; its actual source is included in protocol tests.

The bindings were checked against level2 and sharedassets3.assets in build 23268598.
BrakeBiasRegulator is a direct part; Plugwires has no prefab/spawner references.
These are excluded, not silently treated as boxes. PackageState now creates the
exact host box and mirrors quantity; guest factories and local saved boxes are
preserved separately. v112 adds guest opening intents; v111 adds a dedicated loose
replacement-part adapter. Complete assembly replication remains unfinished. See the v109 native
verification checklist in `docs/BUILDING.md`.

## Native part save identities (v110)

`partIdentity` names the Data FSM, persistent ID, AssemblyID/Consumed fields,
assembly/position save-key variables and suffixes, and initializing states. Native
parts must supply both matching save keys before binding. Since v114 the optional
Installed scratch bool is no longer required: 36 wheel/suspension/accessory prefabs
lack it. The remaining identity shape is present across all 192 native Data prefabs
in sharedassets3.assets, including all 30 standard
package contents; the factory inventory contains 194 distinct prefab names, all
ASCII alphanumeric. Non-part factory prefabs do not match the assembly Data shape.
Original parts with a zero counter retain their exact save ID.

Since v113, `consumedVariable` also names Consumed. Fitting preserves Data but
destroys Rigidbody; removal creates a new body on that Data object. The item layer
tracks this lifetime separately from disposal. Positive AssemblyID is fitted;
Data.Installed is a temporary mount occupancy query and is not an installation flag.
All 30 replacement prefabs' Status/Idle transitions confirm that assembly-ID rule.
Saved fitted Data objects are discovered even when no root Rigidbody remains.

The root rigidbody and its Data/bolt/control FSMs have separate stable IDs. Child
paths exclude the part root and its ancestors, preserving IDs when parts move from
the garage into the engine. Duplicate same-named children keep normal sibling
suffixes. Native part graphs are excluded from generic grocery clone templates.
This is identity support; complete part creation and installation need further work.

The `bolts` state filter also encounters continuous drain/alignment controls. v114
binds only validated integer step/array chains, preserving the live native Index,
array reference, position divisor and parent aggregate behavior. Build 23268598
contains 493 matching Screw FSMs: 489 validated step bolts, one MUDFLAPa0 bolt whose
ThisPart reference is unresolved in the prefab, and three separate continuous
controls (VIN106 drain plug and two VIN209 alignment adjustments). Unsupported or
changed bindings log and disable that adapter. The two timing bolts validate their
at-limit ADJUST route but guard extra guest turns until timing state is replicated.
See the v114 checklist in `docs/BUILDING.md`; static bindings do not prove gameplay.

## Replacement-part factories (v111; presentation v115; fitting/removal v116–v118; adjustment v119/v125; direct bags v121; hand tightening v122; belt visuals v123)

`replacementParts` describes the 30 `CARPARTS/PARTSYSTEM/SPAWNERS_*::Spawn` contents
factories used by standard boxes, plus two direct `Spawner/CreateItems` rules in
v121. Per-rule `fsm` defaults to the shared `Spawn`; `bagOutput: true` selects the
native fresh-output profile with SetVelocity instead of boxed SetFsmFloat.
`Fanbelt` uses `FANBELT0` with `[Wear, Tightness]`; `Oilfilter` uses `OILFILTR0` with
`[Dirt, Tightness]`. Both pass InstallPoint from VINP and retain normal native
Create/load counters. Their original 30 boxed siblings keep the same factory IDs.

Replacement prefab Data FSMs must be initialized before validating their action
definitions, as with package and bag templates. This decodes definitions and
invokes native action Awake callbacks without starting the template or entering
its native initialization/save states. The
spawned instance still performs its own identity initialization.

Optional `disabledInitActions` lists unique zero-based indices of native `GetChild`
actions deliberately disabled in the prefab; other initialization actions cannot
be disabled through this profile. VIN103's obsolete child lookup is index 0.
Per-reference `enabled` defaults to true; VIN130's `PartBlocking` write is false
in both fresh Create product (#4) and saved Create (#2). Disabled references still
require their exact native types, fields and destination names; replica setup
leaves those destinations untouched, including when the obsolete variable is
absent from the native part (as on VIN130). The InstallPoint reference must
remain enabled. These flags were checked against build 23268598's installed
assets; native probes also reject toggled flags and changed destinations. They
do not change factory IDs, message layouts or the protocol version.

An optional `fitPrerequisite` names a native one-shot GetFsmFloat/FloatCompare gate
between Allow install? and Far. Fanbelt's `Alternator` state reads `db_Installed2` /
Data.SettingRotation into Setting and proceeds only above 6; at or below 6 returns
to Idle. Runtime checks verify the exact fields, threshold and transitions before
fitting or preserving an occupied guest mount. Oilfilter uses the usual direct flow.

Oilfilter's optional `handScrew` profile names its root `Screw` FSM, Tightness
scalar/scratch, Rot variable and the eight input, mutation, pose and cooldown
states. Only Mouse off 2, Check tool and Get scroll are ready states; Setup 2 and
Wait1 2 cannot bypass the native 0.2-second cooldown. Profiles must be nonslot,
use the rule's Tightness scalar and exclude `handRotation`; state aliases and
invalid cooldowns fail catalog loading. Runtime validation proves integer ±1
steps within 0–8, native BOLTING notifications, empty-hand/tool checks, 1 m picking
and the pose chain. Native Set divides scratch Tightness by -400, so host remote
entry first refreshes it from Data. Guest Screw stays disabled and presentation
uses resolved host Tightness: local Z rotation = Tightness × 20 degrees and
position = (0, 0, -Tightness / 400). Protocol 122 operations 4/5 in 188–189 carry
requests/receipts; state 185 carries the resulting authoritative values.
Re-extract OILFILTR0 Data/Screw and VINP_Oilfilter Data after game changes; isolated
`hand-screw-probe.json` evidence and the native probe are described in BUILDING.md.

Fanbelt's optional `beltVisual` profile identifies Data.Mesh and the mount's
FanBeltMesh reference, the separate FanBelt visual root, Mesh/FanbeltMesh renderer,
Mesh/ScaleBone, and Animations::Jumping. Only FANBELT0 may use this nonslot profile;
it excludes hand-control profiles and requires nonescaping paths with animation
logic outside the cloned Mesh subtree. The fitted visual is a SkinnedMeshRenderer
with two internal bones; there are no Animation/Animator clips. Guests clone only
the validated render hierarchy and own their material/audio. Jumping's native
wear/BREAKOFF logic never runs on guest copies. Original Jumping, renderer and audio
are separately paused/hidden/muted even when no saved guest belt exists; original
hierarchy and mount variables stay intact and flags restore after copies are removed.
Failed visual validation prevents isolation from moving a fitted saved belt. The
replica's Data.Mesh hides while fitted and returns when loose; a latest loose state,
absent visual or changed parent stops its fitted view even during deferred creation.

`scrollPath` / `scrollFsm` bind
CORRIS/Simulation/Engine/SymptomsEngine/Animations::BeltAnimation. Validate its
seven-action State 1 chain: global RPM accumulates per second, AnimMultiplier
produces Offset, and action 5 targets the exact fanbelt renderer's material 0
_MainTex X offset. The <-1000 reset and 0.1-second delay remain native. Protocol 123
appends PresentationRevision and optional BeltVisual (Visible, Running, Scale,
Pitch, Volume, ScrollSpeed) to state 185. Cosmetic freshness is ordered separately
from physical revision, preserving fitting requests during RPM/sound updates.
Host capture preserves native flutter and damaged squeal behavior;
guest UV and flutter phase use owned presentation only. Re-extract the visual
hierarchy, Jumping and BeltAnimation together; `belt-visual-probe.json` and
BUILDING.md describe the isolated probe and outstanding multiplayer acceptance.

Each rule names the exact prefab/save prefix,
ordered native SaveFloat variables, SetFsmGameObject source/target bindings, and
verified Init/Status action layouts. Bindings were extracted from build 23268598
level2 and sharedassets3; prefix digits are significant, including ALTERNATOR0.
Clutch disc, crankshaft, head gasket, starter, thermostat and timing belt also need
PartBlocking; it must not be omitted just because InstallPoint is present.

The adapter captures every Create product and saved Create output, including each
iteration of multi-product factories. Saved floats include real Wear/Tightness and
any native saved adjustment. Guest copies skip local save access, initialize native
identity and presentation, and allow loose-item carrying. They do not run native
assembly/bolt actions. v115 adds fitted presentation using the host's actual parent
root identity and relative pose/scale, and restores loose tracking on removal.

The `installPointVariable`, `mountPartVariable`, `mountPointVariable` and
`mountInstalledVariable` bindings name Data.InstallPoint and the mount's
ActivePart/AssemblyPoint/Installed proof. Native evidence covers 26 directly
referenced factory mounts, the rev limiter, and all dynamic piston/bearing/rocker
slots. A changed or unfinished mount is not guessed from the prefab's default
reference. Two alternator Status SetRotation actions target their own Pivot and
can replay the saved setting without native installation effects; the other 30
factory templates have no such rotation action.

Unknown, inactive or occupied parents defer; existing guest-save occupants remain
intact. Fitted copies follow the resolved hierarchy with no collisions or loose-item
authority. This does not populate the guest engine's operational mount/bolt graph.
v116 adds host-executed fitting requests for guest-created loose copies; v117 adds
removal requests for fitted copies; v118 adds array-slot installation. See the
v115–v118 checklists in BUILDING.md.

The `fit*` bindings describe the native ASSEMBLING → Another part? → mount CHECK /
Allow install? → Far / Near → PROCEED / Install 1 flow. Runtime validation checks
the entry's occupancy query, selected ActivePart, fixed factory InstallPoint,
native distance target/tolerance, mouse events and native AssemblyID/INSTALL commit.
The native prerequisite actions run unchanged. All 27 fixed-mount factory families
match the extracted build-23268598 flow. VIN103 pistons, VIN104 main bearings and
VIN117 rockers use the separate v118 slot selection adapter described below.
The native PickUp FSM's Part picked left click drops the held part, so the adapter
retains that frame's held candidate and accepts its own freshly released authority.
This does not grant guest replicas local engine, bolt or save authority.

The `slot*` bindings describe `CORRIS/AssembyDatabase::Installer`, the global
AssemblyDatabase reference and the part's ArrayReference. Only the three array
families provide paired `slotReference`/`slotCount`: Pistons/4, MainBearings/5 and
Rockers/8. Arrays reserve null index zero and preserve slot indices through holes;
duplicate object references or changed counts fail closed. The adapter reads the
initialized ArrayReference value: VIN103's action operand snapshots still contain
MainBearings, but its actual variable is Pistons. Never use that stale operand value.
These three templates have no Installed scratch flag. It is required only for
fixed-mount entry validation; all families use AssemblyID for replicated assembly
state. Replica initialization must tolerate its absence on slot templates.

Native ArrayListGetClosestGameObject includes occupied and inactive objects and
resolves equal distances to the later index. Read-only guest selection preserves
these rules; the host rechecks selection and rejects a different requested slot.
The shared Installer must start idle with its candidate/point/index cleared. Part
Far → Installer CHECK → Near sets the selected mount's AssemblyID/AllowInstall and
sends CHECK. Guards precede Installer/Near writes and mount/Near confirmation;
the native Allow install? checks run unchanged in the same frame. All 17 slot
mounts match the extracted action flow. Native Install 1 fetches ActivePart from
the Installer, assigns that part's InstallPoint/AssemblyID, stops the Installer
and sends INSTALL. Unconfirmed guest previews cancel immediately; committed
operations wait for the exact slot's settled attachment. Replica child assembly
FSMs remain disabled. Guest mount/save isolation and engine graphs remain open.

The `remove*` bindings describe Data.BOLTING → Tightness? → Unbolted/Bolted,
Mouse off/Mouse over and Data.Remove → current InstallPoint.REMOVE → Allow removal?.
Runtime validation checks the native Tightness < 1 comparison, the root BoxCollider,
1 m layer-19 mouse selection, right-click transition and one-shot removal target.
All 30 factory families match; VIN130 includes a native FloatClamp before the
tightness comparison. Removal uses the fitted part's current InstallPoint, so
fitted pistons, main bearings and rockers are supported without guessing a slot.
Live readiness additionally requires matching ActivePart/Installed/AssemblyPoint,
the enabled trigger collider, layer 19, Untagged and a native mouse-ready state.
Do not replace this with a direct UNINSTALL: the native collider and mount flow own
blockers, dependent removals, mass and body lifetime. The host publishes readiness
in state 185. `removeHand*` bindings select the normal empty-hand PickUp state for
guest input; the guest intersects the disabled native box analytically and leaves
replica physics and assembly FSMs disabled. See BUILDING.md for runtime checks.

## Guest package opening (v112)

The `partsPackages` opening bindings now name the two native input-ready states,
contents FSM/event, shared PartSpawnPoint, MinimumWear, factory counter/product-count
variables, Check quantity state and the mod's acceptance-feedback state. Runtime
validation checks the six one-shot native opening actions, decrement amount, target
and transition before installing guards. Static extraction verifies all 30 boxes.

The three multi-product factories have **NumberOfProducts=1 in their actual FSM
variables**. Their serialized IntCompare operand snapshots still say 4. Always read
the initialized variables; replaying those stale action defaults would overproduce.
Host/guest box opening uses the same guarded native path. Guest feedback reuses only
native audio/GUI actions after a matching acceptance, without local decrement/spawn.
Installed-part/bolt reconstruction and native two-player/save testing remain open.

## Guest replacement bolt bindings (post-0.1.32)

`replacementParts.replicaRepairVariable` names global `RepairMode`. Owned copies use
catalog-matched integer `Screw` controls only after validating the native action
layout, visual child/index substring, local bolt-size/pose space, live integer-array
slot and layer-12 SphereCollider trigger. Build 23268598 evidence matches 55 controls
across 24 boxed replacement families. Its spanner/ratchet `2Spanner/Raycast::Check`
reads `Screw.Boltsize` and sends TIGHTEN / UNTIGHTEN; the guest keeps that interface
but replaces all native child states with input/pose callbacks. Continuous rocker,
mixture and other adjustment controls remain disabled. Owned copies keep native Data save and engine actions disabled; only host bolt
results update them. Saved guest parts are now isolated and persistence is guarded
until restart; full guest engine reconstruction remains unfinished.
See the post-0.1.32 checklist in BUILDING.md for runtime acceptance cases.

## Native guest engine write protection (local fix under v126)

`guestEngineProtection.writers[]` uses exact `path`/`fsm` pairs. Each `actions[]`
entry declares `state`, zero-based `index`, `actionType`, `targetVariable`,
`targetFsm` and `targetScalar`. The original v126 profile selected 65 persistent
writes across 11 graphs. Only native SetFsmFloat, AddFsmFloat and SubtractFsmFloat
are accepted. Optional `poseActions[]` entries specify `state`, `index`,
`targetVariable` and `angleVariable` for the distributor's local SetRotation;
runtime validation checks its None axes and update flags too.

`pausedFsms[]` declares exact `path`, `fsm` and distinct `requiredStates`. The
PartFallings graph needs a full pause because it writes saved bolt arrays and
sends BREAKOFF. Writer/paused identities and scalar/pose action slots cannot
overlap. Paths, symbol names, list lengths and action indices are bounded and
validated. Missing/changed native signatures defer protected isolation and wake;
successful guards stay active. A malformed profile returns null with
`GuestEngineProtectionError`, leaving other parsed catalog subsystems available.
Guest admission prepares guards immediately after setting the persistent save
latch; failure keeps that latch. Runtime state-entry guards catch late graphs.
After a signature is repaired, only the blocked OnEnter completes, without stale
OnExit/event replay; an inactive graph waits for activation before retrying.

The [native writer audit](../build/engine-write-audit/audit-summary.json) distinguishes
protected persistent writes from delegated FuelLevel/battery consumption and
runtime scratch fields. This profile does not supply host engine simulation or
guest mount reconstruction. Validation passes 1,428 protocol/catalog/policy tests
(51 new catalog cases) and 276 isolated game checks (23 new protection checks).
[Native results and tested hashes](../build/guest-engine-smoke/result.json) record
the controlled Debug run.
Actual two-player behavior and full guest engine/host wear progression remain
unverified.

## Fuse boxes and ordinary contents (v238)

The fixed-capacity package profile also covers FusePackage on Spawner/CreateItems:
capacity 5, prefix fusepackage0, item name fuse package(Clone), `openState: "Create
Fuse"`, and `loadClampIndex: 2` (LoadTransform precedes IntClamp). Other boxes keep
their existing Create Plug and clamp-index defaults. `supplyContents` names the
ordinary output prefix fuse0, display name fuse(Clone) and ready state State 5.
The exact contents FSM is Fuse with its local SpawnPoint variable.

This selects the validated five-action Create product / two-action saved Create
factory shape, root Use identity capture, native save/deletion and guest load/save
isolation. It does not describe a car-part Data/Wear/Screw family. The output and
outer package use distinct factory IDs even though both FSMs share one object.
The existing box opening receipt binds one decrement to the next native output ID.
The original 31 package profiles remain, with this one added (32 total).
Light-bulb/R20 packages remain separate. Household fuse-holder/electrical graphs are now described under development protocol246 below.

## Spark-plug boxes and individual outputs (v143)

`partsPackages.factories[]` can override `path`, `contentsFsm` and `itemName`;
standard factories retain the existing profile defaults. The audited direct-box
shape pairs `contentsSpawnPointVariable` with `fixedCapacity: true`. Sparkplugs
uses Spawner/CreateItems, contents FSM Sparkplug, SpawnPoint and capacity 4.
Native fresh/saved SetFsmGameObject targets must reference the same contents
object. Its Use FSM lacks QuantityMax; the native Load/IntClamp maximum supplies
the validated fixed capacity. Native quantity/disposal/save keys remain intact.

The new SPRKPLUG0 replacement family uses `spawnPointVariable: "SpawnPoint"`,
mutually exclusive with `bagOutput`, and the direct CreateObject/reference/
SetVelocity output shape. It has Wear/Tightness/Durability scalars and the four
Sparkplugs slots. The native Data has AssemblyID without Installed, and Status
has no SetIsKinematic. Factory identity includes the exact FSM so the box and
individual plug can share a scene object without sharing IDs. There are now
31 package and 33 replacement families; the 53-source engine-input profile is
unchanged.

[Native fixtures](../build/sparkplug-opening-smoke/sparkplug-opening-probe.json)
retain both factories, box Use and individual Data/Screw/Wear.
[Standard-box evidence](../build/sparkplug-opening-smoke/standard-box-factories-audit.json)
verifies the fresh/saved contents bindings of all 30 earlier box factories.

Protocol 144 adds optional `replacementParts.factories[].removalLayer` (an
integer Unity layer 0–31, default 19). SPRKPLUG0 selects 12: native Screw.Set
moves a fitted plug to the tool layer and Data's mouse picks use that same layer.
Both the native pick masks and current host collider layer must match the
profile before removal is offered. Guest obstruction checks include configured
removal layers. This changes removal availability semantics without adding a
wire field. [Socket/removal evidence](../build/sparkplug-fitting-smoke/result.json)
covers all four native array entries and host readiness; guest screw-tool input
and the full fitting/removal lifecycle still require acceptance. The v144 suite
passes 1,845 Net tests, 18 launcher tests and 1,001 native checks (11 Net and
25 native checks added; all 976 previous checks retained).

The earlier v143 opening suite passed 1,834 Net tests, 18 launcher tests and 976 native checks (10 Net cases and 18 native checks
added; all 958 prior checks preserved).
[Results and hashes](../build/sparkplug-opening-smoke/result.json) describe the
actual execution boundaries. Native screw-tool controls, complete fitting/removal,
engine consumers and live two-player acceptance remain open.

## Guest radiator-fan power and cooling inputs (v142)

Two VIN137 sources read Installed through db_RadiatorFan::Data: Valves/Radiator
fan #0 and Cooling/Fan #1, both once per entry into Installed1 scratch. The native
factory is RadiatorFan137 and its VINP resolves to the nested
VIN1010/WaterpumpParent/VINP_RadiatorFan mount. Its separate PartBlocking reference
points at VINP_WaterpumpPulley. Wear/Tightness order is unchanged. The original
v142 profile had fifty-three sources and eighty-eight reads across nine consumers. Each fan
source owns a separate inert bool proxy; the belt shares native scratch only.

Protocol 210 corrects the original VIN127 mapping: WaterpumpPulley127 produces
the pulley and cannot provide fan installation. The added VIN137 rule brings
replacement factories to 39 without changing the original factory IDs. No fan
package factory exists in the audited CreatePartsPackages object, so none is
invented. The live mismatch, actual VIN137 prefab/factory definitions and new
fan/pulley separation checks are retained in `build/engine-admission-audit/`.

[Native evidence](../build/radiator-fan-engine-input-smoke/native-audit.json)
from the original v142 work retained the wrong VIN127 Data/Paint source. That
isolated run executed native belt/fan branching and arithmetic in every fitted
combination, with absent/pending/conflicting attachment and cache/restore checks.
All 1,824 Net tests, 18 launcher tests and 958 native checks pass (16 catalog cases and 38 fan checks
added; all 920 earlier checks preserved).
[Results and hashes](../build/radiator-fan-engine-input-smoke/result.json) identify
the tested Debug payload. Downstream cooling, fan presentation, remaining engine
inputs and two-player acceptance remain separate work.

## Guest piston combustion and smoke inputs (v141)

Eight VIN103 sources cover all four Pistons slots in Cylinders and Mixture.
Cylinders reads Wear in Reset #5–8 and Installed in Cylinder1–4 #2; Mixture reads
Wear in Pistons #0/2/4/6. Each source uses its exact native db_Piston reference
and AssemblyDatabase slot, including the paired PistonPivots/1-4 and /2-3 paths.
Wear/Tightness scalar order is unchanged. Native slotted Data has AssemblyID,
not Installed; the latter is supplied only after accepted/applied attachment
validation. Fifty-one sources contain eighty-six reads across nine consumers.

[Native evidence](../build/piston-engine-input-smoke/native-audit.json) includes
both consumer graphs, VIN103 Data/Wear, Spawn, all four mounts and the Pistons
array. All 1,808 Net tests, 18 launcher tests and 920 native checks pass (31 catalog
cases and 50 piston checks added; every earlier native check preserved).
[Results and hashes](../build/piston-engine-input-smoke/result.json) identify
the tested Debug payload. Cylinder-head/spark-plug and remaining inputs, smoke
rendering, physical failures, full engine operation and two-player acceptance
remain open.

## Guest main-bearing oil-pressure inputs (v140)

Five VIN104 entries identify MainBearings slots 1–5 through AssemblyDatabase.
Each Wearing/Pressure leak reader uses its own db_MainBearing reference and
VINP_Mainbearing mount, at zero-based action indices 3/5/7/9/11. All five read
Wear into the intentionally shared Condition scratch variable. Wear/Tightness
scalar order is unchanged; no bearing Bolted value is derived. The input profile
has forty-three sources and seventy-four reads across eight consumers.

[Native evidence](../build/bearing-engine-input-smoke/native-audit.json) contains
the Wearing graph, actual VIN104 Data/Wear prefabs and Spawn factory, all five
mounts, and the native MainBearings array. Runtime validation requires the real
template family/scalar and exact slot table plus an accepted, applied host part.
All 1,777 Net tests, 18 launcher tests and 870 native checks pass (23 catalog
cases and 44 bearing checks added; every earlier native check preserved).
[Results and hashes](../build/bearing-engine-input-smoke/result.json) identify
the tested Debug payload. Redlining/failure effects, downstream fluid operation,
host wear during guest driving and two-player acceptance remain open.

## Guest head gasket, thermostat and oil-filter inputs (v139)

VIN134 adds Cylinders Installed/Wear and a separate Oil Installed source. VIN129
adds Cooling Installed/Wear; VIN128 adds Cooling Tightness; OILFILTR0 adds Oil
Tightness/Dirt. Five required sources contain eight one-shot Data reads, preserving
exact native states, indices, field casing and outputs. The three VIN families
retain Wear/Tightness; the bag-spawned oil filter retains Dirt/Tightness.
There are now thirty-eight sources and sixty-nine reads across eight consumers.

[Native evidence](../build/fluid-engine-input-smoke/native-audit.json) contains
the three consumer graphs and actual factory, Data and mount evidence for all
four families, including the CreateItems oil-filter factory. All 1,754 Net tests,
18 launcher tests and 826 native checks pass (23 catalog cases and 99 fluid-input
checks added; every earlier native check preserved).
[Results and hashes](../build/fluid-engine-input-smoke/result.json) record the
isolated Debug payload. Full engine operation, downstream cooling flow, physical
failure effects and two-player acceptance remain separate work.

## Guest crankshaft and auxiliary-drive inputs (v138)

VIN102 adds Cylinders Installed/Wear plus separate Oil/Wearing Wear sources.
VIN105 and VIN109 add Cylinders Installed; VIN110 adds Cylinders Installed and
FuelLine Wear. Eight native reads form seven required sources, retaining exact
mount nesting, case-sensitive references, scalar order and native action/output
bindings. The profile now has thirty-three sources across eight consumers.

[Native evidence](../build/powertrain-engine-input-smoke/native-audit.json) contains
four consumer graphs and all four actual Spawn factories, Data prefabs and mounts.
All 1,731 Net tests, 18 launcher tests and 727 native checks pass, including 25
catalog cases and 90 powertrain checks. [Results and hashes](../build/powertrain-engine-input-smoke/result.json)
record the isolated Debug run. Remaining cylinder-head/main-bearing inputs,
rotation, physical failure effects and full two-player operation are separate work.

## Guest timing-belt combustion inputs (v137)

VIN107 adds a required Cylinders input source at the live VINP_TimingBelt mount.
Powertrain #1 reads Installed into Installed2; Timing belt #0 reads Wear into
Wear. Both native reads retain Data/db_TimingBelt, one-shot timing and distinct
action ownership. Wear/Tightness state order is unchanged; the protocol bump marks
the new engine-input semantics. The profile has twenty-six sources across eight
consumers, with eleven independent sources in Cylinders.

[Native evidence](../build/timingbelt-engine-input-smoke/native-audit.json) includes
the actual Spawn factory, loose Data, mount and combustion graph. All 1,706 Net
tests, 18 launcher tests and 637 native checks pass, including 13 catalog cases and
25 timing-belt checks. [Results and tested hashes](../build/timingbelt-engine-input-smoke/result.json)
record the isolated Debug run. TimingData/RotateEngine, physical failure effects,
remaining engine inputs and two-player operation remain separate work.

## Guest fan-belt inputs (v136)

Four `guestEngineInputs` entries bind FANBELT0 installation to Oil, Valves,
Cooling and Electrics. The six exact native bool readers retain their original
source casing, action positions, outputs and one-shot timing. Repeated Installed
reads share one bool only within their consumer's owned proxy. No scalar order or
wire layout changes; the protocol bump marks the new engine-input semantics.

The [native audit](../build/fanbelt-engine-input-smoke/native-audit.json) includes
the four consumers, direct bag factory, loose Data and fitted mount. Native belt
load/power, circulation, fan cooling and both electrical gates are tested against
host removal/refitting. Visual receipts preserve an already applied belt's engine
readiness while missing/pending gameplay remains absent. All 1,693 Net tests, 18
launcher tests and 612 native checks pass, including 17 catalog cases and 56 belt
checks. [Results and tested hashes](../build/fanbelt-engine-input-smoke/result.json)
record the isolated Debug run. TimingData/RotateEngine and timing-belt readers,
radiator-fan installation, battery/wiring and full two-player operation remain open.

## Guest alternator electrical inputs (v135)

Both VIN133 and ALTERNATOR0 now publish six scalars in order: Wear, Tightness,
SettingRotation, Friction, Durability, Efficiency. Their
`alternatorDamageVariable: "Damaged"` profile is restricted to these two unslotted
families. It captures the actual fitted host mount bool into state 185; the field
is unavailable for loose/unresolved parts and required for attached alternators.
Damaged is not invented on the loose replica or copied into the saved guest mount.

The Electrics entry includes seven native readers: Efficiency, Installed, Damaged,
Durability, Wear, Damaged, Installed. The two Damaged reads share their native
output; the two Installed reads have distinct outputs. The parser enforces audited
field multiplicities and distinct action slots; only the same field may reuse an
output. Runtime binds every reader separately to one typed proxy value per field.
Missing or pending sources use Installed=false, Damaged=true and zero floats.
Every source must validate before its consumer resumes. Twenty-one sources span
eight consumers; the native electrical wear actions keep their saved targets and
remain suppressed.

[Native evidence](../build/alternator-electrical-input-smoke/native-audit.json)
records source fields and calculations. Native Efficiency is read, but later math
uses constant 350; this adapter preserves that behavior. All 1,676 Net tests, 18 launcher tests and
556 native checks pass, including 29 new Net cases and 26 electrical checks, with
all 530 earlier native checks preserved. Net, Core Debug/Release, probe and launcher
builds have no warnings or errors; native checks used Debug. Full battery/belt/wiring integration,
other engine inputs and two-player acceptance remain open.

## Guest alternator mechanical inputs (v134)

The VIN133 Oil entry requires `alternateFamilies: ["ALTERNATOR0"]`. Both factories
publish `[Wear, Tightness, SettingRotation, Friction]` and must reference the same
live VINP_Alternator through VINP. Starting engine #3 reads Friction;
Alternator #1/#3 read Installed/Wear. Every reader is one-shot. Oil has three
independent sources, bringing the profile to twenty entries across seven consumers.
Latest accepted attachment, applied revision, identity and unique occupancy gate
each source; changed signatures or variant mounts pause the affected consumer.

The native audit confirms Wear <=5 selects seizure, and absence skips that branch.
AlternatorFrictionRate is read but not used elsewhere in Oil; native current-based
load and seizure arithmetic stay intact. Electrical Damaged is mount-owned and
absent from the loose-part Data, so it is not inferred from wear. Electrical
Efficiency/Durability/Damaged and other electrical dependencies remain unprojected.
[Evidence](../build/alternator-mechanical-input-smoke/native-audit.json) records the
actual prefab/mount/factory/consumer definitions. All 1,647 Net tests, 18 launcher tests and 530 native
checks pass, including 17 new catalog cases and 21 alternator checks with all 509
earlier native checks preserved. Net, Core Debug/Release, probe and launcher builds
have no warnings or errors; native checks used Debug. Full engine and two-player
acceptance remain open.

## Guest rocker inputs (v133)

Eight VIN117 entries add `slotIndex` 1–8 and `mountVariable: AssemblyDatabase`.
Only this audited slotted family is allowed, with `slotReference: Rockers` and
`slotCount: 8`; fixed-mount families cannot specify a slot. Every entry is required.
The profile now has nineteen sources across seven consumers, with ten sources in
Cylinders (distributor, camshaft and eight rockers).

The native Rockers array at `CORRIS/AssembyDatabase` has null index zero followed
by eight unique mount references. Its ordered pairs alternate exhaust and intake
for cylinders 1–4. Each Cylinders `CylinderN` state reads Bolted at #4/#5 into the
shared Installed3/Installed4 locals. The corresponding mounts sit below VIN1110's
ValvesExhaust/CylNExh and ValvesIntake/CylNIn branches. Factory VINP is null; runtime
uses the real global database and array rather than treating that as a fixed mount.
The native mount AssemblyID defaults are all 1 and do not identify the slot.

Actual VIN117 Data derives mount Bolted from Tightness >= 1. Runtime validates its
native FloatCompare, transitions and true/false setters before using that rule.
Projection additionally requires the accepted attachment, applied assembly index,
replica identity and ArrayReference to agree. Latest bolt receipts can change the
input without waiting for another replacement packet. Each slot retains its own
typed proxy and neutral false state; saved mounts and shared scratch stay intact.

Evidence under `build/rocker-engine-input-smoke/` includes native prefab/mount
definitions, slot-array ordering and consumer actions. All 1,630 Net tests, 18 launcher tests and 509
isolated native checks pass, including 33 new Net cases and 32 new rocker checks;
all 477 prior native checks are preserved. Net, Core Debug/Release, probe and
launcher builds have no warnings or errors; native checks used the Debug payload.
Full engine and two-player acceptance
remain open.

## Guest camshaft inputs (v132)

All five families (`VIN115`, `CAMTUNEa0`, `CAMTUNEb0`, `CAMTUNEc0`, `CAMTUNEd0`)
append Durability/ValveTolerance to Scalars and require `camProfileVariable` to be
`CamProfile`. State 185 publishes the actual eight-digit host string, with strict
family and gameplay-revision validation. Audited defaults are:

| Family | Durability | ValveTolerance | CamProfile |
|---|---|---|---|
| VIN115 | 1.1 | 2 | 55003500 |
| CAMTUNEa0 | 0.8 | 1.75 | 60003840 |
| CAMTUNEb0 | 0.8 | 1.25 | 70004480 |
| CAMTUNEc0 | 0.8 | 1 | 75004800 |
| CAMTUNEd0 | 0.8 | 0.75 | 80005120 |

The mount copies these fields from ActivePart; host publication reads current part
Data rather than this table. Every factory's VINP points at
`CARPARTS/StartParts/VIN1110/CamParent/VINP_CamshaftSprocket/VINP_CamShaft`.
Three complete entries require every alternate family. Cylinders projects
Powertrain#3 Installed, Break 2#1 ValveTolerance and Cam wear#0 Wear; Wearing
projects State 4#0 Durability; Valves projects Get cam profile#0 ValveTolerance and
#1 CamProfile through a validated GetFsmString. The profile now has eleven sources
across seven consumers. Malformed or incomplete metadata disables the input profile
while retaining unrelated catalog systems.

Native string readers retain their exact local output and action-local target
checks. Unavailable parts use a parseable `00000000` proxy profile. Shared saved
references and native scratch remain unchanged outside normal reads. Evidence in
`build/camshaft-engine-input-smoke/` includes all five actual prefab Data records,
the nested mount, factories and native consumer graphs. The controlled native run
passes 477 checks, preserving 436 earlier checks and adding 41 camshaft checks;
1,597 Net tests and 18 launcher tests pass. Both Net targets, Core Debug/Release,
the probe and Launcher Debug build cleanly. Whole-engine and two-player
acceptance remain open.

## Guest oil-pump inputs and shared consumers (v131)

At v131, `guestEngineInputs` required eight source entries across six consumers. The same
native FSM may now host multiple sources. Its target variables and action slots
must be unique; reused native output locals in different states are allowed.
Oil's water-pump and oil-pump reads both use Installed1/Wear as native scratch.
Independent proxy preparation leaves those values for normal GetFsm execution.
Every source must validate before its native consumer resumes. Reader failures
pause that consumer, and source-specific rebuild/restore preserves the others.

VIN132 Scalars append Durability after Wear/Tightness. Its factory/live VINP and
`db_Oilpump` reference `CARPARTS/StartParts/VIN1010/VINP_Oilpump`. Oil reads Installed
at Oil pump?#0 and Wear at #2; Wearing reads Durability at State 4#2. Both FSMs are
at `CORRIS/Simulation/Engine/Oil`. The native part has Durability about 1.1, whereas
the mount begins at 1 and copies the installed ActivePart. Native Oil starves below
Wear 13, and an absent pump selects No circulation. Evidence is under
`build/oilpump-engine-input-smoke/`; other engine dependencies remain open.

Validation passes 1,563 Net tests, 18 launcher tests and 436 isolated native
checks, preserving all 409 prior checks and adding 27 oil-pump checks. Twelve new
catalog cases cover the added sources and cross-source collisions. Both Net
targets, Core Debug/Release, the probe and Launcher Debug build without warnings
or errors. Native execution uses Debug; Release Core was built separately.
The [native result](../build/oilpump-engine-input-smoke/result.json) records the
matching tested payload and fixture readiness correction.

## Guest stock/racing fuel-pump inputs (v130)

At v130, `guestEngineInputs` required six consumers. FuelLine and Wearing both use
`familyPrefix: VIN125` plus `alternateFamilies: [FUELPUMP0]`. Only this audited
alternative set is accepted. Both replacement families append Durability/OutputRate
after Wear/Tightness, resolve the same native VINP reference and expose every field
needed by the consumer. Missing either variant or its required fields invalidates
the input profile. Existing single-family profiles cannot add alternatives.

The native mount is `CARPARTS/StartParts/VIN1010/VINP_Fuelpump`; both consumer graphs
use `db_Fuelpump`. FuelLine at `CORRIS/Simulation/Engine/Fuel` reads Installed at
Fuel Pump#0, Wear at Fuel Usage#8 and OutputRate at State 2#1. Wearing at
`CORRIS/Simulation/Engine/Oil` reads Durability at State 4#1. All are one-shot.
Wearing has no Installed reader; its inert proxy keeps an internal readiness bool
and only its one required float. Proxies remain independent and stable across
variant changes, with unique accepted/applied attachment and identity gates.

The stock prefab has Durability about 1.2 and OutputRate 145; the racing prefab has
about 0.9 and 357. Mount defaults differ, and Install 2 copies the actual installed
part. Native capacity uses Power > PumpRate and starvation uses Wear < 5. Saved
Data and write targets stay unchanged. Evidence is under
`build/fuelpump-engine-input-smoke/`; other fuel and engine dependencies remain open.

Validation passes 1,551 Net tests, 18 launcher tests and 409 isolated native
checks, preserving all 378 prior checks and adding 31 fuel-pump checks. Twenty-three
new catalog cases cover both variants and their required consumers. Both Net
targets, Core Debug/Release, the probe and Launcher Debug build with zero warnings
or errors. Native execution uses Debug; Release Core was built separately.
The [native result](../build/fuelpump-engine-input-smoke/result.json) records tested
hashes and the matching launcher payload. Changes remain local and unreleased.

## Guest water-pump engine inputs (v129)

At v129, `guestEngineInputs` required four unique consumers: VIN131/Cylinders,
VIN130/Starter, VIN126/Oil and VIN126/Cooling. Repeated family coverage is allowed
only for those two audited pump consumers; omitting or duplicating either rejects
the dependent profile. VIN126 Scalars append Durability and Efficiency after
Wear/Tightness. Oil requires Installed/Wear/Durability; Cooling requires
Installed/Wear/Tightness/Efficiency. Both use the factory's live VINP reference and
`db_Waterpump`, with asset mount `CARPARTS/StartParts/VIN1010/VINP_Waterpump`.

Oil's exact readers are Water Pump#1/#3 and Starting engine#4. Cooling's are
Water Pump 2#3/#5/#6 and Pump tightness#0. All are one-shot native readers; separate
proxies preserve shared scratch and original mount/write references. The same
accepted applied identity and attachment gates serve both. The native prefab has
Durability approximately 0.7 and Efficiency 1.8; the mount begins with 1 and 0.
Install 2#2/#3 copies actual ActivePart values, so publication uses the host part.
Native evidence is in `build/waterpump-engine-input-smoke/`.

The native seizure threshold is Wear <= 5; circulation closes below Wear 7.
The native tightness comparison is 32 despite mount TightnessMax 24; projection
preserves that game behavior. Other cooling and engine inputs remain open.

Validation passes 1,528 Net tests, 18 launcher tests and 378 isolated native
checks: all 351 prior checks plus 27 water-pump checks. Fourteen new catalog cases
cover both required pump consumers and appended fields. Net, Core Debug/Release,
the probe and Launcher Debug build without warnings/errors. The controlled native
run uses Debug; Release Core was built separately. Changes remain local and
unreleased.
The [tested results](../build/waterpump-engine-input-smoke/result.json) retain the
matching payload hashes and exact native coverage.

## Guest starter engine inputs (v128)

At v128 the input profile required two unique family/consumer entries: VIN131's four
distributor readers and VIN130's three starter readers. VIN130 Scalars append
Durability after Wear/Tightness; a missing field disables the dependent profile.
Starter uses `CORRIS/Simulation/STARTERxCorris::Starter`, `db_Starter` and the
factory's live VINP reference. Its asset mount is
`CARPARTS/StartParts/VIN1010/VINP_Starter`; runtime validation permits block movement.
The readers are Wiring#3 (Installed), Starter damage#0 (Wear), and Starter damage#1
(Durability), all once per entry. Inert proxy floats are constructed from each
validated family's readers, with no distributor-only fields in a starter proxy.
Existing scoped write protection contains changed readers and proxy cache rebuilds.

The native starter prefab has Durability approximately 0.7, while the mount asset
starts at 1; Install 2#2 reads the actual ActivePart value. Host publication therefore
reads the part rather than assuming either default. Native evidence and controlled
checks are under `build/starter-engine-input-smoke/`. Broader engine references
remain open.
The [tested results](../build/starter-engine-input-smoke/result.json) record
351 native passes, including 17 starter checks and all 334 prior checks. Nine
new catalog cases bring Net to 1,514 passing tests. Both Net targets, Core
Debug/Release, the probe and Launcher Debug build cleanly; native execution uses
the Debug payload.

## Guest distributor engine inputs (v127)

`guestEngineInputs.entries[]` resolves `familyPrefix` to an existing replacement
factory. The initial profile permits VIN131 only, with four unique typed readers
for Installed, Wear, Tightness and SparkAngle. Each reader names state, action
index/type, source variable, local output and everyFrame timing. `readerPath`/`fsm`
must match a guest-engine-protection writer graph; readers cannot overlap its
suppressed actions. Malformed metadata preserves unrelated catalog entries, but
guest admission requires this profile alongside write protection.

`mountVariable` must match the family's InstallPoint factory reference. `mountPath`
records the asset mount and supplies its leaf name. Runtime uses live factory and
consumer references plus the accepted attachment address, allowing block fitting
to move the hierarchy. It changes only four action-local owner wrappers and an
owned inert Data object. Saved mount fields and shared db_Distributor stay unchanged.
Signature errors retain the consumer pause. Current applied identity, unique
attachment and receipt-ordered Tightness gate values. Proxy rebuild replaces the
whole object to invalidate native GetFsm caches. The probe is
`tools/GuestSaveProbe/GuestEngineInputChecks.cs`, selected by
`--wintermp-guest-engine-input-probe`. Broader engine projection remains open.
The [v127 native evidence](../build/guest-engine-input-smoke/result.json) records
334 passing checks, including 33 new input checks; 46 new catalog cases bring the
Net suite to 1,505 passing tests. The controlled native run tests Debug Core;
Release Core builds separately with zero warnings/errors.

## Native Corris RPM source (local fix under v126)

Optional `vehicleEngineRpm.sources[]` names `rootPath`, `producerPath`, `fsm`,
`objectVariable`, `componentType`, `rpmMember` and `globalVariable`. Each
`producers[]` entry supplies `state`, zero-based `index`, `actionType` and an
explicit `everyFrame`. GetProperty uses the source's component/member/global
binding; SetFloatValue requires exactly one `sourceVariable` or finite numeric
`sourceConstant`. Roots, producer identities, global names and action slots are
unique; paths, names and list sizes are bounded. The producer must belong to its
vehicle root, and each source needs a component property producer. Invalid
metadata yields null with `VehicleEngineRpmError`, preserving other catalog rules.

The installed Corris profile has eight enabled Starter outputs: four native
drivetrain rpm reads, three StarterSpeed cranking assignments and the Wait zero
reset. The [corrected native audit](../build/vehicle-rpm-smoke/native-audit.json)
confirms there is no enabled stopped per-frame zero writer. Runtime checks exact native
action signatures, the CarDrivetrain component on the registered root, output
reference identity and absence of a local RPM shadow. It reads the output without
executing actions or writing native simulation data; inactive/disabled/unstarted
sources read zero. Reference changes retry, and dashboard aliases cannot write
through the protected native output even after a failed binding.

All 1,459 protocol/catalog/policy tests and 301 isolated game checks pass,
including 31 new catalog cases and 25 new native RPM checks. Both Net targets,
Core (Debug/Release) and the probe build cleanly. The [native result and tested
hashes](../build/vehicle-rpm-smoke/result.json) record zero failures and Wine exit 0.
This local fix leaves protocol 126 unchanged and does not implement guest mount
projection or authoritative host wear progression. See BUILDING.md
for `--wintermp-vehicle-rpm-probe` and two-player acceptance.

## Alternator hand adjustment (v119)

VIN133 and ALTERNATOR0 have an optional `handRotation` binding containing the
relative pivot path, HandRotate FSM name, saved scalar and adjusting-bolt path.
Both paths must be nonempty relative paths, the scalar must belong to the family's
published floats, and array-slot families cannot use this adapter. The native
Clockwise/Counterwise/Wait graphs must retain their local half-degree rotation,
0–7 clamp, part/mount scalar targets and synchronous writes. Changed layouts disable
only that part's adjustment. The layer-19 pivot SphereCollider stays disabled on
guests; its shape supplies picking, and the matching integer bolt's fresh host state
gates scroll input. Protocol 119 operations 2/3 carry requests; the existing state
185 scalar and pose path carries the result. See BUILDING.md for the native probe
and outstanding two-player acceptance checks.

## Distributor ignition timing (v125)

Only VIN131 may use `distributorTiming`. This separate profile identifies its root
HandRotate FSM, saved SparkAngle, MeshRotate variable and owned `Pivot/mesh` path,
VINP mount reference, Rotation/Scroll/Tightness scratch variables, native control
states and Data's `Install 2` pose state. Profiles must be nonslot and exclude
handRotation, handScrew and beltVisual. Names, paths, scalar membership, variable
aliases and control-state aliases are validated. Ready states are exactly
Wait Player, Bolt loose? and Input; the mutation/cooldown/init states cannot be
marked ready. The native cooldown is 0.01 seconds.

Root SphereCollider picking is empty-hand, 1 m and layer 19. Native gates require
ToolWrenchSize zero and owner Data.Tightness below 8. Clockwise/Counterwise read
MeshRotate's local Z, change Rotation by ±0.2, then Wait clamps to 0–20, applies
the mesh pose and writes mount and owner Data.SparkAngle. The host seeds the mesh
from authoritative Data before GetRotation. Fresh SparkAngle comes from native
RandomFloat 1–19, so valid saved values need not align to the step size.
The installed Mouse ScrollWheel axis has `invert: false`; its negative value enters
Clockwise. Guest IMGUI delta Y > 0 (wheel down) therefore increases timing, while
wheel up decreases it.
Protocol 125 selects the distributor policy for existing operations 2/3; state
185 already carries the scalar. Alternator profiles and all factory IDs stay intact.

Guest HandRotate stays disabled. Fitted pose updates rotate only the owned child
mesh, preserving Pivot's authored -10-degree offset. Fresh loose meshes retain
their native +10-degree pose; fitting applies SparkAngle and removal retains the
resolved angle. Re-extract VIN131 Data/HandRotate, the root SphereCollider and
VINP_Distributor Data after game changes. Scroll picking requires the latest accepted
revision to be fully applied, with no pending materialization. The surrounding
removal box shares its own timing pick but still obstructs other parts. See BUILDING.md for validation and the
remaining two-player/engine acceptance checks.

The `shoppingBags` adapter (v120) binds Store/Fleetari bag factories, persistent
`Use.ID`, native contents dispatches and local pickup states. `spillFactoryPaths`
identify exact output owners; prefab `SetName` actions map template names to live
names. Native products without a validated replica adapter are rejected before
opening a bag. The old `spawnContainers` descriptors are retained as catalog history;
runtime bag synchronization uses the new adapter and does not salt IDs by scan order.

## Flywheel/flexplate cylinder inputs (protocol 147, unreleased)

`replacementParts` adds VIN120, FLYWHEELa0, FLYWHEELb0 and VIN138 direct factories
with Wear/Tightness/InertiaFactor scalars. `guestEngineInputs` requires all four
variants at the block's CrankshaftParent/VINP_CrankPulley/VINP_FlywheelFlexplate
mount. Cylinders Flywheel #0 reads Installed and #2 reads InertiaFactor; native
#3 assigns Drivetrain.engineInertia and #4–5 calculate/publish shake.
The proxy updates only those two reads and retains saved Data/ActivePart.
Inertia validation prevents invalid division or drivetrain input. The profile
now contains 58 sources and 106 reads across nine consumers. No package profile
is added: these four native products use their direct part factories.
[Validation and remaining limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-flywheelflexplate-inputs-protocol-147-unreleased)
include native factory checks and actual guest materialization. Cylinder-head
VIN1110 is a nested persistent assembly requiring separate synchronization.

## Spark-plug cylinder inputs (protocol 146, unreleased)

Four `guestEngineInputs` entries bind SPRKPLUG0 to native Cylinders. Each entry
uses AssemblyDatabase/Sparkplugs with its native slot index; array slots 1–4
correspond to VIN1110/VINP_Sparkplug4/3/2/1 and db_SparkPlug4/3/2/1.
For cylinder N the readers are Reset #(8+N) Wear, CylinderN #3 Installed,
Add to power N #2 Tightness and Plug data #(6+N) Durability. There are now
57 sources and 104 reads across nine consumers. The existing part scalar order
Wear/Tightness/Durability and all wire layouts remain unchanged.

Parsing requires every slot and field and rejects overlapping sources/readers,
missing writer protection and wrong family/array metadata. Runtime binding checks
the native array, template, mount and typed reader signatures. A unique accepted
and applied host attachment supplies the proxy; pending, removed, conflicting
or mismatched replicas supply inert values. Native calculation scratch changes
only through its readers. Saved guest plug/mount wear writers stay disabled.
Firing/efficiency, misfire eligibility and durability calculations are checked;
random outcomes remain local native behavior. Physical tool selection, full
engine operation and two-player acceptance remain open.
Evidence: [sparkplug-engine-input-smoke](../build/sparkplug-engine-input-smoke/result.json).

## Spark-plug wrench profile (protocol 145, unreleased)

SPRKPLUG0 adds a `toolScrew` profile alongside its layer-12 removal profile.
It names the native Screw FSM, Tightness scalar/scratch, turn/pose/idle states,
and local player tool Check/Raycast FSM path. Parsing rejects wrong families,
slot counts, layers, scalar bindings, duplicate state names and invalid paths.
Runtime validation checks native turn deltas/limits, BOLTING target, pose layer/
depth and tool name/size/send/pick bindings. All other families retain their
existing hand or bolt controls. No engine-input source is added.

The native Check FSM still compares a plug against `spark plug(Clone)`, while
Data renames it using its saved ID. A removable state-entry hook recognizes only
registered plugs and routes them through the existing size gate. Safe replica
Screw states emit requests and use fully applied host attachment/condition.
Evidence and fixture limits: [sparkplug-tool-smoke](../build/sparkplug-tool-smoke/result.json).

The later [lifecycle probe](../build/sparkplug-lifecycle-smoke/result.json) executes
native fitting, wear updates and removal in all four sockets across real Unity
frames, then replays the host states through guest materialization. It checks
mass balance, body/collider restoration, persistent identity, ownership reset,
and stale fitted updates. Functions audio/UI and physical tool selection remain
fixture boundaries; live P2P and cylinder effects are not covered. This adds test
coverage without changing the catalog or protocol 145.

## Rev-limiter engine inputs (protocol 148)

REVLIMITER0 retains its existing Tightness/SettingRPM replacement scalar order.
Cylinders Limiter reads Installed at #0 and SettingRPM at #3 from independent
read-only host input Data. The live mount is CORRIS/AssembliesTuning/VINP_Revlimiter,
relative to the registered vehicle body. Both factory and consumer must agree.
Missing, pending, conflicting or removed copies disable the native limiter; its
early exit leaves maxRPM unchanged. Applied settings are preserved exactly.
The profile has 59 sources/108 reads across nine consumers, with 26 in Cylinders.
All 37 replacement and 31 package factories remain unchanged. Guest knob requests
and presentation remain separate work.
[Native validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-rev-limiter-inputs-protocol-148-unreleased)
cover the host knob calculations and real guest attachment path.

## Ignition-coil replicas and input (protocol 149)

The native IgnitionCoil212::Spawn profile produces VIN212 state with Wear/Tightness.
Its Init includes RandomFloat before identity builders; guest creation must apply
the published host wear after that action. No CreatePartsPackages binding exists.
Cylinders Ignition #0 reads Installed from the unique applied coil on the registered
vehicle at Assemblies/VINP_IgnitionCoil. Factory and consumer must share the live
mount. Missing, pending, conflicting or removed copies remain absent. Distributor
and wiring inputs remain independent, and the native three-way gate decides
whether starting proceeds. This input does not read coil wear or bolt tightness.
There are 60 sources/109 reads across nine consumers, 38 replacement factories
and 31 package factories.
[Native validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-ignition-coil-replicas-and-input-protocol-149-unreleased)
cover creation, guest attachment and starting decisions; wiring authority remains
separate work.

## Engine wiring inputs (protocol 150)

`guestEngineInputs.wires` names eight exact native Data sources under
CORRIS/Wiring/DatabaseWiring, with fixed message-193 source IDs, bolt capability
and settled native state. Eleven entries use an explicit `wire` reference instead
of replacement `familyPrefix`/`mountVariable`. They supply thirteen bool reads in
Cylinders, Starter and Electrics through the existing guarded proxy mechanism.
The parser requires the complete audited source/consumer set and rejects unknown,
duplicate, incomplete or mixed factory/wire declarations. Shared native object
variables and saved wiring Data are preserved; updates never replay consumer
states. There are now 71 sources/122 reads across nine consumers, with 38
replacement factories and 31 package factories unchanged.

Standard Data waits for Basic state; battery terminals wait for Set bolt after
native bolt calculation. Host records distinguish unavailable, installed and
bolted values. Guests retain accepted state even before their reader binds.
[Native validation and limitations](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-engine-wiring-inputs-protocol-150-unreleased)
cover the start/charge inputs; other wiring connections, tools, cable visuals,
battery, shock and fire behavior remain separate work.

## Starter flywheel input (protocol 151)

The VIN120 family group now has two consumers: Cylinders and Starter. Both require
stock VIN120, FLYWHEELa0, FLYWHEELb0 and flexplate VIN138 factory references to agree
on the same live block-relative flywheel mount. Starter adds one GetFsmBool at
Check Flywheel #0, reading Data.Installed into Installed2 on state entry. Its
native BoolTest still selects Prepare starting or No Flywheel. No extra wear,
bolt or inertia condition is introduced by this installation read.

The input uses the existing accepted/applied replica selection and inert proxy
without changing the shared db_Flywheel reference or saved mount Data. Missing,
pending, conflicting, removed or mismatched copies supply false. The complete
profile has 72 sources/123 reads across nine consumers; replacement/package
factories remain 38/31. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-starter-flywheel-input-protocol-151-unreleased)
cover the starter decision and agreement with combustion; battery, block/gearbox
inputs and full two-player starting remain separate work.

## Battery engine inputs (protocol 152)

`guestEngineInputs.battery` names the native Corris battery mount Data, its Idle
removal boundary and four installed capture states (Check joint, Delay 2, Calc,
Died battery). A required `battery: "Battery"` entry describes three native
Electrics readers: Wiring #0 Installed→Battery, Battery #0 Charge→Charge and
Engine running? #1 Charge→Volts. All are GetFsmBool/GetFsmFloat one-shot reads;
no replacement factory or mount-variable substitution is permitted.

Parsing requires battery Data in `guestEngineProtection.pausedFsms`, with its
critical install/removal/drain/degradation states, plus all ten native db_Battery
writers in Starter/Electrics. Runtime signature checks protect the exact native
actions before input projection. The mount pause persists through disconnect;
its ActivePart charge, capacity and discharge rate remain saved-local values.
The full profile has 73 sources/126 reads across nine consumers, 75 scalar write
guards in 11 consumers and two paused graphs. Factories remain 38/31.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-battery-engine-inputs-protocol-152-unreleased)
cover host capture, native electrical decisions and save protection. Physical
battery lifecycle, guest-driver load and other electrical writers remain open.

## Engine block inputs (protocol 153)

`guestEngineInputs.block` names the Corris block mount Data and its settled
Update/Idle states. Three `block: "EngineBlock"` entries add four native readers:
Starter Motor installed #0 and Running #8 read Installed, Oil Major damage? #0
reads Wear, and Cooling Block damage #0 reads Damaged. Running #8 remains an
every-frame read; the other three read once on entry.

Starter requires `directTarget: true` and no targetVariable. Its native literal
object wrappers are validated against the same exact source mount and restored
on teardown; no named variable is manufactured in the game graph. This direct
mode is restricted to block/Starter. Oil and Cooling require db_Block. Source
fields must exist in native Data; replacement factories and mountVariable are
not used. Missing or changed signatures pause the affected consumer and recover.
The block Data pause is required by parsing and persists through disconnect.
Native removal writes Wear and detaches the assembly, while Update #1's continuous
Wear copy is disabled in this game build; the fixture preserves that disabled
flag. The profile has 76 sources/130 reads across nine consumers, 75 scalar
write guards in 11 graphs and three paused graphs, with factories unchanged at
38/31. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-engine-block-inputs-protocol-153-unreleased)
distinguish host input/save protection from physical block reconstruction.

## Gearbox starter input (protocol 154)

`guestEngineInputs.gearbox` binds the fixed Corris transmission Data mount and
its Idle/Update 2 observation boundaries. All 14 native states must be protected
by the gearbox entry in `guestEngineProtection.pausedFsms`, covering the active
wear/oil/damage copy loop and removal side effects. This adds one source/read:
Starter Check automatic #0, GetFsmInt Type→Automatic via db_Gearbox. It introduces
integer proxy fields with the same exact native owner/output/cadence validation
as the other reads. SimAutomatic and its GearLetter remain local driver inputs.

Missing host type pauses Starter until a valid observation arrives. Native type
zero remains meaningful; unavailable is never substituted for a manual gearbox.
Idle observations preserve the native retained type even when Installed=false.
No saved guest gearbox values, attachment or selector are overwritten. The
profile totals 77 sources/131 reads across nine consumers, four paused graphs,
75 scalar write guards across 11 graphs, and unchanged 38/31 factories.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-gearbox-starter-input-protocol-154-unreleased)
describe the isolated native checks and remaining transmission simulation work.

## Cylinder-head combustion input (protocol 155)

`guestEngineInputs.block.head` defines the direct VINP_Cylinderhead mount under
the host's installed VIN101 engine block, its native VIN111 head family and
settled UPDATE state. The existing EngineBlockState carries HeadInstalled=8;
head unavailability never changes independently captured block installation or
condition. No new part factories or message IDs are needed.

The added Cylinders source redirects only Powertrain #0 Installed→Installed1.
Original db_Cylinderhead references and native timing/decisions remain intact.
Guest protection adds all ten mount states with rootPrefix VIN101 and relativePath
VINP_Cylinderhead, so protection follows a relocated/renamed saved block. Native
scene names protect pre-load parts; valid native IDs protect renamed blocks.
The reader additionally validates the unique parent Data identity. The parser
rejects mixed source kinds and incomplete moving-parent protection.

There are 78 sources/132 reads across nine consumers, five paused graphs,
75 scalar guards across 11 graphs and unchanged 38/31 factories. Physical head
reconstruction and valve/thermal/oil dependencies remain separate work.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-cylinder-head-input-protocol-155-unreleased)
record the isolated native tests and pending two-player acceptance.

## Carburettor fuel and mixture inputs (protocol 156)

`guestEngineInputs.block.carburettor` identifies VINP_Carburettor under the
host's installed VIN111 head and the settled Update 2 state. Its native part
prefix is VIN113; alternatePartPrefixes are CARB2BRLa0 and CARB4BRLa0, verified
against the installed-game spawners and sharedassets3 prefabs. These are input
identities, not additional replacement/package factories. Host values come
from the live mount. Invalid or transitional sources clear carburettor inputs
without discarding independently valid block/head state.

Two entries add four native reads: FuelLine Carburator #0 Installed→Installed1,
#2 FuelChamber→FuelChamber, #4 CarbReserve→CarbReserve; Mixture Calculate density
#0 SettingMixture→CarbSetting. All are entry reads. The parser requires this
exact shape and all 14 paused mount states, including Init/Load/Save 2.
Moving protection follows rootPrefix VIN111 and relativePath VINP_Carburettor.
Queued actions in all paused saved-part graphs are finished before admission.
The guest's saved mount remains paused through disconnect.

The current profile has 80 sources/136 reads across nine consumers, six paused
graphs, 75 scalar guards across 11 graphs and unchanged 38/31 factories. Physical
carburettor replication and delegated-driving host fuel/wear remain separate.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-carburettor-inputs-protocol-156-unreleased)
record the native checks and remaining two-player acceptance.

## Intake filtration and performance (protocol 157)

`guestEngineInputs.block.airCleaner` identifies the direct VINP_AirCleaner
mount below the host's installed VIN111 head, its VIN135 part family and
settled Update 2 state. The installed-game Aircleaner135 spawner and VIN135
prefab verify its identity and scalar surfaces. No replacement/package factory
is added. Capture uses the live mount's DataPower, DataTorque and DataPowerAdd.
The same fields are added to carburettor capture, retaining its three supported
native families. Each intake is validated atomically and independently.

Three entries add seven reads: FuelLine Airfilter #1 Installed→Installed1;
Valves Carburettor and AirFilter #0/#1/#2 DataPower/DataTorque/DataPowerAdd→
PartPower/PartTorque/PartPowerAdd. All retain entry cadence. Native filtration
and power/torque additions remain in place. The parser pins each action and
requires all ten paused air-cleaner states. Moving protection uses rootPrefix
VIN111 and relativePath VINP_AirCleaner; saved condition and attachment remain
protected through disconnect. The native disabled Update 2 wear action is not
enabled by this change.

The v157 profile has 83 sources/143 reads across nine consumers, seven paused
graphs, 75 scalar guards in 11 graphs and unchanged 38/31 factories. Physical
intake reconstruction, exhaust/other engine inputs and delegated-driving host
fuel/wear remain separate work.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-intake-inputs-protocol-157-unreleased)
record the complete native regression run and remaining two-player acceptance.

## Exhaust performance inputs (protocol 158)

`guestEngineInputs.block.exhaust` lists four groups in wire order: Headers,
Front, Rear and Muffler. Headers use VINP_ExhaustManifold under the installed
VIN111 head (primary VIN114; alternatives VIN114B, HEADERSa0, HEADERSc0 and
HEADERSd0). The vehicle mounts are VINP_ExhaustFront (VIN213/VIN213B),
VINP_ExhaustRear (VIN214/EXHAUSTRa0) and VINP_ExhaustMuffler (VIN219/MUFFLERa0).
Exact paths, supported variants, settled Update 2 and ten protected mount states
are required. The moving header root is VIN111; fixed roots explicitly use an
empty rootPrefix. Original VIN identities and positive counters are accepted;
aftermarket factory identities require positive counters. Upgraded part Data
need not expose Wear. No replacement/package factories are added.

Four `exhaust` entries pin Valves Headers/Exhaust front/Exhaust rear/Muffler
#0/#1/#2 GetFsmFloat reads. They retain native entry cadence and read
DataPower/DataTorque/DataPowerAdd into PartPower/PartTorque/PartPowerAdd. The
native additions and scratch ownership remain intact. The parser requires the
matching protected mount, source, target variable and exact reader signatures;
malformed exhaust data disables only the engine-input profile. Moving headers
remain protected by head identity after movement; fixed parts require canonical
vehicle paths. The manifold's removal clears DataPower at index 0, while the
other sections use index 1. Native disabled Update 2 wear actions remain disabled.

The v158 profile has 87 sources/155 reads across nine consumers, 11 paused
graphs, 75 scalar guards across 11 writer graphs and unchanged 38/31 factories.
The native Valves performance states do not read the separate racing-front,
sidepipe or exhaust-tip mounts. Their routing, physical reconstruction/sound,
other engine inputs and delegated-driving host fuel/wear remain separate work.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-exhaust-inputs-protocol-158-unreleased)
record the complete native regression and remaining two-player acceptance.

## Cylinder-head valve adjustment inputs (protocol 159)

`guestEngineInputs.valves` identifies Valves' eight ArrayListGet reads, in
cylinder 1–4 intake/exhaust order. Each action is index 0 in its Cyl N intake or
Cyl N exhaust state, reads literal array reference Valves and literal slot 0–7,
and returns a float FsmVar named Data. Get cam profile #8 reads ActivePart from
Data on db_Cylinderhead into Cylinderhead; its native owner/output are retained
and signature-checked. The parser pins the exact paths, names and slot order.
The existing head/block profile and native write protection remain required.

Host capture requires a unique native Valves ArrayList on the installed head,
exactly eight boxed finite floats. Missing or invalid data clears the entire
set. Guest readers use owned arrays without altering the saved head array or
its runtime Cylinderhead scratch. Native arithmetic/tolerance branches and wear
protection remain intact. Owner restoration precedes proxy destruction.

The v159 profile has 87 scalar entries/155 scalar reads plus one valve-array
source/eight array reads across nine consumers. Eleven paused graphs, 75 scalar
write guards in 11 graphs, and 38/31 replacement/package factories are unchanged.
Physical adjustment controls, later engine outputs and two-player operation
remain separate work. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-valve-adjustment-inputs-protocol-159-unreleased)
record the native checks and saved-tuning acceptance requirements.

## Oilpan engine inputs (protocol 160)

`guestEngineInputs.block.oilpan` pins the native moving VIN101 block's direct
VINP_Oilpan Data child and its settled Update 2 state. Its attached part must be
a native VIN106 original/counter identity, AssemblyID=1, with OilLevel. All five
mounted floats are captured together: Wear, Tightness, Oil, OilContamination and
OilViscosity. The part saves use OilLevel and OilDirt; never substitute them for
the live mounted fluid values. Cylinder-head installation is independent.

Three `oilpan: Oilpan` source entries share native db_Oilpan references but own
separate inert proxies. Oil reads Wear at Major damage? #2, OilViscosity at
Friction #0, Tightness at Oilpan leak #0 and Oil at Get oil #0. Wearing reads Oil
at Oil level #0 and OilContamination at Oil contamination #0 into Math1.
Cylinders reads OilContamination at Plug data #1 into SparkPlugOilCont. All are
entry-only GetFsmFloat reads. The parser validates the exact seven signatures.

The ten-state oilpan Data pause follows native block identity after movement.
Guest protection prevents native fluid/condition copies and removal effects,
including after disconnect. The v160 profile has 90 scalar sources/162 reads,
one valve-array source/eight reads, nine consumers, 12 paused graphs and 75
scalar write guards in 11 graphs. Replacement/package factories remain 38/31.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-oilpan-inputs-protocol-160-unreleased)
record native calculations, saved-part preservation and pending two-player work.

## Rocker-cover oil-leak input (protocol 161)

`guestEngineInputs.block.rockerCover` identifies the native VIN111 head's direct
VINP_RockerCover Data child and its settled Update 2 state. Its active part must
have VIN118 original/counter identity, AssemblyID=1 and Tightness. Capture reads
the mounted Tightness, independently of the part's saved scalar. Missing,
nonfinite or transitional sources clear cover installation and tightness;
cover installation requires the head, independently of the oilpan.

One `rockerCover: RockerCover` entry pins Oil/Valve Cover #0 GetFsmFloat,
Data/Tightness through db_Rockercover1 into native Tightness at state entry.
The inert action-local proxy preserves shared references and arrival scratch.
The game's two arithmetic actions compute `(64 - Tightness) / 60000` without
clamping. Native saved-cover protection follows VIN111 identity after movement
and pauses its ten-state Data graph, including after disconnect. Its disabled
continuous wear action is retained as disabled; removal's wear/cap/attachment
effects cannot execute on the saved guest part.

The v161 profile has 91 scalar sources/163 reads, one valve-array source/
eight reads, nine consumers, 13 paused graphs and 75 scalar guards in 11 graphs.
Replacement/package factories remain 38/31. Physical cover reconstruction and
oil-cap/filling controls remain separate work.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-rocker-cover-leak-input-protocol-161-unreleased)
record native leak calculations, saved-part protection and pending two-player work.

## Radiator cooling inputs (protocol 162)

`guestEngineInputs.block.radiator` identifies the fixed
CORRIS/Assemblies/VINP_Radiator Data mount and its settled Update 2 state. The
active attached part needs canonical VIN201 original/counter or RADIATORa0/
RADIATORb0 counter identity, AssemblyID=1 and Coolant. The mount supplies Wear,
Coolant, PressureCap and FlectEfficiency. Missing, invalid or transitional
sources clear these fields atomically, independently of block/head state.

One `radiator: Radiator` entry pins five entry-only reads in
CORRIS/Simulation/Systems/Cooling::Cooling: Installed at Radiator installed? #0
into Installed1; Coolant, PressureCap and Wear at Radiator Data #0/#1/#3 into
WaterLevel, PressureCap and Wear; FlectEfficiency at Flect #1 into FlectEff.
The parser validates the exact signatures and both upgraded part prefixes.
Owned inert Data preserves shared db_Radiator references and arrival scratch.
Native calculations retain coolant clamps, pressure thresholds and fan
hysteresis. The fixed eleven-state saved mount is paused, including Remove
other, and active continuous coolant/wear copies are drained on admission.

The v162 profile has 92 scalar sources/168 reads, one valve array/eight
reads, nine consumers, 14 paused graphs and 75 scalar guards in 11 graphs.
Replacement/package factories remain 38/31. Physical radiator reconstruction,
cap/filling controls, hoses and airflow inputs remain separate work.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-radiator-cooling-inputs-protocol-162-unreleased)
record native cooling decisions, saved-part protection and pending two-player work.

## Coolant hose and carburettor clamp inputs (protocol 163)

`guestEngineInputs.block.coolantHoses` lists four fixed Data mounts under
CORRIS/Assemblies, in wire order: VINP_RadiatorHoseTop (VIN202),
VINP_RadiatorHoseBottom (VIN203), VINP_HeaterHoseInlet (VIN216), and
VINP_HeaterHoseOutlet (VIN217). Radiator hoses settle at Update 2; heater hoses
settle at Update. Unique active attached native original/counter parts must
have AssemblyID=1 and Tightness. Each mount supplies its own live clamp total;
missing, malformed or transitional hoses clear independently of each other
and of radiator/block/head state. Carburettor capture adds mounted Tightness
to its existing atomic group under the installed head, covering all variants.

Four `coolantHose` entries pin Cooling/Hoses #0..#3 Tightness reads into
Tightness1..4; the bottom hose also supplies Installed at Bottom hose #0.
A fifth entry, using `carburettor: Carburettor`, supplies Cooling/Hoses #4
Tightness into Tightness5. All six are entry-only. Original shared db references
and native scratch remain intact; host arrival only changes inert proxy Data.
The native Reset clears the accumulator before Hoses adds all five bolt totals.
Totals below 104 enter State 5, clamp to 1..104, and add 0.2 / total to
WaterLeakRate; totals >=104 bypass the addition. A missing bottom hose takes
the native Empty coolant path, with saved-radiator writes still guarded.

All four ten-state saved hose Data graphs remain paused through disconnect.
Native removal cannot copy wear, reset mounted clamps or detach saved parts;
already-disabled continuous wear stays disabled. The parser's paused-graph
bound increases from 16 to 32 to accommodate the 18 protected graphs. The
v163 profile has 97 scalar sources/174 reads plus one valve array/eight
reads across nine consumers, with 75 scalar guards in 11 writer graphs and
38/31 replacement/package factories.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-coolant-hose-leak-inputs-protocol-163-unreleased)
record native leak decisions and saved-part preservation. Physical hose
reconstruction, clamp controls and leak presentation remain separate work.

## Grille, cover and bonnet cooling airflow inputs (protocol 164)

`guestEngineInputs.block.coolingAirflow` lists four fixed Data mounts in wire
order: CORRIS/Assemblies/VINP_Grille, CORRIS/AssembliesTuning/VINP_GrilleBlockoff,
CORRIS/Assemblies/VINP_Hood and CORRIS/AssembliesTuning/VINP_FiberglassHood.
All settle at Update 2 with unique enabled, started and initialized Installed
Data and an active attached AssemblyID=1 part. Grilles accept VIN413/B/C/D
originals and counters, stock bonnet VIN411, fiberglass bonnet HOODa0 factory
counters. The cover has exact scene ID BLOCKOFF0 and no scalar fields. Grille
parts lack CoolingAirRateModifier; the mount supplies this live value while
Tightness validates the native part. Bonnet mounts also supply their live modifier.

Four `coolingAirflow` entries pin seven entry-only Cooling reads: Grille #0
Installed; Grille and Cover #0 modifier and #2 cover Installed; Hood #0/#2
stock/fiberglass Installed; Hood installed and Hood installed 2 #0 modifiers.
Original db references and scratch remain intact; inert action-local Data holds
host inputs. Native Reset starts at 2900, the grille adds its modifier and an
installed cover adds 4000 only when the grille is installed. Stock bonnet takes
priority over fiberglass. Each source clears independently on invalid capture.

All four saved Data graphs remain paused through disconnect. The v164 profile
has 101 scalar sources/181 reads, one valve array/eight reads, nine consumers,
22 paused graphs and 75 scalar guards in 11 writer graphs; replacement/package
factories remain 38/31.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-cooling-airflow-inputs-protocol-164-unreleased)
record 125 new native checks, including every installation mask, native scene
cover identity, removal/refit and guest save protection. Physical body assembly,
RoofCheck temperature, dynamic thermal state and live two-player testing remain open.

## Cooling ambient temperature input (protocol 165)

`guestEngineInputs.block.coolingAmbient` binds CORRIS/Functions/RoofCheck,
Raycast, TempCar, and its four audited states: Cast ray, Check roof, Under roof,
Under sky. Host capture requires a unique active, enabled, started/initialized
source, all required states, a recognized current state and finite TempCar.
Temperature/availability are independent of installation flags and use state 195.

The `coolingAmbient: CoolingAmbient` entry binds Cooling/Reset #1 GetFsmFloat,
RoofCheck reference, literal Raycast/TempCar and entry-only TempArea output.
This non-Data source has no Installed field or replacement factory. Its owned
inert Raycast proxy keeps original references and scratch intact. Guest RoofCheck
remains active for local shelter/rain behavior; only Cooling consumes host ambient.
Missing temperature pauses Cooling rather than fabricating a value. Admission
accepts a verified safe pending pause, validates every other binding regardless
of catalog order, and leaves simulation readiness false until data arrives.
Waiting reuses the valid proxy; malformed signatures retain ordinary failure
behavior and block admission.

The current profile has 102 scalar sources/182 reads, one valve array/eight reads,
nine consumers, 22 paused saved graphs and 75 scalar guards in 11 writer graphs.
Replacement/package factories remain 38/31. All native Cooling GetFsm reads now
have projections; global/dynamic thermal state and live two-player testing remain.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-cooling-ambient-inputs-protocol-165-unreleased)
cover native shelter progression, air-cooling math, availability/recovery and
actual admission with invalid bindings following a pending source.

## Native vehicle temperature sources (local/unreleased, still protocol 165)

Optional `vehicleTemperature.sources[]` supplies unique `rootPath`, `gaugePath`,
`sourcePath`, `sourceFsm`, `sourceVariable` and `gaugeVariable`. Both paths must be
inside that vehicle, and source and gauge paths must differ. Malformed metadata
leaves unrelated catalog rules available and reports a temperature-only error.
The current entries identify Corris, Machtwagen and Sorbet's fixed native cooling
sources and dashboards; dynamic part paths are not matched by name heuristics.

Each dashboard must have the audited `Temp/Speed` shape: GetFsmFloat at index 0,
FloatOperator at 1 and FloatClamp at 2, all with everyFrame true. Reader and clamp
must be enabled. An enabled operator must divide its output in place by a finite
nonzero literal; a disabled operator is skipped as native Corris does. Clamp
bounds must be finite ordered literals. Runtime identity checks reject a foreign
source, aliased/global display output, duplicate FSM or replaced state/action.
The arithmetic comes from the game graph, allowing catalog path updates without
hardcoding one car's gauge scale into every car.

VehicleState capture reads the source's degree value before any dashboard clamp.
Observer reads substitute accepted degrees at this one native GetFsmFloat boundary,
leaving the subsequent native gauge arithmetic intact. Local driving, stale state
and disconnect restore native reads; cooling source values are never written.
The existing byte's 0–120 °C meaning is unchanged. See
[validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#vehicle-temperature-sources-and-gauges-still-protocol-165-unreleased).

## Host oil-pressure and wear RPM inputs (local/unreleased, protocol 167)

Optional `vehicleWearInputs` identifies the vehicle `rootPath`, a descendant
`path`, and all seven `readers`. The current path is Corris `Simulation/Engine/Oil`.
One reader is in Pressure, five are in Wearing, and one is in Oil. Each descriptor includes
`fsm`, `state`, `index`, `field`, `global`, `operation`, `output`, `everyFrame`,
and exactly one of `otherVariable`/`otherConstant`.

The supported shape requires all seven audited RPM inputs and rejects omissions,
duplicates, changed state/index/operand/global/operation/timing or ambiguous other
operands. Constants must be finite; divisors cannot be zero. Invalid metadata
reports `VehicleWearInputsError` without discarding unrelated sync rules.

Runtime validation requires the native Assembly-CSharp FloatOperator, exact
current state/action and variable identities, no local RPM shadow, a unique FSM,
matching native other operand and local result, and the registered car root.
The complete group is checked before each scoped substitution. The native
DoFloatOperator prefix/finalizer temporarily supplies accepted driver RPM only
on the host and always restores the original global operand reference. The
Pressure temperature read is deliberately outside this group: heat, oil and wear
remain host-owned, and no thermal state is copied from a guest save.

The Oil reader is Oil contamination #2, float1 RPM divided by literal 250000
into local OilContaminationRate, once on entry. Native filtering branches, the
minimum-rate clamp, all persistent writers and the Oil wait stay unchanged.

See [validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-oil-contamination-input-protocol-167-unreleased).

## Native host engine heating inputs (local/unreleased, protocol 168)

Optional `vehicleHeat` selects `rootPath`, descendant `path`, `fsm`,
`runningState`, `stoppedState`, `objectVariable`, `componentType`, `torqueMember`,
`torqueVariable`, `rpmGlobal` and `temperatureGlobal`. Paths, names and independent
states/scalars are validated; malformed data reports `VehicleHeatError` while
other catalog rules remain available. Corris uses CarData/HeatGeneration,
CarDrivetrain.torque → Power, RPM and EngineTemp.

Runtime binding verifies the two native states, their action identities/types,
every-frame flags, PROCEED transitions, torque/currentPower property targets,
friction read, arithmetic operands/results, minimum/maximum heating clamp,
per-second temperature writer and ordered start/stop comparisons. The registered
root, unique source FSM, native drivetrain and local/global references must
match. Four scoped readers cover five fields: both RPM-square operands, torque,
and the two RPM comparisons. Native constants and the friction input remain
owned by the game. Changed bindings clear the group and retry on later capture
or telemetry. A stopped or invalid source cannot publish stale torque.

The native FloatOperator and FloatCompare hooks use a separate declaring type
from the wear hooks: Harmony keys temporary `__state` by declaring type, even
when patch IDs differ. Nested state transitions retain their outer operand
scope until finalization. No guest temperature is copied into the host.
[Native validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-engine-heating-inputs-protocol-168-unreleased).

## Native host cooling movement speed (local/unreleased, protocol 169)

Optional `vehicleSpeed` selects `rootPath`, descendant `producerPath`,
`producerFsm`, `producerState`, `speedGlobal`, descendant `coolingPath`,
`coolingFsm`, `airState` and `checkState`. Missing/invalid names, foreign paths,
aliased producer/consumer paths or duplicate consumer states isolate this profile
as `VehicleSpeedError`, preserving other catalog sections.

The audited Corris Measurements/State 1 starts with native GetSpeed reading this
car's Rigidbody magnitude into global SpeedKMH, followed by FloatMultiply ×3.6.
The separate dashboard Speedo uses Drivetrain.differentialSpeed for its needle.
Measurements also reads differentialSpeed into local DiffSpeed; validation requires
that output to stay separate so it cannot overwrite the converted movement sample.
Capture preserves that distinction and requires an enabled, started movement
producer. Movement availability/value are appended to VehicleState 60 at v169.

One binding group validates the source and Cooling/Air cooling #1 FloatOperator
(Multiply SpeedKMH by TempAreaAdjusted into CoolingBaseRate), plus Check temp #1
FloatCompare (>2 km/h → FINISHED). Native action identity, operand references,
timing and state transitions must agree before either reader projects. Scoped
operands restore through nested calls and exceptions. Speed hooks use a distinct
declaring type for Harmony state, separate from wear and heat hooks on the same
native helpers. Changed groups retry after repair; stream cleanup removes readers.

The host retains native ambient adjustment, cooling math/clamps and temperature
writers; packets do not advance simulation. Missing/stale/unavailable guest speed
uses native stationary behavior. Other cars currently send unavailable movement.
[Native validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-cooling-movement-speed-protocol-169-unreleased).

## Native host cooling driver inputs (local/unreleased, protocol 170)

`vehicleCooling` replaces the local v169 `vehicleSpeed` profile. It retains
rootPath/producerPath/producerFsm/producerState/speedGlobal/coolingPath/coolingFsm/
airState/checkState and requires four additional names: `rpmGlobal` (RPM),
`pumpState` (Water Pump 2), `fanState` (Fan), and `leakState` (Motor on?). Five
consumer state names must be distinct, and RPM cannot alias movement speed.
Malformed metadata isolates `VehicleCoolingError` without discarding other rules.

One binding group now covers both movement reads and all three native Cooling
RPM reads: pump #2 FloatCompare (<100 → PROCEED/Closed), fan #5 FloatOperator
(RPM / CoolingFanModifier → CoolingFanRate), and Motor on? #0 FloatCompare
(>=200 → LEAK/Housing tightness; <200 → FINISHED/Water leak). Equality at 100
permits the remaining pump checks; native belt/pump installation and wear <7
still close circulation. The fan's installed/belt gates and divisor remain native.

The original RPM and movement references, source identity, selected action
identity/timing/operands and transitions validate together. Changing any selected
reader clears all five; repaired references can bind again. Each helper restores
its own global operand through exceptions and nested state transitions. Valid RPM
is independent of optional movement or torque availability, while missing,
expired, foreign-owner or snapshot samples use zero. Host authority and guest-save
protection gates are unchanged. Packet arrival never ticks the graph or writes a
rate, temperature, native global or saved part value.

Implementation is consolidated in `VehicleWorldSync.CoolingInputs.cs` and
`.CoolingReads.cs`, with a distinct Harmony callback declaring type from wear/heat.
[Native validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-cooling-rpm-inputs-protocol-170-unreleased).


## Host coolant dashboard authority (local/unreleased, protocol 171)

Every `vehicleTemperature.sources[]` entry explicitly declares
`hostAuthoritative`. Corris enables it and names `readyState: "Coolant temp 2"`;
Sorbet and Machtwagen remain false without a readiness field. The runtime validates
a unique readiness state distinct from native startup alongside the existing
source/gauge identity and arithmetic binding. Cooling's native Init waits eight
seconds before its first calculation loop. The host observes readiness, captures
finite CoolantTemp without dashboard quantization, and retires unavailable sources.

Reliable state 197 is keyed by the catalogued stable vehicle path ID. Guest
receipt survives late scene discovery and projects only the native gauge output,
including for the local driver. It never writes physical coolant or EngineTemp.
Corris no longer displays guest-owned VehicleState coolant bytes. This catalog
change does not establish shared thermal consumers or complete engine handoff.
[Native validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-coolant-dashboard-protocol-171-unreleased).


## Guest engine-temperature inputs (local/unreleased, protocol 172)

Host `vehicleTemperature` sources now require `engineGlobal` and `engineInputs`.
Corris uses global EngineTemp and separate Fuel/FuelLine, Fuel/Mixture, Oil/Oil and
Oil/Pressure consumers. The named Carburator, Priming, Calculate density, Viscosity
and Oil pressure states identify six audited native operands. All paths remain
under the vehicle root; fuel/oil paths, colocated FSM names and priming/carburator
states must be distinct. Driver-authoritative temperature profiles cannot declare
host engine inputs. Missing/invalid metadata isolates this temperature profile.

The runtime validates native reader types, operands, outputs and priming branches;
source capture rejects a Cooling-local EngineTemp shadow. State 197 appends native
engine degrees (19 bytes total). Guest read hooks restore original references after
native execution and never rewrite physical thermal globals. The complete Corris
audit and the six selected references are retained under
`build/host-engine-temperature-smoke/`; 24 other EngineTemp references, including
native writers, remain outside this projection.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-engine-temperature-inputs-protocol-172-unreleased).

## Guest cabin and heater temperature (local/unreleased, protocol 173)

Host `vehicleTemperature` sources additionally require `cabinInputs`, with
`cabinPath`, `cabinFsm`, `cabinState`, `heaterPath`, `heaterFsm` and `heaterState`.
The Corris profile identifies CarTempCorris/Data/Data and
Electricity/PowerON/HeaterUnit/Function/Calc defrosting. Paths must be distinct
children of the vehicle, separate from its coolant source and dashboard. Driver
thermal sources cannot declare this group. Malformed profiles isolate the
vehicleTemperature section, retaining unrelated catalog groups.

The two reads validate together, independently of the dashboard and fuel/oil
bindings. The cabin FloatOperator at index 4 must divide the global EngineTemp
by five into local MaxTemp, used by the next native cabin clamp. The heater
GetFsmFloat at index 0 must read the catalogued Cooling source into its own local
CoolantTemp. Source/reader identities, names, paths, unique FSMs/states, action
arrays, types, cadence, outputs, target and arithmetic are revalidated; changed
bindings disable both readers and retry. Original engine operands restore on
nested reads and exceptions. Host heat never overwrites native source variables.

Validation passes 3,074 Net tests and 2,662 native checks, preserving every
previous check. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-cabin-and-heater-temperature-protocol-173-unreleased).

## Guest electrical temperature (local/unreleased, protocol 174)

Host `vehicleTemperature` sources require `electricalInputs`: `electricsPath`,
`electricsFsm`, `batteryState`, `chargingState`, `interiorPath`, `interiorFsm` and
`interiorBatteryState`. Main and interior paths must be distinct children of the
vehicle, separate from the cooling source and dashboard; main battery and charging
states must differ. Driver thermal sources cannot declare this group. Missing,
malformed, foreign or ambiguous metadata isolates the vehicleTemperature section
while preserving unrelated catalog groups.

The Corris main Electrics/Battery #1 and InteriorLight/Electrics/Consumption/Battery
#1 read host EngineTemp into local BatteryTemp. Native cold clamps (-20 to -0.1),
local Charge addition and VoltageLimit events/transitions remain validated and
active. Main Electrics/Charge battery #2 uses host heat before native +51, /570,
0.0001–0.55 limiting of Charging. The runtime validates action arrays, types,
source/global identity, absence of local EngineTemp shadows, cadence, scratch
variables and selected downstream arithmetic. The three reads bind and recover as
one group, independently of fuel/oil and cabin/heater groups.

Scoped hooks reuse the existing engine-temperature reader machinery and add native
SetFloatValue OnEnter/OnUpdate boundaries; source operands restore on nesting and
exceptions. Stored battery charge, native global heat and simulation ticks are not
projected. Validation passes 3,101 Net tests and 2,751 native checks.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-electrical-temperature-protocol-174-unreleased).

## Host electrical RPM inputs (local/unreleased, protocol 175)

`vehicleElectrical` independently declares `rootPath`, `path`, `fsm`, `rpmGlobal`,
`runningState`, `chargingState` and `batteryState`. The consumer path must descend
from the vehicle root and the three states must be distinct. Missing/malformed
fields isolate this group while preserving cooling, thermal and guest-protection
metadata. The native Corris profile selects all three Electrics RPM reads:
Engine running? #0, Charge battery #1 and Run on battery #0.

Runtime binding validates unique FSM/state identity, action arrays, native global
RPM without local shadowing, exact scoped operands, cadence, >400 events, native
charge/drain arithmetic and selected transitions. Charging divides by the host's
local AlternatorEfficiency; discharge divides by 60000 and retains its 0.00001–1
clamp. Native wiring, damage/condition, temperature and saved-state writers remain
unchanged. The group binds on host vehicle updates before any report, and accepted
driver messages also retry binding. Shared authority/sample policy supplies valid
current-owner RPM or native zero-RPM behavior when missing/stale. Scoped operands
restore on nested reads and exceptions; changed groups retire and retry.

Validation passes 3,125 Net tests and 2,862 native checks, preserving all previous
checks. The fixture exercises native charging/drain and battery Charge/ChargeMax
and alternator Wear writes using isolated sources; it does not prove complete
live electrical operation.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-electrical-rpm-inputs-protocol-175-unreleased).

## Guest starter draw observations (local/unreleased, protocol 178)

Five existing `guestEngineProtection.writers[].actions[]` entries on Corris Starter
require `starterDraw`: `loaded` for Turn key #6, Fuel Mixture #8, Start or not #7
and Start engine #9; `unloaded` for No Flywheel #5. Other kinds, paths, indices or
destinations reject protection metadata. Missing annotations reject battery input
admission. Native AddFsmFloat, db_Battery::Data.Charge, the local StarterDraw or
StarterDrawNoLoad amount, everyFrame=true and perSecond=false are required.

On registered guests with battery metadata these five actions remain enabled only
to observe callbacks; their native saved writes are always skipped. Other selected
writes remain disabled, so the existing writer count does not change. Host apply
uses these same validated descriptors and host rates. Request 198 carries bounded
counts with ownership/authentication/replay checks, not a battery value. Validation
passes 3,182 Net tests and 3,055 native checks; other loads, starter wear and live
acceptance remain open.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-starter-battery-draw-protocol-178-unreleased).

## Guest accessory battery reads (local/unreleased, protocol 177)

`guestEngineInputs.battery.readVariables` requires exactly Installed, Charge and
ChargeMax, without duplicates; missing or malformed fields reject the engine-input
profile while independent catalog groups remain available. The existing paused
battery external-write flag remains required. Source/proxy entry counts do not
change: a native getter boundary recognizes the actual protected battery Data,
including cached targets, and supplies host state into local consumer outputs.

The retained native Corris audit covers 22 battery reads across eight FSMs, including
19 beyond the original three engine proxy reads. One UseOwner Amps read is an
unrelated current-meter control, despite its serialized battery object reference.
Native source fields and globals stay untouched; missing host power gives false/zero.
BatteryState 194 now includes ChargeMax (15 bytes with ID). Validation passes 3,154
Net tests and 3,016 native checks. Guest electrical demand and live acceptance remain
open. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-accessory-battery-inputs-protocol-177-unreleased).

## Guest battery external scalar writes (local/unreleased, protocol 176)

A paused FSM can declare `blockExternalFloatWrites: true` to preserve its float
variables from native SetFsmFloat, AddFsmFloat and SubtractFsmFloat consumers. The
battery VINP_Battery::Data rule requires this flag before guest battery inputs are
accepted. Other paused graphs default to false. Invalid flag types reject the
protection profile; absent/false battery protection rejects guest engine inputs.
The existing 75 selected writes, one pose write and 22 paused graphs remain intact.

The native write boundary follows the actual destination and cached FSM, covering
31 audited battery writes across 19 consumer FSMs without depending on their paths.
This includes 21 accessory/terminal writes absent from the earlier selection.
Known destination identities remain protected through movement and disconnect;
new targets are recognized before discovery. Other destinations and consumer
calculations continue. This does not forward guest electrical demand to the host.
Validation passes 3,134 Net tests and 2,945 native checks.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-battery-external-writes-protocol-176-unreleased).

## Guest starter wear observation (local/unreleased, protocol 179)

`guestEngineProtection.writers` marks only Starter Fuel Mixture #11 with
`starterWear: true`: SubtractFsmFloat, db_Starter → Data.Wear. Wrong type, target,
state/index or non-true annotation rejects the protection profile. Missing metadata
prevents complete battery/engine-input admission. Existing starterDraw annotations
and read/state layouts remain unchanged.

The guarded action observes native Time.deltaTime on entry and each update while
skipping its saved write. Its amount must remain local StarterWear with both
everyFrame and perSecond true. Host validation additionally checks native Starter
damage #1 (Durability reader) and Fuel Mixture #10 (literal × StarterDurability →
StarterWear); it uses current host mounted durability and the host literal rate.
The game-build audit has rate 0.197. Other cranking states have battery draw without
this wear writer. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-starter-wear-protocol-179-unreleased).

## Guest heater condition (local/unreleased, protocol 180)

`guestEngineInputs.heater` pins `CORRIS/Assemblies/VINP_Heaterbox::Data`, Idle and
Update 2, with exactly Installed/Wear read variables. The matching paused FSM must
include all ten audited native states and `blockExternalFloatWrites: true`.
Missing/changed source or protection metadata prevents full input admission. The
protection inventory now has 23 paused graphs; existing reader entries and
replacement factories are unchanged.

The native heater Function reads Installed at Electrics? #4 and Wear at Blower
wear? #1. Destination-based helper projection preserves their local outputs,
entry-only cadence and native target caching. Blower ok #7 still calculates wear
through native arithmetic, but its external saved-mount subtraction is blocked
for guests. Pausing mount Data also blocks Update 2 publication to ActivePart and
removal writes/attachment changes. The host keeps those native routines active.
State 200 supplies only settled host installation/condition; it does not reconstruct
or fit a heater on the guest. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-heater-condition-protocol-180-unreleased).


## Heater and rear-defroster circuits (local/unreleased, protocol 181)

`guestEngineInputs.wires` now requires eleven sources. IDs 9–11 name
WiringHeatercontrolFusebox, WiringHeater and WiringFuseboxWindow under
CORRIS/Wiring/DatabaseWiring, with Data, Basic state, supportsBolted=false and
projectNativeReads=true. Missing/false/nonboolean projection metadata, changed
identity/load boundary or invented bolt support rejects full input admission.
Sources 1–8 must keep projectNativeReads absent or false and retain their existing
proxy consumers. The 102 indexed entries/182 reads and factories are unchanged.

Native destination-based GetFsmBool Installed projection supplies HeaterUnit
Electrics? #2/#3 and Rear defrosting #0 from host state 193. Output must be safe and
consumer-local; saved wiring stays unchanged, and host/solo readers remain native.
The native supply and rear-window gates keep their existing decisions and timing.
These entries add circuit inputs, not cable reconstruction or guest wiring tools.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-heater-and-rear-defroster-wiring-protocol-181-unreleased).


## Heater hose installation reads (local/unreleased, protocol 182)

`guestEngineInputs.block.coolantHoses` keeps its four ordered sources: top, bottom,
inlet and outlet. Inlet/outlet now require projectNativeReads=true; top/bottom
must omit it or use false. Wrong types, missing heater declarations or projecting
a radiator hose reject full input admission. Existing mount identities, settled
states and paused saved-mount requirements remain unchanged.

Native GetFsmBool Installed reads from the two heater mounts consume state-195
hose bits 2/3 into local outputs. Heater pipes? #2/#3 retain their entry-only timing
and source cache/fallback. Cooling's existing radiator proxy supplies WaterLevel
through native clamp/removal logic; the heater's native 0.5 threshold and two-hose
BoolAllTrue remain active. No new factory, paused graph, indexed input or wire field
is added. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-heater-hose-inputs-protocol-182-unreleased).


## Rear-window heating element (local/unreleased, protocol 183)

`guestEngineInputs.heater.rearWindow` requires path CORRIS/BODY, fsm Save,
variable HeatingSprites and readyStates containing State 1, State 4 and Save.
Missing/changed source or load-boundary metadata rejects full input admission.
No new paused graph or indexed input is added; body Save stays native.

The native body derives HeatingSprites from VIN WindowHeater. Its M/'-' comparisons
disable the element; other values enable it. State 200 appends separate rear flags,
independent of heater mount availability. Native Rear defrosting #2 reads those
flags into local HeatingSprites without changing the saved body option, VIN or
strip visuals. Existing circuit, element and switch gates precede native demand
and heat additions. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-rear-window-heating-element-protocol-183-unreleased).

## Independent window ice (local/unreleased, protocol 184)

The shipped `vehicleClimate` prefixes/CarTemp markers now include
`JOBS/TAXIJOB/MACHTWAGEN/` and `/Simulation/CarTempTaxi`, matching the defaults.
A fresh build-23268598 audit verifies all six Freezing float names for Corris,
Sorbet and the taxi: CutoffWindshield, CutoffSideLeft, CutoffSideRight,
CutoffDoorleft, CutoffDoorright and CutoffRear. Preserve the native door casing.
Freezing State 1 sets these to 0 (iced), and the sheltered Check roof assignments
set them to 1 (clear). Individual scrape states raise only their own cutoff;
HeaterUnit rear heat raises only CutoffRear. VehicleClimate 61 now transmits each
pane separately with availability. No replacement/input catalog counts change.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#independent-window-ice-protocol-184-unreleased).

## Cabin occupancy and native entry (local/unreleased, protocol 186)

A fresh build-23268598 audit confirms Corris, Sorbet and taxi PlayerTrigger
`Press return` action 8 sets CarTemp `GlassFrosting.PlayerIn` true and `Player reset`
action 2 clears it. This is local entry state, also read by driver detection;
received VehicleClimate occupancy must never write it. Capture reads local entry
and accepted passenger seats separately. No catalog bindings or counts change.
Native GlassFrosting `Player in?` also reads global PlayerSweat when computing
FrostingRate; this slice does not project remote sweat or claim passenger fog/heat
simulation. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#vehicle-climate-occupancy-isolation-protocol-186-unreleased).

## Native condensation presentation (local/unreleased, protocol 187)

Corris, Sorbet and taxi GlassFrosting `Defrosting` and `Warm car` use
SetColorRGBA (RGB=1, alpha=Frost), then SetMaterialColor (`_Color`, FrostGlass).
The three native material definitions in sharedassets3 use alpha blending and
white tint; their saved shader `_Cutoff` is independent. SweatRate belongs to
PlayerSweat/300 clamped to .02–.1; it is not accumulated opacity. Warm car divides
Temp by 100 before its per-second Frost subtraction. VehicleClimate now writes
only accumulated Frost to native/material alpha and Data.InteriorTemp in degrees;
neither scratch variable, RGB tint nor interior shader cutoff is a sync output.
Fog is retired in place. No catalog rules/counts change.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-condensation-presentation-protocol-187-unreleased).

## Passenger condensation inputs (local/unreleased, protocol 188)

The optional `vehicleClimate.passengerCondensation` profile names the native
`Player in?` state, local `PlayerIn`, global `PlayerSweat`, and local `SweatRate`,
`FrostingRate`, `DefaultRate`, `DefrostingRate`. Invalid profile data disables this
profile without discarding the other vehicle climate paths. No FSM transition
rules or generic scalar sync rules are added.

A fresh build-23268598 asset extraction covers PLAYER/BodyTemp and CarTemp on all
three cars (17 FSMs). BodyTemp.Calculations::Sweat clamps PlayerSweat to 0–100.
Each GlassFrosting Player in? state has six actions: reset DefrostingRate to zero,
copy DefaultRate to FrostingRate, test local PlayerIn (false -> FINISHED), divide
global PlayerSweat by 300 into SweatRate, clamp .02–.1, copy it to FrostingRate.
Bindings validate action identities, types, references and arithmetic before
borrowing only BoolTest/FloatOperator input operands. Shared global values and
native entry stay untouched, and the original references restore on exit.

The current climate producer supplies one clamped contribution per eligible
occupant, with the native ceiling retained for the whole cabin. Remote passengers
must have a fresh accepted pose, be alive and remain seated in that car. Missing
sweat supplies only the dry minimum; absent occupants use DefaultRate. The
read-only audit, curated native fixture and tested hashes are recorded under
`build/passenger-condensation-smoke/`. Body warmth and live rendered acceptance
remain open.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#passenger-condensation-inputs-protocol-188-unreleased).

## Native body warmth source (local/unreleased, protocol 189)

The v188 read-only PLAYER/BodyTemp audit also exposes a source error in the needs
reporter. Calculations.Get temp reads environmental Temperature from PLAYER/Rain;
Source reads a nearby heat source's Data.Temperature into that same local scratch.
Calculate subtracts 10 from Temperature, divides by AirSpeed, clamps to -20..20,
and adds the result to **global PlayerTemp**. The global definition starts at 50;
PlayerTemp is not in the Calculations local float list. Native movement, clothing,
sweat and alcohol stages also affect it. It is a game warmth value, not Celsius.

The needs reporter now binds global PlayerTemp and leaves local Temperature alone.
Existing car HeatSource distances (.75 Corris, .8 Sorbet/taxi) and all cabin/source
rules remain unchanged. This corrects warmth persistence; it does not establish
that the driver's native heat-source radius covers every passenger seat. The
curated body fixture reuses the verified v188 audit and imports the four Calculate
actions, binding their final write to the test-process global. No catalog rules
or installed assets are changed.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-body-warmth-persistence-protocol-189-unreleased).

## Passenger cabin heating (local/unreleased, protocol 190)

A fresh read-only build-23268598 audit covers PLAYER/BodyTemp, PLAYER/Rain and
the three CarTemp trees: 21 FSMs, four array lists and 14 transforms. BodyTemp's
heat-source selection uses closest-object distance against source Data.Distance;
the cabin sources retain their small .75/.8 m native radii. This establishes
the lookup mechanism, not physical coverage of every seat in a live game.

The optional `vehicleClimate.passengerHeating` profile identifies paths relative
to the actual local player: BodyTemp::Calculations, Get temp and Source, plus
Rain::RoofCheck, local Rain/HeatSource references and the Temperature output.
Relative paths survive the passenger controller parenting PLAYER under a car.
Binding requires the audited four/one action arrays, enabled one-shot native
GetFsmFloat/IntCompare types, exact variable references, literal source names and
the player's own rain source. Changed profiles/actions detach only this binding
and retry with throttled diagnostics. Missing optional profiles preserve other
climate and catalog rules.

A postfix on native GetFsmFloat.DoGetFsmFloat replaces only the local body's read
result for an eligible seated passenger. Available cabin degrees come from the
validated current producer or its accepted unexpired climate cache; the native
Calculate state still updates PlayerTemp. Source objects, radii, lookup caches,
reader operands, local entry, driver ownership, clothing and sweat stay native.
No HeatSource or Rain FSM is modified. Unavailable VehicleClimate flag 8 clears
the cabin input and skips InteriorTemp presentation writes, retaining other
climate fields. Finite cabin values keep the existing -40..40 °C encoding.

The curated fixture imports both reader states and Calculate from the new audit,
using inert source FSMs, temporary PlayerTemp and manually arranged seats. All
72 added native checks pass across Corris, Sorbet and taxi. Physical seat/door
interaction, native closest-source selection, rendered/continuous progression,
loading/respawn and live two-player winter survival remain acceptance work.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#passenger-cabin-heating-protocol-190-unreleased).

## Passenger death lifecycle audit (local/unreleased, protocol 191)

The fresh read-only build-23268598 Systems/Death audit confirms that Activate Dead
Body destroys the player's FPSInputController, CharacterMotor and
CharacterController in State 3. The existing Take photo hook reports local death;
State 2 schedules the existing respawn watch. Passenger cleanup now runs from
local death/group-death and respawn notification, while the host's terminal events
retire canonical/observer occupancy without clearing seat replay history.

No death catalog bindings or installed assets changed. The curated native probe
keeps the audited FSM identity, state names and variables and removes all vanilla
actions/transitions, so testing hook delivery cannot execute native death/save
side effects. Real seat-controller entry/pinning/release, Unity controller
removal, authenticated death/respawn routing and remote anchors run on inert
objects. Native death camera, save/load, physical seat/door interaction and live
two-player acceptance remain open.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#passenger-death-and-respawn-protocol-191-unreleased).

## Passenger discovery audit (local/unreleased, still protocol 191)

The fresh read-only build-23268598 audit records 169 FSMs and 646 transforms,
including inactive MassPassenger, DriveTrigger and bench/rear-seat anchors.
PassengerController still supports Corris and Sorbet. Taxi has a different front
passenger pivot and requires separate native taxi/NPC entry validation; previous
taxi passenger-heating fixtures manually arranged their seats and do not establish
player seating support.

Discovery now refreshes cached geometry when the current world vehicle body is
replaced or removed. It retries missing anchors, safely releases local seating
and preserves accepted remote membership/history for re-anchoring. No catalog
binding or installed asset changed. The native probe exercises real discovery and
host validation on inert structural anchors, not full vehicle reconstruction,
physical entry or two live clients.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#passenger-vehicle-discovery-recovery-still-protocol-191-unreleased).

## Native tyre pressure audit (local/unreleased, still protocol 191)

The read-only build-23268598 audit captures 33 wheel/tyre-pressure/gearbox FSMs.
Corris TirePressure.Data defaults Pressure and PressureOptimal to 190; TIRES enters
WheelFriction and copies them directly into all four Wheel.pressure/optimalPressure
properties. ENABLEPRESSURE instead enters State 1 and flips Enabled. The Core
native conversion now keeps kPa values on both sides of the existing bar×100 wire
byte. No catalog bindings or installed assets changed.

The audit also reopens roadmap 2.2: wheel Condition.Health and gearbox DamageType
are scratch populated from native part Data, RIM is not globally reachable, and
Flat friction writes the referenced tyre health. The new fixture keeps only the
pressure FSM's variables/state graph, omitting every physics/enable action. It
checks the real capture/apply boundary, not complete wheel physics or shared wear.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-tyre-pressure-units-still-protocol-191-unreleased).

## Condition stream boundary checks (local/unreleased, protocol 192)

The pressure/condition audit above supplies six inert probe FSMs: pressure Data,
gearbox Damage and four wheel Condition graphs. Their typed variables and local
PUNCTURE transition are retained; all native wear/physics actions are omitted.
The probe verifies current-owner checks, dedup, host relay/snapshot behavior and
final item-release ordering without executing native TireHealth/Wear mutations.
No catalog binding or installed asset changed. Native durable condition, safe
flat/rim effects and parked publication remain roadmap 2.2 work.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#condition-stream-ownership-protocol-192-unreleased).


## Guest tyre and gearbox saved-part writers (local v193)

`guestEngineProtection.writers` now includes all four Corris WHEELc_* Condition
paths and GearboxDamage.Damage, based on the read-only build-23268598 condition
audit. Per wheel, State 1#11 subtracts ThisTire::Data.TireHealth every frame/per
second and Flat friction#0 writes it to zero. GearboxDamage Reverse#6 subtracts
db_Gearbox::Data.Wear. Existing native admission and state-entry guards cover
these nine actions, including already-active wear; native reader/physics siblings
are not selected. Changed native signatures pause their graph. Solo/host behavior
is unchanged. The profile totals 16 writers/84 scalar actions plus the existing
pose action and 23 paused graphs.

`build/tyre-write-smoke/condition-audit.json` records the 33 audited FSMs;
`guest-engine-probe.json` extends the preceding full fixture with the five writer
rows. Native checks execute the actual writers and tyre-health readers against
inert saved targets, with branch/audio/physics actions omitted. Safe host condition
inputs and physical flat/rim effects remain open. See
[validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-tyre-and-gearbox-write-protection-protocol-193-unreleased).


## Observer tyre-health readers (local v194)

Each Corris wheel Condition writer in guestEngineProtection has wheelHealth.wheel
(FL/FR/RL/RR = 0/1/2/3) and two native read bindings: State 1#12 and Flat
friction#3. Both are GetFsmFloat ThisTire::Data.TireHealth → local Health, every
frame. The parser requires both corresponding protected writers and distinct
reader slots. Native admission validates these readers; replacement/disablement
invalidates the binding. Connected guest observers consume accepted current-owner
condition, with saved-part/global output aliases rejected and native source/cache
left intact. Local drivers and unavailable/ineligible observer state retain native
lookup. Physical tyre handling, driver inputs and host wear remain open.

The 13-row wheel-health-probe.json fixture comes from the installed build-23268598
condition audit and executes native reads, grip math and puncture branch actions
against inert targets. See [validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#observer-tyre-health-inputs-protocol-194-unreleased).

Protocol 195 extends these same bindings to the copied condition retained after
an established owner's approved final release. Catalog/action signatures and the
13-row fixture are unchanged. Host correction, new claims and session teardown
retire that record; it never writes saved tyre data or publishes host wear.
See [parked observer validation](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#parked-observer-tyre-health-protocol-195-unreleased).

## Native wheel-pressure application (local v196)

`vehicleTirePressure` identifies the car root, TirePressure.Data path, TIRES event,
WheelFriction state and separate Pressure/PressureOptimal floats. Four wheel
entries declare exact component paths, canonical FsmObject names, pressure and
optimum action indices, and native `enabled` state. Indices cover all eight slots
exactly once; paths/targets must be distinct and belong to that car. Invalid
metadata disables this profile while preserving other catalog systems.

The build-23268598 graph enables only FL actions 0/1; FR/RL/RR actions 2–7 are
present but disabled. Runtime validation checks this signature, the event target,
absence of a shadowing local transition, source identity, every target and each
native SetProperty pressure/optimalPressure operand before dispatch. The shared
update enables all eight one-shot actions for TIRES and restores original flags
and disabled-action scheduling in finally. ENABLEPRESSURE and SETWHEELS are never
sent. `tire-pressure-physics-probe.json` retains the audited native enabled states;
its test binds real, inactive Wheel components and uses real SetProperty actions.
See [validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-wheel-pressure-application-protocol-196-unreleased).


## Observer gearbox condition input (local v198)

`guestEngineProtection.writers[].gearboxCondition` attaches the audited
`Damage type#0` GetFsmInt reader to the existing Corris GearboxDamage::Damage
writer profile. The parser requires its Reverse#6 SubtractFsmFloat guard for
`db_Gearbox::Data.Wear`. Runtime admission validates a one-shot enabled reader,
canonical db_Gearbox reference and DamageType output, and literal Data/DamageType
source names. The native private DoGetFsmInt boundary projects only eligible
available current-owner or approved parked condition for guest observers. It
leaves saved Data and the native source/cache intact. Missing/changed readers,
moved identities and aliased outputs pause only their protected consumer and
recover after repair. No new message or field is introduced; both peers require
protocol 198 for the changed native behavior.

See [validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#observer-gearbox-condition-input-protocol-198-unreleased).

Protocol 199 also supplies these existing wheel/gearbox readers with copied
accepted inputs during the claiming guest's current physics lease. Catalog
shapes, native signatures and saved-writer guards are unchanged. The claim copy
must match the player and body and is retired on release, competing accepted
motion, replacement or teardown. See [claim validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-condition-claim-inputs-protocol-199-unreleased).

## Periodic drivetrain saved wear (local v201)

`guestEngineProtection.writers` also includes
`CORRIS/Simulation/Systems/Drivetrain::Wear`, state Wear actions 1/3/5.
These native `SubtractFsmFloat` actions target `db_Driveshaft`, `db_Gearbox`
and `db_RearAxle`, respectively, at Data.Wear. The protection profile now has
17 writer FSMs / 87 scalar actions, plus its existing pose guard and 23 paused
graphs. Existing admission and state-entry guards suppress these three writes
on guests; this does not add a new catalog schema or pause the wear calculations.

The 12-FSM static audit at `build/drivetrain-wear-smoke/drivetrain-audit.json`
records the installed graph and asset hash. State 1 reads signed
`Drivetrain.differentialSpeed`, takes its absolute value and tests >1. The three
wear divisors are 42300/31500/22800; State 2 waits two real-time seconds before
repeating. These are asset defaults, not save data. New shared host wear needs
the exact driver input, native mounted-target authority and result publication.

[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-periodic-drivetrain-write-protection-protocol-201-unreleased)
cover the native arithmetic/writer/transition fixture; it supplies differential
speed explicitly and does not execute the drivetrain component's physics.

## Native differential-speed telemetry (local v202)

`vehicleDifferentialSpeed` binds the exact read at
`CORRIS/Simulation/Systems/Drivetrain::Wear`, State 1 #0. The catalog names
the Drivetrain object/component, differentialSpeed field, DiffSpeed result and
three normal cycle states (State 1 / Wear / State 2). Its parser isolates missing,
foreign or ambiguous metadata from unrelated catalog systems. It shares the
existing `SyncCatalogJson.VehicleWear.cs` reader file, without changing the
seven engine RPM inputs or 87 saved scalar write guards.

Runtime validation requires the current catalog, unique FSM and drivetrain,
registered body, exact native GetProperty signature, canonical object/output
variables, nonglobal output and public float member. Capture reads the current
component field only while its component and producer are active and the FSM is
started in a known cycle state. It never replaces a native property or output.
Unavailable/invalid values clear the wire availability and value together;
source repair retries after the normal probe interval. Body replacement and
session teardown retire the old binding.

The signed finite float and availability append to VehicleState 60 in protocol 202;
all existing offsets stay fixed and total message size becomes 30 bytes.
The wire carries native differentialSpeed without RPM or km/h conversion.
v202 adds no host wear consumer; the v203 consumer is described below. [Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#native-differential-speed-telemetry-protocol-202-unreleased).


## Host periodic drivetrain wear (local v203)

`vehicleDrivetrainWear` identifies the same CORRIS Wear FSM as
`vehicleDifferentialSpeed`, plus the three saved targets in native action order:

| Local target variable | Canonical saved mount | Rate / divisor |
|---|---|---|
| db_Driveshaft | CORRIS/Assemblies/VINP_Driveshaft | RateDriveshaft / 42300 |
| db_Gearbox | CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox | RateGearbox / 31500 |
| db_RearAxle | CORRIS/PhysicalAssemblies/REAR/AxleDamagePivot/RearWheelsStatic/WHEELc_RL/wheel_spindle_rl/VINP_RearAxle | RateRearAxle / 22800 |

All three entries are mandatory, unique, and below the registered vehicle root;
divisors must be finite and positive. Parsing errors isolate this profile from
telemetry and other wear protection. Native validation also checks the exact
three-state graph, action types/flags, canonical local operands, literal Data/Wear
writes, saved target identity/readiness, writer caches, and absence of saved/global
scratch aliases. Unexpected graph changes pause this FSM before native entry.

Only fresh accepted remote-driver input supplies the native comparison and
three divisions. Native saved writers and the two-second real-time wait remain
unchanged. A mod validation budget rejects the whole input when any target would
lose more than one Wear point in that cycle; it is not a native maximum speed.
Unavailable or stale input contributes zero. The host's actual drivetrain field
and GetProperty output stay native. Valid repaired bindings retry; local driving
and teardown restore native operation. Guest saved writers remain protected.
This does not publish resulting wear or complete mounted-part lifecycle/repair.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-periodic-drivetrain-wear-protocol-203-unreleased).


## Drivetrain wear result capture (local v204)

The v203 `vehicleDrivetrainWear` and `vehicleDifferentialSpeed` profiles also
identify the host result source. No new catalog fields are required. Publication
validates the entire native Wear graph, all canonical saved Data.Wear targets and
writer caches independently of the simulation binding. The producer must be
active, enabled and started; invalid or nonfinite data withdraws all three
values. Snapshot capture must never call the simulation recovery routine, which
can resume a blocked native write. Results are host-owned even while a guest
drives, and parked native repairs use the same capture.

The retained 12-FSM native audit locates downstream wear reads at Transmission
Driveshaft #2, Gearbox damage #0, Rear axle #1, and GearboxAutomatic /3 speed
Set stall speed #3. These remain native pending consumer protection. Transmission
Gearbox damage #5 writes saved Data.DamageType; Shaft break #0 dispatches
BREAKOFF to the driveshaft mount. Automatic gearbox State 1/State 3 #3 subtract
saved OilLevel. Publishing wear does not authorize guest replay of those writes
or failure transitions. The existing paused gearbox Data FSM does not by itself
prevent an external SetFsmInt from changing its saved variable.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-drivetrain-wear-publication-protocol-204-unreleased).

## Guarded drivetrain wear consumers (local v205)

Two `guestEngineProtection.writers` entries declare `drivetrainWear` readers:

| FSM | State / index | Part | Native reference | Local output |
| --- | --- | --- | --- | --- |
| Transmission | Driveshaft /2 | 0 driveshaft | db_Driveshaft | Wear |
| Transmission | Gearbox damage /0 | 1 gearbox | db_Gearbox | Wear |
| Transmission | Rear axle /1 | 2 rear axle | db_Rearaxle | Wear |
| GearboxAutomatic /3 speed | Set stall speed /3 | 1 gearbox | db_Gearbox | GearboxWear |

Each action must be the exact enabled, once-only native GetFsmFloat reading
literal Data.Wear through its canonical named reference. The output must be its
canonical local variable and cannot alias global or referenced saved data.
Reader identities cannot overlap writes, events or pose actions. In-place
metadata changes invalidate cached bindings before recovery or native entry.

The Transmission entry requires SetFsmInt Gearbox damage /5 targeting
`db_Gearbox.Data.DamageType` plus `eventActions` Shaft break /0 targeting
`db_Driveshaft.Data` with BREAKOFF. The named event must retain its native exact
GameObject destination, literal flags, zero delay and once-only schedule. The
automatic entry requires SubtractFsmFloat State 1 /3 and State 3 /3 targeting
`db_Gearbox.Data.OilLevel`. Selected scalar/event actions are disabled and removed
from active actions; direct named-event dispatch and stale callbacks after graph repair are guarded too.

The paused gearbox Data entry requires both `blockExternalIntWrites` and
`blockExternalFloatWrites`. Destination checks follow native caches, retain live
identities through rename/reparent/disconnect, and leave unrelated destinations
native. There are now 19 writer FSMs, 90 scalar actions, one named event, one pose
action and 23 paused FSMs. This profile protects consumer reads; it does not
complete native failure effects or fitted-part lifecycle.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guarded-guest-drivetrain-wear-consumers-protocol-205-unreleased).

## Automatic gearbox oil reader (local v206)

The guarded `3 speed` writer now requires `gearboxOil` metadata with state
`Set stall speed`, index 0, variable `db_Gearbox`, output `Oil`. It denotes the
native once-only GetFsmFloat of literal Data.OilLevel. Wear metadata and both
saved State 1/State 3 oil-writer guards are mandatory. The reader cannot overlap
any wear, scalar, event or pose action; other FSMs cannot declare this input.
`GuestDrivetrainReadData` carries the scalar name for the four existing wear
readers and this additional oil reader. Counts remain 19 writer FSMs, 90 scalar
guards, one event, one pose and 23 paused FSMs; there are now five drivetrain
float inputs.

Host oil capture uses the already validated gearbox saved target from
`vehicleDrivetrainWear`. Missing/duplicate/nonfinite/non-variable oil or aliases
to globals, producer scratch or saved wear withdraw only oil. An invalid wear
source still withdraws the complete result. The guest oil output has the same
canonical-reference and saved/global-alias checks as wear. Native Set stall speed
math remains unchanged: clamp oil to 0.02–6.3, divide 15120 by oil for UpShiftRPM,
and divide that stored result by 800 for StallSpeed. These clamps affect native
local scratch, not the saved or transmitted oil value. SetFsmFloat updates the
local Stallspeed FSM; the saved Data.OilLevel drains remain guarded.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-gearbox-oil-input-protocol-206-unreleased).

## Automatic gearbox oil-use observers (local v207)

Both automatic `3 speed` State 1/State 3 scalar guards at index 3 now require
`gearboxOilUse: true`. Only these exact SubtractFsmFloat actions targeting
`db_Gearbox.Data.OilLevel` may declare it; non-boolean metadata is rejected.
Runtime checks require canonical local OilLeakRate, everyFrame false and
perSecond false. These are once-per-entry native writes. The three Set stall
speed calculation actions must retain native types, enabled status, canonical
local references, subtraction and constants 15, 5000, 0.0000001 and 1.

The actual guest driver retains the callback as an observer while the native
saved helper is suppressed. Other guests retain disabled actions. Host request
validation uses current mounted automatic gearbox Data and temporary operands
for native rate calculation; delegated host observer callbacks cannot also
subtract saved oil. Counts remain 19 writers, 90 scalar guards, one event, one
pose, 23 paused FSMs and five drivetrain readers.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-automatic-gearbox-oil-use-protocol-207-unreleased).

## Damaged-gearbox wear observer (local v208)

GearboxDamage::Damage requires `gearboxWear: true` on Reverse #6, the native
SubtractFsmFloat targeting `db_Gearbox.Data.Wear`. The parser admits it only on
this exact path/state/index/target, and `gearboxCondition` requires this tagged
saved-wear guard. Runtime validation requires the canonical db_Gearbox operand,
literal 0.0525, everyFrame false and perSecond false. This is one subtraction
per native failure entry, not continuous reverse-driving wear. The installed
audit routes native damage types 1/2/3 into Reverse, which waits 0.6 seconds.

Actual guest drivers retain this callback as an observer; native saved writes
stay suppressed. Host application validates current installed Data, DamageType
1–3, saved Wear, unique fields, global aliases and target/cache identity. It
runs only the native subtraction, then publishes the host wear result. Oil and
failure callbacks must target the exact object from the validated host wear
producer, even if another object has the same scene path and values. The
shared gearbox callback guard retains entry deduplication and protects against
competing host oil/failure writes. Counts remain 19 writers, 90 scalar guards,
one event, one pose, 23 paused FSMs and five drivetrain float readers.
[Validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-gearbox-failure-wear-protocol-208-unreleased).

## Wheel rim presentation (local, protocol 208 unchanged)

Each `guestEngineProtection.writers[].wheelHealth.rim` profile selects the
native rim state, wheel object, radius scalar and sound-off action. Build
23268598 uses `Rim friction` actions 0/1 (`radius <- RimRadius` once and
`rollingFrictionCoefficient <- 0.1` every frame), plus `State 1` action 0 to
deactivate `FlatSound` non-recursively without resetting it on exit.

`SyncCatalogJson.WheelRim.cs` rejects overlapping action slots/states, missing
names and non-finite/out-of-range friction. Omitting the optional rim profile
keeps the saved-health guards and disables rim replay. Runtime also validates
the complete two-action state, target component identity, action signatures,
scoped sound target and the synthetic entry transition before replaying it.
`RIM` itself is only a local transition from `Check rim`; using it from a
healthy or flat state was ineffective. The selected replay bypasses the native
saved tyre-type branch and its saved-health puncture writer.

See [native checks and remaining repair/wear work](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#wheel-rim-presentation-protocol-208-unreleased).

## Host wheel-health sources (local v209)

`vehicleWheelHealth` defines root `CORRIS` and four distinct sources: wheel
index 0/1/2/3, the corresponding `WHEELc_FL/FR/RL/RR::Condition` path, and its
`tire/VINP_WheelXX` target path. The existing protected `wheelHealth.reads`
metadata selects native `State 1` #12 and `Flat friction` #3. Both read
`ThisTire::Data.TireHealth` into local `Health` every frame. Runtime capture
requires the canonical live target, unique initialized Data, matching warmed
native caches and a finite/non-aliased scalar. It does not run native getters,
wear or installation states during snapshots.

The parser rejects missing/duplicate wheel indices or paths and targets outside
the selected wheel. Malformed source metadata disables only this source profile;
guest saved-health protection stays intact. Missing/changed native sources
withdraw per-wheel availability in host message 205. Guests retain host inputs
outside driving leases, pause unavailable registered readers and recover without
writing personal saved health. Physical tyre repair/type/grip reconciliation and
guest-driving tyre wear remain separate tasks.

See [native checks and remaining work](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#host-wheel-health-inputs-protocol-209-unreleased).

## Firewood buyers (unreleased v220)

`firewoodBuyers` lists four canonical job, buyer, payment and outer LOD paths from
the installed graph. The payment path supplies a stable network identity; live
objects resolve through native `Logic.Buyer`/`ThisMan` and `Animations.PayMoney`
references. Customer 1 can reparent from CarPos to WoodPos, so runtime hierarchy
paths must not replace that identity. The host retains native job/offer execution
and uses all living players' fresh positions for six native visibility/departure
reads per site. Guests project host pose, buyer visibility and the native payment
label/collider while pausing local job and buyer decisions. The pre-existing
`hostPayment: firewood` guard remains active if optional buyer metadata fails.

## Sorbet parking brake scalar (v221, local/unreleased)

`vehicleParkingBrake.sources` identifies a vehicle root, its control path/FSM,
scalar variable and distinct idle/increase/decrease states. The current profile
covers Sorbet's `LeverPivot::Use.KnobPos`. Binding requires both native adjustment
states to clamp that same variable to zero and the same positive finite maximum
(currently 20). The normalized value travels in `VehicleClimate` with its existing
owner/sequence/final-release rules. Native timed inputs run only for the simulator;
observers set the accepted scalar without replaying the relative actions. Invalid
metadata disables this profile and reports an error without breaking other catalog
sections. Guest cleanup restores the original local scalar, and native world-save
protection remains active. Other vehicles' brakes are not claimed as covered.

## Persistent cylinder-head fasteners (v222)

`cylinderHead.boltPath` identifies `Bolts/Masked/BoltPM` relative to VIN1110.
Runtime sibling suffixes are handled by matching the native parent and leaf;
the ten controls are addressed by their validated native array indices, not scan
order. Binding reuses the native bolt action/visual validation and requires ten
unique integer slots, native unit steps, bounds 0–8, the head Data target and a
layer-12 trigger/visual pair for each control. Unsupported bindings leave guest
fastener controls disabled. The guest's saved array and head Data remain unchanged;
the reversible tool graph only requests a host turn and displays the returned
position. The host's aggregate is transmitted independently of the bolt array.

## Moose-meat factory (v224)

`mooseMeat` binds `Spawner/CreateMooseMeat::MooseMeat`, the `moosemeat0` prefab,
creation/load-output states and Use/Fire presentation/input states. Native factory
GetName reads CreatePrefab into SaveID: IDs are `moosemeat01`, `moosemeat02`, etc.
The disabled SetParent in Create product is intentional. Creation capture occurs
after SetName, before native item initialization is accepted. Use starts without
an active wait, so replicas disable both FSMs before cloning and never execute
native persistence/cooking/spoilage. Names/materials are read from native states.

Message 210 handles creation/state directly; `spawnContainers` and ItemSpawn's
trophy-only factory flag are unchanged. Malformed metadata disables only meat.
Static evidence is in `build/moose-meat-audit/native-moose.json` and
`native-meat-prefab.json`; both are extracted from installed build 23268598.
[Local validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#moose-meat-output-2026-09-13-unreleased-v224).

## Electricity payment forms (v225)

`utilityPayments` binds the two native electricity sheets, their guarded Check money
entry, receipt/close/error presentation, total text and meter settlement event.
These Pay buttons are removed from `buys[]`: their menu transforms are unrelated to
the world mailbox, an inactive host sheet need not be generically registered, and
replaying Date would repeat cash subtraction. Both players instead submit a quoted
invoice revision. The host checks the meter's live Bill reference, settles once and
runs native Pay bills; only the payer plays the receipt sound and closes its form.
The guest's retained UtilityBillState supplies debt, envelope visibility and power.
PhoneBill remains in `buys[]` pending its separate native charge calculation audit.

Binding validates the native read, comparison, cash subtraction, target/event,
settlement and total TextMesh before replacing the two payment action lists; lists
are restored after pending menus close on disconnect. Native meter save/load and
timers stay host-owned. The installed 23268598 level2 asset has both electricity
envelope Use components disabled. Tests explicitly enable those copied controls;
production does not override their native enablement. Natural envelope access and
physical input remain outside this transaction checkpoint. See BUILDING.md.

## Moose corpse and chopping (v228)

`mooseChop` binds the original `AnimalsMoose/Moose/Offset/dead moose(xxxxx)`
and its detached root name, CarHit death/idle states, the front/rear Chop graphs,
player axe/Pivot collision references, native Spawnpoint and the 10 named child
rigidbodies (root is body zero). Body order is part of message 214's contract.
The adapter initializes dormant FSMs before reading actions and verifies native
state signatures, axe comparison, four-piece limit/transitions, factory targets
and synchronous SPAWNITEM before intercepting guest Pieces/CarHit. The host runs
its original graph; guest food creation remains paused by the v224 meat adapter.
Malformed chop bindings disable this adapter independently of meat output.
Vehicle registration excludes the catalog detached corpse name before its mass
fallback, preventing guest push/cargo ownership of the 450 kg ragdoll.

Regenerate from installed `level2` using `tools/extract_fsm_assets.py` with
`--match AnimalsMoose --include-transforms`; compare against
`build/guest-moose-chop-audit/native-moose.json`. Inspect action arguments and the
ragdoll body topology before changing descriptors. A body-order or chop-semantics
change requires a protocol bump. Actual vehicle/axe collisions and native meat
save/reload still need acceptance; fixture commands enter native hit/check states.

## Phone invoices and settlement (v230, 2026-09-13)

`phonePayments` names both Sheets/PhoneBill forms, their request/receipt states,
CostFinal/TotalS, the four meter usage variables and four sheet tariff variables.
The native form performs local/long-distance multiply-adds plus CostBase (128 in
this build), clamped to 999999; UnpaidBills is a separate accumulator. Runtime
validates sources, operators, text destinations, wallet subtraction and native
Pay bills resets before replacing payment. The generic PhoneBill buy rule is
removed. Guests receive complete host invoice inputs and pause their loaded
phone meters; they never pay by replaying native debit/settlement actions.
The catalog's native binding and 25 local fixture checks are documented in
[BUILDING](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#shared-phone-bill-payments-2026-09-13-unreleased-v230).
Natural envelope access, actual call accrual, native save/reload and Steam
acceptance remain open; disabled copied controls were explicitly exposed by the
fixture. No normal player controls are enabled by this catalog change.

## Corris world parking joint (still v232, 2026-09-13)

`vehicleParkingJoint` identifies `CORRIS::LOD`, state `Remove joint`, action 0.
The installed native action is `DestroyComponent` with UseOwner and the literal
`FixedJoint`. The separate native `FixedJoint` FSM creates the root world lock
with 10000 break force/torque. Moving a dynamically simulated parked car without
recreating that lock leaves its cached world frame at the original position.
Runtime validates the initialized control, release action and unique root joint
before replacing only the world-connected parking joint at an accepted remote
pose. Native settings and object-variable references follow the replacement;
connected-body joints remain untouched. Kinematic motion postpones replacement
until physics resumes. Missing/changed metadata defers the unsafe move and logs
why; it does not disable unrelated catalog sections. No wire change is involved.

## Guest home-stove cooking (v233, 2026-09-13)

`stoves.paths` identifies `HOMENEW/Functions/ElectricThings/OvenStove` and
`YARD/Building/KITCHEN/OvenStove`, in that order. Each uses `Simulation::Data`
and four `KnobPower1`–`KnobPower4::Screw` controls. Binding waits until the native
FSMs have initialized and started before retaining variable/action references.

Knob bindings name `Rot`, `Data`, `Mesh`, `ScrewAmount`, the `Screw`/`Unscrew`
states, `State 1` readback and `State 2`/`State 3` resets. Runtime validates native
51.4-degree arithmetic, disabled clamps, signed wrap limits, local mesh rotation
and readback. A guest submits only plate/direction through request 219; the host
checks identity, fresh nearby position and sequence before running those native
actions. The host's mouse-input loop is never entered for a guest request.

The host simulation supplies `HotPlate1Heat`–`HotPlate4Heat` in full native
degrees, each plate's `GrillTrigger`/`FireTrigger`, `Fuse`, and the `StoveLight`
object. State 99 carries these results with nonzero revisions, knob rotations,
trigger masks and light/smoke flags. Smoke binds directly from native
`SetProperty` action 0 in `Stove light off` and `Smoke`/`Smoke 2`–`Smoke 4`.
All five actions must reference the same `EllipsoidParticleEmitter.emit` target;
the off state supplies literal false and each smoke state supplies literal true.
No emitter hierarchy path is inferred.

`ignitionEnabled: [false, true]` preserves the homes' actual difference:
`Start fire`/`Start fire 2`–`Start fire 4` contain a native hazard reset followed
by `SendEventByName`, disabled in HOMENEW and enabled in YARD. Guest stove
simulation stays paused while accepted host state is projected. Cleanup restores
original knob actions/values, temperatures, fuse, triggers, light, smoke emission
and all four `HotPlate*FireHazard` values before resuming it. Waiting, bound or
failed stove bindings reject legacy guest ignition reports.

After a game update, extract `--match OvenStove` with
`tools/extract_fsm_assets.py` and recheck both homes' action targets and enabled
settings together. This adapter does not provide the sausage-package factory or
the complete house-fire lifecycle. Existing moose-meat state 210 supplies that
food's cooked result and reconnect recovery. See
[validation and limits](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/BUILDING.md#guest-home-stove-cooking-2026-09-13-unreleased-v233).


## ATF refill (development protocol235)

`atfRefill` covers the native `atfoil0` prefab and
`Spawner/CreateItems::ATFOil` factory. The canonical prefix includes the zero:
real saved IDs are `atfoil01`, `atfoil02`, etc. Live shopping-bag outputs retain
their minted shared IDs; saved loose bottles derive their shared identity from
the native factory and saved ID. Replicas use the host's identity, quantity,
empty flag and creation pose, with both native FSMs disabled before cloning.
Guest-native saved bottles are hidden and restored on cleanup.

The live source is `ATFOilTrigger::Data.Fluid`, not the generic jerrycan
`FuelLevel`. Root `Use.Fluid` mirrors load/save, and native `UniqueTagFluid`
saves to global `SaveItems` (`items2.txt` on this build). Source simulation's
local-player distance gate is paused so host-away guests can pour. Native
emptying destroys the child trigger/particles while retaining the pickable
root and its small fluid remainder. That transition runs after the host's
paired source/destination scalar updates; empty does not mean despawn.

The destination is `CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox::Data` with
an actually installed automatic `ActivePart`. `OpenCap::Screw` has native
rotation1–359 and step33; `OpenCap/CapTrigger_ATFOil::Trigger` receives the bottle.
Its native subtract/add writers are suppressed on both peers. Fresh authenticated
requests retain a short pour lease; every host tick rechecks owner, living player,
physical capsule/sphere overlap, tilt, mounted part and open cap. Transfer is
bounded by source, capacity6.3 and native0.1units/second. Host amount is mirrored
to fitted part data immediately so a following save retains it.

The engine has an independent hinged body, so syncing the car root alone does
not align the filler. Message221 includes the cap subtree's pose relative to the
Corris root. Guests project that transient endpoint after root smoothing and
restore its original local pose on cleanup; engine bodies and joints stay native.
Guest pour validation uses the latest authenticated bottle transform for both
tilt and capsule geometry, so display smoothing cannot extend a withdrawn pour.

The exact inactive cap/filler/gauge FSMs are initialized without running startup
or saved mount actions. Guests receive absolute cap/gauge presentation; host
gearbox save variables are never copied into the guest's native mounted part.
The gauge belongs to the local pourer. Native cap openness has no save action:
joining a live session restores host openness; loading a native save closes it.

Catalog parsing isolates invalid/missing ATF metadata from unrelated systems.
Native bindings additionally validate action types, variable references,
transitions, capacity and source/target geometry. After a game update, re-extract
the ATF prefab, gearbox mount/cap and gauge before changing metadata. A prefix or
factory identity change affects the protocol and cannot be hidden by catalog edits.


## Flea-table checkout and proceeds (protocol 236)

`fleaSale` identifies SaleTable Logic/Sell/DayChanger, the rental selector, checkout,
MoneyFlea envelope, native basket and financial variable/state/event names. Native
bindings validate payment and rental action signatures/references before installing
receipt-driven checkout and collection. Generic buy/control registration excludes
both configured boundaries and their old canonical paths, preventing overlapping
cash mutation. Invalid metadata isolates flea transactions from other systems.

The selector's Add state changes the local cart; it must never emit an unpaid RENT
intent. Guests check out only rental weeks with an otherwise empty native Bought
array (zero merchandise total alone is insufficient). Host mixed merchandise keeps
its native purchase sequence. OpenHours, rather than the host's distance-controlled
LOD visibility, governs availability. Guest finance/cart/envelope values and paused
table FSMs restore at cleanup. Listing item IDs/prices and retirement still require
a shared manifest; the native table's name-plus-random-suffix key is not enough to
identify one of several same-named objects after a sale or restart.


## Shared flea listings (protocol 237)

`fleaSale` now includes `listingName`, `listingIdPrefix`, `listingIds`,
`listingPrices`, `pricingPath` and `pricingFsm`. The first supported family is
`Spawner/CreateItems::Chips`, whose native `Use.ID` is `chipsN` and display name
is `potato chips(itemx)`. The existing native collections persist prices under
`potato chips(itemx)OWNNNNNN`; the eight-character suffix keeps the native sale
price-guide slicing intact and distinguishes shared IDs from random vanilla tags.
No separate save file or mutable network ID is used as the durable identity.

The adapter uses the native table trigger and pricing sheet, validates host rental,
player distance, released ownership and resting placement, and resolves native
SELL/SELLRAND to the exact supported object before native GARBAGE retirement.
Guest table timers remain paused. Native RESET can restore old proxy snapshots;
expired shared keys are explicitly removed, with unsold products released from
the native loading pin. Native actions and temporary price UI are restored during
cleanup. Other product families and legacy random listings retain their native
host behavior; they are not advertised as shared guest listing coverage.

## Ignition-to-fuse-box wiring (v239, 2026-09-14)

`guestEngineInputs.wires` source5 now has a `connection` descriptor for its exact
Data, wire mesh, two Assemble endpoints, steering-column prerequisite and Status
gate. `build/corris-wiring-audit/native-wiring.json` is a fresh installed level2
extraction (115 FSMs, 199 transforms, five ArrayLists); native-steering.json adds
six steering FSMs. The saved wire's UniqueTag is WiringIgnitionFusebox, with
Installed persisted by Data/Save game in carparts.txt. The native endpoint flow is
Init → State1 → Assemble → Sound; the second Sound sends CLOSELOOP to the first,
whose Finish assembly sets Installed, activates the cable, broadcasts RESETWIRING
and disables the trigger parent. The native local distance tolerance remains0.1m.

Status/Steering uses the native steering-column Installed boolean to enable the
Ignition endpoint. The audited steering-column mount removal does not send DESTROY to this wire;
FireElectric/Init/Fire does. Shared native destruction/reconnection is covered,
not a new manual-removal control. WiringTool comes from the native Use global and
is checked against Save.UniqueTag because native pickup reparents its object.

The guest pauses this saved Data FSM, guards Finish assembly before native effects,
and replaces only Status/Steering's ignition activation input with a temporary
host boolean. It preserves every other wiring circuit's native Status behavior.
Source5 Connectable flag8 is published only when the host's validated connection
and steering-column prerequisite permit installation. Other circuit inputs retain
their old flags; other wires and complete fire/shock/part-fitting workflows remain
outside this descriptor. See the v239 BUILDING.md checkpoint for evidence/limits.


## Taxi pickup context (2026-09-14, still protocol239)

`taxiPickup` identifies the native job/customer/car, pickup states and host player
context inputs. `TaxiPickupBinding` validates the action fields before projecting
an accepted guest taxi driver's seat into the host customer's distance/look/vehicle
checks. Dormant and boarded customers are discoverable without activating the job.
Native host player globals remain unchanged and original inputs are restored on
cleanup. This block does not claim shared activation, call handling, live metering
or payment/receipt completion; see the taxi native audit in
[SYNC-SCOPE-AUDIT.md](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/SYNC-SCOPE-AUDIT.md#taxi-native-lifecycle-audit-and-pickup-prerequisite-2026-09-14-still-v239).


## Taxi service lifecycle (v240, 2026-09-14)

`taxiService` identifies the native job, tripmeter's stable Customer reference and
incoming handset FSM/states. `TaxiServiceBinding` validates native object links,
Answer/Occupied outputs, acceptance event, caller audio and the three job collider
targets before adapting them. Inactive objects and a walker already reparented to
the car are discoverable through the native reference. This is independent of
`taxiPickup`, which still projects an accepted guest driver's context into native
host distance/car checks.

Service messages 230/231 carry absolute host availability/customer presentation and
current-call answer/hangup intents. Guest job/customer/ringing decisions pause;
received strings resolve native clips/audio and are never arbitrary FSM events.
The generic taxi NPC mover is removed to avoid competing ownership of its pose.
Guest tutorial, native luggage rolls, payment simulation and local offers stay
suppressed; guest duty/meter, luggage/receipt identities and completed earnings
persistence remain separate open work. Native build 23268598 evidence and controlled
two-player limits are in the v240 BUILDING.md checkpoint.


## Taxi duty/meter (v241, 2026-09-14)

`taxiMeter` identifies the job, tripmeter, knob, button, display, native commit and
idle states. Binding validates native object links, +/-35 increments and clamp,
light flip, destructive reset targets, and both native odometer MpS reads. Native
`Volume dec` increases the knob and `Volume inc` decreases it. The visible native
knob pose can differ from the numerical rotation; the packet carries its exact
local quaternion, rather than reconstructing it from mode.

Messages 232/233 carry host values/presentation and authenticated current-revision
controls. Guests retain input states but pause independent calculations and the
unfinished payment terminal. Native host fare math reads accepted delegated gauge
speed with the existing two-second freshness rule. Changed bindings disable this
slice; original actions/variables/visuals/FSM flags restore on teardown. See the
v241 BUILDING.md checkpoint for controlled evidence and remaining fare dependencies.


## Taxi arrival, quote and cash (v242, 2026-09-14)

`taxiFare` identifies the native job/meter, stable Customer reference, terminal and
cash references, and boarding/arrival/commit states. Binding validates the meter
Price → terminal Cost read, Timer adjustment, delayed CASHIER target, cash Paid
output and customer Cost → IncomeTotal credit. Service must bind first so cleanup
captures the guest's original availability and cash visibility before fare state
presentation. The meter's terminal guard grants input only to this validated
adapter; independent guest printing and price-reset states remain suppressed.

Messages 234/235 carry absolute host quote/cash presentation and current-fare
charge/collect intents. Native arrival and meter pause permit a quote. The host's
customer then presents cash and owns the income transition. Remote collection
applies the native Paid/hide outputs without replaying the host achievement action.
Fare/control revisions and per-player sequence checks reject stale or repeated
requests; admission advances the control revision. No new persisted mid-fare state
is introduced. Receipt identity/handoff, luggage and full payday remain open; see
the v242 BUILDING.md checkpoint for evidence and limits.


## Taxi physical receipt (v243, 2026-09-14)

`taxiFare` now also identifies print/take/give states and the native receipt trigger
and physical-paper references. The terminal's print state adds Trip to OdoTotal and
Cost to IncomeReceipts, plays its printer flow and clears Price. Drop ticket detaches
one pre-existing Rigidbody from an inactive printer pivot. The customer trigger
finds that paper under the native ItemPivot, reparents it to the fingers, sends
CASHIER and returns it to the hidden printer pivot after ten seconds. No paper
clone is created and receipt handoff does not credit money.

`TaxiFareBinding.Receipt` validates accounting outputs, native hand lookup, release
and return parents, and completion event. Its `departureState` identifies the native 99-m visibility
check; the closest fresh living guest also counts so a distant host cannot suspend
the receipt return by hiding the customer. `ItemWorldSync.TaxiReceipt` registers the
inactive body under FNV-1a32("taxi:receipt") before it becomes visible. The existing
pickup guard includes this body; only the loose stage permits generic motion.
Printer/customer anchoring blocks stale item/cargo transforms and guest retirement.
The phase transition releases the native pickup joint before reparenting. Receipt
state/pose restores on rejoin, while native save owns completed accounting totals.
See the v243 BUILDING.md checkpoint for controlled evidence and remaining limits.


## Taxi customer luggage (v244, 2026-09-14)

`taxiService` adds luggageFsm, luggageResetState, luggageReleaseState, luggagePool
and luggage0–4 references. Installed build 23268598 has five usable PART rigidbodies:
three suitcases, one beer case and one mattress. The native Riflebag reference has
no Rigidbody and is absent from the selection pool; it is unfinished native content.
This corrects earlier inventory assumptions about six physical luggage items.

`TaxiServiceBinding.Luggage` validates each native reset target/pivot, reset pose
flags, world release target and the host's distinct native selection pool. Amounts
can draw six while that pool contains five; an action before the native zero check
bounds the count to available choices so the selection loop can finish. The pool
itself is unchanged. Guest selection and distance penalties remain suppressed.

Each native RESET advances the host epoch and removes the prior registry identities.
Message 230 appends the epoch, five-bit availability mask and five world poses. IDs
are FNV-1a32("taxi:luggage:" + invariant-decimal-epoch + ":" + zero-based-slot).
`ItemWorldSync.TaxiLuggage` registers the inactive native objects, uses ordinary
item/cargo motion only while selected, blocks second holders and guest destruction,
and recalls them with restored collision settings when reset. Guest activation sets
Transform pose before physics because inactive Rigidbody writes are ignored by
this Unity version. Rejoin uses the existing epoch and host selection, without a
new roll. Native save does not retain this mid-fare luggage state.

The native DISTANCES chain measures accepted shared item poses and adds 120 seconds
to customer MaxDelay for an active piece outside 10 m. It affects subsequent wait
and call timing, not a direct cash deduction. See the v244 BUILDING checkpoint for
controlled acceptance and limits.

## Taxi payday salary sheet (v245, 2026-09-14)

`taxiService` follows the phone's TaxiFunctions parent to `paymentsFsm`, then
`rundownLetter` → `envelopeFsm` → `sheetVariable` → `sheetFsm`. The native
`rundownList` contains eight floats; `rundownVariable` is the saved unread flag.
`envelopeOpenState`, `sheetCloseState` and `settledState` name audited entry points.
`TaxiServiceBinding.Payday.cs` checks the list size/type, native close target and
clamped Money source. Guest Payments was already paused by the service; it stays
paused. Only the native sheet's read-flag write becomes message 236, with the report
identity captured on opening. Camera/menu operations and mailbox hatch use remain
local. Restore closes a guest-owned open sheet and restores the original list,
letter visibility, variables and hook actions.

Native host Payments computes wages and writes bank/net income. At settlement the
adapter copies clamped Money into rundown slot 7 on both positive and zero-pay
paths: vanilla only writes it on positive payment, leaving stale net pay otherwise.
No bank amount is calculated or credited by this adapter. After a game update,
re-extract Payments, EnvelopeTaxiRundown and Sheets/TaxiRundown, including ArrayLists;
verify the salary list's fixed order and the sheet's close action before accepting
new native signatures. Installed evidence is in `build/taxi-payday-audit/`.

## Household fuse holders (development protocol246)

`householdFuses` identifies the house and apartment electricity databases, the
seven/four persistent holder IDs, native list/FSM names and turn/assembly/removal
entry states. Binding follows the live `Holders`, `Slots` and boolean `Fuses`
ArrayList proxies; `arrayList` is a property on the game's proxy component. Holder
identity comes from native `Use.ID`, not the renamed `fuse holder(Clone)` object.
Native slot IDs are local to each home; network slots are house 0–6/apartment 7–10.

`ItemWorldSync.HouseholdFuses` validates native fuse consumption, clamp/shock,
Rigidbody/parenting and save action signatures before installing hooks. The host
runs native Use, Assembly, Screw, Removal and save logic. Guests hide and pause
their own saved holders, use input-capable copies with native Use disabled, and
present host holder state and boolean circuit lists. These copies never save or
independently blow fuses. Loose holders use stable item identities; fitted holders
leave item motion authority. Native fitting releases hand/cargo ownership.

Messages 237–239 carry absolute holders/circuit power, revision-bound guest intents
and actor-specific acceptance/shock receipts. Guest screw turns suppress the host's
shock action; the accepted receipt runs only the requesting guest's native check.
Cross-home holder fitting is rejected because vanilla retains the holder's original
electricity database. Rebinding after a game update requires re-extracting both
databases, persistent holder prefabs, FuseTrigger, each fixed slot and the Screw /
Removal / Use graphs. Main-switch or stove Fuse flags cannot substitute for these
individual circuits. Loose fuse creation/consumption still uses I05 supply bindings.

`partPickupState` names the native PART branch, separate from the bag/supply ITEM
pickup entry. Both are guarded before ownership can be duplicated. Loaded loose
holders can retain a socket parent: use positive native Tightness as well as the
parent to identify an installed holder, matching native `Installed?`.

`tractorTrailer` (v247) identifies the KEKMET hook/release controls and the FLATBED
chassis, tipping bed and support. Its native FSM state signatures and joint graph
are checked before binding. The dedicated adapter owns all three bodies; the
mass-based vehicle fallback excludes the trailer chassis. A malformed profile
isolates this adapter instead of disabling unrelated catalog sections. Native
build23268598 evidence is retained in `build/tractor-trailer-audit/`.


`sausages` (v248) lists all eight native `SausageTrigger :: Logic` paths and the
package/prefab, food variables, native states and child meshes. Before binding,
the adapter checks the condition read, exactly four direct CreateObject actions,
four native condition writes and exact package GARBAGE target. Native writes point
at the prefab; the adapter instead copies the package condition onto each captured
clone. This avoids contaminating the next package's first output. The island NPC's
same-named sausage lacks the Fire FSM and is deliberately outside this prefab rule.

The prefab has no persistent ID or SAVEGAME routine. Each conversion supplies four
session IDs. Packages use their native `sausagesN` identity consistently through
bag manifests, loaded products and retirement; generic display-name ordinals are
unsafe after a prior package is consumed. Guest cooking/spoilage is paused, native
eating remains available for edible food, and host state supplies appearance and
condition. Re-extract both `level2` triggers and `sharedassets3.assets` sausage0/Use
and Fire after a game update. Native evidence: `build/sausage-opening-audit/`.


## Taxi human passengers (unreleased v249)

`taxiPassengers` selects the exact taxi root and relative `driveTrigger`,
`driverMass`, `customerMass` and `tutorial` transforms. On build 23268598,
MassPassenger is the **rear-right fare seat**, so it must not feed the generic
front-seat resolver. Front right mirrors MassDriver with the existing forward
nudge; rear left mirrors MassPassenger. Wire indices 0/2 are available to humans;
index 1 remains reserved. Active tutorial or inactive vehicle refuses seating.
Missing fields disable this profile alone; missing native transforms retry at the
next discovery scan. Re-extract the taxi and customer graph when updating builds.
Native audit and accepted two-game evidence: `build/taxi-passenger-audit/`.


## Home coffee (unreleased v250)

`coffee` identifies the exact household pot/cup and their native input, data,
water/grounds, pouring, lid, surface and sound paths. Vendor cups are deliberately
outside this profile. The native packet factory uses prefab `groundcoffee0` and
IDs **groundcoffee01, groundcoffee02, …**: the zero belongs to the prefix, not to
counter padding. Packet IDs must preserve that prefix across creation and load.
`saveTagVar` plus `potSaveTag`/`cupSaveTag` rediscover the household roots after
pickup/drop reparents them; display names alone also match unrelated vendor cups.
The host retains Data/Fire/Empty and grounds/water writers; guests suppress those
writers and reconstruct packet replicas. Cup filling replaces both native
independent transfers with one host transaction. Native Check drink still checks
hand/helmet eligibility; its accepted result enters Play anim/DRINKCOFFEEHOME.
Changed native drink signatures or missing variables isolate coffee. Malformed
metadata leaves other catalog sections intact. Re-extract level2 home pot/cup and
sharedassets3 groundcoffee0/Use, plus Spawner/CreateItems/Coffee, after game updates.
Native extraction and controlled test artifacts live in `build/coffee-audit/`.


## Train (unreleased v251)

`train` binds the absolute scene root `TRAIN`, its two spawn parents and targets,
and the one moving `TRAIN` body beneath either parent. Root lookup must use the
absolute path: the moving body has the same name. The native Move graph alternates
State 1 → Delay → State 2 → Delay2 at 30 m/s with 250-second waits. Reset holds its
local track axes; both graphs are paused on guests. Six guest controllers are
paused in total: Move, Reset, Whistle, TunnelAudio, WhistleTrigger/Raycast and
Mesh/Lights/Lights Switch. Player's native collision/comparison/death graph stays
local and active; it must not be included in generic FSM replay.

The ordered `colliders` list contains eleven unique relative BoxCollider paths.
Its order defines TrainState bits 0–10; changing that order is a wire-semantic
change. Waiting hides Mesh but leaves root Coll active. Light activation and horn
entry counters follow the host. Native train state has no save tags: new game
loads start its default leg; reconnect reconstructs the current host leg.

Missing fields, duplicate collider paths, or a collider count other than eleven
isolate this catalog section. Runtime checks native movement speed, state/action
signatures, endpoints, audio and collision inventory before applying suppression.
Re-extract TRAIN from level2 after game updates. Audit data and disposable-game
checks are retained locally in `build/train-audit/`.


## Light-bulb boxes (unreleased v252)

The `partsPackages` LightbulbBox profile is capacity one, fixedCapacity=true,
contentsFsm=Lightbulb, contentsSpawnPointVariable=SpawnPoint, loadClampIndex=1.
`bulbContents` names the loose prefab, display name, Data FSM, Wear and initial/
ready/factory-idle states. Only one such profile is supported; it cannot also have
supplyContents. Parser checks reject multiple bulbs/capacity, ambiguous factories
and identical initialization/ready states.

Native sharedassets3 `lightbulbbox0/Use` Create Plug has four actions: audio, GUI,
SetFsmGameObject and SendEventByName. It enters Empty directly; Empty sets Quantity=0.
There is no Check quantity/Delay/decrement action. Display name is
`car light bulb box(Clone)`, loose output is `car light bulb(itemx)`.
Spawner/CreateItems Lightbulb has no save ID or counter and returns to State 1.
Its two creation actions instantiate the prefab and set Data.Wear. Loose Data
starts with RandomFloat/SetName/SetIsKinematic then idles in State 3, with no save
or garbage globals. Guests start in State 3 to preserve host condition.

Unopened boxes use native persistent IDs `lightbulbbox01`, `lightbulbbox02`, …;
consumed boxes are deleted at save. Loose bulbs only survive a live-session rejoin,
not a cold reload. Installed-bulb fitting/removal is a separate graph/roadmap task.
Re-extract level2 Spawner/CreateItems and sharedassets3 bulb/box prefabs after a
native update. Audit and controlled-game artifacts: `build/bulb-audit/`.

## Advert delivery (unreleased v253)

`adverts` binds `JOBS/ADs::Data`, `ResetBoxes`, the singleton pile referenced by
Data.Pile, and 27 scene `WaitAd` mailboxes. Use the native **BoxIndex**, not a
runtime scene ordinal: several mailboxes have identical literal paths, while
`ScenePath.Of` adds sibling suffixes. The catalog's paths are literal ancestor
names. Filter out asset templates, validate each index/path/database reference,
and require exactly the audited index set. Native saved flags contain 28 booleans;
index22 has no scene mailbox in build23268598 and must remain unbound.
The `mailboxRoot`/`lod` names also identify mailbox roots beneath a local-camera
LOD controller. Those small roots temporarily move outside that controller,
preserving world pose, while house/NPC LOD remains native. Otherwise a guest can
see a mailbox that the host refuses as inactive. Parents/transforms restore on
teardown; lookup validates the original catalog path before moving each root.

The phone-number graph `CARPARTS/PARTSYSTEM/PhoneNumbers/08231206::Data` sends JOB
to the job root on CALLED. Data then waits before scheduling the Tuesday/Friday
pile. New job resets all flags and Sheets=30. Friday Calc pay 2 uses the native
PricePerAd=17, clears Delivered and sends its bank transfer. Guests pause Data and
ResetBoxes so neither local scheduling nor native payday races the host.

The pile Use.Open decrements Sheets and creates `advert(Clone)`. Its CreateObject
storeObject is originally None; the adapter temporarily captures that exact output
and restores the field on teardown. The sharedassets3 `advert` prefab has a
Rigidbody and no FSM/save identity. Session IDs and existing item ownership carry
loose sheets; native hand release must precede destruction of a held sheet.

Mailbox Open destroys AdvertObject and presents the hatch; Close increments
Delivered and sets the exact saved array entry. The host validates and reserves
one held sheet before native Open. Guests retain only hatch presentation actions.
Repeated/obsolete requests cannot consume another sheet or credit another delivery.
Generic pile Open replay and old WorldProgress classified-job scalar application
are superseded by this adapter.

Native Save game stores the 28 flags, Delivered, Stage, Sheets and pile transform
under AdJobBoxes/Delivered/Stage/Sheets/PilePos tags. Loose sheets are not saved.
Re-extract the level2 job/phone/mailbox graphs and sharedassets3 advert prefab when
updating this profile. Retained native extraction and controlled-game artifacts:
`build/advert-audit/`. Missing or invalid metadata disables only this catalog profile.


## Motor-oil containers (unreleased v254)

`motorOil` binds `Spawner/CreateItemsSeparate/MotorOil::MotorOil`, the native
`motormoil1` prefab, root Use, child MotorOilTrigger/Data and fluid_particle.
MOil1/2/3 forward grades Type0/1/2 to the single factory. Create product increments
the counter, creates/grades/parents/names its exact output. SaveID is `motormoil1`;
native IDs are `motormoil11`, `motormoil12`, etc. Cold reload confirms the native
factory restores those IDs, Type, Fluid and transform, including empty bottles.

Use.Get data reads the native AssemblyDatabaseOther MotorOilMaterial and
MotorOilViscosity lists. The installed build uses materials motoroil1/2/3 and
viscosities .5/.6/.7. Guest clones invoke the validated material actions, then
apply the host viscosity. Use.State3 copies Fluid into Data; Data.State2 clamps
0–4 and copies it back. Use.State4 renames to empty(itemx) and destroys trigger
and particles. Use.Save writes ID transform, Type and Fluid; GARBAGE marks
Consumed and native save deletes the tags. Host source FSMs remain native.

The guest factory/global transitions and guest original bottles pause; clones
skip startup/save/load. On teardown, activate originals before restoring their
FSM flags, otherwise RestartOnEnable replays State1 and overwrites their ID.
The generic saved-product manifest excludes these dedicated bottle descriptors.
Replica pour colliders stayed disabled in v254; v255 enables them with the validated cap/pan authority path below.

Native binding identified for the v255 implementation below: VIN1110/VINP_RockerCover/OpenCap::Screw and its
CapTrigger_MotorOil::Trigger, the correctly attached VIN1010/VINP_Oilpan and
saved OilLevel/OilDirt/OilViscosity. Source trigger rates/cap geometry require
revalidation when implementing the conserved guest transfer. The audit extracts
are in `build/motoroil-audit/`; malformed metadata disables only motor-oil sync.


## Corris engine-oil refill (unreleased v255)

`motorOil` now requires cap/capFsm, fill/fillFsm and gaugePath/gaugeFsm.
Only started parts with complete native save identity supply OpenCap templates;
resource-only factory prefabs are excluded. The host resolves the actual installed
block → head → rocker-cover mount and oilpan using `guestEngineInputs.block`.
No pan, removed parts or an unavailable cap withdraw the destination and advance
the epoch. Detached engines are outside this bounded Corris path.

OpenCap/Screw uses Rot 1–359 with 33-degree steps, CapMesh and
CapTrigger_MotorOil/Trigger. The latter has a native sphere trigger and 3.7-litre
MaxCapacity. Its Pouring writes consume/add .1 L/s, subtract OilContamination
3.2/s and move viscosity toward the bottle by .06/s. Native fill writers pause
on both roles; guest original caps pause/hide and a separate cap renders host
world pose. The host validates ownership, current actor/source poses, overlap,
angle and epoch before paired scalar writes. Bottle source/root Fluid and mounted
pan Oil/OilContamination/OilViscosity mirror saved OilLevel/OilDirt/OilViscosity
immediately. The native one-second mirror alone cannot protect an immediate save.

Pan Data.Save uses UTOilLevel, UTOilDirt and UTOilViscosity in SaveCarparts.
The native drain plug is independent: Details/BoltPM/Screw saves PlugTightness
via UTPlug and activates Bolts/Oil/Calc below tightness7. That drain subtracts
.2 L/s, so an open plug can lose oil faster than refilling supplies it. Production
retains this behavior. The cap has no native save tag and closes on cold load.
Native extraction and controlled run evidence: `build/oil-refill-audit/`.

The host bottle's Save.GetFsmFloat is replaced by a validated source-or-root
read. Empty destroys MotorOilTrigger, so later native saving must not read that
missing source. Repeated cold-load testing also reproduced the startup race below.
The remaining native SaveTransform/SaveInt/SaveFloat actions and tags are retained.
The original getter is restored on teardown.

Host native Use.Load also pauses child Data and temporarily removes only its
GLOBALEVENT transition until State3/State4 completes. A nearby tilted child can
otherwise copy its default four litres back during Load's one-second wait and
skip Get data/Load2, also losing native material/viscosity initialization. The
completed root values seed the child before its previous enabled/restart state
and native global transition are restored. Initial world-discovery reset keeps
these in-flight handoffs; scene/session cleanup releases retained ones.

## Advert-job telephone enrolment (v256)

`advertPhone` names the native 08231206 listing root, job, speech/subtitle and
three calling/keypad/handle/ringing paths. Household bill0/1 refer to apartment
PhoneBills2 and old-house PhoneBills1 respectively; the taxi has no household
phone bill. Each native handle must reference that exact keypad. The old house
also has an incoming-only `Use` FSM with the same name/path; never select by name
alone. Relative keypad/handle/ring paths have no leading slash.

The bindings verify native Call billing, the real-time duration variable,
Hangup2/CALLED and listing State1/JOB target before installing hooks. They replace
only the advert call's accounting/commit path; native digit entry and other
numbers remain separate. `advertPhone` parse failure does not disable the existing
`adverts` delivery catalog. Native action evidence is retained in
`build/advert-audit/native-calls.json`; the phone-specific extraction/validation
record is under `build/advert-phone-audit/` (ignored local artifacts).

## R20 battery boxes and ordinary contents (v257)

`partsPackages` includes `R20BatteryBox` on `Spawner/CreateItems`, prefix
`r20batterybox0`, capacity 4, fixedCapacity true, native `Create Plug` opening and
loadClampIndex 1. Its direct `R20Battery` contents use local SpawnPoint, prefix
`r20battery0`, display name `r20 battery(Clone)` and inert ready state `State 5`.
There are now 34 package profiles; replacement-part factories are unchanged.

`supplyContents.retirementVariable` accepts only Consumed or Destroy (default
Destroy for the existing fuse profile). R20 garbage has one SetBoolValue and its
Save BoolTest deletes on Consumed. Fuse garbage has two SetBoolValue actions and
Save deletes on Destroy, preserving fitted Consumed fuses. Runtime checks validate
the selected layout, identity capture, factory output, load/save and deletion.
Both use the existing SupplyItemState; R20 cells have no native charge scalar.
Native extraction and controlled save/rejoin evidence: `build/r20-audit/` (ignored).
Battery fitting and appliance charge/use are separate from these contents.
