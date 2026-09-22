# H15 yard-stain contribution — protocol 263 WIP

Status: scoped implementation and portable evidence; NOT an accepted/deployable
package. The worker contract excludes Launcher/Assets and installer metadata,
which still describe protocol 262. The unchanged package-consistency tests expose
that mismatch; they must not be bypassed. H15 remains open pending that scope
recovery and review. No native gameplay acceptance is claimed.

## Native audit (static installed assets, not a save or live game)

Worker RUN: `/home/jaimep/our-winter-car-agent-env/autonomous/rounds/000070-work/`.
`03-native-static`, `11-static-roof`, and `19-static-area-list` contain exact
command receipts, installed level hash, raw extracted definitions and logs.

The original scalar broadcaster was reading the wrong source: `Scale1..5` are
only native save/load caches. SAVEGAME reads mesh local scales into those floats
before saving HomePissStain1..5. LoadFloat clamps and applies each mesh. There is
no per-frame update to Scale1..5. A packet changing those caches alone neither
changes the displayed stain nor survives the next native save getter.

The five stains are indoors (the older snow-stain description was incorrect):

| Wire area | Native reference | Room | Native maximum | Save tag |
| --- | --- | --- | --- | --- |
| 1 | Stain1s3 | MIDDLEROOM | 3 | HomePissStain1 |
| 2 | Stain2s5 | KITCHEN | 5 | HomePissStain2 |
| 3 | Stain3s7 | LIVINGROOM | 7 | HomePissStain3 |
| 4 | Stain4s4 | BEDROOM1 | 4 | HomePissStain4 |
| 5 | Stain5s4 | BEDROOM2 | 4 | HomePissStain5 |

The native PissAreas ArrayList contains those five references followed by four
NOPISS child objects (nine entries). NOPISS objects are selection exclusions,
not writable stains. Native PLAYER/Piss checks RoofCheck and closest distance
<20m before sending PISS to the yard. RoofCheck is supplied by Rain/RaycastAndFog:
world-up, distance 100, layer 27, at the player root. This is not particle-collider
contact or a requirement that the host stand in the same room.

Yard State 2 reads the local player's Closest, reads CurrentArea scale,
computes Addition=PissRate/3500, adds Addition per second to ChangeScale, clamps
and calls SetScale in LateUpdate. The producer's Full power and pumping State 4
accumulate PissRate from native EmptyRate; personal urine/need changes remain
native and local. None of this uses vehicle ownership or creates a save format.

## Implementation path

* SessionMessagePolicy admits only authenticated guest intent 269 to the host;
  SessionManager resolves the real sender actor and routes to WorldSyncManager.
* PissAreaSync owns the native references, write gate and per-session authority.
  Guests intercept only the native CurrentArea SetScale boundary before mutation,
  batching the native Addition*deltaTime into bounded intents. Producer signature
  drift disables contributions while retaining that known write gate.
* Host validates the actor/connected RemotePlayer instance, random world epoch,
  per-connection token, increasing nonzero sequence, host revision freshness,
  finite bounded contribution, live fresh pose, nearest native area/NOPISS and
  native roof query at that guest. Full power/pumping is reported native input;
  it is not claimed to be a remotely observed FSM or anti-cheat proof.
* Host changes the selected native mesh once, retains any pending host-local
  ChangeScale, and sends one absolute five-byte-scale PissAreaState with appended
  epoch/revision/admissions. Invalid requests do not consume accepted high-water.
  A failed native write rolls back; a failed result send retains the commit and
  periodic absolute state repairs delivery without replay.
* Native save/load actions are untouched. Snapshots read meshes, not stale save
  caches. Guest absolute applies update visible meshes and retain original guest
  mesh scales for restoration on Clear. WorldSyncManager's existing two clear
  paths, update and join iterator call this same production implementation.
* New connections (including reused actor slots) receive new admission tokens.
  Session reset clears epochs, challenges, request history, pending contributions
  and hooks. Absolute join state never executes a contribution.

Wire layout and all validation bounds are in `protocol/PROTOCOL.md` v263.
Binding signatures use the audited game references and fail closed for a changed
producer; adding new stain families, toilet/radiator/sauna urine interactions,
and broader urine-need replication is outside this slice.

## Evidence and limits

The original production-linked regressions failed before edits:
`05-red-live-stains` expected host byte 30 but got 0, and expected guest visible
scale 1.5 but got 1. `14-red-roof` separately proved an uncovered guest was accepted
before the native roof check was added. Neither is fabricated native evidence.

`tools/PissArea.Tests` links the actual three Core PissAreaSync source files to
explicit Unity/PlayMaker/session doubles. Tests invoke the installed native write
replacement, serialize/decode messages, run the real host handler and guest apply,
and count writes/results. They also exercise rejection, native failure, send
failure, replay/order, reconnect/reset, initialization and save-cache isolation.
Session/world routing is additionally checked statically, not claimed to execute
through a live transport. `src/WinterMP.Net.Tests/PissAreaMessageTests.cs` covers
framing, truncation, registry and transport admission. Exact receipts and source/
binary hashes are under the RUN; `handoff.md` gives the final command outcomes.

NOT_TESTED: live native action/contact and PlayMaker scheduling, ordinary input,
different-save operation, native save/reload, fresh-player live late join,
physical play, Steam/two-PC, and four-player soak. No game was launched or deployed,
no rig lock was acquired, and no native process needed cleanup. Static asset reads
and Core references use the read-only installed game; no protected save was opened
for writing. Guest/player behavior in portable doubles is not a substitute for
those native dimensions.
