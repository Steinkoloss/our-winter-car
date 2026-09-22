# H10 vendor coffee: disabled native adapter, portable authority foundation

Status: infrastructure candidate only. This does not promote H10 or V11.
The preceding `H10-VENDOR-COFFEE-AUDIT.md` is the static discovery source;
no repeated extraction, protected-input inspection or game launch was performed.

## Implemented boundary

- `vendorCoffee` schema 1 describes exactly one
  `INSPECTION/LOD/CoffeeAutomatic`, with buy/acquire/target/cup roles and required
  state/variable names from the existing audit. Native fields are explicitly null.
  The actual Core parser is compiled into portable tests. Absent/extra keys,
  alternate roots/roles, version drift and non-null invented native values fail
  closed for this section only. No enable flag or signature string can bypass it.
- Protocol 260 allocates registry IDs 262–264 for dedicated intent, absolute
  state and actor-correlated consumption receipt. Household 245–247, household
  constants, generic `shopBuy` catalog entries and unrelated purchases are unchanged.
- `VendorCoffeeAuthority` implements an engine-independent, serialized transaction
  boundary used identically by a host actor and an authenticated guest actor.
  The adapter must prepare without effects, validate native readiness/funds and
  commit atomically. Invalid/no-effect plans never reach commit. Successful plans
  advance one revision; action-mask and sequence guards reject both replayed
  packets and fresh-sequence repeats of an already accepted action.
- Authentication tokens are host-issued and connection-scoped; holder connection,
  epoch, generation, expected revision, fresh living actor/range facts, and exact
  identities must match. Denied valid envelopes spend sequence high-water marks.
  Disconnect or teardown reentered during preparation revokes commit. An exception
  from an uncertain adapter disables further acceptance instead of retrying it.
- A per-cup replica pins the host epoch and identities, rejects old revisions,
  generations and resurrection of a retired serving, and retains completed actions.
  A drink receipt is usable only once, by its live pending actor/connection, after
  the absolute state. Timeout/disconnect/rejoin cannot replay personal effects.
- Session authentication/delivery, World handlers, full/item-group/object snapshots
  and item teardown use the new `VendorCoffeeRuntime` port. Core constructs only
  its inert form. There is no native authority/replica installation, item spawn,
  input hook, personal effect or save writer. No unknown price/identity is minted.
- The exact INSPECTION CoffeeButton/Buy is quarantined before generic registration
  in both the scan and classifier, independent of whether the catalog loads.
  FACTORY/rally/store and near-name objects are not captured. The quarantine prevents
  this mod from replaying an unaudited generic Purchase, but does NOT suppress
  vanilla local FSM actions: this machine is unsupported multiplayer gameplay.

The portable state is a logical **serving**, not a vanilla cup lifetime. Tests use
one synthetic Acquire -> Purchase -> Fill -> Drink transaction sequence, not a
claim that the native state machine has that execution order. The foundation does
not rearm/mint a second serving, select fixed-versus-cloned cup identity, or reload
from native saves. Those decisions remain blocked by the following missing fields.
If native acquisition/purchase is combined, purchase is asynchronous, a free
purchase changes only another object, partial drinks/refills repeat in one serving,
or output retirement differs, extend the typed contract/policy after extraction
(and version the wire if semantics change), rather than forcing native behavior
into this provisional transaction model.

## Exact missing-field receipt

All paths below are under `vendorCoffee.machines[0].native` and are null in the
shipped catalog. Schema 1 intentionally cannot parse a completed native contract;
adding fields requires a reviewed typed schema and native adapter, not swapping
null for an unverified string. A topology/action-type list cannot establish these:

| Field | Required serialized evidence, currently unavailable |
|---|---|
| `price` | CoffeeButton Buy Purchase ordered enabled debit/check actions; literal vs local/global variable binding, amount/value/units, wallet target, condition/failure branches; explicit proof if free. No local Price name is not proof of zero. |
| `references` | Resolved file/path IDs and full target object/FSM paths for Buy.Pan, cup.TargetPan, GetACup action targets, CupPivot/HandDrink/Mesh/Pivot and all directly affected controllers. Resolve factory Prefab/New/ID only if an action actually creates a cup. |
| `actions` | Ordered enabled actions with every relevant serialized field, default/variable flags, start/global/local transitions and event target for Purchase, GetACup State 1/2, PanTarget ON/OFF/POUR, cup Data/Pour/Play anim/State 1. Types alone are insufficient. |
| `cupLifecycle` | Whether acquisition activates/reparents/resets a fixed cup or creates an output; exact native identity assignment and host-output capture; fill/consume limits, removal/return/reuse and generation boundary. |
| `playerEffect` | Cup Play anim emitter, resolved player Drink event/target and native value assignments/effects; animation/thrown-object handling and the before-effect suppression seam. Do not select DRINKCOFFEE or a household event from names. |
| `persistence` | Actual save writers/components, external parent/global/reference-chain writers, keys, IDs and saved/reset fields for cup/contents/pose/availability; explicit absence evidence if session-only. Spawner Coffee save fields are not proof about this cup. |
| `initialization` | Native start/data/reset/load action order, variable defaults and resolved initialization sources, including inactive/LOD activation. Distinguish cold initialization, host-save reload and live-session join snapshot. |
| `geometry` | Machine/cup/target transforms and coordinate spaces, collider and pickup/holder relations, range/placement checks and native fill geometry; not household transfer constants. |

## Bounded next native-extraction contract (not authorization to execute)

Round 000036 adds an offline, caller-pinned serialized-field validator, documented
in `H10-VENDOR-COFFEE-SERIALIZED.md`. It reports this old dump missing all eight
fields. Its accepted synthetic fixtures are schema tests, not native extraction
or permission to enable this adapter. The native contract below remains intact.

1. First resolve V11's independent protected-input provenance/launch gate, or have
   the controller explicitly authorize a permitted read-only serialized extraction.
   Reuse existing allowed unprotected extracts when available. Do not launch,
   prepare a rig, inspect protected inputs or alter controller gates under this card.
2. Extract only the INSPECTION machine's four roles plus direct referenced targets,
   player-effect receiver and external save/initialization writers needed for the
   eight fields above. Record exact source/build IDs, input/output SHA256, ordered
   enabled fields, reference resolution and failure/missing-field receipts beneath
   that worker's actual RUN. No guessed price, factory, lifetime or persistence.
3. Choose the native transaction boundary from that evidence. Prove preparation is
   read-only and commit cannot double-debit/spawn/effect; Unity FSMs are NOT presumed
   atomic. Account for delayed/failed/no-effect execution before enabling the port.
   Supply real host connection nonce, fresh living pose, geometry and native funds;
   suppress BOTH actors' local input before irreversible effects and route both
   through the same authority. Restore every altered action/object on teardown.
4. Bind native output identity, cup motion/content, serving generations and
   initialization to existing item/save semantics; add no vendor save sidecar.
   Exercise host and guest transactions, host-away guest success, result agreement,
   rejoin/retirement and native save/reset only when independently authorized.
   Rig launches then require sandbox markers, exclusive lock, copied saves,
   protected hashes and scoped bounded cleanup. Native injected-state fixtures,
   ordinary input, Steam/two-PC, different saves and soak remain separate gates.

FACTORY opening-time/LOD behavior, rally/vendor cups, store coffee/product factory,
other H10 outcomes and V11's unresolved provenance/gameplay remain in scope for
future work, not removed or classified dormant by this infrastructure change.

## Verification and limits

Round `../rounds/000034-work/` contains exact commands/exit codes, raw red/green
logs, focused assertion output, final source/catalog/protocol/binary fingerprints
and the missing-field receipt. Tests are synthetic portable fixtures, including
the fixture wallet amount, debit, output/drink counters and cup identity. Python
suite output mentioning prepared rigs/session snapshots comes from temporary unit
fixtures, NOT real native execution. Core net35 build is compile-only with
`DeployToGame=false`; only game assembly references are used, not saves/logs.

Initial red: new API absent (compiler failure). Additional behavioral red tests
proved disconnect and teardown could commit after read-only callback revocation;
both were fixed without weakening assertions. Positive and negative acceptance is
portable only. Native execution/persistence, ordinary input, Steam/two-PC,
different saves and soak are all NOT_TESTED. No H10 gameplay or V11 promotion.
