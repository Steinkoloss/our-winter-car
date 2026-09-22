# V11 portable one-pane bridge hardening

Scope: only `CORRIS/BODY/Windshield/collider::Scrape`,
`CORRIS/Simulation/CarTempCorris::Freezing.CutoffWindshield`, material `6`.
The protected-input provenance gate remains **BLOCKED**. This work neither clears
that gate nor authorizes a native launch. V11 remains partial.

## Round 000049: interrupted pickup and guest authority

The immediate guest journey already worked in the portable bridge. A focused
red regression exposed a lifecycle hole: a host session ending between gated
pickup and native resumption left `PickedObject` pointing to the scraper. The
next host session had no lease, but its stale native selection denied a valid
guest pickup as `InvalidEquipment`, preventing that guest's stroke.

`PaneScrapeSync.ResetSession` now disables requests and drops session authority,
then cancels only an outstanding, still-matching native pickup before removing
the cancellation route. Hook restoration runs even if the native cancellation
callback throws. A resumed native pickup or a changed selection is not dropped;
accepted glass/material changes and personal effects are not rolled back.

The same guest hook/codec/host/replica journey runs with and without the interrupted
host pickup. It proves an accepted pickup/equip/stroke, one host glass delta,
matching peer cutoff/material, no vehicle ownership transfer and one guest-only
effect. The red run had four behavioral failures (not compile failures); the
same tests passed after the cleanup correction. RUN-scoped evidence is under
`autonomous/rounds/000049-work/`, including `red-interrupted-pickup-01` and
`green-interrupted-pickup-01`; final receipts and `handoff.md` identify the current
suite/build hashes and exact commands.

Additional production-source bridge regressions check authenticated denied-attempt
high-water and unchanged pane revision/material, corrected replay rejection,
fresh valid operation recovery, nonfinite eye/direction/pose/contact, first-hit
occlusion, parked/outside checks, pickup-before-equip for both actors, other-holder
denial, all six reentrant operation types, and fresh session state without undoing
accepted glass. Reentrant attempts neither replace the outer request nor refresh
its lease or publish an intermediate result. Existing FREEZE/roof observation,
interior frost, other-pane and save behavior is unchanged; this is not native
validation of any of those mechanics. No wire or persistence changes were needed.

## Executable verification

`tools/PaneScrapeBridge.Tests` compiles the **production** `PaneScrapeSync.cs`,
`.Bindings.cs`, `FsmHook.cs`, `PlayerMoveState.cs` and pane catalog reader against
explicit portable Unity/PlayMaker/session doubles. It exercises the installed hand
and stroke actions, host lease/authority, actual packet codec and same guest's
absolute result/effect application. It is not Unity execution or a transport test.

The positive test forwards the real outgoing pickup, keepalive, equip and stroke
requests produced by those hooks to the host bridge. No accepted response or final
cutoff is fabricated in that journey. The host's counted FloatAdd/material double
runs once; the same guest receives the absolute result, grants one actor-local
heat/effect permission, and does not run the additive glass action. Neither peer
owns the vehicle. A separate test uses the host's own hand/stroke hooks and checks
one host-local effect. Double values `.25 -> .255` and `.47` are fixture values,
**not newly observed native results**.

Negative tests cover actor/epoch/vehicle/pane/tool mismatch, obstructed/out-of-range
contact, stale actor/equipment, moving vehicle, inside actor, invalid eye geometry,
duplicate/out-of-order requests, concurrent/reentrant requests, non-holder drop,
disconnect, pre-admission effect replay, delayed pickup approval and changed native
signatures. Existing Net authority/replica tests additionally exercise nonfinite
facts, native-adapter failure, absolute revisions and in-memory rejoin/cold models.

Run from autonomous/source, with a new leaf for every invocation:

    python3 tools/v11_portable_receipt.py --run "$RUN" --name bridge-new -- dotnet test tools/PaneScrapeBridge.Tests/PaneScrapeBridge.Tests.csproj -c Release -p:DeployToGame=false --logger 'console;verbosity=normal'
    python3 tools/v11_portable_receipt.py --run "$RUN" --name net-new -- dotnet test src/WinterMP.Net.Tests -c Release -p:DeployToGame=false --logger 'console;verbosity=normal'
    python3 tools/v11_portable_receipt.py --run "$RUN" --name core-new -- dotnet build src/WinterMP.Core/WinterMP.Core.csproj -c Release -p:DeployToGame=false '-p:MwcGamePath=/home/jaimep/.steam/root/steamapps/common/My Winter Car'

RUN must be the assigned existing contract directory under autonomous/rounds.
Receipts retain exact argv/cwd/UTC/exits, bounded waited-child cleanup, raw output,
source and output DLL hashes. They do not inspect protected log contents, process
environments/arguments or personal saves. The runner is not a native launch gate.

## Corrections and compatibility

- Initialize every injected action against its already-initialized native state.
  Read-only PlayMaker decompilation confirms `Finish()` calls
  `State.FinishAction(this)`; a detached action is not a valid replacement.
- Validate every catalog binding against this audited profile; catalog values are
  unchanged. Require known sound action types, one-shot material/heat actions,
  inside-player variables/collider and the cancellation state before installing.
  Historical action names come from round 000003's retained pane description;
  they are not fresh native discovery.
- Authenticate and consume every denied equipment sequence for the exact target.
  Pickup/equip/keepalive grants require fresh outside pose and a parked Corris;
  pickup additionally needs the first host ray hit on the shared scraper. Stroke
  needs an equipped exclusive lease and the host's first pane hit within `.8 m`.
  Motion authority never creates a scraper lease or vehicle ownership grant.
- Reentrant native callbacks cannot overwrite the current request, alter its lease
  or publish an intermediate glass value. Their authenticated attempts are consumed
  without callbacks. The next absolute publication carries the lease high-water.
  A later retry of the nested sequence is denied. Core runs on the game thread;
  these are serialized/reentrant tests, not multithreaded Unity tests.
- Correlate pickup approval with the still-waiting native hand state and scraper.
  A late approval releases the lease instead of reentering a changed hand state or
  clearing another held object. A denied or stale result cannot grant personal heat.
- Suppress old actor effects until targeted admission; snapshots never grant them.
  Live accepted effects remain actor-only/exact-once. Failed authority remains
  fail-stop; no speculative additive guest action is restored while connected.
- Restore action arrays/Enabled bits and owned Hand global transitions/event-table
  entries on teardown, preserving unrelated hooks/entries installed afterward.

No wire field, ID, channel, version, catalog value or native persistence format was
changed. These are enforcement/lifecycle corrections to the existing v258 parked
pane contract, not a new protocol. SessionManager still authenticates the peer and
selected host through SessionMessagePolicy before routing. VehicleClimate still
skips only this bound windshield; other panes/interior frost are untouched.
`Capture()` observes vanilla state, including FREEZE/roof initialization. There is
no cutoff sidecar, saved scrape replay or new save hook.

## Limits and next step

All native gameplay, injected-state gameplay, ordinary input, different saves,
fresh-player late join, save/reload, Steam/two-PC and four-player soak are
**NOT_TESTED**. Portable double rejoin tests are not native rejoin acceptance.
Denials do not constitute authorized guest capability.

Independent review should rerun the portable projects and net35 build. Only after
separate independent protected-input gate clearance and the real guest fresh-pose
prerequisite may a newly assigned task run the existing copied-rig native journey.
Do not prepare, deploy, launch, rebaseline or weaken the gate to extend this result.
