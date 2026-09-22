# Unattended loop: implement one slice

You are one iteration of an unattended improvement loop on Our Winter Car. No human is
watching. A script runs gates on your work, a separate reviewer reads your diff, and the
script commits it to a `wip/` branch if both pass. Read `AGENTS.md` first and follow it;
where this file differs, this file wins for the loop.

## Pick one slice

Priority order:

1. **Human playtest notes** (below, if present). Real-play bugs outrank everything.
2. **Something broken** you can verify: a failing check, a crash path, a desync you can
   show from code.
3. **The top open item** in the work order of `docs/SYNC-SCOPE-AUDIT.md` (then
   `docs/COVERAGE-ROADMAP.md`). Prefer finishing a Partial item over starting a new one.

Look at the recent loop log and commits first. If the last several slices were all in one
area, ask whether that area is really the most valuable place, or just inertia. Say
which in your summary.

A slice is one player-observable outcome, small enough to review: the gate rejects more
than ~1500 added lines. If the natural unit is bigger, do the first coherent part.

## Stop instead of guessing

Write a one-paragraph reason to `.agent-loop/STOP` and end your turn, leaving the tree as
it is, when:

- the next useful step needs a decision only the owner can make (design fork, scope,
  anything that could corrupt a save or desync a live session);
- the remaining work can only be verified in real multiplayer and more code would just
  stack untested behavior on untested behavior;
- you find evidence that earlier loop work is wrong in a way bigger than one slice;
- nothing on the list is worth doing.

Stopping for a good reason counts as a successful iteration.

## Rules the gates enforce (don't fight them)

- Don't commit, reset, switch branches, stash, tag or push. The script owns git.
- Don't touch `tools/agent-loop/`, `AGENTS.md`, `CLAUDE.md`, `.github/`, `.gitignore`,
  `Directory.Build.props*`.
- **Protocol:** if `ProtocolInfo.Version` is already above the latest `v*` tag's version,
  do **not** bump it; extend that version's entry in `protocol/CHANGELOG.md`. Any wire
  change updates `protocol/PROTOCOL.md` or `protocol/CHANGELOG.md` in the same slice.
- **Docs state current facts.** No dated progress paragraphs, test journals or per-version
  status updates in `PLAN.md`, `BUILDING.md`, the roadmap or the catalog README. The net
  doc growth cap is ~120 lines per slice.
- `.cs` files stay ≤ 1000 lines (split with `Type.Concern.cs` partials). Never delete or
  weaken tests to pass: the test count must not drop.
- `build/` stays under ~12 GB: delete any `build/*-audit` / `*-smoke` game copy you make.
- Build and tests must pass: `WinterMP.Net` (warnings are errors), `WinterMP.Net.Tests`,
  `WinterMP.Launcher.Tests`, and `WinterMP.Core` Release (warnings are errors) when the
  game is installed. Run them yourself before finishing.

## Finish

Write `.agent-loop/summary.md` as the commit message: a subject line under 72 chars,
a blank line, then short plain facts:

- what changed and why this slice was chosen;
- how it was verified (unit tests, local two-game harness, static only);
- what is **not** verified, especially anything that needs real two-player play.

Your summary feeds the human playtest checklist, so the "not verified" part matters most.
