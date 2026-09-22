# Corris ignition wire transaction — WIP, round 000138 / run175

V05/V06 remain Partial. This is source5 `WiringIgnitionFusebox`, not complete
vehicle assembly, the other native circuits, or a running engine.

## Production seam

The existing authenticated channel-0 `WiringInstallRequest`/receipt and
`WiringInstallLedger` remain the only authority path. `SessionManager.Messages`
resolves the actor from its handshaken peer before calling the item sync adapter.
No parked-vehicle owner is required. The native shared wiring tool must still be
registered by global `WiringTool`, have its expected Save.UniqueTag and Use FSM,
be tracked with no conflicting holder, and be within the existing 0.6m tolerance
of an endpoint reached by a recent alive guest (3m player tolerance).

`ItemWorldSync.WiringConnection` now revalidates the tool identity at request time.
On an available/connectable uninstalled revision, it captures the native
Installed/Trigger and cable/trigger/ignition activation flags, cancels incomplete
endpoint selections, then enters each audited Sound state. The second native
CLOSELOOP must produce exactly one Finish assembly. Acceptance requires both the
native Installed flag and completed mesh/trigger presentation. It no longer
jumps directly to Finish assembly.

While this native transaction is pending, ordinary publication, retry replies and
fresh-join snapshots return the pre-request wire revision. Exceptions or timeout
reset both endpoint selections and restore the checkpoint before Failed is sent.
Teardown also cancels an unfinished installation. Successful completion publishes
the existing absolute WiringState and matching Accepted receipt; it does not write
any new persistence format. Host-local native selection and guest Data suppression
remain on their existing paths.

These are bug fixes to the existing v239 transaction contract: no new message,
wire layout, source ID, sequence semantics, status or removal operation. Retry,
wrap, immutable operation identity and catalog endpoint checks are unchanged.

## Fresh portable evidence

Source-linked Core fixture (engine/session doubles, NOT native gameplay):

    dotnet test tools/WiringInstall.Tests/WiringInstall.Tests.csproj -c Release

It executes the real `OnHostWireInstall`, `ProcessWireConnection`,
`BuildWiringStates`, codec, ledger and replica against controlled native-action
success/failure doubles. Actor/token/source, stale revision, actor/tool range,
wrong/conflicting/missing tool, nonfinite tool positions, pending identity drift,
duplicate/old sequences, missing column, throw/partial/timeout rollback and
fresh-join isolation are covered. Success requires the two Sound entries, one
Finish, matching installed replica/receipt and join snapshot. No doubles implement
native graph validation, real player transform freshness, Steam transport, Unity
physics, native saved load or PlayMaker action execution.

The red behavioral baseline was 6 failures / 11 passing; the final source-linked
suite is 22/22. Existing focused wiring tests are 92/92; full Net is 5446/5446.
The final Core and probe builds use net35, DeployToGame=false and an explicit
read-only game reference path. Raw logs, TRX and exact argv/cwd/exit/source hashes
are under `autonomous/rounds/000138-work/run-175-*` outside the source tree.
Earlier fixture setup failures are retained but not counted as red behavior.

## Native blocker — no gameplay PASS

`tools/wiring_install_native.py` is a reusable bounded injected-state scenario.
It requires an assigned absolute round with contract.json and an exclusive name:

    python3 tools/wiring_install_native.py --run <assigned-round> --name <new-leaf>

It uses only the prepared disposable test-rig, copied saves/prefixes, both existing
exclusive rig locks, sandbox markers, protected hashes and environment-token
scoped cleanup. Output leaves cannot be reused. It performs real native pickup,
endpoint selection and the request/result path; it does not inject final installed
results. Native observations include Finish count, pending client, ledger receipt,
wire flags/revision and guest saved-data protection. The driver is NOT fully
verified: the first run never reached a fixture response.

Fresh rendered native attempt `run-175-native-command` exited 1. The host launcher
reported 0, but Unity produced `2026-09-19_191752/error.log`: mono.dll Access
Violation 0xc0000005 during GAME loading, before `host-1.txt`. Guest was never
launched, so there are zero native installation assertions. Raw crash directory
was copied to `run-175-crash/host-crash`. Owned processes were gone; markers,
probe and command bus removed; this attempt's protected before/after hashes match.
That fresh match does NOT clear the historical protected-input provenance blocker
recorded in round 000134. Its independent baseline reconciliation remains open.
No further native launch should assume that historical gate has been cleared.

Native action/result/rejoin, host save/cold reload, ordinary input, Steam/two-PC,
different-save and four-player soak remain NOT_TESTED/BLOCKED for this change.
The next bounded recovery must resolve boot/provenance readiness, then exercise
the native script against the current binary. Portable tests cannot substitute
for acceptance of the native two-endpoint path.

### Assigned-run readiness recovery (round 000140 / run177)

The independent fresh invocation of `protected_input_provenance.py` still exits
1/BLOCKED. The pinned baseline and retained JSON identity checks succeed, but
original-time log bytes/custody and independent attribution of the historical
same-metadata digest drift are unavailable. Current metadata is not content
equality, and this observation does not clear that gate. No new native launch or
deployment was performed in run177.

`tools/wiring_readiness_diagnostic.py` provides a reproducible read-only fallback:

    export RUN=<actual-assigned-absolute-round>
    python3 tools/protected_input_provenance.py --run "$RUN" --name <new-provenance-leaf>
    python3 tools/wiring_readiness_diagnostic.py --run "$RUN" --name <new-diagnostic-leaf> \
      --native-evidence <retained-native-leaf> --crash-evidence <retained-crash-directory> \
      --command-evidence <retained-command-leaf> --provenance "$RUN/<new-provenance-leaf>/report.json"

These commands intentionally return 1 while the prerequisite remains unresolved.
The diagnostic copies only bounded, nonredirected retained round artifacts and
binds their hashes to the fresh provenance report, source, build binaries, raw
command receipt and roles. It does not launch, deploy, create a replacement
protected baseline, or grant launch eligibility. Synthetic parser fixtures are
portable checks, not native evidence. A host-ready flag, wrapper exit0, unmatched
response or cleanup `quit` response cannot satisfy the wire-view readiness check.

The retained run175 minidump decodes to 0xc0000005 at mono.dll+0x1ec41. The current
rig DLL disassembly at that offset matches the crash instruction bytes; this is
not a symbolic stack or proof of root cause. Host logs show a normal Continue
request for GAME, followed by Wheel.Awake exceptions, but no completed GAME level
notification or command response. Steam initialization warnings and the failed
VR log open are observations, not established crash causes. Existing Core/Net
build hashes still match the failed attempt; the subsequently changed probe and
driver do not. The exact crash dump/log are preserved in the assigned round.

Run177 portable checks: WiringInstall 22/22, focused Wiring Net 92/92, full Net
5446/5446, Launcher 20/20, Python 212 tests, compiler-only net35 Core 0 warnings/
errors. The evidence wrapper now retains timeout/startup-failure receipts and
hashes for tool test/probe binaries; it only certifies direct-child timeout
cleanup, not game descendants. Native action/result/rejoin, save/cold reload and
all ordinary-input/external multiplayer gates remain unproved. Next prerequisite:
independently acquired/reviewed protected-input provenance; only after clearance,
a bounded rendered host readiness attempt with a Mono stack if it crashes again.

## Native persistence/removal boundary

Historical asset audit (`build/corris-wiring-audit/native-wiring.json` in the
read-only original checkout) identifies native Data save/load of Installed under
UniqueTag `WiringIgnitionFusebox` in carparts.txt. FireElectric can send DESTROY;
there is no identified reachable player manual-removal FSM/transaction on the
wire mesh. Do not invent a guest removal action or sidecar persistence. Those
historical extracts are not fresh runtime or persistence acceptance for run175.
