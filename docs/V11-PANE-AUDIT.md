# V11: parked Corris windshield scrape audit

Scope: exactly `CORRIS/BODY/Windshield/collider::Scrape`, backed by
`CORRIS/Simulation/CarTempCorris::Freezing.CutoffWindshield` and material `6`
(`corris_frozen_windshield`, `_Cutoff`). This is a discovery/check fixture, not
an implementation of guest scraping and not a V11 completion claim.

## Round 000153: fresh prerequisite reconciliation — still BLOCKED

This infrastructure-only receipt does **not** authorize a native follow-on. The
existing retained-only observer ran in the actual assigned
`autonomous/rounds/000153-work`, first into `reconciliation-before`, then into
the new `reconciliation-final` leaf after narrowly scoped evidence fixes:

    RUN=/home/jaimep/our-winter-car-agent-env/autonomous/rounds/000153-work
    python3 -B tools/protected_input_provenance.py --run "$RUN" --name reconciliation-final --retained-only

These leaves now exist: do not reuse/overwrite them. Reproduction requires a
new leaf in the actual assigned RUN and authorization for metadata observation.
The final observer exited **1**, `gate_status=BLOCKED`, at
2026-09-19 20:09:06 UTC. `native_launch_allowed` and
`future_isolated_launch_eligible` remain false. No target content was read.

- **Current metadata:** before/after descriptor/path/mount observations agree;
  inode 16178453, device 59, size 4089693599 and all recorded timestamps also
  agree with retained round 000026 metadata. The descriptor-bound mount is
  `/home`, ID 758, read-only/noatime, confirmed by `fdinfo` and `statvfs`.
  Mountinfo's device `0:51` and `st_dev` `0:59` are distinct observations, not
  independent storage identities or a cause diagnosis. Btrfs can report such
  distinct values; binding is by descriptor mount ID/inode. Current content
  equality remains **NOT_TESTED**, not inferred from unchanged metadata.
- **Historical comparison:** the pinned baseline and ten fixed JSON receipts
  retain matching before/after hashes. Independent offline replay recomputes
  their exact raw-byte/chunk hashes and descriptor consistency, then compares
  3901 historical target chunks: only chunk 241 differs over
  `[252706816,253755392)`, with `f2dc72...` versus `709aac...` whole digests.
  Both disagree with the pinned original `c415ea...`. Earlier stable, unstable,
  and later stable-but-different claims remain historical; none supersedes the
  mismatch. Exact hashes are in the RUN's `verification.json`.
- **Original/causal provenance:** independently retained original-time bytes,
  descriptor/chunk evidence and attributable explanation of historical drift
  remain missing. The original JSON has only a whole-file digest. Round 000028
  host probes remain historical, not newly executed; their counters/permission
  errors cannot establish this file's cause. No writer, RAM or disk fault is
  diagnosed, and no claim is made that an external backup cannot exist.

The observer now preserves interpreter flags in exact argv, bounds its source
hash observations inside report UTC, and rejects missing/duplicate/inconsistent
descriptor-bound mount records rather than labeling them stable. Tests were
red first (six failing assertions), then **22 focused / 214 full Python tests
passed**. The RUN-specific independent `verify_receipts.py` imports no observer
helpers and opens only RUN artifacts; **65 recorded checks pass**, while its
gate result remains BLOCKED. This is retained-evidence consistency verification,
not independent historical custody or OS-wide syscall tracing. Raw command
receipts preserve cwd/argv/UTC/exit/stdout/stderr hashes and cleanup/role limits.

No runtime/protocol/catalog/persistence change, build, deployment, rig write or
Wine/Unity/Steam launch occurred. V11 remains **partial**. Native discovery,
contact/tool/frost ownership, native host/guest strokes/results, ordinary input,
different saves, native late join/rejoin/save/cold reload, Steam/two-PC and
four-player soak remain **NOT_TESTED**. Next prerequisite is separately
authorized acquisition and independent review of missing original/causal
provenance, not another same-host rehash or a native readiness attempt.

## Round 000151: portable bridge verification and admission correction

The existing connected production bridge already accepts a valid parked-Corris
non-owner guest stroke through pickup/equip/stroke hooks, the codec, host lease,
contact validation, glass-only authority and absolute peer results. This round
keeps that path and fixes one admission edge: `PaneScrapeSync.OnUpdate` previously
installed the initial epoch/replica before validating its snapshot. A rejected
zero-revision snapshot or decision could pin an invalid epoch and block valid
admission afterward. It now commits the candidate replica/epoch only after
acceptance. The established-epoch barrier, targeted admission high-water,
no-speculation rule and actor-only effect deduplication remain intact.

`tools/PaneScrapeBridge.Tests/BridgeTests.Admission.cs` is source-linked into both
existing portable bridge assemblies. Before the fix, its two admission cases
failed the epoch assertion and its other 28 cases passed. Afterward all 30 pass:
invalid initial update and blocked pre-admission input; genuine host admission,
guest pickup/equip/hook stroke accepted once; matching host/guest cutoff/material
and revision; stale-epoch and duplicate rejection; wrong pane/vehicle area,
spoofed/absent actor, non-holder tool use, wrong tool, stale pose/lease and
nonfinite/out-of-range contact/eye/direction rejection without peer glass or
effect changes. The prior host-away/non-owner wrong-pane/recovery and host-local
positive regressions also pass. No vehicle ownership transfer is introduced.

There is **no guest-proposed cutoff or contribution field** in `ScraperAction`.
The contribution tests append an untrusted float to a genuine hook packet:
NaN, infinities, negative, zero, large and even `.005` are all rejected by the
existing codec's trailing-byte check before authority. Existing session policy
also bars guest absolute results and unauthenticated intents. These checks do
not claim to validate a new contribution API or arbitrary native efficiency;
the unchanged valid packet invokes the host action boundary's own `.005` once.
No wire/schema/protocol version, catalog, native delta or persistence changed.

Actual evidence root: `autonomous/rounds/000151-work/portable-bridge-151/`.
Command JSON receipts retain exact argv/cwd/UTC/exit codes and log hashes; TRX
includes request/result bytes, actor/epoch/sequence/high-water/revision/cutoff
traces and assertions. Full portable suites: bridge 136, source-linked gameplay
148 (includes bridge cases, not independent native evidence), Net 5446,
Launcher 20, Python 212. Net net35/netstandard2.0 and Core net35 rebuild with
`DeployToGame=false` pass without warnings/errors. Launcher retains its existing
missing vendor BepInEx ZIP warning. The four read-only game DLL build inputs
have matching before/after descriptor/hash receipts; no installed logs or saves
were read, rebaselined or changed. Source/binary manifests accompany the handoff.

Engine actions, first-hit physics, scene objects and session transport remain
**doubles**; SessionManager/Unity/Harmony dispatch is not executed by these tests.
The scoped invalid-input packets are fixture inputs, not synthetic accepted
game responses. Native initialization/roof/FREEZE and guest-save guard paths
are unchanged; the existing portable capture/rejoin/reset tests are not native
persistence proof. No sidecar save/replay is added. No launch/deployment/rig
mutation occurred and the protected-input/native gate remains unresolved.

V11 remains **partial**. Current native discovery/contact/tool/frost ownership,
native host/guest action/results, ordinary input, different saves, native late
join/rejoin/save/cold reload, Steam/two-PC and four-player soak remain
**NOT_TESTED**. Only a later separately authorized contract after independent
native-gate clearance may exercise the copied-rig readiness/contact/stroke path.

## Round 000142: source-linked outgoing wrong-pane diagnostic (portable only)

The earlier portable `GuestStrokeWire` had its own mutation implementation;
passing it did not exercise the developer probe's outgoing observer. The fixture
now compiles `LiveBagProbe.PaneTrace.cs` alongside the actual Core bridge and
invokes that observer at the session double's send boundary. There is no second
test-only pane mutator. Engine actions, physics, session transport, BepInEx
logging and Harmony registration/dispatch are doubles; this does **not** prove
Unity/Harmony detour execution or native readiness.

The diagnostic command `pane-arm-wrong-pane` requires both
`WINTERMP_LOCAL2P_BAG_TEST=1` and `WINTERMP_LOCAL2P_V11_PANE_TEST=1`, both game-root
`wintermp-live-bag-sandbox.txt` / `wintermp-v11-pane-sandbox.txt` markers, and a
connected guest. It emits no request. At the next fresh outgoing guest Stroke,
the existing observer clones the production packet and changes only `Pane` from
1 to 2. Actor, sequence, epoch, vehicle/tool IDs, eye and direction remain byte
identical; it does not call authority, assign a lease/sequence, transfer ownership
or set a pane result. Pickup/equip/keepalive, wrong actors, results and previously
observed sequences do not consume the one shot. The original request is unedited.

`pane-stroke-packets|<original base64>|<transmitted base64>` is written to the
diagnostic log and snapshot; `pane-mutation-packets|...` retains the wrong-packet
pair across subsequent fresh strokes. These are outgoing-boundary bytes, not a
delivery receipt. `pane-replay-stroke` retransmits the retained transmitted
packet; `pane-replay-original-stroke` sends its retained original with the SAME
consumed sequence. Neither allocates a fresh sequence. `pane-cancel-wrong-pane`,
bridge reset/failure, plugin teardown, command/snapshot/send failure and lost
gating clear the mutation. No probe is added to the release payload.

Fresh evidence: `autonomous/rounds/000142-work/handoff.md`, command receipts/TRX
under that RUN, and `run-179-fixture/report.json`. The red regression first
failed on the missing source-linked arming seam after a valid hook stroke had
already succeeded. A subsequent self-review regression caught older packet
replays replacing the saved latest stroke; the observer now retains only fresh
strokes. The verified fixture establishes exact WrongPane with consumed
high-water and unchanged peer cutoff/revision/material/glass/effects, corrected
same-sequence ReplayedSequence, then a fresh valid hook stroke Accepted once and
replicated, with duplicate suppression. Existing invalid actor/stale/contact/
equipment/native-failure and positive host/guest tests remain part of the suites.

Reproduce without any game access using the three dotnet test commands below,
or run `python3 -B tools/v11_bridge_readiness.py --run "$RUN" --name <new-leaf>`
with the actual assigned RUN. Do not invoke the native commands in this document
on the basis of these portable results. `v11_pane_audit.py` changes only its source
hash manifest, not readiness or launch gates. V11 stays partial: native
discovery/action/results, ordinary input, native rejoin/save/reload,
different-save, Steam/two-PC and four-player soak remain NOT_TESTED. No native
launch, deployment, rig/protected-input access or full native probe build was
performed for this contract.

## Round 000081: portable authority transaction closure

The connected one-pane adapter remains portable-verified, not native-accepted.
`PaneScrapeSync.OnAction` now guards the entire request through absolute-result
publication and actor-local effects, in addition to `PaneScrapeAuthority`'s
glass callback guard. Previously a callback during `Publish` could reenter after
`Decide` had returned: a second stroke could mutate the pane, Off/Drop could
change equipment, or another result could interleave with the accepted result.
Nested authenticated attempts now only consume the existing shared lease's
sequence high-water; they cannot mutate equipment/glass or publish a nested
result. `finally` releases the guard for the next fresh request. No message,
catalog binding, vehicle ownership or persistence semantics changed.

The new regression assembly under `src/WinterMP.Net.Tests/PaneScrapeGameplay`
source-links the existing `tools/PaneScrapeBridge.Tests` engine doubles and all
prior bridge regressions, plus production Core bindings/sync and FsmHook. It is
separate from the main Net assembly to avoid conflicting engine/session doubles.
Run **both** Net test projects; running the main project alone does not run this
source-linked bridge assembly:

    dotnet test src/WinterMP.Net.Tests/WinterMP.Net.Tests.csproj -c Release -p:DeployToGame=false
    dotnet test src/WinterMP.Net.Tests/PaneScrapeGameplay/WinterMP.PaneScrapeGameplay.Tests.csproj -c Release -p:DeployToGame=false
    dotnet test tools/PaneScrapeBridge.Tests/PaneScrapeBridge.Tests.csproj -c Release -p:DeployToGame=false
    python3 -B -m unittest discover -s tools/tests -v
    dotnet build src/WinterMP.Core/WinterMP.Core.csproj -c Release -t:Rebuild -p:DeployToGame=false "-p:MwcGamePath=/home/jaimep/.steam/root/steamapps/common/My Winter Car"

Actual RUN: `autonomous/rounds/000081-work`. Before the implementation edit, all
12 new result/effect reentry cases failed assertions, while both existing
guest-hook positive cases passed. Afterward the combined source-linked suite
passed 96 tests, full Net passed 5374 and Python passed 182; Core rebuilt net35
with zero warnings/errors and deployment disabled. The RUN preserves exact
argv/cwd/UTC/exit codes, raw logs/TRX, source/binary hashes and protection receipts.

The positive portable path uses the actual pickup/equip/stroke hooks, codec,
shared lease, fresh host-observed pose/first-hit contact checks, authority and
absolute result/replica. A parked non-owner guest changes host cutoff from `.25`
by the audited `.005` once; only the guest gets its `.47` effect. Engine actions,
physics and transport are **counted doubles**, not a real game. Existing negative
coverage preserves actor/epoch/sequence/pane/tool authentication, absent/dead/
inside/moving state, stale pose/equipment/contact, occlusion, malformed geometry,
replay and native-callback reentry rejection. The added cases cover all six
nested operations during guest-result publication and host-local effects and
verify replay rejection, unchanged peer/canonical state and fresh-action recovery.

Production audit: the catalog still pins all twenty fields; SessionMessagePolicy
and SessionManager authenticate the sending actor and selected host. The native
WINDSHIELD event is intercepted before addition, glass-only actions exclude host
heat for guest strokes, and VehicleClimate skips only this bound windshield.
Other panes/interior fog and vanilla initialization/save behavior are untouched.
Capture still observes native FREEZE/roof changes; no sidecar or saved scrape
replay was introduced. Portable rejoin/reset tests are not native lifecycle proof.

No deployment or game launch was performed. Read-only game DLL build inputs have
unchanged before/after hashes; protected roots remained on read-only mounts.
Original/save/installed-log contents were not rehashed or rebaselined, and the
historical installed-log provenance discrepancy remains unresolved. Native
discovery/action/results, ordinary input, different saves, fresh-player live late
join, native save/reload, Steam/two-PC and four-player soak remain **NOT_TESTED**.
Only a separately authorized contract after independent gate clearance may
exercise the real copied-rig readiness/contact/action path. V11 remains partial.

## Round 000047: current catalog/contact discovery — native still BLOCKED

[V11-CONTACT-DISCOVERY.md](V11-CONTACT-DISCOVERY.md) documents the new RUN-scoped
read-only receipt. It consumes a fresh retained-only provenance observation,
retains exact current source/catalog inputs, and classifies every audited binding
as exact, changed, ambiguous or unreachable at its stated evidence level. It
does not run the legacy static content-hashing/native commands below. All native
action fields, initialized/started state, ownership/contact and save/load/reset
observations remain unreachable while protected-input provenance is BLOCKED.
Only the discovery helper was tightened; production/wire/persistence is unchanged.

## Round 000041: portable bridge hardening — native still BLOCKED

The bounded implementation and executable Core-source-linked regressions are
documented in [V11-PORTABLE-BRIDGE.md](V11-PORTABLE-BRIDGE.md). They do not clear
protected-input provenance or prove native guest gameplay. All native, ordinary
input, different-save, late-join, save/reload, Steam/two-PC and soak dimensions
remain NOT_TESTED; the historical reconciliation below remains blocking.

## Prior reconciliation: round 000038 — BLOCKED, no native launch

The infrastructure contract is satisfied by an explicit **BLOCKED** diagnostic,
not by clearing the protected-input gate or completing V11. One current invocation
of the existing observer now supports a retained-only reconciliation:

    RUN="<actual assigned RUN>" python3 -B tools/protected_input_provenance.py --run "<actual assigned RUN>" --name reconciliation-new --retained-only

Use an explicit matching RUN and a new non-symlink leaf. It retains exact baseline
and ten fixed historical JSON receipts using `O_RDONLY|O_NOATIME`, and observes
installed-log metadata before/after with **zero target-content bytes read**. It
does not repeat Btrfs, namespace/EDAC or other host probes. The content-hashing
`protected_input_diagnostic.py` and legacy static/native commands below are
historical references, **not authorized commands for this card**.

`report.json` preserves UTC/argv/cwd/exit, current descriptors/mounts, tool-source
hashes, retained input hashes, cleanup and roles. `assessment.json` is a
deterministic projection of those receipts: independent replay requires no target
open. It names the baseline pin, retained-receipt integrity, current metadata,
validated historical descriptor/chunk comparison, and missing original/causal
provenance separately. No supplied boolean or prior `reconciled` verdict can
authorize launch. Invalid or ambiguous input fails closed (including redirected,
oversized, duplicate-key, nonfinite, non-object JSON and malformed chunk/descriptor
records). CLI operational refusal exits 2; a completed unresolved investigation
exits 1. This observer never replaces or relaxes the controller/native launch gate.

Actual RUN `autonomous/rounds/000038-work/current-reconciliation/`:
- Executed 2026-09-15 05:16:48 UTC, exit **1**, `gate_status=BLOCKED`,
  `future_isolated_launch_eligible=false`, `native_launch_allowed=false`.
- Original baseline remains pinned to
  `25064d49e10f09a2fbe6c6993d30a00ce91a7e9d6dcd56ef367035eb852bcdde`;
  original target digest is still
  `c415eafa5eb596ac6db044e3364d6c97efd30255b33894ae6596ff8f963a5314`.
  Baseline and retained JSON have stable before/after noatime read receipts.
- The installed descriptor remains inode 16178453, device 59, size 4089693599,
  atime_ns 1783249572474084659, mtime_ns=ctime_ns 1788846734409756066;
  metadata agrees with retained round 000026. **No current content hash exists.**
- Independently recomputed retained comparison: 3901 complete chunks, identical
  descriptor metadata, but `f2dc72...` versus `709aac...` full digests and chunk
  241 changes over `[252706816,253755392)`. The exact full hashes/ranges are in
  the assessment and historical section below. Original-time bytes/descriptor/
  chunks are absent from the pinned baseline. Round 000024's stable first read,
  unstable replay, and later stable-but-different reads are all retained; none
  supersedes a mismatch or authenticates the original.
- Prior round 000028 Btrfs permission failures/cumulative counters remain
  **historical**, not newly queried and not evidence of this file's cause.

Missing condition: independently retained original-time bytes with custody and
identity, or separately authorized independently attributable evidence capable of
explaining the divergent same-metadata reads and reconciling the pinned original.
No such source is supplied in these fixed receipts. This does not prove that no
external backup exists or diagnose a writer, storage or RAM fault. Further local
hash/counter repetition cannot satisfy that condition. Native launch remains
**PROHIBITED**, even though the diagnostic itself is verified.

Focused portable tests: **20 pass**; full Python suite: **124 pass**. These are
tooling/fixture tests, not native results. Raw commands, red regressions, offline
deterministic replay, immutability assertions, exact hashes and cleanup are in the
assigned RUN handoff and verification receipt. No runtime/catalog/protocol/H10
behavior was changed; no builds, deployment, native processes or rig resources
were created by this worker. All V11 native discovery/host/guest/authority-once/
result/late-join/save-reload dimensions remain NOT_TESTED in this card, as do
ordinary input, different saves, Steam/two-PC and four-player soak.

After **independent** gate clearance, a separately assigned later worker must
preserve the follow-on gameplay contract below: exactly one parked Corris
windshield, real tool/contact/frost ownership, host execution of valid guest and
host strokes, duplicate/invalid rejection, peer agreement, fresh-player late
join, vanilla save/reload behavior, copied-rig ownership and scoped cleanup.
Guest denial/rollback is not valid guest capability. Do not invent climate or
persistence behavior, expand to other panes/vehicles, or launch on this card.

## Prior provenance investigation: round 000028 (unresolved; native prohibited)

`tools/protected_input_provenance.py` retains original/failed JSON receipts and
collects bounded, non-mutating host observations. It does **not** reread installed
guest-log contents or inspect any log text/process environments. Run only with an
explicit actual assignment and a new output leaf:

    RUN="<actual assigned RUN>" python3 tools/protected_input_provenance.py --run "<actual assigned RUN>" --name provenance-new

The pinned baseline and retained JSON are descriptor-bound `O_RDONLY|O_NOATIME`
reads with exact raw bytes/hashes. The installed file gets metadata/FD/mount
observations only; its content equality remains NOT_TESTED. Receipts preserve
UTC, exact argv/exit/stdout/stderr hashes, canonical paths, inode/size/timestamps,
raw mount/device identity, historical chunk ranges, and scoped cleanup. Output
cannot replace prior evidence. Missing/malformed/unstable evidence fails closed.
This tool is not an alternative controller gate and provides no launch override.

Round 000028 `provenance-final/` exited **1**, `reconciled=false`: the original
baseline and eight historical JSON receipts were unchanged, but no independently
retained original bytes/descriptor/chunk provenance was recovered. The historical
round 000026 chunk-241 drift and all its failed receipts remain intact. Neither
same-host counter observations nor later agreeing hashes explain that drift.

Fresh host findings: `btrfs device stats` (without reset) reports cumulative
`corruption_errs=15` on `/dev/nvme0n1p1`; that is **not file attribution**. Btrfs
subvolume show at descriptor mount `/home` returned partial FS_TREE/subvolume-5
metadata, then exit 1 / B-tree search not permitted. Read-only snapshot listing
also exited 1 / operation not permitted: **not proof of no snapshots**. EDAC
exposed no `mcN` counters; `/proc/1/ns/mnt` was denied. No memory-health conclusion,
historical writer attribution or independent snapshot bytes can be inferred.
The first `provenance-1/` receipt is preserved, including the initially incorrect
subvolume-show target. A regression corrected it to use the descriptor mount root;
the final investigation still failed closed. Neither run read guest-log bytes.

Focused portable tests: **15 pass**. Full Python suite: **82 pass**. These are
tooling tests, including a real owned-child timeout, not native results. See
`autonomous/rounds/000028-work/handoff.md`, `verification.json`, raw command logs,
and both provenance directories for exact commands, source hashes and limitations.
Native launch remains **PROHIBITED**; no readiness or scraper follow-on is
authorized. V11 gameplay dimensions are unchanged. Native injected state, ordinary
input, Steam/two-PC, different saves, rejoin/save-reload and four-player soak remain
NOT_TESTED. Independent original-byte provenance or independently attributable
host/storage/memory evidence is still required; no baseline/input/gate may be
changed to bypass that requirement.

## Prior safety diagnosis: round 000026 (unresolved; native prohibited)

`tools/protected_input_diagnostic.py` is a standalone read-only diagnostic, not
a replacement launch gate. It imports no native driver and does not prepare,
deploy, launch, signal, acquire/create rig files, restore, rebaseline, repair,
drop caches, change permissions or inspect log text/process environments. Its
protected inputs are only the exact original baseline and installed guest log.
Other installed files, personal saves and the original checkout are **not freshly
enumerated** by this bounded diagnostic; prior full comparisons remain historical.

Reproduce under the actual assigned RUN, using a new name on every invocation:

    python3 tools/protected_input_diagnostic.py --run "$RUN" --baseline /home/jaimep/our-winter-car-agent-env/autonomous/rounds/000014-work/protected-before.json --name integrity-new

Every protected content read requires `O_RDONLY|O_NOATIME`, with no permission
fallback. Two full Python reads and independent coreutils/OpenSSL commands bind
their hashes to open descriptors, exact canonical paths, byte counts, permissions,
atime/mtime/ctime, and before/after identity. External commands inherit the read-only
descriptor as stdin rather than reopening a pathname. JSON records include exact
1 MiB chunk ranges/hashes, descriptor mount IDs/raw matching mountinfo, permitted
FD observations with denied/racing entries, command argv/UTC/exit/raw output hashes,
and a pinned before/after original-baseline comparison. Output is exclusive under
RUN. Missing/unstable inputs, command errors/timeouts or unresolved mismatches exit
nonzero; stable-but-different reads cannot clear the discrepancy. Historical drift
also remains blocking even if a later read were to return the original hash.

Round 000026 evidence is in `autonomous/rounds/000026-work/integrity-1/` and
`integrity-final/`. Initial reads agreed on
`f2dc72f26e14a054552f7d293e8c6a4501a89c9d81f262d1817afc5bc1ea28c6`,
not the original `c415eafa5eb596ac6db044e3364d6c97efd30255b33894ae6596ff8f963a5314`.
The original baseline file remains pinned to
`25064d49e10f09a2fbe6c6993d30a00ce91a7e9d6dcd56ef367035eb852bcdde`.
Later historical chunk receipts differ only at zero-based chunk 241, range
`[252706816,253755392)`; initial chunk SHA256 was
`9c59adaa488f2c80474da0de4e50646cd8fd3fbeaa3ab8f8040e715c8aa681d2`.
The final diagnostic then observed **new in-run read drift**: Python/coreutils/
OpenSSL first returned the same `f2dc72...` digest, but the last Python pass
(2026-09-15 02:50:22..28 UTC) returned
`709aac9c5ef3dc34bbb065ab8ac45b670699d82f2bdd98620b9b49f3de2342dd`.
Only chunk 241 differed, to
`c314e8d7952ab62d18d664294be8ecf52e0e63d892ce79555f3d4bb793a8dc5f`.
Descriptor metadata including atime/mtime/ctime remained unchanged; the final
receipt explicitly sets `current_reads_stable=false` and exits 1. No later
stable receipt supersedes this failure. The new digest is a read observation,
not proof of a file write or a diagnosed hardware cause.
The original baseline contains no original-time descriptor/chunk identity or raw
bytes. These newer comparisons cannot reconstruct or authenticate its lost bytes.

The installed log resolves to `.local/share/Steam`, inode 16178453, size 4089693599,
and its descriptor mount ID 689 maps to this worker's read-only `/home` mount.
`st_dev=59` and mountinfo `0:51` are retained exactly, not normalized or treated as
proof of a path swap. This is not proof of other namespaces or historical writes.
No matching permitted FD was observed, but visibility is permission-limited.
Python/OpenSSL/coreutils still share the same kernel/cache/storage; agreement is
not a physical-media or memory health test. Round 000016's reported corruption
counters/direct-read observations remain historical, not a current causal finding.

Conclusion: **original protected observations NOT reconciled; native launch stays
PROHIBITED**. The evidence gap is independent original bytes/descriptor provenance
or authorized host-level read/storage/memory evidence explaining the historical
digest drift. Do not repeat stable reads toward a manufactured pass. Only after
independent reconciliation may the controller schedule readiness-only: real guest
connection, actual spawn/resume choice and fresh authenticated alive guest pose;
stop before scraper gameplay. V11 and H07 remain partial. Native injected state,
ordinary input, Steam/two-PC, different saves, native rejoin/save reload and
four-player soak remain NOT_TESTED.

## Round 000024 static audit (native still blocked)

Assigned evidence root: `autonomous/rounds/000024-work`. No production, protocol,
catalog, native launch gate or persistence policy was changed in this round.
The new `static` phase reads the current catalog/source and the **original**
protected-input baseline without preparing/deploying/launching a rig:

    python3 tools/v11_pane_audit.py --run /home/jaimep/our-winter-car-agent-env/autonomous/rounds/000024-work static <unique-name> /home/jaimep/our-winter-car-agent-env/autonomous/rounds/000014-work/protected-before.json

Exit 1 is expected **only when the receipt shows successful static assertions and
a reproduced protected-input mismatch**; it is not a native PASS. Other errors
remain errors. Output refuses reuse of a scenario name. Existing rig locks are
opened read-only and held exclusively during inspection; no ownership or sandbox
marker is created. This is a diagnostic comparison, not a new launch gate or a
way to rebaseline the old one. Do not run `prepare`, `audit`, `readiness` or `bridge`
while the inherited safety issue remains unresolved.

`pane-static/` contains the exact six selected FSM records (one pane, its Freezing
and interior GlassFrosting, Hand, inside trigger, and native scraper factory),
source anchors with line numbers, source/existing-binary hashes, full protected
before/after observations, descriptor-bound repeat hashes and cleanup observations.
The fresh check passed 33 **static text/catalog assertions**, not 33 gameplay tests.

The original baseline still differs in exactly installed
`BepInEx/LogOutput-guest.log`:

* Original: `c415eafa5eb596ac6db044e3364d6c97efd30255b33894ae6596ff8f963a5314`.
* Current: `59404f76ad0a098583b7fa0cd7b052710b1f9ee9c2885f5c612a65485c4ee605`.

Full observed protected inputs did not change during the **first** static check;
the original baseline file did not change either. However, the assigned-RUN
`reproduce-static.sh` replay to `pane-replay/` then **failed** its before/after
equality assertion while all 33 static assertions still passed. Subsequent OpenSSL
at 2026-09-15 02:27 UTC read the installed log as
`f2dc72f26e14a054552f7d293e8c6a4501a89c9d81f262d1817afc5bc1ea28c6`.
This is new observed read/hash drift, not just the inherited mismatch, and its
cause is not established. The failed replay is preserved, not rerun toward green.
`audit-verification-manifest.json` retains **both** observations, the new drift,
historical/native reference hashes and prior H07 source/binary/log provenance.
Its failed protected-comparison assertions must not be hidden by the earlier
stable observation. Native safety remains FAILED/BLOCKED. No file was restored,
excluded or rebaselined. No game process was started or signaled; no rig owner,
command bus or game/profile sandbox markers were present. This does not claim
there are no unrelated system processes or prove a historical writer's identity.

### What is established, and at which evidence level?

* Current `catalog/dump-23268598.json` is an old Tools 0.1.0 GAME dump, dated
  2026-06-13. Its `Scrape` FSM ID is 3720794055 (not the separate same-object
  `Break` FSM). It declares `Distance`, `X`, `Xold`, `Windows`, `This`,
  `InsideTrigger`; Freezing ID 3007572044 declares `CutoffWindshield`,
  `ScrapeEfficiency`, `BodyTempAdd`, material `6`, `FREEZE` and roof/startup states.
  Interior GlassFrosting ID 311833676 declares `Frost` and `FrostGlass` separately.
* That dump omits action fields/values and global transitions. Missing metadata
  is **unknown**, not proof of no save action. Searching `ice scraper` returns
  no FSM; the exact `Spawner/CreateItems::IceScraper` factory is present. Neither
  fact establishes a live shared tool in the current copied save. The catalog
  binding names the actual `ice scraper(itemx)` and Hand.PickedObject, not merely
  the separate camera scraper model. Current native tool availability is UNKNOWN.
* Historical rendered raw evidence was independently read at
  `000003-work/pane-final-live/live-bag/host-1.txt` and `hand-description.txt`:
  native `.8m` MousePickEvent, SCRAPER_ON/OFF, outside-player check, camera-X
  alternating strokes, `WINDSHIELD -> State 7`, `.005` cutoff addition and
  material `_Cutoff`, followed by Sound/PlayerTemp side effects (`BodyTempAdd=.47`).
  This is **historical injected-entry observation**, not fresh input acceptance.
* The old native pre-bridge non-owner guest reached the local stroke but failed
  multiplayer: guest `.2509804 -> .2559804 -> .2509804`, host stayed `.25`.
  That is negative guest capability evidence, never a feature PASS. Historical
  host `.25 -> .255` did replicate as a climate byte; it is not the current
  exact-float bridge test. The old `audit` scenario intentionally encodes those
  obsolete routing expectations and must not be used to validate today's bridge.
* Current source **does** route authenticated ScraperAction to PaneScrapeAuthority
  through PaneScrapeSync, independent of vehicle ownership. Native pickup waits
  for the host lease; active pane strokes replace the native additive event.
  Host-known fresh alive/outside pose, bounded eye geometry, first-hit physics
  within `.8m`, equipped lease and parked vehicle gate acceptance. Actor/epoch/
  sequence/pane checks precede glass-only host mutation; the accepted actor alone
  may run sound/heat. Selected-host absolute PaneScrapeUpdate and guest LateUpdate
  own this windshield result while bound; the climate byte writer skips only this
  pane. Interior Frost and other panes remain in VehicleClimate. Without this
  adapter, a parked non-owner guest still cannot publish vehicle climate.
* Portable tests executed in this round cover the production authority/lease/
  replica/codec and climate policy with explicit adapter doubles: 133 focused
  tests pass. This does **not** establish that the current native guest can pick
  up, contact or scrape without vehicle ownership. That current gameplay answer
  is NOT_TESTED, blocked by protected-input integrity; do not answer yes from
  code inspection or assume the old negative result still describes the bridge.
* Persistence boundary: historical FREEZE sets cutoffs to zero and sheltered
  initialization to one; old native copied-save cold reload observed `.26 -> 1`
  (`scrape_persisted=false`). Current authority Capture reads live native cutoff,
  observes its revision, and retains in-memory high-water during peer rejoin;
  cold world/session reset creates a new epoch. No cutoff sidecar/replay exists.
  Current native save/reload, same/different-save rejoin and actual lifecycle
  interleavings remain NOT_TESTED. Never invent persistence to preserve old ice.

H07 review remains **partial, portable-only**: prior 53 focused/5212 full Net,
Core net35, static binding and provenance evidence were preserved. Fresh runs
again passed those Net counts and Core build (zero warnings/errors), and all
13 H07 static binding assertions. H07 native feed, native results/rejoin/save/cold
reload, ordinary input, Steam/two-PC and soak are NOT_TESTED. They were not
promoted by the previous supervisor's portable acceptance.

### Follow-on gameplay contract (not implemented by this audit)

After independent reconciliation of the **original** protected-input issue,
recover the real guest command/resume/fresh-pose prerequisite first; no pose or
ready-flag injection. Then exercise the already-existing bridge for exactly this
parked windshield with the host away and neither player owning the vehicle:

1. Native shared scraper pickup via Hand.PickedObject, host lease then equip;
   authentic guest pose/contact through `PaneScrapeSync.cs` and `.Bindings.cs`,
   `ScraperLease.cs`, and SessionManager routing. No vehicle ownership grant,
   guest-supplied equipped truth, synthetic response or injected final cutoff.
2. One authorized guest stroke changes **host native** cutoff exactly once by
   audited `.005`, both peers receive the same absolute result, only guest gets
   one native sound/heat effect, and other panes/interior Frost do not change.
   Repeat authorized host operation with roles correctly attributed.
3. Through the real outgoing request/transport route, repeat the identical stroke
   and send a fresh wrong-pane request; test no-tool and out-of-range contact.
   Require denial and no pane/effect mutation on either peer. The existing bridge
   already includes duplicate/range assertions but needs an actual native
   wrong-pane case. Never count a valid guest's denial/rollback as success.
4. Preserve same-peer high-water/effect baseline and absolute rejoin convergence;
   a separate fresh/different-save peer receives host state. Observe FREEZE/roof
   and save/cold-load behavior against vanilla, not a custom persistence policy.
   Keep those dimensions separate if they cannot fit the first bounded stroke run.
5. Use `v11_rendered.py`/`v11_pane_audit.py` plus the opt-in pane probe only in
   copied rig game/prefixes, both locks, explicit owner/sandbox markers, current
   payload hashes, bounded runtime and token-scoped finally cleanup. Recompare
   protected inputs, remove only owned processes/probe/markers/bus, retain errors.

Rendered injected-entry UDP evidence is distinct from ordinary mouse/keyboard,
real Steam/two-PC, different saves and four-player winter soak. All remain
NOT_TESTED in this round. V11 stays partial; this report closes only the audit.

## Historical reports (not current acceptance)

Round 000007 adds the connected protocol/Core equipment-contact adapter described
at the end of this document. Native acceptance remains separately reported there.
The earlier audit/seam sections below are historical, not current routing claims.

Round 000005 adds the **portable authority seam** described below, not a connected
guest gameplay feature. V11 remains Partial. The native observations in this
document are still round 000003 evidence; no new native guest acceptance is claimed.

## Executable and evidence level

`tools/v11_pane_audit.py --run <absolute-assigned-RUN> audit <unique-run-name>` runs a bounded rendered host
and UDP guest in `autonomous/test-rig`, each using its own copied Proton save
prefix. The opt-in `LiveBagProbe.Pane.cs` command driver is developer-only,
requires the ordinary live-bag marker plus a dedicated pane marker/environment,
and is removed from the rig after each run. It is not a release plugin.

Build (installed game references are read-only):

    dotnet build tools/GuestSaveProbe/GuestSaveProbe.csproj -c Release -p:DeployToGame=false "-p:MwcGamePath=/home/jaimep/.steam/root/steamapps/common/My Winter Car"
    python3 -m unittest discover -s tools/tests -p test_v11_pane_audit.py
    python3 tools/v11_pane_audit.py --run <absolute-assigned-RUN> prepare
    python3 tools/v11_pane_audit.py --run <absolute-assigned-RUN> audit <unique-run-name>

Initial rig preparation: `setup` uses the existing no-desktop installer. In this
autonomous source copy the vendor zip is absent; `setup-resume` preserves a
manifest of that failed setup and uses the existing original vendor archive as
a read-only loader input. `runtime` copies Proton into the rig because installed
Proton's own dist.lock cannot be written on the protected Steam mount. Do not
reuse an incomplete or unmarked rig blindly. Source/config/controller safety
policies are not changed by this tool.

For an existing cleaned rig, `prepare` claims it with the current round marker,
both exclusive locks and a NEW protected-input hash baseline. Never point `--run`
at a previous round. The argument is mandatory, has no historical default and must
identify a real nonsymlink contract directory under `autonomous/rounds`. Commands
reject a different round owner. Native scenario directories, command logs and JSON
receipts refuse existing filenames; only a scenario's own incremental assertion and
launch lists are updated. Build the probe first; entry into `NativeRun` copies the
current Core/Net/catalog plus probe payload under the rig locks. Finalize writes a
scenario-named receipt/archive inside the assigned RUN, not the old shared artifact.
The historical `audit` scenario still intentionally reproduces pre-fix guest rollback;
it is NOT a positive test of the new seam or a future guest bridge.

The fixture seeds only the host's initial windshield cutoff to .25, parks the
car and places the players outside. It then enters native Hand `Ice Scraper`,
pane `Get this glass`, and `Scrape 2` states. The game itself executes the
WINDSHIELD event, cutoff addition and material update; no final result, accepted
network message, owner field or guest pane value is injected. Physical pickup,
mouse down, camera movement, ordinary-input usability and Steam accounts are
NOT TESTED. A native Collider.Raycast near-hit/far-miss check is geometry evidence,
not a host contact validator or a camera MousePickEvent acceptance test.

## Native ownership/contact chain

Fresh rendered descriptions are preserved in the round's `native-contact/`
command responses, plus `hand-description.txt`:

* `Hand::PickUp` recognizes the literal item name `ice scraper(itemx)`, then
  `Ice Scraper` broadcasts `SCRAPER_ON`; `Off` broadcasts `SCRAPER_OFF`.
* Pane `SCRAPER_ON -> Init` binds `This` and its layer. `Player outside?` reads
  `CORRIS/Functions/PlayerTrigger::PlayerTrigger.PlayerInside`.
* `Mouse off 2`/`Get scroll` use `MousePickEvent` with native `Distance=0.8`;
  mouse down chooses `Get this glass`, which writes the selected object to
  `Freezing.GlassPos` for sound positioning.
* `Scrape 1`/`Scrape 2` alternate with camera X rotation comparisons and exit
  on mouse-up, inside-player or mouse-off checks. `Scrape 2` sends the native
  `WINDSHIELD` event to `Windows::Freezing`.
* `Freezing.State 7` adds `ScrapeEfficiency=0.005` to `CutoffWindshield` and
  immediately updates material 6 `_Cutoff`. The normal Update state refreshes
  material presentation. Increasing cutoff clears exterior ice, not interior
  condensation (`GlassFrosting.Frost`).
* `Sound` also adds `BodyTempAdd=0.47` to global `PlayerTemp`. A future guest
  request must not accidentally warm the host by replaying this unscoped side
  effect on the host. The audit does not alter it.
* Native `FREEZE` resets cutoffs to 0; startup `Delay -> Check roof` sets them
  to 1 under shelter. No save/load action for cutoff appears in this FSM.

## Historical round 000003 multiplayer wiring (before the bridge)

* `VehicleWorldSync.Climate.cs:15`: discovers Freezing and individual pane vars.
* `VehicleWorldSync.Climate.cs:125` plus `VehicleStateStreamPolicy.CanPublish`:
  publication requires local driver ownership or host with `RemoteOwner=255`.
  A parked non-owner guest is not a publisher; it has no scrape-intent route.
* `SessionManager.Messages.cs:441`, `VehicleWorldSync.Engine.cs:30`,
  `VehicleClimateStreamPolicy.Receive`: authenticate a guest climate sender,
  require established vehicle ownership and filter stale/snapshot misuse.
  A guest's incidental local scrape is not an ownership grant.
* `TryBuildVehicleClimate` captures individual pane bytes; normal climate
  messages and world snapshots carry the same pane identity. Receiver
  `OnRemoteVehicleClimate` and `ApplyRemoteIceLevel` restore host state through
  the existing stream/late presentation hold. No protocol/catalog change is
  needed to audit this path.

## Result interpretation

The executable deliberately requires a real native guest cutoff increase,
unchanged host cutoff, and subsequent guest reconciliation to the host. A PASS
for that assertion means the pre-fix missing guest feature was reproduced; it
is NOT successful guest scraping. The authorized case requires both a host
native increase and the corresponding quantized guest result. The rejoin case
scrapes on the host while the same guest is absent, then checks convergence.
The native save/reload observation records whether the scrape survives, rather
than relabeling a native reset as persistence support.

The final round report contains exact observed values, command exit codes,
source/binary/catalog hashes, complete log bundle, remaining limits and cleanup
assertions. Native warnings/errors remain in raw logs; successful bounded pane
assertions are not a claim of an error-free whole game.

### Observed rendered run: pane-audit-2 (exit 0)

| Case | Actual observation |
| --- | --- |
| Ownership | Both peers bind vehicle 2784793521, locally owned false, remote owner 255; host publishes, guest does not. |
| Contact geometry | Native windshield collider hit from .6m using .8m ray; miss from 2m. Camera input remains NOT TESTED. |
| Pre-fix guest failure | Host .25; guest .2509804 -> .2559804 through native stroke, then restored to .2509804; host remained .25. |
| Authorized host stroke | Host .25 -> .255; guest .254901975 (byte quantization). The material _Cutoff matches in captured responses. |
| Same guest rejoin | Host scrapes while guest absent to .26; returning guest receives .258823544, including material _Cutoff. |
| Native save | Host save reaches menu and changes copied savefile.txt plus native auxiliary saves. |
| Cold reload visibility | Host 1.0 and guest 1.0, versus .26 before save: scraped cutoff did NOT survive. This is the native roof/startup reset gap, not a persistence pass. |

Both live/reload cleanup receipts assert no owned processes left, game/profile
markers and command bus removed, and identical installed-game/personal-save
hashes. The final exact-driver replay is recorded separately under `pane-final-*`
with UTC assertion timestamps, launch roles/prefixes and source/binary manifests.

Portable verification: Net 5117/5117; Launcher 18/18; Python 24/24 (including
three new parser tests). The parser tests were first run red, then green. Probe
and Core Release net35 build: zero warnings/errors. The Launcher build separately
warns about the pre-existing omitted source/vendor loader archive; no installer
release was produced. One earlier native scenario failed because connection
completion preceded local-player discovery; it is preserved and the driver now
waits for actual player readiness rather than weakening the fixture.

## Historical round 000003 proposed contract (superseded by current audit above)

Implement a host-authoritative parked-Corris windshield scrape intent and result,
not a whole-car ownership transfer. Define/bump protocol and catalog bindings
atomically. Authenticate the actor against the sending peer; bind session epoch,
monotonic stroke sequence and this exact vehicle/pane; reject stale, duplicate,
wrong-pane and non-established tool-use requests. Validate fresh host-known
outside-player pose, native 0.8m contact distance and an unobstructed ray to this
pane, plus scraper-equipped evidence. Never accept a guest-proposed cutoff.

Intercept the native guest stroke before its local additive mutation, execute
one accepted native pane delta on the host without attributing guest body-heat
side effects to the host, and publish an absolute revisioned pane result. Preserve
independent interior frost and other panes. Demonstrate out-of-range/no-tool and
duplicate denial, accepted stationary guest stroke, no transient guest divergence,
late join and same-peer rejoin. Audit the native FREEZE/roof reset lifecycle and
preserve vanilla save/initialization semantics. The earlier speculative sidecar
proposal is superseded: do not add durable scrape replay merely because the
native sheltered startup resets the cutoff. Prove the actual lifecycle boundary
in the same copied-save, rendered two-peer fixture.

## Round 000005: tested host-authority seam, native integration still open

The assigned contract explicitly permits an independently tested intent/decision/
result seam when the equipment/contact bridge cannot fit safely. This is that
fallback, not a newly enabled multiplayer feature:

* `src/WinterMP.Net/Sync/PaneScrapePolicy.cs` separates an immutable intent (actor,
  epoch, sequence, discovered vehicle, exact pane and tool identity) from host-only
  equipment/contact observations. There is no cutoff, equipped flag, contact claim
  or player pose in the request. These types are **not IMessage**, have no message
  ID/serializer and are not routed by SessionManager. Existing protocol 257 and
  catalog semantics are unchanged; the future native/wire integration must bump
  protocol and catalog bindings atomically rather than serializing these types
  implicitly or repurposing VehicleClimate.
* `PaneScrapeAuthority` accepts a legitimate parked, outside guest without vehicle
  ownership. Authenticated actor and active epoch are checked before consuming the
  strictly increasing uint sequence. Valid-epoch authenticated denials consume their
  sequence too; replay cannot become valid after equipping or moving closer. It
  validates a host-established exclusive scraper lease, fresh alive/outside pose,
  exact pane and first unobstructed contact within native .8 m. Observation freshness
  is a bounded policy (.6 s), not a claim of native contact validation.
* The authority owns the decision ledger and invokes `IPaneScrapeHost`'s glass-only
  callback once. It reads the absolute result back from the adapter; it does not
  calculate an invented final cutoff. Callback failures fail-stop that authority,
  including partial mutations; nonfinite readback cannot become an accepted result.
  The integration must log/disable on NativeFailure, not silently retry the action.
* `PaneScrapeReplica` authenticates host/epoch/vehicle/revision, accepts absolute
  results, forbids speculative mutation, and exposes personal-effect permission
  once only to the accepted actor. A bulk snapshot that overtakes an ordered action
  result cannot resurrect old ice or swallow that actor's effect. The native
  glass-only adapter must exclude Freezing.Sound/PlayerTemp; actor-local sound/heat
  dispatch is still part of the missing integration, not implemented by this class.
* `Capture()` observes the current native pane for snapshots. It observes FREEZE
  and roof initialization instead of replaying a saved cutoff. Same-peer reconnect
  retains authority sequence and replica effect high-water state; fresh peers get
  current absolute state. A cold world gets a new authority epoch and vanilla's
  actual initialized value. No custom persistence or lifecycle hook was added.

34 portable authority/replica cases cover positive guest/host execution, exact-once
decisions, invalid actor/tool/contact/pane/epoch/sequence, boundaries/nonfinite data,
callback failure/reentrancy, actor-safe effects and distinct rejoin/fresh/cold models.
The adapter in these unit tests is an explicitly labeled test double. Those tests
do not manufacture native equipment proof or demonstrate physical game execution.
The initial missing-seam regression failed to compile; a later snapshot/effect race
failed an actual assertion before its fix. Raw red/green evidence is in the assigned
`autonomous/rounds/000005-work/` directory.

### Exact missing native bridges and next bounded implementation

1. Host-established scraper equip/drop lease and stable shared item identity.
   Historical native `hand-description.txt:308-317` shows `Ice Scraper` hides the
   held ItemPivot and activates a separate camera IceScraper model; `Off` broadcasts
   SCRAPER_OFF. Merely entering that state, seeing the model or claiming generic
   motion ownership is not proof of a real equipped object. Production has no
   scraper catalog binding or corresponding lease. Bind the actual Hand.PickedObject
   ice scraper item, authorize exclusive pickup/equip from host-known item identity
   and proximity, then revoke on off/drop/death/disconnect. Do not populate the new
   host context with a guest-supplied `equipped=true` or just call the old probe's
   `pane-scrape` entry bypass and label it legitimate equipment.
2. Authenticated eye/contact geometry. Existing `PlayerPoseReader.cs:13-20,44-46`
   sends feet position and **yaw-only** rotation (`PlayerSyncManager.cs:205-214`),
   not eye position/camera pitch. This cannot reconstruct the native .8 m aiming
   ray or camera-X reversal. A bounded tool-use pose bridge must validate eye offset
   against fresh accepted player pose, orientation and first-hit host physics against
   this exact outside pane. Do not substitute a feet-radius test or the existing
   near/far Collider.Raycast audit as a completed host contact validator.
3. Atomically bind the one pane, wire the intent/result/session epoch and snapshots,
   suppress guest `Scrape 2 -> WINDSHIELD` before its additive side effect, and call
   the audited glass-only native mutation on accepted host decisions. Preserve all
   other panes/interior frost and do not transfer vehicle ownership. Keep sequence
   high-water marks on same-peer rejoin; resynchronize sequence allocation after a
   guest restart and distinguish a new identity/epoch from the existing slot.
4. Extend the current-RUN native fixture to equip the actual identified scraper,
   use the validated pose/contact bridge and assert legitimate guest host+replica
   convergence, actor-local heat, replay/tool/range denial, fresh join and same-peer
   rejoin. Observe vanilla FREEZE/roof/cold-load behavior; no new persistence is
   justified merely because sheltered initialization changes the cutoff to 1.

No new native game was launched for this seam-only fallback. Graphics/accounts are
not claimed to be unavailable: the limitation is these unimplemented trust bridges.
Ordinary input, rendered valid-guest execution, Steam/two-PC and soak remain NOT TESTED.

## Round 000007: connected equipment/contact adapter (V11 still Partial)

Protocol 258 adds `ScraperAction` (258) and `PaneScrapeUpdate` (259), separate
from vehicle climate. `PaneScrapeSync` is installed with the world components;
SessionManager routes authenticated requests/selected-host updates. The catalog
binds only this windshield, its native glass actions, Hand and the shared scraper.

The actual Hand.PickedObject pickup entry waits for host first-hit authorization
before native parenting/joint attachment. The exclusive lease is independent of
vehicle ownership; Equip/Off/Drop follow native Hand states, keepalive cannot equip,
and stale/dead/disconnected holders are revoked. Every valid-epoch authenticated
attempt consumes its sequence, including denials. Generic item-motion claims for
the scraper require the established lease, so a guest cannot first move an
unleased distant tool into contact. Peer rejoin receives a targeted sequence and
personal-effect baseline before input is enabled; normal snapshots do not swallow
in-flight accepted effects.

Eye/direction are input, not proof: Core anchors them to the fresh host-observed
actor feet/yaw, validates finite/unit geometry, and uses host Physics.Raycast's
first hit within native .8m for this pane (1m for native pickup). The host also
checks outside/alive status and parked linear/angular velocity. Glass execution
calls only the two validated native State 7 actions, excluding the Sound transition.
Only the accepted actor receives one permission to run its own native sound/heat
actions. Guests never execute the intercepted additive stroke; the exact float
absolute result supersedes the climate byte for this windshield only. Other panes
and interior frost are unchanged. FREEZE/roof/cold startup remain vanilla; there
is no custom cutoff save/replay.

`tools/v11_pane_audit.py --run <assigned-RUN> bridge <unique-name>` is the connected
injected-entry fixture, distinct from historical `audit` rollback reproduction.
It records production source/wire/catalog and binary hashes, exact role commands,
assertions and scoped cleanup. Its strict .005/absolute-float assertion rejects
mere climate-byte reconciliation and multiple native increments. The initial
fixture can seed a missing tool only through host native IceScraper.SPAWNITEM;
guest tool materialization must use the existing production spawn path. No lease,
accepted result or final pane value is injected. Camera/feet positioning and native
Hand/stroke entries are fixture inputs, not ordinary physical input evidence.

That historical RUN is `autonomous/rounds/000007-work`. Earlier native attempts found no
live scraper in the copied save (native factory count 0, only inactive prefab).
After host native tool creation, both rendered peers bound shared ID 3491869109;
the no-tool stroke did not mutate either pane. Pickup/contact acceptance remains
under investigation in this RUN; consult its final handoff/receipt for the verdict.
Do not interpret binding/no-tool assertions as authorized guest gameplay success.
Actor-local heat, connected rejoin/cold-load, other panes/vehicles, ordinary input,
Steam/two-PC and four-player soak remain separate unverified gates.

## Round 000009: pose prerequisite diagnostics (WIP, native blocked)

The bridge no longer treats `Connected` plus `offer|none` as spawn completion:
that is also the state before the returning-profile offer arrives. It waits on
the production resume-ready flag, chooses the actual prompt if present, and
requires a fresh, alive host-observed guest transform before tool interaction.
This driver ordering regression has portable red/green coverage. The old native
guest log never tracked a local player and still showed a pending offer; that
supports the race hypothesis but does not prove it was the only cause.

Probe-only snapshots now expose resume phase, pending relocation, dead/disabled
flags, local transform sequence and host remote transform timestamp/age. Optional
Harmony observers count real glass/effect calls and sample the heat change inside
the effect method; they do not replace any decisions, poses or cutoff results.
The fixture adds strict actor-only effects, real outgoing stroke replay, and
beyond-.8m contact assertions. These new native assertions are NOT yet exercised.
Production peer authentication, .6s freshness, equipment/contact authority,
alive/outside checks, wire version and vanilla persistence are unchanged.

The assigned `autonomous/rounds/000009-work` contains failing rendered-launch
receipts: desktop Xwayland reported zero monitors/0x0, and Wine virtual desktop
failed with BadWindow. The WIP `tools/v11_rendered.py` alternative isolates an
installed compositor's config/cache/runtime/D-Bus in the rig. KWin's abstract UNIX
display starts without changing the protected desktop socket directory, but the
game still exits during boot (last Core breadcrumb: Awake done; no host-ready
signal or native command reply). This is neither a fresh pose diagnostic nor a
successful scraper stroke. See the final handoff for exact commands/cleanup.
All V11 gameplay dimensions remain partial, not promoted by portable tests.

## Round 000014: host startup recovered; safety gate failed (WIP)

`tools/v11_rendered.py --run <assigned-round> readiness <unique-name>` now runs
only the existing spawn/pose prerequisite, not `bridge`/scraper fixtures. It holds
both rig locks before deployment and isolated compositor launch, receives the
compositor's actual display environment, queries it, and records UTC/PID command
and cleanup events. Core BootTrace pairs mutex release, readiness-file creation,
and final logging, with bounded managed-error capture that preserves exceptions.
Production-source portable tests cover flush/error/action semantics; no authority,
freshness, peer mapping, equipment/contact, protocol or persistence policy changes.

Fresh `autonomous/rounds/000014-work/boot-trace-1` evidence: isolated Virtual-0
1280x720; all post-Awake host steps returned at 2026-09-15T00:14:00.774Z. The real
host `pane-snapshot` response arrived at 00:14:18.158Z in GAME. This disproves a
current failure inside those steps, but does not establish the cause of round
000009's teardown (the machine rebooted between runs). Guest startup returned
from Awake and completed chainloader startup, then produced no command response
within 240 seconds. Final host quit response still reports Hosting with zero
peers and no remote pose. No scraper fixture ran; no fresh guest pose was proved.

Do not accept this run as safety-green. Cleanup removed owned game/compositor
processes, probe, command bus and sandbox/owner markers, but the protected-input
comparison failed for the installed game's old `BepInEx/LogOutput-guest.log`.
Baseline SHA256 begins `c415eafa`, post-native `433ad4cc`, subsequent independent
sha256sum/OpenSSL/Python reads `9913443f`; inode, size, mtime and ctime remain
unchanged (metadata dated September 8). Cause is unresolved; no original file was
restored, modified deliberately, or excluded from the gate. Personal-save hashes
match. Read-only repeat/chunk evidence is in `protected-hash-stability.json` and
`final-safety.json`. The system SDK10 also segfaulted twice; installed SDK8 built
Core/probe net35 and passed Net 5159, Launcher 18 and Python 39 tests. Those facts
do not establish a causal link to the hash mismatch.

Next bounded contract: first diagnose/re-establish protected-input read integrity
without changing the baseline or protected files. Only then isolate the guest
chainloader-to-first-Update/command boundary under the same rendered driver, and
run the existing spawn-choice/relocation/fresh-host-pose diagnostic. Do not inject
poses or weaken readiness. After a real authenticated guest transform is proved,
use a separate contract for lease/contact, exact-once native delta/effects and
replica evidence. V11 stays Partial; guest scrape, authority-once, convergence,
late join, multiplayer save/reload, ordinary input, Steam/two-PC and four-player
soak remain unproved. Historical discovery/persistence dimensions are unchanged.

## Round 000016: read-only provenance investigation (native still prohibited)

Evidence lives only in `autonomous/rounds/000016-work/`; the original round
000014 baseline and receipts were read, hashed before/after, and not replaced.
No prepare, deployment, native launch, file restoration, exclusion or baseline
change was performed. Full current enumeration still differs in exactly the
installed `BepInEx/LogOutput-guest.log`; all other installed-file and personal-save
digests match the original baseline.

Current sha256sum, OpenSSL, canonical-path sha256sum and two descriptor-bound
Python passes agree on `59404f76ad0a098583b7fa0cd7b052710b1f9ee9c2885f5c612a65485c4ee605`.
Compared to BOTH round 000014 stable chunk receipts (`9913443f...`), only zero-based
1 MiB chunk 241 differs (bytes 252706816..253755391 inclusive). Both receipts have
3901 chunks and 4089693599 bytes; inode 16178453 and nanosecond mtime/ctime still
match. The original `c415eafa...` baseline has no chunk or descriptor identity;
the differing original bytes cannot be reconstructed from its digest.

Current path and open-descriptor evidence resolves `.steam/root` through its
symlink to `.local/share/Steam`, not into the disposable rig. The historical rig
guest log has a distinct inode and is 1297 bytes; the current rig log is absent.
`mountinfo` plus descriptor `mnt_id` binds the installed log to this worker's
read-only `/home` mount; this does not prove the host filesystem or past readers
were read-only. Same-uid process/FD observations found no visible game/Wine
process or open protected-log descriptor, but many descriptor/namespace reads
were permission-denied. Absence of a visible writer is not historical provenance.

Buffered and requested-direct `dd` reads of chunk 241 agree on its current hash.
Requested direct I/O is not proof of independent physical-media reads on a
compressed filesystem. Read-only kernel/device diagnostics report pre-existing
Btrfs corruption counters; they do not attribute this log's drift to hardware,
storage, a writer, or a hashing process. Cause remains unresolved and the safety
gate remains FAILED. Do not rebaseline, repair, drop caches, remount, or run the
game to investigate it.

A separate, demonstrated receipt bug is corrected in `v11_safety_receipt.py`:
`zip` hid added/removed tail chunks, and a disappearing file lost the stability
receipt. Portable CLI regressions failed with two assertions and one exception
before the fix. Stability now records read errors and descriptor/path identity,
counts all bytes, compares unequal-length chunk lists, and reports stability
separately from the unchanged baseline gate. This diagnostic correction does
NOT explain chunk 241 or turn stable-but-different reads into a safety pass.

Next diagnostic requires independent read-only historical bytes or host-level
storage/memory/provenance evidence for that chunk, retaining the original gate.
Only after independent reconciliation may the existing bounded readiness
diagnostic run under both locks: real guest connection, actual spawn choice and
relocation, and an authenticated alive host-observed pose aged 0..0.6 seconds.
Do not inject a pose or weaken peer mapping, equipment/contact or freshness;
scraper gameplay still belongs to a later contract. V11 stays Partial with no new
native/ordinary-input/Steam/two-PC/soak claims from this investigation.
