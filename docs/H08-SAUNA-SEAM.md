# H08 electric sauna timer (bounded implementation)

## Wire decision, before implementation

Protocol 265 adds dedicated SaunaTimerIntent (270) and SaunaTimerState (271),
channel 0 reliable ordered. Legacy HeatSourceIntent/State remain unchanged:
light/feed/steam and byte heat cannot represent this timer or revision safely.
Intent: SourceId:u32, Epoch:u32, Actor:u8, Sequence:u32, ExpectedRevision:u32,
Timer:f32 (absolute native knob value), Eye:vec3, Direction:vec3.
State: SourceId:u32, Epoch:u32,
Revision:u32, Actor:u8 (255 for observation), Sequence:u32, HighWater:u32,
Status:u8 (observation/accepted/rejected), Timer:f32, Time:f32, KnobAngle:f32.
Only the authenticated actor may request, no object ownership lease. A request
must select exactly one native +/-10 step clamped to 1..120, not an arbitrary
jump. Cold native Timer=0 remains a valid observation, never a requested setting.
Epoch binds the session; sequence high-water survives same-session rejoin.
ExpectedRevision denies competing/stale observations. No wraparound acceptance.
A native partial failure latches the adapter faulted, emits no success and never
retries the action. Reentrant calls consume their sequence but cannot mutate or
recursively publish. Periodic states/admission and results are absolute.

## Audited native seam

Fresh read-only installed-asset extraction in rounds/000087-work/sauna-assets.json
(level2 SHA-256 in its metadata; static definitions, NOT native execution):
YARD/Building/SAUNA/Sauna/Kiuas/ButtonTime::Screw:
- Screw: FloatAdd Timer += ScrewAmount (10), FloatClamp 1..120, SetRotation
  CapMesh local Y=Timer. Unscrew uses FloatSubtract with the same clamp/rotation.
- Wait: FloatOperator Math1=Timer*6, SetFsmFloat Simulation::Time.Time=Math1,
  then native real-time 0.1 second Wait.
- MousePickEvent rayDistance=1, scroll-wheel TIGHTEN/UNTIGHTEN inputs.
- Check data/Load game/Save game use existing SaunaTimeKnob in savefile.txt.
  Timer IS natively saved. Do not invent a timer sidecar or force a cold reset.
Simulation::Time retains vanilla heat/electricity logic; Time countdown and knob
mesh writers on guests must not override the host timer result.
Kiuas/StoveTrigger::Steam is nested, not a direct StoveTrigger. It is identified
but deliberately NOT hooked into timer authority or the legacy STEAM shortcut:
water/dipper and steam resource authority remain out of scope.

## Evidence status

H08 stays partial. Round 000116 repairs and verifies the bounded portable slice:

- Full Net suite: 5,437 pass, including both registered-message sweeps. Their
  sauna fixtures now supply valid source/epoch/revision/status/bounds, just as
  other strictly validated messages do. Production validation is unchanged;
  explicit invalid read/write/client regressions still reject empty admissions,
  wrong sources, invalid receipts and nonfinite/out-of-range values.
- Source-linked heat-source suite: 60 pass (30 sauna, 30 existing cabin). Actual
  HeatSourceSync discovery, input interception, native-action adapter, authority,
  codec, absolute guest application, rejoin high-water and Clear run against
  counted engine doubles. Guest scroll leaves local values untouched and emits
  an intent; the host executes one step and one result converges both peers.
- The original guest pre-admission regression already passed when reproduced.
  The original host-local regression failed because constructing the guest
  replaced process-global Physics.Hit with the guest collider. Activating the
  host fixture restores its engine context, not just SessionManager. All original
  assertions remain. A separate regression deliberately keeps the wrong collider
  and verifies that the unchanged host contact guard rejects it before a valid
  host-local action succeeds.
- Negative tests include identity/epoch/source/revision, unavailable/dead/stale/
  distant actor, invalid eye/ray/contact, duplicate/competing requests, callbacks
  reentering read/observe/turn/publish, and partial native failure without retry or
  success. Save/load action arrays and loaded values survive binding and Clear;
  this tests integration boundaries, NOT native disk persistence.
- Core net35 Release builds with DeployToGame=false and the read-only installed
  game DLL path (zero warnings/errors). RNGCryptoServiceProvider now uses the
  existing cabin-style lifetime provider instead of the unsupported IDisposable
  using pattern. Wire layout and authority semantics remain protocol 265.
- Launcher tests: 20 pass. The independent Python suite remains red (182 run,
  one failure/one error) solely on the pre-existing test-package protocol 264 vs
  source 265 mismatch. Manifest, player-doc and installer metadata are outside
  round 000116's allowed paths; neither the gate nor the protocol is weakened.

Raw commands, exit codes, TRX assertions and hashes live in
`autonomous/rounds/000116-work/` (environment root). No game process or native
deployment was used. Native injected state, ordinary keyboard/mouse, genuinely
different saves, native rejoin/save reload, Steam/two-PC and four-player soak
remain NOT_TESTED. This is not native FSM or gameplay acceptance.

## Round 000121 — cold-zero Unscrew and incomplete knob rotation

The round 000116 repairs above were already present. Before any source edits,
the original Net suite (5,437), source-linked suite (60) and a non-incremental
Core net35 build all passed in `rounds/000121-work/baseline/`. The old host-local
fixture failure did not recur; 000117's failure recovery concerned package
metadata, not a newly failing sauna test. Those historical results are not fresh
native evidence.

Tests-first auditing exposed two remaining defects within the same timer seam:

- Cold native Timer=0 followed by Unscrew requests 1 because the native -10
  operation clamps to 1. Inferring direction from `requested > current` wrongly
  selected Screw and rejected this valid guest action. Authority now selects
  direction by matching the native clamped step. A request for 0 is still invalid.
- A native SetRotation that does not update the mesh could formerly publish
  accepted Timer/Time with a stale KnobAngle. Authority now requires the local Y
  result to match the requested knob value within 0.01 degrees (Euler readback
  roundoff); otherwise it faults without publication or retry. The observed
  angle is preserved in the result, not replaced with a fabricated target.

`red/` records the new failures before production changes. `red-expanded/`
records 4 failing Net cases and 3 failing source-linked cases, including the
cold-zero guest journey and both Screw/Unscrew partial mesh failures. The same
expanded targeted suites then passed (62 Net / 42 source-linked cases) in
`green-targeted/`. No existing assertion was removed or reduced.

The source-linked fixture exercises discovery, native input hooks, real codec,
host authority and adapter, and absolute guest application. Counted FloatAdd and
FloatSubtract actions verify native direction/exactly-once execution for both
ordinary steps and clamps at 1/120. Replayed requests/results do not apply a
second step. Invalid source/actor/epoch/revision, contact, range and nonfinite
requests leave both fixtures unchanged. Existing competition, reentrancy,
rejoin high-water, native initialization/save-state and Clear tests remain.

Wire version 265, IDs 270/271, layout, registration and save/load policy are
unchanged: these are corrections to the already specified native step and
partial-failure guarantees, not new wire semantics. The only production edit
is in `SaunaTimerAuthority.Execute`; other HeatSource behavior is untouched.

Full-suite/build commands, exit codes, assertions, source/binary hashes and
selected installed-file protection hashes are in
`autonomous/rounds/000121-work/final/receipt.json` and its adjacent raw logs/TRX.
The round-local recorder requires a fresh output directory and does not launch
or deploy a game. No prior-round evidence was overwritten.

H08 remains **partial**. Steam/water, native runtime, ordinary keyboard/mouse,
different native saves, native rejoin/save-reload, Steam/two-PC and four-player
soak remain **NOT_TESTED**. Portable engine doubles and read-only Core compilation
do not establish those outcomes, including real Unity Euler-rounding behavior.

## Round 000128 — water/steam seam BLOCKED (not implemented)

The bounded water-transfer contract took its explicit discovery/red-regression
fallback. No water message, authority, native adapter or result was added, and
protocol remains 265. Timer, power, heat and native save/load production code are
unchanged. This is **not** an accepted guest water journey or blanket guest-denial
implementation.

The existing paths cannot safely be connected by renaming `StoveTrigger`:

- `HeatSourceSync.LocateSource` resolves direct `StoveTrigger::Steam`. The electric
  sauna's audited graph is `YARD/Building/SAUNA/Sauna/Kiuas/StoveTrigger::Steam`.
  `SaunaTimerAuthority.SteamPath` names it but timer binding never uses it.
- The archived round-000087 asset extraction supplies a global
  `GLOBALEVENT -> Check stove heat`; the local `STEAM -> Calc blur` transition
  follows a native heat comparison (>350). Steam subtracts 10 from
  `Simulation::Time.StoveHeat`, while the cold branch subtracts 2. `Water` is a
  Boolean reset in Idle, not a finite volume. The entry also drives local visual
  effects and stress, so blindly replaying its entire graph on peers is unsafe.
- Legacy messages 50/51 have no water resource identity, finite amount, epoch,
  revision or accepted debit. Host session routing authenticates `PlayerId`, but
  the legacy authority only checks source, nearby pose and a wrapping sequence
  before sending `STEAM`. Timer messages 270/271 cannot represent water. Fuel
  messages 267/268 bind gasoline to Sorbet, not a sauna destination. No wire
  semantics were repurposed.
- Catalog `EQUIPMENTS/water bucket(itemx)/Water::Level` exposes Level/Scale/Water
  floats; `EQUIPMENTS/water bucket(itemx)/BucketTrigger::Level` has Trigger,
  Compare, Tap and Lake states. `EQUIPMENTS/dipper(itemx)` only exposes Save in
  this dump. `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand::PickUp`
  has a `Sauna dipper` state. Their action operands and global transitions are
  absent, and the archived sauna-only asset extract does not fill those gaps.
  Variable names alone establish neither units/capacity nor a debit/contact chain.

`tools/h08_water_seam_audit.py` takes explicit catalog/asset inputs, preserves
missing operands, hashes input/source files and writes fresh-only output. It
never launches a game or certifies readiness. Its parser tests use catalog-only
inputs and must not be mistaken for native-resource validation.

`SaunaTests.Water.cs` source-links the actual Core discovery/hook/legacy accept
methods with counted engine doubles. Discovery passes. Three unskipped red
regressions remain: nested Steam boundary injection changes guest heat 22 -> 12
before acceptance; it emits no explicit request; a direct-trigger decoy is
accepted without water proof through real codec/policy/legacy authority. The
request-emission test is a preparatory missing-hook witness, **not** a valid
admitted finite transfer: the future fixture still needs audited water admission
and resource/contact setup. These doubles do not execute full FSM transitions,
physical dipper input or a real debit, and cannot prove host/guest convergence.

Fresh commands/TRX/hashes are in `autonomous/rounds/000128-work/`. Full Net passes
5,446 cases; the full source-linked suite has 73 passes and those 3 failures.
Core net35 builds with deployment disabled. The handoff/final receipt records
Python results, input/binary hashes and protection/cleanup limits. Existing tests
are unmodified; failed acceptance is not hidden behind the passing discovery.

Next bounded unblock: obtain action-parameter and component/object-reference
definitions for the hand's `Sauna dipper` activation, the activated held-tool
graph, bucket Water/Level writers, refill/empty/debit and the exact contact send
to nested Steam, including native initialization/save policy. Only then select
the native bounded quantity and build guest intent -> host validated debit ->
absolute result/rejoin tests. No invented dipper capacity or sidecar persistence.
Native runtime, ordinary input, native save/reload/different saves, Steam/two-PC
and four-player soak remain **NOT_TESTED**; H08 remains **partial**.

## Round 000146 — retained-input prerequisite audit (not implementation)

Fresh command receipts and the independent JSON report are in environment-root
`autonomous/rounds/000146-work/final-report/`; the unchanged existing auditor's
raw output is in `static-audit/`. No game/rig/protected-input access or Core build
was performed. Historical `000087-work/sauna-assets.json` is NOT fresh native
execution, and its embedded level hash is historical provenance only.

Independent parsing finds exactly one nested home Steam definition in each
input: asset `/fsms/3`, catalog `/fsms/8094`. All six states, 21 actions, default
values and operands are retained, plus the surrounding heat/power/timer/save
graphs. `Steam.Water` is a Boolean reset to false in Idle; its external writer,
source resource, debit target/amount, units/capacity and actor binding remain
UNKNOWN. `GLOBALEVENT` is the global entry; `STEAM` is only a local heat-comparison
transition. The literal 10/2 decrements target `Simulation::Time.StoveHeat`, not
water. The comparison's equality event is empty; no runtime equality behavior
is claimed. Visual, stress and achievement actions are not a shared debit result.

Static SAVEGAME/load operands identify `SaunaTimeKnob` (Timer), `SaunaPowerKnob`
(Rot), `SaunaHeatSauna` (SaunaHeat) and `SaunaHeatStove` (StoveHeat), in
`savefile.txt`. The water/dipper resource save operands remain UNKNOWN; Save
state names alone do not establish water persistence. No sidecar is proposed.
Legacy byte heat replication also cannot preserve the retained >350 hot-branch
range; attaching the nested graph to the existing legacy event is not a fix.

Unchanged parser tests pass four cases. Unchanged source-linked water tests
reproduce one discovery pass and three failures: guest heat changes 22 -> 12,
no explicit outgoing request, and generic direct-trigger decoy acceptance.
These are net8 engine-double fixtures, not native injected-state tests or an
admitted finite-water transfer. No production code, protocol or tests changed.
H08 remains **partial**; host/guest native action/result, ordinary input,
different-save, native rejoin/save-reload, Steam/two-PC and soak remain unproved.

Smallest next prerequisite: obtain the action/component reference selected by
`Hand::PickUp`'s `Sauna dipper` activation, follow that held-tool graph through
refill/empty/contact to the exact nested `GLOBALEVENT` sender, and identify the
actual resource writer/debit and its initialization/save hooks. Include the
referenced bucket Water/Level and BucketTrigger operands, not an assumed dipper
capacity. Only then define a native host-executed action for either player and
its absolute result/rejoin contract. A later task must explicitly authorize any
new asset extraction or native work; this round stops at the missing seam.

## Round 000148 — exact Hand activation boundary remains BLOCKED

Fresh retained-input/source evidence is in environment-root
`autonomous/rounds/000148-work/`: `final-report.md`, `retained-trace/witnesses.json`,
`operands/operands.json`, and command/verification receipts. No production code,
protocol, tool or test changed. No game launch, rig/protected-input access or new
asset extraction occurred; the following is **non-native** evidence.

The exact first missing edge is catalog `/fsms/7919/states/20`: Hand::PickUp's
`Sauna dipper`. `Item picked --EQUIP--> Check item --SAUNADIPPER--> Sauna dipper
--FINISHED--> Hand --FINISHED--> Set pivot 2 --FINISHED--> Item picked` is retained.
Neither `Check item` nor `Sauna dipper` has action types, action operands or an
activation target. Item/ItemPivot/PickedObject/RaycastHitObject and Joint are
variable **names**, not resolved references. The whole toolsVersion 0.1.0 dump
has 8,615 FSMs / 53,463 states, zero action payloads, no start states, variable
defaults or global-transition definitions. A name-only repeat audit cannot
recover the held-tool graph. Current FsmDumperPlugin intentionally writes action
type names rather than operands; merely using its newer version is insufficient.

The lossless inventory preserves duplicate records rather than silently merging
identities: dipper Save `/fsms/3307,8082`, bucket Water::Level `/fsms/2274,2277`,
BucketTrigger::Level `/fsms/5920,7485`, and bucket Save `/fsms/5601`. Water's sole
`State 1` has no transitions; Level/Scale/Water are floats without values/writers.
BucketTrigger retains Trigger/Compare/Wait/Tap/Lake transitions and variable names
Pos/Name/Collider, but no collider/tag/comparison/refill/debit operands. Save FSMs
list SAVEGAME, UniqueTag and This, but omit its global target, save actions, key
values and load/default data. Dipper Loaded is Boolean, not an established fill
flag. Rigidbody mass/kinematic/netId metadata cannot establish a water quantity,
object ownership, held-tool identity or distinct runtime instances.

The historical sauna extract contains no Hand/dipper/bucket definition. Its only
literal GLOBALEVENT is nested Steam's **receiver** global transition, not a sender.
All seven FSMs, 154 actions, defaults, 18 transforms and serialized PPtr operands
are retained. Steam's 10/2 subtract actions address Simulation::Time.StoveHeat
(m_FileID=0, m_PathID=16617 with a retained scenePath), not water. Component IDs
133327 (vignette) and 88014 (particle emitter) lack an ID-to-object table; the
extract cannot assign their runtime actor ownership. No generic STEAM shortcut,
synthetic water transfer or invented persistence is justified.

Fresh unchanged parser tests: four pass. Fresh unchanged source-linked water
fixture: one discovery pass, three failures (guest heat 22 -> 12, no request,
direct-trigger decoy accepted), zero skipped. These net8 engine doubles are not
native injected-state/ordinary-input or an admitted finite-water journey.
H08 remains **partial**. Native host/guest action/result and exactly-once debit,
ordinary input, different saves, native rejoin/save-reload, Steam/two-PC and
four-player soak remain unproved.

Smallest next prerequisite: a separately authorized, component-ID-resolved
action/operand extract beginning at Hand::PickUp `Check item` / `Sauna dipper`.
Resolve its actual activation target before following that target's components
to bucket refill/debit, contact and the nested GLOBALEVENT sender, including
initialization/SAVEGAME/load operands. Stop at any missing definition; do not
design gameplay/protocol until this reference closure exists.

## Round 000157 — fresh native extraction stopped at safety preflight

The assigned discovery run did **not** launch Unity or produce a catalog.
Fresh executable evidence is in environment-root
`autonomous/rounds/000157-work/preflight/` and `command-preflight/`.
The unchanged retained-only protected-input observer exited **1** at
2026-09-19 21:02:52 UTC: `gate_status=BLOCKED`, `native_launch_allowed=false`,
`future_isolated_launch_eligible=false`. Original-time byte/descriptor/chunk
provenance and independent attribution of the retained differing reads are
still missing. Stable current metadata is not original content equality.
No protected log bytes were read, no baseline was replaced, and the discovery
authorization was not treated as clearance of the inherited safety gate.

Consequently the current Hand::PickUp `Check item` / `Sauna dipper` ordered
action records, operands, activation target and component/object references
are **UNAVAILABLE**, not empty. The referenced dipper, bucket Water::Level,
BucketTrigger::Level and nested home Steam definitions were not captured;
duplicate-path cardinality and ambiguity are also unobserved. No historical
catalog or asset extract was substituted. Activation, water writer/debit,
contact sender, initialization and SAVEGAME/load fields all remain unresolved
by this run. No water capacity, units, debit, ownership or persistence is inferred.

Fresh unchanged tooling tests pass: seven Python parser/query tests and eight
net8 source-linked dumper tests with engine doubles. These exercise ordered
records, unknowns, malformed data, duplicate-path handling and fresh-output
protection; they are **not native extraction or gameplay evidence**. No game
binary build/deployment, rig lock/marker creation, save access, game/Steam
launch, input or multiplayer action occurred. No owned native process needed
cleanup; protected-content hash equality was not retested or claimed.

H08 and G_NATIVE_INVENTORY stay **partial**. Native gameplay, ordinary input,
different-save, native rejoin/save-reload, Steam/two-PC and four-player soak
remain **NOT_TESTED**. Next prerequisite is independently reviewed clearance
of the existing protected-input gate, not another same-host rehash. Only then
rerun the bounded copied-rig schema-4 extraction into a fresh assigned output.
