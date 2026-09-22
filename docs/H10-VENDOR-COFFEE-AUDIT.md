# H10 CoffeeAutomatic: static audit and next production contract

## Status and repeatable evidence

This is an audit, not vendor gameplay implementation. H10 stays Partial. Native,
ordinary-input, Steam/two-PC, rejoin/save-reload gameplay and soak are NOT TESTED.
V11's unresolved safety gate is unchanged; this audit does not authorize launches,
rig setup, protected-input reads or deployment.

Inputs are the existing `catalog/dump-23268598.json` and current source/catalog.
The dump identifies GAME, Unity 5.0.0f4, toolsVersion 0.1.0, dumpedAtUtc
2026-06-13T01:58:54.5317474Z. Build 23268598 is its filename label, not a freshly
verified installed build. It contains state transitions, variable names, active
flags and rigidbody records, but **not** native action fields, global transitions,
variable values or resolved GameObject references. Even current dumper 0.3.0's
action *types* alone cannot prove amounts, referenced targets or save semantics.

From autonomous/source, using a new leaf each time:

    python3 -B tools/h10_vendor_coffee_audit.py --run /absolute/assigned/RUN --name vendor-coffee-audit
    python3 -B tools/h10_vendor_coffee_verify.py --run /absolute/assigned/RUN --name vendor-coffee-checks --phase final

RUN must be a direct absolute round directory containing an H10 audit contract.
No output is overwritten; symlink inputs/rounds and traversal are rejected.
The checker exits **2 / MISSING_EVIDENCE** for the precise static evidence gaps,
not success at production/gameplay. Exit 1 is an operational/input failure.
Its topology assertions may pass while production_ready remains false. It does
not silently promote readiness if someone adds actionTypes to this old schema.
The verification driver expects that diagnostic exit, runs portable Python tests,
compares two deterministic reports and verifies refusal to overwrite evidence.
`--baseline-name <earlier-receipt-leaf>` also compares unchanged production
sources/catalogs/binaries against an earlier receipt in the same RUN.

Each audit retains exact input paths/bytes/SHA256 before/after, raw selected FSM
and rigidbody records, bounded line-addressable source excerpts, report and UTC/
argv/exit/cleanup receipts. Verification logs are unedited combined subprocess
stdout/stderr. Temporary unit-test fixtures are not native results. No .NET build
is needed for this Python-only change; existing production binaries stay intact.

Round 000032 evidence: `../rounds/000032-work/handoff.md` relative to source;
final paths and fingerprints are enumerated there. Historical household evidence
in `docs/BUILDING.md:9981-10052` is context only and was not reused as vendor proof.

## Exact discovered machine topology

Two distinct machine roots, each with the following four FSM roles:

- `INSPECTION/LOD/CoffeeAutomatic`
- `JOBS/FACTORY/OpeningTimes/LOD1/Kitchen/CoffeeAutomatic`

All suffixes below are relative to either root. FSM IDs below are **dump FSM
identifiers**, not an allocation rule for dynamically acquired cups.

| Suffix / FSM | INSPECTION ID | FACTORY ID | Observed state edges and variables |
|---|---:|---:|---|
| `Functions/CoffeeButton` / `Buy` | 2078240640 | 2495019580 | Wait player --FINISHED--> Wait button; Wait button --USE--> Purchase; Purchase --FINISHED--> Wait player. String Notification; GameObject Pan. No local Float Price name. |
| `Functions/GetACup` / `Use` | 1272054110 | 1862789458 | Wait player -> Wait button; USE -> State 1 -> State 2, latter has no outgoing state edge. No named local variables. |
| `Functions/PanTarget` / `Data` | 2642221658 | 2118964294 | ON --FINISHED--> OFF; Bool Pouring; POUR in event list but no corresponding local transition. |
| `Functions/CupPivot/coffee cup(itemx)` / `Use` | 3701963876 | 3744522376 | See cup flow below. Float Coffee, Distance, Pos, Scale; Bool Pouring; GameObject HandDrink, Mesh, Pivot, TargetPan. |

Cup: Wait player/Wait button --POUR--> Cup full?; Cup full? --FINISHED--> Pour,
--LOOP--> Delay; Pour --LOOP--> Delay; Delay -> Wait player. Wait button --USE-->
Check drink 2; STOP -> Delay, USE -> Play anim -> State 1. Data -> Wait player
is a separate edge; no incoming local edge establishes native initialization of
Data. State 1 has no outgoing state transition: that does **not** prove destroy,
return, refill or respawn. Full edge lists, including hover-return edges, are in
the checker report. Reachability is conditional graph traversal from named
Wait player/OFF, not proof the game reaches or executes those states.

At dump time INSPECTION button/GetACup are active and waiting, PanTarget active
at OFF; FACTORY equivalents are inactive/unstarted. Both cups are inactive with
Use at Wait player. Each has a non-kinematic rigidbody of approximately 0.8 mass;
dump body IDs are 3640594143 and 65542243 respectively. These flags do not classify
FACTORY as dormant, excluded or inaccessible gameplay; opening-time/LOD dependency
remains an implementation prerequisite, especially with the host elsewhere.

### Unresolved cross-object and price links

Pan and TargetPan names suggest a controller relation, not its actual resolved
reference. GetACup has no variable names explaining whether it activates, clones,
reparents or resets the cup. CupPivot transform, collision geometry, hand pickup,
fill threshold/rate and native item identity must be extracted, not guessed.

Neither CoffeeAutomatic Buy has a local Price float or Check money state. It is
**unknown** whether Purchase charges a constant, reads a global, or is free.
Notification is only a variable name; no visible label/value or price was dumped.
Do not copy a rally/store price, debit a guessed amount or infer zero from absence.

## Other discovered coffee families (not deleted from H10)

- `RACES/HutRally/SausageHutFunctions/LOD/Open/Functions/Coffee::Buy` has Price,
  Notification, Functions and Pan; Wait button --USE--> Check money, FINISHED ->
  Purchase, STOP -> State 3 -> Wait player. Its sibling
  `Functions/CupPivot/coffee cup(itemx)::Use` is a separate vendor cup. Neither
  numeric price nor its object links are proved. Retain as a later vendor variant.
- `PERAPORTTI/Building/LOD100/Store/GFX/TRIGGERS_daily/Coffee::Buy` has Price and
  Carried/NumberOfProducts/Quantity plus CashRegister/Inventory/Product/ProductList/
  ThisProduct references. PURCHASE -> Check inventory -> Add -> Cashier;
  DEPURCHASE -> Check if 0 -> Subtract -> Cashier. This is a cart/checkout candidate,
  not the CoffeeAutomatic dispensing graph; actual product is not resolved.
- `Spawner/CreateItems::Coffee` has Condition, CurrentID/ObjectNumberInt, ID/SaveID,
  New/Prefab, Load ID/Add ID/Create/Create product/Exists/Save new/Save/Idle, and
  SAVEGAME/SPAWNITEM events without local edges. There is a separate inactive root
  `Coffee::Use` with ID/UniqueTag, DestroyProgress/DestroyedBottles and Bottle/Hand/
  Owner. Same spelling does **not** prove the factory produces machine cups or
  store listing products. Resolve Prefab/New/ID assignments before using it.
- Player `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Drink::Drink` has State 4
  edges DRINKCOFFEE -> Activate 5, DRINKCOFFEEPAPER -> Activate 12,
  DRINKCOFFEEGRAN -> Activate 7 and DRINKCOFFEEHOME -> HomeCoffee. CoffeeFly and
  CoffeePaperFly are separate inactive objects. Machine cup Play anim's emitter,
  personal effects and thrown-object target cannot be selected from these names.
- Household pot/cup, finite `groundcoffee0` packets and statistics coffee counter
  are recorded as related inputs only. They do not establish vendor coverage.

## Existing integration and collision boundaries

`ItemWorldSync.Coffee.cs:40-102` resolves only household paths/native
UniqueTagCoffee values. `_coffeeCup` is a singleton household binding. Input
acceptance/drink receipts in `.CoffeeInput.cs`, content snapshots in `.CoffeeState.cs`
and factory IDs in `.CoffeePackets.cs` must not be repurposed by display-name
matching. Coffee messages 245-247 mean household pot/cup/grounds; CoffeePolicy's
0.3 capacity, 0.16 transfer and caffeine bounds are not vendor constants.

There is nevertheless an **existing generic purchase route**, not total absence
of multiplayer wiring: `catalog/sync-catalog.json` buys contains unrestricted
`shopBuy`/Buy. `Catalog/ShopBuyInference.cs:15-43` infers Purchase/USE and Purchase
result for either machine button. `WorldSyncManager.cs:530-599` scans only active,
initialized/started FSMs. `FsmWorldSync.Registry.cs:116-161` installs result and
entry hooks. `.Local.cs:52-114,146-258` sends PurchaseIntent for guests and forces
host Purchase when not waiting on the input; `.Remote.cs:147-179` authenticates
actor/target/event/proximity and rejects old/duplicate purchase sequences.
This is source-path evidence, not a successful vendor purchase or once-only cup
result. Any dedicated adapter must own/exclude these exact buttons before generic
registration, including late LOD bindings, without disabling unrelated shops.

Generic item scanning (`ItemWorldSync.Scan.cs`) can register active `(itemx)` cup
bodies using path/ordinal IDs and move them. `VehicleCatalogConfig.cs:98-145`
requires Destroy/Destroy self, or **Check drink** plus verified removal actions,
for generic consumption hooks. Machine cups have **Check drink 2**, neither
destroy-state name, and no generic content/effect binding. Pose synchronization
alone is not a shared fill/drink journey. A dedicated binding must be installed
before fallback scanning and keep the same identity after reparenting/rejoin.

The household messages already route through `SessionManager.Messages.cs`,
`WorldSyncManager.Handlers.cs` and full/group/targeted `WorldSyncManager.Snapshots.cs`.
Use those integration seams, not a parallel unconnected policy-only implementation.
Shared wallet globals are owned by `WalletSync.cs` (catalog Banking.CashGlobal,
natively PlayerMoney); the machine's actual debit/no-debit operation is still unknown.

## Native persistence boundary — UNKNOWN, not no-save

None of the eight machine FSMs exposes a save-named state/event or household save
tag in this dump. That is insufficient to establish no-save: action parameters,
ES2 components, parent/global save controllers, reference chains and initial/reset
values were not dumped. Conversely, Save/SaveID on Spawner Coffee does not prove
its object is the machine cup or that contents/pose persist. Do not add a sidecar,
reuse CoffeeCupCoffee or import household save/reload results. Future evidence
must distinguish native cold initialization, host-save retention/reset and a
join snapshot of an already-running session. Guest saving remains guarded by
`Session/GuestSaveGuard.cs`; that guard is not evidence about vendor save keys.

## Bounded next contract

Immediate prerequisite: obtain **existing permitted unprotected serialized
extracts**, or a separately authorized read-only action/reference extraction, for
one INSPECTION machine plus its direct targets/player effect and external save
writers. No protected-file access or launch is authorized by this audit. Extract
ordered enabled actions with fields/default-vs-variable flags, native start/global
transitions, reference file/path IDs, cup hierarchy/physics, factory reference and
save initialization/writers. Fail closed again if unavailable; do not manufacture
payment, lifetime or persistence semantics. FACTORY's same topology does not waive
its opening-time/host-away LOD validation.

After those inputs are resolved, the smallest production journey is **one
INSPECTION CoffeeAutomatic: acquire cup, valid host OR guest Purchase, fill and
drink once, both peers converge, host-away guest action, reconnect and native
persistence/reset semantics**. Production requirements, not current achievements:

1. Add a separate `vendorCoffee` catalog section with a `machines` list. Its first
   `root` is exactly `INSPECTION/LOD/CoffeeAutomatic`; role suffix/FSM names are the
   four above. Include required state/variable schema and audited ordered action/
   reference signatures. Price mode/binding, cup lifecycle, event targets and save
   policy stay unresolved until extracted; never insert invented defaults. Keep
   FACTORY/rally/store/factory cups as explicit remaining variants.
2. Add a separate net35 per-machine/per-cup binding (proposed files
   `src/WinterMP.Core/Sync/ItemWorldSync.VendorCoffee.cs`, `.VendorCoffeeInput.cs`,
   `.VendorCoffeeState.cs`) and `Catalog/SyncCatalogJson.VendorCoffee.cs`, wired
   into existing catalog data/load, item scan/update/teardown, world handlers and
   full/group/targeted snapshots. Do not expand the household singleton. Claim
   exact machine FSM ownership in WorldSyncManager scanning before generic buys;
   restore all suppressed guest-original actions/objects on teardown.
3. Both local host input and authenticated guest intent enter the **same** host
   validator. Guest input is suppressed before native debit/spawn/fill/consume/
   personal effects, not blanket denied or merely rolled back after effects.
   Validate fresh living actor pose, exact machine/cup identity, availability,
   lifecycle/holder/geometry and native money conditions. Execute valid intent
   once on the host; reject invalid/spoofed/duplicate/out-of-order requests without
   money/cup/personal effects. Scope replay history to authenticated connection;
   failed/delayed requests must not become fresh purchases after reconnect.
4. Preserve native pricing: if charged, debit exactly the audited native amount
   once (not both manual and replay debit); if free, prove and preserve that.
   Serialize conflicting cup acquisitions/purchases/fills/drinks. Bind a fixed
   cup to its stable machine anchor if native reuses it; if native creates outputs,
   capture exact host output with distinct non-reused session/native identity.
   Choose that branch only after reference evidence, not from the Coffee factory name.
5. Broadcast revisioned absolute availability/contents/lifecycle/pose and a
   once-only actor-correlated drink receipt. Only the accepted drinker executes the
   audited native personal effect. Result replay/late binding/rejoin must neither
   pay/spawn/drink again nor resurrect a retired generation. Include guest pending
   timeout/disconnect, stale-state rejection and host-away LOD discovery.
6. Proposed portable files: `src/WinterMP.Net/Sync/VendorCoffeePolicy.cs`,
   `Messages/VendorCoffeeMessages.cs`, `WinterMP.Net.Tests/VendorCoffeeTests.cs`.
   Allocate wire IDs through the existing Protocol/MessageRegistry machinery and
   bump ProtocolInfo.Version (currently 259); update `protocol/PROTOCOL.md` and
   SessionMessagePolicy. Do not reinterpret household 245-247 or choose IDs from
   historical "next free" paragraphs. This audit changes no wire/catalog semantics.
7. Add failing portable tests for authenticated accepted guest/host equivalence,
   debit/output once, invalid/duplicate/no-effect cases, conflicting cup ownership,
   revision/rejoin/retirement and fail-closed schema drift. Later, when independently
   authorized, test the actual native input/intent/host/result path in the isolated
   rig; record host-away guest success, host success, peer agreement, reconnect and
   host-save/cold-reset outcome. Injected native fixtures, ordinary input and
   Steam/two-PC remain distinct gates. H10 is not complete until other variants
   and cross-cutting requirements are accounted for.
