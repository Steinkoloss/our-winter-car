# S09: Shutdown cleanup after disconnect-send, reset-callback and disposal failures

Bounded portable infrastructure slice; S09 remains partial. No wire/protocol,
intent authority, result, persistence or live Broadcast behavior changes.

## Production behavior

`SessionManager.Shutdown` puts its existing cleanup in `finally` around the
existing disconnect loop. Successful shutdown still sends one reliable-ordered
Disconnect per registered peer in the collection's enumeration order. If encoding
or the transport throws, enumeration stops at that peer: earlier successful sends
are not retried, and later peers are neither attempted nor claimed delivered.
The original exception propagates with its identity, stack and inner exception
intact for the existing caller's diagnostics. No send catch, retry, cache or
transport-wide failure heuristic was added.

Even on that send failure, the pending-cleanup flag is cleared, transport/dev
client are disposed, runtime registries/cooldowns/quality state are reset, and the
session transitions to Idle. Another session on the same manager can attach a
fresh transport and use the real SendTo/codec path without the stale transport.
This does not automatically resume a caller interrupted by the exception.

The round 000165 additional boundary was **PaneScrapeSync.ResetSession only**. That
callback is first in runtime reset: previously its exception replaced an in-flight
disconnect exception and skipped every later reset and the Idle transition. Now
the remaining runtime reset sequence runs in finally. A completion flag on the
unchanged send loop lets this one callback log through WinterMPPlugin.Log.LogError
without replacing a primary send exception (no ExceptionDispatchInfo/newer BCL).
Without a primary send failure, a bare throw preserves the callback exception
after the remaining resets and Idle transition. No callback or delivery retries.

Only explicit Shutdown requests Idle at the end of that remaining reset sequence.
The shared deferred-failure cleanup call uses default arguments, retains its
failure state/text and still propagates a Pane callback failure after reset.
Idle is not asserted if an uncontained later reset throws before that endpoint.

Round 000167 adds **dev-client and transport Dispose boundaries**. Both owned
references are captured and cleared before either external Dispose call. Each
call is attempted once, in the original dev-client-then-transport order, with an
independent `LogError` catch. A disposal failure is logged and swallowed, including
when there is no primary send failure: it must not prevent the remaining resource
attempt or manager reset. Unlike the Pane callback policy, a disposal-only fault
does not propagate. With a primary send failure, the unchanged Shutdown finally
retains its original exception identity/stack/inner cause. No Dispose or send retry
is added. The tests inject exactly one failing disposer per case, never a Pane
fault at the same time. The inherited Pane tests still exercise their own boundary.

The same disposer is used by unchanged deferred-failure cleanup. After one Dispose
failure it still clears runtime once, preserves the precise Failed text and
recovery timestamp when no launch is pending, restores the guest browser flag and
performs the existing pending-launch Idle/status transition when applicable.
Repeated deferred flush with pending=false does nothing. Host, connected single-host
guest and empty Connecting configurations are portable fixture state, not admission
or rejoin evidence.

This is **manager-owned state containment**, not transactional world restoration.
The real Pane reset clears its authority/lease/admission first but its native
pickup/binding restoration may fail partway. The fixture injects at the callback
boundary; it cannot prove those native bindings are restored. Detaching a throwing
disposer cannot prove it released its internal socket, Steam handles or callback
registrations. For example, real SteamP2PTransport.Dispose unregisters callbacks
before closing peers; a failure inside it can leave its own work incomplete. The
separate Steam lobby fallback, all other reset callbacks, a second cleanup exception
(including logging), reentrant callbacks, actual resource release and OnDestroy's
later singleton clearing remain unproved. OnDestroy still skips singleton clearing
if a primary send or other uncontained exception propagates. The selected guarantee
assumes these other cleanup operations succeed; no unconditional Idle is added
around an uncontained failure.

## Executable evidence and dependencies

`tools/TrainSend.Tests/shutdown_fixture.py` extracts the complete, unmodified
production Shutdown, DisposeSessionTransport, ResetSessionRuntimeState, SetState,
AttachTransport, SendTo, FailSession, FlushFailedSessionCleanup and
PendingLaunchStatus bodies, relevant production field declarations, enums
and guest-slot type at build time. The fixture uses an isolated namespace to avoid
changing the existing TrainSend tests. Production ConnectionQuality is linked;
Net's codec, messages, JoinAttempt, PassengerSeatLedger and DeathSessionPolicy are
real. Transport, dev client, traffic meter, logger, Unity time and world-reset
hooks are portable doubles; handshake/ingress callbacks are no-ops. The retained
Steam preprocessor branch is not executed in the portable fixture.

The cases cover every failure position in a three-peer collection, both
before delivery and ambiguous post-delivery exceptions; normal multi-peer order;
zero peers with/without a transport; all reset state and exactly one reset/Idle
transition per call; no detached sends; and fresh transport sends on the same
manager with host and connected-guest role state. Fresh role setup and peer
admission are fixture state, not a handshake or native rejoin journey.

### Round 000167: fresh disposal regression and implementation

Assigned RUN: `autonomous/rounds/000167-work/` (relative to the environment root).
`shutdown-baseline` passes all 42 inherited cases. `shutdown-red` compiles the
unchanged inherited production source with 21 new cases: 42 pass / 21 fail.
Dev Dispose faults leave both references attached, skip transport Dispose and all
reset callbacks. Transport Dispose faults leave its reference attached and also
skip all resets. Runtime peer/state cleanup is skipped, and a disposal exception
replaces a primary disconnect exception. Deferred cleanup leaves pending=false
but stale runtime state. The dev-only Connecting case likewise retains its dev
reference and skips reset. Raw observations are recorded before any failing assert.

`shutdown-green` passes 63/63 with the exact same C# tests, doubles and extractor.
Twelve shutdown cases cover either disposer, host/guest and no-primary/
before-delivery/ambiguous-after-delivery send faults. Eight deferred cases cover
either disposer and host/guest/Connecting failure and pending-launch behavior.
One empty Connecting case has no transport. All inherited send/Pane/Train cases
remain green. Assertions cover primary identity/stack/inner cause, one failure
diagnostic, references detached before Dispose, every manager-owned reset field,
reset counters and Idle transition counts, delivery prefix/order, no send/disposal
retry with the failed disposer still armed, and fresh real-codec sends on a new
transport on the same manager. Deferred flush tests use actual production methods,
not a reimplementation of failure-state cleanup.

Run from autonomous/source with the current assigned RUN and fresh receipt names:

    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name shutdown-final --timeout 180 -- dotnet test tools/TrainSend.Tests -c Release -p:DeployToGame=false -p:UseSharedCompilation=false --logger 'console;verbosity=detailed'
    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name disposal-manifest --timeout 90 -- python3 -B tools/s09_shutdown_disposal_manifest.py --run "$RUN" --out "$RUN/disposal-evidence"

The manifest requires fresh baseline/red/green and final shutdown, dispatch,
callback/error-budget, Pane authority, Net, Launcher, Python and net35 Core receipts.
It verifies the production delta is confined to DisposeSessionTransport, fresh
red identity, unchanged red/green behavior tests and other game sources, complete
unskipped suites, exact raw commands/roles/log hashes, final source/binary hashes,
generated production-method identity, allowed-path scope and scoped portable
process cleanup. Artifact paths and exact commands/results are in RUN/handoff.md.
The historical manifests below are specific to their rounds and counts, not the
current disposal contract.

### Round 000165: inherited selected callback regression and implementation

Assigned RUN: `autonomous/rounds/000165-work/` (relative to the environment root).
`shutdown-baseline` passes the inherited 35 cases. `shutdown-red-final` adds seven
tests against **unchanged inherited production**: 35 pass / 7 fail, all new cases.
Four host/guest before-/after-delivery cases show transport disposed/detached but
peers retained, Hosting/Connected state, later reset counters zero, and the wrong
(cleanup) exception escaping. Three cleanup-only cases (host, guest, empty
Connecting) show retained host/runtime state. This is a fresh real boundary red,
not the historical send-finally negative control described below.

`shutdown-green` passes 42/42 with identical behavior tests/doubles/extractor.
It checks original exception identity/stack/inner cause, exactly one diagnostic
containing the cleanup exception, all existing cleanup assertions and a single
Idle transition, preserved send ordering/delivery prefix/no retry, repeated
shutdown, and real codec sends on a fresh transport on the same manager. New
guest fixtures have exactly one host in their registry; role/admission setup is
still a portable double, not a native handshake or rejoin.

Reproduce final checks using the current assigned RUN and new receipt leaves:

    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name shutdown-final-v2 --timeout 180 -- dotnet test tools/TrainSend.Tests -c Release -p:DeployToGame=false -p:UseSharedCompilation=false --logger 'console;verbosity=detailed'
    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name callback-manifest --timeout 90 -- python3 -B tools/s09_shutdown_callback_manifest.py --run "$RUN" --out "$RUN/callback-evidence" --final-suffix=-v2

The callback manifest verifies exact raw command receipts, fresh red/green test
identity, source/binary hashes, limited production delta, generated methods,
final suites and scoped portable process cleanup. Full commands/results are in
RUN/handoff.md and callback-evidence/verification.json. Use the historical
manifest below only for round 000161, whose contract and counts differ.
Final receipt leaves use `-v2`: the initial full Python suite caught a literal
no-argument signature lookup in the unrelated train-send reset wiring audit.
That lookup now accepts the updated method signature; all cooldown/send-order
assertions remain unchanged. The failed initial receipt is retained, not a pass.

### Round 000161: inherited send-finally proof (historical provenance)

Assignment RUN: `autonomous/rounds/000161-work/` (relative to the environment
root). The shutdown fix and initial tests were **already inherited**, despite the
stale train-send audit used by the task selector. No net production change was
needed. `shutdown-baseline` instead exposed a real fixture compile regression:
current reset code includes clothing admission/sequence and ResetClothingSession,
but the dependency fixture had not followed that addition. The extractor now takes
the exact field declarations from SessionManager.Clothing.cs; doubles seed and
assert their reset and count the clothing reset hook independently. No production
body or existing behavioral assertion is replaced. New guest tests exercise the
single-host registry before/after ambiguous delivery, including clothing cleanup.

`shutdown-red-behavior` freshly compiled the **historical pre-fix Shutdown body**
(verified against round 000063's retained production source) in the otherwise
current source. It is a negative control, not a claim that today's inherited code
was broken. Result: 25 pass / 10 fail out of 35, precisely the disconnect-failure
cleanup assertions. Recorded observations show dispose=0, retained transport/peers,
pending cleanup and Hosting/Connected state. Restoring the byte-identical inherited
production source gives `shutdown-green`: 35/35, with dispose=1, no transport/peers/
pending cleanup and Idle. Tests/generator are byte-identical across behavior red
and green. `shutdown-red` is a retained intermediate extraction compile error,
NOT behavior-red evidence; the declaration matcher was corrected and regression
tested before the behavioral runs.

The RUN receipts retain exact argv/cwd/timestamps, merged raw stdout/stderr, exit
codes, source/binary hashes and owned process IDs. `shutdown-evidence/verification.json`
records final unfiltered suites, source restoration, negative-control identity,
generated production bodies and scoped portable cleanup. All game code, protocol,
catalog and unrelated callback/authority fixtures remain unchanged from assignment.

Reproduce from autonomous/source, using the current assigned RUN and a new name:

    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name shutdown-check --timeout 180 -- dotnet test tools/TrainSend.Tests -c Release -p:DeployToGame=false --logger 'console;verbosity=detailed'

After the named receipts in the assigned RUN, validate them with:

    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name manifest-check-v2 --timeout 90 -- python3 -B tools/s09_shutdown_manifest.py --run "$RUN" --out "$RUN/shutdown-evidence" --final-suffix=-v2

Final receipt names use `-v2`: the first manifest attempt exposed a German CLI
success-marker mismatch in the parser. The raw test run itself passed; parser
coverage was corrected and all final checks rerun without overwriting evidence.

`tools/tests/test_s09_shutdown_fixture.py` separately checks extraction integrity
and the narrow finally wiring. Core must also compile as net35 with
DeployToGame=false and an explicit read-only MwcGamePath; portable compilation
alone is not a native runtime result.

## Limits

NOT_TESTED: native discovery/injected-state fixture, ordinary/protected input,
native host/guest action/once-only authority/matching results, Steam/two-PC,
different saves, late join/rejoin, save/reload, four-player soak and native teardown.
No native processes, rig, deployment or saves are used. A fresh portable transport
is not evidence of Steam reconnection, and a transport-double delivery is not
an observed remote player's result.
