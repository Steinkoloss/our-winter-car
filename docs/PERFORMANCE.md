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

Measurement history (September 2026 optimization and session audits) was removed
on 2026-09-22; see the [snapshot](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/PERFORMANCE.md).

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
