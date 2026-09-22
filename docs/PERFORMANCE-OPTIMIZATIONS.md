# Earlier performance optimizations

For current profiling instructions and the latest measurements, see
[Performance testing](PERFORMANCE.md).

## September 2026 discovery fix

Factory scans now reject unrelated FSM names before walking hierarchies or
comparing every factory rule. Generic catalog matching groups rules by FSM name
while retaining their original priority and all path/state checks. Disabled
world FSMs are rejected before part-parent lookup; that lookup is performed only
when guest save isolation can actually defer discovery. No hierarchy identity is
cached between scans, and no discovery retries or save-protection checks are
removed.

The controlled development-build comparison at 5,000 test FSMs reduced median
active host discovery from 78.007 ms to 22.501 ms (P95 84.203 to 28.918 ms), and
disabled discovery from 48.059 ms to 7.336 ms. Package/replacement discovery
medians fell from 17.106/18.174 ms to 0.900/0.830 ms. Across 21 active scans,
generation-zero collections fell from 61 to 7. These are synthetic native
runtime measurements, not promised FPS gains. Raw evidence is retained locally
under `build/performance-stutter-audit/`.

A separate run using copies of this PC's installed 0.1.32/protocol-118 payload
measured 38.621 ms median / 45.129 ms P95 for the same 5,000-FSM active scan.
That build has fewer features and factory rules, so the direct optimization
comparison above uses the unchanged development workload on both sides.
The normal installation was not modified for these measurements.

The subsequent local two-player run also exposed repeated reads of uninitialized
PlayMaker state actions. That getter logs several Unity errors before attempting
deserialization and throwing, even when the caller catches the exception.
State-entry hooks and banking now defer before that getter; vehicle and guest
engine validators reject uninitialized actions through their existing failure
paths. Guest write protection remains in place while validation is pending.
World discovery also defers FSMs that have not initialized and started. These
checks prevent avoidable error output; they do not suppress Unity logging.

## September 2026 recurring discovery fix

Jobs, police, home stereo, rally, ice race and banking now check candidate FSM
names before building hierarchy paths. Rally and banking take those names from
the current catalog. Candidate objects still undergo the original path,
variable and checkpoint-layout checks; discovery intervals and retries are
unchanged. This introduces no cross-frame path cache or wire-format change.

The controlled before/after comparison uses the same Release build settings,
probe, catalog and native Unity/Mono workload. At 5,000 fixture FSMs:

| Discovery | Median before → after (ms) | P95 before → after (ms) | Gen-0 collections before → after |
| --- | --- | --- | --- |
| Police | 5.279 → 0.716 | 11.286 → 0.938 | 9 → 0 |
| Home stereo | 4.425 → 3.285 | 10.394 → 9.025 | 9 → 6 |
| Rally | 4.945 → 0.713 | 11.023 → 0.725 | 9 → 0 |
| Ice race | 4.493 → 0.712 | 10.601 → 0.831 | 9 → 0 |
| Jobs | 8.097 → 0.747 | 14.946 → 0.763 | 9 → 0 |
| Banking | 4.427 → 3.272 | 10.491 → 9.187 | 9 → 6 |

Collection counts cover 21 measured scans per row. The fixture deliberately
includes 1,000 `Use` FSMs, which remain candidates for stereo and banking and
still require path checks. These timings measure discovery, not overall FPS.
All 37 native performance/readiness checks pass before and after, including 18
new discovery checks; the 4,191-case native multiplayer regression suite also
passes with every predecessor case retained. The Core/probe Release build has
zero warnings or errors. Raw results, payload hashes and the scoped source diff
are retained locally under `build/periodic-discovery-audit/`.

The subsequent 60-second windowed local host/guest run connected, applied the
join snapshot and retained working player cameras. Both roles found 39
replacement factories with zero decode failures; the guest retained 24 engine
input bindings and the same 10 dormant-engine plus four wheel-health waits.
There were no camera-direction errors or scan-failure logs. Host/guest frame
interval medians were 7.356/18.077 ms, P95 19.019/36.262 ms, and maxima
179.881/348.646 ms, with 8/17 generation-zero collections. The previous camera
verification run had medians 6.857/15.376 ms and P95 15.203/25.336 ms: these
short local recordings do **not** establish an overall FPS improvement.
Substantial pauses remain, and guest engine validation is still a major
inclusive cost. Diagnostic plugins were removed after testing; the optimized
payload is staged only in the isolated local test copy. Normal installed
payloads and all 14 checked save files remain unchanged.

## September 2026 bound guest-reader validation fix

Bound guest engine readers now retain their immutable field descriptors instead
of looking up `fsmName`, `variableName`, `everyFrame` and `storeValue` on every
validation. Their field contents and output references are still read and
checked each time. Rebinding a replaced native action resolves new descriptors.
The variable-count check reuses a per-binding scratch set, clears it each time,
and recalculates the expected shape from the current catalog. It does not cache
successful validation or skip checks after a failure. No protocol change.

The native comparison uses byte-identical benchmark, Net and captured fixture
inputs, with only Core changed. These are medians in milliseconds per 1,000
repetitions, with 21 measured samples:

| Fixture | Validation before → after | Full reader callback before → after |
| --- | --- | --- |
| Distributor / Cylinders | 703.197 → 551.408 | 1164.881 → 1083.594 |
| Camshaft / Cylinders | 618.614 → 534.663 | 1150.308 → 1040.985 |
| Camshaft / Valves | 82.990 → 52.446 | 177.533 → 146.927 |

The Cylinder fixtures include their other bound input sources. These numbers
are neither time per rendered frame nor isolated timing of one GetFsm action.
Validation generation-zero collections fell from 376/313/22 to 350/289/16
across the three fixtures; callback collections fell from 844/704/82 to
817/676/76. Significant allocation and validation costs remain.

The Release build is clean. All 40 performance/readiness checks pass on both
payloads. All 4,196 multiplayer regression checks pass, retaining the prior
4,191 and adding five cases covering in-place field edits, duplicate variables,
catalog growth, unexpected proxy variables and recovery on the same binding.
Raw timings, input hashes, native results and the scoped diff are under
`build/guest-validation-audit/`. Synthetic gains require live and two-PC
confirmation before claiming smoother gameplay.

The following 60-second local session connected and applied its snapshot with
working cameras, 39 replacement factories and zero decode failures on each
role. Guest inputs retained 24 bindings and the same ten dormant-engine/four
wheel-health waits. There were no camera-direction errors or scan failures.
Host/guest frame interval medians were 6.766/15.679 ms, P95 15.691/25.797 ms,
maxima 166.395/285.927 ms, and generation-zero collections 8/20. The preceding
discovery-fix sample had guest median/P95 18.077/36.262 ms, but earlier samples
were also around 15/25 ms: this short run does not prove an overall FPS gain.
Guest preparation still averages 1.597 ms per call under inclusive diagnostic
instrumentation, and significant pauses remain. The optimized payload is
staged in Local 2P Test; temporary diagnostics were removed and normal installed
payload/save hashes remain unchanged.

## September 2026 slot-list metadata fix

Detailed native measurement found mount validation accounted for about three
quarters of warmed distributor-fixture validation time. Guest slot lookup
repeatedly inspected the assembly and members of every slot-list component.
It now retains only the immutable CLR type metadata: whether the type is the
native `PlayMakerArrayListProxy`, and the descriptors for `referenceName` and
`arrayList`. The cache holds no Unity instances or slot contents. Each call
still enumerates current components and checks the current database path,
reference names, list values, shape, duplicate slots and requested mount.

The same native breakdown workload and byte-identical probe/Net/input captures
measured the following medians per 1,000 repetitions; only Core changed:

| Phase | Before → after (ms) | Gen-0 collections before → after |
| --- | --- | --- |
| Mount validation | 410.804 → 233.720 | 301 → 130 |
| Reader paths | 58.672 → 57.991 | 41 → 41 |
| Full validation | 551.780 → 369.718 | 350 → 178 |
| Full reader callback | 1076.332 → 882.266 | 816 → 641 |

Collection counts cover all 21 measured samples. The fixture includes the
other sources bound to its Cylinders consumer. Path handling and validation
cadence are unchanged; no successful validation result is reused and there is
no protocol change. Both payloads pass all 38 breakdown/readiness checks.
The Release build has zero warnings/errors, and all 4,199 native multiplayer
checks pass, retaining the prior 4,196 plus renamed, replaced and null slot-list
recovery cases. Raw evidence is retained under
`build/guest-mount-validation-audit/`.

The following 60-second windowed local session connected and applied its
snapshot, retaining working cameras, 39 factories with zero decode failures
on each role, and 24 guest input bindings. The existing ten dormant-engine and
four wheel-health waits remain. No camera-direction errors or scan failures
were logged. Host/guest frame interval medians were 6.928/15.951 ms, P95
16.133/26.069 ms, maxima 183.259/273.861 ms, and generation-zero collections
8/19. This is broadly similar to the preceding run's guest median/P95
15.679/25.797 ms; it does not establish smoother gameplay. Inclusive guest
preparation averaged 1.493 ms per call (previously 1.597 ms), but significant
pauses remain. The new payload is staged in Local 2P Test. Both test processes
were closed and temporary profilers removed; normal installed payloads and
all 14 checked save files remain unchanged.

## September 2026 guest input allocation fixes

Separate measurements recorded 457 collections during input-value refresh and
643 during the full reader callback in the warmed distributor fixture.
Every mount checked all candidate replacement IDs through a defensive getter,
copying each state and its arrays before rejecting parts at other mounts.
`ReplacementPartReplica.GetAttached` now checks retirement and the exact live
attachment address before making the defensive copy. The engine reader uses
this filtered lookup. Matching states remain independently owned snapshots;
candidate enumeration, duplicate-occupant rejection, pending application,
family/identity checks and native mount validation are unchanged. There is no
cross-frame cache and no wire or protocol change.

The same native fixture and byte-identical probe measured these medians per
1,000 repetitions, with three warmups and 21 measured samples per phase:

| Phase | Median before → after (ms) | Gen-0 collections before → after |
| --- | --- | --- |
| Input-value refresh | 485.469 → 403.283 | 457 → 329 |
| Full validation | 369.609 → 369.071 | 178 → 178 |
| Full reader callback | 883.483 → 798.204 | 643 → 515 |

This reduces value-refresh time by 16.9% and its collection count by 28.0%;
the full callback takes 9.7% less time with 19.9% fewer collections. Counts
cover all 21 samples and the fixture's bound sources, not collections per
rendered frame. Collection count is not allocated bytes or GC pause duration.
These controlled measurements do not establish an overall FPS improvement.

Engine-state snapshots also allocated three default arrays during construction
and immediately replaced them with copies. `EngineBlockState.Copy` now clones
the scalar fields without running those initializers, then independently clones
each of its three mutable arrays. Existing null-to-empty normalization is
preserved. No received state or array is shared with callers. With the part
lookup fix already present on both sides and a byte-identical probe, 10,000
native replica reads took 2.440 ms median before and 1.458 ms after; collections
across 21 samples fell from 10 to 6. This is a 40.2% timing reduction and 40%
fewer collections for snapshot copying. The full distributor callback remained
similar at 799.557 versus 802.920 ms and 517 versus 514 collections; this does
not establish an additional overall callback improvement.

The archived initial snapshot benchmark labeled the row's count as 1,000 even
though it performed 10,000 reads. The corrected helper reports 10,000; original
recordings remain unchanged and `snapshot-comparison.json` records the actual
repetitions. The correction changes reporting only.

The Release Core/probe build has zero warnings/errors. All 4,248 protocol and
replica tests pass. Nine new attachment lookup cases cover exact
mounts, mismatches, loose/unattached records, defensive scalar ownership,
accepted movement, stale updates, retirement and reset. Seven further snapshot
cases cover populated wire fields, all three arrays' independent ownership and
malformed array normalization. All 4,199 native
multiplayer cases remain and pass, including pending duplicate occupants and
saved-part protection. All four native performance runs pass all 38 readiness
checks. Raw results, payload hashes and the scoped changes are retained under
`build/guest-input-allocation-audit/`.

The final lightweight 60-second local session retained working cameras, 39
decoded factories with zero failures on each role, and 24 guest input bindings.
The ten dormant-engine and four wheel-health waits remain. Host median/P95/max
frame intervals were 7.034/17.921/155.614 ms; guest intervals were
15.345/25.395/270.456 ms, with 8/21 collections. The preceding frame-only control
had guest intervals of 15.223/25.350/260.856 ms and 21 collections. Substantial
pauses remain; these idle local recordings do not establish smoother gameplay.
The guest had zero materialized replacement parts, so this scene does not
exercise the candidate-copy savings measured in the populated native fixture.
Both games were closed and temporary diagnostic plugins removed. The checked
build is staged in Local 2P Test, with normal installed payloads and all 14
checked personal-save files unchanged.

## September 2026 per-frame belt discovery fix

Expanded live profiling found `IsolatePartBeltSources` repeatedly searching every
PlayMaker object for an unavailable local belt animation. Guest copies already
receive their scroll rate from the host; this local lookup supplied no guest
presentation input. `GetPartBeltSource` now performs it only for host capture.
Guest belt validation, native wear/event containment, saved display restoration
and projection into the copy's owned material remain intact. No wire change.

The preceding native wheel-wait benchmark measured 30.785 ms with pending host
health versus 20.137 ms with ready health per 1,000 four-wheel preparation calls:
only about 0.011 ms extra per call. That waiting path was left unchanged.
The expanded 60-second live sample instead measured 7.183 ms per guest belt
isolation call, totaling 17.030 seconds within 27.670 seconds of world updates.
Method timings are inclusive; unrelated CPU-intensive work was present, so
these values locate the repeated work rather than establish an FPS comparison.
After the fix, the same expanded instrumentation measured 20.086 ms across
5,574 guest belt-isolation calls: about 0.004 ms per call. The world-update mean
was 2.556 ms versus 11.730 ms in the earlier sample. This removes the repeated
discovery cost, but long frames still occurred: the fixed guest's maximum was
277.831 ms. Detailed-run frame timing alone cannot establish gameplay FPS gains.

Four new native checks cover ready, absent and malformed guest-local scroll
inputs, host-rate material projection, containment/restoration, and the retained
host binding. Two fail against the preceding code and pass with the fix.
All 4,203 native regression checks pass, retaining every previous 4,199 check;
the Core/probe Release build has zero warnings or errors. Raw native results,
live recordings, payload hashes and the scoped change are retained locally
under `build/guest-wait-allocation-audit/`.

The subsequent frame-only comparison used identical diagnostic, Net, catalog
and FastBoot files, changing only Core. Each role sampled 60 seconds after its
15-second GAME warmup:

| Role | Median before → after (ms) | P95 before → after (ms) | Maximum before → after (ms) |
| --- | --- | --- | --- |
| Host | 6.666 → 6.761 | 14.281 → 14.591 | 199.922 → 157.486 |
| Guest | 15.281 → 8.972 | 25.342 → 16.425 | 288.459 → 282.538 |

Guest steady-frame timing improved in this local workload, with roughly
unchanged host median/P95. This does **not** resolve the reported stutters:
guest intervals of at least 50 ms numbered 48 before and 50 after, and
generation-zero collections numbered 21 and 26. More frames ran in the same
minute (3,396 versus 5,708), so absolute collection counts alone do not measure
allocation per update. Unrelated CPU-intensive work remained present; two
instances shared the machine. These are local development measurements, not
promised FPS or two-PC gameplay acceptance.

Both recordings retained working player cameras, 39 decoded factories with zero
failures per role, 24 guest input bindings and the existing ten dormant-engine
plus four wheel-health waits. Both games were closed and temporary diagnostics
removed. The checked fix is staged in Local 2P Test; the normal installed
payload and all 14 checked personal-save files remain unchanged.

## September 2026 heap-growth investigation

The new opt-in heap mode below identified substantial repeated path construction
in the guest engine guards. In a 60-second idle local session, `ScenePath.Of`
ran 1,078,039 times. Its inclusive eligible heap-growth observations totaled
about 2.67 GB, against 3.57 GB across eligible frame intervals. These are
process-wide counter differences, not exact allocations attributable to that
method; nested rows overlap and collection intervals are excluded.

A direct live path comparison was tested, including duplicate sibling names,
literal separators and current hierarchy changes. It retained all native
regression cases but did not earn a production change. After two revisions,
the guest's mean observed heap growth per eligible frame was 635,994 bytes
versus 638,960 before: less than 0.5% lower. Rule lookup became cheaper, but
engine reader preparation became slower. Fewer calls to the path renderer
alone therefore overstated the benefit.

The final frame-only control confirmed the reason to reject the experiment:

| Role | Median before → candidate (ms) | P95 before → candidate (ms) | Maximum before → candidate (ms) |
| --- | --- | --- | --- |
| Host | 6.743 → 6.711 | 13.919 → 13.500 | 198.667 → 199.621 |
| Guest | 8.823 → 9.222 | 16.059 → 16.811 | 264.470 → 252.974 |

Guest intervals of at least 50 ms numbered 55 before and 53 with the candidate;
27 and 25 coincided with a collection. This does not measure collection duration
or establish a reduction in stutters. Both game instances shared the machine
with other CPU-intensive processes. Each run retained working player cameras,
39 decoded factories with zero failures per role, 24 guest input bindings and
the existing ten dormant-engine plus four wheel-health waits.

The direct matcher and its call-site changes were removed. The retained Core
and Net rebuilds are byte-for-byte identical to the preceding belt-discovery
fix. The new heap profiler, Linux launch mode and its launcher regression test
remain. The retained code passes 4,248 Net tests and all ten Linux launcher
tests; the Core/probe Release build has zero warnings or errors. This work
improves diagnostics, with no claimed gameplay performance gain. Raw recordings,
counter reconciliation, rejected source, payload hashes and the decision are
retained locally under `build/guest-heap-audit/`.

Repeating the native suite exposed a stale destination file in the probe's
host-rename fixture: later guest checks intentionally leave that file present.
The host check now removes its own previous destination before testing native
rename. All 4,203 native checks pass on two consecutive runs without resetting
the test directory. Temporary diagnostic plugins were removed and both games
closed. Local 2P Test retains the preceding build; the normal installed payload
and all 14 checked personal-save files are unchanged.

## September 2026 direct relative-mount lookup

`ScenePath.FindRelative` now resolves each path segment with one live sibling
pass. Previously, each call created a `ScenePathCache`, indexed every sibling
name into temporary dictionaries, then enumerated the siblings again to find
the requested child. The new `ScenePathLookup` retains only its accessors;
renames, reparenting and duplicate changes are observed on every call. Full
path generation and the shared index used for whole-world discovery are
unchanged. Hand interaction and part attachment use this relative lookup.

Duplicate ordinals remain identical to the sender's path format. A lone child
has no `[0]` alias, and a literal bracket name colliding with an indexed sibling
still makes the mount unresolved. Invalid paths retain the same validation.
This is an implementation optimization with no wire-format or protocol change.

Ten new Net cases compare resolution with the preceding implementation across
literal names, nested duplicates, invalid paths, three cultures and live
hierarchy changes. A wide-tree check verifies one read per sibling. All 4,258
Net tests and 4,203 existing native multiplayer checks pass. The Core/probe
Release build has zero warnings or errors. Both native benchmark runs pass
all 45 readiness checks, including eight new mount-resolution checks.

The native Unity/Mono benchmark resolves a three-level path 1,000 times per
sample, with 21 measured samples after warmup. Each level has the requested
mount plus the listed number of unrelated siblings:

| Unrelated siblings per level | Median before → after (ms per 1,000 lookups) | Collections before → after (all 21 samples) |
| --- | --- | --- |
| 32 | 54.121 → 18.355 | 98 → 16 |
| 256 | 558.202 → 142.768 | 1,141 → 128 |

These measurements isolate lookup work in the real game runtime; they do not
establish overall FPS or resolve the reported stutters. Raw results and the
scoped source change are retained locally under `build/relative-lookup-audit/`.

In the paired 60-second live heap recordings, the guest still performed four
relative lookups per frame. Their inclusive mean duration fell from 0.041 to
0.017 ms, and observed heap growth per eligible lookup fell from 13,166 to
1,828 bytes. Per eligible frame, observed growth fell from 648,050 to 606,617
bytes (about 6.4%). These are the profiler's process-wide heap differences,
not exact allocated-byte counts. Both samples used the same diagnostic DLL;
the unchanged lookup count helps distinguish reduced work from skipped checks.
Long pauses remained, with guest maxima of 273.596 and 278.486 ms.

The subsequent frame-only controls omitted method/heap instrumentation and
sampled 60 seconds after the same 15-second GAME warmup:

| Role | Median before → after (ms) | P95 before → after (ms) | Maximum before → after (ms) |
| --- | --- | --- | --- |
| Host | 6.843 → 6.690 | 14.375 → 13.113 | 195.025 → 205.058 |
| Guest | 8.899 → 8.649 | 16.348 → 15.577 | 256.112 → 261.722 |

Guest intervals of at least 50 ms numbered 56 and 53, with 27 and 26
collections respectively. Both median/P95 results improved slightly, but the
small differences cannot establish an FPS gain under shared CPU load. The
specific lookup's time and memory reductions are clearer; long pauses still
need investigation. All live runs retained working cameras, 39 decoded
factories with zero failures per role, 24 guest input bindings and the existing
ten dormant-engine plus four wheel-health waits. Both games were closed and
temporary diagnostic plugins removed. The checked Core/Net pair is staged in
Local 2P Test; all 14 checked personal-save files and the normal installed
payload remain unchanged.

## September 2026 spreading routine discovery

A detailed call timeline showed the five-second world scan followed immediately
by banking, jobs, mail orders, police, stereo, race, results and other object
discovery. One guest update took 232.809 ms without a collection: world discovery
occupied 95.202 ms, followed by several separate scans taking 6–33 ms each.
These are inclusive instrumented timings; frame-only controls are needed to
measure the player's overall pauses.

The selected routine scans now share a `PeriodicDiscoveryBudget`: one due scan
may start in a Unity frame. A deferred scan keeps its original deadline, then
reschedules its five-second interval only when it actually runs. Initial
discovery and existing explicit refreshes stay immediate. The world scan's
internal FSM/item/NPC discovery remains synchronous, preserving its complete
ID-hash update. Guest damage and engine protection retain their existing
immediate checks. This changes discovery scheduling, not wire messages or
authoritative state application; the protocol remains unchanged.

Seven new Net tests cover simultaneous deadlines, progress in update order,
preserved deferred deadlines, startup, forced refreshes, reset and frame-number
wrap. All **4,265 Net tests** pass. Core and the diagnostic probe build against
the installed game assemblies with zero warnings or errors. Native checks pass
**55/55 discovery/performance cases** (including ten new scheduling checks) and
**4,203/4,203 existing regression cases**. The latter retain every previous
case. Native checks also confirm that deferral leaves bindings untouched and
that later discovery still sees rename/reparent changes.

The detailed after timeline shows secondary discovery on separate frames;
both roles still perform 12 world scans over the sample. Each of the seven
instrumented routine `Scan` methods also has 12 calls exceeding 5 ms. No timeline
records were dropped. These observations demonstrate scheduling and continued
discovery, rather than an FPS improvement: detailed instrumentation is costly
and the background CPU workload differed between recordings.

The first frame-only pair was confounded by unrelated CPU work. The baseline
was mostly quiet after startup, while another program used about six CPU cores
throughout the candidate run. Those original results are retained:

| Role | Median ms, before → after | P95 ms, before → after | Maximum ms, before → after |
| --- | --- | --- | --- |
| Host | 6.746 → 9.712 | 13.317 → 24.842 | 153.656 → 184.545 |
| Guest | 8.897 → 12.176 | 15.701 → 25.091 | 261.861 → 223.352 |

A reverse-order repeat (candidate, then baseline) had no sustained heavy
background process during either sample. Both used the identical frame-only
probe and isolated profile, with separate 60-second host and guest samples:

| Role | Median ms, before → after | P95 ms, before → after | Maximum ms, before → after | Intervals ≥100 ms, before → after |
| --- | --- | --- | --- | --- |
| Host | 6.836 → 6.855 | 15.681 → 18.224 | 141.760 → 170.880 | 12 → 9 |
| Guest | 8.770 → 8.910 | 15.881 → 17.798 | 260.689 → 188.530 | 34 → 25 |

Host intervals of at least 50 ms fell from 21 to 10; guest intervals rose from
51 to 55. Guest intervals of at least 200 ms fell from four to zero. Collection
counts were 9 → 9 on the host and 26 → 25 on the guest. The largest recorded
interval without a collection fell from 114.413 to 52.314 ms on the host and
158.650 to 98.091 ms on the guest. Collection co-occurrence does not measure the
collection's own duration. The change is retained for reducing clustered long
pauses, with a measured tradeoff of higher P95 intervals. Median differences
were small; this is not evidence of a general FPS gain or elimination of
stutters. A scan remains indivisible and the budget cannot cap its duration.

All recordings retained player cameras, 39 decoded factories with zero failures
per role, 24 guest input bindings, and the existing ten dormant-engine plus four
wheel-health waits. The frame-only method/timeline CSVs are empty, and event
collection counts reconcile with each sample total. Only Core/Net differed
between compared payloads. Results, load logs and scoped source changes are
under `build/scan-pause-audit/`. Both game instances are closed, temporary probes
removed, and the checked Core/Net pair is staged in Local 2P Test. All 14 checked
personal-save files and four normal installed payload files remain unchanged.

## September 2026 duplicate native name reads

`ScenePath.SegmentFor` already reads the target's name before enumerating
siblings to determine its duplicate-name ordinal. It now reads the target's
live native sibling index, counts that slot directly, and reads names only from
the other siblings. This avoids the duplicate native name getter without
adding Unity object comparisons for every sibling. Every invocation still
reads the current hierarchy. The original path renderer, per-scan index, guest
protection timing and wire format remain unchanged; the shared Net binary is
byte-for-byte identical to the baseline.

Two preliminary approaches are archived under `build/path-buffer-audit/`.
`buffer-candidate/` holds a rejected reusable-text-buffer implementation: about
1.6% lower observed guest path-call heap growth did not justify buffer ownership
and reentrancy code. Its helper and eight tests were removed.
`identity-candidate/` skipped the duplicate getter using Unity wrapper equality,
but added that comparison for every sibling. Its typical guest frame interval
rose 8.788 → 9.030 ms; the native-index refinement avoids that extra loop cost.
Neither earlier implementation remains in production.

All **4,265 Net tests** pass. Core and the updated probe build against the
installed game assemblies with zero warnings/errors. Native discovery checks
pass **66/66**, including eleven new live full-path cases covering exact text,
duplicate siblings and their reorder, literal bracket ambiguity, rename,
reparent, empty/separator/Unicode names, destroyed Unity objects and deep paths.
All **4,203 previous native regression cases** remain present and pass.

The native benchmark renders a path with eight nested mount levels 1,000 times
per sample, with 21 samples after warmup:

| Unrelated siblings per level | Median before → after (ms) | P95 before → after (ms) | Collections before → after |
| --- | --- | --- | --- |
| 4 | 6.503 → 5.584 | 10.189 → 9.205 | 9 → 8 |
| 32 | 37.415 → 35.625 | 38.371 → 36.017 | 42 → 42 |
| 256 | 299.031 → 293.990 | 302.176 → 301.730 | 337 → 337 |

Collection counts cover all 21 samples; retained heap delta is not allocated
bytes. Raw results and scoped baselines are under `build/path-buffer-audit/`.

The live heap comparison reuses `buffer-candidate/heap-before` as its baseline:
that recording used the original production payload and the exact same live
probe DLL. Only Core differs from `heap-after`; Net, catalog and diagnostic
payloads match. Both roles retain 12 world scans per minute. Observed growth per
eligible guest frame fell **630,998 → 593,628 bytes** (about **5.9%**); host growth
fell 184,738 → 179,946 bytes (about 2.6%). Guest `ScenePath.Of` mean growth fell
2,471 → 2,415 bytes, with mean instrumented time 0.010 → 0.009 ms. These are
inclusive, process-wide heap observations, excluding collection/decreasing
intervals, rather than exact allocated-byte counts. Method and frame totals
overlap. Guest collection counts were 23 before and 24 after, with more frames
rendered after; these results do not establish fewer collections per minute.

The subsequent frame-only controls ran candidate first, then baseline, using
separate 60-second samples for each role:

| Role | Median before → after (ms) | P95 before → after (ms) | Maximum before → after (ms) | Intervals ≥100 ms, before → after |
| --- | --- | --- | --- | --- |
| Host | 6.926 → 6.788 | 18.329 → 17.586 | 153.346 → 162.852 | 9 → 9 |
| Guest | 8.885 → 8.887 | 17.726 → 17.338 | 208.913 → 182.172 | 25 → 24 |

Intervals of at least 50 ms numbered 9 → 12 on the host and 57 → 55 on the
guest. Collections were 9 → 9 and 25 → 24 respectively. Typical guest timing
was effectively unchanged. The small timing differences and varying background
CPU activity do not establish a general FPS gain or elimination of stutters;
pauses around 180 ms still occurred. The refinement is retained for avoiding
redundant work with no material typical-frame regression in these controls.

All retained recordings have active player cameras, 39 decoded factories with
zero failures per role, 24 guest input bindings and the existing ten dormant
engine plus four wheel-health waits. Heap counter totals reconcile, as do frame
event collection totals; frame-only methods/timelines remain empty. The checked
Core/Net pair is staged in Local 2P Test, both games are closed and temporary
probes/markers removed. All 14 checked personal-save files and four normal
installed payload files remain unchanged.

## September 2026 unrelated drivetrain reads

The guest's native `GetFsmFloat` hook checks for drivetrain projection on every
scalar read. Previously it searched the complete protection catalog and built
scene paths even for consumers whose names have no drivetrain readers. The
hook now checks the current catalog's drivetrain consumer names first. A
remembered drivetrain consumer still takes the full validation path after a
rename or missing catalog. The filter stores no result between callbacks;
selected readers retain the existing path, action, destination, output-alias
and saved-write checks. No message layout or semantics changed (protocol 210).

The expanded opt-in heap probe separates input validation, mount checks, action
identity checks and value updates, plus guard maintenance. In the baseline,
`ValidateGuestEngineInputs` observed 5,702 bytes of mean inclusive growth per
call, while `UpdateGuestEngineInputValues` observed 185 bytes. Copying accepted
engine state was therefore left unchanged. These process-wide observations
include nested calls, exclude calls containing collections/decreasing heap,
and are not exact allocated-byte counts.

All **4,265 Net tests**, **69 native performance/readiness checks**, and
**4,207 native regression checks** pass; every prior case remains present.
Four new regression cases invoke native reads directly with a renamed FSM,
changed scene path, missing protection catalog, or missing consumer binding.
Each pauses without writing the output or saved parts and recovers after repair.
Core and the probe build against the installed game with zero warnings/errors.

The native benchmark invokes the drivetrain hook 10,000 times per sample on
unrelated consumers, with 21 samples after warmup:

| Consumer name | Median before → after (ms) | P95 before → after (ms) | Collections before → after |
| --- | --- | --- | --- |
| Data | 64.951 → 2.487 | 77.525 → 2.540 | 30 → 0 |
| Cylinders | 34.736 → 2.449 | 41.020 → 2.543 | 23 → 0 |
| Transmission at another path | 34.390 → 40.506 | 41.371 → 52.613 | 23 → 23 |

The last workload still needs the full path lookup and pays for the additional
name filter. It does not establish faster handling of every consumer. These
are synthetic hook-dispatch timings in the actual Unity/Mono runtime, not FPS.

Fresh 60-second live heap samples used the same diagnostic DLL and catalog;
only Core differed. Guest full-rule lookups fell 1,172,091 → 979,078, or about
16.2% per frame. Observed growth per eligible guest frame fell
**613,826 → 599,672 bytes (2.3%)**; host growth was 170,766 → 173,777 bytes.
Collections were guest 25 → 24 and host 10 → 10. This modest reduction does not
establish that long stutters are fixed. Both roles retained 12 world scans.
Evidence, baseline payloads and load logs are in
`build/engine-input-validation-audit/`.

Separate frame-only controls ran the candidate first, then the baseline:

| Role | Median before → after (ms) | P95 before → after (ms) | Maximum before → after (ms) | Intervals ≥100 ms, before → after |
| --- | --- | --- | --- | --- |
| Host | 6.921 → 6.979 | 18.347 → 18.376 | 159.886 → 148.048 | 10 → 10 |
| Guest | 8.869 → 8.838 | 17.475 → 17.191 | 184.812 → 187.656 | 24 → 24 |

Intervals of at least 50 ms numbered host 11 → 12 and guest 53 → 55.
Collection counts were unchanged: host 10 and guest 24. Typical timing was
essentially unchanged; pauses near 188 ms remain. The change is retained for
avoiding unnecessary work, with no general FPS or stutter-frequency gain
established. Background process-load logs accompany both controls.

Both roles retained active player cameras and 39 decoded factories with zero
failures; the guest retained ten dormant-engine and four wheel-health waits.
All heap/frame counters reconcile and frame-only method/timeline files contain
only headers. Both test games are closed, diagnostics removed, and the checked
Core/Net pair is staged in Local 2P Test. The Net binary is unchanged. All 14
checked personal-save files and four normal installed payload files retain
their original hashes.

## September 2026 world discovery name filtering

The world scan previously checked guest saved-part ancestry before determining
whether a graph's name could reach any registration branch. It now skips names
outside the fixed Use/Screw/Buy/Data/Button branches unless the current catalog
has a starter or control rule for that name. This reuses the catalog's existing
name index and follows reloads. Matching names still undergo full path, state,
and pending-part-isolation checks. Forced damage/engine protection, discovery
cadence, atomic item/NPC registration and ID recomputation remain unchanged.
The Net binary and protocol 210 are unchanged.

The native workload now measures protected-guest discovery as well as host
discovery. Each batch contains active, initialized graphs beneath nested
parents; 80% have unrelated names and 20% have Use names without matching
catalog states. Results use 21 samples after warmup:

| Graphs | Role | Median before → after (ms) | P95 before → after (ms) | Collections before → after |
| --- | --- | --- | --- | --- |
| 1,000 | Host | 4.476 → 4.523 | 9.000 → 9.316 | 2 → 2 |
| 1,000 | Guest | 6.336 → 5.507 | 11.038 → 10.861 | 3 → 2 |
| 5,000 | Host | 21.963 → 22.219 | 28.986 → 29.649 | 7 → 7 |
| 5,000 | Guest | 36.751 → 26.877 | 38.100 → 34.006 | 11 → 8 |

The large synthetic guest workload improves by about 27%. This is not a
27% gameplay or frame-rate improvement. In the live baseline, all parent-part
lookups together took only 44 ms over a minute, compared with 699 ms in twelve
world scans, so this lookup cannot explain most of the remaining pauses.
The opt-in heap probe now measures `NativePartIdentity.FindData` directly.

All **4,265 Net tests**, **89 native performance/readiness checks**, and all
**4,207 native regression checks** pass. Twenty new discovery cases exercise
host and guest custom controls/starters: unlisted names, wrong current paths,
later reparenting, stable IDs/hooks on repeated scans, and new names after a
catalog reload. Every previous check remains present. The final Core/probe
build has zero warnings/errors. Native comparison recordings use the same
probe binary; the final probe build additionally makes its missing-session
failure explicit and checks collection-free timing counters, without changing
the measured Core/Net binaries.

The first live heap pair reduced guest parent-part lookups from 22,251 to 9,450
over twelve world scans. Their inclusive cost fell from 44.264 to 23.254 ms
over the minute. However, five candidate world scans contained a collection,
versus none in the baseline. Their unfiltered scan means (58.269 → 107.170 ms)
cannot distinguish lookup work from the collector. This prompted the eligible
timing counters and a fresh comparison in reversed order, using the same
updated probe in both builds.

That second heap pair recorded:

| Metric | Before | After |
| --- | --- | --- |
| Guest parent-part lookup calls | 22,734 | 9,471 |
| Guest parent-part lookup total (ms) | 45.070 | 23.233 |
| Guest eligible world scan mean (ms; calls) | 59.010; 12 | 57.190; 12 |
| Host eligible world scan mean (ms; calls) | 25.566; 12 | 24.487; 11 |
| Guest observed heap growth per eligible frame (bytes) | 600,752.900 | 590,228.443 |
| Guest collections | 24 | 24 |

All twelve guest scans were collection-free in this pair; one host candidate
scan was excluded. The roughly 22 ms saved across the guest's twelve scans
matches the lower parent-lookup total. This supports retaining the small,
stateless name filter. Process snapshots also caught unrelated Godot work in
two candidate samples (up to 192% CPU), so treat timing magnitudes as local
observations. The two heap pairs disagree on the direction of per-frame growth;
there is no consistent allocation improvement to claim.

A separate frame-only control also ran candidate first, then baseline:

| Role | Median before → after (ms) | P95 before → after (ms) | Frames ≥100 ms before → after | Collections before → after |
| --- | --- | --- | --- | --- |
| Host | 7.853 → 6.926 | 20.930 → 18.347 | 9 → 10 | 9 → 10 |
| Guest | 9.700 → 9.109 | 19.269 → 17.742 | 28 → 23 | 21 → 23 |

The baseline's process snapshots include five samples of unrelated Godot work,
reaching 621% CPU; the candidate's contain none. These frame differences cannot
be attributed to this change. Long pauses remain, and this change does not
establish a general FPS or allocation improvement.

All six live recordings completed with active PLAYER cameras, 39 decoded
replacement factories and no factory failures. Guests retained 24 engine input
bindings, ten dormant consumers awaiting initialization, and four wheel-health
waits. Frame event collection totals reconcile with their summaries; frame-only
method/slow-call files remain empty. Both new heap calibrations validate the
eligible timing exclusions. Each before/after pair uses matching support
payloads and probe binaries, with only Core changing.

The candidate Core/Net pair is staged in Local 2P Test and the native sandbox.
Profiling DLLs/markers were removed and the test games closed. All 14 checked
personal-save files and four normal installed payload files retain their
original hashes. Evidence, final verification and scoped baselines are under
`build/world-discovery-filter-audit/`.

## September 2026 engine rule leaf filtering

`GuestEngineProtection.FindRule` now rejects impossible object-name suffixes
before constructing a complete hierarchy path. The shared
`ScenePathLookup.MayMatchLeafName` predicate is deliberately conservative:
possible matches still require the original full-path comparison, in catalog
order, and moving native mounts retain their separate identity check. Duplicate
ordinals, literal brackets/separators and live renames/reparenting remain valid.
During a discovery scan the filter defers to the existing path snapshot. No
result is retained between callbacks. Net adds a pure helper; message layouts
and semantics remain unchanged at protocol 210.

All **4,286 Net tests**, **100 native performance/readiness checks**, and
**4,207 native regression checks** pass. The 21 new unit cases cover suffix
rejection and rendered literal/duplicate names, including three cultures. Eleven new
native checks cover full-path rejection, priority, movement, duplicate order,
scan snapshots, moving mounts and catalog replacement. Every preceding native
case remains present. The final Core/probe build has zero warnings/errors.

The initial live pair encountered sharply different CPU load and frame counts,
so aggregate lookup counts did not provide a fair comparison. The new
OutsideScan counters separate routine lookups from scheduled discovery; both
builds were then measured with the same updated probe:

| Guest metric | Before | After |
| --- | --- | --- |
| Routine rule lookups per frame | 52.356 | 52.523 |
| Observed growth per eligible routine rule lookup (bytes) | 2,328.918 | 1,914.994 |
| Full path constructions outside scans per frame | 134.741 | 117.117 |
| Observed growth per eligible frame (bytes) | 630,914.910 | 626,261.986 |
| Collections during the minute | 22 | 20 |

Routine lookup growth fell about 18%, but the whole-frame difference is only
0.7%; the host's whole-frame growth rose about 2.9%. These inclusive,
process-wide counter differences are not exact allocation totals. Background
load also varied during native and frame-only comparisons: even unchanged
full-path benchmark workloads became slower. No general FPS improvement or
stutter reduction is established. The small filter is retained for reducing
routine lookup work; expensive validation of matching consumers remains.

All six live runs retained active PLAYER cameras, 39 decoded factories with no
failures, 24 guest input bindings, ten dormant engine consumers and four wheel
health waits. Counter/subset totals reconcile. The tested Core/Net pair is in
Local 2P Test and the native sandbox; temporary profilers/markers were removed
and test games closed. All 14 checked personal-save files and four normal
installed payloads are unchanged. Raw comparisons, coverage checks and final
verification are under `build/engine-guard-hotpath-audit/`.

## September 2026 bounded engine reader path reuse

Engine input preparation now reuses the current path already resolved during
catalog selection for the first bound source's validation. It discards that
path after the source refresh, including when preparation throws. Creating or
rebinding a proxy also discards it because adding the PlayMaker component runs
native Awake. Later sources and later calls resolve the current path again.
All mount, action, variable, proxy and saved-part checks remain in place. Only
Core changes; Net and protocol 210 are unchanged.

The Core/probe build has zero warnings/errors. All **4,286 Net tests** and
**4,212 native regression checks** pass; all 4,207 preceding native labels are
retained. Five new checks cover same-frame object/FSM renames, reparenting,
changes after binding and changes between source refreshes, including recovery
and preservation of saved parts. Both native benchmark runs pass **103 checks**,
including the previous 100 performance/readiness checks and three bound-input
fixtures. Their probe binaries and support payloads match.

The native benchmarks show no useful overall speed change: the two Cylinders
preparation medians are 713.981 → 713.600 ms and 687.363 → 698.937 ms per 1,000
calls; Valves is 129.055 → 129.025 ms. Collection counts change by at most one
over 21 samples. Process-load snapshots contain no Godot jobs in either native
run, but small timing differences still cannot establish an FPS improvement.

| Guest metric, sixty-second heap recordings | Before | After |
| --- | --- | --- |
| Full path lookups outside scans per frame | 113.854 | 112.075 |
| Observed growth per eligible input validation (bytes) | 5,676.853 | 5,418.774 |
| Observed growth per eligible frame (bytes) | 587,365.347 | 577,097.005 |
| Collections during the minute | 22 | 22 |

Validation growth falls about 4.5% and whole-frame growth about 1.7%; host
whole-frame growth rises about 2.0%. These process-wide, inclusive heap readings
are not exact allocations. Long pauses remain: heap-profiled guest maxima are
155.336 → 256.275 ms. The change is a small reduction in repeated lookup work,
not evidence that the reported stutters are fixed.

A separate frame-only pair ran candidate first, then baseline, with all method
instrumentation disabled:

| Role | Median before → after (ms) | P95 before → after (ms) | Frames ≥100 ms before → after | Collections before → after |
| --- | --- | --- | --- | --- |
| Host | 6.716 → 6.924 | 18.056 → 18.362 | 11 → 10 | 11 → 10 |
| Guest | 8.794 → 8.801 | 17.168 → 17.094 | 23 → 23 | 23 → 23 |

The guest's frame-only timing is essentially unchanged. None of the four live
recordings' process snapshots contains Godot work. All retain active PLAYER
cameras, 39 decoded factories with no failures, 24 guest input bindings, ten
dormant consumers and four wheel-health waits. Counter/subset totals reconcile;
frame-only method files remain empty. The before/after logs introduce no new
error headers, but existing native/Steam startup errors remain. Every matched
native, heap and frame pair differs only in Core.

The recordings also identify `UpdateRemoteEngineAudio` as a follow-up target:
about 1.6 seconds inclusive time over the guest minute, with calls up to 29 ms.
Its failed remote-electricity retries can invoke forced whole-engine discovery
every three seconds. Separate retry/discovery measurements are needed before
changing that path; native safety checks must still precede any electrical FSM
transition.

The tested Core/Net pair is staged in Local 2P Test and the native sandbox.
Diagnostic DLLs/markers were removed and the test games closed. All 14 checked
personal-save files and four normal installed payload files retain their
original hashes. Evidence, final verification and scoped predecessors are under
`build/engine-reader-path-audit/`.

## September 2026 remote-electricity retry bursts

The new heap diagnostics distinguish forced engine preparation from preparation
inside `ApplyRemoteElectricity`. In the baseline guest minute, three failed
electricity retries each forced a whole-engine scan in the same frame, repeating
every three seconds. These 60 preparations occupied only 20 frames. Each
eligible call showed roughly 4 MB of inclusive managed-heap growth; waiting for
host engine readiness did not make the retries cheap.

`ApplyRemoteElectricity` now uses the existing shared discovery budget for its
retry deadline. A due failed attempt waits when another routine scan already
used the frame, leaving its deadline and accepted host state intact. First
attempts remain immediate. Successful transitions clear the deadline, so the
next ON/OFF transition also remains immediate. Admitted attempts still perform
`GuestEngineProtection.Prepare(force: true)` before entering native Power.
This spreads repeated work; it does not weaken the protection gate or reduce
the number of required checks. Core alone changes; Net and protocol 210 remain
unchanged.

| Guest electricity preparation, sixty-second heap recordings | Before | After |
| --- | --- | --- |
| Forced preparations inside electricity retries | 60 | 60 |
| Frames containing those preparations | 20 | 60 |
| Largest number in one frame | 3 | 1 |

The baseline contains 28 process samples of unrelated Godot work; the candidate
contains one. Their overall frame medians and maximum pauses cannot isolate
this change's effect. The counters directly confirm that the three-scan burst
was removed without dropping checks. Each scan remains indivisible, and other
forced safety work can still occur in the same frame.

A separate frame-only pair ran the candidate first and baseline second, with
the same probe and all method hooks disabled:

| Guest frame-only metric, per minute | Before | After |
| --- | --- | --- |
| Frames ≥50 ms | 54 | 34 |
| Frames ≥100 ms | 23 | 23 |
| Median / P95 (ms) | 8.643 / 16.728 | 8.979 / 18.286 |
| Maximum (ms) | 205.438 | 171.673 |
| Collections | 23 | 23 |

The reduction of twenty ≥50 ms frames matches the burst frequency, but this is
one stationary local comparison, not proof of general gameplay performance.
Median and P95 timings worsen slightly; long collection pauses remain. Both
frame-only runs contain one Godot process sample during early startup (20 s
for the candidate, 10 s for baseline), and none in later process snapshots.

All **4,286 Net tests**, **4,218 native regression checks**, and **100 native
performance/readiness checks** pass. Every preceding native regression is
retained. Six new native checks cover immediate ON/OFF, deferred ON/OFF without
losing the deadline or cached state, future retry timers, forced protection,
failure and recovery. The synchronous native fixtures isolate the budget's
admission storage; the existing Net tests cover successive-frame progress,
startup/forced bypasses, deadline resets and frame-number wrap. Both probe/Core
builds have zero warnings/errors. All four live recordings retain active PLAYER
cameras, 39 decoded factories with no failures, 24 guest engine input bindings,
ten dormant consumers and four wheel-health waits. Method/subset counters and
collection totals reconcile. Every live before/after pair differs only in Core.

The candidate heap run logs one additional Cooling factory-not-ready error
during snapshot application. By the final readiness check all factories are
ready and Cooling is absent from the paused-engine list. This transient is
retained in the error comparison rather than counted as a clean error log.
The frame-only pair introduces no new error headers; existing native/Steam
startup errors remain.

The tested Core/Net pair is staged in Local 2P Test and the native sandbox.
Diagnostic DLLs/markers were removed and all test games closed. All 14 checked
personal-save files and four normal installed payload files retain their
original hashes. Evidence, final verification, error comparisons and scoped
predecessors are under `build/electricity-retry-audit/`.

## September 2026 paused-entry resume checks

The caller breakdown identified repeated rule/path lookups while attempting to
resume entries whose components were still disabled or whose objects were
inactive. `Reassert` now defers that resume-only validation until the current
pending entry can run. A stale entry whose state has changed still follows the
original validation and cleanup path. Once activation permits a retry, both
the current action graph and live catalog/path identity must pass before native
entry. There is no cached validation result. Write disabling, active-writer
retirement, input validation, discovery and forced protection remain intact.

The matched guest heap recordings show:

| Measurement | Before | After |
| --- | --- | --- |
| Outside-scan resume rule lookups per frame | 23.63 | 0 |
| All outside-scan path lookups per frame | 112.10 | 89.02 |
| Observed heap growth per eligible frame (bytes) | 592,877 | 522,859 |
| Collections in the sampled minute | 22 | 18 |

The observed frame heap growth falls **11.8%**; this is process-wide inclusive
growth, not an exact allocation count. The removed resume lookups previously
accounted for about 90.6 KB of observed growth per frame. Other work, collection
exclusions and the different number of sampled frames affect whole-frame totals.
Caller partitions reconcile exactly with lookup totals, including eligible,
collected and decreasing-heap calls. Both final scope observations report root.

The frame-only comparison ran the candidate first. It does **not** establish an
overall FPS improvement: the baseline contains eleven process snapshots of
unrelated Godot work using several cores, while the candidate contains none.
The heap baseline/candidate also contain four/two Godot snapshots respectively.

| Guest frame-only metric, per minute | Before | After |
| --- | --- | --- |
| Median / P95 (ms) | 9.965 / 21.050 | 8.391 / 16.903 |
| Frames ≥50 / ≥100 ms | 33 / 21 | 30 / 20 |
| Maximum (ms) | 175.723 | 180.686 |
| Collections | 20 | 20 |

Long pauses remain: every candidate frame-only collection coincides with an
interval over 100 ms. This change removes unnecessary routine work; it does not
resolve the reported stutters or replace two-PC driving/Steam acceptance tests.

All **4,286 Net tests** and **4,224 native regression checks** pass, retaining
all 4,218 previous native checks. Six new checks cover inactive/disabled retries,
identity changes and replaced actions at activation, stale-entry cleanup, and
exactly-once recovery between scans with saved wear preserved. Both Release
builds are clean. The four completed live recordings retain active PLAYER
cameras, 39 decoded factories with no failures, 24 guest input bindings, ten
dormant consumers and four wheel-health waits. Neither matched pair introduces
new error headers; existing native/Steam errors remain. An earlier launch stalled
before GAME and produced no valid sample; the complete retry is used here.

Only Core differs between each matched live pair; Net/protocol 210 and the probe
payload are unchanged. The tested candidate is staged in both isolated game
copies. Diagnostic DLLs/markers are removed, test games are closed, and all 14
checked personal-save files plus four normally installed payloads retain their
original hashes. Evidence and scoped predecessors are under
`build/path-caller-audit/`.

