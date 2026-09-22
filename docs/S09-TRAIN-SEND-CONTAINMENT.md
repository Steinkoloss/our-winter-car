# S09: TrainState snapshot transport-send containment

This is a bounded portable infrastructure slice, not full S09 or native gameplay
acceptance. It follows the [receive/capture slice](S09-TRAIN-DISPATCH-CONTAINMENT.md).

## Audited path and behavior

`HandleSnapshotRequest`, `HandleResyncRequest` and the sibling
`HandleObjectStateRequest` lazily enumerate world state, then call `SendTo`.
`BuildTrainSnapshot` contains capture failures, but cannot contain exceptions from
subsequent encoding or transport. The outer `OnPacketReceived` catch logged a
TrainState send failure and abandoned the rest of the current request. That could
starve later world chunks, time/wallet, guest spawn/outfit and forced broadcasts.

Only these three snapshot iteration call sites now use `TrySendSnapshotMessage`.
The helper attempts one original `SendTo`, returns false after logging a TrainState
failure, and lets the caller advance the existing iterator. Summary message counts
exclude that failed attempt. A later authenticated peer's independent request is
not poisoned. A newly admitted request still captures current state; it is not a
retry of the earlier failed packet.

There is no stored failure state, retry, replay, queue, cached result, peer removal,
new cooldown or world-wide disable. A transport can deliver and then throw; the
helper deliberately makes no second attempt. Exceptions for other message types
are rethrown with `throw;` to the existing packet handler fallback. Exceptions from
producer enumeration, join adjuncts, forced broadcasts and the diagnostic sink
remain under their original boundaries.

The error identifies `TrainState`, recipient and actual channel and interpolates
the original exception (type, message, inner cause and stack), rather than replacing
it with a generic failure. As with the previous outer packet catch, this send error
does not charge/reset the world's global error budget or pause/quarantine healthy
callbacks. Existing global eight-error disable/dump and local callback budgets are
unchanged. Request cooldowns still bound repeated new requests; they are not retries.

`SendTo`, `PacketCodec`, the reusable writer, attempted-traffic accounting and
`ITransport` are unchanged. The writer resets on every encode, so a failed TrainState
validation cannot contaminate the next payload. A null transport retains the
original no-op behavior; the wrapper's boolean is exception status, not a delivery
acknowledgement. No new wire semantics or protocol version is introduced.

Ingress state, framing, authentication/selected-host, channels, resync flags,
request cooldown and id-hash diagnostics are unchanged. TrainState is a host-owned
result stream, not a guest intent. Connected guests still receive valid host
TrainState results. Existing authorized guest action/once-only authority tests run
separately; this change adds no host-only gameplay, guest rollback or denial rule.
Native train ownership, initialization, capture/receive and save behavior are
unchanged. World iterators and all unrelated handler statements remain byte-identical.

## Production-linked portable evidence

Run from source, with a new receipt name for each invocation and an assigned RUN:

    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name send-final --timeout 180 -- dotnet test tools/TrainSend.Tests -c Release -p:DeployToGame=false --logger 'console;verbosity=detailed'
    python3 -B tools/s09_train_send_manifest.py --run "$RUN" --out "$RUN/send-evidence"

The fixture extends the existing generator; the bounded baseline scenario repair
below preserves every unrelated baseline test and every original fallback assertion.
It compiles exact production `OnPacketReceived`, selected switch arms, all three
request handlers, cooldown, `SendTo`, helper, complete full/resync world iterators,
Train receive/capture boundary and actual `TrainSync.Receive`, against the real Net
codec/messages/policy. Explicit doubles replace Unity/session storage, native
capture/preparation, transport and unselected snapshot producers. Other producers
yield labelled sentinels only to expose ordering/truncation. For object requests,
the handler is real but the producer is a short train-plus-sentinel double: the
complete production object iterator is static-audited, not dynamically exercised.
No double is presented as a game response.

Round 000059 remained WIP, not accepted: its unchanged TrainDispatch fallback test
threw on **every** send in a Train-first resync but expected one **total** attempt.
Train-only containment must advance to the next chunk; when that non-Train send
also throws, the original outer fallback stops the iterator. The two attempts are
for distinct messages, not a retry. Inferring a transport-wide failure from the
first exception would defeat selected-Train continuation; swallowing non-Train
exceptions would instead weaken the existing fallback.

Round 000061 resolves the fixture contradiction, not the production policy:

* `SelectedTrainSendFailureContinuesHealthyResyncWithoutRetry` fails only Train,
  asserts exact healthy non-Train payload order, one Train attempt, diagnostics,
  no outer error, and duplicate-request suppression.
* `ExistingSessionFallbackContainsTransportFailureWithoutAnyRetry` retains **all
  original assertions and its all-messages-fail callback**. Its sole fixture change
  is `world._train.OnSnapshot = () => null`, an absent Train result. Thus its first
  send is non-Train and the original one-total-attempt fallback remains meaningful.
  This is not claimed as a byte-identical method; the strict audit reconstructs the
  complete original file by removing only that line/comment and the new test.
* New real-codec `FallbackTests` cover all-messages failure with full snapshot,
  resync and object request. Full snapshot stops at its first non-Train send; the
  Train-first paths attempt Train once, then the first non-Train once, then stop.
  Nothing is delivered or retried, the original exception reaches the outer
  diagnostic, duplicate cooldown persists, and a later healthy Ping still works.

Fresh RUN-scoped `train-baseline` reproduces the original expected-1/actual-2 failure
(28 pass / 1 fail). The fresh pre-fix experiment removes only the three helper call
sites, restoring the exact prior handler; the unused helper stays unchanged. With
byte-identical behavior tests/generators, `send-red` is 11 pass / 11 fail and
`train-red` is 29 pass / 1 fail. The all-send fallback with absent Train and the
non-Train-first real-codec case pass even on red. Restoring the inherited minimal
production fix gives `send-green` 22/22 and `train-green` 30/30. No further game-code
change is required. Manifest parser tests separately reject assertion relaxation,
unrelated changes, duplicate fixture edits, missing tests and unexpected failures.

Assertions compare the complete ordered encoded payload sequence, with only the
failed TrainState removed; attempted sends retain the healthy ordering. They cover
original exception detail, accurate failed-attempt counts, later peer requests,
duplicate cooldown/no-retry, a new sequence on a later fresh request, writer reset,
non-Train fallback, malformed/unauthorized/wrong-channel/invalid-flags negatives,
connected-role admission and explicit fixture teardown/new-session isolation.
The ambiguous-delivery counter is a portable double, not native once-only evidence.
Existing TrainDispatch, WorldSyncCallbacks (Train FixedUpdate and vehicle
Update/LateUpdate/global fallback/reset), Net, Python, PaneScrape authority doubles,
Launcher and Core net35 checks are separately recorded.

RUN receipts include exact argv/cwd, timestamp, exit/timeout, owned process identity,
raw output and before/after source/binary hashes. The manifest validates the narrowly
authorized fixture delta, all unrelated baseline and red/green test identity, exact
production diff, unchanged gameplay families, complete unfiltered gate commands,
final receipts and owned portable process cleanup. No game or protected input is
opened; reading installed managed references for Core compilation is read-only.

## Remaining boundaries / evidence limits

* `Broadcast` still encodes once before its peer loop and can abandon later peers
  on transport failure. The live `TrainSync.Update -> SendWorldMessage -> Broadcast`
  path is outside this request slice and can reach TrainSync.Disable/Clear.
* General `SendTo` callers, non-Train snapshot producers/sends, handshake adjuncts
  and forced broadcasts retain their existing failure behavior.
* The subsequent [Shutdown cleanup slice](S09-SHUTDOWN-CLEANUP.md) puts disposal,
  reset and Idle in `finally`, so disconnect-send failures no longer skip them
  when cleanup succeeds. Fresh round 000161 receipts revalidate that inherited fix
  against the current runtime fields. Exceptions in Dispose/Reset themselves and
  native WorldSync cleanup fanout are still not transactional. Diagnostic-sink
  exceptions can still escape the local helper into the packet catch; these remain
  separately bounded follow-up candidates.
* The fixture cleanup is explicit double disposal. Existing production callback
  reset tests and static session-reset audit do not prove native session teardown.

NOT_TESTED: native discovery/injected-state, protected-input equality/provenance,
ordinary input, native host/guest actions and authority-once/matching peer results,
Steam/two-PC, different saves, fresh-player late join/rejoin, save/reload,
four-player soak and native teardown. No native launch/deployment occurred. Historical
protected-input provenance remains unresolved; no missing equality is called a pass.
