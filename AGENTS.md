# Our Winter Car — agent working contract

Build a working co-op mod, not an ever-growing agent framework. The owner's
current request sets the task. This file is the canonical agent entry point;
`CLAUDE.md` imports it. Historical instructions in `docs/archive/` are reference,
not a second active rulebook. Product requirements and safety constraints remain
in the source documents below.

## Start with one concrete outcome

1. Check your branch and `git status --short`. Use your own branch/worktree;
   never stage, revert, reset, or claim somebody else's work.
2. State the observable result, acceptance checks, affected subsystem, and
   non-goals in a short task/PR brief. Read the relevant source and reproduce
   the problem before changing it. Do not require a separate plan file for a
   small fix.
3. Implement one coherent vertical slice, including its real call site and
   regression test. A helper nobody calls is not a completed feature.
4. Run the checks below, review your diff, and report the result and remaining
   limitations. Missing prerequisites mean **NOT VERIFIED**, not success.

Default work comes from the top buildable item in `docs/COVERAGE-ROADMAP.md`.
Do not create a competing backlog, silently reorder priorities, or broaden a
fix into a refactor. See `CONTRIBUTING.md` for the delivery/review contract.

## Find the right owner before editing

| Concern | Source of truth / implementation |
|---|---|
| Product, authority model, architecture | `PLAN.md` — read the relevant sections |
| Next unsynced system | `docs/COVERAGE-ROADMAP.md` |
| Subsystem and file routing | `docs/CODEMAP.md` |
| Existing extension recipes | `docs/AGENT-RECIPES.md` |
| Wire contract | `protocol/PROTOCOL.md`, `src/WinterMP.Net/` |
| Protocol tests | `src/WinterMP.Net.Tests/` |
| Unity, PlayMaker, Steam and world adapters | `src/WinterMP.Core/` |
| Game diagnostics / fast boot | `src/WinterMP.Tools/`, `src/WinterMP.FastBoot/` |
| Installer UI and policies | `src/WinterMP.Launcher/`, `src/WinterMP.Launcher.Tests/` |
| Build and real-game setup | `docs/BUILDING.md`, `Directory.Build.props.user.example` |
| Catalog evidence | `catalog/`, `tools/extract_fsm_details.py` |

Read related code/tests before introducing a new abstraction. Keep
`WinterMP.Net` independent of engine and plugin projects; adapters depend on
the protocol, not the reverse. Do not move game-specific logic into the
protocol library merely to make CI accept it. Preserve the existing source
layout; update the codemap when ownership genuinely changes.

## Commands from the repository root

Use Python 3.9+ (`python3` on Linux, `py -3` on Windows) and the .NET 8 SDK.
The repository's `.slnx` is not a portable .NET 8 CLI entry point. Do not use
bare `dotnet build` or `dotnet test` at the root.

```sh
python3 tools/verify.py                         # portable build + both test projects
python3 tools/verify.py --checks-only           # static boundary checks; NOT a test run
python3 -m unittest discover -s tools/tests -v # verification-tool regressions
python3 tools/verify.py --game                  # above + compile real game plugins
```

The default runner builds **both** Net targets (`net35;netstandard2.0`) and the
launcher, then requires fresh, nonempty, passing TRX results for both test
projects. CI uses the same runner. Reports go under `.artifacts/verification/`.
For iteration, target the exact `.csproj` and relevant test filter; rerun the
portable gate before proposing a merge.

`--game` requires the game's DLLs through untracked
`Directory.Build.props.user` / `MwcGamePath`. Compilation does not prove a
working host/guest session. For sync changes, record the game build, host and
guest versions, reproduction steps, expected/observed state and relevant logs.
Use a disposable save/backup. Never fabricate DLLs, successful playtests, or
save-safety evidence to get past this boundary.

## Invariants that are not optional

- In-game projects and Net's `net35` target run on legacy Mono/.NET 3.5.
  Modern syntax does not authorize modern runtime APIs. Modern .NET APIs
  belong in the .NET 8 launcher. Keep nullable warnings meaningful.
- Host-authoritative: guests send intents; the host validates, applies and
  broadcasts. Preserve save ownership and targeted recovery on desync.
- Integrate through observed PlayMaker FSM events/variables and Harmony.
  Do not invent game methods or guess FSM paths. Catalogs are game-build data.
- Wire/semantic changes bump `ProtocolInfo.Version` in
  `src/WinterMP.Net/Protocol.cs`, update `protocol/PROTOCOL.md` in the same
  change, and add compatibility/serialization tests. Never reuse retired IDs.
  Channels remain reliable ordered (0), unreliable sequenced (1), bulk (2).
- Contain crashes within the affected subsystem, with useful logging. Do not
  suppress errors, weaken tests, or add silent fallbacks to claim success.
- Follow existing folders and concern-based partials. Split oversized files
  when the task needs it; no unrelated formatting, mass renames, speculative
  frameworks, or replacement pipelines. Delete code superseded by your change.
- Never commit game DLLs, credentials, saves, local configuration, catalog
  dumps, build output or logs. Preserve GPLv3 attribution for reused code.

## Completion and authority

Tests prove only the scope they exercised. Report **implemented**, **verified**,
and **not verified** separately, with exact commands/results and a playable
check for visible changes. Update behavior/protocol docs only when their
contract changes. Documentation churn is not gameplay progress.

Do not commit/push unless the task authorizes repository changes. Do not merge
your own PR, tag, version-bump or release unless explicitly authorized;
shipping follows `.cursor/rules/github-releases.mdc`. Ask about unresolved
save-corruption or incompatible protocol decisions, not routine code choices.
