# Bounded guest clothing persistence (protocol 264, candidate)

Scope: S06 portable profile/restore/relay only. This is not native dressing or
insulation restoration. The host's historical CSV sidecar gains an optional
complete clothing triple, retaining older rows and absent values. Stage/type
use their existing wire byte range; winter garment permits only 0/1/2. Integer
parsing rejects fractions, nonfinite text, overflow and partial triples.

Production path:

1. `SessionManager.Handlers` generates a new clothing admission token for each
   authenticated guest and distributes it in handshake and player introductions.
2. `ClothingSync.Update` waits for GAME, both native ints and guest spawn readiness.
   Valid owner state travels over `PlayerClothingState` with token and sequence.
3. `SessionManager.Clothing` verifies owner membership, token, monotonic sequence
   and garment range before storing accepted state, writing the host sidecar or
   relaying to peers. No native guest FSM/save is written. Session-owned accepted
   values remain available for avatar reads and join snapshots after scene rebind.
4. On reconnect the host's SteamID lookup populates the optional `GuestSpawn`
   snapshot with the new recipient identity/token. `PlayerSyncManager.Clothing`
   accepts only the matching connected local guest, once through GuestResumePolicy.
5. `GuestClothingResume` keeps a mod-owned restore overlay. The loaded personal
   native tuple is a baseline, not a new dressing action; it cannot immediately
   replace the restored report. A subsequent owner-native tuple change ends the
   overlay. The next ordinary clothing report follows the same host/peer path.
   Restore does not change the native warmth tier, garment state, visuals or ES2.

Evidence lives in `autonomous/rounds/000074-work/` outside the source copy. The
initial profile regression executed against preimplementation production code:
5 failures (roundtrip loss and empty needs padding) and 11 passes. Admission
regressions were authored before their new API and initially failed compilation;
that run is NOT a behavioral result. Later passing bridge tests compile actual
Core ClothingSync, GuestProfileStore, SessionManager.Clothing,
PlayerSyncManager.Clothing and RemotePlayer into Net.Tests. Unity/PlayMaker,
transport sink, world dispatch wrapper and UI/relocation completion are explicit
boundary doubles (`ClothingBridgeDoubles.cs`), not a simulated game acceptance.

The linked journey exercises an owner report, host file write, cold store reload,
new admission, stale offer rejection, once-only local restore, unchanged native
int values, report/host/peer equality, late-join snapshot and a later local tuple
change. Negatives cover wrong sender/player, no admission, stale token/sequence,
duplicates, invalid garment/native values and host-only persistence. Wire tests
pin old prefix offsets plus exact appended lengths; existing warmth offsets
remain asserted. Fixtures are under ignored test output and delete in Dispose.

Remaining gates: native action/input, native save/initialization semantics,
ordinary input, genuine different-save, fresh-player live join, Steam/two-PC and
four-player soak are NOT_TESTED. No game launch/deployment was authorized for this
round. Package metadata still naming protocol 263 lies outside this card's
allowed paths: the complete Python suite fails its manifest/preflight gate until
the orchestrator authorizes the associated package-coherence repair. Candidate
source must not be treated as an accepted/installable package meanwhile.
