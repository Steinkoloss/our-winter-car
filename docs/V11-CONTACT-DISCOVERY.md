# V11 contact/frost discovery gate

Scope is one parked Corris windshield: `CORRIS/BODY/Windshield/collider::Scrape`,
`CORRIS/Simulation/CarTempCorris::Freezing.CutoffWindshield`, material `6`/`_Cutoff`.
This is a portable/source-linked audit with a read-only discovery gate. V11 remains
partial. No sidecar frost save, alternate pane/vehicle behavior, protocol or
runtime binding is added.

## Fresh portable parked non-owner bridge receipt (000134)

`tools/v11_bridge_readiness.py` executes the existing production-linked
WrongPane regression in a fresh bounded .NET test process, then validates its
structured observations. Use the actual assigned contract RUN, not a historical
round. Every attempt must use a new leaf:

    python3 -B tools/v11_bridge_readiness.py --run "$RUN" --name bridge-readiness-new

The runner refuses aliases, missing contracts and existing output leaves. It
retains exact argv/cwd/UTC/exit, raw dotnet output and TRX, a fresh per-process
token, before/after source hashes, binary hashes and cleanup in that leaf.
`command.json` describes the inner `dotnet test` invocation; `report.json`
independently separates `PASS_FIXTURE_ONLY` from native `NOT_READY`. Exit 0
means only the portable contract was verified; it is never launch permission.
The runner accepts no old TRX or caller-supplied readiness claim as execution.
Its parser remains reusable for independent verification of retained artifacts.

Before pickup, before the fresh WrongPane request and before recovery it requires
all eight matrix rows (absent/null/type-invalid fields are NOT_READY):

* Authenticated actor mapping in the explicit session fixture (host 0, guest 2),
  host authority role and matching host-observed guest identity.
* Alive guest, matching local/observed feet, positive pose timestamp, age 0..0.6s.
* Host away from both guest and car (>10m for this scenario), no held object and
  no host scraper lease. These are scenario controls, not new runtime rules.
* Matching shared Corris identity and bound bridge ID on both peers, vehicle
  flags and rigidbody names; no ID inferred from retained catalog FSM IDs.
* Both local-ownership flags false and both remote owners 255.
* Guest move-state outside, no passenger seat, no host trigger containment,
  local PlayerInside false. Seat/Bounds queries here are explicit doubles.
* Exactly zero linear AND angular motion on both peers, not a small-speed proxy.
* Matching shared ice-scraper ID and body name, separate from the camera prop.

Neither `pane-park` success nor a fresh pose alone satisfies this matrix. The
portable test's existing injected host position/tool selection/physics hit and
fresh timestamp are explicitly fixture inputs, not observed native operands.
The C# trace reads current fixture objects and production-generated decisions;
it never sets a final result to make the audit pass. Existing regression
assertions are retained, including one-byte pane mutation, frost/other-pane
noninterference, no guest additive action, actor-only effects and reset routing.

Fresh WrongPane consumes the sequence but preserves pane/revision/effects;
corrected replay is denied, the next fresh guest hook stroke is accepted by the
host once, and its duplicate is denied. Each phase records the absolute
host/guest cutoff/material, lease/guest sequence, host decision counts, revision
and counted actions/effects. A new token, passed exact test, complete ordered
matrices/decisions and cleanup are required; stale, omitted or contradictory
observations cannot become positive evidence.

This does not fix or certify historical native `bridge()` prerequisites. The
retained native operand checklist remains entirely NOT_SUPPLIED. Protected-input
provenance remains BLOCKED, without accessing protected content or overriding
the gate. Native/ordinary-input/different-save/native rejoin/save-reload,
Steam/two-PC and four-player soak remain NOT_TESTED. V11 remains partial.
Next native requirement: independent protected-input provenance clearance,
then a separate readiness-only run with genuine guest resume/spawn and fresh
host-observed alive pose before contact/action acceptance. There is no new
persistence mechanism or inferred vanilla save result.

## Round 000132: executable nested-route readiness checklist

The existing `tools/v11_contact_discovery.py` now emits schema 2 with a separate
`discovery.json.readiness` section. `tools/v11_readiness_requirements.py` traces
exact retained FSM -> state -> transition objects and rigidbody records, retaining
JSON pointers, raw records, source lines and input hashes. It does not launch the
game, interpret portable doubles as native objects, or change any production,
protocol, catalog, authority or persistence behavior. V11 remains **partial**.

From `autonomous/source`, use the actual assigned existing contract RUN and new
leaf names on every attempt:

    export RUN=/home/jaimep/our-winter-car-agent-env/autonomous/rounds/000132-work
    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name provenance-command-new -- python3 -B tools/protected_input_provenance.py --run "$RUN" --name provenance-new --retained-only
    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name discovery-command-new -- python3 -B tools/v11_contact_discovery.py --run "$RUN" --name discovery-new --provenance provenance-new
    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name replay-command-new -- python3 -B tools/v11_contact_discovery.py --run "$RUN" --name discovery-new --verify
    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name python-command-new -- python3 -B -m unittest discover -s tools/tests -p 'test_v11*py' -v

Completed unresolved discovery/provenance exits **1**, not native success. A
malformed/drifted audit exits **2**; offline replay exits **0** only after checking
retained hashes and reconstructing the report. Exclusive leaf creation preserves
old attempts. Schema-1 historical receipts retain their original source for
replay; the schema-2 verifier requires the expanded input manifest. Do not rerun
the historical native `audit`, `static`, `prepare` or `bridge` modes to bypass the
provenance blocker. The retained-only observer opens installed-log metadata with
zero content bytes; discovery itself reads only source and same-RUN evidence.

### Concrete nested paths and unresolved operands

The June Tools 0.1.0 GAME catalog declares these paths; it is not a fresh native
dump and has no action fields, global-transition metadata or full object tree:

* Pane: `CORRIS/BODY/Windshield/collider::Scrape` (FSM ID `3720794055`). Nested
  `Get scroll --DOWN--> Get this glass --FINISHED--> Scrape 1 --SCRAPE--> Scrape 2`
  alternates back to Scrape 1. Its rigidbody ID `2587790521` is a different
  namespace, not the live vehicle/shared item ID. Parent path components do not
  prove parent GameObject existence. `Windows`, `This`, `InsideTrigger`,
  `Distance`, `X`/`Xold` and actual MousePickEvent camera/layer operands are missing.
* Hand: `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand::PickUp`.
  `Set pivot 2 -> Item picked --EQUIP--> Check item --ICESCRAPER--> Ice Scraper`
  then `Off -> Hand -> Set pivot 2`. Source binds the separate eye path
  `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Camera/Camera`. Neither path
  proves a current shared tool, held-camera prop identity or native first hit.
* Tool: `Spawner/CreateItems::IceScraper` declares `Prefab`, `New`, `SaveID`,
  `ObjectNumberInt`, SPAWNITEM/SAVEGAME and Load/Save states. The dump also has
  `icescraper0::Use` (FSM ID `2237998909`) with Owner/ID/Consumed/Loaded and
  Save/Load declarations, plus inactive `icescraper0` rigidbody `290171502`.
  These do **not** establish the live registry identity `ice scraper(itemx)`,
  exclusive pickup/equip lease or the fields persisted by vanilla.
* Glass result: `CORRIS/Simulation/CarTempCorris::Freezing` State 7 declares
  FINISHED -> Sound -> Update, with CutoffWindshield/material 6 separate from
  `GlassFrosting.Frost/FrostGlass`. Production intentionally executes only the
  validated glass actions on host and actor-local effects after acceptance; the
  declared transition is not permission to replay host Sound for a guest.

Seven independent `NOT_READY` rows name required artifacts for native objects,
contact, tool lease, host-away guest readiness, accepted action, absolute result
and vanilla save/reload. Each retains source anchors and missing native capture
fields. The report never turns absent action/save metadata into proof of absence:
windshield persistence and exact tool save fields stay **UNKNOWN**. Production
Capture still observes vanilla cutoff; reset clears in-memory state, without a
sidecar or saved-stroke replay.

### Driver and evidence boundaries

`pane-contact` uses collider.Raycast on the selected collider, not production
scene-wide first-hit Physics.Raycast. Aim commands teleport/hold the camera;
pickup assigns PickedObject and enters Set pivot 2; stroke enters native states.
These are injected-entry fixtures, not ordinary input. The current positive
`bridge()` does not assert all host-away/both-non-owner/linear-and-angular-speed
preconditions. Both `pane-park` calls put players near the car. The old `audit()`
guest rollback is not positive multiplayer acceptance. Native replay repeats a
used sequence; the fresh WrongPane/replay/recovery mutation remains portable only.

Portable execution is deliberately `NOT_TESTED` **by the read-only discovery
tool**. Companion receipt/TRX commands independently test the real source-linked
bridge, including the unchanged WrongPane regression; a green fixture uses
engine/session/physics doubles, not live contact. Round 000132 handoff and
verification.json retain actual test/build commands, exits, counts and hashes.
New parser red/green checks cover missing readiness output, changed/duplicate/
missing/malformed nested edges, unavailable tool save nodes, untrusted action
objects and source-seam drift. No test expectation or production capability is
weakened to obtain a green result.

Protected-input provenance still lacks independently retained original-time log
bytes/custody/identity or independently attributable reconciliation of historical
same-metadata digest drift. The same-RUN assessment names the exact protected
target, pinned baseline and raw receipts. It is not launch authorization. Native,
ordinary-input, different-save, late-join/rejoin/save-reload, Steam/two-PC and
four-player-soak gates remain **NOT_TESTED/BLOCKED**. No rig lock/marker/deployment
or game process is created by discovery; compiler-only copied DLL reads are
recorded separately. The next native prerequisite is independent provenance
clearance, then a separately authorized readiness-only run proving real guest
resume/spawn and authenticated host-observed pose before any contact journey.

## Round 000125: wrong-Pane portable rejection and recovery

Evidence: `autonomous/rounds/000125-work`. This closes only the portable outgoing
mutation/regression portion of the next contract below. Native probe/driver code
is unchanged, no native launch is authorized, and V11 stays **partial**.

`tools/PaneScrapeBridge.Tests/GuestStrokeWire.cs` attaches to the existing session
double's outgoing `SendWorldMessage` observer. It captures the genuinely emitted
production request through `PacketCodec`, clones it, and changes **only Pane to
2** on the next local guest Stroke after arming. A KeepAlive does not consume the
one-shot; fresh sequence, actor, epoch, vehicle, tool, eye/direction and the
original message object remain unchanged. Disposal or capture failure disarms
and detaches this fixture-only observer. It is not compiled into Core or release
payloads and cannot seed a lease, result or final glass value.

`BridgeTests.WrongPane.cs` enters the production pickup/equip/stroke hooks and
routes captured packets through the production codec, lease, authority and
absolute result/replica path. It explicitly asserts stationary CORRIS, non-owner
guest 2 on both peers (`LocallyOwned=false`, `RemoteOwner=255`), host 0 away at
`(100,0,0)` with no tool, and a fresh/alive/outside guest at `(0,0,0)`. Physics,
engine actions and sessions are counted doubles, **not native contact/input**.
The only engine-double extension allows an explicit local pose rather than an
always-zero pose; production code, catalog and protocol are unchanged.

The test-first transparent capture failed exactly `expected WrongPane, actual
Accepted` (`wrong-pane-red`, exit 1); this demonstrated missing test-tool mutation,
not a production authority bug. The one-shot implementation passes:

* Genuine valid guest Stroke sequence 4: Accepted, one host glass/material write,
  absolute `.255` on both, one guest-only effect.
* KeepAlive sequence 5 passes unchanged while armed. Next emitted Stroke sequence
  6 differs by exactly one Pane byte: **WrongPane**, high-water 6, no glass/material
  write, no cutoff/revision/result-value/effect advance. Lease holder/equipped/age
  stay unchanged; only the specified sequence ledger/decision metadata advance.
* Correcting only Pane on the same sequence 6: **ReplayedSequence**, still no
  mutation. Fresh hook Stroke sequence 7: **Accepted**, revision 3, `.26` on both,
  exactly two total host glass writes and guest effects. Its duplicate is denied.
* Separate `GlassFrosting.Frost`/material and side-pane boundary sentinels stay
  unchanged throughout; this is noninterference, not cabin/frost simulation.
  Guests may reapply identical absolute material values on denied updates; zero
  guest setter calls is neither required nor claimed.

Reproduce from `autonomous/source` using a **new** receipt leaf and results path:

    RUN=/home/jaimep/our-winter-car-agent-env/autonomous/rounds/000125-work
    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name <new-leaf> -- dotnet test tools/PaneScrapeBridge.Tests/PaneScrapeBridge.Tests.csproj -c Release -p:DeployToGame=false --filter 'FullyQualifiedName~WrongPane' --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=<new-name>.trx' --results-directory "$RUN/test-results"

The RUN handoff/verification manifest retains exact commands, exits, parsed TRX
counts, original/transmitted/result packets and hashes, baseline/red source, final
source diff and cleanup assertions. Full bridge, expanded gameplay, Net, Launcher
and Python checks plus Net/Core builds are recorded separately; overlapping
suites are not additional unique coverage. Core uses only existing copied rig
compiler DLLs read-only, with deployment disabled and before/after hashes stable.

No installed/protected contents, original checkout or normal saves are accessed.
No rig preparation, lock/marker, deployment, native process, controller edit,
commit or prior evidence overwrite occurs. Protected-input provenance remains
BLOCKED, not re-observed or cleared. Native tool/contact/action/results, ordinary
physical input, different-save, native late join/rejoin/save-reload, Steam/two-PC
and four-player soak remain **NOT_TESTED**. The future native mutation/precondition
driver extension and independently authorized readiness checks below are still
separate work; passing this fixture does not enable them.

## Round 000123: current readiness audit, portable green / native BLOCKED

Assigned evidence root: `autonomous/rounds/000123-work`. This is a fresh audit of
the existing seam, not another implementation of scraping. Production, protocol,
catalog and tests are unchanged. The only source change is this report. Existing
focused tests and discovery ran **before** this documentation edit.

### Executed evidence

Exact argv, cwd, UTC, exit codes, raw output and before/after source/binary hashes
are in each named command leaf's `receipt.json` and `output.log`; .NET assertions
also have retained TRX under `*-results/`. RUN was explicitly the assigned root.
All commands were invoked through the existing `tools/v11_portable_receipt.py`
runner; the inner commands, relative to `autonomous/source`, were:

| Command / evidence leaf | Exit / observed result |
| --- | --- |
| `python3 -B tools/protected_input_provenance.py --run "$RUN" --name contact-provenance-01 --retained-only` (`provenance-command-01`) | 1, BLOCKED; zero installed-log content bytes |
| `python3 -B tools/v11_contact_discovery.py --run "$RUN" --name contact-discovery-01 --provenance contact-provenance-01` (`discovery-command-01`) | 1, static checks true; 93 exact, 16 unreachable |
| `python3 -B tools/v11_contact_discovery.py --run "$RUN" --name contact-discovery-01 --verify` (`replay-command-01`) | 0, exact offline replay; 26 output hashes and 39 provenance hashes |
| `python3 -B -m unittest discover -s tools/tests -p 'test_v11*py' -v` (`focused-discovery-01`) | 0, 42 passed |
| `python3 -B -m unittest discover -s tools/tests -p 'test_protected_input*py' -v` (`focused-protection-01`) | 0, 36 passed |
| `dotnet test tools/PaneScrapeBridge.Tests/PaneScrapeBridge.Tests.csproj -c Release -p:DeployToGame=false` (`bridge-01`) | 0, 84 passed |
| `dotnet test src/WinterMP.Net.Tests/PaneScrapeGameplay/WinterMP.PaneScrapeGameplay.Tests.csproj -c Release -p:DeployToGame=false` (`pane-gameplay-01`) | 0, 96 passed, including the same 84 bridge cases |
| `dotnet test src/WinterMP.Net.Tests/WinterMP.Net.Tests.csproj -c Release -p:DeployToGame=false --filter 'FullyQualifiedName~PaneScrape\|FullyQualifiedName~Scraper\|FullyQualifiedName~VehicleClimateStreamPolicy'` (`net-pane-01`) | 0, 105 passed |
| `dotnet build src/WinterMP.Core/WinterMP.Core.csproj -c Release -t:Rebuild -p:DeployToGame=false '-p:MwcGamePath=/home/jaimep/.steam/root/steamapps/common/My Winter Car'` (`core-01`) | 0, net35 compilation only |

The test receipts retain the additional console/TRX logger and results-directory
arguments. Pipes in the table's filter are Markdown-escaped, not shell escapes.
`contact-discovery-01/inputs/` retains exact pre-edit source/catalog bytes;
`discovery.json` retains the six complete selected FSM records. The current file
`catalog/dump-23268598.json` itself remains the **historical** Tools 0.1.0 GAME
dump dated 2026-06-13, not a current native dump. Its `Scrape`, `Freezing`,
`GlassFrosting`, `PickUp`, `PlayerTrigger` and `IceScraper` records have IDs
3720794055, 3007572044, 311833676, 728551272, 1427562570 and 2012982511 respectively.
All twenty current catalog binding values match production `PaneScrapeData`.

None of the 93 exact rows is a live-native pass. Six initialization rows and ten
native field/contact/ownership/lifecycle groups remain unreachable. The dump has
neither action field values nor global-transition metadata. A declared scraper
factory does not prove a live shared scraper; historical `activeState` does not
prove current Initialized/Started state; missing save fields do not prove that
vanilla never saves a value. Historical `.005` scrape, `.47` heat, material name,
pre-bridge guest rollback and `.26 -> 1` cold reload remain historical only.

### Current source-linked authority findings

* Admission/ownership: `PaneScrapeSync.Bindings.cs:85-178` discovers unique shared
  car/tool, initialized FSMs and audited actions before installing hooks.
  `PickupGate` at lines 46-70 delays native attachment; `StrokeAction` at lines
  30-44 replaces the local additive event. `PaneScrapeSync.cs:125-189` and
  `ScraperLease.cs:28-62` require authenticated epoch/sequence, correct shared
  tool, pickup then equip, fresh outside pose and parked vehicle, **not driver
  ownership**. Requests contain no cutoff or guest-supplied equipped truth.
* Contact: `PaneScrapeSync.cs:247-305` uses host-known actor feet/yaw/age, bounded
  finite eye/direction, and a new host `Physics.Raycast` first hit on the exact
  pane within `.8m`; pickup uses the same first-hit path within `1m`. Portable
  physics is a double. This does not prove the current native camera/layers or
  obstruction behavior. Cached contact age is not taken from a guest request.
* Exactly once/result: `PaneScrapeSync.cs:143-199,236-245` keeps the request,
  native actions, publication and effects inside the reentry guard. Only the two
  glass actions execute on host; `PaneScrapeAuthority.cs:53-100` authenticates,
  consumes attempts and reads the actual adapter result. `PaneScrapeReplica.cs`
  and `PaneScrapeSync.cs:113-120,202-239` apply absolute cutoff/material and permit
  only the accepted actor's effect. `SessionMessagePolicy.cs:23-26` and
  `SessionManager.Messages.cs:88-107` authenticate the peer/selected host before
  dispatch; test doubles are not evidence of real transport authentication.
* Positive executable: `tools/PaneScrapeBridge.Tests/BridgeTests.cs:126-174,321-333`
  runs production pickup/equip/stroke hooks and codec. The non-owner guest changes
  counted host glass once, host and guest cutoff/material agree, and only guest
  gets an effect. Both cars retain non-owner/255 fields. Host-local hooks also
  execute once. `.25 -> .255` and `.47` here are **fixture values**, not new
  native measurements; the accepted result is produced by the bridge, not seeded.
* Negative executable: `BridgeTests.Validation.cs:14-145` and
  `BridgeTests.cs:176-208` reject invalid/wrong first-hit contact, range, stale or
  nonfinite pose/equipment, inside/moving state, wrong target/tool/actor/epoch and
  duplicate/out-of-order requests without another glass/effect mutation. Denied
  authenticated sequences cannot become valid on replay; fresh valid operations
  recover. `PaneScrapeAuthorityTests.cs:92-137` additionally covers stale contact
  facts. Reentry during glass, publication or effects is independently green in
  the expanded suite. These are serialized callbacks, not multithreaded Unity.
* Frost/lifecycle: `VehicleWorldSync.Climate.cs:352-358,404-408` still updates
  interior frost and skips only the bound windshield's climate-byte writer.
  `PaneScrapeAuthority.Capture` reads current vanilla cutoff; reset clears only
  in-memory authority. Existing portable rejoin/reset cases pass, but native
  FREEZE/roof initialization, other-pane noninterference, native rejoin and save/
  reload are **not executed**. No sidecar or saved scrape replay was introduced.

### Blocker and next bounded implementation contract

The new retained-only assessment still lacks independently retained original-time
bytes/custody/identity or independently attributable evidence explaining the old
same-metadata digest drift. Original baseline pin and retained JSON are unchanged;
current installed-log descriptor metadata is stable, **content equality is not
tested**. Repeating local hashes cannot recover missing provenance. The native
gate is unchanged and launch is prohibited. No rig was prepared, locked, deployed
or launched. Compiler DLL input hashes remained stable on read-only mounts.

The portable production seam is green; this audit found no failing production
assertion requiring a new runtime patch. The smallest concrete follow-on is a
**tooling-only regression for a fresh wrong-pane outgoing stroke**, usable while
the native gate remains blocked:

1. Work only in `tools/GuestSaveProbe/LiveBagProbe.Pane.cs`, `.PaneTrace.cs`, the
   bounded `tools/v11_pane_audit.py` bridge section and focused portable tests.
   Today `Pane.cs:79-82` replays a captured outgoing stroke unchanged, and
   `v11_pane_audit.py:492-514` asserts replay/far-contact denial. There is no
   fresh wrong-pane path in that bridge. Simply changing the old replay payload
   would test `ReplayedSequence`, not `WrongPane`.
2. Add a one-shot, marker/opt-in-gated test mutation of **only Pane** on the next
   genuinely emitted guest Stroke, at the existing outgoing-message observation
   boundary (`PaneTrace.cs:49-55`). Preserve production-allocated fresh Sequence,
   Actor, Epoch, VehicleId, ToolId and eye/direction. Record original/transmitted
   packets. Do not directly invoke authority, assign private sequence/lease state,
   fabricate results, transfer car ownership or set final pane values. Clear the
   one-shot observer on teardown and on failure; never ship it in release payloads.
3. First add a failing source-linked portable regression for that path. Assert
   exact `WrongPane`, consumed high-water, unchanged cutoff/revision/material/
   glass/effects on both fixture peers, rejection of corrected same-sequence
   replay, then a fresh valid hook stroke accepted exactly once. Retain existing
   actor/stale/contact guards; a blanket guest denial is a regression.
4. The future native bridge must also explicitly assert its reported ownership
   and host-away preconditions; the old `audit` branch's ownership/rollback checks
   at lines 526 onward are **not** current positive bridge acceptance. Preparation
   of these checks is portable tooling only. Do not execute them on the next
   tooling-only card or count fixture/parser tests as native results.

After **independent** gate clearance, a separately authorized readiness-only card
must first run the existing `readiness` path (`v11_pane_audit.py:420-445`): real guest
command response, actual resume/spawn selection, and authenticated alive
host-observed pose aged 0..0.6 seconds. No ready-flag or pose injection, no scraping.
Only a later bounded native bridge contract may exercise the single-pane journey
with copied saves, both rig locks/markers, explicit roles/hashes and scoped cleanup.
Native host/guest action, fresh-player/different-save join, vanilla save/reload,
ordinary input, Steam/two-PC and four-player soak remain separate unrun gates.
V11 remains partial. H08's accepted portable timer repair also stays partial for
native/input/Steam/different-save/save-reload/soak; this audit adds no sauna evidence.

## Round 000047 discovery-tool reference

## Reproduce under a newly assigned RUN

From `autonomous/source`, with `RUN` explicitly equal to the assigned existing
contract directory, choose new leaf names every time:

    python3 -B tools/protected_input_provenance.py --run "$RUN" --name contact-provenance-new --retained-only
    python3 -B tools/v11_contact_discovery.py --run "$RUN" --name contact-discovery-new --provenance contact-provenance-new
    python3 -B tools/v11_contact_discovery.py --run "$RUN" --name contact-discovery-new --verify
    python3 -B -m unittest discover -s tools/tests -p 'test_v11*py' -v

The first command deliberately exits **1 / BLOCKED** while the original
protected-input provenance remains unresolved. It reads retained JSON without
atime changes and observes installed-log descriptor metadata with **zero log
content bytes**. It does not repeat host/storage probes. The second command
consumes that same-RUN observation and independently recomputes its assessment;
it never opens installed-game inputs, personal saves, the original checkout or
the rig. It has no launch mode or authorization override. Do not substitute the
legacy `v11_pane_audit.py static/prepare/audit/bridge` commands: those inspect
protected contents or run native fixtures and are not this read-only path.

Discovery exit **1** means static checks succeeded but native remains BLOCKED;
exit **2** means an audit/input/changed-signature error. Neither is native PASS.
Existing leaves, redirected inputs, duplicate JSON keys and nonfinite JSON fail
closed. Output retains exact source/catalog bytes, descriptor-bound noatime read
receipts, before/after hashes, existing-but-not-deployed binary hashes, exact
argv/UTC/role, raw provenance hashes, classifications and scoped cleanup.
`discovery.json` can be recomputed from `inputs/` without accessing the game.
The read-only `--verify` command checks retained hashes and replays the exact
classification; exit 0 verifies the **BLOCKED audit artifact**, not native safety
or gameplay. It creates no files and does not rerun the protected-input observer.

## Reconciliation and evidence levels

Each row says **exact**, **changed**, **ambiguous** or **unreachable**, with its
own evidence level. Exact catalog identity is not an exact live object:

- All twenty catalog profile fields are compared with both the pinned tool
  profile and current production `PaneScrapeData.Keys/Audited`. The discovery
  helper now rejects changed/missing hand, eye, state and event fields too; it
  previously pinned only seven fields. Production already pins all twenty.
- Six exact FSM records are retained: pane Scrape (not same-object Break),
  Freezing, interior GlassFrosting, Hand PickUp, PlayerTrigger and IceScraper
  factory. Duplicate FSM/state/variable signatures fail closed. A factory
  declaration is not a live shared tool; an absent eye FSM is not proof that the
  eye GameObject is absent.
- Current `dump-23268598.json` is a historical Tools 0.1.0 GAME dump. It records
  names, states, transitions, active/activeState and variable declarations, not
  live action fields or current initialized/started flags. Current Tools emits
  `actionTypes`, still not action parameter values. The helper no longer claims
  action-field availability merely because an arbitrary `actions` member exists.
  Explicitly empty action lists remain distinguishable from missing metadata;
  nonempty unversioned action objects are not a supported native field schema.
- Source checks retain lines for initialized/started gates, two glass-only native
  actions, material 6, actor-only heat/sound, unique WINDSHIELD event interception,
  real shared Hand.PickedObject lease, .8m first-hit contact, fresh alive/outside
  pose, parked speeds, result ownership, selected-host routing and rejoin epoch.
  Text anchors are inspection evidence, not execution or a complete C# proof.
- Native action types/fields, material identity/value, scraper equipment/holder,
  PlayerInside/trigger bounds, eye/contact, object ownership and current frost
  values remain **unreachable**. Source expectations and historical `.005` delta,
  `.47` heat, camera-X motion and `corris_frozen_windshield` observations are
  explicitly labeled, never emitted as fresh observations.
- FREEZE/Delay/Check roof declarations are catalog evidence only. Current native
  reset/save/load is unreachable; the historical `.26 -> 1` cold-load observation
  is not a new save test. The bridge reads native cutoff and resets its in-memory
  session state, with no new persistence. Missing save metadata is unknown, not
  proof of absence of vanilla save behavior.

All current native discovery, injected-state fixtures, host/guest action,
authority-once, native peer result, ordinary input, different saves, native
rejoin, fresh-player late join, save/reload, Steam/two-PC and four-player soak
remain **NOT_TESTED/BLOCKED**. The portable bridge accepted in round 000041 is
separate evidence; this audit does not promote it to native acceptance.

## Next bounded step

Preserve the independent protected-input gate. No local repeated hashes or
source/catalog agreement can recover missing original-time provenance. After
independent clearance, a later assigned contract may inspect only one parked
Corris pane/tool/contact chain in the marked, exclusively owned copied rig,
including actual initialized/started flags and current action field identities.
Do not seed a final cutoff, manufacture a native response, grant vehicle ownership
or count valid guest denial as capability. Gameplay implementation, ordinary-input
validation, fresh-player join and vanilla save/reload require their own evidence.
