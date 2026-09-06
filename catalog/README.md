# Sync catalog

Per-game-build data describing *what* gets synchronized (PLAN.md §4.2).

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

## Two files, two jobs

| File | Purpose |
|------|---------|
| `sync-catalog.json` | **Shipped with the mod.** Curated rules the runtime loads. |
| `dump-23268598.json` | **Dev reference** for Steam build 23268598 (GAME scene, post-sleep F9 dump 2026-06-13). Full F9 dump (~8600 FSMs). ⚠ `toolsVersion 0.1.0` — **no `actionTypes`, no `globalTransitions`**; re-dump with tools ≥ 0.2.0 before relying on it for sync work. Diff across patches with `tools/catalog_diff.py`. |

## Rule sections in `sync-catalog.json`

| Section | Registers as | Example |
|---------|----------------|---------|
| `doors` | Door handles (house, car, sauna) | Open door / Close door; garage Open/Close |
| `controls` | Buttons, knobs, interactables | SORBET hazard, beercase Remove bottle, Fleetari brochure Set job / On / Switch |
| `switchRules` | ON/OFF style toggles | Lights, radiators, fireplaces, TV |
| `ignitions` | Key/ACC FSMs | `IGNITION` objects with ACC on / Motor OFF |
| `starters` | Engine run/stall FSMs | SORBET/CORRIS Starter, CORRIS Pushstart |
| `buys` | Host-authoritative purchases / payments | Inspection, shops (`template: shopBuy`), Fleetari, cash register |
| `parts` | Car-part assembly Data FSMs | `(VINXX)` bolt on/off, install, remove |
| `bolts` | Wrench Screw FSMs | Tight? / Loose? / Set pos |
| `vehicles` | Rigidbody roots treated as vehicles | `minMass`, `namePrefixes` |
| `pickables` | Synced item rigidbodies | `(itemx)` suffixes, optional Use FSM probe |
| `consumables` | Food/drink despawn hooks | Destroy + drink empty states |
| `vehicleClimate` | Frost/heater FSM path filters | SORBET / CORRIS car-temp roots |

Shared rule fields:

- `pathPrefix` — scene path must start with this (optional)
- `pathContains` — substring match (optional)
- `objectName` — exact GameObject name (optional)
- `objectNameContains` — substring in GameObject name (optional)
- `fsmName` — required PlayMaker template name
- `states` — synced states (all must exist on the FSM)
- `requireStates` — extra states that must exist but are not synced (switch shape checks)
- `excludePathPrefixes` — skip paths starting with these prefixes

`buys` entries use `entryGuards` (`state` + `event`, optional `optional: true`) and
`resultStates`, or `template: shopBuy` to infer guards from a generic `Buy` FSM.
`parts` add `optionalStates` when present on the FSM.

## Adding a rule

1. F9-dump the GAME scene (or use an existing dump under `<game>/WinterMP/dumps/`).
   Schema v2 dumps also include each state’s PlayMaker action type names, which are
   essential when a transition shape alone cannot prove whether it charges money,
   rolls RNG, spawns an object, or only updates presentation.
   **Check `meta.toolsVersion` before trusting a dump.** `0.1.0` has neither
   `actionTypes` nor `globalTransitions`; `0.2.0` has both. Without
   `globalTransitions` an event can appear in `events[]` with no state pointing at
   it and still be a live entry point — absence reads as "unknown", not "none".
   `dump-23268598.json` is `0.1.0`, so it cannot answer either question.
2. Find the FSM: `python tools/extract_fsm_details.py dump.json show:ButtonFoo`
3. Add an entry to the right section in `sync-catalog.json`.
4. Rebuild — `sync-catalog.json` deploys next to `WinterMP.Core.dll`.
5. Both players must show the same **cat** hash on the debug overlay (TAB).

Handshake refuses a catalog mismatch (same as protocol/mod/game version).

## Raw dump workflow

1. Run the game with `WinterMP.Tools` — auto-dump ~20s after level load, or **F9** manual.
2. Copy `WinterMP/dumps/catalog-<timestamp>.json` here as `dump-<gameBuildId>.json`.
3. After a game update: `python tools/catalog_diff.py dump-old.json dump-new.json`
4. Fix `sync-catalog.json` if paths or state names moved.

## Sleep / time skip (build 23268598)

From `dump-23268598.json` (post-sleep F9). Host sleep uses `SleepTrigger :: Activate`:

| Path | States (consent hooks) | Events |
|------|------------------------|--------|
| `HOMENEW/Functions/FunctionsDisable/Sleep/SleepTrigger` | `Confirm`, `Get positions` → `AnimateSleep` → `Sleep` → `Sleep time` | `ACTIVATE`, `STOP`, `ABORT`, `DAY` |
| `GIFU(...)/LOD/Sleep/SleepTrigger` | same template (in-vehicle sleep) | same |
| `CABIN/LOD/Sleep/SleepTrigger`, `COTTAGE/LOD/...` | same template | same |

Runtime: `PlayerSleepHook` hooks `Confirm` / `Get positions` on any `*/Sleep/SleepTrigger :: Activate`;
`Get positions` rolls back to `Confirm` until all guests accept. Post-sleep TimeSync fires on `Calc rates`.
Abort: `STOP` / `ABORT`. Proceed after consent: `ACTIVATE`. Guests get `SleepConsentResult` + fatigue reset.

## Death / respawn (build 23268598)

| Path | Role |
|------|------|
| `Systems/Death :: Activate Dead Body` | Master death/orbituary FSM — hook `Take photo` (death start), `State 2` (non-permadeath respawn after SAVE) |
| `Database/PlayerDatabase :: Simulation` | ES2 tag `UniqueTagPlayerPermaDeath` (character creation) |
| `Systems/Steam :: Achi` | Achievement bookkeeping — `_DEATHON` / `_DEATHOFF` when mirroring host permadeath on guests |

Runtime: host sends `sessionFlags.permadeath` in handshake; guests mirror the ES2 flag locally.
Permadeath ON: one death triggers group wipe on all clients. Permadeath OFF: dead player hides avatar,
completes vanilla orbituary SAVE flow, sends `PlayerRespawn` when the death FSM finishes.

## Guest rejoin (build 23268598)

Host keeps stable `playerId` slots per SteamID for the session. On disconnect the host writes pose
to `wintermp-guests.json` (needs when `PlayerNeedsReport` has arrived). Reconnecting guests get
the same id, snapshot + `GuestSpawn` with last pose, and chat `* name reconnected`.

## Still heuristic in code

Vehicle gauge/system FSM binding (speedo, fuel, blinkers, revs) remains in
`WorldSyncManager.EnsureVehicleSystemsProbe` until moved into declarative bindings.


### Lotto tickets (`lottoTickets`, v103)

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

### Trophy factory identities (v106)

`trophyFactories` binds 15 exact path/FSM pairs to their native ID prefixes, prefab
names and display names. Common fields identify the direct `New` output, creation
completion, factory idle state and native item initialization/persistence states.
Do not replace these with display-name matching: five different award classes use
only three display names. The adapter checks action layouts before binding and
keeps guest replicas separate from guest save objects. After game updates, extract
both `Spawner` from level2 and trophy prefabs from sharedassets3.assets; the v106
checklist in `docs/BUILDING.md` records the verified hashes and native checks.

### Parts-package factories (v109)

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

### Native part save identities (v110)

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

### Replacement-part factories (v111; presentation v115; fitting/removal v116–v118; adjustment v119)

`replacementParts` describes the 30 `CARPARTS/PARTSYSTEM/SPAWNERS_*::Spawn` contents
factories used by standard boxes. Each rule names the exact prefab/save prefix,
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
can replay the saved setting without native installation effects; the other 28
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

### Guest package opening (v112)

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

### Guest replacement bolt bindings (post-0.1.32)

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

### Alternator hand adjustment (v119)

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

The `shoppingBags` adapter (v120) binds Store/Fleetari bag factories, persistent
`Use.ID`, native contents dispatches and local pickup states. `spillFactoryPaths`
identify exact output owners; prefab `SetName` actions map template names to live
names. Native products without a validated replica adapter are rejected before
opening a bag. The old `spawnContainers` descriptors are retained as catalog history;
runtime bag synchronization uses the new adapter and does not salt IDs by scan order.
