# Performance testing

Use timings from the affected build and machine before attributing stutters to
network latency. World discovery runs synchronously, normally every five
seconds. Factory discovery can also run while bindings are still missing.
The existing `WorldSync: ... discovery took ... ms` warnings in the BepInEx log
identify scans over 50 ms; absence of that warning does not prove smooth frames.

## Native discovery benchmark

`tools/GuestSaveProbe` supports `--wintermp-performance-probe` independently of
the save-correctness probe. It runs in the game's actual Unity 5 / Mono runtime
and requires a file named `wintermp-performance-sandbox.txt` in the game-copy
root. Use an isolated game copy and Wine/Proton profile; never mark the normal
installation as a sandbox. The probe is a development tool and must not ship.

Build `tools/GuestSaveProbe/GuestSaveProbe.csproj` with `DeployToGame=false`, then
stage the probe DLL alongside a matching Core, Net, catalog and compatibility
manifest in the isolated copy. Start the copy with `-batchmode -nographics
--wintermp-performance-probe`. Archive the existing `guest-save-probe` directory
before each run. The process exits after writing `result.txt` and
`performance.csv` there; check the result file even if its exit code is zero.

The workload creates 1,000 and then 5,000 FSMs, grouped 25 per branch at six
levels below a test root. One in five is named `Use`; the rest are unrelated
FSMs. It measures native enumeration, path building, parent lookup, the host
world scan with active and disabled FSMs, recurring job/police/stereo/race/banking
discovery, and factory discovery when the catalog's factories are absent.
Each phase warms up three times and records
21 samples after a garbage collection. P95 is the nearest-rank 20th sample.
Collection counts cover the measured samples only. Heap delta is retained heap
growth after any intervening collections, **not allocated bytes**.

Do not run builds or another game process alongside the benchmark. Compare
identical workloads; keep source/payload hashes, catalog, runtime, logs and raw
CSV with the result. The probe also checks indexed catalog rule order, fallback,
path validation and reload behavior in builds that have the index. Readiness
checks verify that pre-Awake action lists are left unloaded and that hooks can
be installed once their state initializes.
Recurring-discovery fixtures also verify all seven systems reject wrong FSM names
and wrong hierarchy paths, then bind after those names and paths become valid
on a later scan. These fixtures run after timing and are destroyed afterward.
The `vehicle-source-same-fsm-name` row selects one extra target among the fixture
FSMs (one in five shares its `Use` name). Source-lookup checks cover live object
and FSM renames, parent changes, duplicate ordinals/components, literal ambiguity,
unusual names, destruction and the lifetime of an active discovery index.

Add `--wintermp-bound-input-performance` to include warmed native engine-reader
fixtures. This optional workload also requires the existing
`guest-engine-input-probe.json` and `camshaft-engine-input-probe.json` captures
in the isolated game root, as used by the corresponding engine-input regression
tests. It measures the distributor/Cylinders, camshaft/Cylinders and
camshaft/Valves fixtures after protection has bound their inputs. The `objects`
column denotes 1,000 repetitions for these rows: validation walks every binding
in that fixture; preparation invokes the normal reader callback. Each still has
three warmups and 21 measured samples, so the full run can take several minutes.
The checks verify repeated calls retain the protected reader targets and saved
part values. This optional workload does not change the default discovery run.

Use `--wintermp-bound-input-breakdown` instead to measure the distributor
fixture alone, with separate action-identity, mount-validation and reader-path
phases, input-value refresh, and full validation/preparation. Component phases
include their reflection invocation overhead and overlap the full-callback
measurement;
do not add them to the full callback time. This retains the same 1,000
repetitions, three warmups and 21 samples per phase.
The default discovery run also includes two 10,000-read snapshot rows:
`guest-component-engine-block-snapshots` retains the defensive-copy API, while
`guest-component-engine-block-input-reads` uses read-only inputs when available
and the defensive API in older binaries. Accessor selection happens before
timing. Native checks cover mutation isolation, revision replacement and clear.

The `engine-source-batch-100x11` row measures 100 fresh host source-lookup
batches, each reading eleven paths in the fixture hierarchy. Each batch resolves
its scene root once in builds with `EngineSourceLookup`; older builds use their
individual native full-path lookups and active-object guard. It includes batch
construction and delegate selection, with three warmups and 21 samples. Checks
cover active sources, inactive children/roots and fresh capture after rename.
This row measures lookup CPU cost, not complete capture, allocation reduction,
or frame rate.

The `guest-protection-ordinary-1000` row isolates ordinary protection bookkeeping
with 0, 32 or 128 registered fixture consumers. Their write/read lists are empty
and an input callback counts visits. Each sample performs 1,000 preparation
passes while discovery is held outside the timed interval. Three warmups and
21 samples report timing and collection counts; these are not whole-engine or
frame costs. Checks cover nested calls, registry changes between passes and
continued protection of other consumers when one callback fails.

The `GamblingSync-discovery` row runs the existing machine-discovery pass in
the same 1,000/5,000-FSM scene. Its fixtures check both statistics variables, all
seven buttons per configured machine and their root transforms. Matching paths
with wrong FSM names and matching names under wrong parents remain unbound; a
later scan binds every inactive fixture after its names and hierarchy are fixed.

The default benchmark also measures 10,000 native action validations and
10,000 pairs of field reads in the `engine-native-action-metadata` and
`engine-native-field-metadata` rows. The action row includes identical reflection
invocation overhead on both builds; field readers use prebuilt delegates.
Eleven checks cover current instance values, replaced/null wrappers, live value
types, wrong/missing fields, action replacement, changed expected types and
counterfeit native names from another assembly, including a null expected name.

The vehicle-state regression suite also accepts `--wintermp-wheel-wait-performance`
with the same performance sandbox marker. It writes `wheel-waiting-performance.csv`
and `wheel-ready-performance.csv` while its four-wheel fixture respectively lacks
and has host tyre health. Each sample performs 1,000 four-wheel lookups or 1,000
whole-fixture protection updates. Discovery is held outside these timed batches,
then its original deadline is restored. Three warmups and 21 measured samples
match the other native benchmarks; these are between-scan costs, not frame times.

This deliberately stresses discovery and missing factories. It does not measure
real-world FPS, rendering, driving physics, the full guest update, Steam latency,
or save-dependent scene density. Confirm the remaining costs in a real host and
guest session before declaring the stutters fixed. A second local instance also
competes for the same CPU/GPU, so it cannot substitute for testing on two PCs.

## September 2026 guest protection allocation reductions

Guest protection now collects pending-binding results only for admission and
activation checks, and applied-binding results only for restoration after a
full scan. The ordinary per-frame pass previously built and grew these sets
only to discard them. Its boolean readiness result, failed-graph pauses,
callback order, registry snapshots and three-second discovery schedule remain
unchanged. Required sets are still local to each call; there is no shared scratch
pool or retained identity cache.

Protection-rule lookup also uses the object name already read in that call to
reject impossible moving-mount matches. Matching names still undergo the same
current-parent/native-ID fallback, full static-path checks and catalog priority.
The change avoids repeatedly marshalling the same Unity name for unrelated
`Data` FSMs. Static discovery snapshots and the moving fallback's live-name
behavior remain distinct.

Core/probe Release builds have zero warnings/errors. **4,293 Net tests** and
all **4,292 preceding native regression cases** pass. Both builds pass the same
**165 native discovery/performance checks**. Ten added checks cover moving
mount/FSM renames, catalog edits, native-ID changes, live fallback names inside
an indexed scan, ordinary consumer counts, nested preparation, registry changes
between passes and containment when an input callback throws.

The new `guest-protection-ordinary-1000` benchmark isolates per-frame bookkeeping
with 0, 32 or 128 registered fixture consumers, empty write/read lists and a
counting input callback. It holds discovery outside the timed interval. These
are not complete engine simulations. Each sample performs 1,000 preparation
calls; three warmups and 21 samples match the existing benchmark. The rule
workloads perform 10,000 lookups per sample:

| Workload | Median before → after | Collections before → after, over 21 samples |
| --- | --- | --- |
| Ordinary preparation, 0 consumers | 2.667 → 0.857 ms | 5 → 2 |
| Ordinary preparation, 32 consumers | 15.544 → 8.521 ms | 16 → 9 |
| Ordinary preparation, 128 consumers | 48.190 → 32.332 ms | 67 → 35 |
| Unrelated `Data` object name | 56.079 → 36.848 ms | 10 → 1 |
| Matching battery name, wrong hierarchy | 276.607 → 247.736 ms | 179 → 167 |
| Matching static writer control | 315.703 → 217.874 ms | 169 → 167 |

The unchanged static-writer control also has a large timing swing, so native
wall-time reductions alone cannot establish the size of the improvement.
Collection counts support reduced allocation pressure in the targeted rows;
retained-heap deltas after intervening collections are not allocated-byte counts.

Four sequential Local2P recordings use the same fixed live probe: separate
before/after heap-detail and frame-only pairs, each with 15 seconds of GAME
warmup and 60 seconds of sampling. Only Core differs within each pair. The
observed guest heap growth per eligible call is:

| Scope | Before → after, bytes/call | Reduction |
| --- | --- | --- |
| Whole frame | 172,141.771 → 163,698.672 | 4.90% |
| Protection rule lookup | 347.286 → 265.247 | 23.62% |
| Protection rule lookup outside scans | 391.863 → 231.131 | 41.02% |
| Protection preparation | 37,508.157 → 35,404.527 | 5.61% |
| Protection reassertion | 26,985.412 → 25,707.152 | 4.74% |

Guest rule-lookup calls increase from 382,963 to 386,875, while their observed
growth falls from 132,997,120 to 102,617,088 bytes. Frame samples increase from
6,426 to 6,558. Host frame growth changes by only 0.43%. These scopes overlap,
include process-wide activity and exclude calls spanning a collection or heap
decrease; they cannot be summed or treated as exact allocated-byte counters.
Scene-path growth per call is essentially unchanged; this change does not cache
paths across callbacks.

Frame-only recordings, without the detailed method hooks, show:

| Role | Frames before → after | Median before → after | P95 before → after | Maximum before → after | Collections before → after |
| --- | --- | --- | --- | --- | --- |
| Host | 8,047 → 8,028 | 6.783 → 6.780 ms | 10.839 → 11.323 ms | 149.893 → 139.536 ms | 7 → 7 |
| Guest | 7,224 → 6,923 | 7.480 → 7.745 ms | 14.691 → 15.690 ms | 162.856 → 170.453 ms | 8 → 8 |

The guest frame-only result is slightly worse; no overall frame-rate or stutter
improvement is established. Heap-detail guest recordings also have eight
collections each; their median/P95 change from 8.237/17.019 to 8.073/16.604 ms.
Every frame above 100 ms in all eight role recordings coincides with a
collection. The allocation reduction is accepted as a targeted efficiency
improvement, while memory-cleanup stalls remain unresolved.

Other work was left untouched. Process snapshots contain two Godot rows at
26.2–99.6% lifetime-average CPU in heap-before, one at 99.3% in heap-after,
none in frames-before and two at 99.1–99.4% in frames-after. These are per-process
lifetime averages, not instantaneous or aggregate load. Together with the
unchanged native control's timing swing, this limits timing comparisons.

All four recordings retain camera readiness, 39 decoded item factories with
no factory decode failures, 24 guest input bindings, five matching cooling
mounts and nine ignition observations. Corris and Sorbet have their host OFF
state applied; inactive Taxi remains safely pending. The same 14 guest waits
remain: ten dormant engines and four unstarted wheel-health consumers. No new
error headers appear relative to the matched baselines or preceding audit.
These checks do not replace a driving or shop interaction test on two PCs.

Evidence is in `build/guest-protection-churn-audit/`: matched payloads and raw
recordings, native regression/performance results, `guest-churn-comparison.json`,
`long-frame-collections.json`, `error-header-comparison.json`,
`final-verification.json`, source snapshots and scoped diffs. All 18 protected
save/installed-payload hashes are unchanged. Both isolated test copies contain
the tested candidate; temporary probes and markers are removed and test games
are closed. Core remains 0.1.33 and protocol 210; no release is created.

## September 2026 host input capture batches

Separate heap/timing scopes now cover the jobs inside `ProcessPendingSpawns`.
The baseline host spends 4,076.232 ms there during a sixty-second recording;
wiring capture accounts for 1,491.370 ms and engine-block capture for
1,838.867 ms. Nested rows overlap; they must not be added to their parent.
Ordinary pending-spawn processing was not itself the expensive operation.

Wiring and engine-block captures now resolve each scene root once per
synchronous batch, then use native relative lookups for its children. The
lookup is a local object passed explicitly through capture functions. It is
never retained across updates, yielded to a snapshot consumer, or shared through
static state. Wiring captures produce their records before returning them to
callers. Original exact-path, duplicate-sibling, active-object, FSM identity,
native readiness, installation and scalar checks remain in place. Publication
revisions, snapshot/delta behavior, polling and message layouts are unchanged.

Core/probe Release builds have zero warnings/errors. **4,293 Net tests**,
**4,292 native regression cases** and **155 native discovery/performance checks**
pass. Both Core binaries pass the same final native probe, preserving all
preceding cases. Twelve new wiring cases cover current names/parents, duplicate
siblings and replacement scene roots. Eight benchmark checks cover eleven
active sources and inactive child/root and rename recovery in both scene sizes.

The new `engine-source-batch-100x11` benchmark performs 100 batches of eleven
lookups per sample, with three warmups and 21 measured samples. It uses the
production batch lookup when available and the preceding native lookup plus
its active-object guard otherwise. Candidate construction/delegate selection
costs are included. This isolates source lookup, not complete capture or FPS:

| Fixture FSMs | Median before → after | P95 before → after | Collections before → after |
| --- | --- | --- | --- |
| 1,000 | 33.001 → 4.135 ms | 33.037 → 4.361 ms | 0 → 0 |
| 5,000 | 192.372 → 18.443 ms | 193.283 → 18.590 ms | 0 → 0 |

Median lookup time falls 87.47% and 90.41%. The candidate allocates temporary
root maps and path substrings; measured retained heap growth is about 5.64 MB
across the 21 larger-fixture samples versus zero reported by the preceding
lookup. This is a CPU optimization, not an allocation reduction. Neither
prefixing individual full paths with `/` nor independently looking up a root
for every individual source improved the native benchmark; those changes were
not retained. Unity's path lookup can return an inactive child, so both benchmark
paths apply the same existing active-object guard as production capture.

The matched live heap recordings each contain 292 complete wiring and engine
captures: 3,212 wiring path checks and 4,088 engine-block path checks in both
builds. Host work decreases without reducing capture frequency or validation:

| Host operation | Eligible total before → after, over sixty seconds | Observed heap growth before → after |
| --- | --- | --- |
| Wiring | 1,491.370 → 200.401 ms | 11,624,448 → 12,365,824 bytes |
| Engine block and associated inputs | 1,838.867 → 193.305 ms | 12,165,120 → 13,045,760 bytes |
| All pending item processing, inclusive | 4,076.232 → 1,149.835 ms | 31,268,864 → 33,107,968 bytes |

Combined wiring/engine time falls **88.18%**; inclusive pending-item time falls
**71.79%**. Their combined observed heap growth increases 1,622,016 bytes per
minute. These process-wide growth measurements are not exact allocations;
parent and child measurements overlap. Host heap-mode collections remain six.
Guest pending-item time is 297.463 → 307.453 ms; the optimized host captures do
not run on guests. Whole-frame mean observed growth falls 3.79% on the host and
rises 1.49% on the guest, which cannot be attributed solely to this change.

The separate frame-only runs contain no detailed timing/heap hooks:

| Role | Median before → after | P95 before → after | Maximum before → after | Collections before → after |
| --- | --- | --- | --- | --- |
| Host | 6.983 → 6.798 ms | 18.486 → 10.806 ms | 133.612 → 130.626 ms | 6 → 7 |
| Guest | 7.447 → 7.485 ms | 14.812 → 14.838 ms | 159.431 → 150.616 ms | 9 → 8 |

Frame counts are 7,445 → 8,015 host and 7,198 → 7,230 guest. Every frame-only
interval of at least 100 ms coincides with a collection: host 6 → 7, guest 9 → 8.
The worst stutters remain. Heap-mode guest P95 instead rises 16.921 → 18.256 ms.
Background Godot CPU samples vary: heap before/after have 2/9 rows ranging
85.6–228% / 95.8–548%; frame-only before/after have 5/12 rows ranging
99–99.6% / 99.2–546%. These are per-process lifetime averages, not instantaneous
or aggregate load; other work was left untouched. The targeted native and live
host improvement is supported, but overall stutter resolution and comparable-load
two-PC driving acceptance are still open.

All four recordings retain the active player camera, 39 decoded replacement
factories without failures, 24 guest engine-input bindings, ten dormant engines
and four wheel-input waits. Both active ignition states and the inactive taxi's
pending ignition match the baseline; all five cooling mounts agree. No error
header is new beyond the current baselines and preceding audit history. Caller
and engine-consumer counters reconcile, and frame-only mode has no detailed
hooks. All 18 protected save/installed-payload files are unchanged. Both isolated
copies contain the accepted build, all test games are closed, and temporary
probes and sandbox markers are removed. Nothing was committed or released.

Evidence: `build/item-update-audit/`, including matched payload/source hashes,
raw recordings and logs, native comparisons, `item-update-comparison.json`,
`final-verification.json`, scoped diff and archived test drivers. Only Core
differs within each matched native/live pair; Net is unchanged. The initial
inactive-child benchmark correction and rejected lookup experiments are archived
separately and are excluded from accepted comparisons.

## September 2026 discovery callers and machine scan

Heap diagnostics now name all Core callers of `ScenePath.ScanFsms`, including
factory, banking, race, shop and other recurring subsystem lookups. Their
method and path-caller rows use the existing nested/exception-safe heap scopes;
all caller partitions still reconcile. This exposed `GamblingSync.Locate` as
the largest of the newly separated discovery costs: twelve passes produced
100,933,632 bytes of observed guest heap growth across ten eligible calls.
The other two contained collections and were excluded. Its path builder was
called 103,680 times during the minute.

Machine discovery now checks the two configured FSM names before building
paths. Statistics and controls still require their exact current paths, all
seven controls per configured machine remain discoverable, and inactive graphs
remain eligible. The five-second discovery budget, binding lifecycle, payments,
ledger behavior and messages are unchanged. There is no retained hierarchy cache.

Core/probe Release builds against the installed game with zero warnings/errors.
**4,293 Net tests**, **4,280 native regression cases** and **147 native discovery
and performance checks** pass. All preceding cases are preserved. Three new
machine-discovery cases verify rejection of wrong names/parents and complete
later binding after both are repaired, including both statistics scalars,
all controls and each machine's root. Both binaries pass the same final native
probe. Synthetic discovery has three warmups and 21 measured scans:

| Fixture FSMs | Median before → after | P95 before → after | Collections across measured batches |
| --- | --- | --- | --- |
| 1,000 | 1.279 → 1.045 ms | 6.522 → 6.351 ms | 3 → 2 |
| 5,000 | 5.052 → 4.622 ms | 12.664 → 12.061 ms | 10 → 8 |

Both live builds still perform twelve machine-discovery passes in sixty
seconds. Ten host scans on each build and ten baseline guest scans are eligible;
all twelve candidate guest scans are eligible. Method measurements overlap
nested path measurements and represent process-wide heap growth, not exact allocations:

| Role | Path calls before → after | Mean growth per eligible scan before → after | Eligible mean duration before → after |
| --- | --- | --- | --- |
| Host | 103,403 → 20,460 | 9,580,544 → 4,883,251 bytes (49.03% lower) | 31.724 → 19.883 ms |
| Guest | 103,680 → 20,724 | 10,093,363 → 4,756,480 bytes (52.88% lower) | 30.845 → 21.665 ms |

Full-session frame results are strongly confounded by other work on this PC.
In heap mode, host/guest frame counts fall 7,221/6,264 → 4,203/3,715 and guest
median time rises 8.213 → 12.316 ms. Mean observed growth per eligible frame
rises 38.36% for the host and 37.41% for the guest, despite the cheaper scan.
Separate frame-only recordings, without detailed diagnostic hooks, move in the
opposite direction:

| Role | Median before → after | P95 before → after | Maximum before → after | Collections before → after |
| --- | --- | --- | --- | --- |
| Host | 10.347 → 6.908 ms | 32.050 → 18.175 ms | 225.219 → 140.725 ms | 5 → 6 |
| Guest | 10.404 → 7.547 ms | 30.468 → 15.538 ms | 254.890 → 137.558 ms | 7 → 8 |

Frame-only guest intervals of at least 100 ms fall 12 → 8; host intervals fall
8 → 6. All candidate intervals of that size coincide with collections. Frame
counts rise 4,336/4,324 → 7,496/7,095. These swings do not establish an overall
stutter improvement: the guest heap recording instead has 8 → 17 such intervals.
Godot process CPU samples range 96.6–548% before and 275–371% after in heap mode,
then 353–441% before and 59.5–100% after in frame-only mode. Only three Godot
rows remain in the candidate frame-only snapshots, versus 21 before. These are
per-process lifetime averages, not instantaneous/aggregate load. Other work was
left untouched. Comparable-load and two-PC driving checks remain necessary.

All four runs retain the active player camera, 39 decoded replacement factories
without failures, 24 guest engine-input bindings, ten dormant engines and four
wheel-input waits. Corris/Sorbet OFF ignition applies, inactive taxi ignition
stays pending, and all five cooling mounts agree. No slot discovery/sync warning
or error appears in either role of any run. The candidate frame-only guest has
a previously known startup cooling-factory wait; it recovers. No error header
is new beyond current baselines and preceding audit history.

`build/discovery-callers-audit/` retains matching payloads, native checks, four
raw live recordings, caller breakdowns, load samples, source snapshots and
scoped diffs. Only Core differs within each native/live comparison; each uses
an identical probe on both sides. The fixed live probe predates the three new
native-only fixture checks; the native pair uses the final probe. All 18
protected save/installed-payload hashes remain unchanged. Test games are closed,
probes/markers removed, and the tested candidate remains in both isolated copies.
Version 0.1.33 and protocol 210 are unchanged; no commit, push or release.

## September 2026 engine action metadata

Guest engine protection now reuses immutable CLR type and field metadata when
validating native actions. The cache contains `Type`, `FieldInfo` and action-name
strings; it retains no Unity objects or field values. Native slots, expected
catalog action names, field types, current values, saved-output aliases and
hierarchy identity are still checked on each call. A missing expected type
cannot authorize a rejected native type. No frame/state-entry guard is deferred
by this change, and the preceding engine-read refresh behavior is unchanged.

Core and the native probe build against the installed game with zero warnings
and errors. **4,293 Net tests**, all **4,280 preceding native regression cases**,
and **144 native performance/readiness checks** pass. The latter includes eleven
metadata cases and passes with both the preceding and candidate Core. The native
before/after pair uses the same final probe; only Core differs. These benchmarks
have three warmups and 21 measured batches of 10,000 operations:

| Workload | Median before → after | Collections across measured batches |
| --- | --- | --- |
| Native action validation | 40.658 → 3.127 ms | 72 → 1 |
| Two native field reads | 8.708 → 2.791 ms | 0 → 0 |

The matched live heap recordings confirm less growth inside guest drivetrain
validation: **10,937.720 → 2,646.060 bytes per eligible call (75.81% lower)**.
Its eligible mean duration is 0.055 → 0.038 ms. Whole recurring protection
growth falls 36,519.582 → 26,270.591 bytes per call, although its mean duration
rises 0.361 → 0.400 ms. Nested method counters overlap and represent observed
process-wide heap growth, not exact per-method allocation totals.

The full-session results do **not** establish a stutter improvement. Guest mean
growth per eligible frame rises 189,646.631 → 204,594.672 bytes in heap mode;
frame counts fall 6,496 → 5,055. Host per-frame growth also rises 17.19%.
The separate frame-only runs have no detailed diagnostic hooks:

| Role | Median before → after | P95 before → after | Maximum before → after | Collections before → after |
| --- | --- | --- | --- | --- |
| Host | 7.592 → 10.465 ms | 19.963 → 31.235 ms | 146.250 → 229.358 ms | 6 → 5 |
| Guest | 8.336 → 11.537 ms | 17.788 → 26.251 ms | 173.763 → 346.346 ms | 9 → 7 |

Guest intervals of at least 100 ms increase 9 → 11 in frame-only mode; only
seven of the latter coincide with collections. Host intervals increase 6 → 7,
with five of the latter coinciding with collections. Frame counts fall
6,517 → 4,423 for the host and 6,063 → 4,281 for the guest. Fewer collections
under a lower frame rate cannot by itself establish fewer stutters.

Other Godot work remained active and was left untouched. Per-process `ps` CPU
samples range 99.4–632% in the baseline heap run and 98.9–667% afterward;
frame-only ranges are 104–500% and 207–362%. These are process lifetime averages,
not instantaneous or aggregate CPU utilization. Workload overlap varies, and
the two game processes share CPU/GPU resources. Both roles slow down, so the
recordings cannot isolate the cause of the overall regression. Retain the
smaller validated metadata work; repeat gameplay comparisons under comparable
load and on two PCs before claiming an overall performance gain.

All four live runs reach the active player camera, decode all 39 replacement
factories without failures, retain 24 guest engine-input bindings and the same
10 dormant/four wheel-input waits, and reconcile all five cooling mounts. The
Corris/Sorbet ignition OFF states apply; inactive taxi ignition remains pending.
Heap caller/consumer partitions reconcile. Each live pair uses identical Net,
probe and supporting files; only Core differs. The fixed live probe predates the
last metadata-fixture assertion, which is unused in live mode; native checks
use the final probe including that assertion.

One additional error appears once in the candidate frame-only guest log at
startup: `UnityEngine.WWW.get_text` from native `WWWPOST.OnUpdate`, before the
SplashScreen → MainMenu transition and well before GAME and sampling. Its cause
is unestablished; it is archived separately, not counted as a clean error-header
comparison or treated as a sampled engine-guard failure. No other new error
headers appear against the matched baselines and preceding audit history.

`build/engine-metadata-audit/` retains raw recordings, native results, matching
payload hashes, source snapshots, scoped diff, load samples, readiness and
error review. All 18 protected personal-save/installed-payload hashes remain
unchanged. Test games are closed and temporary probes/markers removed; the
candidate remains only in the two isolated test copies. Version is still
0.1.33, protocol 210; Net and the wire format are unchanged. No commit or release.

## September 2026 engine input read boundaries

Already-bound guest engine inputs can defer their ordinary per-frame refresh.
Admission, activation, forced checks, state entry and recovery of failed inputs
still prepare the full consumer. Saved-part write suppression and active-action
containment still run each frame. Valve scratch inputs keep their original
refresh path because their native reads are not `GetFsm*` actions.

The native `GetFsmBool/Float/Int/String` read helpers now validate and refresh the
selected input source before it is read. They validate again after refresh to
observe any intervening native callback changes. A remembered readiness flag
never authorizes a native read. Replacement actions bind before reading; retired
callbacks cannot fall through to restored saved targets. Failed or reentrant
reads pause the consumer, including when its native state metadata has changed.
The starter's `Running` every-frame block reader observes host removal without
waiting for periodic preparation or state reentry.

The clean Core/probe Release build has zero warnings/errors, and **4,293 Net
tests** pass. Using the same probe and Net binaries, the preceding Core passes
all 4,268 original native checks and fails all twelve new read-boundary cases;
the candidate passes all **4,280**. Cases cover deferred idle refresh, host
updates/removal between frames, changed consumer/mount identity, saved-output
aliasing, replacement and retired callbacks, changes during refresh, nested read
failure, changed state metadata and running-starter removal. Wire fields and
host-state semantics are unchanged; protocol remains 210 and Net is identical.

Heap mode includes the new `PrepareGuestEngineInputRead` scope when the loaded
Core has it. Compare total frame/guard costs and actual native-read work as well
as `PrepareGuestEngineInputFsm`: preparation moving between entry points must
not be mistaken for a reduction by itself. Consumer CSV rows continue to
partition `PrepareGuestEngineInputFsm` only.

In the matched 60-second heap recordings, guest mean observed growth per
eligible frame falls from **339,814.526 to 190,506.776 bytes (43.94%)**, including
the new native-read checks. Host growth is 106,863.638 versus 109,795.175 bytes.
Guest reassertion mean growth falls from 178,806.745 to 37,781.426 bytes, while
source preparations fall from 166,644 to 21,116 despite more guest frames
(6,100 versus 6,664). The new native-read scope records 459,588 calls, including
unrelated reads, and 20,836,352 observed bytes. Its time/growth overlaps its
nested source validation and refresh; do not add those rows together. These
process-counter differences are inclusive observations, not exact allocations.

The same supporting payloads and probe are used for both sides; only Core
differs. Each frame-only control has no detailed diagnostic hooks:

| Role | Median before → after | P95 before → after | Maximum before → after | Collections before → after |
| --- | --- | --- | --- | --- |
| Host | 6.742 → 6.797 ms | 17.967 → 17.871 ms | 145.625 → 161.913 ms | 6 → 6 |
| Guest | 8.017 → 7.469 ms | 15.604 → 14.983 ms | 139.727 → 137.428 ms | 15 → 9 |

Guest frames increase from 6,661 to 7,165, with intervals of at least 50 ms
falling from 23 to 15. All 15/9 guest intervals of at least 100 ms coincide with
collections. Both heap recordings also have 15/9 such guest intervals, although
the candidate heap run's maximum is higher (195.989 versus 147.690 ms).
This supports retaining the reduced recurring work; long collection-associated
pauses remain. It does not establish two-PC driving performance or guarantee
that the testers' lag is resolved. Godot CPU samples range from 1.7–86.7% before
and 1.1–99.1% after during heap runs; no Godot process appears in the frame-only
load snapshots. Other workload variation and shared CPU/GPU remain limitations.

All four live recordings retain active player cameras, 39 decoded factories
per role, 24 guest input bindings, ten dormant graphs, four wheel-health waits
and the existing ignition outcomes. The per-consumer and caller counters
reconcile, with no late registrations. No candidate error header is added to
its paired baseline; startup starter/cooling waits recover, and all five final
cooling mounts agree. All 14 personal-save and four installed-payload hashes are
unchanged. Both isolated test copies hold the candidate, temporary diagnostics
are removed and test games are closed. Raw results and scoped predecessors are
retained in `build/engine-read-boundary-audit/`.

## September 2026 engine consumer measurements

The opt-in heap probe now separates engine input preparation by consumer,
call-entry activity and recurring/state-entry origin. This is a diagnostic
change: Core and Net remain byte-identical to the accepted vehicle-source-lookup
build below, with protocol 210. No engine validation or refresh scheduling was
relaxed. The Core/probe Release build has zero warnings and errors.

Two 60-second guest recordings identify the same concentration of work. The
first probe was followed by a final version that releases its consumer references
when profiling stops. Their observed growth shares are:

| Consumer | First recording | Final recording |
| --- | --- | --- |
| Cooling | 77.60% | 77.48% |
| Starter | 21.38% | 21.41% |
| All remaining consumers | 1.02% | 1.11% |

These shares are of eligible, inclusive `PrepareGuestEngineInputFsm` heap-growth
observations, **not all game allocation or frame time**. In the final recording,
108,860 calls across 32 consumer identities partition the method's 587,194,368
observed bytes exactly. All identities register during warmup, with no unlisted
calls. Recurring checks account for 475,353,088 bytes; native state-entry checks
account for 111,841,280 bytes. Call totals, eligible/excluded counts and growth
reconcile exactly, and eligible timing reconciles within output rounding.

The expensive consumers are enabled, active, initialized and started. Their
final states are `Coolant temp 2` and `Wait for start`; accepted Corris ignition
remains off. Disabled consumers represent 90,639 calls but only about 1% of this
method's observed growth. All sampled consumer objects are active. The ten
uninitialized dormant graphs remain outside this callback, and the four waiting
wheel readers contribute about 0.16%. Disabling checks solely on inactive or
disabled graphs would therefore miss these hotspots in this workload.

The next optimization needs to address repeated cooling/starter input work.
State entry and actual native reader execution remain essential boundaries:
the starter's direct block reader also runs every frame in `Running`. A blanket
idle throttle or state-entry-only refresh is not justified by this recording.
This diagnostic pass does not establish behavior while cranking or driving.

The old probe was also recorded on the exact same production payloads. Guest
median/P95 intervals were 9.914/20.040 ms with that probe and 14.990/34.708 ms with
the final probe; host intervals were 8.710/22.800 and 9.302/32.366 ms. Other Godot
work varied from 604–647% sampled CPU in the old-probe run to 76.4–721% in the
final run, and both players shared the machine. These measurements expose the
observer/load difference; they do not isolate its cause or establish a speedup.

The final frame-only control installs no detailed method or consumer hooks:

| Role | Median | P95 | Maximum | Collections |
| --- | --- | --- | --- | --- |
| Host | 10.706 ms | 31.971 ms | 198.704 ms | 5 |
| Guest | 11.536 ms | 27.508 ms | 201.069 ms | 12 |

Host/guest intervals of at least 100 ms number six/thirteen; five/twelve coincide
with a collection. One long interval per role has no observed collection.
Competing Godot CPU samples range from 88.8–728%. Stutters remain; this is a
single frame-only control, not a before/after performance improvement claim.

Both probes and the frame-only control retain active player cameras, 39 decoded
factories per role, 24 guest input bindings, ten dormant graphs and four
wheel-health waits. Final cooling mounts agree, Corris/Sorbet ignition applies
off, and the inactive taxi remains pending. Error headers add nothing to the
old-probe baseline; the existing startup starter-input wait recovers before the
final observation. All 14 personal save files and four installed payloads retain
their original hashes. Both isolated copies keep the accepted mod, the temporary
diagnostic plugin/marker is removed, and all test games are closed.

Raw recordings, consumer partitions, payload hashes and scoped predecessors are
in `build/engine-consumer-profile-audit/`.

## September 2026 vehicle source lookup

The shared vehicle source lookup now rejects unrelated object names before
constructing full paths. Possible matches retain exact path and ambiguity checks;
active discovery retains its existing path snapshot. This affects temperature,
thermal/electrical/drivetrain sources and wheel-health capture. Identity caching
retains its existing scope, and readiness gates, wire fields and message semantics
are unchanged. Protocol remains 210; the Net binary is unchanged.

The Core/probe Release build has zero warnings/errors, and all **4,293 Net tests**
pass. Both native binaries pass the same **133 discovery/performance checks**,
retaining the previous 123 plus ten source-lookup cases. All **4,268 native
vehicle/saved-part regression checks** also pass with identical results.

| Fixture FSMs, plus one target | Median before → after | Collections before → after |
| --- | --- | --- |
| 1,000 | 1.522 → 0.082 ms | 1 → 0 |
| 5,000 | 43.088 → 0.422 ms | 17 → 0 |

Each row has three warmups and 21 samples. These fixtures intentionally include
many graphs sharing the same FSM name but different object paths; they do not
measure gameplay FPS or establish a stutter fix. The same probe and supporting
payloads are used for both binaries; only Core differs.

The paired 60-second heap runs perform the same number of source searches:
768 on the host and 140 on the guest. Full path calls inside those searches fall
from **2,440 to 768** on the host and **3,700 to 140** on the guest. Mean observed
heap growth per source search falls from 6,640 to 2,101.333 bytes on the host and
93,330.286 to 3,803.429 bytes on the guest. These are inclusive process-counter
measurements, excluding collection/decreasing intervals, not exact allocated-byte
totals. Whole-frame guest mean growth is 374,538.128 versus 371,895.065 bytes;
this does not establish a repeatable whole-frame improvement.

| Frame-only role | Median before → after | P95 before → after | Maximum before → after | Collections before → after |
| --- | --- | --- | --- | --- |
| Host | 6.773 → 7.034 ms | 18.074 → 18.580 ms | 145.878 → 144.869 ms | 6 → 6 |
| Guest | 8.056 → 8.278 ms | 16.192 → 16.214 ms | 151.249 → 176.437 ms | 15 → 14 |

Detailed hooks are disabled in these frame-only runs. Every interval over 100 ms
coincides with a collection; long pauses remain and no overall speedup is established.
Godot CPU samples range from 540–582% before and 579–593% after during heap runs;
frame-only ranges are 99.5% before and 61.1–104% after. Keep this competing load
and the two-instance workload in mind when comparing timings.

All four live runs retain active player cameras, 39 decoded factories, 24 guest
engine-input bindings and the existing readiness/ignition outcomes. Neither pair
adds a candidate error header. Both frame-only guests have the previously seen
startup cooling-factory wait; all five final cooling mounts agree and cooling is
absent from the remaining failures. The 14 personal saves and four installed
payloads retain their original hashes. Both isolated copies hold the candidate;
temporary diagnostic plugins/markers are removed and test games are closed.

Raw results and scoped predecessors are in `build/vehicle-source-lookup-audit/`.

## September 2026 read-only engine snapshots

Engine readers, valve inputs and heater-hose checks now read an immutable view
of the accepted engine revision. Receiving a packet still takes an owned copy;
callers cannot obtain its mutable arrays. A later accepted revision or clear
leaves previously acquired views unchanged. The existing `Get()` API continues
to return independent mutable copies. No hierarchy validation, native callback
boundary, wire format or message semantics changes; protocol remains 210.

The Core/probe Release build has zero warnings/errors and all **4,293 Net tests**
pass. Six new tests cover every snapshot field, array isolation and bounds,
rejected/replaced revisions, clear, and allocation-free repeated reads.
Both native binaries pass the same **123 discovery/performance checks**, including
three new snapshot cases in the game's Unity 5 / legacy Mono runtime. All **4,268
preceding native regression checks** also pass with identical results.

Across 21 samples of 10,000 reads, the new input path falls from 3.625 to 0.050 ms
median, with collections falling from 13 to zero and zero retained heap growth
in the candidate row. The defensive-copy control still collects 13 times in both
binaries. These are isolated operations, not frame-rate or stutter measurements.
The same probe and supporting payloads are used; only Core and Net differ.

Live heap recordings reduce mean observed growth inside
`UpdateGuestEngineInputValues` from **187.255 to 1.302 bytes per eligible call**
(99.3%). Calls number 122,844 versus 81,817. This is an inclusive process-counter
measurement, excluding collection/decreasing intervals, not an exact allocated-byte
total. Whole-frame mean growth rises from 372,277.686 to 471,632.884 bytes on the
guest while its frame count falls from 4,275 to 2,579. Competing Godot CPU samples
range from 211–359% before to 309–440% after; the heap runs cannot establish an
overall frame-time improvement.

Frame-only recordings, with detailed hooks disabled, show:

| Role | Median before → after | P95 before → after | Maximum before → after | Collections before → after |
| --- | --- | --- | --- | --- |
| Host | 9.288 → 6.841 ms | 22.302 → 17.979 ms | 178.996 → 143.217 ms | 5 → 6 |
| Guest | 10.952 → 8.343 ms | 22.253 → 16.463 ms | 177.354 → 143.659 ms | 12 → 14 |

Background Godot CPU samples also differ substantially in these runs: 708–845%
before versus 114–482% after. These timings do **not** establish an overall speedup.
Every frame-only interval above 100 ms coincides with a collection; longer pauses
remain. Instrumented heap recordings also contain long intervals without collections.

All four live runs retain active player cameras, 39 decoded factories, 24 guest
engine-input bindings and the existing readiness/ignition outcomes. Neither pair
adds a candidate error header. Both heap guests report the same native `WWWPOST`
startup error, absent from the preceding audit. The 14 personal saves and four
installed payload files retain their original hashes. Both isolated copies hold
the candidate, with diagnostic plugins and markers removed and test games closed.

Raw results and scoped predecessors are in `build/engine-snapshot-read-audit/`.

## Earlier measurements

[Earlier optimization reports](PERFORMANCE-OPTIMIZATIONS.md) record the discovery,
engine-input, path-lookup and paused-entry changes, with their tests and limits.

[Earlier session audits](PERFORMANCE-SESSION-AUDITS.md) record the live-session
repairs, collection controls, discovery scheduling and vehicle filters.

## Live session profiling

The same development probe supports `--wintermp-live-performance-probe` in a
windowed isolated game copy. It additionally requires
`wintermp-live-performance-sandbox.txt` in that copy's root. With the diagnostic
DLL staged, `WINTERMP_LOCAL2P_PROFILE=1 tools/local2p-test.sh` passes the flag to
both roles; explicitly set `WINTERMP_GAME_DIR` and
`WINTERMP_COMPAT_DATA_PATH` to the isolated game and profile. The installed
desktop shortcut already supplies those isolated paths. Ordinary desktop setup
does not install the diagnostic DLL or enable profiling.

Use `WINTERMP_LOCAL2P_PROFILE=frames` for a lightweight control. It passes
`--wintermp-live-performance-frames-only` alongside the live-probe flag to both
roles and skips all method, interaction, camera-write and engine-failure
observation hooks. It still records frame intervals and collection counts,
with display/camera/readiness snapshots outside the sample. Its methods CSV
contains only the header. `WINTERMP_LOCAL2P_PROFILE=0` (the default) disables
live profiling; invalid values fail before either game starts. Compare the
frame-only and detailed modes on the same payload/profile to check diagnostic
overhead before attributing inclusive method timings to gameplay cost.

Use `WINTERMP_LOCAL2P_PROFILE=heap` for selected method timings and observed
managed-heap growth. It passes `--wintermp-live-performance-heap` to both games,
uses the same isolated-copy marker, and omits interaction/camera diagnostic
hooks. At startup it checks that the native counter sees a retained 256 KiB
allocation and detects a forced collection; calibration runs before GAME
warmup. The resulting `<role>-heap.csv` includes per-frame and per-method
growth, eligible call counts, and calls excluded because a collection occurred
or the counter decreased. `eligible_total_ms` and `eligible_mean_ms` report
timings for those same eligible calls; the methods CSV still includes every
call, including collection pauses. Calibration also checks that collected
calls leave eligible timing totals unchanged. Normal launch and frame-only
mode do not run these heap measurements.

`GuestEngineProtection.FindRuleOutsideScan` and `ScenePath.OfOutsideScan` are
subsets of the corresponding method rows, selected when no discovery path index
is active. Use them to distinguish regular engine validation from scheduled
scan work when recordings have different frame counts. These subsets appear
only in the heap CSV and must not be added to their parent method totals.

`GuestEngineProtection.PrepareCoreForced` is a subset of `PrepareCore`;
`GuestEngineProtection.PrepareCoreFromElectricity` is its forced subset inside
`ApplyRemoteElectricity`. Both use the same sample as their parent, including
throwing calls. Observations also report how many frames contain those
electricity preparations, their maximum count per frame, and the final scope
depth (expected zero). Heap mode additionally measures `ApplyRemoteElectricity`,
`EnsureVehicleSystemsProbe` and `RefreshNativeRpmBinding`. Frame-only and normal
play do not install these hooks.

The native read-source breakdown additionally measures `IsBatteryReadSource`,
`IsHeaterReadSource`, `FindWireReadSource`, `FindHeaterHoseReadSource` and
`ProjectRearWindowRead`. Their nested path calls appear under these methods in
the caller CSV. A remembered source can return without a path lookup.

Heap mode also writes `<role>-heap-callers.csv`, partitioning path and engine-rule
lookups by their nearest enclosing instrumented method. This is not necessarily
the immediate C# caller: `root` means no instrumented method encloses the call.
Each lookup has total and `OutsideScan` rows using the same samples as the main
heap CSV. Caller totals must reconcile with their method totals; the outside-scan
partition must reconcile separately. Do not add the two partitions together.
Caller storage is allocated before sampling, uses no per-call stack trace, and
restores its thread-local scope in the finalizer even for unsampled or throwing
calls. Startup checks cover nested, recursive and unsampled scope restoration;
the final observation should report `Heap caller final scope=root`.

Heap mode additionally writes `<role>-engine-inputs.csv`. Its rows partition
`PrepareGuestEngineInputFsm` by managed consumer identity, the nearest recurring
reassertion or state-entry guard, and the consumer's live/enabled/object-active/
initialized/started flags at call entry. Each bucket uses the parent method's
same interval, including throwing calls; its call, eligible-growth and excluded
call totals must reconcile exactly. Timing totals reconcile within CSV rounding.
These activity flags do not establish whether an engine is running. Paths, FSM
names and state names describe the end of the sample; the numeric consumer ID
distinguishes instances even if their labels coincide or change.

The probe registers at most 256 consumer identities, normally during warmup,
preallocating every activity/origin bucket for each, and releases those game
references when profiling stops. Observations report late
registrations and calls exceeding the limit or using a null consumer; those
calls remain in an explicit `unlisted` bucket. Registration and activity reads
precede the method's measurement interval but remain in enclosing methods and
frame costs. No path lookup, state-name lookup or stack trace is added to each
call. Scope calibration covers recurring, entry, recursive, throwing and
unsampled calls. Final origin should be `other`. Compare the previous probe on
the same production binaries to assess observer overhead, and retain a
frame-only control, which installs none of these hooks or counters.

These are process-wide `GC.GetTotalMemory(false)` differences, **not exact
allocated-byte counts**. Counter granularity, other threads, excluded collection
intervals and instrumentation affect the totals. Nested method measurements
overlap and must not be added together. Use them to locate repeated temporary
memory growth, then confirm changes with the same workload and frame-only mode.
Heap finalizers observe throwing calls without suppressing their exceptions.

After 15 seconds in GAME, the probe samples for at least 60 seconds, then
unpatches itself. It leaves the games running. Results in `live-performance/`
include role-specific method timings, frame intervals and camera observations.
Timings are inclusive, overlap between nested methods and include Harmony
instrumentation overhead. Frame intervals measure time between probe updates,
not GPU presentation. Generation-zero collections cover the sample window,
ending before result formatting and the final scene observations.
Detailed method selection includes `Process*` and `Isolate*` calls as well as
updates, scans, refresh/preparation, and world readiness/level checks. Omitting
those methods previously hid most of the guest's belt-discovery cost inside the
inclusive world-update total.
`<role>-frame-events.csv` records elapsed sample seconds, frame interval and
collection-count change for every interval of at least 50 ms or containing a
collection. Events accumulate in memory; the CSV is written after sampling.
Detailed mode also writes `<role>-slow-calls.csv` with start/end seconds,
method, duration and collection-count change for calls taking at least 5 ms.
It retains at most 4,096 records and reports dropped records in observations.
Timings use void Harmony finalizers so throwing callbacks are observed without
suppressing their exceptions. Nested calls overlap; their durations and
collection counts must not be summed. Frame-only and heap mode leave this
timeline empty. The expanded detailed instrumentation adds overhead, so compare
identical diagnostic payloads and retain a frame-only control.
Co-occurrence does not measure GC duration or identify which code allocated
the collected memory. A long inclusive method timing can include a collection
or scheduling pause, so it is not sufficient evidence of expensive method logic.
Archive the directory between runs, close both games and remove the diagnostic
DLL and sandbox marker after testing. Never ship the probe.

For a solo control, `--wintermp-live-performance-solo` alongside the live probe
flag allows FastBoot's native Continue gesture while multiplayer remains idle.
Core and FastBoot are still loaded: this is not an unmodded control. The probe
observes camera bindings and native camera writes without suppressing their
exceptions. The solo flag changes only the development menu admission gate.
