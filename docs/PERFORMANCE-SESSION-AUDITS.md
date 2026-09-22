# Earlier session performance audits

Historical measurements; see [performance testing](PERFORMANCE.md) for current
instructions and the latest result. Timings belong to the workloads recorded below.

## September 2026 vehicle discovery filters

Vehicle system discovery now constructs a full path only for an unbound
`TurnSignals` candidate; its existing subtree check remains. Known engine audio
also bypasses a redundant transform-array allocation. Climate discovery rejects
unrelated FSM names and non-climate `Use` buttons before building paths. Every
admitted candidate still uses the existing full catalog/path checks. Discovery
deadlines, current native RPM validation and guest protections are unchanged;
protocol remains 210 and the Net binary is identical to the preceding build.

Both compared native binaries pass all **120 discovery checks**, retaining the
previous 108 plus twelve vehicle cases covering wrong names/subtrees, delayed
binding, gauges/tank, audio identity/replacement, and all eight climate bindings.
The Core/probe Release build has zero warnings/errors; all **4,287 Net tests**
pass, as do all **4,268 preceding native regression checks**, with identical
results. Both comparisons use the same probe and supporting payloads; only Core
differs. Heap diagnostics now attribute `EnsureClimateProbe` separately.

The native stress fixture contains unrelated vehicle controls with already-known
audio. Three warmups precede 21 measured samples for each discovery pass:

| Fixture / discovery | Median before → after | Collections before → after |
| --- | --- | --- |
| 1,000 FSMs / systems | 7.814 → 0.122 ms | 8 → 0 |
| 1,000 FSMs / climate | 7.636 → 0.105 ms | 8 → 0 |
| 5,000 FSMs / systems | 145.029 → 0.565 ms | 87 → 0 |
| 5,000 FSMs / climate | 145.092 → 0.362 ms | 87 → 0 |

These deliberately heavy discovery workloads do not measure gameplay FPS or
prove that the reported stutters are fixed. Relevant paths still need validation.

The matched 60-second heap recordings have almost identical frame counts
(host 7,282 → 7,286; guest 6,042 → 6,040). Nearest-caller attribution shows:

| Full path lookups | Host before → after | Guest before → after |
| --- | --- | --- |
| Vehicle systems discovery | 6,900 → 0 | 1,035 → 0 |
| Climate discovery | 6,900 → 360 | 6,900 → 360 |

Observed path-call heap growth in these two scopes totals 28,512,256 → 720,896
bytes on the host and 19,947,520 → 827,392 on the guest. These are inclusive
process-wide counter differences, excluding collection/decreasing intervals,
not exact allocated-byte totals. Native RPM validation is a separate scope and
retains its full identity checks.

Frame-only medians are host 7.289 → 7.874 ms and guest 8.883 → 9.447 ms, with
variable competing CPU load. Maxima are 144.328 → 171.076 ms and
182.157 → 158.868 ms respectively. Frames above 100 ms number 6 → 6 and
14 → 13; all coincide with collections. Whole-frame heap observations are also
essentially unchanged. No overall FPS or repeatable stutter improvement is claimed.
All four runs retain active cameras, 39 decoded factories and existing engine
readiness. One candidate startup cooling-factory wait also occurred in the prior
audit; it recovers, with all five cooling mounts agreeing and no remaining cooling
failure. The 14 personal saves and four installed payloads retain their hashes.
Both isolated copies have the candidate, diagnostics are removed, and games are
closed. Nothing is committed or released. Full driving and two-PC acceptance remain open.
Evidence and scoped predecessors: `build/vehicle-discovery-filter-audit/`.

## September 2026 scan index allocation

The preceding heap recording attributed substantial inclusive heap growth to
path lookup inside discovery scans. `ScenePathCache.IndexChildren` kept two
temporary dictionaries for each sibling group: its counts and first objects.
One dictionary now holds both values in a small struct. Enumeration order, native
name reads, duplicate suffixes and literal-name ambiguity are unchanged. The
dictionary is still local to each indexing call; no hierarchy data gains a longer
lifetime, and live lookups outside discovery are unchanged. Protocol remains 210.

All **4,287 Net tests** pass, including a new case with interleaved, case-sensitive
duplicates, empty names, a literal suffix collision, and removal/reordering.
Both native performance binaries pass the same **108 discovery checks**. Core
and probe build against the installed game assemblies with zero warnings/errors.
The probe and supporting payloads match; only the rebuilt Core/Net pair differs.
All **4,268 preceding native regression checks** also retain identical results.

In the 5,000-object native fixture, collection counts over 21 samples fall
7 → 6 for indexed paths, 6 → 5 for active host world discovery, 7 → 6 for guest
world discovery, and 5 → 4 for both stereo and banking discovery. These counts
do not measure allocated bytes or collection duration. The indexed-path median
is 5.733 → 3.898 ms, but unaffected enumeration also falls 0.804 → 0.636 ms:
changing background CPU load prevents attributing the whole timing difference
to this change.

Matched 60-second heap recordings show the intended reduction in path lookup
**inside discovery scans**, calculated by subtracting the separately recorded
outside-scan subset from the total:

| Eligible inside-scan observation | Host before → after | Guest before → after |
| --- | --- | --- |
| Lookup calls | 205,675 → 205,677 | 236,580 → 236,576 |
| Heap growth (bytes) | 320,966,656 → 248,139,776 | 372,514,816 → 276,963,328 |
| Mean growth per lookup (bytes) | 1,560.553 → 1,206.454 | 1,574.583 → 1,170.716 |

This is about 23% less observed growth per host lookup and 26% less per guest
lookup in the affected scope. Each recording retains twelve control, twelve item
and twelve NPC passes across 36 separate frames per role. These are inclusive
process-wide counter differences, excluding collection/decrease intervals, not
exact allocated-byte totals. Full-frame observed growth does not fall in this
pair: host mean 117,896.523 → 141,787.648 bytes and guest mean
382,832.357 → 421,774.658 bytes. The candidate renders substantially fewer frames;
periodic work and variable background load limit those per-frame comparisons.
This establishes the narrower scan-index improvement, not a general FPS gain.

The first frame-only candidate ran under substantial competing CPU load and
was slower (host median 10.108 ms, guest 10.494 ms). Background load dropped
sharply before the following baseline. That recording remains archived as
`frames-after-first`; a candidate repeat under lighter load gives the following
comparison. Both have 60-second samples with no method, heap or phase hooks:

| Frame-only measure | Host baseline → repeat candidate | Guest baseline → repeat candidate |
| --- | --- | --- |
| Median interval | 6.713 → 6.822 ms | 8.018 → 8.145 ms |
| p95 interval | 17.780 → 18.010 ms | 16.587 → 16.484 ms |
| Maximum interval | 155.664 → 181.214 ms | 163.622 → 154.830 ms |
| Frames at least 100 ms | 7 → 6 | 16 → 15 |
| Gen0 collections | 7 → 6 | 16 → 15 |

Typical timing is close to baseline. Every interval above 100 ms in this final
pair coincides with a collection. One fewer collection and long frame per role
in these samples does not establish a repeatable stutter improvement. Retain the
small lookup simplification for its measured reduction in scan memory churn;
allocation outside scans, complete driving and two-PC Steam acceptance remain
open. There is no cross-frame caching or changed guest safety condition.

All five live runs retain active PLAYER cameras, 39 decoded replacement factories
per role, the existing guest input/protection observations, and applied remote
OFF state for Corris and Sorbet; inactive taxi ignition stays pending. No new mod
error header appears. The initial busy candidate has a native PlayMaker `WWWPOST`
error during splash, before world discovery; the final frame pair has no new
error header. All 14 personal-save and four installed-payload hashes remain
unchanged. Both isolated copies contain the candidate, temporary probes/markers
are removed, and test games are closed. Nothing is committed or released.

Evidence and scoped predecessors are under `build/scan-index-allocation-audit/`.

## September 2026 world discovery scheduling

Routine world discovery previously combined protection/control discovery, item
discovery and NPC discovery in one frame every five seconds. The preceding
detailed guest trace measured about 54 ms per combined scan, including roughly
9 ms of item discovery and 5 ms of NPC discovery. These inclusive timings are
not additive across nested calls and can include collection or scheduling pauses.

Routine item and NPC passes now use their own deadlines with the existing shared
discovery budget. Due passes wait for an available frame without moving their
deadline. Initial discovery still completes all three passes before the join
snapshot request; explicit purchase refreshes also remain complete and immediate.
Each pass retains its own synchronous enumeration and path scope. Control
discovery keeps its existing protection preparation, and item discovery still
rejects motion for guest parts awaiting isolation. Newly found items update the
world identity hash immediately. Level/session resets restore complete discovery.
No wire format or message semantics change; protocol remains 210.

Eight new native scheduling checks cover initial delay, complete first discovery,
deferred deadlines, item hash updates, separate NPC discovery, immediate explicit
refresh, contained scan failure/retry, and session reset. All **100 preceding
native discovery checks**, these **eight new checks**, **4,268 native regression
checks**, and **4,286 Net tests** pass. Release Core/probe builds have zero warnings
or errors. The matched native discovery binaries differ only in Core, using the
same probe and support payloads.

Heap and detailed diagnostics now record `<role>-discovery-frames.csv` with the
native frame number and a phase mask: 1 for protection/controls, 2 for items and
4 for NPCs. Records merge phases within a frame and are bounded to 1,024 frames;
observations report dropped records. No phase hooks are installed in frame-only
or normal gameplay. This distinguishes work moved to another frame from work
that stopped running.

The matched 60-second heap recordings confirm the scheduling behavior in both
roles: the preceding binary performs all three passes on the same 12 frames
(mask 7). The candidate performs 12 of each pass across 36 separate frames
(masks 1, 2 and 4). Method-call counts reconcile with the phase records, and no
phase records are dropped. Routine work is redistributed without reducing its
discovery frequency in these samples.

Complete synchronous discovery has approximately unchanged cost in the native
5,000-object fixture: host active median 21.954 → 22.072 ms, guest active
26.773 → 27.147 ms, and host disabled 7.202 → 7.169 ms. The change separates
routine work across frames; it does not remove that work.

The separate 60-second frame-only recordings have no method, heap or phase
hooks. Only the Core binary differs within each before/after pair:

| Frame-only measure | Host before → after | Guest before → after |
| --- | --- | --- |
| Median frame interval | 7.751 → 7.342 ms | 8.899 → 8.474 ms |
| p95 frame interval | 19.840 → 19.952 ms | 17.770 → 16.952 ms |
| Maximum frame interval | 175.132 → 162.204 ms | 223.201 → 171.585 ms |
| Frames at least 100 ms | 6 → 6 | 16 → 15 |
| Gen0 collections | 6 → 6 | 15 → 15 |

These timings do **not** establish a general speedup or resolve the reported
stutters. Unrelated Godot processes had substantial, changing CPU use throughout
the comparisons; the instrumented candidate run was slower than its baseline.
The candidate frame-only run still has six host and fifteen guest frames above
100 ms, all coinciding with collections. Retain the scheduling change because
the phase records establish that routine passes stop sharing a frame while
preserving frequency and the tested discovery behavior. Allocation-driven pauses,
complete driving sessions and two-PC Steam acceptance remain open.

All four live runs retain the active PLAYER camera, 39 decoded replacement
factories, the existing guest protection/readiness observations, and applied
remote OFF state for Corris and Sorbet; inactive taxi ignition remains pending.
No new mod error header appears. One candidate heap run logs a native PlayMaker
`WWWPOST` error during splash startup, before world discovery; the frame-only
pair has no new error header. Existing native startup/audio errors remain.
The 14 personal save hashes and four installed payload hashes are unchanged.
Both isolated test copies have the candidate, test processes are closed, and
temporary probes and markers are removed. Nothing is committed or released.

Evidence and scoped predecessors are under `build/world-scan-phases-audit/`.

## September 2026 repeated engine-input waits

Routine preparation now returns a waiting reason when host tyre health,
drivetrain wear or gearbox oil is unavailable, instead of allocating and throwing
an exception on each update. Both preparation and native state entry retain the
same pause, blocked entry and readiness conditions. First-wait ring-buffer logging
is shared with the existing exception path. Unexpected validation failures still
fail closed; pending exceptions from other input callbacks keep their prior handling.
No identity, readiness result or native input is newly cached, and no wire behavior
changes (protocol 210).

The identical native probe passes all **4,268 checks** on both binaries, with only
Core differing. This preserves the ignition activation, missing-input, malformed
reader, native entry, recovery and saved-part checks. The Core Release build has
zero warnings/errors, and all **4,286 Net tests** pass.

The native four-wheel benchmark performs 1,000 preparations per sample, with
three warmups and 21 measured samples. Discovery stays outside the timed batches.

| Workload | Before median ms | After median ms | Before / after Gen0 collections |
|---|---:|---:|---:|
| Waiting for host health | 27.076 | 17.980 | 13 / 11 |
| Host health available | 17.171 | 17.875 | 11 / 11 |

Waiting preparation is about 34% faster in this fixture, an absolute saving of
about **0.009 ms per four-wheel preparation**. That small saving cannot explain or
resolve the large reported stutters. Ready preparation is slightly slower in
this pair; its validation work is unchanged.

Matched 60-second guest heap recordings show a modest effect on the full session:

| Metric | Before | After |
|---|---:|---:|
| Observed frame heap growth, bytes per eligible frame | 370,952 | 367,515 |
| Engine preparation, eligible mean ms per call | 1.191 | 1.144 |
| Guest median frame interval, ms | 8.749 | 8.780 |
| Guest Gen0 collections | 16 | 16 |

The roughly **0.9% lower observed heap growth** is an inclusive process-memory
measurement, not exact allocated bytes. These samples include profiling overhead;
typical frame times and collection counts are effectively unchanged. Each has one
sampled unrelated Godot process near 100% CPU. The largest remaining measured
allocation contributors are native path validation in engine input mount/reader
checks, which must still observe live identity changes.

The frame-only controls use the same comparison probe with zero method hooks:

| Role | Before median / p95 / max ms | After median / p95 / max ms | Before / after frames ≥100 ms | Before / after Gen0 collections |
|---|---|---|---:|---:|
| Host | 6.683 / 17.250 / 145.146 | 6.789 / 17.790 / 141.209 | 6 / 6 | 6 / 6 |
| Guest | 8.086 / 15.765 / 176.074 | 8.146 / 15.887 / 149.041 | 16 / 16 | 16 / 16 |

Neither frame control records a Godot process. This pair does **not** establish
improved gameplay smoothness: typical intervals, collection counts and the number
of large pauses are essentially unchanged. A lower single maximum is insufficient
to claim a stutter fix.

All four recordings finish with an active PLAYER camera, 39 decoded replacement
factories without failure, 24 guest engine-input bindings, and the same ten dormant
engines plus four safely paused tyre consumers. Guest Corris and Sorbet retain
applied host OFF state; the inactive taxi remains pending. No new error headers
appear in either role compared with its baseline. Heap eligibility, timing,
collection totals and nested caller partitions reconcile, with profiling scopes
returned to their roots. Only Core differs within each matched pair.

The updated detailed probe observes direct waits as well as legacy exceptions.
A separate candidate session reports all 14 paused-engine reasons, including all
four host tyre-health waits, with no dropped slow-call records. This diagnostic
run uses the newer probe and is excluded from the matched timing comparisons;
the tested production Core is unchanged.

Both isolated test copies retain the candidate. Test game processes are closed,
diagnostic plugins/markers are removed, and hashes of 14 personal save files and
four normally installed mod files remain unchanged. Full driving and two-PC Steam
play are not covered by these parked local sessions.

Evidence and scoped predecessors are under `build/engine-pending-audit/`.

## September 2026 ignition activation containment

Remote electricity incorrectly required every protected engine to be ready to
simulate before replaying native ON/OFF. A safely paused dormant engine, or a
wheel waiting for host tyre health, could therefore block power. The identical
native probe reproduces eight activation/startup failures on the preceding binary
and passes with the fix. Its power controller is initialized
before installing actions, while the engine uses genuinely serialized dormant
actions. Vehicle power fixtures preserve their actions through native startup
and verify ACC values as well as state names.

`PrepareForActivation` uses the same durable containment policy as admission.
Dormant graphs must retain their catalog/state identities and disabled component;
bound consumers missing host data must retain current bindings, protected writers,
and a durable pause. The native entry hook remains mandatory. Between discovery
scans, activation readiness rechecks these conditions without claiming simulation
readiness. `Prepare` still returns false for unresolved simulation inputs.

Ignition retries use this activation readiness check, then force a full protection
scan immediately before native power replay. First attempts and successful ON/OFF
transitions remain immediate. A changed initialized reader blocks power; a changed
serialized writer can awaken but remains blocked from executing when its native
actions are decoded. Ordinary discovery and state-entry validation control later
simulation recovery. No preparation method itself wakes a dormant graph.

The first live comparison also exposed false application of OFF for the inactive
taxi: its default ACC value matched OFF even though its power controller's object
was inactive. Remote power now requires an initialized, started controller on an
active object before doing engine preparation. The same controller must
remain ready after preparation and replay. Unstarted controllers retain their
pending state without forced engine scans; startup permits the normal guarded
retry. A controller replacement during preparation cannot be acknowledged.

Core/probe Release builds are clean. All **4,286 Net tests** and **4,268 native
checks** pass, retaining the previous 4,260 checks. Eight new cases cover dormant
activation readiness, contained tyre-input waits, remote ON/OFF during those waits,
changed readers blocking power, pending ON/OFF through native startup, and power
deactivation/replacement during preparation. Existing dormant tests now exercise
actual remote power replay and native object activation, including corrupted serialized
writes. Changed identity, missing metadata and replaced states also reject
between-scan activation readiness. The matched native binaries differ only in
Core; the Net binary, protocol 210 and probe are identical.

The final matched Local2P pair confirms that guest Corris and Sorbet now apply
the accepted host OFF state; the preceding binary leaves it pending. Both native
controllers are initialized, started and active. The taxi controller is also
initialized and started, but its object is inactive: it correctly remains pending
with no native state. Both runs finish with an active PLAYER camera, all 39
replacement factories decoded without failure, 24 guest input bindings, and the
same ten dormant engines plus four tyre consumers safely paused. Neither role
adds an error header compared with the baseline; existing game errors remain.

These frame-only samples contain no method hooks or heap instrumentation. Only
Core differs between runs; the probe, Net and catalog match.

| 60-second sample | Median frame ms | p95 ms | Maximum ms | Frames ≥50 ms | Gen0 collections |
|---|---:|---:|---:|---:|---:|
| Host, before | 7.637 | 21.556 | 293.894 | 40 | 6 |
| Host, after | 14.921 | 42.390 | 230.250 | 108 | 5 |
| Guest, before | 10.085 | 26.642 | 208.797 | 43 | 13 |
| Guest, after | 18.178 | 47.645 | 222.573 | 119 | 9 |

Frame performance is worse in this pair. Four unrelated Godot jobs appeared
partway through the baseline and ran throughout the candidate, with substantial
multi-core CPU usage recorded in the process samples. This prevents attributing
the timing change to the mod or claiming a performance improvement. Long stalls
remain, and a comparison under stable load is still needed. The live pair covers
parked OFF synchronization; ON/OFF and dormant awakening are covered by the native
fixtures. Full driving and two-PC Steam behavior remain unverified here.

Both isolated test copies contain the candidate, diagnostic plugins and markers
are removed, and game test processes are closed. Hashes of 14 personal save files
and four normal-install payload files are unchanged. The failed first live
launch is archived separately and excluded from these results.

Evidence and scoped predecessors are under `build/ignition-activation-audit/`.

## September 2026 ignition retry readiness

This report records the preceding revision. The activation containment fix above
replaces its simulation-readiness gate while preserving retry scheduling.

Waiting remote ignition attempts now call normal engine preparation before
requesting forced preparation. Inactive engines and unavailable host inputs
previously caused three waiting cars to force 60 complete engine scans per minute.
The first attempt still uses forced preparation immediately. A retry that passes
normal readiness must then pass fresh forced preparation before native ON/OFF
replay. Successful transitions clear their deadline, so the next transition
remains immediate even when the routine discovery slot is occupied.

Pending attempts retain the latest accepted host state and their existing
one-second retry timer. Repair detection follows the guard's normal three-second
discovery cadence; a repaired waiting engine may therefore recover later than
with the previous forced retries. New OFF state and expired ignition cannot
replay a stale pending ON. No readiness result or object identity is newly cached.
The protocol and Net binary are unchanged.

Matched 60-second local guest heap recordings:

| Measurement | Before | After |
| --- | --- | --- |
| Forced preparation calls from ignition retries | 60 | 0 |
| All forced preparation calls | 72 | 12 |
| Observed heap growth per eligible frame, bytes | 399,674 | 365,215 |
| Collections | 17 | 16 |

The **8.6% lower observed frame heap growth** is an inclusive process-memory
measurement, not an exact allocation count. Zero forced retry calls describes
this waiting scene: normal preparation still discovers engines, and a ready
retry still forces its activation check. Caller partitions and collection
exclusions reconcile. Both heap runs include one sampled Godot process near
the end of recording (baseline at 100 seconds, candidate at 90 seconds).

Separate frame-only guest recordings:

| Measurement | Before | After |
| --- | --- | --- |
| Median / P95 interval, ms | 8.124 / 16.381 | 8.327 / 16.237 |
| Frames ≥50 / ≥100 ms | 29 / 17 | 26 / 16 |
| Maximum interval, ms | 157.168 | 180.277 |
| Collections | 17 | 16 |

Every guest collection coincides with a frame over 100 ms. The candidate has
fewer long intervals but a worse median and maximum; this does not establish
an FPS improvement or resolve the long stutters. The frame-only candidate has
one Godot process snapshot at 40 seconds; the baseline has none. Host median
also rises from 6.807 to 7.277 ms. These are single stationary pairs on one PC;
two-PC Steam play and driving remain unverified.

Core/probe Release builds have zero warnings/errors. All **4,286 Net tests**
and **4,260 native checks** pass, including all 4,254 previous checks. Six new
native ignition cases cover pending readiness, failed activation after passing
readiness, recovery without another packet, replacement OFF, expiration, and
immediate first activation. Four live runs retain active PLAYER cameras,
39 decoded factories without failures, 24 guest input bindings, ten dormant
consumers and four wheel-health waits. No new error headers appear in either
matched pair; existing native and Steam startup errors remain.

The checked candidate is staged in both isolated game copies. Test games are
closed and diagnostic DLLs/markers removed. All 14 checked personal-save files
and four normally installed payloads retain their original hashes. Scoped
predecessors, recordings and verification are in
`build/electricity-readiness-audit/`.

## September 2026 native read-source filtering

Battery, heater, rear-window, wiring and heater-hose source classification now
uses the existing conservative leaf-name predicate before constructing a full
scene path. Unrelated `Installed` reads were repeatedly walking the hierarchy
for every possible circuit or hose. Possible matches still require the complete
current path and FSM name. Remembered sources retain their existing identity
through movement and catalog loss. Active discovery still uses its existing
path snapshot; no negative result is cached between calls.

The matched heap comparison uses the repeated baseline and the candidate:

| Guest measurement | Before | After |
| --- | --- | --- |
| Source-classification path lookups outside scans, per frame | 24.97 | 0 |
| All outside-scan path lookups, per frame | 87.92 | 62.36 |
| Source-classification observed heap growth, bytes/frame | 80,162 | 1,383 |
| Observed heap growth per eligible frame, bytes | 496,468 | 408,969 |
| Collections per sampled minute | 19 | 17 |

Observed frame heap growth is **17.6% lower**. These are inclusive process-heap
differences, not exact allocated bytes; frame counts and fixed-rate work also
affect the totals. The source-classification methods do not nest one another,
so their growth is summed here, without adding their nested path measurements.
Caller partitions, collection/decrease exclusions and final scope restoration
all reconcile. The heap candidate still reaches a 215.9 ms maximum interval.

The original baseline ran alongside a CPU-heavy Godot job throughout the
recording and is excluded from the headline comparison. The repeated baseline
has one Godot snapshot at 30 seconds, before the guest sampling window of about
41–101 seconds; its later snapshots and the candidate snapshots contain none.
The separate frame-only baseline has no Godot snapshots. Its candidate has one
at 110 seconds, after both samples finished (guest output at about 101 seconds).

| Guest frame-only metric, per minute | Before | After |
| --- | --- | --- |
| Median / P95 interval, ms | 8.296 / 17.210 | 8.136 / 16.608 |
| Frames ≥50 / ≥100 ms | 32 / 20 | 28 / 17 |
| Maximum interval, ms | 190.625 | 179.713 |
| Collections | 20 | 17 |

This is one stationary local pair. Every collection in both guest frame-only
samples coincides with a frame over 100 ms; long stutters remain. Two-PC Steam
play and driving still need acceptance testing.

All **4,286 Net tests** and **4,254 native checks** pass, retaining all 4,224
previous native checks. Thirty new checks exercise actual native reads across
the five source families: unrelated names, matching leaves under wrong parents,
duplicate siblings, repair after rejection, remembered moved sources with lost
metadata, and rename during an active discovery snapshot. Saved source values
remain unchanged. Core/probe Release builds have no warnings or errors.

All five completed live recordings retain active PLAYER cameras, 39 decoded
factories without failures, 24 guest input bindings, ten dormant consumers and
four wheel-health waits. Matched pairs differ only in Core and introduce no
new error headers; existing native/Steam startup errors remain. Net/protocol 210
is unchanged. The candidate is staged in both isolated game copies, with probe
DLLs/markers removed and test games closed. All 14 checked personal-save files
and four normally installed payloads retain their original hashes. Evidence,
scoped predecessors and verification are under `build/read-source-filter-audit/`.

## September 2026 frame-only and collection controls

The detailed profiler observes native interaction/camera calls in addition to
129 timed mod methods. Frame-only mode removes those hooks so stutter diagnosis
can check their influence. The initial frame-only guest sample still reached
260.733 ms. After adding per-frame collection events, the same development
Core/Net/catalog and copied profile produced these 60-second samples:

| Mode | Role | Median / P95 / maximum interval (ms) | Frames ≥50 ms | Frames with a collection |
| --- | --- | --- | --- | --- |
| Frame-only | Host | 6.766 / 14.324 / 204.485 | 21 | 9 |
| Frame-only | Guest | 15.939 / 26.790 / 271.385 | 46 | 20 |
| Detailed | Host | 8.806 / 25.874 / 203.039 | 50 | 8 |
| Detailed | Guest | 27.659 / 56.071 / 412.283 | 131 | 14 |
| Frame-only repeat | Host | 6.789 / 14.680 / 222.525 | 24 | 9 |
| Frame-only repeat | Guest | 15.223 / 25.350 / 260.856 | 50 | 21 |

In both frame-only samples every recorded guest collection coincided with an
interval over 120 ms, including the longest pause in each run. The first
sample's five longest guest intervals all contained a collection. Significant
pauses also occurred without a collection in that process: up to 160.672 and
182.477 ms. These observations justify investigating repeated allocations;
they do not measure GC pause duration, locate the allocation source or establish
that GC accounts for every reported stutter.

The detailed run was slower, but diagnostic overhead cannot be quantified from
this comparison. A process snapshot during repeat startup found three unrelated
Godot processes using several cores; they had exited by a later snapshot.
No process-load trace was collected during the earlier detailed sample, so
its slowdown cannot be assigned to either instrumentation or background load.
Two game instances also share the same CPU/GPU. No production code or protocol
changed in this pass, and these timings are not a gameplay FPS improvement.

The probe Release build has zero warnings/errors and all nine Linux launcher
checks pass, including profiling disabled by default, propagation of the new
mode to both roles and rejection of invalid modes before launch. All recordings
completed with working player cameras, 39 decoded replacement factories and
zero factory failures per role. The guest retained 24 input bindings and its
existing ten dormant-engine/four wheel-health waits. The six event files agree
with their corresponding frame summaries and collection totals. Raw recordings,
logs, payload hashes, load observations and scoped changes are retained under
`build/frame-control-audit/`. The temporary profiler was removed and both games
closed; normal installed payloads and all 14 checked save files are unchanged.

## September 2026 guest update fixes and remaining findings

Loaded profiling found guest engine callbacks building hierarchy paths and
allocating temporary lists even for unrelated protected FSMs. They now reject
unrelated names before building the path and create lists only when a rule or
previous binding exists. Existing bindings still undergo identity checks after
renames, reparenting and catalog replacement. Paused engine states also avoid
copying empty active-action lists; nonempty lists retain their snapshot before
finishing actions. No protection checks are throttled or removed.

The native benchmark additionally runs 10,000 engine-input callbacks per sample.
For an unrelated name, median time fell from 32.112 to 8.980 ms, with 32 versus
zero generation-zero collections across 21 samples. For a matching name on the
wrong path, the median was 35.620 versus 34.013 ms: that case still validates
its current hierarchy. The same probe reproduced a startup error in the old
build's part-fitting prompt; the new guard waits for world initialization.
All 17 performance/readiness checks and all 4,143 native correctness checks
passed on the new build. Core Debug and Release built without warnings.

The updated isolated host and guest loaded GAME, connected over localhost and
exchanged a 95-message world snapshot. Neither log contained the former
`DrawPartFitPrompt` exceptions. Live guest input callback mean time fell from
approximately 14 to 1 microsecond, but these instrumented runs do not establish
an overall FPS improvement. The final two-player sample still had pauses above
one second. Evidence, logs and the controlled benchmark CSVs are retained
locally in `build/live-stutter-audit/`.

The outstanding native error is now identified as `PLAYER::Update Cursor`,
state `Update cursor`: its `PlayerDir` output is bound, but `Camera.main` is
null. The tagged player camera becomes disabled as the player activates.
This also reproduces in the idle multiplayer solo control. A further control
with Core and the profiler removed, leaving only FastBoot, still logged the
native WherePlayerIsLooking exception. The camera-disable source remains
unresolved; trace runs have not attributed it to the native SetProperty,
EnableBehaviour or CutToCamera actions. Steam was initially not running; after
starting it, its connection log showed the client signed out, and game-side
Steam initialization still failed. A signed-in Steam retry is required before
attributing the remaining camera failure to the local launcher or native game.
The subsequent readiness pass below addresses the remaining `Init`/`Speed`
action reads. Both the native camera errors and long pauses
need resolution before calling local gameplay or overall stutters fixed.
No Steam-latency or two-PC FPS result was collected by these local tests.

## September 2026 starter hooks and vehicle readiness

Loaded traces identified two further pre-initialization callers: replacement
template validation (`Init`) and coolant temperature discovery (`Speed`). The
next two-player trace also identified wear-input discovery (`Oil pressure`).
Those paths now check state readiness before reading the lazy action getter,
including cached temperature/wear validation. Native checks exercise repeated
deferral and recovery after the original initialized states return.

Starter registration exposed a separate integration fault. Its leading
state-entry observers shifted the catalog positions used by native RPM binding,
guest writer protection and guest engine-input projection. RPM could disappear
from publication, and guest protection could pause the otherwise valid starter.
A shared lookup now accounts only for leading `FsmHookAction` observers; native
action identity, field signatures, destinations and foreign-edit rejection
remain enforced. It is also used by the guarded starter/gearbox/wheel callbacks.
No wire format or protocol semantics changed.

The native suite passes 4,154 checks, preserving every previously passing check
and adding 11 cases covering vehicle readiness, hooked RPM publication, starter
demand counts, saved-battery protection, foreign-action rejection and cold/warm
guest-input binding. Core Debug and Release build without warnings. The separate
performance/readiness probe passes 19 checks. Evidence is retained locally under
`build/vehicle-readiness-audit/`.

The final isolated host and guest connected and applied their join snapshot;
neither log contained uninitialized-action errors or the former RPM action-type
failure. This PC's launch environment also enables MangoHud. Disabling it for
one diagnostic run removed its repeated unsupported-present-mode warnings, but
the largest sampled intervals remained 1,376.829 ms on the host and 1,345.289 ms
on the guest (previous run: 1,301.361 / 1,362.959 ms). That does not establish an
FPS improvement or eliminate the underlying stalls. The overlay preference was
left unchanged. The final Release payload is staged only in the isolated desktop
test copy, with the diagnostic DLL and marker removed afterward. Hash checks
confirmed all 14 personal-save files and four normal mod payload files unchanged.

That readiness pass did not initialize dormant replacement templates or change
the failed-factory policy. Its live guest preparation still reported unloaded
engine-input factories, leading to the replacement-factory work below. The
player camera failure remained separate from those fixes.

## September 2026 replacement factory initialization

Both roles previously disabled all 38 replacement factories because prefab Data
FSMs had never received Unity Awake. Replacement validation now uses the same
native FSM initialization step as package and shopping-bag templates. This loads
action definitions (including native action Awake callbacks) without starting
the template or entering its Init/Load/Save states. Inactive serialized-template
checks verify unchanged identity/condition values and enabled/active flags,
stable action instances on repeated validation, and independent native identity
initialization when a product is cloned from the prepared template.

The first live retry prepared all templates and exposed two previously hidden
catalog mismatches. VIN103 deliberately disables its obsolete Init GetChild at
index 0. VIN130 disables the PartBlocking reference writes in both creation
states, and its native Data no longer contains that variable. The catalog now
records those exact flags. Validation still rejects changed types, destination
names, required actions, or reenabled obsolete actions. Guest replica setup skips
disabled reference writes. The install-point reference cannot be disabled.

Core Debug and Release build without warnings. All 4,174 native checks and 4,230
protocol/catalog unit tests pass; all 4,154 previously passing native checks are
retained. The separate performance/readiness run passes all 19 checks. Evidence
and the scoped patch are under `build/factory-readiness-audit/`.
The native bag/part fixture now also needs VIN103 and VIN130 Data definitions and
the `SPAWNERS_VIN/Starter130::Spawn` record in `native-bag-part-probe.json`. Extract
those with `tools/extract_fsm_assets.py` from the installed prefab asset and GAME
scene respectively; retain the disabled flags from the source definitions.

The final isolated two-player run confirms 38 bound and decoded replacement
factories with zero failures on each role. The host sent a 96-message snapshot;
the guest applied its replacement-part snapshot. Guest engine-input bindings
increased from four after the first template retry to eleven. This establishes
factory readiness, not working guest driving. Fifteen guest engine protections
remain paused: ten inactive, uninitialized consumers; Cooling with conflicting
native mount references; and four wheel conditions awaiting host tyre health.
The live probe records each failure reason and native readiness flags after the
measured window, without initializing, resuming or modifying those consumers.

Both roles still report the native player-camera exception and failed Steam
initialization. The 60-second instrumented sample measured host median/P95/max
frame intervals of 4.967/16.098/1,372.114 ms and guest intervals of
11.096/22.246/1,202.206 ms. The remaining pauses exceed one second; these results
do not establish a gameplay FPS improvement. MangoHud was disabled only for this
diagnostic run. The Release DLLs and catalog remain staged in the isolated test
copy; both test games were closed and the diagnostic DLL and marker removed.
All 14 personal-save files and four normal installed mod payload files still
match their original hashes.

## September 2026 radiator-fan identity correction

The next live trace identified the cooling mismatch precisely. The consumer's
db_RadiatorFan referenced VINP_RadiatorFan, while the configured VIN127 factory
referenced its sibling VINP_WaterpumpPulley. Earlier fixtures had assigned both
to the same manufactured mount, so their passing native arithmetic checks did
not establish the real scene identity. Installed build 23268598 has a separate
RadiatorFan137 factory and VIN137 prefab. The catalog now registers that factory
and uses VIN137 for both Valves and Cooling fan inputs. VIN127 remains a distinct
pulley; its receipt cannot provide fan installation. The fan's native
PartBlocking reference still points at the pulley mount.

Protocol 210 rejects peers with the old fan-input interpretation. The state-185
layout and existing factory identities remain unchanged; the new fan family
carries Wear/Tightness. No fan packaging rule was fabricated: the installed
CreatePartsPackages object has no fan factory. The native fixture needs the
actual RadiatorFan137 Spawn and VIN137 Data records in
`native-bag-part-probe.json`, extracted from level2 and sharedassets3.assets
respectively. The existing fan consumer fixture continues to use the actual
Valves/Cooling action definitions.

Core Debug and Release build without warnings. All 4,180 native checks, 4,232
protocol/catalog tests and 18 launcher tests pass. Every previous 4,174 native
check remains; six new checks cover the real factory layout, native fresh/saved
output references, replica references and independent fan/pulley installation
in both consumers. Two new catalog cases reject restoring the pulley mapping.
The separate performance/readiness probe passes all 19 checks.

The final protocol-210 host and guest connected and exchanged a 98-message
snapshot. Both roles report 39 bound/decoded replacement factories and zero
factory failures. Cooling's actual consumer and VIN137 factory mount match, and
its protection failure clears. Guest engine-input bindings increase from 11 to
24. Fourteen guest protections still pause: ten inactive, uninitialized engine
consumers and four wheel conditions waiting for host tyre health. This change
does not initialize or resume those graphs or establish working guest driving.

Native camera errors and failed Steam initialization remain. Instrumented
host median/P95/max frame intervals were 5.962/19.322/1,287.537 ms; guest intervals
were 14.268/32.414/1,481.369 ms. Long pauses persist, and this correctness fix does
not establish a performance improvement. The overlay was disabled only for the
diagnostic run. Evidence is retained under `build/engine-admission-audit/`.
Both test games were closed and the profiler removed; the isolated desktop copy
retains the protocol-210 Release payload. All 14 personal-save files and four
normal installed mod payload files still match their original hashes.

## September 2026 dormant engine admission

Ten inactive guest engine consumers had never received native Awake. Their
unloaded actions repeatedly failed engine preparation. Known dormant writers
now remain disabled with restart suppressed while admission treats their
durable pause as safe containment. Normal simulation readiness stays false.
Preparation neither decodes these actions nor initializes, starts or activates
the engine. The existing native state-entry guard must be installed before a
deferred graph can satisfy admission. Its graph, catalog rule and state
identities must remain unchanged, and required state names must be present.

When native activation initializes a deferred engine, full action destination
and signature validation still precedes restoration and native entry. A changed
serialized write remains paused, including when native code directly starts the
FSM. Saved-part isolation now requests this containment check rather than
requiring every dormant engine and pending host input to be simulation-ready.
The forced damage-protection scan still runs before any saved part is moved.
No protocol layout or semantics changed; compatibility remains protocol 210.

Core Debug and Release build without warnings. All 4,191 native checks and 4,232
protocol/catalog tests pass, preserving every previous 4,180 native check.
Eleven new native cases cover repeated dormant admission, simulation readiness,
reenabling, changed identity/metadata/states, native activation, rejected changed
write destinations, destroyed graphs, and isolation/restoration of a supported
loose guest part while its engine stays paused. The separate performance and
readiness run passes all 19 checks. That controlled part fixture is distinct
from the live scene observation below.

The final isolated two-player trace has 39 bound/decoded replacement factories
with zero failures, 24 guest engine-input bindings and ten paused initialization
waits without the former unavailable-action errors. Four wheel conditions still
wait for host tyre health. All seven discovered native parts are loose and
outside the supported replacement families: extinguisher clamp, engine block,
parcel shelf, grille cover, cylinder head, steering column and clutch cover
plate. Consequently this scene performs zero saved-part isolations; it does not
establish working guest part replacement or driving. The bounded diagnostic
listing is collected after the measured window and does not mutate those parts.

Native camera exceptions and failed Steam initialization remain. Instrumented
host median/P95/max frame intervals were 5.240/16.534/1,364.018 ms; guest intervals
were 11.167/23.106/1,400.974 ms. These pauses do not establish a performance
improvement. MangoHud was disabled only for the diagnostic run. Both test games
were closed and the diagnostic DLL and marker removed; the isolated desktop
copy retains the updated Release DLLs. All 14 personal-save files and four
normal installed mod payload files remain unchanged. Evidence and the scoped
patch are retained under `build/dormant-engine-audit/`.

## September 2026 local display failure and recovery

The local test's Xwayland server reported zero monitors. Temporarily enabling
native player logging in the isolated copy exposed an early
`UnityEngine.Display.RecreateDisplayList` exception and a 0×0 desktop. Active
cameras subsequently became disabled. A separate trace of 95 managed methods
calling the native enabled setter did not find a player-camera disable call.
That temporary trace was removed from the maintained probe.

Launching the same payload and copied profile through a Wine virtual desktop
cleared the display-list exception and camera-direction errors while Steam
remained signed out. The local two-player launcher now uses separate per-role
virtual desktop windows when `xrandr --listmonitors` reports zero. Explicit
`WINTERMP_LOCAL2P_DESKTOP=1/0` overrides are available; normal monitor detection,
failed detection and headless diagnostics retain direct launch. The wrapper
preserves the game arguments, isolated prefix and process lock, including paths
with spaces and relative game-directory overrides. The seven launcher tests
pass, and the final native diagnostic builds without warnings.

In the final 60-second two-player sample, host and guest each reported one
display and a live main player camera after loading GAME. Neither role logged
the display-list exception or `WherePlayerIsLooking` failures. They connected
and applied the join snapshot; all 39 replacement factories still prepared.
Host median/P95/max frame intervals were 6.857/15.203/238.359 ms; guest intervals
were 15.376/25.336/359.281 ms. Earlier runs with disabled cameras are not a
representative gameplay baseline. The reduced maximum pause does not establish
that testers' stutters on other PCs are fixed; substantial pauses, failed Steam
initialization and other native startup/audio errors remain. No two-PC or Steam
latency acceptance was performed.

The maintained live probe now records display count, viewport size and main
camera at startup and sample completion, and captures final camera flags. These
observations sit outside the measured frame window. Evidence and the scoped
patch are under `build/camera-stall-audit/`. Both test games were closed, the
profiler was removed, and the temporary one-byte native logging change was
restored exactly. All 14 personal-save files and four normal mod payload files
remain unchanged. The existing desktop shortcut uses the updated launcher;
the in-game Release payload and protocol 210 are unchanged by this fix.
