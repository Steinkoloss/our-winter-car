# S09: bounded vehicle Update callback containment

This infrastructure slice extends the earlier [LateUpdate slice](S09-LATEUPDATE-CONTAINMENT.md).
It does not complete S09 or claim native multiplayer acceptance.

## Selected production boundary

`WorldSyncManager.Update.cs` contains the actual `UpdateWorldSync` method,
extracted without changing admission, discovery, message order or cleanup.
The following ten vehicle callbacks retain their original position after train
Update and before clothing Update, in this order:

1. UpdateVehicleStates
2. UpdateStarterDraws
3. UpdateStarterWear
4. UpdateVehicleCoolant
5. UpdateDrivetrainWearStates
6. UpdateWheelHealthStates
7. UpdateVehicleDamage
8. UpdateVehicleCondition
9. UpdateFuelTransfers
10. UpdateVehicleClimate

Each has a separate `CallbackFailure` gate, using the same production policy as
LateUpdate: first escaping failure waits 1 unscaled second, second waits 2,
third quarantines only that callback until world/session cleanup. Successful
retries run normally and do not refund this cumulative failure budget. There
are no per-frame delegates, timers, registrations or captured work queues.
The next admitted Update calls the live method with the current session; it
does not replay a failed frame, requeue an intent, undo a previous callback or
clear subsystem ownership state. Existing subsystem validators, publication
sequences and pending-intent consumption remain unchanged. Native partially
applied transactions are not made atomic by this wrapper.

All errors retain full exception detail, inner cause/stack, the `error` ring
category and aggregate ordinal. The phase is `Update.VehicleWorldSync.<method>`.
A local failure no longer skips healthy vehicle or later nonvehicle Update
callbacks or pauses LateUpdate. Guest preparation/protection is deliberately
outside these local gates: a prerequisite failure must not fail open.

The existing global eight-error fallback still counts every caught error,
including mixed LateUpdate/unscoped/vehicle failures. Already-admitted Update
siblings finish the frame even if the threshold is crossed. Ten escaping
vehicle failures on one frame therefore produce ten records, with error/dump
fallback at ordinals 8, 9 and 10; the next Update/LateUpdate is disabled. This is
a finite per-frame diagnostic bound, not log suppression. Unscoped Update
exceptions still apply the original one-second global backoff.

Session release clears all local and global error state, including before lazy
initialization. Scene change and destruction clear the new local gates alongside
the old ones, without erasing the global error history. Existing native Clear,
ReleaseSession, guest suppression and hook restoration calls are unchanged.
Inactive-session Update can still reach release even under global disable;
failed release is retried at most once a second. Local cooldown/quarantine does
not delay release. A remaining global cooldown can defer it until its deadline.

## Audit and precise remaining gaps

- Update's prerequisites and nonselected families still use only the global
  catch: level changes, guest damage/engine protection, initialization, bags,
  discovery, pending FSM work, join snapshot requests, time/banking/wallet and
  checksums; item spawn/milk/item/ATF work, NPC, train, all callbacks from clothing
  through car radio, and optional door/cargo/dev-key work. Internal catches in
  individual subsystems are not a coordinator-wide guarantee.
- At this slice's baseline, FixedUpdate called only `TrainSync.FixedUpdate`
  directly without coordinator containment. The subsequent
  [Train FixedUpdate slice](S09-FIXEDUPDATE-CONTAINMENT.md) adds the bounded
  coordinator policy; native movement/Disable/Clear evidence remains open.
- `WorldSyncManager.Handlers.cs` mostly delegates directly, sometimes after
  initialization/scene checks. `SessionManager.OnPacketReceived` rejects malformed
  framing, invalid channels and unauthorized senders, then catches each
  `HandleMessage` exception. There is no per-handler bounded failure budget, and
  direct nonpacket callers are not covered by that catch. Snapshot builders and
  player-admission reset fanout also remain outside this slice.
- No new handler catch is added: swallowing a partially applied host transaction
  or replaying an intent could violate once-only authority. A follow-up must audit
  that family and its result/dedup semantics rather than blanket-denying guests.
- Native teardown exceptions/fanout isolation remain unproved. Static preservation
  of Clear calls is not proof that every native restoration succeeds.

The train FixedUpdate follow-up is documented separately above. Remaining
bounded candidates include a single nonvehicle Update family or the train
message/snapshot boundary. Full Update and handler coverage remain open work.

## Evidence and limits

Assigned RUN: `/home/jaimep/our-winter-car-agent-env/autonomous/rounds/000053-work`.
Every command uses `tools/v11_portable_receipt.py --run "$RUN" --name NAME
--timeout SECONDS -- COMMAND`. Receipts pin argv, cwd, timestamps, owned PID,
exit, timeout/cleanup, raw output and source/binary hashes.

Portable tests compile the production Update and UpdateWorldSync methods plus
LateUpdate/error handling, not a substitute scheduler. Unity time, session,
subsystems, discovery/protection operations, message sinks and native release
are explicit doubles. The red run has 22 behavioral failures (all ten methods
for Hosting and Connected, plus local-cooldown teardown for both roles) and all
36 previous tests pass. The byte-identical regression tests, doubles and project
pass green. Expanded tests cover intermittent/live retries, staggered clocks,
full exception capture, mixed/global fallback, world/session reset, inactive
states, scene/init/snapshot admission and rate-limited failed teardown. Existing
LateUpdate tests are retained unchanged. Python assertions statically check
production linkage, original call order, distinct gates and lifecycle wiring.

The selected Update callbacks accept no incoming message/sequence to validate.
No fabricated malformed/duplicate packet fixture is used here. Full Net tests
retain the existing real protocol/policy negative cases; no new end-to-end
malformed/duplicate or authorized-guest gameplay result is claimed. All vehicle,
handler, session, protocol, catalog and persistence implementation files outside
the selected coordinator stay hash-identical to the assignment baseline.

Build commands use `DeployToGame=false`; Core targets net35 with the installed
game assemblies only as read-only compiler references. No native launch,
deployment, test-rig setup, protected log/save reads, or controller/ledger writes.
Protected-input historical provenance remains unresolved, not cleared by this
portable run. Current protected-content equality is NOT_TESTED.

NOT_TESTED: native discovery/injected-state fixtures, native host/guest action,
once-only authority or result convergence, protected-input content equality,
ordinary input, Steam/two-PC, different saves, fresh-player late join/rejoin,
save/reload, four-player soak and native teardown. Portable doubles are not two
players. No ledger row is verified by this evidence alone.
