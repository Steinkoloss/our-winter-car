# S09: bounded Train FixedUpdate coordinator containment

This is a partial infrastructure slice after [vehicle Update containment](S09-UPDATE-CONTAINMENT.md), not full S09 or native multiplayer acceptance.

## Boundary and policy

WorldSyncManager's only FixedUpdate call is `_train.FixedUpdate()`. Previously it had readiness, global-disable, cached session-active and GAME guards but no catch. TrainSync catches its own movement-body errors and calls Disable/Clear, but admission before that try and errors escaping its diagnostic/disable/restore code could bypass the coordinator budget entirely.

The actual coordinator FixedUpdate now lives in WorldSyncManager.Callbacks.cs, compiled directly into portable tests. Its dedicated CallbackFailure uses the unchanged shared policy:

- First escaping exception: retry only this callback after 1 unscaled second.
- Second: retry after 2 seconds. Repeated physics steps before the deadline neither call nor log.
- Third: quarantine this callback until world/session cleanup. Successful calls do not refund the cumulative budget.
- Retry calls the current live TrainSync method. No captured frame, intent, packet or result is replayed. The wrapper does not make a partially applied native operation atomic.
- Update (including train Update), LateUpdate and session release remain available after a local failure. There are no new role restrictions, guest rollbacks, native Clear calls, suppression restoration, per-frame delegates, timers or protocol changes.
- Errors retain `FixedUpdate.TrainSync`, the aggregate ordinal and full exception/inner cause/stack in the existing error ring and warning/error sink. Every caught exception contributes to the existing eight-error global disable/dump fallback.
- FixedUpdate now also respects the existing global one-second recovery backoff, as LateUpdate does. This intentionally prevents physics during a global prerequisite failure pause. Local train cooldown never sets that global pause. Global disable stops future gameplay frames but still permits inactive-session Update to reach release.

TrainSync.cs is unchanged: healthy host FixedUpdate remains its internal no-op; connected replicas retain freshness checks, physics, authority/sequence validation, discovery, guest suppression, Disable and native restoration. Normal errors already swallowed by TrainSync are not turned into coordinator retries. Wire format and authority/result/rejoin/persistence implementations are unchanged.

## Lifecycle and evidence limits

ResetFixedUpdateErrors clears only the train callback record. Production scene change invokes it after the same-level guard and before the lazy-init guard/native Clear calls. Destruction resets it before cleanup. Both preserve the global budget/disable and diagnostics. ResetSyncErrors (existing session release, including pre-lazy-init release) clears it alongside all prior gates and global state.

Portable tests execute production FixedUpdate/Update/UpdateWorldSync/LateUpdate/error/reset methods with explicit Unity clock, train, subsystem, session, discovery and native-release doubles. They prove:

- The exact original FixedUpdate extracted into the production partial escapes for Hosting and Connected: red has 2 behavioral failures and all 96 baseline tests pass. Identical red/green tests then pass after containment (98 total).
- Exact attempts at 10/11/13, repeated physics steps, quarantine, full exception identity, healthy original Update order and LateUpdate, unchanged session/outbox, inactive-session cleanup dispatch and next-session reset.
- Healthy admission in both roles; current-call successful/flapping retries; independent Fixed/vehicle Update/Late clocks; global cooldown/eighth-error fallback; local reset idempotence without global-budget refund; failed-release retry; inactive/null session handling.

Scene/destruction call placement and preserved later native cleanup calls are static Python evidence; reset bodies are executed, but native lifecycle bodies are not executed by these doubles. This is not proof that native Clear, suppression restoration, diagnostic sinks or partially applied train operations succeed. Existing Update/LateUpdate tests are byte-identical to the assignment baseline. The coordinator receives no intent or packet sequence; no fabricated valid/duplicate/malformed action fixture is claimed. Full Net tests retain existing protocol/policy coverage.

RUN: `/home/jaimep/our-winter-car-agent-env/autonomous/rounds/000055-work`.
Use `tools/v11_portable_receipt.py --run "$RUN" --name NAME --timeout SECONDS -- COMMAND` for exact argv/cwd/timestamps/owned PID/exit/cleanup/raw-output/source/binary receipts. `tools/s09_fixed_manifest.py --run "$RUN"` validates the red extraction, unchanged tests and unmodified gameplay families, final checks, hashes and exact change scope. Build uses DeployToGame=false and net35 game references read-only; this contract forbids native launch/deployment. No protected log/save content was accessed; protected-input content equality remains NOT_TESTED, including historical provenance.

## Precise remaining families and next step

- Source audit found only WorldSyncManager.FixedUpdate and TrainSync.FixedUpdate in Core; no further coordinator FixedUpdate sibling is claimed open. Native execution of TrainSync admission/body/Disable/Clear (including escaping engine/diagnostic faults and restoration) remains unproved. A failed diagnostic sink inside the common HandleSyncError is not newly hardened by this slice.
- The nearest remaining train message boundary is `WorldSyncManager.OnTrainState` -> EnsureSyncReady/TrainSync.Receive. Train snapshot creation in WorldSyncManager.Snapshots.cs and per-player admission fanout remain unaudited for bounded failure containment. SessionManager.OnPacketReceived validates sender/channel/framing and catches decoded-message dispatch errors, but has no cumulative per-handler budget; direct callers are not protected by that packet catch. Handler work must preserve once-only authority and accept valid guest intents, not blanket-deny them.
- Nonselected Update prerequisites (level/protection/init/bags/discovery/pending/snapshot/time/banking/wallet/checksum), item/NPC/train Update, clothing through car radio and optional dev/test work still have only the global coordinator catch. Native teardown fanout in OnDestroy/WatchLevelChanges/ReleaseEverything is not independently exception-isolated.

Next bounded candidate: audit the train OnTrainState/snapshot failure family and its validation/result ordering before selecting a production-linked regression; do not apply a blanket handler retry. No full S09 or ledger verification is inferred here.

NOT_TESTED: native discovery/injected-state fixtures; protected-input content equality; ordinary input; native host/guest action, once-only authority and matching peer results; Steam/two-PC; different saves; fresh-player late join/rejoin; save/reload; four-player soak; native teardown.
