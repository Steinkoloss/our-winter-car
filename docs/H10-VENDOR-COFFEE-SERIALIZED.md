# H10 Inspection CoffeeAutomatic serialized-field reader

Status: offline infrastructure only. No native input was supplied for round
000036. The existing toolsVersion 0.1.0 catalog dump is missing all eight native
fields. Positive documents used in tests are explicitly synthetic, not game
responses or extracted native values. H10 and V11 stay partial.

## Trust and scope

`tools/h10_vendor_coffee_serialized.py` consumes one explicitly supplied JSON
extraction; it does not open Unity assets, load UnityPy, discover installed games,
read saves/logs, prepare a rig, build, deploy or launch anything. It accepts only
absolute nonredirected inputs under `source/catalog/` or the assigned
`RUN/permitted-inputs/`. Placing a file there is not authorization to obtain it
from a protected source. Any future native exporter/input must be independently
permitted by its controller contract first.

The caller supplies independent expected input SHA256, build ID, source ID and
source-manifest SHA256. Do not obtain these pins from an untrusted incoming
extraction and call that authentication. The validator checks byte integrity,
identity consistency, schema completeness and reference/pointer resolution. It
cannot authenticate an exporter, prove an asset manifest exhaustive against
unread assets, infer what arbitrary action code does, or prove that exporter
annotations represent the correct transaction/save semantics. Those remain
independent source-review and later native-test obligations. In particular,
ACCEPTED_FIELDS is a structurally accepted extraction for review, NOT permission
to consume its fields as runtime configuration or proof of free/session-only
coffee. There is no accepted native extraction in this round.

`extract_fsm_assets.py` is an existing installed-asset extractor, not invoked here.
Its current output preserves useful action parameters but lacks this reader's
full source/build manifest, stable component identities and external-writer
coverage. Do not wrap its action-type list or the old dump in a guessed envelope.
Unsupported parameter encodings or unresolved dynamic references fail closed;
a future permitted exporter must preserve them losslessly through a reviewed
schema extension rather than omit them or replace them with defaults.

## Invocation and immutable evidence

From autonomous/source, with the actual assigned RUN and a fresh leaf:

    python3 -B tools/h10_vendor_coffee_serialized.py \
      --run /absolute/rounds/NNNNNN-work --name new-reader-receipt \
      --input /absolute/permitted/extraction.json --input-sha256 EXACT_INPUT_SHA256 \
      --build-id EXPECTED_BUILD --source-id EXPECTED_SOURCE_ID \
      --source-sha256 EXPECTED_SOURCE_MANIFEST_SHA256

RUN must contain an H10 infrastructure contract. Explicit `--run` is the caller's
assignment boundary (the worker environment in round 000036 had no TASK/RUN env
value). The invocation, exact input path, expected pins and contract/tool hashes
are preserved in the RUN receipt. Never target another worker's RUN.

Exit codes:

- 0 / ACCEPTED_FIELDS: complete structurally validated extraction, evidence class
  retained (`synthetic-fixture` or caller-identified `serialized-asset`).
- 2 / MISSING_FIELDS: only the exact known historical dump hash with build label
  `23268598` and source ID `historical-dump-23268598`. Its source SHA256 pin is the
  dump hash itself, not a nonexistent asset manifest. The filename build label is
  not independently verified as the installed build. No values are promoted.
- 1 / REJECTED: malformed/missing/ambiguous fields, unknown legacy extracts,
  source/build/hash mismatch, unsupported encodings, unsafe paths or IO errors.

Every parsed input result enumerates `price`, `references`, `actions`,
`cupLifecycle`, `playerEffect`, `persistence`, `initialization`, `geometry` with
required topics and a precise next-extraction instruction. Rejected fields are
not partially promoted. Unsafe filesystem inputs are rejected before reading;
record those stderr/exit results with the command receipt wrapper.

Outputs are a new exclusive directory containing:

- `input.json`: exact input bytes, including whitespace and ordering;
- `report.json`: deterministic UTF-8, sorted JSON keys, two-space indentation,
  newline, finite numbers only; raw ordered extraction retained on acceptance;
- `receipt.json`: input/report SHA256 and byte lengths, argv/cwd/time, source pins,
  role, exit status, tool/contract hashes and cleanup/limits. Time and argv belong
  only here, not in deterministic report data.

Existing leaves (including symlinks) are never overwritten. Linux descriptor-
based `O_NOFOLLOW` traversal rejects symlinks in every path component, `..`,
`.` and path normalization. Inputs must be regular single-link files (no
hardlinks, devices, FIFOs). Reads are size-bounded and check descriptor metadata
before/after. A report cannot be emitted against different input bytes. The
report hash is in the receipt, not a self-referential hash inside report.json.

## Schema 1 (tool-side only, not vendorCoffee catalog schema 1)

The root has exactly `schemaVersion: 1`,
`kind: "owc-inspection-coffee-serialized"`, `provenance`, `root`, `objects`, `fsms`,
`components`, `globals`, `scans`, `foundation`. `root` is exactly
`INSPECTION/LOD/CoffeeAutomatic`. No runtime enable flag exists. Extra keys are
rejected instead of silently ignoring unsupported native data.

`provenance` has buildId, sourceId, sourceSha256, extractorId, extractorVersion,
evidenceClass and assets. `assets` is an ordered list of `{fileId, sha256, bytes}`
for every source file involved, including resources/globals or external sources.
fileId is a leaf asset identifier, NOT a filesystem address or an external URL.
sourceSha256 equals SHA256 of that list encoded with the canonical JSON rules
above. Both extraction and manifest pins must match caller expectations. Every
object/FSM/component fileId must appear uniquely in this manifest. No manifest
asset is opened by the reader.

All object/FSM/component identities are unique `(fileId, pathId)` with positive
signed-64-bit pathId and exact slash-separated scene paths. References are
`{fileId, pathId, path, fsmName}`; fsmName is null for a GameObject or component.
Every non-null reference resolves to a unique included record and exact path/FSM.
Other CoffeeAutomatic variants are forbidden. Extra FSMs/components must belong
to the machine's directed reference closure, its attached controllers, or an
identified inbound external writer. Name similarity is not a reference.

- objects: identity + path, boolean activeSelf, parent reference or explicit null,
  transform, colliders, rigidbody. Parent cycles and FSM-as-parent are rejected.
  Transform has explicit `space: local`, position/scale three-vectors and rotation
  four-vector. Colliders preserve type, enabled, isTrigger and nonempty typed
  parameters. Rigidbody is explicit null or nonempty typed parameters. Null is a
  serialized absence, not an inferred absence; topic evidence cannot cite null.
- fsms: identity + path, fsmName, enabled, native startState, globalTransitions,
  variables, states. The four exact audited roles and their required states must
  exist once. Each state has name, transitions and ordered actions; each action
  has its full type name, boolean enabled and ordered parameters. Disabled actions
  remain in their original positions. Transition event names are unambiguous in
  their source state/global table; every destination and native start resolves.
- components: identity + path, componentType, enabled and nonempty typed
  parameters. Includes external non-FSM save/initialization writers; they cannot
  be dropped just because they are not PlayMaker FSMs.
- parameters/variables/globals: ordered records with exactly field, type, value,
  useVariable, useDefault, binding. Supported normalized types are Float, Int,
  Bool, String, Event, Vector3, Quaternion, GameObject, Fsm. Numeric bools,
  nonfinite values and conflicting flags are rejected. Literal/default bindings
  are null. Variable bindings explicitly specify local/global scope and name,
  resolving once with the same type. Variables/globals retain literal serialized
  defaults. Unknown types fail closed; there is no rawHex-to-no-effect fallback.
- scans: persistence and initialization coverage records, optionally price.
  Each records purpose, sourceIds, scope, complete, writers, absence, method.
  Scope must enumerate every extracted FSM/component exactly once; sourceIds
  must match the pinned manifest identity. Writers resolve inside that scope;
  empty writers require explicit absence and complete coverage. The scope and
  method are exporter evidence declarations, NOT independent proof of absence
  across unexamined assets. A real session-only/free conclusion requires reviewing
  the permitted source inventory and the actual full external reference scan.
- foundation: exactly the eight fields, each with the topic map in the reader's
  `TOPICS`. Each topic is `{origin: "serialized", pointers: [...]}`; values and
  guessed/inferred claims are forbidden. Pointers address validated typed raw
  records, not arbitrary strings/metadata. Price cites Buy, payment cites Purchase,
  amounts are numeric or cite an explicit price-absence scan. Persistence fields
  and keys cite identified writers or a matching complete absence scan. The cup
  emitter targets the unique external receiver and a resolved native event;
  assignments cite receiver actions. No DRINKCOFFEE variant is chosen by name.

Bounds: 32 MiB input, depth 64, 1,500,000 JSON nodes (to admit the historical
19-MB dump), at most 32 source assets, 128 objects/FSMs/components, 256 states per
FSM/actions per state/parameters per collection. Duplicate JSON keys, trailing
JSON, unsupported structures and nonfinite values are rejected. Ordered arrays
are never sorted; repeatability means identical bytes produce identical reports.

## Verification and unchanged next native contract

    python3 -B tools/h10_vendor_coffee_serialized_verify.py \
      --run /absolute/assigned/RUN --name new-verification-leaf

The driver records focused/full portable Python logs, two legacy diagnostics,
two explicitly synthetic CLI acceptances, refusal to overwrite, rejection of a
build mismatch and out-of-root input, exact input/report/source/catalog/tool and
existing mod-binary hashes, source immutability and owned-process/scratch cleanup.
It does not build or execute any mod binary. Synthetic input is retained under
RUN/permitted-inputs with a synthetic filename/provenance.

The next native contract in `H10-VENDOR-COFFEE-FOUNDATION.md` is unchanged:
independently permit richer extraction first; choose transaction and cup-lifetime
semantics from actual native evidence, never force the provisional portable
Acquire/Purchase/Fill/Drink sequence; route host and authorized guest through one
host validator, before irreversible local effects; preserve delayed/failed/no-
effect execution, once-only debit/output/personal effect, identity/generation,
result/rejoin and native initialization/save/reset semantics without a sidecar.
Future launches require the independently cleared safety gate and isolated rig.
Household 245–247, generic purchases, FACTORY/rally/store variants and the inert
VendorCoffeeRuntime are unchanged here. Native execution, ordinary input,
different saves, save/reload, Steam/two-PC and four-player soak are NOT_TESTED.
