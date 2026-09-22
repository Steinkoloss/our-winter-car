# H07 fixed cabin woodstove fuel — production binding candidate

## Round 000085: portable integration slice (not native H07 acceptance)

The production cabin binding now has source-linked portable gameplay regressions
under `src/WinterMP.Net.Tests/WoodstoveGameplay/`. They compile the actual
`HeatSourceSync.cs`, `.Cabin.cs`, `.CabinNative.cs` and `FsmHook.cs`, exercising
discovery/binding, guest contact queue -> encoded intent -> authenticated-actor
authority entry -> counted action-adapter invocation -> deferred completion ->
encoded absolute result -> guest fuel/cache/heat/lit/visual/tombstone application.
The host is positioned away; the guest needs neither host nor vehicle ownership.
The host-local contact queue enters the same authority.

Failing-first tests exposed and fixed three integration defects:

- A Busy response to a duplicate discarded a previously confirmed Pending guest
  attempt. The client now retains that reservation through duplicate Busy/replay
  denials until the original final outcome, without speculative retry or loss of
  its matching acknowledgment. A Busy for an attempt that never became Pending
  remains terminal; NativeFailure also remains terminal, never success.
- Busy attempts during native execution/deferred retirement did not retain actor
  high-water, so replay could consume another newly available piece. Authenticated,
  current-epoch attempts now retain their sequence even while busy. Wrong actor
  and old epoch still cannot poison another actor's high-water.
- Result callbacks could reenter after the native reservation completed, spend a
  second piece and interleave another outcome. Core now guards decision and
  admission publication (including tick callbacks), remembers rejected attempts
  through the same authority, and releases the guard in `finally`. It does not
  recursively emit a response from a publication callback.

These are corrections to the already-defined v259 exactly-once/high-water/ack
contract; IDs 260/261, codecs, channel policy, snapshot semantics and current
protocol version 264 are unchanged. Sauna, fireplace, grill and cabin lighting
routes are unchanged. No fuel/log sidecar, save writer or cold-init replay exists
in this change.

Evidence boundaries: action classes, Unity-null destruction, physics notifications
and session transport are explicit counted doubles, not game execution. The
actual `CabinFuelHost` adapter runs against those doubles; the test does not merely
inject final fuel. Tests supply already-admitted resource identities instead of
running the native prefab/split/materialization factory. SessionManager/WorldSync
authentication forwarding, generic despawn protection and catalog exclusions are
separately checked against production source; the portable SessionManager double
does not prove real transport authentication. Main Net tests cover the full sender
and channel matrix, missing equipment, invalid observations, immutable snapshots,
rejoin/high-water and cold-epoch initialization. The native feed graph itself has
no axe prerequisite, so Core intentionally reports equipment-ready.

Exact argv/CWD/UTC/exit codes, failed-first and final raw logs/TRX, static assertion
witnesses, source/DLL hashes and cleanup are in
`autonomous/rounds/000085-work/` outside this tree. Run the separate source-linked
suite with `dotnet test src/WinterMP.Net.Tests/WoodstoveGameplay/WinterMP.WoodstoveGameplay.Tests.csproj -c Release -p:DeployToGame=false`;
the main Net test project excludes its engine doubles to avoid assembly collisions.

H07 remains partial. Native injected state, ordinary input, different saves,
native rejoin/save reload, Steam/two-PC and four-player soak remain NOT_TESTED.
No game launch/deployment was attempted; the protected-input provenance gate is
still BLOCKED. The next contract must name the remaining native journey: once
that gate is cleared, use a marked exclusively locked copied-save rig to exercise
actual guest release/contact of a host-assigned half-piece with the host away,
exactly-once native feed/retirement and peer results, host-local feed, invalid and
duplicate requests, late join/rejoin, and vanilla-control save/exit/cold reload.
Do not replace that journey with this portable fixture or invent fuel persistence.

## Round 000022 historical binding status

The portable seam is now bound in `HeatSourceSync.Cabin*.cs`, with authenticated
SessionManager/WorldSync routing, protocol v259 messages 260-261, and an
engine-independent production `WoodstoveFuelClient` admission/acknowledgment gate.
This is a portable/build-verified candidate, NOT native gameplay acceptance. H07
remains partial. The inherited installed-log discrepancy still prohibits native
launch/deployment; no game was launched or deployed in this round.

The fixed source uses WoodTrigger.Woods, actual Collider contact and a host-assigned
nonzero world epoch and resource ID. Both host contact and a connected authorized
guest request enter the same authority; legacy cabin WOOD/HeatSourceIntent feed
bypasses are rejected. The host native Destroy firewood action array is enabled
only during the one validated adapter invocation. Guest contact detection remains
native but its feed/depletion writers cannot consume speculatively. Guest Woods,
SetFire cache, heat/embers and loaded-log visuals use absolute results. Other heat
sources keep their old routes.

The host reserves the identified half-piece, checks the immediate canonical +1,
then checks actual Unity-null on a later frame before reporting success. A 2s
timeout, exception or partial effect is a sticky fault, not a success or retry.
Portable deferred-adapter doubles test this contract; they do NOT establish
Unity's actual runtime timing. Host depletion during retirement is observed rather
than restoring cached pre-burn fuel.

Host discovery is bounded to native split PART/firewood(Clone) pieces within 20m
of the cabin source, up to 256 identities per world. Mesh plus audited distinct
half-collider centers select the native prefab shape; no path or Unity instance
ID is sent as resource identity. An admitted guest binds an unambiguous existing
piece at its supplied pose or materializes the matching native prefab half with
split-statistics FSMs disabled. This materialization route, same-save duplicate
avoidance and real guest motion/contact remain runtime-unverified. Generic item
motion uses the assigned ID; generic despawn cannot bypass cabin retirement.

Admission/rejoin carries epoch, retained actor high-water, live piece descriptors
and nonresurrecting tombstones. Ordered absolute snapshots may overtake results
without losing matching acknowledgments or restoring old fuel. No fuel/log save
sidecar, global fuel persistence or cold-reload replay was added; native
NoRainCabin temperature saves and cold initialization remain untouched.

Evidence RUN: `autonomous/rounds/000022-work/` outside this source tree. Exact
commands/exit codes/raw logs, failed-first tests, static assertions and source/DLL
hashes are in the handoff and verification manifest. Native-injected,
ordinary-input, reversed native roles, native save/reload, Steam/two-PC and
four-player soak are all NOT_TESTED. The next native task, only after the
controller resolves the protected-input gate, must exercise the actual guest
release/contact -> host feed -> retirement/result route with the host away,
then host feed, invalid/duplicate requests, rejoin and a vanilla-control cold load.

## Historical seam and prerequisite audits

Round 000018 implements the contract's portable fallback, NOT working in-game feeding.
`HeatSourceSync` and SessionManager are unchanged. No new wire messages or catalog
entries are registered, and protocol version 258 is unchanged. The existing
speculative guest/identity-free feed path is still an open defect; it is not made
safe by this unbound seam. H07 remains partial.

## Round 000018 audit (historical/static evidence only)

The round-000020 read-only audit below supersedes the action-field unknowns in
this historical section. It does not change the unbound production status.

- `HeatSourceSync.cs:283` binds `CABIN/Cabin/woodstove/Fireplace` using its existing
  stable hash. `LocateSource` prefers `SetFire::Use.Woods`, falling back to
  `WoodTrigger::Trigger.Woods`; whether these are temporaries or the canonical
  loaded-wood count is not established by the variable-name-only dump.
- `catalog/dump-23268598.json` has `WoodTrigger`: `Wait wood -> Parent -> Check
  firewood -> State 1 -> Destroy firewood -> State 2/3/4/5`, local `Woods` int,
  `Wood` float, `Collider` and `Parent` object variables. `SetFire` has `Woods`,
  `BurnTime`, `HeatingEfficiency`, `Door`, `Hiillos`; `Burn -> Remove wood ->
  Check wood` is the native depletion path. The dump has no action parameter
  records or global transitions. Do not infer a stable resource ID, save key,
  one-unit native increment, or four-log capacity from these names alone.
- Existing guest feed hook prepends an intent callback to `State 1` but leaves
  native actions enabled. Host `TryAcceptIntent` fires `WOOD` without assigning
  the identified log to `Collider`/`Parent`, without epoch/resource validation,
  and without confirming consumption. Actor authentication is enforced in
  `SessionManager.Messages.cs:542`; per-source ushort sequence latches are cleared
  on rejoin. Heating is a separate reliable periodic stream forced on join, not
  an ordered resource snapshot.
- Host state clamps fuel/heat to bytes. Guest application sets only embers and
  Woods for non-sauna; it does not replicate `HeatingEfficiency`. The new seam
  preserves observed float heat verbatim and does not extrapolate temperature.
- Read-only historical `build/firewood-delivery-audit/native-wood-prefabs.json`
  in the original checkout shows `log/log(Clone)::Check joint` renaming both
  pieces to `firewood(Clone)`, then changing parents/tags. This makes a clone
  scene-path hash unsafe as the identity rule. `ItemWorldSync.Scan.cs:64-103`
  groups clones by initial position and skips later collisions; no specific
  firewood identity factory was found in ItemWorldSync. Do not reuse this
  fallback as proof of stable log identity on rejoin.
- GuestSaveProbe conventions require explicit sandbox opt-in, markers, copied
  saves, exclusive rig ownership and scoped cleanup. No new native fixture was
  deployed or run. Native launches remain prohibited by the unresolved
  round-000016 installed `LogOutput-guest.log` discrepancy.

## Implemented seam and evidence limits

`src/WinterMP.Net/Sync/WoodstoveFuel*.cs` implements one-source game-thread
intent/decision/snapshot models and a finite-resource authority. It authenticates
the supplied actor, locks the nonzero world epoch, retains nonwrapping uint
attempt high-water marks across rejoin, rejects replay/wrong source/missing or
already consumed resource, and checks live host observations (actor alive/fresh,
range, source ready/capacity, actual firewood, resource access, equipment, contact).
No observation is accepted from request payload fields. Authentication itself
still has to be supplied by the future SessionManager binding.

After validation it invokes the host adapter once. A reentrancy reservation and
sticky fault stop retrying partial native effects. Acceptance requires exactly
one added unit of loaded wood and confirmation that the specific resource was
consumed. Source heat/lit values come from the adapter, not an invented heating
formula. Adapter exceptions or invalid/nonfinite post-state yield no success
snapshot. Reads for normal snapshot capture observe vanilla burn/initialization,
never restore earlier fuel. Actor high-water is available through `Seen(actor)`
for a future admission message; the current snapshot is not a wire admission.

The portable replica accepts only authenticated-host snapshots for its known
source/epoch, applies monotonically revisioned absolute values, rejects conflicting
equal revisions, and never allows resource tombstones to disappear. Snapshots
clone their arrays. Sending an intent has no replica mutation path. These classes
neither grant ownership nor write saves. There is no persistence sidecar.

The 3m actor-radius/2s pose-age checks are conservative seam policy limits, NOT
measured vanilla trigger geometry. `EquipmentReady`, `ResourceAvailableToActor`
and contact are host-adapter facts; holding versus releasing the log and exact
native requirements remain unaudited. The one-unit increment and synchronous
adapter contract must be confirmed before binding. Portable tests use explicit
adapter doubles and are not native/physical/Steam evidence.

Round evidence: `autonomous/rounds/000018-work/` (outside this source tree).
Initial tests failed to compile because the seam was absent (`red-net.log`).
A subsequent real behavioral regression failed because a non-firewood resource
was accepted (`red-resource-kind.log`, Expected InvalidResource / Actual Accepted);
the explicit host-observed firewood-kind guard fixes it. See final verification
receipts and handoff for exact commands/counts/hashes.

## Round 000018 continuation (historical; updated contract below)

1. Keep the protected-input gate FAILED and native launch prohibition intact until
   the controller resolves the existing installed-log provenance discrepancy.
   Do not rebaseline/exclude it. Separately recover read-only static extraction:
   current Python environments lack UnityPy; offline installation was blocked
   by incomplete package threat-intelligence checks, not a native game failure.
2. Extract only cabin `WoodTrigger`, `SetFire`, native log-splitting/creation and
   their save/load action fields. Identify which log object is consumed, how
   `Woods` is read/written, true capacity/contact/release/equipment prerequisites,
   and native cold-load persistence. Preserve raw asset hashes. Narrow the native
   adapter to the verified action signature; no guessed `WOOD` replay.
3. Bind one stable host-assigned firewood identity and its retired lifetime through
   join/rejoin. Do not expand this to every item, logging delivery, sauna or grill.
   Replace only the cabin guest feed mutation entry with a suppress-and-request
   path, retaining native input detection; host and guest requests must both work.
   Confirm the guest's burn/progression writers cannot race the absolute results.
4. Wire the seam into HeatSourceSync/session routing with authenticated intent,
   epoch/high-water admission, absolute result and resource retirement snapshot.
   Disable the legacy cabin feed bypass only atomically with that working route,
   not as blanket guest denial. Wire changes must bump protocol and document
   codec/registry/channel/sender/catalog changes together; preserve other sources.
5. Run actual-adapter portable tests where feasible, then (only after the gate is
   cleared) a marked bounded native fixture exercising one accepted guest feed,
   competing log use, duplicates, old epoch, no resource, range, late join and
   native save/reload. Capture the implementation request/result path, not final
   value injection. Physical input, reversed native roles, real Steam/two-PC and
   four-player soak remain separate open gates even if that fixture passes.

## Round 000020: recovered read-only installed-asset audit

Evidence RUN: `/home/jaimep/our-winter-car-agent-env/autonomous/rounds/000020-work/`.
No game launch or deployment occurred. No protocol, catalog, gameplay source,
controller or safety file was changed. The installed `LogOutput-guest.log`
discrepancy remains unresolved; within-run unchanged hashes are NOT a replacement
baseline and do NOT reopen native testing.

### Route, provenance and evidence levels

The initial default-interpreter extraction reproduced `ModuleNotFoundError:
No module named 'UnityPy'` (exit 1). Normal `uv venv` and `uv pip install
--offline UnityPy==1.25.3 TypeTreeGeneratorAPI==0.0.10` into this RUN then succeeded
(exit 0), without policy changes or alternate downloads. The existing, unchanged
`tools/extract_fsm_assets.py` read the installed assets; no game code was executed.
The isolated venv is disposable, not a retained project dependency.

- Fresh static: `cabin-installed-assets.json` (10 FSMs),
  `cabin-expanded-assets.json` (31 cabin FSMs, 198 transform records),
  `log-installed-assets.json` (one prefab FSM), and `asset-objects.json`
  (serialized component fields and external references). These contain asset
  defaults, NOT loaded save values or runtime contact results.
- Installed `level2` SHA-256:
  `36795e9354d7233fe68fe11822e4c139872db1ba46cfab3833fb7b985613be5d`.
  `sharedassets3.assets` SHA-256:
  `4212b819e589e084c9fb0b26a6790e3760a27433d56c696e231aaf607b976b43`.
- Static IL: the installed `Assembly-CSharp.dll` action implementations and
  `PlayMaker.dll` TriggerType enum were read with the existing local ilspycmd.
  `trigger-action-il.log`, `destroy-action-il.log`, `get-parent-il.log`,
  `parent-compare-il.log`, `trigger-enum-il.log` contain the results. The first
  two invocations printed an automatic update notice; subsequent calls use the
  documented `--disable-updatecheck`. No tool update was installed. Do not label
  those first invocations as verified network-isolated execution.
- Historical static: original `build/firewood-delivery-audit/native-wood-prefabs.json`
  matches the freshly extracted prefab FSM. The filename's word `native` does not
  make it a runtime fixture. Historical catalog `dump-23268598.json` supplies
  runtime-discovery names/states but lacks action parameters and global transitions.
- Historical fixture: the original firewood-delivery audit concerns a supplied
  trailer load, unloading and payment; its summary explicitly excludes guest
  woodcutting/loose-wood loading. It is NOT cabin-feeding evidence.
- Current runtime: host/guest feeding, contact, destruction completion, rejoin,
  cold reload, ordinary input, reversed roles, Steam/two-PC and soak are all
  NOT TESTED. No runtime pass is inferred from static extraction or portable tests.

`*.command.json` records exact argv, CWD, exit code, timestamps and raw output
SHA-256. `static-assertions.json` records 20 passing assertions on actual extracts,
with witnesses; these are static checks, not 20 gameplay tests. Unsupported
parameter shapes remain explicit `rawHex`/`dataPosition` in the raw extraction;
in particular, do not treat an unsupported array as proof of an empty layer mask.

### Audited feed and depletion boundary

All paths below are under `CABIN/Cabin/woodstove/Fireplace` unless stated otherwise.
Action indices are zero-based and refer to the raw extracted state action arrays.

| Native location | Verified static fields / meaning | Limit for the binding |
|---|---|---|
| `WoodTrigger::Trigger/Wait wood`, actions 3/4 | `TriggerEvent.trigger` 0/1 = OnTriggerEnter/OnTriggerStay; `collideTag=PART`, `storeCollider=Collider`, `sendEvent=WOOD`. IL stores `other.gameObject`, not its rigidbody root. | Actual host-observed collider object is the consumption identity. A bare WOOD event without setting/validating this object is unsafe. |
| `Parent`, actions 0/1 | GetParent reads Collider into Parent; GameObjectCompare compares Parent to null, equal -> WOOD -> Check firewood, not equal -> FINISHED -> Wait. | Require the native unparented condition. Holding/releasing through the real player hand is NOT TESTED; do not equate network ownership with Transform parenting. |
| `Check firewood`, actions 0/1 | GetName(Collider) -> Name; exact comparison with `firewood(Clone)` -> WOOD -> State 1. | Tag/name are vanilla predicates, not a network identity or sufficient security proof. |
| `State 1`, action 0 | IntCompare local Woods against 4; less -> WOOD -> Destroy firewood; equal -> FINISHED -> Wait; greater event is empty. | Capacity is four in the graph. Reject invalid negative/over-capacity observations rather than allowing a stuck transition. This is not measured runtime capacity. |
| `Destroy firewood`, actions 0..8 | Choose/enable sound, activate SetFire; action 3 DestroyObject(Collider, delay 0, detachChildren false); action 4 IntAdd(local Woods, +1, everyFrame false); comparisons dispatch loaded-log visuals for 1..4. | Canonical immediate feed counter is **WoodTrigger::Woods**. Retire that exact resource, not Parent, an arbitrary nearby item or all firewood clones. |
| `SetFire::Use/Burn`, actions 2..4 | GetFsmInt reads WoodTrigger::Trigger.Woods into local Woods every frame; AddFsmFloat adds HeatingEfficiency to NoRainCabin::Data.Temp per second; Wait uses BurnTime. | SetFire::Woods is a read cache, not the immediate feed result; guest cache/burn writers must not undo absolute results. |
| `SetFire::Use/Remove wood`, actions 0/1 | IntAdd(local Woods,-1), then SetFsmInt writes it back to WoodTrigger::Trigger.Woods. | Host owns depletion too; a periodic scalar correction alone does not suppress guest burns. |

The enabled serialized WoodTrigger BoxCollider has local size approximately
`(0.35, 0.4, 0.4)`, center `(0,0,0)` and `isTrigger=true` (object ID 112465).
Raw transforms/scales are retained; this is not a live world-space contact or an
actor-distance measurement. The portable seam's 3m/2s limits remain mod policy,
not vanilla trigger geometry. No axe/equipment check occurs in the feed graph.

`DestroyObject.OnEnter` calls `UnityEngine.Object.Destroy(value)` for delay <= 0,
then finishes the action; it does not use DestroyImmediate or await destruction.
The seam's immediate `IsConsumed` postcondition is therefore NOT confirmed by
this audit. Reserve before mutation and validate retirement at a demonstrated
native completion boundary; never replay a partially executed feed. Whether a
pending/final result split is necessary must be resolved with actual runtime
evidence, not by redefining a test double's success as native destruction.

Lighting is distinct from feeding: SetFire starts at State 1 and reads the hatch
Handle::Use.DoorOpen. Wait button reads the `Use` button; Start fire 2 waits one
real-time second before Burn. SetFire's asset BurnTime is 150; hatch Open door
writes 100. Its HeatingEfficiency asset default is about 0.18. These are not
loaded values or a new heating formula. No feed-graph door check was found;
physical hatch obstruction and real release/contact remain NOT TESTED.

### Native log creation and identity

`CABIN/LOD/Logging/Logwall::Use/Check ax` reads the player Hand::PickUp.AxInHand;
`Check log` waits on NoLog. This is a **creation** prerequisite, not a stove-feed
requirement. Create log action 3 references `(m_FileID=2,m_PathID=9225)`; level2's
external table resolves file 2 to sharedassets3.assets, whose GameObject 9225 is
`log`. It stores the instance in Log, parents it to `Pölkky/Spawn`, and randomizes
rotation. Do not confuse these asset IDs with stable multiplayer instance IDs.

The prefab has two physical pieces with Rigidbody/BoxCollider, and a FixedJoint
on `log/log(Clone)` connected to the root rigidbody. Its Check joint FSM waits
for the absence of FixedJoint, then updates LogsChopped/statistics, renames root
and self `firewood(Clone)`, sets both parents to null, tags both PART, and destroys
its own PlayMakerFSM component. It also applies native stress/sound effects.
The scene logging sample has the same split actions with scene-local references.

Thus a path/name hash cannot distinguish successive split pieces or survive the
rename/reparent operation reliably. Assign an epoch-scoped host resource ID to
the selected actual piece and carry that mapping through replication and
retirement. The consumed Collider is one piece, not automatically the whole
unsplit log. Current source has no proven cabin-specific resource factory.

### Vanilla save/load: known fields versus UNKNOWN outcomes

- WoodTrigger and SetFire local Woods asset defaults are both zero. SetFire is
  asset-inactive; WoodTrigger starts Wait wood. Neither target FSM nor the log
  prefab Check joint FSM contains a Save/Load/ES2 action or SAVEGAME transition.
  This is scoped absence, **not proof that a global/external saver never exists**.
- `CABIN/NoRainCabin::Data` has a real, separate temperature persistence graph:
  Exists uses GetOwner -> This and GetName(This) -> UT, then Exists(UT,
  SavePlayerData); Load uses LoadFloat -> Temp; SAVEGAME -> State 2 calls
  SaveFloat(Temp, UT, SavePlayerData). UT derives from the owner name
  `NoRainCabin`; SavePlayerData's asset default is `savefile.txt`. Set temp clamps
  Temp between AmbientTemperature and 29. Personal saves were read only for
  integrity hashes, not parsed or loaded; current runtime resolution remains
  NOT TESTED.
- Cabin ownership saves (`VenttiCabinPLR`/HasCabin) are not fuel persistence.
  No loose-firewood save identity/key was established. Fuel, lit/embers and loose
  resources across cold reload remain UNKNOWN/NOT TESTED. Preserve native
  initialization; do not introduce a fuel/log sidecar or force previous-session
  tombstones across a cold-loaded world.

### Smallest subsequent binding contract (not dispatched)

1. Scope exactly one host-created/split firewood piece and the fixed cabin source.
   Keep wider logging jobs, sauna, grills and generic-item coverage out. Keep the
   protected-input launch gate intact; only the controller can resolve its cause.
2. Before binding, establish actual deferred retirement timing, hand release to
   null parent, authoritative contact while host is away, and native save/reload
   behavior in a permitted copied-save rig. Use the audited Collider and
   WoodTrigger.Woods fields. If runtime cannot run, keep the binding explicit WIP;
   no synchronous adapter or persistence behavior is yet approved by this audit.
3. Add a bounded host-assigned nonzero epoch/resource identity and per-piece
   retirement record, not a mutable path or Unity instance ID as wire identity.
   Admit the same identity, live pose and retired state to joining/rejoining
   guests. Preserve exclusive interaction rights through release/contact without
   requiring the distant host's local hand, axe or camera to perform guest input.
4. Route authenticated guest **and host** intents through one authority entry.
   Requests identify source/epoch/sequence/resource; host observations supply
   actual type, capacity, actor/access/contact and native state. Guard the native
   mutation boundary before Destroy firewood for both local contact and remote
   intent so the same resource cannot be consumed twice or speculatively on a
   guest. Replace the legacy cabin State 1 callback/WOOD bypass only atomically
   with a positive guest route; blanket guest denial is a failure.
5. Wire source discovery + adapter in a cabin-specific HeatSourceSync partial,
   its native resource lifecycle/guest suppression, and SessionManager's
   authenticated request/result/admission/snapshot paths. Keep other heat sources
   unchanged. Admission carries epoch and retained actor high-water; final result
   matches the attempt and carries absolute revisioned fuel/heat/lit/resource
   state; join/rejoin includes live identities and nonresurrecting tombstones.
   Prevent SetFire burn/cache and legacy HeatSourceState from racing the new
   cabin result stream. Codec/registry/sender/channel/catalog docs and protocol
   bump must land atomically when semantics are introduced; none change here.
6. Tests first: positive host and authorized guest feed; one native mutation and
   one matching final result; duplicate/competing attempts; wrong actor/source/
   epoch/resource, parented/wrong-tag/non-firewood, missing contact/range, full
   capacity; deferred/partial destruction failure without retry; SetFire cache
   lag and host depletion; snapshot overtaking, reconnect high-water, tombstones
   and a live identified piece at late join. Preserve current portable seam tests
   and adapt its timing only with evidence-backed tests, never weakened success.
7. Once native launches are allowed, a bounded two-peer actual-path fixture must
   feed that identified piece via the guest while the host is away, compare
   canonical Woods, peer visuals/heat and retirement, test the same host action,
   replay/invalids, rejoin, and perform vanilla host save -> exit -> cold reload ->
   guest rejoin. Observe cold-load fuel/lit/room Temp and unconsumed/consumed loose
   pieces against a vanilla control; preserve resets as well as saved fields.
   No final-value injection counts as replication. Physical input, reversed
   native roles, Steam/two-PC and four-player winter soak remain separate gates.
