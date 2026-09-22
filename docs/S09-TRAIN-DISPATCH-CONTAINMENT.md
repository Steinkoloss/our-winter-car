# S09: TrainState receive and snapshot creation containment

This bounded infrastructure slice follows the Train FixedUpdate slice. It is not
native gameplay, full S09 acceptance or an external multiplayer gate.

## Audit and change

`SessionManager.Messages.cs:OnPacketReceived` already validates session state,
framing, sender/handshake and channel before `HandleMessage`. Its decoded-handler
catch logs the message/peer and full exception, then returns to packet processing.
A TrainState fault cannot normally escape this catch, but direct `OnTrainState`
callers were unprotected. Repeated faulty TrainState calls also had no local budget.

The more consequential gap was lazy snapshot enumeration: a failure escaping
`TrainSync.Snapshot` aborts `BuildWorldSnapshot`, `BuildResyncMessages` or
`BuildObjectStateMessages`. The outer packet catch cannot resume the aborted
iterator, so later world chunks and join fanout are starved. TrainSync has its own
internal catch; pre-try Unity checks or errors escaping Disable/Clear/logging can
still reach this boundary.

The change adds only two independent callback failure records using the existing
cumulative policy: first failure backs off 1 unscaled second, second 2 seconds,
third quarantines until world/session cleanup. Successful calls do not refund the
budget. These are eligibility gates for a subsequent *new call*, not scheduled
retries. Receive never captures/replays a message. Snapshot creation never retries
an iterator or sends a cached result. All three snapshot producers share one
`BuildTrainSnapshot` helper, including the object-id check inside its catch; each
retains its original position and yields non-train data normally after a local
train failure.

Train GAME -> initialization -> Receive ordering is preserved. Full exception,
inner cause, stack, aggregate ordinal and error category go through the unchanged
`HandleSyncError`, with `Receive.TrainSync` / `Snapshot.TrainSync` phase identities.
Local errors do not pause unrelated world callbacks. The existing global backoff
and eight-error disable/dump remain; the train receive/capture gates observe them.
Scene change and destruction clear the new local gates before native cleanup,
without refunding global history. Session release still reaches ResetSyncErrors,
including the existing pre-lazy-init path. No native Clear or restoration logic
was changed.

## Authority and limits

TrainState is a host-owned result stream, not a guest intent. Packet sender/channel
policy, actual TrainSync.Receive role/validity/freshness logic, TrainSync native
movement/ownership/restore, protocol, catalogs, all other session handlers and
persistence are unchanged. Authorized guest actions elsewhere are not denied or
rolled back. No packet retry, replay queue, duplicate application, new protocol
semantics or wire version change was introduced.

This slice fixes **creation/MoveNext escape**, not arbitrary transport failure.
The subsequent [snapshot-send slice](S09-TRAIN-SEND-CONTAINMENT.md) contains
TrainState `SendTo`/encoding failures per snapshot message; general transport and
live broadcast failures still retain their original boundaries.
Other snapshot producers, general initialization, unselected message handlers and
native cleanup fanout are not independently hardened here. Diagnostic-sink failure
can still escape HandleSyncError (the outer packet catch remains for packet paths).
A partly completed native cleanup is not made transactional by this change.

## Portable verification

Run from the cumulative source with a fresh RUN receipt leaf:

    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name train-final --timeout 180 -- dotnet test tools/TrainDispatch.Tests -c Release -p:DeployToGame=false --logger 'console;verbosity=detailed'
    python3 -B tools/v11_portable_receipt.py --run "$RUN" --name callbacks-final --timeout 180 -- dotnet test tools/WorldSyncCallbacks.Tests -c Release -p:DeployToGame=false --logger 'console;verbosity=detailed'
    python3 -B tools/s09_train_manifest.py --run "$RUN"

The TrainDispatch fixture generates exact production method bodies in obj:
OnPacketReceived, selected unmodified switch arms (TrainState/Ping/full snapshot/
resync), full snapshot and resync request handlers and cooldown, both complete lazy
world/resync iterators, OnTrainState, actual TrainSync.Receive, capture helper and
shared error policy. It does not compile the entire unrelated HandleMessage switch.
Unity, sessions, preparation, native Snapshot and other snapshot subsystems are
explicit doubles; each unselected producer yields a labelled sentinel solely to
assert ordering and starvation. These are not synthesized native game responses.
The full object-state iterator's wiring is static-tested; its shared id/capture
helper is executed, but the complete object handler is not in this fixture.

Red uses unchanged pre-fix production source and fails on truncated full/resync
fanout and an escaping direct receive preparation exception. The same test source
passes green. All assignment-baseline test files remain byte-identical; expanded
coverage is added in new files, not by weakening prior assertions. Existing
FixedUpdate/Update/LateUpdate/global fallback tests execute alongside new production
reset tests. Existing PaneScrapeBridge host/guest once-only authority tests and full
Net tests remain separate portable regression evidence, not native peer results.

RUN receipts contain exact argv/cwd, exit/status, timestamps, timeout, owned PID,
raw logs and source hashes. Final binaries are hashed; early train red/green receipts
predated registration of the new test DLL in the shared receipt tool, so they do
not contain that DLL hash. No missing historical binary hash is invented. The
manifest checks current binaries, baseline/red-green test equality, unchanged
production families, exact production diff and cleanup evidence.

NOT_TESTED: native discovery/injected-state, protected-input content equality,
ordinary input, native host/guest once-only action and matching results,
Steam/two-PC, different saves, fresh-player late join/rejoin, save/reload,
four-player soak and native teardown. No game was launched/deployed and no protected
log/save content was accessed. Historical protected-input provenance stays unresolved.
